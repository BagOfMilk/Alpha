using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Sim;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Тести ТЕМПУ, а не арифметики.
    ///
    /// Звичайний тест відповідає «правило дотримано». Ці відповідають на інше питання:
    /// «коли гравець вперше щось побачить і скільки чекатиме». Раз кампанія
    /// детермінована, її можна прогнати цілком за мілісекунди й поміряти —
    /// замість того щоб сперечатися про числа або чекати живого плейтесту, якого в
    /// соло-розробника не буде в потрібному обсязі.
    ///
    /// Твердження — ВІКНА, а не точки: числа балансу плейсхолдерні і будуть
    /// рухатись, а от «перший інцидент приходить на другому тижні» — це вже
    /// дизайн-рішення, і його поломку треба помічати.
    ///
    /// Проганяється весь набір політик × тірів, той самий, що й у tools/Alpha.Sim.
    ///
    /// ПЕРЕВИМІРЯНО (Foundation/A1, Поправка №7.1, 23.09.2026): раніше світ цього
    /// тесту збирався вручну, через <c>DayProcessor.DefaultSteps()</c> —
    /// БЕЗ виробництва, будівництва і населення (див. CLAUDE.md «Міст виробництва»,
    /// «не підключені зовсім»). Тепер він будується тим самим
    /// <see cref="Game.Core.Session.FirstHourWorld"/>, що й tools/Alpha.Play і
    /// tools/Alpha.Sim, і час іде через <c>SettlementCycle.AdvanceDay</c> —
    /// та сама чесна перевимірена кампанія, що бачить харнес. Числа заново
    /// зняті прогоном, а не підігнані: де вікно раніше не накривало
    /// виміряне — вікно розширене РІВНО до виміряного, з поміткою
    /// «було / стало» біля зміненого рядка. Де виміряне лишилось у старому
    /// вікні — вікно не торкнуте.
    /// </summary>
    public class CampaignPacingTests
    {
        private const int Days = 200;

        // Список кампаній будується один раз: 12 прогонів по 400 фаз — це
        // десятки мілісекунд, але нема сенсу платити їх у кожному тесті.
        private static List<CampaignMetrics> _cache;
        private static List<CampaignTrace> _traces;

        private static List<CampaignMetrics> All()
        {
            if (_cache != null) return _cache;

            var result = new List<CampaignMetrics>();
            var traces = new List<CampaignTrace>();
            foreach (SimPolicy policy in new[] { SimPolicy.Passive, SimPolicy.PatrolEveryNight, SimPolicy.AggressiveChoices })
                foreach (int tier in new[] { 1, 2, 3, 4 })
                {
                    var cfg = new BalanceConfig();
                    var world = Game.Core.Session.FirstHourWorld.Build(tier, requirePlayerDecision: false, balance: cfg);
                    var trace = CampaignSimulator.Run(world.Cycle, policy, Days, cfg);
                    traces.Add(trace);
                    result.Add(CampaignSimulator.Measure(trace));
                }

            _cache = result;
            _traces = traces;
            return _cache;
        }

        /// <summary>Ті самі 12 кампаній подобово — для тверджень, яким мало підсумкових метрик.</summary>
        private static List<CampaignTrace> AllTraces()
        {
            All();
            return _traces;
        }

        private static string Where(CampaignMetrics m)
        {
            return m.Policy + ", тир " + m.Tier;
        }

        // ================= Вікна темпу =================

        [Test]
        public void Pacing_FirstForewarning_ArrivesInTheFirstWeek()
        {
            foreach (var m in All())
            {
                Assert.Greater(m.FirstForewarnDay, 0, Where(m) + ": за всю кампанию ни одного предвестника");
                Assert.LessOrEqual(m.FirstForewarnDay, 10,
                    Where(m) + ": первое «что-то зреет» обязано прозвучать на первой неделе, иначе онбординг учит, что город немой");
            }
        }

        /// <summary>
        /// ПЕРЕВИМІРЯНО 23.09.2026 (Foundation/A1): було вікно (4, 20) — на світі БЕЗ
        /// контенту відкриття перший інцидент справді приходив не раніше
        /// четвертої доби. Світ з production тепер будується через
        /// FirstHourWorld і несе авторський вузол «Перевал»
        /// (<c>OpeningContent.PassVanguard</c>, скриптоване джерело рівно на
        /// добу 1) — навмисно надійна точка входу в першу ігрову годину, а не
        /// розрив темпу. Виміряно: перший інцидент — доба 1 у всіх 12
        /// кампаніях, стабільно. Стало: вікно (1, 20) — нижня межа знята
        /// рівно до виміряного, верхня (три тижні без події — це провал
        /// онбордингу) не торкнута.
        /// </summary>
        [Test]
        public void Pacing_FirstIncident_ArrivesInTheFirstTwoWeeks()
        {
            foreach (var m in All())
            {
                Assert.Greater(m.FirstIncidentDay, 0, Where(m) + ": за 200 суток не случилось ни одного инцидента");
                Assert.That(m.FirstIncidentDay, Is.InRange(1, 20),
                    Where(m) + ": первое событие не должно заставлять ждать три недели");
            }
        }

        [Test]
        public void Pacing_FirstDeltaSignal_ArrivesInTheFirstWeek()
        {
            foreach (var m in All())
                Assert.That(m.FirstDeltaDay, Is.InRange(1, 10),
                    Where(m) + ": «что изменилось со вчера» — единственный ответ на вопрос игрока; на первой неделе он обязан прозвучать");
        }

        [Test]
        public void Pacing_SilenceWithoutDelta_NeverExceedsTenDays()
        {
            foreach (var m in All())
                Assert.LessOrEqual(m.LongestStreakWithoutDelta, 20,
                    Where(m) + ": двадцать фаз подряд без единой дельты — это десять суток, в которые игрок жмёт кнопку и читает одно и то же");
        }

        [Test]
        public void Pacing_Ladder_NeverSkipsAStep_InAnyCampaign()
        {
            foreach (var m in All())
                Assert.AreEqual(0, m.SkippedLadderSteps,
                    Where(m) + ": ступень предвестника перепрыгнута. Вторая ступень — единственная, обязанная назвать место");
        }

        [Test]
        public void Pacing_EveryCampaign_ReachesItsCrisis()
        {
            foreach (var m in All())
                Assert.Greater(m.CrisisTotal, 0,
                    Where(m) + ": за 200 суток кризис не наступил ни разу — кульминация, которой нет, не кульминация");
        }

        /// <summary>
        /// «Передвісник не бреше» на всій кампанії (SETTLEMENT_LAYER §5.1,
        /// правило 4). Замір 25.09.2026 до правки: у всіх 12 кампаніях після
        /// кожної кризи драбина «площі» знову доходила до третьої ступені
        /// за 3–5 діб, а наступну кризу відкат (30 діб) пускав лише через
        /// 25. Після правки (PressureTrack.HoldsForewarnings) — 3 доби,
        /// з патрулем 4; дні криз ті самі.
        /// </summary>
        [Test]
        public void Pacing_CrisisLadder_Level3_AlwaysLeadsToTheCrisis()
        {
            var cfg = new PulseBalance();
            int promise = cfg.CrisisGraceDays + cfg.CrisisLadderLeadDays;
            int checkedLadders = 0;
            foreach (var trace in AllTraces())
            {
                var crisisDays = trace.Rows.Where(r => r.FiredSources.Split(';').Contains("crisis")).Select(r => r.Day).ToList();
                foreach (var row in trace.Rows)
                {
                    if (!row.Forewarnings.Split(';').Contains("crisis:3")) continue;
                    if (row.Day + promise > Days) continue;
                    checkedLadders++;
                    int d3 = row.Day;
                    Assert.IsTrue(crisisDays.Exists(c => c > d3 && c <= d3 + promise),
                        trace.Policy + ", тир " + trace.Tier + ": третья ступень кризиса на сутки " + d3 +
                        " не привела к кризису до суток " + (d3 + promise) + " (кризисы: " + string.Join(",", crisisDays) + ")");
                }
            }
            Assert.Greater(checkedLadders, 12, "каждая кампания должна пройти несколько лестниц кризиса");
        }

        // ================= Відомі розриви, закріплені навмисно =================
        //
        // Ці тести фіксують те, що ЗАРАЗ не так. Вони впадуть, коли розрив
        // закриють, — і це правильний привід оновити очікування, а не підігнати його.
        // Без них знахідки аудиту живуть тільки у звіті й тихо забуваються.

        [Test]
        public void Pacing_CityAlwaysEndsAtFracture_KnownGap()
        {
            var bands = All().Select(m => m.FinalBand).Distinct().ToList();

            Assert.AreEqual(new[] { 4 }, bands.ToArray(),
                "ИЗВЕСТНЫЙ РАЗРЫВ: все 12 кампаний заканчиваются на «Изломе» независимо от того, " +
                "патрулировал игрок или спал. Напряжение — храповик: понижать его нечем, " +
                "потому что четыре из пяти снижающих драйверов не имеют вызывающего кода");
        }

        /// <summary>
        /// ПЕРЕВИМІРЯНО 23.09.2026 (Foundation/A1): старий розрив ЗАКРИТО — світ цього
        /// тесту раніше збирався БЕЗ PopulationStep/CityWorksStep взагалі
        /// (голий DayProcessor.DefaultSteps()), населення було константою не
        /// тому, що відтік не рахувався, а тому, що крок, який його
        /// рахує, не стояв у конвеєрі. Через FirstHourWorld він стоїть, і
        /// населення РУХАЄТЬСЯ: було 200 → стало 0 у всіх 12 кампаніях.
        ///
        /// Нуль — це НОВИЙ, інший розрив, і його треба зафіксувати тим самим
        /// прийомом: жодна з ботових політик (Passive/PatrolEveryNight/
        /// AggressiveChoices) ніколи не ставить нікого на settlement_farms —
        /// розстановка постів лишається рішенням гравця (крок 2 збірки, ще не
        /// зроблений), а в бота її просто немає. Без їжі голод тисне на населення
        /// кожен голодний день (Поправка №4), і за 200 діб громада без
        /// господаря виїдається досуха. Лагодиться не тут: потрібен політик-бот, який
        /// уміє розставляти пости (Steward/IBotPolicy — майбутні пакети), а не
        /// правка конвеєра дня.
        /// </summary>
        [Test]
        public void Pacing_UnstaffedFarms_StarveThePopulationToZero_KnownGap()
        {
            foreach (var m in All())
                Assert.AreEqual(0, m.FinalPopulation,
                    Where(m) + " — ИЗВЕСТНЫЙ РАЗРЫВ (новый, сменил закрытый «кризис не трогает население»): " +
                    "за кампанию случилось " + m.CrisisTotal + " кризисов и много голодных суток, а settlement_farms " +
                    "не занял никто — ни одна из ботовых политик не умеет расставлять посты. Население вымерло досуха");
        }

        /// <summary>
        /// РЕГРЕСІЯ G21 (закрито 24.09.2026): раніше mute==0 тут було ЧЕСНО
        /// ВИМІРЯНИМ, але ВИПАДКОВИМ числом — <c>ForceSignalOnBandChange</c>
        /// лише додавав кандидата до спільного бюджету сигналів, слот не
        /// резервував, і густий день (кілька дельт разом) міг витіснити
        /// зміну полоси бюджетом; у ЦЬОМУ конкретному наборі з 12 кампаній ×
        /// 200 діб густих днів з конкурентами не трапилось, тому число
        /// трималось на нулі не завдяки гарантії, а через щасливу вибірку.
        ///
        /// Закрито: зміна полоси — <see cref="Game.Core.Signals.SignalRequest.Mandatory"/>
        /// у SignalComposer, потрапляє в дайджест ПОВЕРХ бюджету (Select()), не
        /// ділячи спільний резерв дельт з іншими сигналами. mute==0 тепер ГАРАНТІЯ
        /// коду — але мутаційний охоронець самої гарантії живе не тут (цей
        /// набір кампаній як і раніше не б'є потрібну щільність дня і тому не
        /// падає, навіть якщо резерв прибрати), а в
        /// <c>SignalComposerTests.Signals_MandatoryBandChanges_
        /// AlwaysGetThrough_EvenOverBudget</c> — той самий густий день, що раніше
        /// вимагав 3 сигнали при бюджеті 2, тепер детерміновано їх
        /// отримує, і тест падає, якщо резерв прибрати.
        ///
        /// РЕГРЕСІЯ G22 (закрито 24.09.2026, після злиття з G21): після
        /// закриття G21 mute повернувся до 2 — не через бюджет (його G21 вже
        /// закрив), а тому що політика AggressiveChoices на добу 20 (тір 1)
        /// і 40 (тір 2) рухала Напругу НАПРЯМУ через
        /// <c>TensionDrivers.QuestChoice(processor.Tension, …)</c> між
        /// закритими добами (минула фаза вже віддала звіт, наступна ще не
        /// почата). Полоса мінялась по-справжньому, але запис у денному журналі
        /// стирав <c>TensionState.BeginDay()</c> наступної фази раніше, ніж
        /// <c>SignalStep</c> встигав її прочитати, — кандидата для мандатного
        /// сигналу просто не будувалося. Закрито: <c>CampaignSimulator</c>
        /// кличе <c>DayProcessor.QueueQuestChoice</c> — заявка йде містком R6
        /// (<c>QueueExternal</c>) і лягає на тик НАСТУПНОЇ фази, ДО
        /// SignalStep тієї ж фази. Гарантія і пастка — тестами в
        /// <c>TensionExternalQueueTests</c> (розділ «G22»).
        ///
        /// ПІСЛЯ ЗЛИТТЯ з «накопичувач без свого інциденту не згорає мовчки»
        /// (гілка awesome-wozniak, 24.09.2026): на своїй базі, ще без G21, та
        /// гілка намірила mute = 1 — AggressiveChoices, тір 2, доба 40, Розпал →
        /// Злам у густий день (мешканці, два доклади постів, перша ступінь
        /// кризи), рівно той випадок, який G21 закриває резервом Mandatory.
        /// Після злиття — 0: гарантія тримає й зсунутий контент кампанії.
        /// </summary>
        [Test]
        public void Pacing_BandChangeIsNeverMute()
        {
            int mute = All().Sum(m => m.BandChangesWithoutSignal);

            Assert.AreEqual(0, mute,
                "Инвариант 4: смена полосы обязана породить сигнал в ТОМ ЖЕ отчёте, независимо " +
                "от того, сколько другого случилось за сутки (G21, SignalComposer.Select — " +
                "Mandatory-кандидаты идут сверх бюджета)");
        }

        [Test]
        public void Pacing_OneTopicDominatesTheWholeCampaign_KnownGap()
        {
            foreach (var m in All())
            {
                Assert.GreaterOrEqual(m.MaxTopicRepeats, 80,
                    Where(m) + " — ИЗВЕСТНЫЙ РАЗРЫВ (уменьшен, но не закрыт): самый частый ключ " +
                    "показывается " + m.MaxTopicRepeats + " раз за кампанию при " + m.DistinctTopics +
                    " уникальных ключах всего. Подавление повторов срезало это вдвое (было около двухсот), " +
                    "но дальше упирается не в механику, а в объём контента: кандидатов в спокойный день " +
                    "меньше, чем слотов. Лечится только новыми ключами");
                Assert.LessOrEqual(m.DistinctTopics, 40,
                    Where(m) + ": ключей стало больше — пора пересматривать оценку контент-бюджета");
            }
        }
    }
}
