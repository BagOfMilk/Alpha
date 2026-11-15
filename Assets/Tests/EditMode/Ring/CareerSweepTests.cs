using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Прохід ядром кар'єри (М1, доручення ПМ 15.11.2026): 3 сезони × 8 ґіміків × обидві ролі × обидва дивізіони, кілька зерен і
    /// варіантів бота. Шукає тупики й неможливі стани: тиждень без доступної дії, гроші в мінусі, «перевантаження» без кінця, травма,
    /// що не лікується, сцена закулісся, яка ніколи не настає.
    /// Звірено з наявними охоронцями: цикл (CareerLoopTests — 8 варіантів, 2 сезони, кожен ґімік в одній ролі), ґіміки 5–8
    /// (EveryNewGimmick_PlaysASeason — 2 сезони), стеля травм (CareerAgeingTests — 6 сезонів), щосезонні події (BackstageTests —
    /// 3 сезони, лише Шоумен-герой), дуга (Arc_ComesNaturally_ForEveryGimmick_…), частка тижнів з нестачею (EconomyPerksFeelTests).
    /// Тут — лише те, чого там немає: кожен ґімік у кожній ролі й дивізіоні на 3 сезони; доступна дія на початку КОЖНОГО тижня;
    /// суперник картки свого дивізіону (мішаний — лише Молот) у всіх федераціях; межі стану після кожного кроку; «перевантаження»
    /// поспіль; сцени закулісся — у сумі прогонів; травма лікується, коли гравець її відновлює.
    /// </summary>
    public class CareerSweepTests
    {
        private sealed class Run
        {
            public string Who;
            public int Weeks, MaxStrainedStreak, MinMoney = int.MaxValue, Fired, MaxFederation;
            /// <summary>Скільки тижнів тривала найдовша травма (кожна окремо) і найбільший лишок травми на початку тижня.</summary>
            public int MaxInjuredStreak, MaxInjuryWeeks;
            public int DeadWeeks;
            public readonly HashSet<string> Scenes = new HashSet<string>();
        }

        private static Run Sweep(Gimmick g, CareerRole role, bool female, ulong seed, int variant, List<string> bad, int seasons = Seasons, bool careful = false)
        {
            var s = CareerKit.NewAtCreate(seed);
            var d = CareerKit.Draft(g, role, CareerKit.FreeFor(s.Content, g, (RingAttr)(variant % 5)), "Прохід");
            d.Female = female;
            s.CreateHero(d);
            var run = new Run { Who = g + "/" + role + "/" + (female ? "Ж" : "Ч") + "/" + seed + "/" + variant };
            var rep = new CareerRunReport();
            int strained = 0, guard = 0;
            var since = new[] { -1, -1, -1 };   // з якого тижня триває травма зони (−1 — здорова); кожна травма окремо
            string lastWeek = null;
            string lastCard = null;
            while (s.State.Season <= seasons)
            {
                if (++guard > 4000) { bad.Add(run.Who + ": кар'єра не рухається, етап " + s.Stage + "/" + s.State.Phase); break; }
                if (s.Stage == CareerStage.Week)
                {
                    string key = s.State.Season + ":" + s.State.Week;
                    if (key != lastWeek)
                    {
                        lastWeek = key;
                        run.Weeks++;
                        if (s.BackstageOpen) run.Scenes.Add(s.CurrentBackstageScene.Id);
                        if (!AnyWeekActionWorks(s)) { run.DeadWeeks++; bad.Add(run.Who + ": тиждень " + key + " без жодної доступної дії"); }
                        strained = s.State.Strained ? strained + 1 : 0;
                        run.MaxStrainedStreak = Math.Max(run.MaxStrainedStreak, strained);
                        for (int z = 0; z < 3; z++)
                        {
                            if (since[z] >= 0) run.MaxInjuredStreak = Math.Max(run.MaxInjuredStreak, run.Weeks - since[z]);
                            run.MaxInjuryWeeks = Math.Max(run.MaxInjuryWeeks, s.State.Injury[z]);
                        }
                    }
                }
                if (s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Card)
                {
                    string key = s.State.Season + ":" + s.State.Week;
                    var opp = s.Content.FindNpc(s.State.Opponent);
                    if (key != lastCard && opp == null) bad.Add(run.Who + ": на картці " + key + " суперника немає в ростері (" + s.State.Opponent + ")");
                    // свій дивізіон; мішаний суперник кар'єри — лише Молот (P2-6)
                    else if (key != lastCard && opp.Female != s.State.Female && opp.Id != CareerContent.HammerId)
                        bad.Add(run.Who + ": на картці " + key + " суперник іншого дивізіону — " + opp.Id);
                    lastCard = key;
                }
                // обережний гравець: травмовану зону відновлює першою дією тижня (без лікаря — безкоштовно); з шоу не сходить
                if (careful && s.Stage == CareerStage.Week && !s.BackstageOpen && !s.State.PromoChoicePending)
                    for (int z = 0; z < 3; z++) if (s.State.Injury[z] > 0 && s.State.ActionsLeft > 0) s.Recover((BodyZone)z, false);
                TrackInjuries(s, since, run.Weeks);
                if (careful && s.Stage == CareerStage.Show && s.State.Phase == ShowPhase.Card) { s.StartShow(); continue; }
                int before = rep.Scenes.Count;
                try { CareerBot.Step(s, variant, rep); }
                catch (Exception ex) { bad.Add(run.Who + ": виняток на " + s.Stage + "/" + s.State.Phase + " " + s.State.Season + ":" + s.State.Week + " — " + ex.Message); break; }
                for (int i = before; i < rep.Scenes.Count; i++) run.Scenes.Add(rep.Scenes[i]);
                TrackInjuries(s, since, run.Weeks);
                if (s.Money < 0) bad.Add(run.Who + ": гроші в мінусі " + s.Money);
                if (s.Popularity < 0 || s.Popularity > CareerRules.MaxPopularity) bad.Add(run.Who + ": популярність " + s.Popularity);
                if (s.Trust < 0 || s.Trust > CareerRules.MaxTrust) bad.Add(run.Who + ": довіра " + s.Trust);
                if (s.DevPoints < 0) bad.Add(run.Who + ": очки розвитку " + s.DevPoints);
                CheckState(s, run.Who, bad);
                run.MinMoney = Math.Min(run.MinMoney, s.Money);
                run.MaxFederation = Math.Max(run.MaxFederation, s.State.Federation);
            }
            run.Fired = rep.Fired;
            return run;
        }

        /// <summary>Травма зажила (0) — зона здорова; нова травма — відлік з поточного тижня. Між кроками, а не лише на початку тижня.</summary>
        private static void TrackInjuries(CareerSession s, int[] since, int week)
        {
            for (int z = 0; z < 3; z++)
            {
                if (s.State.Injury[z] <= 0) since[z] = -1;
                else if (since[z] < 0) since[z] = week;
            }
        }

        /// <summary>Неможливі стани: тиждень і федерація поза межами, дій більше, ніж має тиждень, від'ємні шкали, травма понад стелю.</summary>
        private static void CheckState(CareerSession s, string who, List<string> bad)
        {
            var st = s.State;
            string at = who + " " + st.Season + ":" + st.Week + " (" + s.Stage + "): ";
            if (st.Week < 1 || st.Week > CareerRules.WeeksInSeason(st.Season)) bad.Add(at + "тиждень поза сезоном");
            if (st.Federation < 1 || st.Federation > s.Content.Federations.Count) bad.Add(at + "федерація " + st.Federation);
            if (st.ActionsLeft < 0 || st.ActionsLeft > CareerRules.ActionsPerWeek) bad.Add(at + "дій лишилось " + st.ActionsLeft);
            for (int z = 0; z < 3; z++)
            {
                int cap = CareerRules.MaxInjuryWeeks + Ageing.ScarWeeks(st.Season, st.Scars[z]);
                if (st.Injury[z] < 0 || st.Injury[z] > cap) bad.Add(at + "травма зони " + z + " — " + st.Injury[z] + " тиж. (стеля " + cap + ")");
                if (st.ZoneDamage[z] < 0) bad.Add(at + "ушкодження зони " + z + " від'ємне");
            }
            foreach (RingAttr a in Enum.GetValues(typeof(RingAttr)))
            {
                int v = s.Player.Get(a);
                if (v < WrestlerStats.MinAttr || v > WrestlerStats.MaxAttr) bad.Add(at + "атрибут " + a + " = " + v);
            }
        }

        /// <summary>Хоч одна дія тижня спрацьовує — пробується на копії сесії (сейв → свіжа сесія), сама кар'єра не змінюється.</summary>
        private static bool AnyWeekActionWorks(CareerSession s)
        {
            string blob = s.CaptureState();
            Func<CareerSession, WeekActionResult>[] tries =
            {
                t => t.Train(false), t => t.Promo(), t => t.Backstage(), t => t.SideJob(),
                t => t.Recover(BodyZone.Head, false), t => t.Recover(BodyZone.Body, false), t => t.Recover(BodyZone.Legs, false)
            };
            foreach (var f in tries)
            {
                var t = CareerKit.Fresh(s.State.Seed, s.Content);
                t.RestoreState(blob);
                if (t.BackstageOpen) return true;   // подія, що відкрилась сама: спершу вона, дії — після
                if (f(t).Ok) return true;
            }
            return false;
        }

        [Test]
        public void ThreeSeasons_EveryGimmick_BothRoles_BothDivisions_NoDeadEndsOrImpossibleStates()
        {
            var bad = new List<string>();
            var runs = new List<Run>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int variant = 0;
            foreach (Gimmick g in Enum.GetValues(typeof(Gimmick)))
                foreach (var role in new[] { CareerRole.Hero, CareerRole.Villain })
                    foreach (bool female in new[] { false, true })
                        foreach (ulong seed in new ulong[] { 301, 302, 303 })
                            runs.Add(Sweep(g, role, female, seed + (ulong)(int)g * 10, variant++, bad));

            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(inv, "прогонів {0}; перевантаження поспіль макс. {1}; одна травма тривала макс. {2} тиж.; найбільший лишок травми {3} тиж.; звільнень {4}; федерація макс. {5}; мінімум грошей {6}\n",
                runs.Count, runs.Max(r => r.MaxStrainedStreak), runs.Max(r => r.MaxInjuredStreak), runs.Max(r => r.MaxInjuryWeeks),
                runs.Sum(r => r.Fired), runs.Max(r => r.MaxFederation), runs.Min(r => r.MinMoney));
            foreach (var r in runs.OrderByDescending(r => r.MaxStrainedStreak).Take(5))
                sb.AppendFormat(inv, "  перевантаження: {0} — {1} тиж. поспіль\n", r.Who, r.MaxStrainedStreak);
            foreach (var r in runs.OrderByDescending(r => r.MaxInjuredStreak).Take(5))
                sb.AppendFormat(inv, "  травма: {0} — одна тривала {1} тиж. (лишок до {2})\n", r.Who, r.MaxInjuredStreak, r.MaxInjuryWeeks);
            var seen = new HashSet<string>(runs.SelectMany(r => r.Scenes));
            var all = CareerKit.Content().Backstage.Scenes;
            sb.Append("сцени, яких не було: " + string.Join(", ", all.Where(sc => !seen.Contains(sc.Id)).Select(sc => sc.Id + "(" + sc.Kind + ", з сезону " + sc.MinSeason + ")")) + "\n");
            sb.Append("сцени за кількістю прогонів: " + string.Join(", ", all.Select(sc => sc.Id + "=" + runs.Count(r => r.Scenes.Contains(sc.Id)))) + "\n");
            TestContext.WriteLine(sb.ToString());
            Console.WriteLine("[career-sweep] " + sb);
            Assert.IsEmpty(bad, string.Join("\n", bad.Take(30)));

            // «перевантаження» (нема чим заплатити за тиждень) не триває цілий сезон: є з чого вибратись
            Assert.Less(runs.Max(r => r.MaxStrainedStreak), CareerRules.WeeksPerSeason, "перевантаження поспіль — цілий сезон:\n" + sb);

            // кожна сцена закулісся настає хоч в одному прогоні. Винятки — за будовою, не тупики:
            // - розмова букера федерації, до якої за 3 сезони не дійти (одна федерація за сезон: четверта — не раніше сезону 4;
            //   замір 15.11.2026 на 4 сезонах — booker4_talk у 7 прогонах із 96);
            // - сцени дуги Сталевого (лише федерація 1): їхня природна досяжність — Arc_ComesNaturally_ForEveryGimmick_… у
            //   BackstageVeteranTests, бот тут переважно йде у вищі федерації.
            var content = CareerKit.Content();
            Func<BackstageScene, bool> beyond = sc => sc.Requires.Any(c => c.Key == "booker"
                && content.Federations.Any(f => f.Booker == c.Target && f.Tier > Seasons));
            var never = all.Where(sc => !seen.Contains(sc.Id) && !sc.Arc && !beyond(sc)).Select(sc => sc.Id).ToList();
            Assert.IsEmpty(never, "сцени закулісся, які не настали ні в одному прогоні:\n" + sb);
            Assert.IsTrue(all.Any(beyond), "виняток «федерація поза трьома сезонами» ще потрібен (інакше прибрати)");
        }

        private const int Seasons = 3;

        [Test]
        public void AnInjury_Heals_WhenThePlayerRecoversIt_EvenFightingEveryShow()
        {
            var bad = new List<string>();
            var runs = new List<Run>();
            int variant = 0;
            foreach (Gimmick g in Enum.GetValues(typeof(Gimmick)))
                foreach (var role in new[] { CareerRole.Hero, CareerRole.Villain })
                    foreach (ulong seed in new ulong[] { 401, 402 })
                        runs.Add(Sweep(g, role, variant % 2 == 1, seed + (ulong)(int)g * 10, variant++, bad, Seasons, true));
            var worst = runs.OrderByDescending(r => r.MaxInjuredStreak).First();
            string msg = "прогонів " + runs.Count + ", одна травма тривала макс. " + worst.MaxInjuredStreak + " тиж. (" + worst.Who + "), лишок до "
                         + runs.Max(r => r.MaxInjuryWeeks) + ", перевантаження поспіль макс. " + runs.Max(r => r.MaxStrainedStreak);
            TestContext.WriteLine(msg);
            Console.WriteLine("[career-careful] " + msg);
            Assert.IsEmpty(bad, string.Join("\n", bad.Take(30)));
            // за 3 сезони слідів травм ще немає (Ageing — з сезону 4), тож стеля — звичайна. Замір 15.11.2026: найдовша травма — 3 тижні;
            // бот без відновлення тягне одну травму до 5 тижнів (виступ травмованим з важким ушкодженням її погіршує)
            Assert.LessOrEqual(worst.MaxInjuredStreak, CareerRules.MaxInjuryWeeks, msg);
        }
    }
}
