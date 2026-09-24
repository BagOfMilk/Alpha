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

        /// <summary>
        /// Фикс-ревью D1b (блокер + мажор): выстрел из дозора (ReactToMovement),
        /// а не собственная атака команды хода. Раньше GameSession различал это
        /// позиционным сравнением AttackerId с юнитом, чей был ход на момент
        /// вызова команды — эвристика ломается на CombatAutoResolve, где за один
        /// вызов ходят МНОГО юнитов обеих сторон подряд и единого "actingUnitId"
        /// нет. Флаг ставится прямо там, где рождается запись (CombatState —
        /// единственный, кто знает истинный источник выстрела), поэтому
        /// GameSession может классифицировать combat.attack.* / combat.
        /// overwatch.triggered по каждой новой записи, не привязываясь к тому,
        /// какая именно внешняя команда вызвала ход.
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
