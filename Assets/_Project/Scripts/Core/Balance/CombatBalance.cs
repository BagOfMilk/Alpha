using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа тактичного бою (Б1, R14): зібрані у своїй секції, щоб сім
    /// паралельних пакетів не редагували один спільний список полів
    /// одночасно. Підключається одною властивістю в BalanceConfig.Combat.
    /// Усі значення — ПЛЕЙСХОЛДЕРИ (перенесені з архівної бойової лінії,
    /// коміт 20b8dcf, без зміни величин).
    /// </summary>
    [Serializable]
    public sealed class CombatBalance
    {
        // ---- Точність (надійний %/поріг) ----
        //
        // Фікс-ревью раунд 2 (QA, major): AccuracyPerWeaponSkill=2 — це
        // необроблене архівне число (скил там жив в іншому масштабі), а модель
        // персонажа Епіка 2 дає скилу лише 0..10. Разом з AccuracyPerAgility=1
        // (BalanceConfig) це давало Максиму (Melee 6, Ловкість 5, перший
        // бойовий напарник вузла 1) Accuracy = 5+6×2 = 17 — суцільний Miss під
        // ThresholdRule (потрібно ≥50 навіть на Graze) і ~19% під PercentRule,
        // саме цифри зі скріншотів QA. Найгірше: це не залежало ні від
        // складності бою, ні від гри гравця — 17 нижче порога Graze НАЗАВЖДИ,
        // навіть на Defense=0 (перший бій вузла 1, два горд-розвідники) — за
        // 40 раундів (RoundCap) не було жодного улучення гравця, і Максим
        // гарантовано гинув. Емпірична перевірка детермінованого вузла 1
        // (ThresholdRule) по кожному цілому значенню: 2..7 — та сама
        // катастрофа (Draw на раунді 41, Максим мертвий); 8 — чиста перемога
        // за 2 раунди, ніхто не падає; 10 — перемога за 1 раунд. Узято 8 —
        // найменше значення, що фактично лагодить бій (не форсує ще й
        // тривіальний одно-раундовий стомп): Максим 5+6×8=53 (Graze, зачіпає),
        // з 2-3 раундами реального обміну ударами з обох боків, а не гарантія
        // без шансу для ворога.
        public int AccuracyPerWeaponSkill = 8;
        public int KnockdownDefensePenalty = 10;
        public int MarkedHitBonus = 10;
        public int CoverHalfHitPenalty = 20;
        public int CoverFullHitPenalty = 40;
        public int DistancePenaltyPerTile = 5;
        public int SuppressionAccuracyPenalty = 15;
        public int HitChanceMin = 1;
        public int HitChanceMax = 99;

        // ---- PercentRule: граза/крит-поріг за одним-двома кидками IDiceRoller ----
        public int GrazeThresholdPercent = 15;
        public int HighHitNoFullMiss = 85; // шанс ≥ цього — найгірше можливе — граза, не промах

        // ---- ThresholdRule: детерміновані полоси за маржею (R1) ----
        // «Показаний поріг» тлумачиться як margin-від-точки-рівноваги:
        // margin = shown − ThresholdBaseline. Жодного кидка кубика не відбувається.
        public int ThresholdBaseline = 50;
        public int ThresholdGrazeBand = 15;  // 0 ≤ margin < band — Graze
        public int ThresholdCritBand = 35;   // margin ≥ band — Crit (між — Hit)

        // ---- Урон ----
        public double GrazePartialPercent = 50.0; // граза — частка повного урону

        // ---- Strike-метр ----
        public int StrikeGuaranteeAt = 3;
        public int StrikePerHit = 1;

        // ---- Даун/стабілізація ----
        public int DownWindowTurns = 2;
        public int StabilizeApCost = 3;
        public int StandUpApCost = 2;

        // ---- Статуси/DoT ----
        public int DotDurationTurns = 3;
        public int DotDamagePerTurn = 2;
        public int ResolvePerStatusTurnReduction = 3; // Воля/StatusDurationReduction скорочує тривалість на 1 хід за N очок

        // ---- Рух ----
        public double SuppressionMoveCostMultiplier = 1.5;

        // ---- Overwatch (US-3.6, коміт 20b8dcf) ----
        public int OverwatchAccuracyPenalty = 10; // штраф за постріл без прицілювання
        public int OverwatchConeSlopeNum = 1;     // 1/1 = конус 90°
        public int OverwatchConeSlopeDen = 1;

        // ---- Завершуваність автобою (гарантія B1) ----
        /// <summary>Раунд, після якого незавершений бій примусово стає Draw.</summary>
        public int RoundCap = 40;
    }
}
