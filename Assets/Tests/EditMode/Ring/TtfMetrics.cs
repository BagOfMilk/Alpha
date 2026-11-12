using System;
using System.Collections.Generic;
using System.IO;
using Game.Gameplay.Ring.Models;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Ширина рядка справжнім шрифтом без Unity (охоронець переносів кар'єри, 11.11.2026): таблиці TTF <c>cmap</c> (формат 4),
    /// <c>hmtx</c>, <c>head</c>, <c>hhea</c>. Кернінгу не враховує — ширина з запасом (кернінг здебільшого стискає), тож що
    /// влазить тут, влазить і в грі. Розрядка UI Toolkit (letter-spacing) — між сусідніми літерами.
    /// </summary>
    internal sealed class TtfMetrics
    {
        private readonly Dictionary<int, int> _glyph = new Dictionary<int, int>();
        private readonly ushort[] _advance;
        private readonly int _unitsPerEm, _ascender, _descender, _lineGap;

        public string Name { get; private set; }

        private TtfMetrics(string path)
        {
            Name = Path.GetFileNameWithoutExtension(path);
            var d = File.ReadAllBytes(path);
            int tables = U16(d, 4);
            var at = new Dictionary<string, int>();
            for (int i = 0; i < tables; i++)
            {
                int r = 12 + 16 * i;
                string tag = new string(new[] { (char)d[r], (char)d[r + 1], (char)d[r + 2], (char)d[r + 3] });
                at[tag] = (int)U32(d, r + 8);
            }
            _unitsPerEm = U16(d, at["head"] + 18);
            int hhea = at["hhea"];
            _ascender = S16(d, hhea + 4); _descender = S16(d, hhea + 6); _lineGap = S16(d, hhea + 8);
            int metrics = U16(d, hhea + 34);
            _advance = new ushort[metrics];
            for (int i = 0; i < metrics; i++) _advance[i] = (ushort)U16(d, at["hmtx"] + 4 * i);
            ReadCmap(d, at["cmap"]);
            if (_glyph.Count == 0) throw new InvalidDataException(path + ": немає cmap формату 4 (Windows Unicode BMP)");
        }

        public static TtfMetrics Load(params string[] relative)
        {
            var parts = new List<string> { Application.dataPath };
            parts.AddRange(relative);
            return new TtfMetrics(Path.Combine(parts.ToArray()));
        }

        /// <summary>Літери, яких у шрифті немає (рушій підставить резервний шрифт — ширина тоді не наша).</summary>
        public List<char> Missing(string s)
        {
            var m = new List<char>();
            foreach (char c in s ?? "") if (!_glyph.ContainsKey(c) && !m.Contains(c)) m.Add(c);
            return m;
        }

        /// <summary>Ширина рядка в одиницях панелі: кегль <paramref name="size"/>, розрядка = <paramref name="track"/> × кегль.</summary>
        public float Width(string s, float size, float track = 0f)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            long units = 0;
            foreach (char c in s)
            {
                int g;
                if (!_glyph.TryGetValue(c, out g)) g = 0;
                units += _advance[Math.Min(g, _advance.Length - 1)];
            }
            return units * size / _unitsPerEm + track * size * (s.Length - 1);
        }

        /// <summary>Висота рядка (висхідна − низхідна + проміжок) для кегля.</summary>
        public float LineHeight(float size) { return (_ascender - _descender + _lineGap) * size / (float)_unitsPerEm; }

        /// <summary>
        /// Перенос, як у рушія: за пробілами, жадібно. <paramref name="widestWord"/> — найширше слово (якщо воно ширше рядка, рушій
        /// розірвав би його посеред слова). Відрізок <c>&lt;nobr&gt;…&lt;/nobr&gt;</c> (TextBreak.GlueDashes) — одне слово, теги
        /// без ширини.
        /// </summary>
        public int Lines(string s, float size, float width, float track, out float widestWord)
        {
            return Wrap(s, size, width, track, out widestWord).Count;
        }

        /// <summary>Рядки після переносу (текст без тегів) — щоб перевірити, з чого починається кожен.</summary>
        public List<string> Wrap(string s, float size, float width, float track, out float widestWord)
        {
            widestWord = 0f;
            var lines = new List<string>();
            if (string.IsNullOrEmpty(s)) return lines;
            float space = Width(" ", size, 0f) + track * size;
            float x = 0f;
            var line = new System.Text.StringBuilder();
            foreach (var word in Units(s))
            {
                float w = Width(word, size, track);
                if (w > widestWord) widestWord = w;
                if (line.Length == 0) { x = w; line.Append(word); continue; }
                if (x + space + w <= width) { x += space + w; line.Append(' ').Append(word); }
                else { lines.Add(line.ToString()); line.Clear().Append(word); x = w; }
            }
            if (line.Length > 0) lines.Add(line.ToString());
            if (lines.Count == 0) lines.Add("");
            return lines;
        }

        /// <summary>Одиниці переносу: слова за пробілами; <c>&lt;nobr&gt;слово —&lt;/nobr&gt;</c> — одна одиниця без тегів.</summary>
        public static List<string> Units(string s)
        {
            var units = new List<string>();
            System.Text.StringBuilder glued = null;
            foreach (var word in s.Split(' '))
            {
                if (glued != null)
                {
                    glued.Append(' ').Append(word);
                    if (word.EndsWith(TextBreak.Close, StringComparison.Ordinal)) { units.Add(TextBreak.Strip(glued.ToString())); glued = null; }
                    continue;
                }
                if (word.StartsWith(TextBreak.Open, StringComparison.Ordinal))
                {
                    if (word.EndsWith(TextBreak.Close, StringComparison.Ordinal)) units.Add(TextBreak.Strip(word));
                    else glued = new System.Text.StringBuilder(word);
                    continue;
                }
                if (word.Length > 0) units.Add(word);
            }
            if (glued != null) units.Add(TextBreak.Strip(glued.ToString()));
            return units;
        }

        /// <summary>
        /// Частини рядком із переносом (flex-wrap UI Toolkit, ActionTile.SetEffectParts): кожна — без переносу; кожна має лівий
        /// відступ <paramref name="gap"/>, а рядок зсунуто на нього ліворуч, тож у рядок стає Σw + gap × (n − 1).
        /// <paramref name="widest"/> — найширша частина.
        /// </summary>
        public int PartLines(IList<string> parts, float size, float width, float gap, out float widest)
        {
            widest = 0f;
            int lines = 0;
            float x = 0f;
            foreach (var p in parts)
            {
                float w = Width(p, size, 0f);
                if (w > widest) widest = w;
                if (lines == 0 || x + gap + w > width) { lines++; x = w; }
                else x += gap + w;
            }
            return lines;
        }

        private void ReadCmap(byte[] d, int cmap)
        {
            int n = U16(d, cmap + 2);
            for (int i = 0; i < n; i++)
            {
                int rec = cmap + 4 + 8 * i;
                int platform = U16(d, rec), encoding = U16(d, rec + 2);
                int sub = cmap + (int)U32(d, rec + 4);
                if (platform != 3 || (encoding != 1 && encoding != 10) || U16(d, sub) != 4) continue;
                int segs = U16(d, sub + 6) / 2;
                int ends = sub + 14, starts = ends + 2 * segs + 2, deltas = starts + 2 * segs, offsets = deltas + 2 * segs;
                for (int k = 0; k < segs; k++)
                {
                    int end = U16(d, ends + 2 * k), start = U16(d, starts + 2 * k);
                    int delta = S16(d, deltas + 2 * k), ro = U16(d, offsets + 2 * k);
                    for (int c = start; c <= end && c != 0xFFFF; c++)
                    {
                        int g;
                        if (ro == 0) g = (c + delta) & 0xFFFF;
                        else
                        {
                            g = U16(d, offsets + 2 * k + ro + 2 * (c - start));
                            if (g != 0) g = (g + delta) & 0xFFFF;
                        }
                        if (g != 0) _glyph[c] = g;
                    }
                }
                return;
            }
        }

        private static int U16(byte[] d, int i) { return (d[i] << 8) | d[i + 1]; }
        private static int S16(byte[] d, int i) { return (short)U16(d, i); }
        private static uint U32(byte[] d, int i) { return (uint)((d[i] << 24) | (d[i + 1] << 16) | (d[i + 2] << 8) | d[i + 3]); }
    }
}
