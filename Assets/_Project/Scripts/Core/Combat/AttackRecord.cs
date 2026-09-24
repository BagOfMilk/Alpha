namespace Game.Core.Combat
{
    /// <summary>
    /// Запись одной атаки для телеметрии честности броска: показанный игроку
    /// шанс/порог против фактического исхода. Плейтест сверяет агрегаты
    /// (hit-rate по корзинам шанса, доля критов, урон) с ожиданием — расхождение
    /// сигналит о баге стека смягчения (граза/клампы/Strike).
    /// </summary>
    public readonly struct AttackRecord
    {
        public readonly int Round;
        public readonly Side AttackerSide;
        public readonly string AttackerId;
        public readonly string TargetId;

        /// <summary>Показанное игроку число (% для PercentRule, порог для ThresholdRule).</summary>
        public readonly int Chance;

        public readonly AttackOutcome Outcome;
        public readonly int Damage;

        /// <summary>Гарантированный удар (потрачен Strike-метр) — в hit-rate не считается.</summary>
        public readonly bool Forced;

        public AttackRecord(int round, Side attackerSide, string attackerId, string targetId,
                            int chance, AttackOutcome outcome, int damage, bool forced)
        {
            Round = round;
            AttackerSide = attackerSide;
            AttackerId = attackerId;
            TargetId = targetId;
            Chance = chance;
            Outcome = outcome;
            Damage = damage;
            Forced = forced;
        }
    }
}
