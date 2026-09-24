using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа тактического боя (Б1, R14): собраны в своей секции, чтобы семь
    /// параллельных пакетов не редактировали один общий список полей
    /// одновременно. Подключается одним свойством в BalanceConfig.Combat.
    /// Все значения — ПЛЕЙСХОЛДЕРЫ (перенесены из архивной боевой линии,
    /// коммит 20b8dcf, без смены величин).
    /// </summary>
    [Serializable]
    public sealed class CombatBalance
    {
        // ---- Точность (надёжный %/порог) ----
        public int AccuracyPerWeaponSkill = 2;
        public int KnockdownDefensePenalty = 10;
        public int MarkedHitBonus = 10;
        public int CoverHalfHitPenalty = 20;
        public int CoverFullHitPenalty = 40;
        public int DistancePenaltyPerTile = 5;
        public int SuppressionAccuracyPenalty = 15;
        public int HitChanceMin = 1;
        public int HitChanceMax = 99;

        // ---- PercentRule: граза/крит-порог по одному-двум роллам IDiceRoller ----
        public int GrazeThresholdPercent = 15;
        public int HighHitNoFullMiss = 85; // шанс ≥ этого — худшее возможное — граза, не промах

        // ---- ThresholdRule: детерминированные полосы по марже (R1) ----
        // «Показанный порог» интерпретируется как margin-от-точки-равновесия:
        // margin = shown − ThresholdBaseline. Никакого броска кубика не происходит.
        public int ThresholdBaseline = 50;
        public int ThresholdGrazeBand = 15;  // 0 ≤ margin < band — Graze
        public int ThresholdCritBand = 35;   // margin ≥ band — Crit (между — Hit)

        // ---- Урон ----
        public double GrazePartialPercent = 50.0; // граза — доля полного урона

        // ---- Strike-метр ----
        public int StrikeGuaranteeAt = 3;
        public int StrikePerHit = 1;

        // ---- Даун/стабилизация ----
        public int DownWindowTurns = 2;
        public int StabilizeApCost = 3;
        public int StandUpApCost = 2;

        // ---- Статусы/DoT ----
        public int DotDurationTurns = 3;
        public int DotDamagePerTurn = 2;
        public int ResolvePerStatusTurnReduction = 3; // Воля/StatusDurationReduction сокращает длительность на 1 ход за N очков

        // ---- Движение ----
        public double SuppressionMoveCostMultiplier = 1.5;

        // ---- Overwatch (US-3.6, коммит 20b8dcf) ----
        public int OverwatchAccuracyPenalty = 10; // штраф навскидку
        public int OverwatchConeSlopeNum = 1;     // 1/1 = конус 90°
        public int OverwatchConeSlopeDen = 1;

        // ---- Завершаемость автобоя (гарантия B1) ----
        /// <summary>Раунд, после которого незавершённый бой принудительно становится Draw.</summary>
        public int RoundCap = 40;
    }
}
