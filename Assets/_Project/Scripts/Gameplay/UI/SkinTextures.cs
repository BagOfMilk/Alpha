using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Процедурні текстури шкурки UI v2 (власник 30.09.2026: «редизайн усіх
    /// вікон… щось сучасне з відео ігр UI», орієнтир — Baldur's Gate 3).
    ///
    /// Жодного файлу-картинки (R18: шкурка — рантайм-код): рамки, зрізані
    /// кути, градієнт заливки, внутрішнє сяйво наведення й ромб-роздільник
    /// рахуються тут попіксельно один раз і далі живуть як 9-slice фон
    /// <see cref="GUIStyle"/> (<c>GUIStyle.border</c> тримає кути й рамку
    /// нерозтягнутими, розтягується лише середина).
    ///
    /// Геометрія рамки: для кожного пікселя рахується «глибина» — відстань
    /// до зовнішнього контуру з урахуванням зрізаних під 45° кутів. Шари за
    /// глибиною: 1 px темний контур → бронзова лінія → 1 px внутрішня темна
    /// лінія → (сяйво) → заливка. Зовнішній край згладжений часткою пікселя.
    /// </summary>
    public static class SkinTextures
    {
        /// <summary>Опис однієї рамки. Кольори з альфою; шар з альфою 0 фактично пропускається.</summary>
        public struct Frame
        {
            public int Size;
            public int Chamfer;
            public Color32 Outer;
            public Color32 Line;
            public int LineWidth;
            public Color32 Inner;
            public Color32 FillTop;
            public Color32 FillBottom;
            public Color32 Glow;
            public int GlowWidth;
            public bool CornerStuds;
            public Color32 Stud;
        }

        /// <summary>Скільки пікселів з кожного краю не розтягувати (кути + рамка + сяйво + шпильки).</summary>
        public static int SliceBorder(Frame f)
        {
            int ring = 2 + f.LineWidth + (f.GlowWidth > 0 ? f.GlowWidth : 0);
            int corner = f.Chamfer + ring;
            if (f.CornerStuds && StudCenter(f) + 4 > corner) corner = StudCenter(f) + 4;
            return corner + 1;
        }

        public static Texture2D Build(Frame f)
        {
            int s = f.Size;
            var pixels = new Color[s * s];
            float lineEnd = 1f + f.LineWidth;
            float innerEnd = lineEnd + 1f;
            int stud = StudCenter(f);

            for (int y = 0; y < s; y++)
            {
                float t = s > 1 ? y / (float)(s - 1) : 0f; // y = 0 — низ текстури
                Color fill = Lerp(f.FillBottom, f.FillTop, t);

                for (int x = 0; x < s; x++)
                {
                    float d = Depth(x + 0.5f, y + 0.5f, s, f.Chamfer);
                    Color c;
                    if (d <= 0f) c = new Color(0f, 0f, 0f, 0f);
                    else if (d < 1f) c = Over(fill, f.Outer);
                    else if (d < lineEnd) c = Over(fill, f.Line);
                    else if (d < innerEnd) c = Over(fill, f.Inner);
                    else
                    {
                        c = fill;
                        if (f.GlowWidth > 0 && d < innerEnd + f.GlowWidth)
                        {
                            float k = 1f - (d - innerEnd) / f.GlowWidth;
                            c = Mix(c, f.Glow, k * (f.Glow.a / 255f));
                        }
                    }

                    if (f.CornerStuds && d > innerEnd && IsStud(x, y, s, stud)) c = Over(c, f.Stud);

                    // Згладжування зовнішнього краю: часткове покриття пікселя.
                    if (d > 0f && d < 1f) c.a *= d;
                    pixels[y * s + x] = c;
                }
            }

            return Upload(s, s, pixels);
        }

        /// <summary>
        /// Горизонтальна лінія роздільника: найяскравіша посередині, згасає до
        /// прозорої на кінцях (розтягується по ширині — згасання теж).
        /// </summary>
        public static Texture2D FadeLine(Color32 color)
        {
            const int w = 64;
            var pixels = new Color[w];
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float edge = u < 0.5f ? u : 1f - u;   // 0 на краях, 0.5 посередині
                Color c = color;
                c.a *= Clamp01(edge * 4f);            // повна яскравість з чверті довжини
                pixels[x] = c;
            }
            return Upload(w, 1, pixels);
        }

        /// <summary>
        /// Ромб-«ружа» посередині роздільника: світлий контур, темна середина,
        /// крапка в центрі — мотив вишивки, наша відмінність від готичних скоб
        /// орієнтира. Гліфа ◆ у Fixel немає, тому текстура.
        /// </summary>
        public static Texture2D Diamond(int size, Color32 outline, Color32 fill, Color32 dot)
        {
            var pixels = new Color[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float m = Abs(x - c) + Abs(y - c); // манхеттенська відстань — ромб
                    Color px;
                    if (m > c + 0.5f) px = new Color(0f, 0f, 0f, 0f);
                    else if (m > c - 1.2f) px = outline;
                    else if (m <= 1.1f) px = dot;
                    else px = fill;
                    if (m > c - 0.5f && m <= c + 0.5f) px.a *= c + 0.5f - m;
                    pixels[y * size + x] = px;
                }
            return Upload(size, size, pixels);
        }

        /// <summary>Суцільна заливка 2×2 — підкладки й тонування через <c>GUI.color</c>.</summary>
        public static Texture2D Solid(Color32 color)
        {
            Color c = color;
            return Upload(2, 2, new[] { c, c, c, c });
        }

        // ================= внутрішнє =================

        private static int StudCenter(Frame f) => f.Chamfer / 2 + f.LineWidth + 5;

        /// <summary>Чотири маленькі ромби (радіус 2) у кутах, всередині рамки.</summary>
        private static bool IsStud(int x, int y, int s, int center)
        {
            int dx = x - center; if (dx < 0) dx = -dx;
            int dxr = x - (s - 1 - center); if (dxr < 0) dxr = -dxr;
            int dy = y - center; if (dy < 0) dy = -dy;
            int dyr = y - (s - 1 - center); if (dyr < 0) dyr = -dyr;
            int mx = dx < dxr ? dx : dxr;
            int my = dy < dyr ? dy : dyr;
            return mx + my <= 2;
        }

        /// <summary>Відстань від точки до контуру квадрата зі зрізаними під 45° кутами.</summary>
        private static float Depth(float px, float py, int s, int chamfer)
        {
            float l = px, r = s - px, b = py, t = s - py;
            float d = Min(Min(l, r), Min(b, t));
            if (chamfer > 0)
            {
                const float inv = 0.70710678f;
                d = Min(d, (l + b - chamfer) * inv);
                d = Min(d, (r + b - chamfer) * inv);
                d = Min(d, (l + t - chamfer) * inv);
                d = Min(d, (r + t - chamfer) * inv);
            }
            return d;
        }

        private static Texture2D Upload(int w, int h, Color[] pixels)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        /// <summary>Шар поверх заливки з урахуванням його альфи.</summary>
        private static Color Over(Color under, Color32 layer)
        {
            float a = layer.a / 255f;
            Color l = layer;
            return new Color(
                under.r + (l.r - under.r) * a,
                under.g + (l.g - under.g) * a,
                under.b + (l.b - under.b) * a,
                under.a + (1f - under.a) * a);
        }

        private static Color Mix(Color a, Color32 b, float k)
        {
            Color bc = b;
            k = Clamp01(k);
            return new Color(a.r + (bc.r - a.r) * k, a.g + (bc.g - a.g) * k, a.b + (bc.b - a.b) * k, a.a);
        }

        private static Color Lerp(Color32 a, Color32 b, float t)
        {
            Color ac = a, bc = b;
            t = Clamp01(t);
            return new Color(ac.r + (bc.r - ac.r) * t, ac.g + (bc.g - ac.g) * t, ac.b + (bc.b - ac.b) * t, ac.a + (bc.a - ac.a) * t);
        }

        private static float Min(float a, float b) => a < b ? a : b;
        private static float Abs(float v) => v < 0f ? -v : v;
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
