using System;
using System.Collections.Generic;
using Game.Core.Session.Views;

namespace Game.Gameplay.UI
{
    /// <summary>Один хід на колесі черги: хто ходить, у якому раунді, чи пропустить.</summary>
    public sealed class TurnWheelSlot
    {
        public string UnitId;

        /// <summary>"Player" | "Enemy" | "FromDefector" — як <see cref="BattleUnitView.Side"/>.</summary>
        public string Side;

        /// <summary>Хід, що йде просто зараз (завжди слот 0).</summary>
        public bool IsCurrent;

        public bool IsDowned;

        /// <summary>Оглушений: цей його хід пропаде (ОД на нулі).</summary>
        public bool SkipsTurn;

        /// <summary>Номер раунду, у якому випадає цей хід.</summary>
        public int Round;
    }

    /// <summary>
    /// Колесо черги ходів (Поправка №14.5; власник, 29.09.2026: «не треба,
    /// краще показати по черзі хто ходить, колесо чи щось таке» — обрано
    /// «Колесо в кутку»). Замість стрічки, що переносилась у рядки, без
    /// межі раунду і без зв'язку з мапою.
    ///
    /// ЩО показувати рахує ця модель (без рушія, тести
    /// <c>TurnWheelModelTests</c>); <c>BattleHudScreen</c> лише малює. Черга
    /// ініціативи фіксована на весь бій (<c>TurnSystem</c>), тож наперед її
    /// видно точно: від поточного бійця до кінця раунду, далі — наступний
    /// раунд від початку. Ті, хто вибув (<see cref="BattleUnitView.IsOutOfBattle"/>),
    /// з колеса зникають; упалі лишаються перекресленими. Оглушений
    /// пропускає рівно один — найближчий — свій хід: <c>CombatState.BeginTurn</c>
    /// знімає стан «Stunned» і обнуляє ОД на початку цього ходу.
    /// </summary>
    public sealed class TurnWheelModel
    {
        /// <summary>Скільки ходів уміщує колесо — більше не читається по колу.</summary>
        public const int DefaultMaxSlots = 8;

        public IReadOnlyList<TurnWheelSlot> Slots = Array.Empty<TurnWheelSlot>();

        /// <summary>Індекс першого слоту наступного раунду; −1 — на колесі лише поточний раунд.</summary>
        public int NextRoundStartsAt = -1;

        public int CurrentRound;

        public static TurnWheelModel Build(BattleView view, int maxSlots = DefaultMaxSlots)
        {
            var model = new TurnWheelModel();
            if (view == null) return model;
            model.CurrentRound = view.Round;

            var order = view.InitiativeOrder;
            if (order == null || order.Count == 0 || maxSlots <= 0) return model;

            var units = new Dictionary<string, BattleUnitView>(StringComparer.Ordinal);
            if (view.Units != null)
                foreach (var u in view.Units)
                    if (u != null && !string.IsNullOrEmpty(u.Id)) units[u.Id] = u;

            int n = order.Count;
            int start = 0;
            for (int i = 0; i < n; i++)
                if (string.Equals(order[i], view.CurrentUnitId, StringComparison.Ordinal)) { start = i; break; }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var slots = new List<TurnWheelSlot>();

            // Поточний раунд до кінця і весь наступний — не далі.
            int horizon = (n - start) + n;
            for (int k = 0; k < horizon && slots.Count < maxSlots; k++)
            {
                int idx = (start + k) % n;
                if (!units.TryGetValue(order[idx], out var unit) || unit.IsOutOfBattle) continue;

                // Поточний хід уже почався: якщо юніт був оглушений, стан знято
                // на початку ходу, і пропуск видно по нулю ОД, а не на колесі.
                bool firstUpcoming = seen.Add(unit.Id) && k > 0;
                bool skips = firstUpcoming && !unit.IsDowned && IsStunned(unit);

                int round = view.Round + (start + k) / n;
                if (round > view.Round && model.NextRoundStartsAt < 0) model.NextRoundStartsAt = slots.Count;

                slots.Add(new TurnWheelSlot
                {
                    UnitId = unit.Id,
                    Side = unit.Side,
                    IsCurrent = k == 0 && string.Equals(unit.Id, view.CurrentUnitId, StringComparison.Ordinal),
                    IsDowned = unit.IsDowned,
                    SkipsTurn = skips,
                    Round = round,
                });
            }

            model.Slots = slots;
            return model;
        }

        /// <summary>
        /// Кут слоту на колі в градусах (екранні координати, y донизу):
        /// поточний — угорі (−90°), далі за годинниковою стрілкою рівними кроками.
        /// </summary>
        public static float SlotAngleDegrees(int index, int count)
        {
            if (count <= 0) return -90f;
            return -90f + 360f * index / count;
        }

        private static bool IsStunned(BattleUnitView unit)
        {
            if (unit.Statuses != null)
                foreach (var s in unit.Statuses)
                    if (s == "Stunned") return true;
            return false;
        }
    }
}
