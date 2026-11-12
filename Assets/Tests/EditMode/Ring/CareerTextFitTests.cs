using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Gameplay.Ring.Models;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Охоронець переносів екранів кар'єри без Unity (ПМ 11.11.2026, кадри збірки 27: «ВІДНОВЛЕНН/Я», описи на смужці шансів,
    /// травма за плиткою, «ВЕЧОРА» за кнопкою на 4:3). Правило ПМ для всіх плиток і кнопок кар'єри:
    /// жодного розриву посеред слова; назва плитки — один рядок (кегль ≥ 15); опис — не більше двох рядків, смуга шансів —
    /// у потоці; травма — в межах плитки; золота плита — не більше двох рядків.
    /// Міряє справжні тексти (<c>ui_uk.json</c>, найгірші числа) справжніми шрифтами (TTF у <c>ThirdParty/Fonts</c>,
    /// <see cref="TtfMetrics"/>) у ширинах, які дає та сама модель розкладки, що й гра (<see cref="CareerTileLayout"/>),
    /// на 16:9 (1080 і 720 — ті самі одиниці), 16:10, 4:3 і 21:9.
    /// </summary>
    public class CareerTextFitTests
    {
        private static readonly KeyValuePair<string, float>[] Panels =
        {
            new KeyValuePair<string, float>("16:9", 1600f),
            new KeyValuePair<string, float>("16:10", 1440f),
            new KeyValuePair<string, float>("4:3", 1200f),
            new KeyValuePair<string, float>("21:9", 2133f),
        };

        private const float Body = BrandTokens.TypeMinInfo;
        /// <summary>
        /// Запас на рендер тексту (растр гліфів, округлення до пікселя): сума ширин гліфів шрифту — не точно те, що ставить
        /// TextCore, тож рядок, що в моделі влазить «впритул», у грі може перенестись.
        /// </summary>
        private const float RenderSlack = 2f;

        /// <summary>Ширина тексту плитки з запасом на рендер.</summary>
        private static float TextWidth(float tileW) { return CareerTileLayout.BodyWidth(tileW) - RenderSlack; }

        private static TtfMetrics _title, _legend, _word, _text;
        private static Dictionary<string, object> _ui;
        private static RingText _t;

        /// <summary>Тексти гри так, як їх бачить екран (RingText — для спільних функцій на кшталт RingLabels.AttrBonuses).</summary>
        private static RingText T { get { return _t ?? (_t = RingText.FromJson(CareerKit.Read("ui_uk.json"))); } }

        private static TtfMetrics TitleFont { get { return _title ?? (_title = Font("FiraSansExtraCondensed-ExtraBold")); } }   // Display800
        private static TtfMetrics LegendFont { get { return _legend ?? (_legend = Font("FiraSansExtraCondensed-Bold")); } }     // Display700
        private static TtfMetrics WordFont { get { return _word ?? (_word = Font("FiraSansExtraCondensed-Black")); } }          // Display900
        private static TtfMetrics TextFont { get { return _text ?? (_text = TtfMetrics.Load("ThirdParty", "Fonts", "Fixel", "FixelText-Regular.ttf")); } }

        private static TtfMetrics Font(string file) { return TtfMetrics.Load("ThirdParty", "Fonts", "FiraSansExtraCondensed", file + ".ttf"); }

        private static string Ui(string key)
        {
            if (_ui == null) _ui = MiniJson.ParseObject(CareerKit.Read("ui_uk.json"));
            object v;
            Assert.IsTrue(_ui.TryGetValue(key, out v), "немає ключа " + key + " в ui_uk.json");
            return (string)v;
        }

        private static string Ui(string key, params object[] args) { return string.Format(CultureInfo.InvariantCulture, Ui(key), args); }

        private static string Caps(string s) { return s.ToUpperInvariant(); }

        // ---------------- тиждень ----------------

        /// <summary>Шість дій тижня (як у WeekScreenView) — назва і всі можливі тексти ефекту, ризику й причини, найгіршими числами.</summary>
        private static List<TileText> WeekTiles()
        {
            int job = CareerRules.SideJobBase + 5 * WrestlerStats.MaxAttr;
            var all = new List<TileText>();
            var spent = new[] { Ui("c.hub.no_actions"), Ui("c.hub.need_money", 999) };
            all.Add(new TileText(Ui("week.action.0"), new[] { Ui("c.week.pay.train", CareerRules.TrainingPoints) }, spent));
            all.Add(new TileText(Ui("c.week.train_coach"),
                new[] { Ui("c.week.pay.coach", CareerRules.TrainingPoints + CareerRules.TrainerExtraPoints, CareerRules.TrainerCost) }, spent));
            var injuries = new List<string>(spent);
            for (int z = 0; z < 3; z++) injuries.Add(Ui("c.injury.tile", Ui("bodyzone." + z), 9, 99));
            all.Add(new TileText(Ui("week.action.1"), new[] { Ui("c.week.pay.recover", CareerRules.RecoverZoneHeal), Ui("c.week.pay.healthy") }, injuries.ToArray()));
            all.Add(new TileText(Ui("week.action.2"), new[] { Ui("c.week.pay.promo") }, spent) { Chances = true });
            all.Add(new TileText(Ui("week.action.3"), new[] { Ui("c.week.pay.backstage") }, spent) { Chances = true });
            all.Add(new TileText(Ui("week.action.4"), new[] { Ui("c.hub.pay.job", job) }, new[] { Ui("c.hub.risk", 100), spent[0], spent[1] }));
            return all;
        }

        private static List<TileText> PromoRewardTiles()
        {
            return new List<TileText>
            {
                new TileText(Ui("week.promo.reward.0"), new[] { "" }, new string[0]),
                new TileText(Ui("week.promo.reward.1"), new[] { "" }, new string[0]),
            };
        }

        [Test]
        public void Week_TileTitles_FitOneLine_AtMinSize_OnEveryPanel()
        {
            var bad = new List<string>();
            foreach (var set in new[] { WeekTiles(), PromoRewardTiles() })
                foreach (var p in Panels)
                {
                    float tileW = CareerTileLayout.WeekTileWidth(p.Value, set.Count);
                    foreach (var t in set) CheckTitle(bad, "тиждень " + p.Key, t.Title, tileW, true);
                }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void Week_TileBodies_TwoLinesAtMost_NoWordBreak_OnEveryPanel()
        {
            var bad = new List<string>();
            foreach (var set in new[] { WeekTiles(), PromoRewardTiles() })
                foreach (var p in Panels)
                {
                    float tileW = CareerTileLayout.WeekTileWidth(p.Value, set.Count);
                    foreach (var t in set) CheckBody(bad, "тиждень " + p.Key, t, tileW);
                }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void Week_ChanceLegend_FitsOneLine_ForEveryModifier()
        {
            var bad = new List<string>();
            float gap = 2f * 4f;   // між трьома підписами легенди — хоч по 4 одиниці
            for (int mod = -6; mod <= 9; mod++)
            {
                var c = TwoD6.ChancesOf36(mod);
                string a = Caps(Ui("c.odds.miss", Pct(c[0]))), b = Caps(Ui("c.odds.partial", Pct(c[1]))), d = Caps(Ui("c.odds.strong", Pct(c[2])));
                float w = LegendFont.Width(a, Body) + LegendFont.Width(b, Body) + LegendFont.Width(d, Body) + gap;
                foreach (var p in Panels)
                {
                    // легенда — від лівого краю тексту до скосу правого краю на висоті її низу (ActionTile.FitSlant), кожна
                    // плитка зі смугою — своїм відступом; підписи без переносу, тож і тут запас на рендер
                    float tileW = CareerTileLayout.WeekTileWidth(p.Value, 6);
                    foreach (var t in WeekTiles())
                    {
                        if (!t.Chances) continue;
                        float body = CareerTileLayout.BodyWidth(tileW) - LegendInset(t, tileW) - RenderSlack;
                        if (w > body) bad.Add(string.Format("{0}, «{1}»: легенда «{2} · {3} · {4}» {5:0} > {6:0}", p.Key, t.Title, a, b, d, w, body));
                    }
                }
                foreach (var s in new[] { a, b, d })
                    if (LegendFont.Missing(s).Count > 0) bad.Add("у шрифті легенди немає «" + new string(LegendFont.Missing(s).ToArray()) + "»");
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>Низ легенди шансів від верху плитки: смуга з легендою є лише в увімкненої плитки — рядка причини тоді немає.</summary>
        private static float LegendBottom(TileText t, float tileW)
        {
            return Height(t, tileW, true, false) - CareerTileLayout.BodyBottom;
        }

        /// <summary>Правий відступ смуги шансів і легенди плитки (як ActionTile.FitSlant).</summary>
        private static float LegendInset(TileText t, float tileW)
        {
            return CareerTileLayout.SlantInset(LegendBottom(t, tileW), CareerTileLayout.Compact(tileW));
        }

        /// <summary>
        /// Найменший видимий просвіт між правим кінцем легенди і скошеним краєм плитки на висоті низу легенди (кадри слоту 28:
        /// «%» за краєм фокусованої плитки). Число тесту, а не моделі: модель (<see cref="CareerTileLayout.SlantClear"/>,
        /// <see cref="CareerTileLayout.SlantInset"/>) може змінитись, просвіт — ні.
        /// </summary>
        private const float MinGapToSlant = 4f;

        [Test]
        public void Week_ChanceLegend_StaysOffTheSlantedEdge()
        {
            var bad = new List<string>();
            foreach (var p in Panels)
            {
                float tileW = CareerTileLayout.WeekTileWidth(p.Value, 6);
                bool compact = CareerTileLayout.Compact(tileW);
                float right = compact ? CareerTileLayout.CompactBodyRight : CareerTileLayout.BodyRight;
                foreach (var t in WeekTiles())
                {
                    if (!t.Chances) continue;
                    float y = LegendBottom(t, tileW);
                    float end = tileW - right - LegendInset(t, tileW);   // SpaceBetween: останній підпис кінчається на краю ряду
                    float edge = tileW - (float)Math.Tan(BrandTokens.SpaceSlantDeg * Math.PI / 180.0) * y;
                    if (edge - end < MinGapToSlant - 0.01f)
                        bad.Add(string.Format("{0}: «{1}» — легенда до {2:0.0}, скіс на {3:0.0}: просвіт {4:0.0} < {5}", p.Key, t.Title, end, edge, edge - end, MinGapToSlant));
                }
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Ряд створення на вузькій панелі: чотири плитки в ряд і після округлення до пікселя. Розкладка округлює ширину кожної
        /// плитки й відступу вгору — на 4:3 (1024×768) з запасом в одну одиницю крок був 175 px, і четверта з 696 px ряду
        /// переносилась до ролей (кадр слоту 28). Масштаби — вузькі панелі: 4:3 від 800×600 до 1600×1200, 16:10 від 1280×800.
        /// </summary>
        [Test]
        public void Create_NarrowRow_FourTilesFit_AfterPixelRounding()
        {
            var bad = new List<string>();
            var screens = new[] { new[] { 800, 600 }, new[] { 1024, 768 }, new[] { 1280, 960 }, new[] { 1600, 1200 },
                new[] { 1280, 800 }, new[] { 1440, 900 }, new[] { 1680, 1050 }, new[] { 1920, 1200 } };
            int tiles = CreateTiles().Count;
            foreach (var s in screens)
            {
                float scale = s[1] / 900f, panel = s[0] / scale;
                if (!CareerTileLayout.Narrow(panel)) continue;
                float row = CareerTileLayout.CreateRowWidth(panel), tileW = CareerTileLayout.CreateTileWidth(panel, tiles);
                int rowPx = (int)Math.Floor(row * scale);
                int stepPx = (int)Math.Ceiling(tileW * scale - 0.001f) + (int)Math.Ceiling(CareerTileLayout.TileGap * scale - 0.001f);
                if (stepPx * CareerTileLayout.CreatePerRowNarrow > rowPx)
                    bad.Add(string.Format("{0}×{1}: {2} плитки по {3} px > ряд {4} px", s[0], s[1], CareerTileLayout.CreatePerRowNarrow, stepPx, rowPx));
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void Week_TileHeight_FitsTheGrowLimit()
        {
            var bad = new List<string>();
            foreach (var p in Panels)
            {
                float tileW = CareerTileLayout.WeekTileWidth(p.Value, 6);
                foreach (var t in WeekTiles())
                {
                    float h = NeededHeight(t, tileW);
                    if (h > CareerTileLayout.TileH + CareerTileLayout.TileGrowMax)
                        bad.Add(string.Format("{0}: «{1}» потребує {2:0} > {3:0}", p.Key, t.Title, h, CareerTileLayout.TileH + CareerTileLayout.TileGrowMax));
                }
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        // ---------------- рядок-підказка шапки ----------------

        /// <summary>
        /// Найвужча підказка шапки тижня: 4:3, очки розвитку двоцифрові — аудит 11.11.2026 виміряв 355,7 (ряд мінус назва «ДІЇ
        /// ТИЖНЯ», крапки дій, «лишилось», дві вкладки); беремо з запасом. Підказка створення — 665,6 на 16:9 (ряд ґіміків мінус
        /// «ҐІМІК»), теж із запасом. Обидва числа — не з моделі (шапку збирає екран), тож це оцінка, а не точна межа.
        /// </summary>
        private const float WeekHintNarrowest = 340f, CreateHintNarrowest = 650f;
        /// <summary>
        /// Шапка тижня, коли треба обрати нагороду за промо: крапок дій і «лишилось» немає — аудит виміряв 515,9 на 4:3; саме тоді
        /// підказка може бути двома текстами разом («промо наполовину» + «грошей на життя не було»), як у WeekScreenView.
        /// </summary>
        private const float WeekHintPromoNarrowest = 500f;

        [Test]
        public void Hints_TwoFullLinesAtMost_OnTheNarrowestHeader()
        {
            var bad = new List<string>();
            float box = CareerTileLayout.HintMaxLines * TextFont.LineHeight(Body) + CareerTileLayout.HintPadV;
            if (box > CareerTileLayout.HintMaxHeight + 0.5f)
                bad.Add(string.Format("підказка: {0} рядки = {1:0.0} > HintMaxHeight {2:0}", CareerTileLayout.HintMaxLines, box, CareerTileLayout.HintMaxHeight));
            var week = new List<string>
            {
                Ui("week.action.0.desc"), Ui("c.week.train_coach.desc"), Ui("week.action.1.desc"), Ui("week.action.2.desc"),
                Ui("week.action.3.desc"), Ui("week.action.4.desc"), Ui("week.promo.reward.0"), Ui("week.promo.reward.1"),
                Ui("c.week.promo_choice"), Ui("week.strained"),
            };
            foreach (var h in week) CheckLines(bad, "тиждень, підказка", h, WeekHintNarrowest, CareerTileLayout.HintMaxLines);
            CheckLines(bad, "тиждень, вибір нагороди й перевтома разом", Ui("c.week.promo_choice") + " " + Ui("week.strained"),
                WeekHintPromoNarrowest, CareerTileLayout.HintMaxLines);
            var create = new List<string> { Ui("role.hero.desc"), Ui("role.villain.desc") };
            foreach (var g in CareerKit.Content().Gimmicks) create.Add(Ui(g.NameKey + ".desc"));
            foreach (var h in create) CheckLines(bad, "створення, підказка", h, CreateHintNarrowest, CareerTileLayout.HintMaxLines);
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        private static void CheckLines(List<string> bad, string where, string s, float width, int maxLines)
        {
            float widest;
            int lines = TextFont.Lines(s, Body, width, 0f, out widest);
            if (lines > maxLines) bad.Add(string.Format("{0}: «{1}» — {2} рядки у {3:0}", where, s, lines, width));
            if (widest > width) bad.Add(string.Format("{0}: у «{1}» слово ширше рядка", where, s));
            if (TextFont.Missing(s).Count > 0) bad.Add(where + ": у шрифті тексту немає «" + new string(TextFont.Missing(s).ToArray()) + "»");
        }

        // ---------------- створення героя ----------------

        /// <summary>Плитки створення (як у CreateScreenView): ґіміки з бонусами характеристик тією самою функцією, що й гра, і дві ролі.</summary>
        private static List<TileText> CreateTiles()
        {
            var all = new List<TileText>();
            foreach (var g in CareerKit.Content().Gimmicks)
                all.Add(new TileText(Ui(g.NameKey), new[] { "" }, new string[0]) { Parts = RingLabels.AttrBonusParts(T, g.Attr) });
            all.Add(new TileText(Ui("role.hero"), new[] { "" }, new string[0]));
            all.Add(new TileText(Ui("role.villain"), new[] { "" }, new string[0]));
            return all;
        }

        [Test]
        public void Create_Tiles_TitleOneLine_BodyTwoLines_FitFixedHeight_OnEveryPanel()
        {
            var bad = new List<string>();
            var tiles = CreateTiles();
            foreach (var p in Panels)
            {
                float tileW = CareerTileLayout.CreateTileWidth(p.Value, tiles.Count);
                foreach (var t in tiles)
                {
                    CheckTitle(bad, "створення " + p.Key, t.Title, tileW, true);
                    CheckBody(bad, "створення " + p.Key, t, tileW);
                    float h = NeededHeight(t, tileW);   // ряд створення не росте: плитка — рівно TileH
                    if (h > CareerTileLayout.TileH)
                        bad.Add(string.Format("створення {0}: «{1}» потребує {2:0} > {3:0}", p.Key, t.Title, h, CareerTileLayout.TileH));
                }
                foreach (var t in tiles)
                    if (t.Parts != null)
                        foreach (var e in t.Parts)
                            if (e.IndexOf('⟦') >= 0) bad.Add("створення: немає тексту ключа в «" + e + "»");   // RingText.Get без ключа дає «⟦ключ⟧»
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Галочка вибраної плитки створення (кадр слоту 28, 21:9: на «Герой» галочка лягала на квадрат ключа на 2 одиниці): на
        /// кожній панелі, у компактній і некомпактній плитці, галочка не торкається ні квадрата ключа, ні назви на її кеглі (назва
        /// вужчає до 15, як у ActionTile.FitTitle). Просвіт — <see cref="CheckClear"/>, число тесту.
        /// </summary>
        [Test]
        public void Create_SelectedCheck_ClearsTheKeyBox_AndTheTitle_OnEveryPanel()
        {
            var bad = new List<string>();
            var tiles = CreateTiles();
            var widths = new List<KeyValuePair<string, float>>();
            foreach (var p in Panels) widths.Add(new KeyValuePair<string, float>(p.Key, CareerTileLayout.CreateTileWidth(p.Value, tiles.Count)));
            // обидва стани розкладки, навіть якщо жодна панель зі списку не дає компактної плитки
            widths.Add(new KeyValuePair<string, float>("компактна", CareerTileLayout.CompactTileW - 1f));
            widths.Add(new KeyValuePair<string, float>("некомпактна", CareerTileLayout.CompactTileW));
            foreach (var p in widths)
            {
                float w = p.Value;
                bool compact = CareerTileLayout.Compact(w);
                float checkR = w - CareerTileLayout.CheckRight(compact), checkL = checkR - CareerTileLayout.CheckW;
                float checkT = CareerTileLayout.CheckTop, checkB = checkT + CareerTileLayout.CheckH;
                float keyR, keyL, keyT, keyB;
                if (compact)
                {
                    keyR = w - CareerTileLayout.CompactKeyRight; keyL = keyR - CareerTileLayout.CompactKeyBox;
                    keyT = CareerTileLayout.CompactKeyTop; keyB = keyT + CareerTileLayout.CompactKeyBox;
                }
                else
                {
                    keyR = w - CareerTileLayout.BodyRight; keyL = keyR - CareerTileLayout.KeyBox;
                    keyT = CareerTileLayout.BodyTop; keyB = keyT + CareerTileLayout.KeyBox;
                }
                bool rowsMeet = checkT < keyB + CheckClear && keyT < checkB + CheckClear;
                bool colsMeet = checkL < keyR + CheckClear && keyL < checkR + CheckClear;
                if (rowsMeet && colsMeet)
                    bad.Add(string.Format("{0} (плитка {1:0}): галочка {2:0}–{3:0} × {4:0}–{5:0} ближче {6:0} до ключа {7:0}–{8:0} × {9:0}–{10:0}",
                        p.Key, w, checkL, checkR, checkT, checkB, CheckClear, keyL, keyR, keyT, keyB));
                float left = compact ? CareerTileLayout.CompactBodyLeft : CareerTileLayout.BodyLeft;
                float baseSize = CareerTileLayout.TitleBaseSize(w), avail = CareerTileLayout.TitleWidth(w, true);
                foreach (var t in tiles)
                {
                    string title = Caps(t.Title);
                    float size = CareerTileLayout.FitTitle(baseSize, TitleFont.Width(title, baseSize, CareerTileLayout.TitleTrack), avail);
                    float right = left + TitleFont.Width(title, size, CareerTileLayout.TitleTrack) + RenderSlack;
                    if (right > checkL - CheckClear)
                        bad.Add(string.Format("{0} (плитка {1:0}): назва «{2}» на кеглі {3:0} сягає {4:0}, галочка — від {5:0}", p.Key, w, title, size, right, checkL));
                }
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>Найменший просвіт галочки до ключа й до назви — число тесту, не моделі.</summary>
        private const float CheckClear = 4f;

        // ---------------- папір кінця сезону ----------------

        private static TtfMetrics _semi;
        /// <summary>Текст жирним (MenuKit.Body bold → RingFontRole.TextBold → FixelText-SemiBold, RingFontAssets).</summary>
        private static TtfMetrics SemiFont { get { return _semi ?? (_semi = TtfMetrics.Load("ThirdParty", "Fonts", "Fixel", "FixelText-SemiBold.ttf")); } }

        /// <summary>Розміри паперу кінця сезону — як у SeasonEndScreenView.Report (ширина 520, відступи 30/26, 16/24).</summary>
        private const float PaperW = 520f, PaperPadL = 30f, PaperPadR = 26f, PaperPadT = 16f, PaperPadB = 24f;
        /// <summary>Між низом паперу й канатами — не менше (число тесту).</summary>
        private const float PaperClear = 10f;

        private static float Para(string s, float size, float width, out int lines)
        {
            float widest;
            lines = Math.Max(1, SemiFont.Lines(s, size, width, 0f, out widest));
            return lines * SemiFont.LineHeight(size);
        }

        private static float Para(string s, float size, float width) { int n; return Para(s, size, width, out n); }

        /// <summary>Пункт ShowKit.Bullet: відступ 4, квадрат 11 + 8 ліворуч, текст 15 жирним.</summary>
        private static float BulletH(string s, float col) { return 4f + Math.Max(4f + 11f, Para(s, BrandTokens.TypeMinInfo, col - 19f)); }

        private static string Longest(params string[] ss)
        {
            string best = "";
            foreach (var s in ss) if (SemiFont.Width(s, BrandTokens.TypeMinInfo) > SemiFont.Width(best, BrandTokens.TypeMinInfo)) best = s;
            return best;
        }

        /// <summary>
        /// Папір кінця сезону з блоком пропозиції (ПМ 12.11.2026) не сягає канатів (верх канатів <c>ArenaKit.RopesY</c> 668) — ряд кнопок
        /// під ним не перекривається. Найгірший набір: найдовші рядки підсумку, обидва ключі з підказками «чого бракує» і найдовший
        /// рядок стану разом (у грі підказки й «кличе» разом не бувають — запас). Висоти рядків — шрифтом жирного тексту.
        /// </summary>
        [Test]
        public void SeasonEnd_Paper_WithTheOffer_StaysAboveTheRopes()
        {
            const float ropesY = 668f, top = 45f;   // ArenaKit.RopesY, ArenaKit.Top (Gameplay-UI тест не бачить)
            float col = PaperW - PaperPadL - PaperPadR;
            var content = CareerKit.Content();
            string fed = "";
            foreach (var f in content.Federations) { string n = Ui(f.NameKey); if (n.Length > fed.Length) fed = n; }
            // імена борців — у strings_uk.json (екран бере їх через T.Get так само)
            string hammer = RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json")).Get(content.FindNpc(CareerContent.HammerId).NameKey);
            float h = PaperPadT;
            h += WordFont.LineHeight(32f);                                                                 // «Кінець сезону N»
            h += 2f + Para(string.Format(Ui("c.fed.line"), fed, 4, 4), BrandTokens.TypeMinInfo, col);
            h += 18f;                                                                                      // ShowKit.Rule
            h += Para(string.Format(Ui("c.season.record"), 99, 99, 99), 19f, col);
            h += BulletH(string.Format(Ui("c.season.pop"), 10, 10), col);
            h += 6f + Para(string.Format(Ui("c.season.line"), 10, 5, 99999), BrandTokens.TypeMinInfo, col);
            h += BulletH(Longest(string.Format(Ui("c.season.feud"), hammer, 4, 4), string.Format(Ui("c.season.feud_done"), hammer)), col);
            h += BulletH(Longest(Ui("c.season.belt_yes"), Ui("c.season.belt_no"), Ui("season.title_shot"), Ui("season.defense"),
                string.Format(Ui("c.season.goal"), 4, 4)), col);
            // блок пропозиції
            h += 18f;
            h += Para(Longest(string.Format(Ui("c.offer.title"), fed), string.Format(Ui("c.offer.progress"), fed), string.Format(Ui("c.offer.top"), fed)),
                BrandTokens.TypeBody, col);
            float keyText = col - CareerTileLayout.CheckW - 8f;
            string hint = Longest(string.Format(Ui("c.offer.need_audience"), 10), string.Format(Ui("c.offer.need_booker"), 5));
            for (int i = 0; i < 2; i++)
                h += 6f + Math.Max(CareerTileLayout.CheckH, SemiFont.LineHeight(BrandTokens.TypeBody)) + Para(hint, BrandTokens.TypeMinInfo, keyText);
            string status = Longest(Ui("c.offer.ready"), Ui("c.offer.farewell"), Ui("c.offer.declined"), Ui("c.offer.offended"),
                string.Format(Ui("c.offer.moved"), fed), string.Format(Ui("c.offer.farewell.ok"), fed), string.Format(Ui("c.offer.farewell.bad"), fed, fed),
                string.Format(Ui("c.season.demoted"), fed, fed));
            int statusLines;
            h += 8f + Para(status, BrandTokens.TypeMinInfo, col, out statusLines);
            h += PaperPadB;
            Assert.LessOrEqual(top + h, ropesY - PaperClear,
                string.Format("папір кінця сезону: {0:0} + {1:0} = {2:0} > канати {3:0} − {4:0} (рядок стану — {5} рядки)", top, h, top + h, ropesY, PaperClear, statusLines));
        }

        // ---------------- ряд показу ----------------

        [Test]
        public void Show_RowTiles_TitleOneLine_AtMinSize_OnEveryPanel()
        {
            var bad = new List<string>();
            foreach (var p in Panels)
            {
                float wd = CareerTileLayout.ShowTileWidth(p.Value, CareerTileLayout.WithdrawTileW, CareerTileLayout.WithdrawTileWNarrow);
                float sk = CareerTileLayout.ShowTileWidth(p.Value, CareerTileLayout.TitleSkipTileW, CareerTileLayout.TitleSkipTileWNarrow);
                // ряд показу не стискає плитки (SetCompact не кличе) — тож і модель має бачити їх некомпактними
                if (CareerTileLayout.Compact(wd) || CareerTileLayout.Compact(sk)) bad.Add(p.Key + ": плитка показу вужча за CompactTileW");
                CheckTitle(bad, "картка вечора " + p.Key, Ui("c.card.withdraw"), wd, false);
                CheckTitle(bad, "кінець сезону " + p.Key, Ui("c.season.title_skip"), sk, false);
                // «Лишитися» — та сама плитка ряду кінця сезону (ПМ 12.11.2026): опис — що буде, друга відмова поспіль — ризиком під
                // ним; ряд не росте, тож обидва разом — у TileH
                CheckTitle(bad, "кінець сезону " + p.Key, Ui("c.offer.decline"), sk, false);
                var decline = new TileText(Ui("c.offer.decline"), new[] { Ui("c.offer.decline.again") }, new[] { Ui("c.offer.decline.offend") });
                CheckBody(bad, "кінець сезону " + p.Key, decline, sk);
                foreach (var t in new[] { decline })
                    if (NeededHeight(t, sk) > CareerTileLayout.TileH)
                        bad.Add(string.Format("кінець сезону {0}: «Лишитися» потребує {1:0} > {2:0}", p.Key, NeededHeight(t, sk), CareerTileLayout.TileH));
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        // ---------------- золота плита ----------------

        private static readonly string[] GoldKeys =
            { "week.end", "c.create.start", "c.card.go", "c.hub.card.go_feud", "ui.next", "c.season.title_go", "season.next", "c.offer.accept" };

        [Test]
        public void GoldPlates_TwoLinesAtMost_NoWordBreak_AtFittedSize()
        {
            var bad = new List<string>();
            foreach (var key in GoldKeys)
            {
                string text = Caps(Ui(key));
                foreach (var plateW in new[] { CareerTileLayout.GoldW, CareerTileLayout.GoldWNarrow })
                {
                    float availW = CareerTileLayout.WordWidth(plateW), availH = CareerTileLayout.WordHeight(CareerTileLayout.TileH);
                    float s = FitWord(text, availW, availH);
                    float widest;
                    int lines = WordFont.Lines(text, s, availW, CareerTileLayout.WordTrack, out widest);
                    float h = lines * WordFont.LineHeight(s);
                    if (widest > availW) bad.Add(string.Format("{0} ({1:0}): слово ширше плити на кеглі {2} — {3:0} > {4:0}", key, plateW, s, widest, availW));
                    if (lines > CareerTileLayout.WordMaxLines) bad.Add(string.Format("{0} ({1:0}): {2} рядки на кеглі {3}", key, plateW, lines, s));
                    if (h > availH + 0.5f) bad.Add(string.Format("{0} ({1:0}): висота {2:0} > {3:0} на кеглі {4}", key, plateW, h, availH, s));
                    if (HintClash(text, s, plateW, MinGapToHint)) bad.Add(string.Format("{0} ({1:0}): останній рядок на кеглі {2} лягає на «{3}»", key, plateW, s, GoldHintWorst()));
                    if (WordFont.Missing(text).Count > 0) bad.Add(key + ": у шрифті немає «" + new string(WordFont.Missing(text).ToArray()) + "»");
                }
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>Найширша підказка клавіші золотої плити (клавіатура й геймпад) — display 800, кегль 15, розрядка 0,12.</summary>
        private static string GoldHintWorst()
        {
            string a = Caps(Ui("m.hint.enter")), b = Caps(Ui("m.hint.pad_a"));
            return TitleFont.Width(a, BrandTokens.TypeLabel, GoldHintTrack) >= TitleFont.Width(b, BrandTokens.TypeLabel, GoldHintTrack) ? a : b;
        }

        private const float GoldHintTrack = 0.12f;

        /// <summary>
        /// Останній рядок слова заходить на підказку клавіші і по висоті, і по ширині (як GoldPlate.FitWord): низ слова нижче
        /// верху підказки, а правий кінець останнього рядка — правіше за її лівий край.
        /// </summary>
        /// <summary>
        /// Найменший просвіт між словом золотої плити і підказкою клавіші, яким міряє тест (не число моделі: модель
        /// <see cref="CareerTileLayout.GoldHintGap"/> може змінитись, а слово на «ENTER» — ні).
        /// </summary>
        private const float MinGapToHint = 2f;

        private static bool HintClash(string text, float s, float plateW, float gap)
        {
            string hint = GoldHintWorst();
            float hw = TitleFont.Width(hint, BrandTokens.TypeLabel, GoldHintTrack), hh = TitleFont.LineHeight(BrandTokens.TypeLabel);
            float availW = CareerTileLayout.WordWidth(plateW);
            float widest;
            int lines = WordFont.Lines(text, s, availW, CareerTileLayout.WordTrack, out widest);
            float hintTop = CareerTileLayout.TileH - CareerTileLayout.GoldHintBottom - hh - gap;
            float hintLeft = plateW - CareerTileLayout.GoldHintRight - hw - gap - RenderSlack;   // підпис без переносу — запас на рендер
            return CareerTileLayout.WordTop + lines * WordFont.LineHeight(s) > hintTop
                && CareerTileLayout.WordLeft + LastLineWidth(text, s, availW) > hintLeft;
        }

        /// <summary>Жадібні переноси між словами (як мітка) — ширина останнього рядка.</summary>
        private static float LastLineWidth(string text, float s, float availW)
        {
            string line = "";
            foreach (var w in text.Split(' '))
            {
                if (w.Length == 0) continue;
                string next = line.Length == 0 ? w : line + " " + w;
                line = line.Length == 0 || WordFont.Width(next, s, CareerTileLayout.WordTrack) <= availW + 0.5f ? next : w;
            }
            return WordFont.Width(line, s, CareerTileLayout.WordTrack);
        }

        /// <summary>Той самий підбір, що GoldPlate.FitWord: за шириною найдовшого слова — пропорційно; зайві рядки чи рядок на підказці — мінус 2; не нижче WordMin.</summary>
        private static float FitWord(string text, float availW, float availH)
        {
            float s = CareerTileLayout.WordSize;
            string longest = "";
            foreach (var w in text.Split(' ')) if (w.Length > 0 && WordFont.Width(w, s, CareerTileLayout.WordTrack) > WordFont.Width(longest, s, CareerTileLayout.WordTrack)) longest = w;
            float pw = WordFont.Width(longest, s, CareerTileLayout.WordTrack);
            if (pw > availW + 0.5f) s = Math.Max(CareerTileLayout.WordMin, (float)Math.Floor(s * availW / pw));
            for (int guard = 0; guard < 20; guard++)
            {
                float widest;
                int lines = WordFont.Lines(text, s, availW, CareerTileLayout.WordTrack, out widest);
                float plateW = availW + CareerTileLayout.WordLeft + CareerTileLayout.WordRight;
                if ((lines * WordFont.LineHeight(s) <= availH + 0.5f && !HintClash(text, s, plateW, CareerTileLayout.GoldHintGap)) || s <= CareerTileLayout.WordMin) break;
                s = Math.Max(CareerTileLayout.WordMin, s - 2f);
            }
            return s;
        }

        // ---------------- спільне ----------------

        internal sealed class TileText
        {
            public readonly string Title;
            public readonly string[] Effects, Risks;
            public bool Chances;
            /// <summary>Ефект частинами (ActionTile.SetEffectParts) — замість <see cref="Effects"/>.</summary>
            public string[] Parts;
            public TileText(string title, string[] effects, string[] risks) { Title = title; Effects = effects; Risks = risks; }
        }

        private static void CheckTitle(List<string> bad, string where, string title, float tileW, bool hasKey)
        {
            string t = Caps(title);
            float avail = CareerTileLayout.TitleWidth(tileW, hasKey);
            float w = TitleFont.Width(t, CareerTileLayout.TitleMinSize, CareerTileLayout.TitleTrack);
            if (w > avail) bad.Add(string.Format("{0}: назва «{1}» на кеглі 15 — {2:0} > {3:0} (плитка {4:0})", where, t, w, avail, tileW));
            if (TitleFont.Missing(t).Count > 0) bad.Add(where + ": у шрифті назви немає «" + new string(TitleFont.Missing(t).ToArray()) + "»");
        }

        private static void CheckBody(List<string> bad, string where, TileText t, float tileW)
        {
            float width = TextWidth(tileW);
            if (t.Parts != null)
            {
                float wp;
                int pl = TextFont.PartLines(t.Parts, Body, width, CareerTileLayout.EffectPartGap, out wp);
                if (pl > CareerTileLayout.BodyMaxLines) bad.Add(string.Format("{0}: «{1}» — {2} рядки у {3:0} (плитка «{4}»)", where, string.Join(" | ", t.Parts), pl, width, t.Title));
                if (wp > width) bad.Add(string.Format("{0}: частина ширша за рядок — {1:0} > {2:0} (плитка «{3}»)", where, wp, width, t.Title));
                foreach (var part in t.Parts)
                    if (TextFont.Missing(part).Count > 0) bad.Add(where + ": у шрифті тексту немає «" + new string(TextFont.Missing(part).ToArray()) + "»");
            }
            var texts = new List<string>(t.Effects);
            texts.AddRange(t.Risks);
            foreach (var s in texts)
            {
                if (string.IsNullOrEmpty(s)) continue;
                float widest;
                int lines = TextFont.Lines(s, Body, width, 0f, out widest);
                if (lines > CareerTileLayout.BodyMaxLines) bad.Add(string.Format("{0}: «{1}» — {2} рядки у {3:0} (плитка «{4}»)", where, s, lines, width, t.Title));
                if (widest > width) bad.Add(string.Format("{0}: у «{1}» слово ширше рядка — {2:0} > {3:0}", where, s, widest, width));
                if (TextFont.Missing(s).Count > 0) bad.Add(where + ": у шрифті тексту немає «" + new string(TextFont.Missing(s).ToArray()) + "»");
            }
        }

        /// <summary>
        /// Висота, якої плитці треба (як ActionTile.NeededHeight) — найбільша з її станів: увімкнена плитка зі смугою шансів
        /// (рядка причини тоді немає) чи вимкнена з причиною (без смуги — WeekScreenView ставить шанси лише увімкненим).
        /// </summary>
        private static float NeededHeight(TileText t, float tileW)
        {
            float h = Height(t, tileW, false, true);
            return t.Chances ? Math.Max(h, Height(t, tileW, true, false)) : h;
        }

        /// <summary>Висота плитки: відступ, назва, найдовший ефект, найдовший ризик (якщо <paramref name="risks"/>), смуга з легендою (якщо <paramref name="chances"/>).</summary>
        private static float Height(TileText t, float tileW, bool chances, bool risks)
        {
            float width = TextWidth(tileW);
            bool compact = CareerTileLayout.Compact(tileW);
            float top = compact ? CareerTileLayout.CompactBodyTop : CareerTileLayout.BodyTop;
            float head = Math.Max(TitleFont.LineHeight(CareerTileLayout.TitleBaseSize(tileW)), compact ? 0f : CareerTileLayout.KeyBox);
            float lh = TextFont.LineHeight(Body);
            int effect = 0, risk = 0;
            float widest;
            foreach (var s in t.Effects) effect = Math.Max(effect, Math.Max(1, TextFont.Lines(s, Body, width, 0f, out widest)));
            if (t.Parts != null) effect = Math.Max(effect, TextFont.PartLines(t.Parts, Body, width, CareerTileLayout.EffectPartGap, out widest));
            if (risks) foreach (var s in t.Risks) risk = Math.Max(risk, TextFont.Lines(s, Body, width, 0f, out widest));
            float h = top + head + CareerTileLayout.LineGap + effect * lh + risk * lh;
            if (chances) h += CareerTileLayout.ChancesGap + CareerTileLayout.ChancesH + CareerTileLayout.LegendGap + LegendFont.LineHeight(Body);
            return h + CareerTileLayout.BodyBottom;
        }

        private static int Pct(int of36) { return (int)Math.Round(of36 * 100.0 / 36.0); }

        /// <summary>Рядок коду (без коментаря), що починається з <paramref name="code"/>.</summary>
        private static bool CodeLine(string src, string code)
        {
            foreach (var l in src.Split('\n')) if (l.Trim().StartsWith(code)) return true;
            return false;
        }

        /// <summary>
        /// Плитка й золота плита беруть числа з моделі, яку міряють тести вище (кадри слоту 28): відступ смуги й легенди від скосу,
        /// темний золотий сегмент на золотій заливці, текст без внутрішніх відступів мітки, слово над підказкою клавіші.
        /// </summary>
        [Test]
        public void Sources_TileAndGoldPlate_FollowTheModel()
        {
            string src = System.IO.File.ReadAllText(System.IO.Path.Combine(UnityEngine.Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI", "Career", "ArenaKit.cs"));
            var bad = new List<string>();
            var need = new[]
            {
                "_legendRow.RegisterCallback<GeometryChangedEvent>(e => FitSlant());",
                "float m = CareerTileLayout.SlantInset(_bodyTop + bottom, _compact);",
                "_chances.style.marginRight = m;",
                "_legendRow.style.marginRight = m;",
                "_strongSeg = Add3(strong36, total, RingTheme.Gold);",
                "if (_strongSeg != null) _strongSeg.style.backgroundColor = darkText ? RingTheme.Bg : RingTheme.Gold;",
                "NoPad(_effect);",
                "NoPad(_risk);",
                "NoPad(l);",
                "hint.style.right = CareerTileLayout.GoldHintRight; hint.style.bottom = CareerTileLayout.GoldHintBottom;",
                "if (WordTop + wh > hintTop && WordLeft + LastLineWidth(availW) > hintLeft) availH = Math.Min(availH, hintTop - WordTop);",
                "_check.style.right = CareerTileLayout.CheckRight(false); _check.style.top = CareerTileLayout.CheckTop;",
                "_check.style.right = CareerTileLayout.CheckRight(on);",
                "_keyBox.style.top = CareerTileLayout.CompactKeyTop; _keyBox.style.right = CareerTileLayout.CompactKeyRight;",
                "style.width = CareerTileLayout.CheckW; style.height = CareerTileLayout.CheckH;",
            };
            foreach (var n in need) if (!CodeLine(src, n)) bad.Add("ArenaKit.cs: немає «" + n + "»");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
