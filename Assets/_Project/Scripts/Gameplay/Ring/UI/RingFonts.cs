using Game.Gameplay.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>Роль шрифту (Статут вигляду, TYPE-*): display — лише ВЕЛИКИМИ (<see cref="RingTheme.Caps"/>), гроші зі знаком ₴ — лише Text.</summary>
    public enum RingFontRole { Text = 0, TextBold, Display700, Display800, Display900, Hand, Spray }

    /// <summary>
    /// Єдина точка шрифтів «Рингу» (ТЗ B00 §2): екрани не вантажать шрифт самі, а просять роль. Без асета набору (лінт, тести,
    /// старий білд) — текстовий шрифт HUD (Fixel або вбудований), тож екрани не ламаються.
    /// </summary>
    public static class RingFonts
    {
        private static RingFontSet _set;
        private static bool _tried;

        private static RingFontSet Set
        {
            get
            {
                if (!_tried) { _tried = true; _set = Resources.Load<RingFontSet>("Ring/RingFontSet"); }
                return _set;
            }
        }

        public static Font Get(RingFontRole role)
        {
            var s = Set;
            Font f = null;
            if (s != null)
            {
                switch (role)
                {
                    case RingFontRole.Text: f = s.Text; break;
                    case RingFontRole.TextBold: f = s.TextBold ?? s.Text; break;
                    case RingFontRole.Display700: f = s.Display700; break;
                    case RingFontRole.Display800: f = s.Display800 ?? s.Display700; break;
                    case RingFontRole.Display900: f = s.Display900 ?? s.Display800; break;
                    case RingFontRole.Hand: f = s.Hand; break;
                    case RingFontRole.Spray: f = s.Spray; break;
                }
            }
            return f ?? HudToolkitTheme.LoadFont();
        }

        public static bool IsDisplay(RingFontRole role)
        {
            return role == RingFontRole.Display700 || role == RingFontRole.Display800 || role == RingFontRole.Display900;
        }

        /// <summary>Призначити елементу шрифт ролі.</summary>
        public static void Apply(VisualElement e, RingFontRole role = RingFontRole.Text)
        {
            var f = Get(role);
            if (f != null) e.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(f));
        }
    }
}
