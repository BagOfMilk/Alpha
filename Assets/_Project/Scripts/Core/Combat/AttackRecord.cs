namespace Game.Core.Combat
{
    /// <summary>
    /// Запис однієї атаки для телеметрії чесності кидка: показаний гравцю
    /// шанс/поріг проти фактичного наслідку. Плейтест звіряє агрегати
    /// (hit-rate за кошиками шансу, частка критів, урон) з очікуванням — розбіжність
    /// сигналить про баг стеку пом'якшення (граза/клампи/Strike).
    /// </summary>
    public readonly struct AttackRecord
    {
        public readonly int Round;
        public readonly Side AttackerSide;
        public readonly string AttackerId;
        public readonly string TargetId;

        /// <summary>Показане гравцю число (% для PercentRule, поріг для ThresholdRule).</summary>
        public readonly int Chance;

        public readonly AttackOutcome Outcome;
        public readonly int Damage;

        /// <summary>Гарантований удар (витрачений Strike-метр) — у hit-rate не рахується.</summary>
        public readonly bool Forced;

        /// <summary>
        /// Фікс-ревʼю D1b (блокер + мажор): постріл з дозору (ReactToMovement),
        /// а не власна атака команди ходу. Раніше GameSession розрізняв це
        /// позиційним порівнянням AttackerId з юнітом, чий був хід на момент
        /// виклику команди — евристика ламається на CombatAutoResolve, де за один
        /// виклик ходять БАГАТО юнітів обох сторін поспіль і єдиного "actingUnitId"
        /// немає. Прапорець ставиться прямо там, де народжується запис (CombatState —
        /// єдиний, хто знає справжнє джерело пострілу), тому
        /// GameSession може класифікувати combat.attack.* / combat.
        /// overwatch.triggered за кожним новим записом, не прив'язуючись до того,
        /// яка саме зовнішня команда викликала хід.
        /// </summary>
        public readonly bool IsReaction;

        public AttackRecord(int round, Side attackerSide, string attackerId, string targetId,
                            int chance, AttackOutcome outcome, int damage, bool forced, bool isReaction = false)
        {
            Round = round;
            AttackerSide = attackerSide;
            AttackerId = attackerId;
            TargetId = targetId;
            Chance = chance;
            Outcome = outcome;
            Damage = damage;
            Forced = forced;
            IsReaction = isReaction;
        }
    }
}
