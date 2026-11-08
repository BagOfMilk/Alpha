using UnityEngine;

namespace Game.Gameplay.Ring.UI
{
    /// <summary>
    /// Набір шрифтів «Рингу» за ролями (Статут вигляду, ТЗ B00 §2): текст — Fixel, display — Fira Sans Extra Condensed
    /// трьох ваг (статичні TTF), рукописний — Amatic SC, графіті — Rubik Spray Paint. Асет <c>Resources/Ring/RingFontSet</c>
    /// заповнює збирач (<c>Editor/RingFontAssets.cs</c>); екрани беруть шрифт лише через <see cref="RingFonts"/>.
    /// </summary>
    public sealed class RingFontSet : ScriptableObject
    {
        public Font Text;
        public Font TextBold;
        public Font Display700;
        public Font Display800;
        public Font Display900;
        public Font Hand;
        public Font Spray;
    }
}
