using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters.Perks;
using Game.Core.Stats;

namespace Game.Core.Characters.Build
{
    /// <summary>
    /// Планувальник білда (US-2.3): рахує, що дасть план, і застосовує його
    /// тільки за явним підтвердженням.
    ///
    /// Головна властивість — до підтвердження не змінюється НІЧОГО. Тому превью
    /// рахує по копії скілів і по тимчасовому провайдеру перків, а не по
    /// бійцю: «подивитися» не повинно коштувати очка.
    ///
    /// Гейти перків не дублюються — їх рахує CompanionPerks.Evaluate, той самий
    /// код, що й при звичайному взятті. Друга копія правил розійшлася б.
    /// </summary>
    public static class BuildPlanner
    {
        /// <summary>Перки плану як джерело модифікаторів для превью.</summary>
        private sealed class PlannedPerks : IModifierProvider
        {
            private readonly List<PerkDefinition> _perks = new List<PerkDefinition>();

            public void Add(PerkDefinition perk) { if (perk != null) _perks.Add(perk); }

            public void CollectModifiers(List<StatModifier> into)
            {
                for (int i = 0; i < _perks.Count; i++)
                {
                    var mods = _perks[i].Modifiers;
                    if (mods == null) continue;
                    for (int m = 0; m < mods.Count; m++) into.Add(mods[m]);
                }
            }
        }

        public static BuildPreview Preview(Companion companion, BuildPlan plan, int pointsAvailable,
            IEnumerable<PerkDefinition> catalog, BalanceConfig cfg)
        {
            var preview = new BuildPreview { PointsAvailable = pointsAvailable };
            if (companion == null || plan == null) return preview;
            cfg = cfg ?? new BalanceConfig();

            preview.PointCost = plan.PointCost;

            // Скіли: показуємо СПРАВЖНІЙ намір гравця, навіть якщо він вищий за
            // стелю. Мовчки обрізати — означає збрехати: гравець побачив би «+3» і
            // отримав «+1», не зрозумівши, куди поділися очки.
            var after = companion.Skills.Clone();
            for (int i = 0; i < plan.Skills.Count; i++)
            {
                var skill = plan.Skills[i];
                int from = companion.Skill(skill);
                int to = from + plan.InvestedIn(skill);
                preview.Skills.Add(new SkillChange { Skill = skill, From = from, To = to });
                if (to > cfg.MaxSkillLevel) preview.Status = BuildPlanStatus.AboveSkillCeiling;
                after[skill] = StatScales.ClampSkill(to, cfg);
            }

            // Очок не вистачає — це важливіше за стелю: гравець дізнається про ціну
            // раніше, ніж про межу шкали.
            if (preview.PointCost > pointsAvailable) preview.Status = BuildPlanStatus.NotEnoughPoints;

            // Перки плану: кожен наступний бачить попередні як взяті, інакше
            // зв'язка «перк відкриває перк» усередині одного плану була б хибною
            // відмовою.
            var planned = new PlannedPerks();
            var alsoTaken = new List<string>();
            for (int i = 0; i < plan.Perks.Count; i++)
            {
                var perk = plan.Perks[i];
                var verdict = companion.Perks.Evaluate(perk, after, alsoTaken);
                preview.Perks.Add(new PerkVerdict { Perk = perk, Verdict = verdict });
                if (verdict == PerkAvailability.Available)
                {
                    alsoTaken.Add(perk.Id);
                    planned.Add(perk);
                }
                else if (preview.Status == BuildPlanStatus.Ok)
                {
                    preview.Status = BuildPlanStatus.PerkUnavailable;
                }
            }

            // Стати: до і після, за єдиним агрегатором. Провайдери ті самі, що у
            // бійця, плюс перки плану — інакше ефект перка в превью не видно.
            var before = companion.Resolve(cfg);
            var afterSnapshot = StatResolver.Resolve(companion.Attributes, after,
                new IModifierProvider[] { companion.Traits, companion.Scars, companion.Perks, planned }, cfg);
            CollectChangedStats(before, afterSnapshot, preview.Stats);

            // «Що відкриється» — тільки те, чого гравець НЕ планував: заплановане
            // він і так бачить у списку перків.
            if (catalog != null)
                foreach (var perk in catalog)
                {
                    if (perk == null || string.IsNullOrEmpty(perk.Id)) continue;
                    if (alsoTaken.Contains(perk.Id)) continue;
                    if (IsPlanned(plan, perk.Id)) continue;
                    if (companion.Perks.Has(perk.Id)) continue;

                    bool openNow = companion.Perks.Evaluate(perk, companion.Skills, null) == PerkAvailability.Available;
                    bool openAfter = companion.Perks.Evaluate(perk, after, alsoTaken) == PerkAvailability.Available;
                    if (openAfter && !openNow) preview.Unlocks.Add(perk);
                }

            return preview;
        }

        /// <summary>
        /// Застосовує план. Без підтвердження не робить НІЧОГО і говорить про це
        /// статусом: розподіл незворотний, тому «випадково натиснув» не
        /// повинно бути можливим сценарієм.
        /// </summary>
        public static BuildPlanStatus Commit(Companion companion, BuildPlan plan, int pointsAvailable,
            IEnumerable<PerkDefinition> catalog, BalanceConfig cfg, bool confirmedIrreversible)
        {
            if (!confirmedIrreversible) return BuildPlanStatus.NotConfirmed;
            if (companion == null || plan == null) return BuildPlanStatus.NotConfirmed;
            cfg = cfg ?? new BalanceConfig();

            var preview = Preview(companion, plan, pointsAvailable, catalog, cfg);
            if (!preview.CanCommit) return preview.Status;

            for (int i = 0; i < preview.Skills.Count; i++)
            {
                var change = preview.Skills[i];
                companion.Skills[change.Skill] = StatScales.ClampSkill(change.To, cfg);
            }

            // Перки беремо в порядку плану: ланцюжок усередині плану тримається на
            // тому, що пререквізит уже лежить у бійця на момент наступного перка.
            for (int i = 0; i < plan.Perks.Count; i++)
                companion.Perks.TryTake(plan.Perks[i], companion.Skills);

            plan.Clear();
            return BuildPlanStatus.Ok;
        }

        private static bool IsPlanned(BuildPlan plan, string perkId)
        {
            for (int i = 0; i < plan.Perks.Count; i++)
                if (plan.Perks[i].Id == perkId) return true;
            return false;
        }

        private static void CollectChangedStats(StatSnapshot before, StatSnapshot after, List<StatChange> into)
        {
            // Перебір за переліком, а не за знімком: знімок ключі назовні не
            // віддає, а список статів фіксований.
            foreach (StatKey key in System.Enum.GetValues(typeof(StatKey)))
            {
                if (key == StatKey.None) continue;
                double from = before.Get(key);
                double to = after.Get(key);
                if (from != to) into.Add(new StatChange { Key = key, From = from, To = to });
            }
        }
    }
}
