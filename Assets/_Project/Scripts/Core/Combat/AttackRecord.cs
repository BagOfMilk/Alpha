namespace Game.Core.Combat
{
    /// <summary>
    /// Запись одной атаки для телеметрии честности RNG (риск R11 GDD): показанный
    /// игроку шанс против фактического исхода. Плейтест сверяет агрегаты
    /// (hit-rate по корзинам шанса, доля критов, урон) с ожиданием — расхождение
    /// сигналит о баге стека смягчения (граза/клампы/Strike).
    /// </summary>
    public readonly struct AttackRecord
    {
        public readonly int Round;
        public readonly Side AttackerSide;
        public readonly string AttackerId;
        public readonly string TargetId;
        /// <summary>Показанный шанс попадания (%) — тот же, что видел игрок в телеграфии.</summary>
        public readonly int Chance;
        public readonly HitOutcome Outcome;
        public readonly bool Crit;
        public readonly int Damage;
        /// <summary>Гарантированный удар (потрачен Strike-метр) — в hit-rate не считается.</summary>
        public readonly bool Forced;

        public AttackRecord(int round, Side attackerSide, string attackerId, string targetId,
                            int chance, HitOutcome outcome, bool crit, int damage, bool forced)
        {
            Round = round;
            AttackerSide = attackerSide;
            AttackerId = attackerId;
            TargetId = targetId;
            Chance = chance;
            Outcome = outcome;
            Crit = crit;
            Damage = damage;
            Forced = forced;
        }
    }
}
