using System;
using Game.Core.Balance;
using Game.Core.Randomness;

namespace Game.Core.Combat
{
    /// <summary>
    /// R1: правило влучання без кубика — показаний відсоток справджується рівно.
    /// IDiceRoller НЕ викликається жодного разу (інваріант 1).
    ///
    /// Власник, 29.09.2026: «Усі відсотки мають працювати у тому режимі, в настройках
    /// його можна змінить». Раніше тут була маржа від 50: шанс нижче 50 не влучав
    /// НІКОЛИ, 50–64 давав лише зачеп — 30 % і 45 % нічого не означали, а стрілець
    /// проти укриття міг не влучити жодного разу (глухий кут скиту, трек C3).
    ///
    /// Тепер — детермінований накопичувач, той самий прийом, що й у планувальнику
    /// подій: у кожного бійця свій лічильник (старт <see cref="CombatBalance.ThresholdCarryStart"/>),
    /// кожен удар додає до нього показаний шанс; набралося 100 — влучання, 100
    /// списується. Удар на 30 % влучає рівно 3 рази з 10, на 60 % — 6 з 10, без
    /// жодного кидка. Результат саме цього удару відомий наперед — прев'ю його
    /// показує (<see cref="Predict"/>, інваріант 8). Крит — як і раніше, для
    /// впевнених ударів (шанс ≥ Baseline + CritBand); зачепу в цьому правилі немає.
    /// </summary>
    public sealed class ThresholdRule : IHitRule
    {
        private readonly BalanceConfig _cfg;

        public ThresholdRule(BalanceConfig cfg)
        {
            _cfg = cfg ?? new BalanceConfig();
        }

        public AttackOutcome Resolve(CombatUnit attacker, CombatUnit target, int shownChanceOrThreshold, IDiceRoller roller)
        {
            var outcome = Step(CarryOf(attacker), shownChanceOrThreshold, out int carryAfter);
            if (attacker != null) attacker.HitCarry = carryAfter;
            return outcome;
        }

        /// <summary>Чим закінчиться наступний удар цього бійця з таким шансом — без зміни лічильника.</summary>
        public AttackOutcome Predict(CombatUnit attacker, int shownChance) => Step(CarryOf(attacker), shownChance, out _);

        /// <summary>Скільки з <paramref name="shots"/> ударів поспіль (однаковий шанс) влучать — для «Черги» тощо.</summary>
        public int PredictHits(CombatUnit attacker, int shownChance, int shots)
        {
            int carry = CarryOf(attacker), hits = 0;
            for (int i = 0; i < shots; i++)
                if (Step(carry, shownChance, out carry) != AttackOutcome.Miss) hits++;
            return hits;
        }

        private int CarryOf(CombatUnit attacker) =>
            attacker != null && attacker.HitCarry != CombatUnit.UnsetCarry ? attacker.HitCarry : _cfg.Combat.ThresholdCarryStart;

        private AttackOutcome Step(int carry, int shown, out int carryAfter)
        {
            var c = _cfg.Combat;
            int total = carry + Math.Max(0, shown);
            if (total >= 100)
            {
                carryAfter = total - 100;
                return shown >= c.ThresholdBaseline + c.ThresholdCritBand ? AttackOutcome.Crit : AttackOutcome.Hit;
            }
            carryAfter = total;
            return AttackOutcome.Miss;
        }
    }
}
