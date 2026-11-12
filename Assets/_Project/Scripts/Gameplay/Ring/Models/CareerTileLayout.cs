using System;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>
    /// Числа розкладки плиток і золотої плити екранів кар'єри (B03 v2) — одні для гри (<c>ArenaKit</c>, <c>ActionTile</c>,
    /// <c>GoldPlate</c>, екрани тижня й створення) і для охоронця переносів без Unity (<c>CareerTextFitTests</c>): той міряє
    /// справжні тексти справжніми метриками шрифтів у ширинах, які дає саме цей клас (ПМ 11.11.2026: «жодного розриву
    /// посеред слова; назва плитки — один рядок…; опис — не більше двох рядків»).
    /// Одиниці — логічні одиниці панелі: висота 900, ширина 900 × пропорція (16:9 — 1600, 4:3 — 1200, 21:9 — 2133).
    /// </summary>
    public static class CareerTileLayout
    {
        // ---- ряд ----
        public const float Edge = 80f;                 // безпечні боки ACC-04
        public const float GoldW = 273f, GoldWNarrow = 210f;
        public const float GoldGap = 14f;              // ряд плиток — золота плита
        public const float NarrowWidth = 1450f;        // вужча панель (4:3, 16:10) — золота плита вужча, плитки двома рядами
        public const float TileH = 112f;
        public const float TileGap = 9f;               // правий відступ кожної плитки
        public const float TwoRowBasis = 0.31f;        // два ряди по три (тиждень)
        /// <summary>Створення на вузькій панелі: ґіміки по стільки в ряд (ролі — другим рядом за підписом «РОЛЬ»).</summary>
        public const int CreatePerRowNarrow = 4;
        /// <summary>
        /// Створення героя: повна золота плита — лише з такої ширини панелі (21:9). На 16:9 шість плиток поруч із повною плитою
        /// виходили по 183 і «Сила +2 · Спритність −1» не ставали в рядок (три рядки — ПМ 11.11.2026 «опис — не більше двох
        /// рядків»); з вужчою плитою — по 194.
        /// </summary>
        public const float CreateFullGoldFrom = 1800f;
        /// <summary>Плитка вужча за це — компактна: назва меншим кеглем, ключ-цифра на верхньому краї (назва — на всю ширину).</summary>
        public const float CompactTileW = 230f;
        /// <summary>Скільки ряд може вирости вгору, коли тексту більше, ніж уміщає <see cref="TileH"/>.</summary>
        public const float TileGrowMax = 64f;
        /// <summary>Ряд показу (картка вечора, кінець сезону): плитка поруч із золотою плитою, ширина — широка / вузька панель.</summary>
        public const float WithdrawTileW = 300f, WithdrawTileWNarrow = 230f;
        public const float TitleSkipTileW = 340f, TitleSkipTileWNarrow = 260f;
        public const float ShowRowGap = 14f;

        // ---- плитка ----
        public const float BodyLeft = 18f, BodyRight = 22f, BodyTop = 10f, BodyBottom = 8f;
        public const float CompactBodyLeft = 12f, CompactBodyRight = 14f, CompactBodyTop = 14f;
        public const float KeyBox = 30f, KeyGap = 6f, CompactKeyBox = 24f;
        public const float TitleSize = 21f, CompactTitleSize = 17f, TitleTrack = 0.03f;
        /// <summary>Назва плитки — завжди один рядок: кегль меншає до цього, не нижче (Статут: кегль ≥ 15).</summary>
        public const float TitleMinSize = BrandTokens.TypeMinInfo;
        /// <summary>Ефект, ризик, причина — не більше стількох рядків (ПМ 11.11.2026).</summary>
        public const int BodyMaxLines = 2;
        public const float LineGap = 4f;               // між назвою й ефектом
        /// <summary>Ефект частинами (бонуси ґіміка): правий відступ кожної частини в рядку з переносом.</summary>
        public const float EffectPartGap = 10f;
        public const float ChancesGap = 6f, ChancesH = 7f, LegendGap = 3f;

        // ---- рядок-підказка в шапці ряду (опис плитки під мишею чи фокусом) ----
        /// <summary>
        /// Два повні рядки тексту 15 (рядок Fixel 15 ≈ 20,7) плюс підкладка 3 + 3; шапка ряду 42, підказка по центру виходить
        /// за неї на 3 вгору й униз — між верхом плиток і канатами місце є.
        /// </summary>
        public const float HintMaxHeight = 48f, HintPadV = 6f;
        public const int HintMaxLines = 2;

        // ---- золота плита ----
        public const float WordSize = 36f, WordMin = 26f, WordTrack = 0.01f;
        public const float WordLeft = 40f, WordRight = 16f, WordTop = 12f, WordBottom = 6f;
        /// <summary>Золота плита: слово головної дії не більше стількох рядків (ПМ: «“Перейти до вечора” тримає два рядки на 4:3»).</summary>
        public const int WordMaxLines = 2;

        /// <summary>Панель вузька: золота плита вужча, плитки (тиждень, створення) — двома рядами.</summary>
        public static bool Narrow(float panelWidth) { return panelWidth < NarrowWidth; }

        public static float GoldWidth(float panelWidth) { return Narrow(panelWidth) ? GoldWNarrow : GoldW; }

        /// <summary>Ширина ряду плиток: від лівого краю до золотої плити.</summary>
        public static float RowWidth(float panelWidth) { return panelWidth - Edge - (Edge + GoldWidth(panelWidth) + GoldGap); }

        /// <summary>Тиждень: два ряди по три, коли панель вузька й плиток більше трьох.</summary>
        public static bool WeekTwoRows(float panelWidth, int tiles) { return Narrow(panelWidth) && tiles > 3; }

        /// <summary>Ширина плитки ряду тижня (flex: усі однакові, кожна з правим відступом).</summary>
        public static float WeekTileWidth(float panelWidth, int tiles)
        {
            float row = RowWidth(panelWidth);
            int perRow = WeekTwoRows(panelWidth, tiles) ? 3 : Math.Max(1, tiles);
            return (row - TileGap * perRow) / perRow;
        }

        /// <summary>Золота плита створення героя: повна лише від <see cref="CreateFullGoldFrom"/>, інакше вузька.</summary>
        public static float CreateGoldWidth(float panelWidth) { return panelWidth >= CreateFullGoldFrom ? GoldW : GoldWNarrow; }

        /// <summary>Ряд створення: від лівого краю до золотої плити створення.</summary>
        public static float CreateRowWidth(float panelWidth) { return panelWidth - Edge - (Edge + CreateGoldWidth(panelWidth) + GoldGap); }

        /// <summary>
        /// Ширина плитки ряду створення героя: широка панель — усі плитки в ряд (ґіміки й ролі); вузька — рівно
        /// <see cref="CreatePerRowNarrow"/> у ряд без росту (ґіміки, ролі другим рядом за підписом «РОЛЬ»).
        /// </summary>
        public static float CreateTileWidth(float panelWidth, int tiles)
        {
            float row = CreateRowWidth(panelWidth);
            if (Narrow(panelWidth))   // рівно чотири в ряд — мінус одиниця запасу, щоб четверта не переносилась через округлення
                return (row - TileGap * CreatePerRowNarrow - 1f) / CreatePerRowNarrow;
            int n = Math.Max(1, tiles);
            return (row - TileGap * n) / n;
        }

        /// <summary>Ширина плитки ряду показу: <paramref name="wide"/> на широкій панелі, <paramref name="narrow"/> — на вузькій.</summary>
        public static float ShowTileWidth(float panelWidth, float wide, float narrow) { return Narrow(panelWidth) ? narrow : wide; }

        public static bool Compact(float tileWidth) { return tileWidth < CompactTileW; }

        /// <summary>Ширина тексту плитки (ефект, ризик, причина).</summary>
        public static float BodyWidth(float tileWidth)
        {
            return Compact(tileWidth) ? tileWidth - CompactBodyLeft - CompactBodyRight : tileWidth - BodyLeft - BodyRight;
        }

        /// <summary>Ширина назви: у компактній плитці — уся ширина тексту (ключ на краї), інакше — без квадрата ключа.</summary>
        public static float TitleWidth(float tileWidth, bool hasKey)
        {
            float body = BodyWidth(tileWidth);
            return Compact(tileWidth) || !hasKey ? body : body - KeyBox - KeyGap;
        }

        public static float TitleBaseSize(float tileWidth) { return Compact(tileWidth) ? CompactTitleSize : TitleSize; }

        /// <summary>
        /// Кегль назви в один рядок: природна ширина назви на базовому кеглі <paramref name="naturalAtBase"/> (з розрядкою, що
        /// пропорційна кеглю) — і менший кегль пропорційно, не нижче <see cref="TitleMinSize"/>. NaN — даних розкладки ще немає.
        /// </summary>
        public static float FitTitle(float baseSize, float naturalAtBase, float available)
        {
            if (float.IsNaN(naturalAtBase) || float.IsNaN(available) || naturalAtBase <= 0f || available <= 0f) return float.NaN;
            if (naturalAtBase <= available) return baseSize;
            float s = (float)Math.Floor(baseSize * available / naturalAtBase);
            return Math.Max(TitleMinSize, Math.Min(baseSize, s));
        }

        /// <summary>Місце під слово золотої плити: ширина й висота без відступів.</summary>
        public static float WordWidth(float plateWidth) { return plateWidth - WordLeft - WordRight; }
        public static float WordHeight(float plateHeight) { return plateHeight - WordTop - WordBottom; }
    }
}
