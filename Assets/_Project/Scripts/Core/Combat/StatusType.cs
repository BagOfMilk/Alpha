namespace Game.Core.Combat
{
    /// <summary>
    /// Боевые состояния: набор из 7 + Шред как отдельная механика снижения
    /// брони (на юните копится счётчик шреда, не статус). Наложение
    /// гарантированное; Воля (производная StatusDurationReduction) сокращает
    /// длительность, минимум 1 ход.
    ///
    /// В срезе механически реализованы: Подавление, Кровотечение, Поджог.
    /// Остальные значения объявлены под итерацию способностей.
    /// </summary>
    public enum StatusType
    {
        None = 0,
        Bleeding = 1,    // Кровотечение — урон/ход, игнорирует броню, True-урон
        Stunned = 2,     // Оглушение — пропуск хода / потеря AP [итерация способностей]
        Suppressed = 3,  // Подавление — −точность + дороже движение, до конца след. хода
        KnockedDown = 4, // Сбит с ног — −защита, AP чтобы встать [итерация способностей]
        Marked = 5,      // Метка — +шанс по цели [итерация способностей]
        Burning = 6,     // Поджог — урон огнём/ход (DoT, множится резистом огня)
        Poisoned = 7     // Яд — урон токсином/ход, обходит броню [итерация способностей]
    }

    /// <summary>Активный экземпляр состояния на юните: тип, остаток ходов, DoT-параметры.</summary>
    public sealed class StatusInstance
    {
        public StatusType Type;
        public int RemainingTurns;
        public int DotDamagePerTurn; // 0 — не DoT
        public DamageType DotType = DamageType.True;

        public StatusInstance(StatusType type, int remainingTurns, int dotDamagePerTurn = 0,
                              DamageType dotType = DamageType.True)
        {
            Type = type;
            RemainingTurns = remainingTurns;
            DotDamagePerTurn = dotDamagePerTurn;
            DotType = dotType;
        }
    }
}
