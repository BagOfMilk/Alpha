using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters.Perks;
using Game.Core.Stats;

namespace Game.Core.Characters.Build
{
    /// <summary>
    /// Планировщик билда (US-2.3): считает, что даст план, и применяет его
    /// только по явному подтверждению.
    ///
    /// Главное свойство — до подтверждения не меняется НИЧЕГО. Поэтому превью
    /// считает по копии скилов и по временному провайдеру перков, а не по
    /// бойцу: «посмотреть» не должно стоить очка.
    ///
    /// Гейты перков не дублируются — их считает CompanionPerks.Evaluate, тот же
    /// код, что и при обычном взятии. Вторая копия правил разъехалась бы.
    /// </summary>
    public static class BuildPlanner
    {
        /// <summary>Перки плана как источник модификаторов для превью.</summary>
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

            // Скилы: показываем НАСТОЯЩЕЕ намерение игрока, даже если оно выше
            // потолка. Молча обрезать — значит соврать: игрок увидел бы «+3» и
            // получил «+1», не поняв, куда делись очки.
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

            // Очков не хватает — это важнее потолка: игрок узнаёт про цену
            // раньше, чем про предел шкалы.
            if (preview.PointCost > pointsAvailable) preview.Status = BuildPlanStatus.NotEnoughPoints;

            // Перки плана: каждый следующий видит предыдущие как взятые, иначе
            // связка «перк открывает перк» внутри одного плана была бы ложным
            // отказом.
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

            // Статы: до и после, по единому агрегатору. Провайдеры те же, что у
            // бойца, плюс перки плана — иначе эффект перка в превью не виден.
            var before = companion.Resolve(cfg);
            var afterSnapshot = StatResolver.Resolve(companion.Attributes, after,
                new IModifierProvider[] { companion.Traits, companion.Scars, companion.Perks, planned }, cfg);
            CollectChangedStats(before, afterSnapshot, preview.Stats);

            // «Что откроется» — только то, чего игрок НЕ планировал: планируемое
            // он и так видит в списке перков.
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
        /// Применяет план. Без подтверждения не делает НИЧЕГО и говорит об этом
        /// статусом: распределение необратимо, поэтому «случайно нажал» не
        /// должно быть возможным сценарием.
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

            // Перки берём в порядке плана: цепочка внутри плана держится на том,
            // что пререквизит уже лежит у бойца к моменту следующего перка.
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
            // Перебор по перечислению, а не по снимку: снимок ключи наружу не
            // отдаёт, а список статов фиксирован.
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
