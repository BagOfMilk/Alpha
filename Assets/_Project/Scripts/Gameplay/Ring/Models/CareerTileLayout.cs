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
        /// Запас ряду створення на вузькій панелі: розкладка округлює кожну плитку й відступ до пікселя вгору — на 4:3
        /// (1024×768, масштаб 0,853) крок плитки 175 px замість 173,9, і четверта з 696 px ряду переносилась до ролей (кадр
        /// слоту 28, 11.11.2026). По 3 одиниці на плитку покривають до двох пікселів на плитку з масштабу 0,67.
        /// </summary>
        public const float CreateRowSlack = 12f;
        /// <summary>
        /// Повна золота плита — лише з такої ширини панелі (21:9); тиждень і створення героя. На 16:9 шість плиток поруч із повною
        /// плитою виходили по 183: на створенні «Сила +2 · Спритність −1» не ставали в рядок (три рядки — ПМ 11.11.2026 «опис — не
        /// більше двох рядків»), на тижні легенда шансів під двома рядками опису «Промо» лягала на скіс плитки (кадри слоту 28);
        /// з вужчою плитою — по 194.
        /// </summary>
        public const float FullGoldFrom = 1800f;
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
        /// <summary>
        /// Ефект частинами (бонуси ґіміка): відступ між частинами в рядку з переносом. 8, а не 10: на 4:3 (плитка 192) «Сила +2 ·
        /// Спритність −1» з 10 не ставали в рядок із запасом на рендер — «Монстр» мав три рядки (кадри слоту 28).
        /// </summary>
        public const float EffectPartGap = 8f;
        public const float ChancesGap = 6f, ChancesH = 7f, LegendGap = 3f;
        /// <summary>
        /// Правий край плитки скошений (ArenaPlate: від (w, 0) до (w − k·h, h)), тож на висоті y від верху плитки він стоїть на
        /// k·y лівіше: смуга шансів і легенда внизу, що тягнуться на всю ширину тексту, лягали на скіс — «%» легенди за краєм
        /// плитки (кадри слоту 28, 11.11.2026).
        /// </summary>
        public static float SlantK { get { return (float)Math.Tan(BrandTokens.SpaceSlantDeg * Math.PI / 180.0); } }
        /// <summary>Просвіт між правим кінцем смуги чи легенди і скошеним краєм.</summary>
        public const float SlantClear = 6f;

        /// <summary>
        /// Правий відступ рядка плитки, чий низ — на висоті <paramref name="plateY"/> від верху плитки: скіс краю на цій висоті
        /// мінус правий відступ тексту плитки, плюс просвіт; не менше 0.
        /// </summary>
        public static float SlantInset(float plateY, bool compact)
        {
            float right = compact ? CompactBodyRight : BodyRight;
            return Math.Max(0f, (float)Math.Ceiling(SlantK * plateY) - right + SlantClear);
        }

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
        /// <summary>
        /// Підказка клавіші золотої плити («ENTER») — у правому нижньому куті. Останній рядок слова, що заходить на неї і
        /// по висоті, і по ширині, — кегль меншає, доки рядок не стане над підказкою (кадри слоту 28: «КАР'ЄРУ» на «ENTER»).
        /// </summary>
        public const float GoldHintRight = 18f, GoldHintBottom = 10f, GoldHintGap = 4f;

        /// <summary>Панель вузька: золота плита вужча, плитки (тиждень, створення) — двома рядами.</summary>
        public static bool Narrow(float panelWidth) { return panelWidth < NarrowWidth; }

        /// <summary>Золота плита тижня й створення: повна лише від <see cref="FullGoldFrom"/>, інакше вузька.</summary>
        public static float GoldWidth(float panelWidth) { return panelWidth >= FullGoldFrom ? GoldW : GoldWNarrow; }

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

        /// <summary>Ряд створення: від лівого краю до золотої плити (та сама ширина плити, що на тижні).</summary>
        public static float CreateRowWidth(float panelWidth) { return RowWidth(panelWidth); }

        /// <summary>
        /// Ширина плитки ряду створення героя: широка панель — усі плитки в ряд (ґіміки й ролі); вузька — рівно
        /// <see cref="CreatePerRowNarrow"/> у ряд без росту (ґіміки, ролі другим рядом за підписом «РОЛЬ»).
        /// </summary>
        public static float CreateTileWidth(float panelWidth, int tiles)
        {
            float row = CreateRowWidth(panelWidth);
            if (Narrow(panelWidth))   // рівно чотири в ряд — із запасом на округлення до пікселя (див. CreateRowSlack)
                return (row - TileGap * CreatePerRowNarrow - CreateRowSlack) / CreatePerRowNarrow;
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
