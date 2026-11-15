using System;
using System.Collections.Generic;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Вільна зона кадру між панелями інтерфейсу (огляд слотів 32–33, ПМ 13.11.2026; У0 арт-директора 14.11.2026). Раніше герой
    /// наодинці стояв на сталих 32 % ширини, тож на 4:3 і 16:10 голова ховалася під панеллю «ВИГЛЯД», рука на кінці сезону — під
    /// «РОЗВИТОК», а картка шоу закривала суперника. Тепер камера повертається так, щоб те, кого вона показує (прямокутник
    /// «маківка — ключиці» на ширину плечей; на картці шоу — по прямокутнику на кожного борця), лягло між панелями з полем
    /// <see cref="Pad"/>, і лише на стільки, на скільки треба. Де все й так вільно (зазвичай 16:9), кадр не змінюється; де вільного
    /// місця в межах повороту немає — теж (сцена пише в лог «не влізло»).
    /// <para>
    /// Чистий C#: прямокутники в частках екрана (x — зліва направо, y — згори вниз, 0..1) і проекція точки на горизонталь кадру.
    /// Сцена міряє екранні прямокутники своїх людей і крутить камеру за <see cref="YawFor"/>. Охоронці — <c>RingFrameZoneTests</c>.
    /// </para>
    /// </summary>
    public static class RingFrameZone
    {
        /// <summary>Прямокутник у частках екрана: (X0; Y0) — лівий верхній кут, (X1; Y1) — правий нижній.</summary>
        public struct Box
        {
            public float X0, Y0, X1, Y1;
            public Box(float x0, float y0, float x1, float y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
            public float Width { get { return X1 - X0; } }
            public float Height { get { return Y1 - Y0; } }
            public Box Moved(float dx) { return new Box(X0 + dx, Y0, X1 + dx, Y1); }
            public override string ToString() { return string.Format(System.Globalization.CultureInfo.InvariantCulture, "[{0:F3}..{1:F3} × {2:F3}..{3:F3}]", X0, X1, Y0, Y1); }
        }

        /// <summary>Панель береться до уваги, якщо заходить у смугу об'єкта хоча б на стільки (частка висоти екрана).</summary>
        public const float MinBandOverlap = 0.01f;

        /// <summary>Поле від людини до краю панелі — частка ширини кадру (арт-директор: «щонайменше 2,5 %», 14.11.2026).</summary>
        public const float Pad = 0.025f;

        /// <summary>Усі вільні проміжки по горизонталі в смузі [<paramref name="y0"/>; <paramref name="y1"/>] (зліва направо).</summary>
        public static List<KeyValuePair<float, float>> Gaps(IList<Box> panels, float y0, float y1)
        {
            var spans = new List<KeyValuePair<float, float>>();
            if (panels != null)
                foreach (var p in panels)
                {
                    float overlap = Math.Min(y1, p.Y1) - Math.Max(y0, p.Y0);
                    if (overlap < MinBandOverlap || p.X1 <= p.X0) continue;
                    float a = Clamp01(p.X0), b = Clamp01(p.X1);
                    if (b > a) spans.Add(new KeyValuePair<float, float>(a, b));
                }
            spans.Sort((l, r) => l.Key.CompareTo(r.Key));
            var gaps = new List<KeyValuePair<float, float>>();
            float cursor = 0f;
            foreach (var s in spans)
            {
                if (s.Key > cursor) gaps.Add(new KeyValuePair<float, float>(cursor, s.Key));
                if (s.Value > cursor) cursor = s.Value;
            }
            if (cursor < 1f) gaps.Add(new KeyValuePair<float, float>(cursor, 1f));
            return gaps;
        }

        /// <summary>
        /// Найширший вільний проміжок по горизонталі в смузі [<paramref name="y0"/>; <paramref name="y1"/>]. <c>false</c> —
        /// ширшого за <paramref name="minWidth"/> немає. Без панелей — увесь екран.
        /// </summary>
        public static bool FreeGap(IList<Box> panels, float y0, float y1, float minWidth, out float gx0, out float gx1)
        {
            float best = -1f;
            gx0 = gx1 = 0f;
            foreach (var g in Gaps(panels, y0, y1))
                if (g.Value - g.Key > best) { best = g.Value - g.Key; gx0 = g.Key; gx1 = g.Value; }
            return best >= minWidth && best > 0f;
        }

        /// <summary>
        /// Прямокутник вільний: цілком на екрані по горизонталі й не ближче за <paramref name="pad"/> до жодної панелі, що
        /// заходить у його смугу (край екрана поля не потребує — панелі там немає).
        /// </summary>
        public static bool IsFree(IList<Box> panels, Box r, float pad)
        {
            const float eps = 1e-5f;
            if (r.X0 < -eps || r.X1 > 1f + eps) return false;
            if (panels == null) return true;
            foreach (var p in panels)
            {
                float overlap = Math.Min(r.Y1, p.Y1) - Math.Max(r.Y0, p.Y0);
                if (overlap < MinBandOverlap || p.X1 <= p.X0) continue;
                if (r.X1 + pad > p.X0 + eps && r.X0 - pad < p.X1 - eps) return false;
            }
            return true;
        }

        /// <summary>
        /// Зсув кадру по горизонталі (частки ширини, «+» — праворуч) у межах [<paramref name="dxMin"/>; <paramref name="dxMax"/>],
        /// з яким кожен прямокутник <paramref name="must"/> вільний (<see cref="IsFree"/>), — найменший за модулем; за рівності —
        /// праворуч (детерміновано). Спершу шукає такий, з яким вільні й <paramref name="nice"/> (піднята рука, голова букера), потім
        /// лише <paramref name="must"/>. Уже вільне — 0: кадр той самий. <c>false</c> — у межах зсуву вільного місця немає (кадр
        /// лишається як є, <paramref name="dx"/> = 0). <paramref name="withNice"/> — чи вмістилось і бажане.
        /// </summary>
        public static bool Shift(IList<Box> panels, IList<Box> must, IList<Box> nice, float pad, float dxMin, float dxMax,
            out float dx, out bool withNice)
        {
            dx = 0f;
            withNice = false;
            if (must == null || must.Count == 0) return false;
            var all = new List<Box>(must);
            if (nice != null) all.AddRange(nice);
            // Кандидати: 0 і кожне положення, де якийсь прямокутник рівно торкається поля свого проміжку — найменший потрібний
            // зсув завжди серед них.
            var cand = new List<float> { 0f };
            foreach (var r in all)
                foreach (var g in Gaps(panels, r.Y0, r.Y1))
                {
                    float lo = g.Key > 0f ? g.Key + pad : 0f, hi = g.Value < 1f ? g.Value - pad : 1f;
                    if (hi - lo < r.Width) continue;
                    cand.Add(lo - r.X0);
                    cand.Add(hi - r.X1);
                }
            cand.Sort((a, b) => { int c = Math.Abs(a).CompareTo(Math.Abs(b)); return c != 0 ? c : b.CompareTo(a); });
            if (nice != null && nice.Count > 0 && First(panels, all, cand, pad, dxMin, dxMax, out dx)) { withNice = true; return true; }
            return First(panels, must, cand, pad, dxMin, dxMax, out dx);
        }

        private static bool First(IList<Box> panels, IList<Box> boxes, List<float> cand, float pad, float dxMin, float dxMax, out float dx)
        {
            foreach (float c in cand)
            {
                if (c < dxMin - 1e-6f || c > dxMax + 1e-6f) continue;
                bool ok = true;
                foreach (var b in boxes)
                    if (!IsFree(panels, b.Moved(c), pad)) { ok = false; break; }
                if (ok) { dx = c; return true; }
            }
            dx = 0f;
            return false;
        }

        /// <summary>
        /// Поворот камери по курсу (градуси; «+» — праворуч, як у Unity), щоб точка з екранним x <paramref name="x01"/> (частки
        /// ширини) опинилась на <paramref name="x01"/> + <paramref name="dx"/>. Точно для точки на горизонталі камери; з нахилом
        /// камери — наближено, тож сцена поправляє вдруге від нової проекції.
        /// </summary>
        public static float YawFor(float x01, float dx, float vfovDeg, float aspect)
        {
            double tanH = Math.Tan(vfovDeg * 0.5 * Math.PI / 180.0) * aspect;
            double a = Math.Atan((2.0 * x01 - 1.0) * tanH);
            double b = Math.Atan((2.0 * (x01 + dx) - 1.0) * tanH);
            return (float)((a - b) * 180.0 / Math.PI);
        }

        /// <summary>
        /// Обернене до <see cref="YawFor"/>: на скільки зсунеться по горизонталі точка з екранним x <paramref name="x01"/>, якщо
        /// камера повернеться на <paramref name="yawDeg"/> («+» — праворуч, точка їде ліворуч). Для меж зсуву під найбільший поворот.
        /// </summary>
        public static float ShiftForYaw(float x01, float yawDeg, float vfovDeg, float aspect)
        {
            double tanH = Math.Tan(vfovDeg * 0.5 * Math.PI / 180.0) * aspect;
            double a = Math.Atan((2.0 * x01 - 1.0) * tanH) - yawDeg * Math.PI / 180.0;
            a = Math.Max(-1.55, Math.Min(1.55, a));
            return (float)(0.5 + 0.5 * Math.Tan(a) / tanH - x01);
        }

        /// <summary>
        /// Екранний x (частки ширини) точки в просторі камери: <paramref name="right"/> — праворуч від осі, <paramref name="depth"/>
        /// — уздовж погляду (метри). Позаду камери — <see cref="float.NaN"/>.
        /// </summary>
        public static float ScreenX(float right, float depth, float vfovDeg, float aspect)
        {
            if (depth <= 1e-4f) return float.NaN;
            double tanH = Math.Tan(vfovDeg * 0.5 * Math.PI / 180.0) * aspect;
            return (float)(0.5 + 0.5 * right / (depth * tanH));
        }

        /// <summary>Екранний y (частки висоти, згори вниз) точки в просторі камери: <paramref name="up"/> — над віссю погляду.</summary>
        public static float ScreenY(float up, float depth, float vfovDeg)
        {
            if (depth <= 1e-4f) return float.NaN;
            double tanV = Math.Tan(vfovDeg * 0.5 * Math.PI / 180.0);
            return (float)(0.5 - 0.5 * up / (depth * tanV));
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
    }
}
