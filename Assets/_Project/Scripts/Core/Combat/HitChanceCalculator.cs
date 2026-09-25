using System;
using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.Combat
{
    /// <summary>
    /// Один доданок розкладу шансу влучання (§7.2 COMBAT_V2.md): ключ +
    /// скільки він додав/відняв. Сума всіх доданків (разом із <c>clamp</c>)
    /// ДОРІВНЮЄ <see cref="HitChanceCalculator.Compute(int,bool,int,CoverType,bool,int,int,BalanceConfig,bool,bool,int)"/> —
    /// охоронець <c>HitChanceCalculatorTests.Decompose_SumsToCompute_OnAllCombinations</c>.
    /// </summary>
    public readonly struct ChanceTerm
    {
        public readonly string Key;
        public readonly int ChanceDelta;

        public ChanceTerm(string key, int delta)
        {
            Key = key;
            ChanceDelta = delta;
        }
    }

    /// <summary>
    /// Надійний % / поріг:
    ///   число = точність (скіл/зброя) − захист цілі − укриття (напів −20 / повне −40)
    ///           − дистанція за оптималом − Придушення [+ Мітка]; клемп у межі
    ///           CombatBalance.HitChanceMin..HitChanceMax.
    ///
    /// ОДНА формула годує ОБИДВА правила влучання (R1): PercentRule читає
    /// результат як %, ThresholdRule — як margin-від-рівноваги (див.
    /// ThresholdRule.Resolve). Сам цей клас киданням кубика не займається —
    /// це справа IHitRule/IDiceRoller.
    ///
    /// Бій v2 (§7.2): <see cref="Compute(int,bool,int,CoverType,bool,int,int,BalanceConfig,bool,bool,int)"/>
    /// сам більше не рахує суму — він лише підсумовує доданки
    /// <see cref="Decompose"/>, тому розклад і підсумок фізично не можуть
    /// розійтись («промах (43)» без пояснення «чому 43» — закритий розрив
    /// аудиту ядра #6).
    /// </summary>
    public static class HitChanceCalculator
    {
        /// <summary>Закритий список ключів розкладу — той самий, що й у §7.2 COMBAT_V2.md.</summary>
        public static class TermKeys
        {
            public const string Accuracy = "accuracy";
            public const string Ability = "ability";
            public const string Defense = "defense";
            public const string KnockedDown = "knocked_down";
            public const string Marked = "marked";
            public const string CoverHalf = "cover_half";
            public const string CoverFull = "cover_full";
            public const string Distance = "distance";
            public const string Suppressed = "suppressed";
            public const string Clamp = "clamp";
        }

        /// <summary>
        /// Розклад шансу влучання доданками — ті самі кроки, що були в старій
        /// монолітній формулі Compute, просто кожен крок тепер віддає СКІЛЬКИ
        /// він змінив число, а не лише мутує локальну змінну.
        /// </summary>
        public static IReadOnlyList<ChanceTerm> Decompose(int attackerAccuracy, bool attackerSuppressed,
                                  int targetDefense, CoverType cover, bool ignoreCover,
                                  int distance, int optimalRange, BalanceConfig cfg,
                                  bool targetMarked = false, bool targetKnockedDown = false,
                                  int accuracyBonus = 0)
        {
            var c = cfg.Combat;

            // Збитий з ніг — легка ціль: захист просідає (не нижче нуля).
            // "defense" несе ПОВНИЙ (немодифікований) захист, "knocked_down" —
            // окремим доданком повертає те, на скільки він просів, щоб причина
            // «чому саме це число» була видна в розкладі, а не схована всередині "defense".
            int knockdownDelta = targetKnockedDown ? Math.Min(targetDefense, c.KnockdownDefensePenalty) : 0;

            int markedDelta = targetMarked ? c.MarkedHitBonus : 0;

            int coverHalfDelta = (!ignoreCover && cover == CoverType.Half) ? -c.CoverHalfHitPenalty : 0;
            int coverFullDelta = (!ignoreCover && cover == CoverType.Full) ? -c.CoverFullHitPenalty : 0;

            int beyond = distance - optimalRange;
            int distanceDelta = beyond > 0 ? -(beyond * c.DistancePenaltyPerTile) : 0;

            int suppressedDelta = attackerSuppressed ? -c.SuppressionAccuracyPenalty : 0;

            int raw = attackerAccuracy + accuracyBonus - targetDefense + knockdownDelta
                      + markedDelta + coverHalfDelta + coverFullDelta + distanceDelta + suppressedDelta;

            int clamped = raw;
            if (clamped < c.HitChanceMin) clamped = c.HitChanceMin;
            if (clamped > c.HitChanceMax) clamped = c.HitChanceMax;

            return new[]
            {
                new ChanceTerm(TermKeys.Accuracy, attackerAccuracy),
                new ChanceTerm(TermKeys.Ability, accuracyBonus),
                new ChanceTerm(TermKeys.Defense, -targetDefense),
                new ChanceTerm(TermKeys.KnockedDown, knockdownDelta),
                new ChanceTerm(TermKeys.Marked, markedDelta),
                new ChanceTerm(TermKeys.CoverHalf, coverHalfDelta),
                new ChanceTerm(TermKeys.CoverFull, coverFullDelta),
                new ChanceTerm(TermKeys.Distance, distanceDelta),
                new ChanceTerm(TermKeys.Suppressed, suppressedDelta),
                new ChanceTerm(TermKeys.Clamp, clamped - raw),
            };
        }

        /// <summary>Низькорівнева чиста формула — для тестів і передперегляду в UI. Сума <see cref="Decompose"/>.</summary>
        public static int Compute(int attackerAccuracy, bool attackerSuppressed,
                                  int targetDefense, CoverType cover, bool ignoreCover,
                                  int distance, int optimalRange, BalanceConfig cfg,
                                  bool targetMarked = false, bool targetKnockedDown = false,
                                  int accuracyBonus = 0)
        {
            var terms = Decompose(attackerAccuracy, attackerSuppressed, targetDefense, cover, ignoreCover,
                distance, optimalRange, cfg, targetMarked, targetKnockedDown, accuracyBonus);
            int sum = 0;
            for (int i = 0; i < terms.Count; i++) sum += terms[i].ChanceDelta;
            return sum;
        }

        /// <summary>Число юніта по юніту на карті поточною зброєю (+бонус точності від здібності).</summary>
        public static int Compute(CombatUnit attacker, CombatUnit target, GridMap map,
                                  BalanceConfig cfg, int accuracyBonus = 0)
        {
            var w = attacker.Weapon;
            if (w == null) return 0;
            var cover = map != null ? map.CoverAgainst(target.Pos, attacker.Pos) : CoverType.None;
            int distance = GridPos.Chebyshev(attacker.Pos, target.Pos);
            return Compute(attacker.Profile.Accuracy, attacker.HasStatus(StatusType.Suppressed),
                           target.Profile.Defense, cover, w.IsMelee,
                           distance, w.OptimalRange, cfg,
                           target.HasStatus(StatusType.Marked), target.HasStatus(StatusType.KnockedDown),
                           accuracyBonus);
        }
    }
}
