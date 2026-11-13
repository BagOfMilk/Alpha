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

        // ---------------- високі плити: фаска лише в куті (огляд слоту 30) ----------------

        /// <summary>
        /// Плити розвитку й шухляда тижня мають фаску 12° лише на висоту стрічки (<see cref="CareerTileLayout.PlateSlantCapH"/>):
        /// вміст біля зрізаного кута відступає від прямого краю не менше, ніж фаска на його висоті — інакше він стоїть поза заливкою
        /// (кадри 4:3 слоту 30: «ЗГОРНУТИ» підсумку над 3D-рефері). Підсумок — дзеркальна плита (зріз угорі ліворуч), кінець сезону
        /// й шухляда — зріз унизу праворуч.
        /// </summary>
        [Test]
        public void TallPlates_ContentNearTheChamferedCorner_StaysInsideTheFill()
        {
            Assert.That(CareerTileLayout.PlateChamfer, Is.InRange(20f, 25f), "фаска — tg 12° × висота стрічки (≈22,5)");
            Assert.AreEqual(CareerTileLayout.PlateChamfer, CareerTileLayout.ChamferInset(0f), 1e-4f);
            Assert.AreEqual(0f, CareerTileLayout.ChamferInset(CareerTileLayout.PlateSlantCapH), "нижче фаски край прямий");
            Assert.GreaterOrEqual(CareerTileLayout.ResultDevPadLeft, CareerTileLayout.ChamferInset(CareerTileLayout.ResultDevPadTop),
                "підсумок: шапка «РОЗВИТОК / ЗГОРНУТИ» біля зрізаного верхнього лівого кута");
            Assert.GreaterOrEqual(CareerTileLayout.DevPadRight + CareerTileLayout.DevCardPad, CareerTileLayout.ChamferInset(CareerTileLayout.DevFootPadBottom),
                "кінець сезону: підвал «Усе (N)» біля зрізаного нижнього правого кута");
            Assert.GreaterOrEqual(CareerTileLayout.DrawerPadRight + CareerTileLayout.DevCardPad, CareerTileLayout.ChamferInset(CareerTileLayout.DevFootPadBottom),
                "шухляда «Розвиток»: підвал «Усе (N)»");
            Assert.GreaterOrEqual(CareerTileLayout.DrawerPadRight, CareerTileLayout.ChamferInset(10f), "шухляда «Витрати»: край прокрутки (відступ 10)");
        }

        /// <summary>Шухляда тижня — над канатами (була на 214 при канатах 210–232: канат різав «▸ Усе (N)» на 4:3, огляд слоту 30).</summary>
        [Test]
        public void WeekDrawer_SitsAboveTheRopes()
        {
            Assert.GreaterOrEqual(CareerTileLayout.DrawerBottom, CareerTileLayout.RopesBottom + CareerTileLayout.RopeH);
            Assert.GreaterOrEqual(CareerTileLayout.DrawerScrollPadBottom, 0f);
        }

        /// <summary>
        /// Клин тла титулу: на 16:9 — без змін (0,46), на 4:3 підпис «МІСЦЕВА ФЕДЕРАЦІЯ · …» лежить на клині з запасом
        /// (кадр m4x3\053 слоту 30: виходив за край). Підпис — Display700 18 з розрядкою 0,14, великими; лівий край 95, верх 240.
        /// </summary>
        [Test]
        public void TitleWedge_CoversTheTagline_OnEveryPanel()
        {
            string sub = Caps(Ui("m.title.sub"));
            // Ширина підпису між двома межами моделі: без розрядки (401) і з повною розрядкою 0,14 × кегль (525); у кадрі 16:9 слоту 30 —
            // 427 (рушій ставить розрядку вужчою). Гра міряє сам підпис (layout.xMax), тож тут — обидві межі.
            float bottom = 240f + LegendFont.LineHeight(18f);
            float low = 95f + LegendFont.Width(sub, 18f, 0f), right = 95f + LegendFont.Width(sub, 18f, 0.14f) + RenderSlack;
            Assert.AreEqual(CareerTileLayout.TitleWedge, CareerTileLayout.FitTitleWedge(1600f, 900f, low, bottom), 1e-4f, "16:9 — без змін");
            Assert.AreEqual(CareerTileLayout.TitleWedge, CareerTileLayout.FitTitleWedge(1600f, 900f, 95f + 427f, bottom), 1e-4f, "16:9, ширина з кадру — без змін");
            foreach (var p in Panels)
            {
                float w = CareerTileLayout.FitTitleWedge(p.Value, 900f, right, bottom);
                Assert.That(w, Is.InRange(CareerTileLayout.TitleWedge, CareerTileLayout.TitleWedgeMax), p.Key);
                Assert.GreaterOrEqual(CareerTileLayout.WedgeEdge(p.Value, 900f, w, bottom) + 0.01f, right + CareerTileLayout.TitleTagClear,
                    p.Key + ": кінець підпису з запасом на клині (" + right.ToString("0", CultureInfo.InvariantCulture) + ")");
            }
            Assert.Greater(CareerTileLayout.FitTitleWedge(1200f, 900f, 95f + 427f, bottom), CareerTileLayout.TitleWedge, "4:3, ширина з кадру — клин ширший (вада кадру m4x3\\053)");
        }

        /// <summary>
        /// Створення на вузькій панелі: відкриті «Прийоми» стають на папір афіші замість опису ролі (огляд слоту 30: темна картка
        /// лягала на рядки характеристик). Папір не росте більше ніж на рядок понад опис — для кожного ґіміка й обох ролей.
        /// Ширина тексту афіші — смуга 410 − відступи 26 і 22.
        /// </summary>
        [Test]
        public void CreatePaperMoves_AreNoTallerThanTheRoleDescription_PlusOneLine()
        {
            const float paperW = CareerTileLayout.StripW - 26f - 22f - RenderSlack;
            var rt = RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json"));
            var view = CareerKit.NewAtCreate(1).GetCreationView();
            float widest;
            float descH = float.MaxValue;
            foreach (var key in new[] { "role.hero.desc", "role.villain.desc" })
                descH = Math.Min(descH, TextFont.Lines(TextBreak.GlueDashes(Ui(key)), BrandTokens.TypeBody, paperW, 0f, out widest) * TextFont.LineHeight(BrandTokens.TypeBody));
            Assert.Greater(view.Gimmicks.Count, 0);
            foreach (var g in view.Gimmicks)
            {
                var names = new List<string>();
                foreach (var m in g.StartMoves) names.Add(rt.Get(m.NameKey));
                string line = string.Join(" · ", names);
                int n = TextFont.Lines(TextBreak.GlueDashes(line), Body, paperW, 0f, out widest);
                Assert.LessOrEqual(widest, paperW, g.Def.Key + ": рядок прийомів ширший за папір");
                Assert.LessOrEqual(n * TextFont.LineHeight(Body), descH + TextFont.LineHeight(Body), g.Def.Key + ": прийомів " + n + " рядків — «" + line + "»");
            }
        }

        // ---------------- тиждень ----------------

        /// <summary>Шість дій тижня (як у WeekScreenView) — назва і всі можливі тексти ефекту, ризику й причини, найгіршими числами.</summary>
        private static List<TileText> WeekTiles()
        {
            int job = CareerRules.SideJobBase + 5 * WrestlerStats.MaxAttr;
            var all = new List<TileText>();
            var spent = new[] { Ui("c.hub.no_actions"), Ui("c.hub.need_money", 999) };
            // №11: зірка поза рингом тренується раз на тиждень — причина на обох плитках тренування
            var train = new[] { spent[0], spent[1], Ui("c.week.reason.star") };
            all.Add(new TileText(Ui("week.action.0"), new[] { Ui("c.week.pay.train", CareerRules.TrainingPoints) }, train));
            all.Add(new TileText(Ui("c.week.train_coach"),
                new[] { Ui("c.week.pay.coach", CareerRules.TrainingPoints + CareerRules.TrainerExtraPoints, CareerRules.TrainerCost) }, train));
            var injuries = new List<string>(spent);
            for (int z = 0; z < 3; z++) injuries.Add(Ui("c.injury.tile", Ui("bodyzone." + z), 9, 99));
            all.Add(new TileText(Ui("week.action.1"), new[] { Ui("c.week.pay.recover", CareerRules.RecoverZoneHeal), Ui("c.week.pay.healthy") }, injuries.ToArray()));
            all.Add(new TileText(Ui("week.action.2"), new[] { Ui("c.week.pay.promo") }, spent) { Chances = true });
            all.Add(new TileText(Ui("week.action.3"), new[] { Ui("c.week.pay.backstage") }, spent) { Chances = true });
            // №8: під виключним правом підробіток заборонено — причина на плитці
            all.Add(new TileText(Ui("week.action.4"), new[] { Ui("c.hub.pay.job", job) },
                new[] { Ui("c.hub.risk", 100), spent[0], spent[1], Ui("c.week.reason.exclusive") }));
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
            int lines = TextFont.Lines(TextBreak.GlueDashes(s), Body, width, 0f, out widest);
            if (lines > maxLines) bad.Add(string.Format("{0}: «{1}» — {2} рядки у {3:0}", where, s, lines, width));
            if (widest > width) bad.Add(string.Format("{0}: у «{1}» слово ширше рядка", where, s));
            if (TextFont.Missing(s).Count > 0) bad.Add(where + ": у шрифті тексту немає «" + new string(TextFont.Missing(s).ToArray()) + "»");
        }

        // ---------------- створення героя ----------------

        /// <summary>
        /// Плитки створення (як у CreateScreenView): ґіміки екрана створення — той самий вид, що бере екран (<c>GetCreationView</c>: ґіміки
        /// 5–8 з <c>onCreationScreen = false</c> до У0 власника не показуються, злиття «Кар'єри» a3fd3fd5), з бонусами характеристик тією
        /// самою функцією, що й гра, і дві ролі. Розкладка восьми — окремий пункт (Б4) після У0.
        /// </summary>
        private static List<TileText> CreateTiles(bool female = false)
        {
            var all = new List<TileText>();
            foreach (var gv in CareerKit.NewAtCreate(1).GetCreationView().Gimmicks)
            {
                var g = gv.Def;
                // назва — за тілом на екрані (ПМ 13.11.2026, RingLabels.GimmickName): «ТЕХНІКИНЯ», «ШОУВУМЕН» довші за чоловічі
                all.Add(new TileText(RingLabels.GimmickName(T, g.Key, female), new[] { "" }, new string[0]) { Parts = RingLabels.AttrBonusParts(T, g.Attr) });
            }
            // ролі героїні — «.f» (HeroForms підставляє їх на екрані створення за тілом)
            all.Add(new TileText(Ui(female ? "role.hero.f" : "role.hero"), new[] { "" }, new string[0]));
            all.Add(new TileText(Ui(female ? "role.villain.f" : "role.villain"), new[] { "" }, new string[0]));
            return all;
        }

        /// <summary>Плитки створення обох родів (герой і героїня): кількість та сама, назви — свого роду.</summary>
        private static IEnumerable<List<TileText>> CreateTilesBothSexes() { yield return CreateTiles(false); yield return CreateTiles(true); }

        [Test]
        public void Create_Tiles_TitleOneLine_BodyTwoLines_FitFixedHeight_OnEveryPanel()
        {
            var bad = new List<string>();
            foreach (var tiles in CreateTilesBothSexes())
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
            tiles.AddRange(CreateTiles(true));   // назви героїні — та сама ширина плитки (кількість плиток рахує екран, не цей список)
            int onScreen = CreateTiles().Count;
            var widths = new List<KeyValuePair<string, float>>();
            foreach (var p in Panels) widths.Add(new KeyValuePair<string, float>(p.Key, CareerTileLayout.CreateTileWidth(p.Value, onScreen)));
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

        /// <summary>
        /// Назва ґіміка обох родів (ПМ 13.11.2026, RingLabels.GimmickName) на афіші створення: велика назва (display Black 52, розрядка
        /// 0,01) і плашка ролі (display Black 26, розрядка 0,04, поля 24 і 22) в одному рядку колонки афіші (стрічка 410 без полів 26
        /// і 22), з проміжком 8. Жіночі назви на 52 не влазили (ТЕХНІКИНЯ + ЛИХОДІЙКА) — кегль назви меншає тією самою функцією, що й
        /// на екрані (CareerTileLayout.FitPosterName, не нижче токена TypeTitle 28): на ньому назва мусить влізти. Усі вісім ґіміків —
        /// і ті, що з'являться на екрані після Б4. Чоловічі назви ґіміків, що вже на екрані, лишаються на 52 (вигляд не змінився).
        /// </summary>
        [Test]
        public void Create_PosterHead_GimmickNameAndRolePlate_FitOneLine_BothSexes()
        {
            var bad = new List<string>();
            float row = CareerTileLayout.StripW - CareerTileLayout.CreatePosterPadLeft - CareerTileLayout.CreatePosterPadRight;
            var onScreen = new HashSet<string>();
            foreach (var g in CareerKit.NewAtCreate(1).GetCreationView().Gimmicks) onScreen.Add(g.Def.Key);
            Assert.That(onScreen.Count, Is.GreaterThanOrEqualTo(4), "екран створення без ґіміків — охоронець нічого не міряє");
            int shrunk = 0;
            foreach (var g in CareerKit.Content().Gimmicks)
                foreach (bool female in new[] { false, true })
                    foreach (var path in new[] { RingPath.Face, RingPath.Heel })
                    {
                        string name = Caps(RingLabels.GimmickName(T, g.Key, female)), role = Caps(RingLabels.PathName(T, path, female));
                        float plate = WordFont.Width(role, CareerTileLayout.RolePlateWordSize, CareerTileLayout.RolePlateWordTrack)
                            + CareerTileLayout.RolePlatePadLeft + CareerTileLayout.RolePlatePadRight + RenderSlack;
                        float avail = row - plate - CareerTileLayout.PosterNameGap;
                        float natural = WordFont.Width(name, CareerTileLayout.PosterNameSize, CareerTileLayout.PosterNameTrack) + RenderSlack;
                        float size = CareerTileLayout.FitPosterName(natural, avail);
                        // екран міряє справжню ширину й масштабує її лінійно (FitPosterName у CreateScreenView) — і тест так само
                        float fitted = natural * size / CareerTileLayout.PosterNameSize;
                        if (fitted > avail)
                            bad.Add(string.Format("афіша: «{0}» + «{1}» — {2:0} > {3:0} навіть на {4:0}", name, role, fitted, avail, size));
                        if (size < CareerTileLayout.PosterNameSize) shrunk++;
                        // чоловічі назви ґіміків, що вже є на екрані, — тим самим кеглем 52, що й до родових назв (вигляд не змінився)
                        if (!female && onScreen.Contains(g.Key) && size < CareerTileLayout.PosterNameSize)
                            bad.Add(string.Format("афіша: «{0}» + «{1}» стиснуто до {2:0} — чоловіча назва мала влазити на 52", name, role, size));
                        if (WordFont.Missing(name).Count > 0) bad.Add("афіша: у шрифті немає гліфів «" + name + "»");
                    }
            Assert.That(shrunk, Is.GreaterThan(0), "жодна назва не стискається — міра афіші, певно, не та (ТЕХНІКИНЯ + ЛИХОДІЙКА на 52 не влазить)");
            // екран рахує тією самою функцією, а не своєю: інакше охоронець міряв би не те, що на афіші
            string create = System.IO.File.ReadAllText(System.IO.Path.Combine(UnityEngine.Application.dataPath, "_Project", "Scripts", "Gameplay", "Ring", "UI", "Career", "CreateScreenView.cs"));
            if (!create.Contains("FitPosterName(head, gname, plate);") || !create.Contains("CareerTileLayout.FitPosterName(w * CareerTileLayout.PosterNameSize / cur,"))
                bad.Add("CreateScreenView: назва на афіші не вміщається CareerTileLayout.FitPosterName");
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>
        /// Мітки стрічок обох родів (ПМ 13.11.2026): «ҐІМІК · ШЛЯХ» (display Bold 15, розрядка 0,12). Створення — поруч із підписом
        /// «ІМ'Я НА РИНГУ» (24 ліворуч, 34 праворуч, проміжок 14) — там нічого не стискається, тож мітка мусить влізти. Стрічки героя
        /// й суперника стискають ім'я до 40 і лише тоді ховають ґімік (FitNameAndTag) — мітка з ґіміком влазить поруч з ім'ям 40.
        /// </summary>
        [Test]
        public void Strips_GimmickTag_FitsBothSexes()
        {
            var bad = new List<string>();
            float tagSize = BrandTokens.TypeLabel, track = 0.12f;
            float label = LegendFont.Width(Caps(Ui("c.create.name")), tagSize, track);
            foreach (var g in CareerKit.Content().Gimmicks)
                foreach (bool female in new[] { false, true })
                    foreach (var path in new[] { RingPath.Face, RingPath.Heel, RingPath.Rebel })
                    {
                        string gn = Caps(RingLabels.GimmickName(T, g.Key, female)), pn = Caps(RingLabels.PathName(T, path, female));
                        float heroTag = LegendFont.Width(gn + " ·", tagSize, track) + LegendFont.Width(" " + pn, tagSize, track) + RenderSlack;
                        if (path != RingPath.Rebel && 24f + label + 14f + heroTag + 34f > CareerTileLayout.StripW)
                            bad.Add(string.Format("створення: «{0} · {1}» — {2:0} поруч із підписом {3:0} у {4:0}", gn, pn, heroTag, label, CareerTileLayout.StripW));
                        if (24f + 40f + 14f + heroTag + 34f > CareerTileLayout.StripW)
                            bad.Add(string.Format("стрічка героя: «{0} · {1}» — {2:0}", gn, pn, heroTag));
                        float rivalTag = LegendFont.Width(pn, tagSize, track) + LegendFont.Width(" · " + gn, tagSize, track) + RenderSlack;
                        if (40f + rivalTag + 14f + 40f + 24f > CareerTileLayout.StripW)
                            bad.Add(string.Format("стрічка суперника: «{0} · {1}» — {2:0}", pn, gn, rivalTag));
                        if (LegendFont.Missing(gn).Count > 0) bad.Add("мітка: у шрифті немає гліфів «" + gn + "»");
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
            lines = Math.Max(1, SemiFont.Lines(TextBreak.GlueDashes(s), size, width, 0f, out widest));
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

        /// <summary>Підсумок сезону на папері (без блоку пропозиції й нижнього відступу) — найдовшими рядками, як у тесті вище.</summary>
        private static float SeasonPaperSummaryH(float col, out string fed)
        {
            var content = CareerKit.Content();
            fed = "";
            foreach (var f in content.Federations) { string n = Ui(f.NameKey); if (n.Length > fed.Length) fed = n; }
            string hammer = RingText.FromJson(CareerKit.Read("strings_uk.json"), CareerKit.Read("ui_uk.json")).Get(content.FindNpc(CareerContent.HammerId).NameKey);
            float h = PaperPadT;
            h += WordFont.LineHeight(32f);
            h += 2f + Para(string.Format(Ui("c.fed.line"), fed, 4, 4), BrandTokens.TypeMinInfo, col);
            h += 18f;
            h += Para(string.Format(Ui("c.season.record"), 99, 99, 99), 19f, col);
            h += BulletH(string.Format(Ui("c.season.pop"), 10, 10), col);
            h += 6f + Para(string.Format(Ui("c.season.line"), 10, 5, 99999), BrandTokens.TypeMinInfo, col);
            h += BulletH(Longest(string.Format(Ui("c.season.feud"), hammer, 4, 4), string.Format(Ui("c.season.feud_done"), hammer)), col);
            h += BulletH(Longest(Ui("c.season.belt_yes"), Ui("c.season.belt_no"), Ui("season.title_shot"), Ui("season.defense"),
                string.Format(Ui("c.season.goal"), 4, 4)), col);
            return h;
        }

        /// <summary>Заголовок пропозиції й два ключі (6 + галочка/рядок 17); під <paramref name="hints"/> з них — «чого бракує».</summary>
        private static float OfferKeysH(float col, string fed, int hints)
        {
            float h = 18f;
            h += Para(Longest(string.Format(Ui("c.offer.title"), fed), string.Format(Ui("c.offer.progress"), fed), string.Format(Ui("c.offer.top"), fed)),
                BrandTokens.TypeBody, col);
            float keyText = col - CareerTileLayout.CheckW - 8f;
            string hint = Longest(string.Format(Ui("c.offer.need_audience"), 10), string.Format(Ui("c.offer.need_booker"), 5));
            for (int i = 0; i < 2; i++)
                h += 6f + Math.Max(CareerTileLayout.CheckH, SemiFont.LineHeight(BrandTokens.TypeBody)) + (i < hints ? Para(hint, BrandTokens.TypeMinInfo, keyText) : 0f);
            return h;
        }

        /// <summary>
        /// №8: папір кінця сезону з контрактом до рішення (ключі виконано — без «чого бракує»): аванс з менеджеркою, пункт пастки,
        /// «перевірила менеджерка» і штраф чинної пастки — усе разом (з менеджеркою пункт видно одразу, а штраф — від минулого контракту),
        /// рядок стану чемпіона — найдовший. Папір не сягає канатів.
        /// </summary>
        [Test]
        public void SeasonEnd_Paper_WithTheContract_StaysAboveTheRopes()
        {
            const float ropesY = 668f, top = 45f;
            float col = PaperW - PaperPadL - PaperPadR;
            string fed;
            float h = SeasonPaperSummaryH(col, out fed);
            h += OfferKeysH(col, fed, 0);
            h += 8f + Para(Longest(Ui("c.offer.ready"), Ui("c.offer.farewell"), Ui("c.offer.farewell.f")), BrandTokens.TypeMinInfo, col);
            h += BulletH(Longest(Ui("c.offer.advance", 99999), Ui("c.offer.advance.manager", 99999, 25)), col);
            float line = col - CareerTileLayout.BulletIndent;
            h += 4f + Para(Longest(Ui("c.offer.fine.unread"), Ui("c.offer.fine.missed"), Ui("c.offer.fine.hint"), Ui("c.offer.fine.clean_hint"),
                Ui("c.offer.fine.clean"), Ui("c.offer.fine.exclusive", 50, 2, 9999)), BrandTokens.TypeMinInfo, line);
            h += 4f + Para(Ui("c.offer.fine.manager"), BrandTokens.TypeMinInfo, line);
            h += 4f + Para(Longest(Ui("c.offer.locked", 9999, 99999), Ui("c.offer.penalty", 9999)), BrandTokens.TypeMinInfo, line);
            h += PaperPadB;
            Assert.LessOrEqual(top + h, ropesY - PaperClear,
                string.Format("папір кінця сезону з контрактом: {0:0} + {1:0} = {2:0} > канати {3:0} − {4:0}", top, h, top + h, ropesY, PaperClear));
        }

        /// <summary>
        /// №11: останній крок кінця сезону — пропозиція вже вирішена (ключі з підказками й найдовший рядок стану), під нею «Пізня роль»:
        /// заголовок — чинна роль, кнопки ролей (CareerScreens.Small, текст 13: поля 8 і 8, смужка 4 і рамка 1, проміжок 4) переносяться в
        /// рядки, під ними — «З наступного сезону: …» найдовшим описом. Папір не сягає канатів.
        /// </summary>
        [Test]
        public void SeasonEnd_Paper_WithTheLateRole_StaysAboveTheRopes()
        {
            const float ropesY = 668f, top = 45f;
            float col = PaperW - PaperPadL - PaperPadR;
            string fed;
            float h = SeasonPaperSummaryH(col, out fed);
            // блок пропозиції на цьому кроці — найвищий зі станів, що бувають (SeasonOfferModel.StatusText): пропозиції ще немає —
            // ключі з двома «чого бракує» без рядка стану; відмова — ключі (довіра після образи могла впасти — одна підказка) і рядок
            // відмови; перехід — ключів нової федерації не видно, лише рядок стану. Звільнений — блоку немає (рядок звільнення вище).
            float none = OfferKeysH(col, fed, 2);
            float declined = OfferKeysH(col, fed, 1) + 8f + Para(Longest(Ui("c.offer.declined"), Ui("c.offer.declined.f"), Ui("c.offer.offended")),
                BrandTokens.TypeMinInfo, col);
            float moved = 18f + 8f + Para(Longest(string.Format(Ui("c.offer.moved"), fed), string.Format(Ui("c.offer.farewell.ok"), fed),
                string.Format(Ui("c.offer.farewell.ok.f"), fed), string.Format(Ui("c.offer.farewell.bad"), fed, fed)), BrandTokens.TypeMinInfo, col);
            h += Math.Max(none, Math.Max(declined, moved));
            // блок пізньої ролі
            // заголовок — чинна роль (043f2706: «Пізня роль: Зірка поза рингом» найдовша), без ролі — «Пізня роль»
            h += 18f + Para(Longest(Ui("c.late.title"), Ui("c.late.current", Ui("c.late.icon")), Ui("c.late.current", Ui("c.late.legend")),
                Ui("c.late.current", Ui("c.late.star"))), BrandTokens.TypeBody, col);
            var names = new[] { Ui("c.late.icon"), Ui("c.late.legend"), Ui("c.late.star"), Ui("c.late.none") };
            int rows = 1;
            float x = 0f;
            foreach (var n in names)
            {
                float w = TextFont.Width(n, 13f) + 8f + 8f + 4f + 1f + 4f;
                if (x > 0f && x + w > col) { rows++; x = 0f; }
                x += w;
            }
            h += 6f + rows * RecButtonH;
            // рядок під кнопками (CareerScreenLines.LateRoleNote): «З наступного сезону: роль. Що дає.», та сама — що дає, без ролі — правило
            string note = Longest(Ui("c.late.hint"),
                Ui("c.late.next", Ui("c.late.icon")) + ". " + Ui("c.late.icon.desc", 9) + ".",
                Ui("c.late.next", Ui("c.late.legend")) + ". " + Ui("c.late.legend.desc") + ".",
                Ui("c.late.next", Ui("c.late.star")) + ". " + Ui("c.late.star.desc", 999, 999) + ".",
                Ui("c.late.next", Ui("c.late.none")) + ".");
            h += 2f + Para(note, BrandTokens.TypeMinInfo, col);
            h += PaperPadB;
            Assert.LessOrEqual(rows, 2, "кнопки ролей — щонайбільше два рядки");
            Assert.LessOrEqual(top + h, ropesY - PaperClear,
                string.Format("папір кінця сезону з пізньою роллю: {0:0} + {1:0} = {2:0} > канати {3:0} − {4:0} (кнопок — {5} рядки)", top, h, top + h, ropesY, PaperClear, rows));
        }

        /// <summary>
        /// Правка 5 огляду слоту 31 (h4x3 014): на вузькій панелі рваний низ афіші створення заходив на вкладку «Прийоми» на ~13 одиниць
        /// (11 px на 768). Компактна вузька афіша (нижчі рядки характеристик і відступ) знімає це з запасом, а рядок і далі вміщає назву
        /// характеристики (display 18).
        /// </summary>
        [Test]
        public void CreatePaper_Narrow_IsCompact_PastTheMeasuredOverlap()
        {
            const float overlap = 13f, clear = 6f;
            Assert.GreaterOrEqual(CareerTileLayout.CreatePaperNarrowSaving, overlap + clear,
                "компактна афіша має зняти виміряний залаз і лишити просвіт");
            Assert.GreaterOrEqual(CareerTileLayout.CreateAttrRowNarrowH, WordFont.LineHeight(18f) + 6f, "рядок характеристики вміщає назву");
            Assert.GreaterOrEqual(CareerTileLayout.CreatePaperPadBottomNarrow, 12f, "рваний край (зуб 4) не сягає останнього рядка");
        }

        /// <summary>
        /// Кнопка поради чи «Усе (N)» (CareerScreens.Small, текст 13) разом із полями мітки пояснення під нею — виміряно з кадрів слоту 30
        /// (1080, кінець сезону): крок порад 52,7 / 73,4 / 94,1 одиниці = 28 + 20,7 × рядки + 4.
        /// </summary>
        private const float RecButtonH = 28f, RecGap = 4f;

        /// <summary>
        /// Ширина пояснення поради на плиті кінця сезону: плита 410 − прокрутка 20 і 14 − картка (рамка 4 і 1, поля 10 і 10) − відступ мітки 6 −
        /// поля мітки Unity 2 і 2 − запас на рендер.
        /// </summary>
        private static float SeasonDevWhyW
        {
            get
            {
                return CareerTileLayout.StripW - CareerTileLayout.DevPadLeft - CareerTileLayout.DevPadRight - 4f - 1f - 2f * 10f - 6f - 4f
                    - RenderSlack;
            }
        }

        /// <summary>
        /// Найгірші три пояснення «Радимо зараз» у рядках (DevelopmentAdvice.For): ключ c.dev.why.* — щонайбільше раз (одне пояснення — одна
        /// порада, повтор бере опис із каталогу); причина програшу — або втримали, або здався (lost_pin і lost_sub разом не бувають);
        /// описи з каталогу (перк, прийом для дії, атрибут) можуть повторитися; жіноча форма пояснення («.f», «.rf» — HeroForms) — той самий
        /// ключ, не окрема порада.
        /// </summary>
        private static int WorstThreeWhyLines(float width, out string which)
        {
            if (_ui == null) Ui("c.dev.all");
            // форми звертання (HeroForms: «ключ.f», «ключ.rf») — той самий ключ: на екрані один із текстів, тож рахується найдовший
            var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
            int catalog = 0;
            string catalogText = "";
            foreach (var kv in _ui)
            {
                string k = kv.Key;
                var s = kv.Value as string;
                if (s == null) continue;
                bool why = k.StartsWith("c.dev.why.", StringComparison.Ordinal);
                bool desc = (k.StartsWith("perk.", StringComparison.Ordinal) && k.EndsWith(".desc", StringComparison.Ordinal))
                    || k.StartsWith("c.dev.attr.why.", StringComparison.Ordinal);
                if (k.StartsWith("action.", StringComparison.Ordinal)) { desc = true; s = Ui("c.dev.move.why", s); }
                if (!why && !desc) continue;
                float widest;
                int n = Math.Max(1, TextFont.Lines(TextBreak.GlueDashes(s), BrandTokens.TypeMinInfo, width, 0f, out widest));
                if (why)
                {
                    string b = k;
                    foreach (var suf in new[] { HeroForms.FemaleSuffix, HeroForms.RivalSuffix })
                        if (k.EndsWith(suf, StringComparison.Ordinal) && _ui.ContainsKey(k.Substring(0, k.Length - suf.Length))) b = k.Substring(0, k.Length - suf.Length);
                    int was;
                    if (!byKey.TryGetValue(b, out was) || n > was) byKey[b] = n;
                }
                else if (n > catalog) { catalog = n; catalogText = s; }
            }
            var once = new List<KeyValuePair<string, int>>(byKey);
            Assert.Greater(once.Count, 8, "пояснень c.dev.why.* — десяток; менше — розбір ui_uk.json зламався");
            int worst = 0;
            which = "";
            foreach (var reason in new[] { "lost_pin", "lost_sub" })
            {
                string other = reason == "lost_pin" ? "c.dev.why.lost_sub" : "c.dev.why.lost_pin";
                var pick = once.FindAll(p => !p.Key.StartsWith(other, StringComparison.Ordinal));
                pick.Sort((x, y) => y.Value.CompareTo(x.Value));
                for (int k = 0; k <= 3 && k <= pick.Count; k++)
                {
                    int sum = (3 - k) * catalog;
                    var keys = new List<string>();
                    for (int i = 0; i < k; i++) { sum += pick[i].Value; keys.Add(pick[i].Key + " " + pick[i].Value); }
                    if (sum <= worst) continue;
                    worst = sum;
                    for (int i = k; i < 3; i++) keys.Add("опис «" + catalogText + "» " + catalog);
                    which = string.Join(", ", keys);
                }
            }
            return worst;
        }

        /// <summary>Найдовший із текстів ключа та його форм звертання (HeroForms «.f», «.rf» — на екрані один із них).</summary>
        private static string UiForms(string key)
        {
            if (_ui == null) Ui("c.dev.all");
            string best = Ui(key);
            foreach (var suf in new[] { HeroForms.FemaleSuffix, HeroForms.RivalSuffix })
            {
                object v;
                var s = _ui.TryGetValue(key + suf, out v) ? v as string : null;
                if (s != null && TextFont.Width(s, 12f) > TextFont.Width(best, 12f)) best = s;
            }
            return best;
        }

        /// <summary>
        /// Усі підказки повного списку «Усе» (CareerScreens.DevelopmentPanel, наведення на кнопку): атрибут — ціна, навіщо й причина
        /// недоступності; прийом — для якої дії й ціна; перк — опис і причина. Числа — найгірші (99), причина — найдовша.
        /// </summary>
        private static List<string> DevAllHints()
        {
            if (_ui == null) Ui("c.dev.all");
            string reason = "";
            foreach (var kv in _ui)
                if (kv.Key.StartsWith("dev.reason.", StringComparison.Ordinal) && kv.Key.IndexOf('.', "dev.reason.".Length) < 0)
                {
                    string r = UiForms(kv.Key);
                    if (TextFont.Width(r, 12f) > TextFont.Width(reason, 12f)) reason = r;
                }
            var all = new List<string>();
            for (int i = 0; i < 5; i++)
                all.Add(string.Format(UiForms("dev.attr.buy"), 99) + " — " + UiForms("c.dev.attr.why." + i) + " · " + reason);
            foreach (var kv in _ui)
            {
                string k = kv.Key;
                if (k.StartsWith("action.", StringComparison.Ordinal) && k.IndexOf('.', "action.".Length) < 0)
                    all.Add(string.Format(UiForms("c.dev.move.why"), UiForms(k)) + " — " + string.Format(UiForms("dev.cost"), 99));
                if (k.StartsWith("perk.", StringComparison.Ordinal) && k.EndsWith(".desc", StringComparison.Ordinal))
                    all.Add(UiForms(k) + " · " + reason);
            }
            return all;
        }

        /// <summary>
        /// Рядок-підказка підвалу плити розвитку (рев'ю слоту 30: поруч із «Усе (N)» опис перка займав 3–5 рядків, підвал ріс при кожному
        /// наведенні, прокрутка стрибала). Тепер він під перемикачем на всю ширину й сталої висоти: кожна підказка повного списку
        /// вміщається в <see cref="CareerTileLayout.DevFootHintLines"/> рядків на вужчій із двох плит (підсумок матчу) — текст 12.
        /// </summary>
        [Test]
        public void DevFooter_Hint_EveryFullListHint_FitsTheFixedLines()
        {
            float season = CareerTileLayout.StripW - (CareerTileLayout.DevPadLeft + CareerTileLayout.DevCardPad)
                - (CareerTileLayout.DevPadRight + CareerTileLayout.DevCardPad);
            float result = CareerTileLayout.StripW - CareerTileLayout.ResultDevPadLeft - CareerTileLayout.ResultDevPadRight - 2f * CareerTileLayout.DevCardPad;
            float width = Math.Min(season, result) - 4f - RenderSlack;   // поля мітки Unity 2 і 2
            var hints = DevAllHints();
            Assert.Greater(hints.Count, 20, "підказок повного списку — десятки; менше — розбір ui_uk.json зламався");
            int worst = 0;
            string worstText = "";
            foreach (var h in hints)
            {
                float widest;
                int n = Math.Max(1, TextFont.Lines(TextBreak.GlueDashes(h), 12f, width, 0f, out widest));
                if (n > worst) { worst = n; worstText = h; }
            }
            Assert.LessOrEqual(worst, CareerTileLayout.DevFootHintLines, string.Format(CultureInfo.InvariantCulture,
                "ширина {0:0}: «{1}» — {2} рядки", width, worstText, worst));
            Assert.AreEqual(CareerTileLayout.DevFootHintLineH, TextFont.LineHeight(12f), 0.05f, "рядок тексту 12 — як у моделі");
        }

        /// <summary>Підвал плити розвитку: «Усе (N)» (кнопка Small) і, коли список розгорнуто, рядок-підказка сталої висоти під ним.</summary>
        private static float DevFoot(bool expanded, float padBottom)
        {
            return CareerTileLayout.DevFootTop + RecButtonH + (expanded ? CareerTileLayout.DevFootHintH : 0f) + padBottom;
        }

        /// <summary>Шапка плити кінця сезону: «РОЗВИТОК», мітка й число очок у рядку.</summary>
        private static float SeasonDevHead
        {
            get
            {
                return CareerTileLayout.DevHeadPadTop
                    + Math.Max(WordFont.LineHeight(CareerTileLayout.DevTitleSize), Math.Max(WordFont.LineHeight(CareerTileLayout.DevPointsSize),
                        LegendFont.LineHeight(BrandTokens.TypeLabel)))
                    + CareerTileLayout.DevHeadGap;
            }
        }

        /// <summary>
        /// Плита розвитку кінця сезону (кадри слоту 30, 12.11.2026: край прокрутки висотою 370 різав «▸ Усе (19)» навпіл). Тепер плита —
        /// до канатів (<see cref="CareerTileLayout.DevPlateMaxH"/>), «Усе (N)» — у підвалі під прокруткою. Тут — бюджет на еталонній панелі
        /// (розмір інтерфейсу 100 %): шапка, три поради з найдовшими поясненнями й згорнутий підвал уміщаються без прокрутки.
        /// </summary>
        [Test]
        public void SeasonEnd_DevPlate_ThreeAdviceAndFooter_FitAboveTheRopes()
        {
            string which;
            int lines = WorstThreeWhyLines(SeasonDevWhyW, out which);
            float line15 = TextFont.LineHeight(BrandTokens.TypeMinInfo), line12 = TextFont.LineHeight(12f);
            float head = SeasonDevHead;
            // картка Glass(8): рамка 1 і поля 8 згори й знизу; «Радимо зараз:» — мітка 12 з полями Unity
            float card = 1f + 8f + line12 + 1f + 3 * (RecButtonH + RecGap) + lines * line15 + 8f + 1f;
            float scroll = card + CareerTileLayout.DevScrollPadBottom;
            float foot = DevFoot(false, CareerTileLayout.DevFootPadBottom);
            float max = CareerTileLayout.DevPlateMaxH(CareerTileLayout.SeasonDevTop, CareerTileLayout.PanelH);
            Assert.LessOrEqual(head + scroll + foot, max, string.Format(CultureInfo.InvariantCulture,
                "плита кінця сезону на 100 %: шапка {0:0.0} + прокрутка {1:0.0} + підвал {2:0.0} > {3:0.0} до канатів; пояснення: {4} (разом {5} рядків)",
                head, scroll, foot, max, which, lines));
        }

        /// <summary>
        /// Обидві плити розвитку (кінець сезону; підсумок матчу — широка й вузька панель) за кожного розміру інтерфейсу (90–120 %: канати
        /// відлічуються знизу, тож місце плити міняється) і в обох станах «Усе»: шапка, найменша прокрутка
        /// (<see cref="CareerTileLayout.DevScrollMinH"/> — не менше за одну пораду з однорядковим поясненням) і підвал не сягають канатів.
        /// Найтісніше — підсумок на 4:3 при 120 % з розгорнутим списком.
        /// </summary>
        [Test]
        public void DevPlates_HeadMinScrollAndFooter_StayAboveTheRopes_AtEveryUiSize()
        {
            float oneRec = RecButtonH + TextFont.LineHeight(BrandTokens.TypeMinInfo) + RecGap;
            Assert.GreaterOrEqual(CareerTileLayout.DevScrollMinH, oneRec, "найменша прокрутка — хоч одна порада цілком");
            // шапка підсумку: «РОЗВИТОК» над міткою «Згорнути» ліворуч, число очок праворуч — вища з двох
            float resultHead = Math.Max(WordFont.LineHeight(CareerTileLayout.ResultDevTitleSize) + LegendFont.LineHeight(BrandTokens.TypeLabel),
                WordFont.LineHeight(CareerTileLayout.ResultDevPointsSize));
            var bad = new List<string>();
            foreach (int scale in RingSettingsValues.UiScales)
            {
                float panel = CareerTileLayout.PanelH * 100f / scale;
                foreach (bool expanded in new[] { false, true })
                {
                    float season = SeasonDevHead + CareerTileLayout.DevScrollMinH + DevFoot(expanded, CareerTileLayout.DevFootPadBottom);
                    float seasonMax = CareerTileLayout.DevPlateMaxH(CareerTileLayout.SeasonDevTop, panel);
                    if (season > seasonMax) bad.Add(string.Format(CultureInfo.InvariantCulture, "кінець сезону {0} %{1}: {2:0.0} > {3:0.0}",
                        scale, expanded ? ", «Усе» розгорнуто" : "", season, seasonMax));
                    float result = CareerTileLayout.ResultDevPadTop + resultHead + CareerTileLayout.DevScrollMinH + DevFoot(expanded, 0f)
                        + CareerTileLayout.ResultDevPadBottom;
                    foreach (var top in new[] { CareerTileLayout.SecondY, CareerTileLayout.ResultDevTopNarrow })
                    {
                        float resultMax = CareerTileLayout.DevPlateMaxH(top, panel);
                        if (result > resultMax) bad.Add(string.Format(CultureInfo.InvariantCulture, "підсумок (верх {0:0}) {1} %{2}: {3:0.0} > {4:0.0}",
                            top, scale, expanded ? ", «Усе» розгорнуто" : "", result, resultMax));
                    }
                }
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
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
                // «Пропустити титульний матч» (ПМ 12.11.2026): опис і ризик чемпіона — та сама висота плитки
                var skip = new TileText(Ui("c.season.title_skip"), new[] { Ui("c.season.title_skip.effect") }, new[] { Ui("c.season.title_skip.champion") });
                CheckBody(bad, "кінець сезону " + p.Key, skip, sk);
                if (NeededHeight(skip, sk) > CareerTileLayout.TileH)
                    bad.Add(string.Format("кінець сезону {0}: «Пропустити» потребує {1:0} > {2:0}", p.Key, NeededHeight(skip, sk), CareerTileLayout.TileH));
                foreach (var t in new[] { decline })
                    if (NeededHeight(t, sk) > CareerTileLayout.TileH)
                        bad.Add(string.Format("кінець сезону {0}: «Лишитися» потребує {1:0} > {2:0}", p.Key, NeededHeight(t, sk), CareerTileLayout.TileH));
                // №8: «Прочитати дрібний шрифт» — та сама плитка; опис — шанси кидка найгіршими числами
                CheckTitle(bad, "кінець сезону " + p.Key, Ui("c.offer.read"), sk, false);
                var read = new TileText(Ui("c.offer.read"), new[] { Ui("c.offer.read.chance", "+10", 100, 100) }, new string[0]);
                CheckBody(bad, "кінець сезону " + p.Key, read, sk);
                if (NeededHeight(read, sk) > CareerTileLayout.TileH)
                    bad.Add(string.Format("кінець сезону {0}: «Прочитати» потребує {1:0} > {2:0}", p.Key, NeededHeight(read, sk), CareerTileLayout.TileH));
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
                int lines = TextFont.Lines(TextBreak.GlueDashes(s), Body, width, 0f, out widest);
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
            foreach (var s in t.Effects) effect = Math.Max(effect, Math.Max(1, TextFont.Lines(TextBreak.GlueDashes(s), Body, width, 0f, out widest)));
            if (t.Parts != null) effect = Math.Max(effect, TextFont.PartLines(t.Parts, Body, width, CareerTileLayout.EffectPartGap, out widest));
            if (risks) foreach (var s in t.Risks) risk = Math.Max(risk, TextFont.Lines(TextBreak.GlueDashes(s), Body, width, 0f, out widest));
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
