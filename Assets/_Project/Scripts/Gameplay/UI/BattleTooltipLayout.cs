namespace Game.Gameplay.UI
{
    /// <summary>
    /// Бій v2, раунд 2 (docs/COMBAT_V2.md §3, аудит знімків 25.09.2026,
    /// «підказка прилипає до лівого верхнього кута поверх «Раунд 1»»):
    /// чиста математика розміщення підказки біля курсора — де мають бути
    /// x/y, щоб панель стояла ПОРУЧ із ціллю (наведений юніт/тайл), а не в
    /// сталому куті, і НЕ лягала на верхню смугу, нижню панель дій чи
    /// журнал праворуч.
    ///
    /// Жодного типу рушія (жодного <c>UnityEngine.Rect</c>/<c>Vector2</c>) —
    /// самі float, тому <see cref="BattleHudScreenTests"/> (headless)
    /// перевіряють клемп без Unity. <see cref="BattleHudScreen.DrawCursorTooltip"/>
    /// лише підставляє екранні координати з <see cref="IBattleHudData"/> й малює
    /// готовий прямокутник.
    /// </summary>
    public static class BattleTooltipLayout
    {
        /// <summary>
        /// Прямокутник підказки: за замовчуванням трохи правіше й нижче
        /// точки-якоря (оверлей наведеного юніта чи екранна точка наведеного
        /// тайла), потім затиснутий у вільну прямокутну область
        /// [<paramref name="freeLeft"/>..<paramref name="freeRight"/>] ×
        /// [<paramref name="freeTop"/>..<paramref name="freeBottom"/>] — та,
        /// що лишається поза верхньою смугою, нижньою панеллю дій і
        /// журналом. Якщо підказка ширша/вища за вільну область — притискає
        /// до її ближнього краю, а не ламає розмір.
        /// </summary>
        public static (float x, float y) PlaceNearAnchor(
            float anchorX, float anchorY, float width, float height,
            float freeLeft, float freeTop, float freeRight, float freeBottom)
        {
            const float offsetX = 22f;
            const float offsetY = 12f;

            float x = ClampInto(anchorX + offsetX, width, freeLeft, freeRight);
            float y = ClampInto(anchorY + offsetY, height, freeTop, freeBottom);
            return (x, y);
        }

        /// <summary>
        /// Раунд 3 (знімки: «ПровідниЗастрільник орди»): розсуває підписи над
        /// юнітами по вертикалі, щоб блоки не накладались. Нижчі на екрані
        /// стоять на місці, вищі (і рівні — за порядком) піднімаються над
        /// тими, з якими перетинаються. Детерміновано: той самий вхід — той
        /// самий вихід. Повертає нові верхні межі (y згори, як у GUI).
        /// </summary>
        public static float[] ResolveVerticalOverlaps(float[] centerX, float[] top, float[] width, float height, float gap)
        {
            int n = top.Length;
            var result = (float[])top.Clone();
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            System.Array.Sort(order, (a, b) =>
            {
                int byY = top[b].CompareTo(top[a]); // нижчі на екрані — першими
                return byY != 0 ? byY : a.CompareTo(b);
            });

            var placed = new System.Collections.Generic.List<int>(n);
            foreach (int i in order)
            {
                for (int guard = 0; guard <= n; guard++)
                {
                    bool moved = false;
                    foreach (int j in placed)
                    {
                        bool overlapX = System.Math.Abs(centerX[i] - centerX[j]) < (width[i] + width[j]) * 0.5f + gap;
                        bool overlapY = System.Math.Abs(result[i] - result[j]) < height + gap;
                        if (overlapX && overlapY)
                        {
                            result[i] = result[j] - height - gap;
                            moved = true;
                        }
                    }
                    if (!moved) break;
                }
                placed.Add(i);
            }
            return result;
        }

        /// <summary>Верхній лівий кут відрізка довжини <paramref name="size"/> у межах [<paramref name="min"/>..<paramref name="max"/>], притиснутий до ближнього краю, якщо не влазить.</summary>
        private static float ClampInto(float start, float size, float min, float max)
        {
            float upperBound = max - size;
            if (upperBound < min) upperBound = min; // область вужча за підказку — притиснути до min, не ламати
            if (start < min) return min;
            if (start > upperBound) return upperBound;
            return start;
        }
    }
}
