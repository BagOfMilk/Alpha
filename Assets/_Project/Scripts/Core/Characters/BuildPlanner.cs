using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Stats;

namespace Game.Core.Characters
{
    /// <summary>
    /// Превью траты одного очка скила ДО подтверждения (US-2.3): что изменится и
    /// что откроется. Вложение необратимо (респека нет) — превью обязано быть.
    /// </summary>
    public sealed class SkillPointPreview
    {
        public SkillType Skill;
        public int CurrentLevel;
        public int NewLevel;

        /// <summary>Можно ли вообще потратить (есть очки, скил валиден и не на потолке).</summary>
        public bool CanSpend;

        /// <summary>Скил уже на потолке кампании (cfg.SkillMax) — очко в него не вложить.</summary>
        public bool AtCap;

        /// <summary>Явное предупреждение (US-2.3): вложение НЕ откатывается.</summary>
        public bool Irreversible => true;

        /// <summary>Перки, которые откроются этим очком (порог + пререквизиты).</summary>
        public readonly List<PerkDefinition> PerksUnlocked = new List<PerkDefinition>();

        /// <summary>Дельты производных статов (только ненулевые) — от новых перков.</summary>
        public readonly Dictionary<DerivedStat, int> DerivedDeltas = new Dictionary<DerivedStat, int>();

        /// <summary>Дельта флэта к проверкам этого скила (само очко + чек-бонусы новых перков).</summary>
        public int CheckValueDelta;
    }

    /// <summary>
    /// Планировщик билда (US-2.3): считает эффект вложения БЕЗ мутации напарника.
    /// Показывает пороги/перки/дельты статов — игрок подтверждает осознанно.
    /// </summary>
    public static class BuildPlanner
    {
        /// <summary>Превью: что даст одно очко в скил (напарник НЕ меняется).</summary>
        public static SkillPointPreview PreviewSkillPoint(Companion c, SkillType skill,
                                                          BalanceConfig cfg,
                                                          IEnumerable<PerkDefinition> perkCatalog)
        {
            var preview = new SkillPointPreview { Skill = skill };
            if (c == null || skill == SkillType.None) return preview;

            preview.CurrentLevel = c.GetSkill(skill);
            preview.NewLevel = preview.CurrentLevel + 1;
            preview.AtCap = cfg != null && preview.CurrentLevel >= cfg.SkillMax;
            // Один источник истины с тратой (Companion.SpendSkillPoint): превью и
            // кнопка UI не должны расходиться в том, можно ли вложить очко.
            preview.CanSpend = c.UnspentSkillPoints > 0 && !preview.AtCap;
            preview.CheckValueDelta = 1; // само очко: +1 к значению проверки этого скила

            if (perkCatalog == null || cfg == null) return preview;

            // Какие перки откроются на гипотетическом уровне (пороги + пререквизит-
            // цепочки до фикс-пойнта — как в Companion.RefreshPerks, но без мутации).
            var pool = new List<PerkDefinition>();
            foreach (var p in perkCatalog)
                if (p != null) pool.Add(p);

            var unlockedIds = new HashSet<string>();
            foreach (var p in c.Perks) unlockedIds.Add(p.Id);

            // Перки, уже достижимые ТЕКУЩИМИ уровнями (даже если RefreshPerks ещё не
            // звали), — не заслуга нового очка: исключаем их из атрибуции превью.
            bool seeded = true;
            while (seeded)
            {
                seeded = false;
                for (int i = 0; i < pool.Count; i++)
                {
                    var perk = pool[i];
                    if (unlockedIds.Contains(perk.Id)) continue;
                    if (perk.Skill == SkillType.None) continue;
                    bool prereqOk = string.IsNullOrEmpty(perk.RequiresPerkId)
                                    || unlockedIds.Contains(perk.RequiresPerkId);
                    if (prereqOk && c.GetSkill(perk.Skill) >= perk.RequiredSkillLevel)
                    {
                        unlockedIds.Add(perk.Id);
                        seeded = true;
                    }
                }
            }

            bool added = true;
            while (added)
            {
                added = false;
                for (int i = 0; i < pool.Count; i++)
                {
                    var perk = pool[i];
                    if (unlockedIds.Contains(perk.Id)) continue;
                    if (perk.Skill == SkillType.None) continue;

                    int level = perk.Skill == skill ? preview.NewLevel : c.GetSkill(perk.Skill);
                    bool prereqOk = string.IsNullOrEmpty(perk.RequiresPerkId)
                                    || unlockedIds.Contains(perk.RequiresPerkId);
                    if (level >= perk.RequiredSkillLevel && prereqOk)
                    {
                        unlockedIds.Add(perk.Id);
                        preview.PerksUnlocked.Add(perk);
                        added = true;
                    }
                }
            }

            if (preview.PerksUnlocked.Count == 0) return preview;

            // Дельты производных: текущие модификаторы + модификаторы новых перков.
            var bases = DerivedStatCalculator.BaseValues(c.Attributes, cfg);
            var before = ModifierAggregator.ResolveAll(bases, c.CollectModifiers());
            var after = ModifierAggregator.ResolveAll(bases, WithNewPerks(c, preview.PerksUnlocked));
            foreach (var kv in after)
            {
                int delta = kv.Value - before[kv.Key];
                if (delta != 0) preview.DerivedDeltas[kv.Key] = delta;
            }

            for (int i = 0; i < preview.PerksUnlocked.Count; i++)
                preview.CheckValueDelta += preview.PerksUnlocked[i].CheckModifierFor(skill);

            return preview;
        }

        private static IEnumerable<StatModifier> WithNewPerks(Companion c, List<PerkDefinition> newPerks)
        {
            foreach (var m in c.CollectModifiers()) yield return m;
            for (int i = 0; i < newPerks.Count; i++)
                for (int j = 0; j < newPerks[i].Modifiers.Count; j++)
                    yield return newPerks[i].Modifiers[j];
        }
    }
}
