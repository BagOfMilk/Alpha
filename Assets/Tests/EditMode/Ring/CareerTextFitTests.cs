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
                    float body = CareerTileLayout.BodyWidth(CareerTileLayout.WeekTileWidth(p.Value, 6));
                    if (w > body) bad.Add(string.Format("{0}: легенда «{1} · {2} · {3}» {4:0} > {5:0}", p.Key, a, b, d, w, body));
                }
                foreach (var s in new[] { a, b, d })
                    if (LegendFont.Missing(s).Count > 0) bad.Add("у шрифті легенди немає «" + new string(LegendFont.Missing(s).ToArray()) + "»");
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
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        // ---------------- золота плита ----------------

        private static readonly string[] GoldKeys =
            { "week.end", "c.create.start", "c.card.go", "c.hub.card.go_feud", "ui.next", "c.season.title_go", "season.next" };

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
                    if (WordFont.Missing(text).Count > 0) bad.Add(key + ": у шрифті немає «" + new string(WordFont.Missing(text).ToArray()) + "»");
                }
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>Той самий підбір, що GoldPlate.FitWord: за шириною найдовшого слова — пропорційно; зайві рядки — мінус 2; не нижче WordMin.</summary>
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
                if (lines * WordFont.LineHeight(s) <= availH + 0.5f || s <= CareerTileLayout.WordMin) break;
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
            float width = CareerTileLayout.BodyWidth(tileW);
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

        /// <summary>Висота, якої плитці треба (як ActionTile.NeededHeight): відступ, назва, найдовший ефект, найдовший ризик, смуга з легендою.</summary>
        private static float NeededHeight(TileText t, float tileW)
        {
            float width = CareerTileLayout.BodyWidth(tileW);
            bool compact = CareerTileLayout.Compact(tileW);
            float top = compact ? CareerTileLayout.CompactBodyTop : CareerTileLayout.BodyTop;
            float head = Math.Max(TitleFont.LineHeight(CareerTileLayout.TitleBaseSize(tileW)), compact ? 0f : CareerTileLayout.KeyBox);
            float lh = TextFont.LineHeight(Body);
            int effect = 0, risk = 0;
            float widest;
            foreach (var s in t.Effects) effect = Math.Max(effect, Math.Max(1, TextFont.Lines(s, Body, width, 0f, out widest)));
            if (t.Parts != null) effect = Math.Max(effect, TextFont.PartLines(t.Parts, Body, width, CareerTileLayout.EffectPartGap, out widest));
            foreach (var s in t.Risks) risk = Math.Max(risk, TextFont.Lines(s, Body, width, 0f, out widest));
            float h = top + head + CareerTileLayout.LineGap + effect * lh + risk * lh;
            if (t.Chances) h += CareerTileLayout.ChancesGap + CareerTileLayout.ChancesH + CareerTileLayout.LegendGap + LegendFont.LineHeight(Body);
            return h + CareerTileLayout.BodyBottom;
        }

        private static int Pct(int of36) { return (int)Math.Round(of36 * 100.0 / 36.0); }
    }
}
