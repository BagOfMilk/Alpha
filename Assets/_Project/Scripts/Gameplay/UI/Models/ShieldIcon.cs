using System;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Значок укриття (подача бою П4, docs/research/RT_COMBAT_PRESENTATION.md): щит, наполовину залитий —
    /// часткове укриття, залитий повністю — повне. Малюється кодом, без стороннього значка (нові асети — лише
    /// CC0 чи власні, Поправка №18.3). Чистий C#: HUD будує з маски текстуру; охоронець — <c>ShieldIconTests</c>.
    /// </summary>
    public static class ShieldIcon
    {
        public const int Transparent = 0, Outline = 1, Fill = 2;

        /// <summary>
        /// Що в клітинці (<paramref name="x"/>, <paramref name="y"/>) значка розміром <paramref name="size"/>
        /// (y = 0 — верх): прозоре, контур чи заливка. Половинний щит заливає лише ліву половину.
        /// </summary>
        public static int Cell(int x, int y, int size, bool full)
        {
            if (size <= 0 || x < 0 || y < 0 || x >= size || y >= size) return Transparent;
            float u = (x + 0.5f) / size * 2f - 1f;   // −1…1 зліва направо
            float v = (y + 0.5f) / size;              // 0…1 згори вниз
            const float top = 0.06f, bottom = 0.96f, shoulder = 0.55f, width = 0.82f;
            if (v < top || v > bottom) return Transparent;
            float halfWidth = v < shoulder ? width : width * (1f - (v - shoulder) / (bottom - shoulder));
            if (Math.Abs(u) > halfWidth) return Transparent;

            float stroke = 2.4f / size;               // ~1,2 пікселя контуру з кожного боку
            bool edge = Math.Abs(u) > halfWidth - 2f * stroke || v < top + stroke || v > bottom - 2f * stroke;
            if (edge) return Outline;
            return full || u < 0f ? Fill : Transparent;
        }
    }
}
