using System;
using System.Collections.Generic;
using Game.Core.Session.Views;

namespace Game.Gameplay.UI
{
    /// <summary>Один портрет загону в бою: хто, чий хід, скільки здоров'я й очок дії, які стани.</summary>
    public sealed class PartyPortraitSlot
    {
        public string UnitId;
        /// <summary>Id персонажа для портрета (без бойового префікса <c>u_</c>).</summary>
        public string CharacterId;
        public bool IsCurrent;
        public bool IsDowned;
        public int Hp, HpMax, Ap, ApMax;
        public float HpFraction, ApFraction;
        /// <summary>Стани бійця («Bleeding», «Suppressed», …) — як у <see cref="BattleUnitView.Statuses"/>.</summary>
        public List<string> Statuses = new List<string>();
    }

    /// <summary>Прямокутник без типів рушія (модель тестується headless).</summary>
    public struct PortraitRect
    {
        public float X, Y, Width, Height;

        public PortraitRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float Bottom => Y + Height;
    }

    /// <summary>
    /// Портрети загону в бою, як у BG3 (власник, 08.10.2026: «іконки персонажів такі самі на рушії гри»;
    /// «У бою»): стовпець ліворуч під колесом черги. ЩО показати і ДЕ — тут (чистий C#, тести
    /// <c>PartyPortraitsModelTests</c>), малює <see cref="BattleHudScreen"/>.
    /// </summary>
    public static class PartyPortraitsModel
    {
        /// <summary>Найбільше портретів у стовпці: загін фіналу — 4 разом із протагоністом (Поправка №17.2), запас — для подій.</summary>
        public const int MaxSlots = 6;

        public const float CardAspect = 1.2f;          // висота обличчя / ширина
        public const float BarsHeight = 9f;            // смужки здоров'я й очок дії під обличчям
        public const float MinCardWidth = 44f;

        /// <summary>Id персонажа з бойового id: <c>u_zakhar</c> → <c>zakhar</c>, <c>defector_x</c> → <c>x</c>.</summary>
        public static string CharacterIdOf(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return unitId;
            if (unitId.StartsWith("u_", StringComparison.Ordinal)) return unitId.Substring(2);
            if (unitId.StartsWith("defector_", StringComparison.Ordinal)) return unitId.Substring(9);
            return unitId;
        }

        /// <summary>
        /// Свої бійці, що в бою (упалі лишаються — їх ще можна підняти; хто поза боєм — ні). Протагоніст першим, далі
        /// порядок бою — стовпець не стрибає між ходами.
        /// </summary>
        public static List<PartyPortraitSlot> Build(BattleView view, string protagonistId)
        {
            var slots = new List<PartyPortraitSlot>();
            if (view?.Units == null) return slots;
            foreach (var u in view.Units)
            {
                if (u == null || u.Side != "Player" || u.IsOutOfBattle) continue;
                var slot = new PartyPortraitSlot
                {
                    UnitId = u.Id,
                    CharacterId = CharacterIdOf(u.Id),
                    IsCurrent = string.Equals(u.Id, view.CurrentUnitId, StringComparison.Ordinal),
                    IsDowned = u.IsDowned,
                    Hp = u.Hp, HpMax = u.HpMax, Ap = u.Ap, ApMax = u.ApMax,
                    HpFraction = Fraction(u.Hp, u.HpMax),
                    ApFraction = Fraction(u.Ap, u.ApMax)
                };
                if (u.Statuses != null) slot.Statuses.AddRange(u.Statuses);
                if (slot.CharacterId == protagonistId) slots.Insert(0, slot);
                else slots.Add(slot);
            }
            if (slots.Count > MaxSlots) slots.RemoveRange(MaxSlots, slots.Count - MaxSlots);
            return slots;
        }

        /// <summary>
        /// Стовпець від <paramref name="top"/> до <paramref name="bottom"/> біля лівого краю: картки бажаної ширини, а
        /// якщо всі не влазять — менші (не дрібніші за <see cref="MinCardWidth"/>); що й тоді не влізло — не малюється.
        /// </summary>
        public static List<PortraitRect> Layout(int count, float x, float top, float bottom, float preferredWidth, float gap)
        {
            var rects = new List<PortraitRect>();
            if (count <= 0 || bottom <= top) return rects;
            float width = preferredWidth;
            float Card(float w) => w * CardAspect + BarsHeight;
            float available = bottom - top;
            float need = count * Card(width) + (count - 1) * gap;
            if (need > available)
            {
                width = (available - (count - 1) * gap - count * BarsHeight) / (count * CardAspect);
                if (width < MinCardWidth) width = MinCardWidth;
            }
            float y = top;
            for (int i = 0; i < count; i++)
            {
                float h = Card(width);
                if (y + h > bottom + 0.01f) break;
                rects.Add(new PortraitRect(x, y, width, h));
                y += h + gap;
            }
            return rects;
        }

        private static float Fraction(int value, int max) => max <= 0 ? 0f : Math.Max(0f, Math.Min(1f, value / (float)max));
    }
}
