using System;
using System.Collections.Generic;

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
        /// <summary>Квадрат ключа компактної плитки — на верхньому краї, а не в рядку назви (ActionTile.SetCompact).</summary>
        public const float CompactKeyTop = -11f, CompactKeyRight = 14f;
        /// <summary>
        /// Галочка вибраної плитки (створення): розмір і верх. Стоїть у рядку назви ліворуч від ключа — унизу лягала на рядок ефекту
        /// (кадр 4:3).
        /// </summary>
        public const float CheckW = 24f, CheckH = 20f, CheckTop = 14f, CompactCheckRight = 50f;

        /// <summary>
        /// Правий відступ галочки. У некомпактній плитці ключ — у рядку назви (праворуч BodyRight, ширина KeyBox): галочка стоїть
        /// за ним із тим самим просвітом KeyGap. Було 50 — на 2 одиниці на квадраті ключа (кадр слоту 28, 21:9, «Герой»).
        /// </summary>
        public static float CheckRight(bool compact) { return compact ? CompactCheckRight : BodyRight + KeyBox + KeyGap; }

        /// <summary>Просвіт від назви до галочки вибору: 4 між ними й 2 запасу рендера (CareerTextFitTests: CheckClear + RenderSlack).</summary>
        public const float TitleCheckGap = 6f;

        /// <summary>
        /// Ширина назви плитки, що може бути вибраною (створення): до галочки, а не до краю — кегль меншає завчасно й не стрибає від
        /// вибору. Б4-1: «ХАРДКОРНИЦЯ» на 4:3 (плитка 192) на кеглі 17 лягала на галочку. <paramref name="bodyLeft"/> — лівий відступ
        /// тексту плитки, <paramref name="checkRight"/> — правий відступ галочки (<see cref="CheckRight"/>, <see cref="LowCheckRight"/>).
        /// </summary>
        public static float TitleWidthBeforeCheck(float tileWidth, float bodyLeft, float checkRight)
        {
            return tileWidth - checkRight - CheckW - TitleCheckGap - bodyLeft;
        }
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

        // ---- плита розвитку (підсумок матчу, кінець сезону) ----
        /// <summary>Еталонна висота панелі (розмір інтерфейсу 100 %); при 90–120 % логічна висота 1000–750.</summary>
        public const float PanelH = 900f;
        /// <summary>Верх другої плити (під стрічкою) — відлік ЗГОРИ; ширина стрічки й плит під нею.</summary>
        public const float SecondY = 162f, StripW = 410f;
        /// <summary>Лінія канатів — відлік ЗНИЗУ (низ 210, висота 22): її верх на еталонній панелі — 668.</summary>
        public const float RopesBottom = 210f, RopeH = 22f;
        public static float RopesTop(float panelH) { return panelH - RopesBottom - RopeH; }
        /// <summary>Кінець сезону: плита розвитку — під плитою ресурсів (висота 104) з просвітом 14.</summary>
        public const float SeasonDevTop = SecondY + 104f + 14f;
        /// <summary>Підсумок матчу на вузькій панелі: плита розвитку — під табличкою кінцівки (70) й ажіотажем (40).</summary>
        public const float ResultDevTopNarrow = SecondY + 132f;
        /// <summary>
        /// Просвіт плити розвитку до канатів. Плита не вища за <see cref="DevPlateMaxH"/>: коли порад більше, ніж місця, стискається
        /// лише прокрутка, а шапка й підвал («Усе (N)», рядок-підказка) лишаються цілі (кадри слоту 30, 12.11.2026: на кінці сезону
        /// край прокрутки різав «▸ Усе (19)» навпіл; на підсумку 4:3 плита лягала на канати). Плиту ставить відлік згори, канати —
        /// знизу, тож межа — від справжньої висоти панелі (розмір інтерфейсу), а не від 668.
        /// </summary>
        public const float DevRopeClear = 4f;
        public static float DevPlateMaxH(float top, float panelH)
        {
            if (float.IsNaN(panelH) || panelH <= 0f) panelH = PanelH;   // до першої розкладки — еталон
            return Math.Max(0f, RopesTop(panelH) - DevRopeClear - top);
        }
        /// <summary>
        /// Висота фаски високих плит (плити розвитку, шухляда тижня) — висота стрічки: зріз 12° лише в куті, далі край прямий (ArenaPlate
        /// SlantCapH). Зріз на всю висоту відступав на tg 12° × висота — у плити розвитку ~80 од., і заголовок «РОЗВИТОК / ЗГОРНУТИ»
        /// підсумку стояв поза заливкою над 3D-рефері (кадри 4:3 слоту 30). Вміст біля зрізаного кута відступає щонайменше на
        /// <see cref="PlateChamfer"/> — охоронець CareerTextFitTests.
        /// </summary>
        public const float PlateSlantCapH = 106f;
        /// <summary>Глибина фаски: tg 12° × <see cref="PlateSlantCapH"/> (≈22,5).</summary>
        public static float PlateChamfer { get { return SlantK * PlateSlantCapH; } }
        /// <summary>Відступ краю фаски від прямого краю плити на відстані <paramref name="d"/> від зрізаного кута (по вертикалі).</summary>
        public static float ChamferInset(float d) { return d >= PlateSlantCapH ? 0f : PlateChamfer * (1f - Math.Max(0f, d) / PlateSlantCapH); }

        // ---- шухляда тижня («Розвиток», «Витрати») ----
        /// <summary>
        /// Низ шухляди — над канатами (відлік знизу, як канати): була на 164 + 50 = 214 при канатах 210–232, і на 4:3 канат різав її
        /// низ разом із «▸ Усе (N)» (огляд слоту 30, ПМ). Ряд дій піднімає і канати, і шухляду на той самий lift.
        /// </summary>
        public const float DrawerBottom = RopesBottom + RopeH + DevRopeClear;
        /// <summary>
        /// Наліпка форми суперника («У ФОРМІ · СИЛЬНІШИЙ НА 2») звисає з низу афіші на стільки одиниць (bottom = −PosterStickerHang).
        /// Межу шухляди треба рахувати від її низу, а не від афіші: на кадрах слоту 31 (o_ready 026) плита «Розвиток» ховала низ наліпки.
        /// </summary>
        public const float PosterStickerHang = 26f;
        /// <summary>Відступ тексту пункту ShowKit.Bullet від краю колонки: квадрат 11 і проміжок 8 — під ним стають рядки контракту (№8).</summary>
        public const float BulletIndent = 19f;
        /// <summary>Просвіт між низом афіші (з наліпкою) і верхом шухляди. Було 12 — «Витрати» стояли впритул до рваного краю (огляд слоту 33, 4:3).</summary>
        public const float DrawerPosterGap = 20f;

        /// <summary>Скільки місця шухляді під афішею: від низу афіші (і наліпки, якщо є) з просвітом до низу шухляди.</summary>
        public static float DrawerRoomUnderPoster(float drawerBottomY, float posterBottomY, bool sticker)
        {
            return drawerBottomY - (posterBottomY + (sticker ? PosterStickerHang : 0f)) - DrawerPosterGap;
        }

        /// <summary>Найбільша висота шухляди (є місце над рядом дій).</summary>
        public const float DrawerMaxH = 430f;
        /// <summary>
        /// Шапка шухляди (заголовок 25 і рядок-підказка в один рядок, відступ 8) і підвал «▾ Усе (N)» за кадрами 4:3 слоту 33 — для
        /// рішення, чи влазить мінімум шухляди під афішу. Сталі, а не розкладка: дворядкова підказка при наведенні не згортає афішу.
        /// </summary>
        public const float DrawerHeadH = 40f, DrawerFootH = 36f;
        /// <summary>Прокрутка шухляди, коли під афішею тісно, — не менша за рядок кнопок.</summary>
        public const float DrawerScrollMinTight = 32f;

        /// <summary>Мінімум шухляди: шапка, прокрутка <see cref="DevScrollMinH"/> і підвал (у «Витрат» підвалу немає).</summary>
        public static float DrawerNeed(bool dev) { return DrawerHeadH + DevScrollMinH + (dev ? DrawerFootH : 0f); }

        /// <summary>
        /// Висота шухляди тижня над афішею (огляд слоту 33, ПМ 13.11.2026: «верх шухляди не вище за низ картки (із зазором), решта —
        /// прокруткою»; коротку шухляду на 4:3 в М1 прийнято). Була межа «менше 150 під афішею — до стрічки суперника»: на 4:3 з трьома
        /// рядками умов «Розвиток» ліз на картку, текст крізь текст (кадри s33_w_p11_star_4x3 006–011). Тепер шухляда не вища за місце
        /// під афішею (<paramref name="room"/>, <see cref="DrawerRoomUnderPoster"/>) і за <see cref="DrawerMaxH"/>. Якщо під афішею менше
        /// за мінімум шухляди — <paramref name="fold"/>: афіша згортає рядки умов, доки шухляда відкрита (решта — шапка, гонорар, план);
        /// на цей прохід — мінімум, наступний рахує вже від згорнутої.
        /// </summary>
        public static float DrawerMaxHeight(float room, bool dev, out bool fold)
        {
            float need = DrawerNeed(dev);
            if (float.IsNaN(room)) room = need;
            fold = room < need;
            return Math.Min(DrawerMaxH, Math.Max(room, need));
        }

        /// <summary>
        /// Шухляда над афішею з рядками умов, що згортаються (<see cref="DrawerMaxHeight"/>). Рішення «згорнути» — за ПОВНОЮ висотою
        /// афіші: зараз низ <paramref name="posterBottomNow"/>, і якщо вона згорнута (<paramref name="foldedNow"/>) — плюс сховані
        /// рядки <paramref name="detailsH"/>. Тож рішення однакове в обох станах (не коливається) і оборотне: проміжний прохід
        /// розкладки, коли ряд дій ще не на місці й шухляді здається тісно, більше не згортає картку назавжди (кадр слоту 34, a_s1 4:3:
        /// рядок «Суперник під твоє місце в картці» зник при ~198 од. під повною карткою). Висота — від низу афіші в обраному стані.
        /// </summary>
        public static float DrawerOverPoster(float drawerBottomY, float posterBottomNow, bool foldedNow, float detailsH, bool sticker, bool dev, out bool fold)
        {
            if (float.IsNaN(detailsH) || detailsH < 0f) detailsH = 0f;
            float full = posterBottomNow + (foldedNow ? detailsH : 0f);
            DrawerMaxHeight(DrawerRoomUnderPoster(drawerBottomY, full, sticker), dev, out fold);
            bool unused;
            return DrawerMaxHeight(DrawerRoomUnderPoster(drawerBottomY, fold ? full - detailsH : full, sticker), dev, out unused);
        }

        /// <summary>
        /// Найменша висота прокрутки шухляди: <see cref="DevScrollMinH"/>, а коли тісно — скільки лишають шапка й підвал, але не менше
        /// за рядок (<see cref="DrawerScrollMinTight"/>): так шапка з дворядковою підказкою не виштовхує підвал на канати.
        /// </summary>
        public static float DrawerScrollMin(float maxHeight, float headH, float footH)
        {
            if (float.IsNaN(headH) || headH <= 0f) headH = DrawerHeadH;
            if (float.IsNaN(footH) || footH < 0f) footH = 0f;
            return Math.Max(DrawerScrollMinTight, Math.Min(DevScrollMinH, maxHeight - headH - footH));
        }

        /// <summary>
        /// Афіша створення «Твій борець»: висота рядка характеристики й нижній відступ паперу (широка панель / вузька). На 4:3 вкладка
        /// «Прийоми» стоїть над піднятими канатами, а папір росте згори — з рядками по 38 рваний низ паперу заходив на вкладку
        /// (огляд слоту 31, h4x3 014: ~13 одиниць). Вузька — компактніша: п'ять рядків по 34 і відступ 16.
        /// </summary>
        public const float CreateAttrRowH = 38f, CreateAttrRowNarrowH = 34f;
        public const float CreatePaperPadBottom = 24f, CreatePaperPadBottomNarrow = 16f;
        /// <summary>Скільки одиниць висоти паперу дає компактна вузька афіша: п'ять рядків характеристик і нижній відступ.</summary>
        public static float CreatePaperNarrowSaving
        {
            get { return 5f * (CreateAttrRowH - CreateAttrRowNarrowH) + (CreatePaperPadBottom - CreatePaperPadBottomNarrow); }
        }

        /// <summary>
        /// Афіша створення: бічні відступи паперу й рядок «назва ґіміка — плита ролі». Назва — display Black 52 з розрядкою 0,01;
        /// плита ролі — слово 26 з розрядкою 0,04 і полями 24 + 22. Жіночі назви довші (ТЕХНІКИНЯ, ШОУВУМЕН — ПМ 13.11.2026,
        /// <c>RingLabels.GimmickName</c>) і на 52 наїжджали на плиту ЛИХОДІЙКА — кегль назви меншає (<see cref="FitPosterName"/>).
        /// </summary>
        public const float CreatePosterPadLeft = 26f, CreatePosterPadRight = 22f;
        public const float PosterNameSize = 52f, PosterNameTrack = 0.01f, PosterNameGap = 8f;
        /// <summary>Нижня межа кегля назви — заголовний кегль токенів: назва лишається заголовком афіші, а не підписом.</summary>
        public const float PosterNameMinSize = BrandTokens.TypeTitle;
        public const float RolePlateWordSize = 26f, RolePlateWordTrack = 0.04f, RolePlatePadLeft = 24f, RolePlatePadRight = 22f;

        /// <summary>
        /// Кегль назви ґіміка на афіші: <see cref="PosterNameSize"/>, якщо назва влазить до плити ролі, інакше менший пропорційно,
        /// не нижче <see cref="PosterNameMinSize"/>. <paramref name="naturalAtBase"/> — ширина назви на базовому кеглі (розрядка
        /// пропорційна кеглю, тож ширина лінійна); <paramref name="available"/> — місце до плити без проміжку. NaN — розкладки ще немає.
        /// </summary>
        public static float FitPosterName(float naturalAtBase, float available)
        {
            return FitOneLine(PosterNameSize, PosterNameMinSize, naturalAtBase, available);
        }

        /// <summary>Шухляда: відступи прокрутки (лівий — від смужки 6), рядок «Розвиток» і підказка — у шапці над прокруткою.</summary>
        public const float DrawerPadLeft = 18f, DrawerPadRight = 24f, DrawerScrollPadBottom = 4f;

        /// <summary>Кінець сезону: відступи шапки, прокрутки й підвалу (лівий — від смужки плити).</summary>
        public const float DevPadLeft = 20f, DevPadRight = 14f, DevHeadPadTop = 8f, DevHeadGap = 2f, DevScrollPadBottom = 4f;
        /// <summary>Підвал: перемикач «Усе (N)» і рядок-підказка — від прокрутки й до низу плити.</summary>
        public const float DevFootTop = 4f, DevFootPadBottom = 8f;
        /// <summary>Картка порад (<c>Glass(8)</c>): внутрішній відступ — підвал і число в шапці стають на ту саму вертикаль.</summary>
        public const float DevCardPad = 8f;
        /// <summary>
        /// Найменша висота прокрутки порад, коли плиті тісно (розмір інтерфейсу 120 %, 4:3): видно хоч одну пораду з однорядковим
        /// поясненням (52,7). Менше за це прокрутка не стискається — інакше плита перейшла б канати.
        /// </summary>
        public const float DevScrollMinH = 56f;
        /// <summary>Заголовок плити кінця сезону і число очок у шапці (на підсумку — 28 і 44: там шапка ще й розгортає плиту).</summary>
        public const float DevTitleSize = 25f, DevPointsSize = 28f;
        /// <summary>Підсумок матчу: відступи колонки плити розвитку (праворуч ширше — там скіс дзеркальної плити), заголовок і число очок.</summary>
        public const float ResultDevPadLeft = 24f, ResultDevPadRight = 28f, ResultDevPadTop = 10f, ResultDevPadBottom = 12f;
        public const float ResultDevTitleSize = 28f, ResultDevPointsSize = 44f;
        /// <summary>
        /// Рядок-підказка підвалу (текст 12, рядок Fixel ≈ 16,56) — лише коли розгорнуто «Усе»: підказки при наведенні є тільки в
        /// повному списку (у порад і перемикача їх немає). Він під перемикачем на всю ширину й сталої висоти —
        /// <see cref="DevFootHintLines"/> рядків: опис перка (до ~180 знаків) займав поруч із перемикачем 3–5 рядків, і підвал ріс при
        /// кожному наведенні, а прокрутка стрибала (рев'ю слоту 30). Найдовша підказка вміщається — CareerTextFitTests.
        /// </summary>
        public const int DevFootHintLines = 3;
        public const float DevFootHintLineH = 16.56f, DevFootHintPadTop = 4f;
        /// <summary>Висота рядка-підказки підвалу: відступ, рядки й поля мітки Unity (1 + 2).</summary>
        public static float DevFootHintH { get { return DevFootHintPadTop + DevFootHintLines * DevFootHintLineH + 3f; } }

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

        // ---- клин тла титулу (MenuBackdrop, B01 §2) ----
        /// <summary>
        /// Клин звужується донизу: правий край на висоті y — <c>ww · (1 − WedgeTaper · y / H)</c>, ww = ширина панелі × частка клина.
        /// </summary>
        public const float WedgeTaper = 0.38f;
        /// <summary>Частка клина титулу (права третина — рингу) і найбільша, до якої його розширює підпис на вузькій панелі.</summary>
        public const float TitleWedge = 0.46f, TitleWedgeMax = 0.62f;
        /// <summary>
        /// Темна смуга вздовж краю клина (MenuBackdrop, ACC-03): її правий край угорі — <c>WedgeInnerTop · ww</c>, унизу —
        /// <c>WedgeInnerBottom · ww</c>. Між нею й краєм клина — світліший кармін, крізь який видно ринг, і напівтон.
        /// </summary>
        public const float WedgeInnerTop = 0.86f, WedgeInnerBottom = 0.50f;
        /// <summary>
        /// Від кінця підпису титулу до краю ТЕМНОЇ СМУГИ клина — не менше. Було 40 від краю клина: на 4:3 кінець підпису «…СЕЗОНОМ»
        /// лягав на напівтон і світлий кармін, крізь який видно стійку рингу й борця, і зливався з тлом (огляд слоту 33, ПМ 13.11.2026,
        /// кадри m4x3 004–010; раніше — m4x3\053 слоту 30, коли підпис виходив за клин).
        /// </summary>
        public const float TitleTagClear = 16f;

        /// <summary>x правого краю темної смуги клина на висоті y.</summary>
        public static float WedgeInnerEdge(float panelW, float panelH, float wedgeWidth, float y)
        {
            float f = panelH <= 0f ? 0f : Math.Min(1f, Math.Max(0f, y / panelH));
            return panelW * wedgeWidth * (WedgeInnerTop + (WedgeInnerBottom - WedgeInnerTop) * f);
        }

        /// <summary>
        /// Підпис титулу на вузькій панелі (4:3, 16:10) — двома рядками: одним рядком він тягнув клин на пів екрана або лягав на
        /// напівтон (огляд слоту 33). Розрив — на тому роздільнику « · », за якого довший рядок найкоротший; сам роздільник на розриві не
        /// лишається. Широка панель, текст без роздільника — як є.
        /// </summary>
        public static string TitleTagLines(string tag, float panelW)
        {
            const string sep = " · ";
            if (string.IsNullOrEmpty(tag) || panelW <= 0f || !Narrow(panelW)) return tag;
            int best = -1, bestLen = int.MaxValue;
            for (int i = tag.IndexOf(sep, StringComparison.Ordinal); i >= 0; i = tag.IndexOf(sep, i + 1, StringComparison.Ordinal))
            {
                int longer = Math.Max(i, tag.Length - i - sep.Length);
                if (longer < bestLen) { bestLen = longer; best = i; }
            }
            return best < 0 ? tag : tag.Substring(0, best) + "\n" + tag.Substring(best + sep.Length);
        }

        /// <summary>x правого краю клина на висоті y.</summary>
        public static float WedgeEdge(float panelW, float panelH, float wedgeWidth, float y)
        {
            if (panelH <= 0f) return panelW * wedgeWidth;
            return panelW * wedgeWidth * (1f - WedgeTaper * Math.Min(1f, Math.Max(0f, y / panelH)));
        }

        /// <summary>
        /// Частка клина титулу, за якої підпис (правий край <paramref name="textRight"/>, низ <paramref name="textBottom"/>) лежить на
        /// темній смузі клина з запасом <see cref="TitleTagClear"/>: не менше <see cref="TitleWedge"/> (широкі панелі — без змін), не
        /// більше <see cref="TitleWedgeMax"/>. Смуга звужується донизу, тож межа — на висоті низу підпису.
        /// </summary>
        public static float FitTitleWedge(float panelW, float panelH, float textRight, float textBottom)
        {
            if (panelW <= 0f || panelH <= 0f || textRight <= 0f) return TitleWedge;
            float k = WedgeInnerEdge(1f, panelH, 1f, textBottom);
            float need = (textRight + TitleTagClear) / (k * panelW);
            return Math.Min(TitleWedgeMax, Math.Max(TitleWedge, need));
        }

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
        /// Ширина плитки ряду створення героя за кількістю ґіміків на екрані. До чотирьох ґіміків широка панель ставить у ряд
        /// ґіміки й дві ролі, вузька — рівно <see cref="CreatePerRowNarrow"/> у ряд без росту (ролі окремим рядом за підписом
        /// «РОЛЬ»). Від п'яти (<see cref="CreateEight"/>, Б4-1) на будь-якій панелі — ґіміки по чотири у два ряди, ролі — у колонці
        /// золотої плити.
        /// </summary>
        public static float CreateTileWidth(float panelWidth, int gimmicks)
        {
            float row = CreateRowWidth(panelWidth);
            if (Narrow(panelWidth) || CreateEight(gimmicks))   // рівно чотири в ряд — із запасом на округлення до пікселя (CreateRowSlack)
                return (row - TileGap * CreatePerRowNarrow - CreateRowSlack) / CreatePerRowNarrow;
            int n = Math.Max(1, gimmicks) + 2;
            return (row - TileGap * n) / n;
        }

        // ---- Б4-1: вісім ґіміків — ролі в колонці золотої плити (ПМ і арт-директор 14.11.2026) ----

        /// <summary>
        /// Ґіміків більше, ніж <see cref="CreateGimmicksPerRow"/>: розкладка Б4-1 — ґіміки 4 + 4 у два ряди на всіх панелях, ролі —
        /// дві низькі плити над «Почати кар'єру», вкладка «Прийоми» над піднятими канатами, рядки афіші стиснені (як на 4:3).
        /// </summary>
        public static bool CreateEight(int gimmicks) { return gimmicks > CreateGimmicksPerRow; }

        /// <summary>Між рядами плиток створення; на стільки ж (і на ряд) піднімаються канати й шапка ряду.</summary>
        public const float CreateRowsGap = 12f;

        /// <summary>Підйом канатів, шапки й вкладки «Прийоми», коли плитки створення стоять двома рядами (вузька панель чи Б4-1).</summary>
        public const float CreateLift = TileH + CreateRowsGap;

        /// <summary>
        /// Плита ролі в колонці золотої плити (Б4-1): низька — лише назва й ключ; між двома плитами — проміжок, більший за підйом
        /// плитки у фокусі (7), щоб нижня у фокусі не лягала на верхню.
        /// </summary>
        public const float CreateRoleChipH = 35f, CreateRoleChipGap = 8f;

        /// <summary>Колонка плит ролей: дві плити й проміжок між ними.</summary>
        public const float CreateRoleColumnH = 2f * CreateRoleChipH + CreateRoleChipGap;

        /// <summary>
        /// Просвіт між плитами ролей і золотою плитою (Б4-1): ряди ґіміків заввишки дві плитки й проміжок, колонка праворуч — золота
        /// плита, просвіт і дві плити ролей, тож просвіт — решта висоти. Арт-директор 14.11.2026: «кобальт не торкається золота» —
        /// просвіт не менше <see cref="CreateRoleGoldClear"/>; рядок «чому не можна почати» стоїть саме в ньому.
        /// </summary>
        public const float CreateRoleZone = TileH + CreateRowsGap - CreateRoleColumnH;

        /// <summary>Найменший просвіт між плитами ролей і золотою плитою (арт-директор 14.11.2026).</summary>
        public const float CreateRoleGoldClear = 12f;

        /// <summary>Рядок причини в просвіті Б4-1: від золотої плити — 4, згори — до нижньої плити ролі.</summary>
        public const float CreateReasonGap = 4f, CreateRoleReasonH = CreateRoleZone - CreateReasonGap;

        /// <summary>
        /// Низька плитка (плита ролі Б4-1, ActionTile.SetLow): назва компактним кеглем і ключ компактного розміру в одному рядку по
        /// центру висоти; галочка вибору — ліворуч від ключа, теж по центру.
        /// </summary>
        public const float LowBodyTop = (CreateRoleChipH - CompactKeyBox) / 2f;
        public const float LowCheckTop = (CreateRoleChipH - CheckH) / 2f;
        public const float LowCheckRight = CompactBodyRight + CompactKeyBox + KeyGap;

        // ---- Б4: вісім ґіміків на створенні (власник 13.11.2026, «Так, усі 7») ----

        /// <summary>Ґіміків в одному ряду створення (Б4: верхній ряд — чотири ґіміки й ролі, решта ґіміків — під ними).</summary>
        public const int CreateGimmicksPerRow = 4;

        /// <summary>Клавіша «0» — у плитки та в <see cref="KeyLabel"/>; цифри 1–9 — самі собою.</summary>
        public const int KeyZero = 10;

        /// <summary>Напис квадрата клавіші плитки: 1–9, <see cref="KeyZero"/> — «0», без клавіші (0 і менше) — порожньо.</summary>
        public static string KeyLabel(int key) { return key == KeyZero ? "0" : key > 0 ? key.ToString() : ""; }

        /// <summary>
        /// Клавіші ролей на створенні. Поки ґіміків не більше <see cref="CreateGimmicksPerRow"/> — 5 і 6, як у М1; з п'ятим
        /// ґіміком ролі переходять на 9 і 0 — інакше зіткнулися б із ґіміками 5–6 (Б4, власник 13.11.2026).
        /// </summary>
        public static void CreateRoleKeys(int gimmicks, out int hero, out int villain)
        {
            if (gimmicks > CreateGimmicksPerRow) { hero = 9; villain = KeyZero; }
            else { hero = 5; villain = 6; }
        }

        /// <summary>
        /// Що робить цифра на створенні: індекс ґіміка в порядку плиток (0…), <see cref="CreateDigitHero"/>,
        /// <see cref="CreateDigitVillain"/> чи <see cref="CreateDigitNone"/>. Ґіміки — 1…кількість (не більше 8), ролі —
        /// <see cref="CreateRoleKeys"/>.
        /// </summary>
        public static int CreateDigit(int n, int gimmicks)
        {
            int hero, villain;
            CreateRoleKeys(gimmicks, out hero, out villain);
            if (n == hero) return CreateDigitHero;
            if (n == villain) return CreateDigitVillain;
            if (n >= 1 && n <= Math.Min(gimmicks, 8)) return n - 1;
            return CreateDigitNone;
        }

        public const int CreateDigitNone = -1, CreateDigitHero = -2, CreateDigitVillain = -3;

        /// <summary>
        /// Порядок частин ефекту плитки ґіміка (V8 3d7345e6, ПМ 13.11.2026): у плитці з трьома й більше частинами найдовша стає
        /// останньою, решта — у звичному порядку. Тоді дві коротші стають в один рядок: у Шоумена «Сила −1 · Харизма +3» /
        /// «Витривалість +1» — два рядки замість трьох (текст плитки 164–166). Якщо найдовша вже остання (Монстр, Летун) чи частин
        /// менше трьох — порядок той самий. Довжина — кількість знаків (тут шрифту немає); що два рядки справді влазять, міряє
        /// <c>CareerTextFitTests</c> шрифтом гри. З <paramref name="values"/> (бонуси частин) серед частин, що стоять перед
        /// найдовшою, першою стає підписна — найбільший бонус (арт-директор 14.11.2026, Ш-1: «Харизма +3 · Сила −1» /
        /// «Витривалість +1» — Шоумена читаємо за Харизмою; ширина рядка та сама). Порядок без перестановки найдовшої не чіпає.
        /// </summary>
        public static List<string> CreateEffectOrder(IList<string> parts, IList<int> values = null)
        {
            var res = new List<string>();
            if (parts == null) return res;
            res.AddRange(parts);
            if (res.Count < 3) return res;
            int longest = 0;
            for (int i = 1; i < res.Count; i++) if ((res[i] ?? "").Length >= (res[longest] ?? "").Length) longest = i;   // рівні — остання
            if (longest == res.Count - 1) return res;
            var order = new List<int>();
            for (int i = 0; i < res.Count; i++) if (i != longest) order.Add(i);
            if (values != null && values.Count == res.Count)
            {
                int best = 0;   // рівні — перша
                for (int k = 1; k < order.Count; k++) if (values[order[k]] > values[order[best]]) best = k;
                int b = order[best];
                order.RemoveAt(best);
                order.Insert(0, b);
            }
            order.Add(longest);
            var sorted = new List<string>();
            foreach (int i in order) sorted.Add(res[i]);
            return sorted;
        }

        /// <summary>Клітинка шкали характеристики на афіші створення.</summary>
        public enum AttrCell { Empty = 0, Filled = 1, Negative = 2, Free = 3 }

        /// <summary>
        /// Шкала характеристики афіші: [0] — мінусовий блок, [1…max] — додатні. Основа нижче нуля — мінусовий блок сигнальний;
        /// вільні бали (до двох у Самородка, Б4) — золоті клітинки над основою: перший бал при мінусовій основі закриває мінусовий
        /// блок, решта — додатні клітинки від 1. Стеля — <paramref name="max"/>.
        /// </summary>
        public static AttrCell[] CreateAttrCells(int baseValue, int freeCount, int max)
        {
            max = Math.Max(1, max);
            freeCount = Math.Max(0, freeCount);
            var c = new AttrCell[max + 1];
            c[0] = baseValue < 0 ? (freeCount >= 1 ? AttrCell.Free : AttrCell.Negative) : AttrCell.Empty;
            int top = baseValue + freeCount;
            for (int k = 1; k <= max; k++)
                c[k] = k <= baseValue ? AttrCell.Filled : k > Math.Max(baseValue, 0) && k <= top ? AttrCell.Free : AttrCell.Empty;
            return c;
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
            return FitOneLine(baseSize, TitleMinSize, naturalAtBase, available);
        }

        /// <summary>Один рядок: базовий кегль або менший пропорційно (ціле число), не нижче <paramref name="minSize"/>.</summary>
        public static float FitOneLine(float baseSize, float minSize, float naturalAtBase, float available)
        {
            if (float.IsNaN(naturalAtBase) || float.IsNaN(available) || naturalAtBase <= 0f || available <= 0f) return float.NaN;
            if (naturalAtBase <= available) return baseSize;
            float s = (float)Math.Floor(baseSize * available / naturalAtBase);
            return Math.Max(minSize, Math.Min(baseSize, s));
        }

        /// <summary>Місце під слово золотої плити: ширина й висота без відступів.</summary>
        public static float WordWidth(float plateWidth) { return plateWidth - WordLeft - WordRight; }
        public static float WordHeight(float plateHeight) { return plateHeight - WordTop - WordBottom; }

        /// <summary>
        /// Вигляд плитки дії за станом (ActionTile.Sync) — без кольорів рушія, щоб правило тримав охоронець. Звичайна — темна зі
        /// смужкою акценту; фокус — золота заливка з білою обводкою; вибрана — заливка кольору вибору й галочка. Вибрана «краєм»
        /// (<paramref name="selectAsEdge"/>, власник 12.11.2026, №4: «Вибрана плитка «ГЕРОЙ» — темна плитка з кобальтовим краєм
        /// замість кобальтової заливки») лишається темною: смужка й обводка — кольору вибору, текст світлий; фокус поверх неї —
        /// той самий, що в усіх плиток.
        /// </summary>
        public static TileLook Look(bool enabled, bool focus, bool selected, bool selectAsEdge, bool selectDarkText)
        {
            var l = new TileLook();
            if (!enabled) { l.Fill = TileFill.Disabled; l.Stripe = TileEdge.Disabled; l.Outline = TileEdge.None; return l; }
            bool filledSelect = selected && !selectAsEdge;
            l.Fill = filledSelect ? TileFill.Select : focus ? TileFill.Gold : TileFill.Panel;
            l.Stripe = filledSelect || focus ? TileEdge.None : selected ? TileEdge.Select : TileEdge.Accent;
            l.Outline = focus ? TileEdge.Focus : selected && selectAsEdge ? TileEdge.Select : TileEdge.None;
            l.DarkText = filledSelect ? selectDarkText : focus;
            return l;
        }

        // ---- прокрутка списку розвитку: на межу рядка (ПМ 13.11.2026, п. 2 і 5) ----

        /// <summary>Допуск порівнянь розкладки — пів пікселя.</summary>
        public const float RowEps = 0.5f;

        /// <summary>
        /// Атоми вмісту (кнопки, рядки тексту) — у рядки: ті, що перекриваються по висоті, — один рядок (кнопки одного ряду з
        /// переносом стоять на одній висоті). Рядок — заголовок, лише якщо всі його атоми — заголовки. За зростанням верху.
        /// </summary>
        public static List<ScrollRow> MergeRows(IEnumerable<ScrollRow> atoms)
        {
            var list = new List<ScrollRow>();
            if (atoms != null)
                foreach (var a in atoms)
                    if (!float.IsNaN(a.Top) && !float.IsNaN(a.Bottom) && a.Bottom - a.Top > RowEps) list.Add(a);
            list.Sort((x, y) => x.Top.CompareTo(y.Top));
            var rows = new List<ScrollRow>();
            foreach (var a in list)
            {
                int last = rows.Count - 1;
                if (last >= 0 && a.Top < rows[last].Bottom - RowEps)
                {
                    var r = rows[last];
                    rows[last] = new ScrollRow(r.Top, Math.Max(r.Bottom, a.Bottom), r.Head && a.Head);
                }
                else rows.Add(a);
            }
            return rows;
        }

        /// <summary>
        /// Прокрутка на межу рядка (огляд слоту 32, ПМ 13.11.2026): верхній край вікна — початок рядка, нижній — кінець останнього
        /// цілого рядка; заголовок розділу без свого рядка під ним над краєм не лишається. Шухляда «Розвиток» на с4 (1080) щойно
        /// розгорнутим «Усе» стояла на межі прокрутки, і над «АТРИБУТИ» висів зріз «Залізна щелепа · 2»; на 4:3 нижній край різав
        /// «ПЕРКИ» під перемикачем.
        /// <paramref name="rows"/> — рядки вмісту (<see cref="MergeRows"/>), координати — від верху вмісту; <paramref name="contentH"/> —
        /// висота вмісту; <paramref name="view"/> — висота вікна прокрутки з відступом, який дав екран; <paramref name="want"/> — бажаний
        /// зсув; <paramref name="mode"/> — звідки він (<see cref="RowSnapMode"/>).
        /// Повертає зсув; <paramref name="extra"/> — скільки додати до нижнього відступу прокрутки, щоб вікно скінчилося на межі
        /// рядка (вікно коротшає; мітка нижче межі прокрутки — вікно показує від неї до кінця, решта порожня).
        /// Вміст уміщається — 0 і 0: гортати нічого.
        /// </summary>
        public static float SnapRows(IList<ScrollRow> rows, float contentH, float view, float want, RowSnapMode mode, out float extra)
        {
            extra = 0f;
            if (rows == null || rows.Count == 0 || float.IsNaN(contentH) || float.IsNaN(view) || view <= 0f || contentH <= view + RowEps) return 0f;
            if (float.IsNaN(want) || want < 0f) want = 0f;
            // відновлений зсув — не далі межі прокрутки: після покупки вміст коротшає, і колишній низ списку дав би майже порожнє вікно
            // (рев'ю 13.11.2026); мітка — навпаки, може бути нижче межі (п. 2), а зсув рукою межу вже має від самої прокрутки
            if (mode == RowSnapMode.Restore) want = Math.Min(want, contentH - view);
            float t = 0f;
            if (mode == RowSnapMode.Keep) t = want;
            else
                foreach (var r in rows)
                    if (r.Top <= want + RowEps && r.Top > t) t = r.Top;
            if (t > contentH - view + RowEps) { extra = view - (contentH - t); return t; }   // початок нижче межі — до кінця, решта порожня
            float bottom = t + view;
            int first = -1, last = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Top < t - RowEps) continue;
                if (first < 0) first = i;
                if (rows[i].Bottom <= bottom + RowEps) last = i;
                else break;
            }
            if (last < 0 || last == rows.Count - 1) return t;   // жоден рядок не вміщається цілим (вікно нижче рядка) або видно до кінця
            while (rows[last].Head && last > first) last--;       // заголовок (і заголовки поспіль) — разом зі своїм рядком
            var next = rows[last + 1];                            // перший рядок, якого не видно цілим
            if (next.Bottom - next.Top > view - RowEps) return t;  // вищий за вікно — цілим не стане ніколи: краще частина, ніж порожнеча
            if (next.Top < bottom - RowEps) extra = bottom - next.Top;
            return t;
        }
    }

    /// <summary>
    /// Звідки бажаний зсув прокрутки на межу рядка (<see cref="CareerTileLayout.SnapRows"/>): <c>Mark</c> — початок щойно розгорнутого
    /// списку (може лежати нижче межі прокрутки — тоді запас знизу); <c>Restore</c> — зсув до перебудови (не далі межі); обидва —
    /// на початок рядка. <c>Keep</c> — гравець прокрутив сам: верх не рухати, лише чистий нижній край.
    /// </summary>
    public enum RowSnapMode { Mark, Restore, Keep }

    /// <summary>Рядок у прокрутці (<see cref="CareerTileLayout.SnapRows"/>): верх і низ від верху вмісту; заголовок розділу чи ні.</summary>
    public struct ScrollRow
    {
        public float Top, Bottom;
        public bool Head;
        public ScrollRow(float top, float bottom, bool head) { Top = top; Bottom = bottom; Head = head; }
        public override string ToString() { return (Head ? "H " : "") + Top + "–" + Bottom; }
    }

    /// <summary>Заливка плитки дії: темна панель, золото фокуса, колір вибору, вимкнена (напівпрозора панель).</summary>
    public enum TileFill { Panel, Gold, Select, Disabled }

    /// <summary>Смужка чи обводка плитки: немає, акцент плитки, колір вибору, сіра вимкненої, біла фокуса.</summary>
    public enum TileEdge { None, Accent, Select, Disabled, Focus }

    /// <summary>Вигляд плитки дії за станом (<see cref="CareerTileLayout.Look"/>).</summary>
    public struct TileLook
    {
        public TileFill Fill;
        public TileEdge Stripe;
        public TileEdge Outline;
        /// <summary>Текст темний (на світлій заливці — фокус чи світла заливка вибору).</summary>
        public bool DarkText;
    }
}
