using System;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Тіло старіє (TRPG_DESIGN §5.5; черга П5, пункт 5; умови ПМ 12.11.2026): «після кількох сезонів витривалість росте важче;
    /// травми лишають сліди». З сезону 4 витривалість дорожча (в очках розвитку і в тренера), травма зони, що вже травмувалась, —
    /// на тиждень довша. Сліди пишуться з сезону 1, діють з сезону 4, тож М1 (сезони 1–3) той самий. Числа — ПЛЕЙСХОЛДЕР.
    /// </summary>
    public class CareerAgeingTests
    {
        private static readonly RingAttr[] NotStamina = { RingAttr.Strength, RingAttr.Agility, RingAttr.Technique, RingAttr.Charisma };

        private static int StaminaCoach { get { return Array.IndexOf(CareerRules.Coaches, RingAttr.Stamina); } }

        private static CareerSession Ready(CareerSession s)
        {
            if (s.BackstageOpen) CareerKit.PlayScene(s, 0);
            Assert.IsFalse(s.BackstageOpen);
            return s;
        }

        /// <summary>
        /// Свіжа сесія з того самого зліпка, але в сезоні <paramref name="season"/>: ціна приходить із Sync відновлення — як у сейві
        /// гравця. Сліди можна підмінити (null — як у зліпку). <paramref name="ready"/> — зіграти сцену, що відкрилась сама (для купівель
        /// і тренувань); травмам вона заважає: сцена сама може травмувати.
        /// </summary>
        private static CareerSession InSeason(CareerSession s, int season, int[] scars = null, bool ready = true)
        {
            string blob = s.CaptureState(), from = "|season=" + s.State.Season + "|";
            Assert.AreEqual(1, (blob.Length - blob.Replace(from, "").Length) / from.Length, "у зліпку один сезон");
            blob = blob.Replace(from, "|season=" + season + "|");
            if (scars != null) blob = System.Text.RegularExpressions.Regex.Replace(blob, @"\|scars=[^|]*", "|scars=" + string.Join(",", scars));
            var t = CareerKit.Fresh(s.State.Seed, s.Content);
            t.RestoreState(blob);
            Assert.AreEqual(season, t.State.Season);
            return ready ? Ready(t) : t;
        }

        [Test]
        public void Numbers_SurchargeFromSeason4_CappedAt2_ScarOnlyFromSeason4_AndOnlyForAnEarlierInjury()
        {
            for (int season = 1; season < Ageing.FromSeason; season++)
            {
                Assert.AreEqual(0, Ageing.StaminaSurcharge(season), "сезон " + season);
                Assert.AreEqual(0, Ageing.ScarWeeks(season, 3), "сезон " + season + ": слід уже є, але ще не діє");
            }
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 2, 2, 2, 2 }, Enumerable.Range(4, 7).Select(Ageing.StaminaSurcharge).ToArray(), "сезони 4–10");
            Assert.AreEqual(4, Development.AttrCost + Ageing.StaminaSurcharge(10), "стеля надбавки: на сезоні 10 витривалість — 4 очки, не 5");
            Assert.AreEqual(Ageing.MaxStaminaSurcharge, Ageing.StaminaSurcharge(40));
            Assert.AreEqual(0, Ageing.ScarWeeks(4, 0), "перша травма зони — без сліду");
            Assert.AreEqual(Ageing.ScarExtraWeeks, Ageing.ScarWeeks(4, 1));
            Assert.AreEqual(Ageing.ScarExtraWeeks, Ageing.ScarWeeks(9, 5), "слід не накопичується");
        }

        // ---------------- витривалість дорожчає ----------------

        [Test]
        public void SeasonEnd3_KeepsTheOldPrice_StartNextSeason_MakesStaminaCost3_TheRest2_AndTheStaminaCoach4()
        {
            var s = CareerKit.NewHero(1);
            var rep = new CareerRunReport();
            for (int guard = 0; s.Stage != CareerStage.SeasonEnd; guard++) { Assert.Less(guard, 3000, "бот не дійшов до кінця сезону"); CareerBot.Step(s, 0, rep); }
            if (s.CanStartTitleMatch) s.SkipTitle();
            Assert.IsTrue(s.CanStartNextSeason);
            s.State.Season = 3;
            // кінець сезону 3: ціна ще стара — тіло старіє з нового сезону (Sync у StartNextSeason після Season++)
            Assert.AreEqual(Development.AttrCost, s.AttrCost(RingAttr.Stamina));
            Assert.AreEqual(CareerRules.CoachProgressStep, s.CoachStepFor(StaminaCoach));

            s.StartNextSeason();
            Ready(s);
            Assert.AreEqual(4, s.State.Season);
            Assert.AreEqual(3, s.AttrCost(RingAttr.Stamina), "сезон 4: витривалість — 3 очки");
            foreach (var a in NotStamina) Assert.AreEqual(Development.AttrCost, s.AttrCost(a), a + " — як і була");
            Assert.AreEqual(4, s.CoachStepFor(StaminaCoach), "тренер витривалості — 4 тренування на бал");
            for (int c = 0; c < CareerRules.Coaches.Length; c++)
                if (c != StaminaCoach) Assert.AreEqual(CareerRules.CoachProgressStep, s.CoachStepFor(c), "тренер " + c);
            Assert.AreEqual(1, s.GetCareerView().StaminaSurcharge, "надбавка — у виді кар'єри (для «Меню»)");
            Assert.AreEqual(3, Development.AttrCostFor(s.Feud.State, RingAttr.Stamina), "та сама ціна в екрані розвитку ворожнечі");

            // порада показує ту саму ціну: програне утримання — порада радить витривалість
            s.Feud.State.DevPoints = 6;
            s.State.Last = new ShowResult { Played = true, Winner = MatchWinner.Rival, EndKind = MatchEndKind.Pin, Hype = 3 };
            var advice = DevelopmentAdvice.For(s, 5).Where(x => x.Kind == AdviceKind.Attr).ToList();
            Assert.IsTrue(advice.Any(x => x.Attr == RingAttr.Stamina), "замір: порада бачить витривалість");
            foreach (var x in advice) Assert.AreEqual(s.AttrCost(x.Attr), x.Cost, x.Attr.ToString());
        }

        [Test]
        public void Season4Save_InAFreshSession_StaminaCosts3_BuyAttrSpends3_AndRefusesWith2()
        {
            var h = Ready(CareerKit.NewHero(2));
            Assert.AreEqual(Development.AttrCost, h.AttrCost(RingAttr.Stamina), "сезон 1 — стара ціна");
            Assert.AreEqual(CareerRules.CoachProgressStep, h.CoachStepFor(StaminaCoach));
            Assert.AreEqual(0, h.Feud.State.StaminaSurcharge);

            var t = InSeason(h, 4);
            Assert.AreEqual(3, t.AttrCost(RingAttr.Stamina));
            foreach (var a in NotStamina) Assert.AreEqual(Development.AttrCost, t.AttrCost(a), a.ToString());
            Assert.AreEqual(4, t.CoachStepFor(StaminaCoach));
            Assert.AreEqual(CareerRules.CoachProgressStep, t.CoachStepFor(-1), "без тренера — звичайний крок");
            Assert.AreEqual(0, t.Feud.State.Clone().StaminaSurcharge, "надбавка не зберігається й губиться на копії: її ставить кар'єра");
            Assert.IsFalse(t.CaptureState().Contains("surcharge"), "надбавки немає в зліпку");

            string reason;
            foreach (RingAttr a in Enum.GetValues(typeof(RingAttr))) t.Feud.State.Player.Set(a, 0);   // жоден атрибут не на стелі
            int stamina = t.Player.Get(RingAttr.Stamina);
            t.Feud.State.DevPoints = 2;
            Assert.IsFalse(t.CanBuyAttr(RingAttr.Stamina, out reason), "2 очки — мало");
            Assert.AreEqual("points", reason);
            Assert.IsFalse(t.BuyAttr(RingAttr.Stamina));
            Assert.AreEqual(2, t.DevPoints, "відмова нічого не списує");
            Assert.IsTrue(t.CanBuyAttr(RingAttr.Strength, out reason), "інший атрибут — за 2");

            t.Feud.State.DevPoints = 3;
            Assert.IsTrue(t.BuyAttr(RingAttr.Stamina));
            Assert.AreEqual(0, t.DevPoints, "списано 3");
            Assert.AreEqual(stamina + 1, t.Player.Get(RingAttr.Stamina));

            Assert.AreEqual(Development.AttrCost, InSeason(h, 3).AttrCost(RingAttr.Stamina), "сейв сезону 3 — стара ціна");
            Assert.AreEqual(4, InSeason(h, 10).AttrCost(RingAttr.Stamina), "сейв сезону 10 — стеля");
        }

        [Test]
        public void StaminaCoach_FromSeason4_RaisesOnTheFourthTraining_OthersStillOnTheThird()
        {
            var h = Ready(CareerKit.NewHero(3));
            foreach (int season in new[] { 3, 4 })
                for (int coach = 0; coach < CareerRules.Coaches.Length; coach++)
                {
                    var t = InSeason(h, season);
                    t.State.Money = 10000;
                    Assert.IsTrue(t.HireCoach(coach));
                    var attr = CareerRules.Coaches[coach];
                    t.Feud.State.Player.Set(attr, 0);   // не на стелі: тренер може підняти
                    int before = t.Player.Get(attr), need = t.CoachStepFor(coach);
                    Assert.AreEqual(season >= 4 && attr == RingAttr.Stamina ? 4 : 3, need, "сезон " + season + ", тренер " + coach);
                    for (int i = 1; i <= need; i++)
                    {
                        t.State.ActionsLeft = CareerRules.ActionsPerWeek;   // тренувань більше, ніж дій одного тижня
                        var r = t.Train(false);
                        Assert.IsTrue(r.Ok, r.Reason);
                        Assert.AreEqual(i == need, r.CoachRaised, "сезон " + season + ", тренер " + coach + ", тренування " + i);
                    }
                    Assert.AreEqual(before + 1, t.Player.Get(attr));
                }
        }

        // ---------------- сліди травм ----------------

        [Test]
        public void Scar_RecordedFromSeason1_LengthensOnlyFromSeason4_CountedBeforeTheIncrement_ThenInsurance()
        {
            var h = Ready(CareerKit.NewHero(4));
            int head = (int)BodyZone.Head, thr = h.InjuryAtZoneDamage;
            Assert.IsTrue(h.AddZoneDamage(head, thr));
            Assert.That(h.State.Injury[head], Is.InRange(CareerRules.MinInjuryWeeks, CareerRules.MaxInjuryWeeks), "сезон 1 — травма звичайна");
            CollectionAssert.AreEqual(new[] { 1, 0, 0 }, h.State.Scars, "слід записано з сезону 1");
            Assert.IsTrue(h.CaptureState().Contains("|scars=1,0,0"), "сліди — у зліпку");
            h.State.Injury[head] = 0;   // загоїлась

            int shortest = int.MaxValue;
            foreach (int season in new[] { 1, 3, 4, 7 })
                for (int week = 1; week <= 6; week++)
                    for (int z = 0; z < 3; z++)
                    {
                        var clean = InSeason(h, season, new[] { 0, 0, 0 }, false);
                        var scarred = InSeason(h, season, new[] { 1, 1, 1 }, false);
                        var insured = InSeason(h, season, new[] { 1, 1, 1 }, false);
                        insured.State.InsuredSeason = season;
                        foreach (var x in new[] { clean, scarred, insured }) x.State.Week = week;
                        string at = "сезон " + season + ", тиждень " + week + ", зона " + z;
                        foreach (var x in new[] { clean, scarred, insured }) Assert.IsTrue(x.AddZoneDamage(z, thr), at);
                        int w = clean.State.Injury[z];
                        shortest = Math.Min(shortest, w);
                        int scar = season >= Ageing.FromSeason ? Ageing.ScarExtraWeeks : 0;
                        Assert.AreEqual(w + scar, scarred.State.Injury[z], at + ": слід — +" + scar);
                        Assert.AreEqual(Math.Max(1, w + scar - 1), insured.State.Injury[z], at + ": спершу слід, потім страховка −1");
                        Assert.AreEqual(1, clean.State.Scars[z], at + ": перша травма зони — слід 1");
                        Assert.AreEqual(2, scarred.State.Scars[z], at);
                        Assert.AreEqual(2, insured.State.Scars[z], at);
                    }
            Assert.AreEqual(CareerRules.MinInjuryWeeks, shortest, "замір: серед травм є найкоротша — на ній видно порядок слід → страховка");
        }

        [Test]
        public void Worsening_AScarredFiveWeekInjury_StaysFive_AScarredFour_GoesToFive_ACleanOneStopsAtFour()
        {
            var h = Ready(CareerKit.NewHero(5));
            var t = InSeason(h, 4, new[] { 2, 1, 0 }, false);
            // голова: травма зі слідом (раніше вже травмувалась) — 4 + слід; тіло: перша травма зони; ноги: старий сейв без слідів
            t.State.Injury[0] = CareerRules.MaxInjuryWeeks + Ageing.ScarExtraWeeks;
            t.State.Injury[1] = CareerRules.MaxInjuryWeeks;
            t.State.Injury[2] = CareerRules.MaxInjuryWeeks + 1;
            t.WorsenInjuries();
            CollectionAssert.AreEqual(new[] { 5, 4, 5 }, t.State.Injury, "погіршення травму не вкорочує, а стеля зі слідом — 5");

            t.State.Injury[0] = CareerRules.MaxInjuryWeeks; t.State.Injury[1] = 3; t.State.Injury[2] = 0;
            t.WorsenInjuries();
            CollectionAssert.AreEqual(new[] { 5, 4, 0 }, t.State.Injury, "4 зі слідом → 5; 3 без сліду → 4; здорова зона — здорова");
            t.WorsenInjuries();
            CollectionAssert.AreEqual(new[] { 5, 4, 0 }, t.State.Injury, "далі стелі не йде");

            var early = InSeason(h, 3, new[] { 2, 1, 0 }, false);
            early.State.Injury[0] = CareerRules.MaxInjuryWeeks;
            early.WorsenInjuries();
            Assert.AreEqual(CareerRules.MaxInjuryWeeks, early.State.Injury[0], "до сезону 4 слід ще не діє");
        }

        [Test]
        public void InjuryView_ShowsScars_AndHowMuchLongerTheZonesInjuryIs()
        {
            var h = Ready(CareerKit.NewHero(6));
            var t = InSeason(h, 4, new[] { 2, 1, 0 }, false);
            t.State.Injury[0] = 5; t.State.Injury[1] = 2; t.State.Injury[2] = 0;
            var v = t.GetCareerView().Injuries;
            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, v.Select(x => x.Scars).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 0, 0 }, v.Select(x => x.ScarWeeks).ToArray(), "поточна травма: голова зі слідом, тіло — перша");

            var healed = InSeason(h, 4, new[] { 1, 0, 0 }, false);
            Array.Clear(healed.State.Injury, 0, 3);
            CollectionAssert.AreEqual(new[] { 1, 0, 0 }, healed.GetCareerView().Injuries.Select(x => x.ScarWeeks).ToArray(), "здорова зона зі слідом: наступна травма довша");
            var early = InSeason(h, 3, new[] { 1, 0, 0 }, false);
            Array.Clear(early.State.Injury, 0, 3);
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, early.GetCareerView().Injuries.Select(x => x.ScarWeeks).ToArray(), "до сезону 4 — 0");
        }

        [Test]
        public void Save_OldBlobWithoutScars_ReadsAsCleanZones_SameBlobVersion()
        {
            var h = Ready(CareerKit.NewHero(7));
            var t = InSeason(h, 5, new[] { 3, 0, 2 }, false);
            string blob = t.CaptureState();
            Assert.IsTrue(blob.StartsWith(CareerSession.BlobVersion + "|"), "версія зліпка та сама");
            var back = CareerKit.Fresh(t.State.Seed, t.Content);
            back.RestoreState(blob);
            CollectionAssert.AreEqual(new[] { 3, 0, 2 }, back.State.Scars, "сліди переживають сейв");

            var old = CareerKit.Fresh(t.State.Seed, t.Content);
            old.RestoreState(System.Text.RegularExpressions.Regex.Replace(blob, @"\|scars=[^|]*", ""));
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, old.State.Scars, "старий сейв без слідів читається — зони чисті");
            Assert.AreEqual(5, old.State.Season);
            Assert.AreEqual(Development.AttrCost + 1, old.AttrCost(RingAttr.Stamina), "ціна — від сезону, не від сейва");
        }

        [Test]
        public void CareerBot_SixSeasons_InjuriesStayWithinTheirCaps_ScarsOnlyGrow_AndInjuriesWorsen()
        {
            int worsened = 0;
            foreach (int variant in new[] { 0, 1, 2, 5 })
            {
                var s = CareerKit.NewAtTitle((ulong)(9100 + variant));
                var rep = new CareerRunReport();
                int longest = 0;
                for (int guard = 0; guard < 6000 && !(s.Stage != CareerStage.Title && s.Stage != CareerStage.Create && s.State.Season > 6); guard++)
                {
                    bool started = s.Stage != CareerStage.Title && s.Stage != CareerStage.Create;
                    int[] scarsBefore = started ? (int[])s.State.Scars.Clone() : new int[3];
                    int[] injuryBefore = started ? (int[])s.State.Injury.Clone() : new int[3];
                    CareerBot.Step(s, variant, rep);
                    if (s.Stage == CareerStage.Title || s.Stage == CareerStage.Create) continue;
                    for (int z = 0; z < 3; z++)
                    {
                        int cap = CareerRules.MaxInjuryWeeks + (s.State.Season >= Ageing.FromSeason ? Ageing.ScarExtraWeeks : 0);
                        Assert.LessOrEqual(s.State.Injury[z], cap, "варіант " + variant + ", сезон " + s.State.Season + ", зона " + z);
                        Assert.GreaterOrEqual(s.State.Scars[z], scarsBefore[z], "сліди лише ростуть");
                        // травмована зона нової травми не бере (AddZoneDamage), тож ріст наявної травми за крок — лише погіршення
                        if (injuryBefore[z] > 0 && s.State.Injury[z] > injuryBefore[z]) worsened++;
                        longest = Math.Max(longest, s.State.Injury[z]);
                    }
                }
                int scarsSeen = s.State.Scars.Sum();
                Assert.Greater(s.State.Season, 6, "варіант " + variant + ": шість сезонів зіграно");
                Assert.Greater(scarsSeen, 0, "варіант " + variant + ": замір — травми траплялись");
                TestContext.WriteLine("варіант " + variant + ": слідів " + scarsSeen + ", найдовша травма " + longest + " тиж.");
            }
            TestContext.WriteLine("погіршень травм: " + worsened);
            Assert.Greater(worsened, 0, "замір: виступ травмованим з важким ушкодженням затягує травму (WorsenInjuries викликається)");
        }
    }
}
