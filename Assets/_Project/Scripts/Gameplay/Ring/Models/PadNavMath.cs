using System;
using System.Collections.Generic;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>Напрям руху фокусу (хрестовина, лівий стік, стрілки).</summary>
    public enum PadDir { Up = 0, Down, Left, Right }

    /// <summary>
    /// Ціль фокусу екрана кар'єри для розрахунку: ключ (щоб фокус пережив перебудову екрана), прямокутник у координатах
    /// панелі (y згори), доступність і ранг («куди стати першим»: менший — раніше).
    /// </summary>
    public struct PadSlot
    {
        public string Key;
        public float X, Y, W, H;
        public bool Enabled;
        public int Rank;

        public PadSlot(string key, float x, float y, float w, float h, bool enabled = true, int rank = 3)
        {
            Key = key; X = x; Y = y; W = w; H = h; Enabled = enabled; Rank = rank;
        }

        public bool HasGeometry { get { return !float.IsNaN(X) && !float.IsNaN(Y) && W > 0f && H > 0f; } }
        public float CX { get { return X + W * 0.5f; } }
        public float CY { get { return Y + H * 0.5f; } }
    }

    /// <summary>
    /// Фокус без миші на екранах кар'єри (P3 геймпад, 11.11.2026): куди стати першим, куди повернутись після перебудови
    /// екрана і куди перейти в напрямку. Чистий C#: усе, що тут вирішується, тестується без Unity (<c>PadNavTests</c>).
    /// </summary>
    public static class PadNavMath
    {
        /// <summary>Недоступна ціль стає першою, лише коли доступних немає.</summary>
        public const int DisabledPenalty = 100;

        /// <summary>Ціль за замовчуванням: найменший ранг серед доступних (недоступні — лише якщо інших немає), далі порядок у дереві.</summary>
        public static int Default(IList<PadSlot> slots)
        {
            int best = -1, bestRank = int.MaxValue;
            for (int i = 0; i < slots.Count; i++)
            {
                int r = slots[i].Rank + (slots[i].Enabled ? 0 : DisabledPenalty);
                if (r < bestRank) { bestRank = r; best = i; }
            }
            return best;
        }

        /// <summary>Повернути фокус після перебудови: та сама ціль за ключем; ключа немає — ціль за замовчуванням.</summary>
        public static int Restore(IList<PadSlot> slots, string key)
        {
            return Restore(slots, key, float.NaN, float.NaN);
        }

        /// <summary><see cref="Restore(IList{PadSlot},string,float,float)"/>: цілі поруч ще без розкладки — спитати наступного кадру.</summary>
        public const int WaitLayout = -2;

        /// <summary>
        /// Повернути фокус після перебудови: та сама ціль за ключем; ключа вже немає (куплену перевагу прибрано зі списку) —
        /// найближча доступна до місця, де стояв фокус (<paramref name="lastX"/>, <paramref name="lastY"/> — центр; NaN —
        /// невідомо), а не перша плитка екрана (аудит P3 геймпад, 11.11.2026); у цілей ще немає розкладки (щойно перебудовані) —
        /// <see cref="WaitLayout"/>; інакше — ціль за замовчуванням.
        /// </summary>
        public static int Restore(IList<PadSlot> slots, string key, float lastX, float lastY)
        {
            if (!string.IsNullOrEmpty(key))
                for (int i = 0; i < slots.Count; i++)
                    if (slots[i].Key == key) return i;
            if (string.IsNullOrEmpty(key) || float.IsNaN(lastX) || float.IsNaN(lastY)) return Default(slots);
            int best = -1;
            bool laid = false;
            float bestD = float.MaxValue;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (!s.HasGeometry) continue;
                laid = true;
                if (!s.Enabled) continue;
                float dx = s.CX - lastX, dy = s.CY - lastY;
                float d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best >= 0) return best;
            return laid || slots.Count == 0 ? Default(slots) : WaitLayout;
        }

        /// <summary>
        /// Ключі в порядку дерева без повторів: другий «Далі» стає «Далі#2». Інакше дві однакові кнопки (дві «Купити» в шухляді)
        /// після перебудови повертали б фокус на першу.
        /// </summary>
        public static string[] UniqueKeys(IList<string> raw)
        {
            var seen = new Dictionary<string, int>();
            var res = new string[raw.Count];
            for (int i = 0; i < raw.Count; i++)
            {
                string k = raw[i] ?? "";
                int n;
                seen.TryGetValue(k, out n);
                n++;
                seen[k] = n;
                res[i] = n == 1 ? k : k + "#" + n;
            }
            return res;
        }

        /// <summary>
        /// Наступна ціль у напрямку <paramref name="dir"/> від <paramref name="from"/>; −1 — далі нічого (фокус лишається; по колу
        /// не ходимо: гравець має відчувати край екрана). <paramref name="from"/> &lt; 0 — ціль за замовчуванням.
        /// Оцінка — як у просторовій навігації браузерів: відстань між краями вздовж напрямку + утроєний зазор упоперек (сусід у
        /// тому ж ряду завжди ближчий за ближчого в іншому ряду) + чверть зсуву центрів упоперек (з двох у сусідньому ряду —
        /// той, що навпроти).
        /// </summary>
        public static int Move(IList<PadSlot> slots, int from, PadDir dir)
        {
            if (from < 0 || from >= slots.Count) return Default(slots);
            var f = slots[from];
            if (!f.HasGeometry) return -1;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < slots.Count; i++)
            {
                if (i == from) continue;
                var c = slots[i];
                if (!c.HasGeometry) continue;
                float along, gap, across, offset;
                switch (dir)
                {
                    case PadDir.Up:
                        along = f.CY - c.CY;
                        gap = Math.Max(0f, f.Y - (c.Y + c.H));
                        across = Math.Max(0f, Math.Max(c.X - (f.X + f.W), f.X - (c.X + c.W)));
                        offset = Math.Abs(c.CX - f.CX);
                        break;
                    case PadDir.Down:
                        along = c.CY - f.CY;
                        gap = Math.Max(0f, c.Y - (f.Y + f.H));
                        across = Math.Max(0f, Math.Max(c.X - (f.X + f.W), f.X - (c.X + c.W)));
                        offset = Math.Abs(c.CX - f.CX);
                        break;
                    case PadDir.Left:
                        along = f.CX - c.CX;
                        gap = Math.Max(0f, f.X - (c.X + c.W));
                        across = Math.Max(0f, Math.Max(c.Y - (f.Y + f.H), f.Y - (c.Y + c.H)));
                        offset = Math.Abs(c.CY - f.CY);
                        break;
                    default:
                        along = c.CX - f.CX;
                        gap = Math.Max(0f, c.X - (f.X + f.W));
                        across = Math.Max(0f, Math.Max(c.Y - (f.Y + f.H), f.Y - (c.Y + c.H)));
                        offset = Math.Abs(c.CY - f.CY);
                        break;
                }
                if (along <= 0.5f) continue;        // не в той бік (або центр на одній лінії)
                float score = gap + 3f * across + 0.25f * offset;
                if (score < bestScore - 0.001f) { bestScore = score; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Скільки прокрутити список, щоб ціль стала видимою: ціль нижче вікна — вниз до її низу з полем, вище — вгору до її верху.
        /// Повертає новий відступ прокрутки (не менше 0); ціль уже видна — той самий.
        /// </summary>
        public static float ScrollToShow(float offset, float viewTop, float viewH, float itemTop, float itemH, float margin = 8f)
        {
            if (float.IsNaN(viewTop) || float.IsNaN(itemTop) || viewH <= 0f) return offset;
            float viewBottom = viewTop + viewH, itemBottom = itemTop + itemH;
            if (itemH + 2f * margin >= viewH || itemTop < viewTop + margin) return Math.Max(0f, offset - (viewTop + margin - itemTop));
            if (itemBottom > viewBottom - margin) return Math.Max(0f, offset + (itemBottom - (viewBottom - margin)));
            return offset;
        }
    }
}
