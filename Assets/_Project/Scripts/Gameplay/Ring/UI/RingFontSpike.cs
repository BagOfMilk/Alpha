using Game.Gameplay.Ring.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>
    /// Спайк шрифтів (ТЗ B00 §2, кадр для арт-директора): кожна роль на трьох кеглях стрічкою «Їжак Єнот Ґанок · Л — Λ ·
    /// 0123456789 · ₴». Display (Sofia) — лише ВЕЛИКИМИ (RingTheme.Caps) і ще раз курсивом (синтетичний нахил); малі
    /// кириличні — окремо в ролі Text (Fixel) для порівняння. Запуск: Ring.exe -ring-font-spike &lt;png&gt;.
    /// Службовий екран для перевірки, не частина гри: текст — сам зразок гліфів, а не повідомлення гравцеві.
    /// </summary>
    public static class RingFontSpike
    {
        private const string Sample = "Їжак Єнот Ґанок · Л — Λ · 0123456789 · ₴";
        private const string Lower = "абвгґдеєжзиіїйклмнопрстуфхцчшщьюя · Ґ Є І Ї";

        public static VisualElement Build()
        {
            var root = RingUi.Column();
            root.style.position = Position.Absolute;
            root.style.left = 0f; root.style.right = 0f; root.style.top = 0f; root.style.bottom = 0f;
            root.style.backgroundColor = RingTheme.Bg;
            root.style.paddingLeft = 24f; root.style.paddingTop = 12f;
            var sizes = new[] { BrandTokens.TypeBody, BrandTokens.TypeTitle, BrandTokens.TypeDisplay };
            foreach (RingFontRole role in System.Enum.GetValues(typeof(RingFontRole)))
            {
                bool display = RingFonts.IsDisplay(role);
                var head = RingUi.Lbl(role + (display ? " · CAPS" : ""), BrandTokens.TypeLabel, RingTheme.Gold, true);
                head.style.marginTop = 4f;
                root.Add(head);
                var row = RingUi.Row();
                row.style.alignItems = Align.FlexEnd;
                foreach (int size in sizes)
                {
                    var l = Line(display ? RingTheme.Caps(Sample) : Sample, role, size, FontStyle.Normal);
                    l.style.marginRight = 18f;
                    row.Add(l);
                }
                root.Add(row);
                if (display) root.Add(Line(RingTheme.Caps(Sample), role, BrandTokens.TypeTitle, FontStyle.Italic));
                if (role == RingFontRole.Text) root.Add(Line(Lower, role, BrandTokens.TypeTitle, FontStyle.Normal));
            }
            return root;
        }

        private static Label Line(string text, RingFontRole role, int size, FontStyle style)
        {
            var l = new Label(text);
            RingFonts.Apply(l, role);
            l.style.fontSize = size;
            l.style.color = RingTheme.Text;
            l.style.unityFontStyleAndWeight = style;
            return l;
        }
    }
}
