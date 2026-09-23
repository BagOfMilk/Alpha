using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Sim;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Тесты ТЕМПА, а не арифметики.
    ///
    /// Обычный тест отвечает «правило соблюдено». Эти отвечают на другой вопрос:
    /// «когда игрок впервые что-то увидит и сколько будет ждать». Раз кампания
    /// детерминирована, её можно прогнать целиком за миллисекунды и померить —
    /// вместо того чтобы спорить о числах или ждать живого плейтеста, которого у
    /// соло-разработчика не будет в нужном объёме.
    ///
    /// Утверждения — ОКНА, а не точки: числа баланса плейсхолдерные и будут
    /// двигаться, а вот «первый инцидент приходит на второй неделе» — это уже
    /// дизайн-решение, и его поломку надо замечать.
    ///
    /// Прогоняется весь набор политик × тиров, тот же, что и в tools/Alpha.Sim.
    ///
    /// ПЕРЕМЕРЕНО (Foundation/A1, Поправка №7.1, 23.09.2026): раньше мир этого
    /// теста собирался вручную, через <c>DayProcessor.DefaultSteps()</c> —
    /// БЕЗ производства, стройки и населения (см. CLAUDE.md «Мост производства»,
    /// «не подключены вовсе»). Теперь он строится тем же
    /// <see cref="Game.Core.Session.FirstHourWorld"/>, что и tools/Alpha.Play и
    /// tools/Alpha.Sim, и время идёт через <c>SettlementCycle.AdvanceDay</c> —
    /// та же честная переизмеренная кампания, что видит харнес. Числа заново
    /// сняты прогоном, а не подогнаны: где окно раньше не накрывало
    /// измеренное — окно расширено РОВНО до измеренного, с пометкой
    /// «было / стало» у изменённой строки. Где измеренное осталось в старом
    /// окне — окно не тронуто.
    /// </summary>
    public class CampaignPacingTests
    {
        private const int Days = 200;

        // Список кампаний строится один раз: 12 прогонов по 400 фаз — это
        // десятки миллисекунд, но незачем платить их в каждом тесте.
        private static List<CampaignMetrics> _cache;

        private static List<CampaignMetrics> All()
        {
            if (_cache != null) return _cache;

            var result = new List<CampaignMetrics>();
            foreach (SimPolicy policy in new[] { SimPolicy.Passive, SimPolicy.PatrolEveryNight, SimPolicy.AggressiveChoices })
                foreach (int tier in new[] { 1, 2, 3, 4 })
                {
                    var cfg = new BalanceConfig();
                    var world = Game.Core.Session.FirstHourWorld.Build(tier, requirePlayerDecision: false, balance: cfg);
                    var trace = CampaignSimulator.Run(world.Cycle, policy, Days, cfg);
                    result.Add(CampaignSimulator.Measure(trace));
                }

            _cache = result;
            return _cache;
        }

        private static string Where(CampaignMetrics m)
        {
            return m.Policy + ", тир " + m.Tier;
        }

        // ================= Окна темпа =================

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
        /// ПЕРЕМЕРЕНО 23.09.2026 (Foundation/A1): было окно (4, 20) — на мире БЕЗ
        /// контента открытия первый инцидент действительно приходил не раньше
        /// четвёртых суток. Мир с production теперь строится через
        /// FirstHourWorld и несёт авторский узел «Перевал»
        /// (<c>OpeningContent.PassVanguard</c>, скриптованный источник ровно на
        /// сутки 1) — намеренно надёжная точка входа в первую игровую час, а не
        /// разрыв темпа. Измерено: первый инцидент — сутки 1 во всех 12
        /// кампаниях, стабильно. Стало: окно (1, 20) — нижняя граница снята
        /// ровно до измеренного, верхняя (три недели без события — это провал
        /// онбординга) не тронута.
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

        // ================= Известные разрывы, закреплённые намеренно =================
        //
        // Эти тесты фиксируют то, что СЕЙЧАС не так. Они упадут, когда разрыв
        // закроют, — и это правильный повод обновить ожидание, а не подогнать его.
        // Без них находки аудита живут только в отчёте и тихо забываются.

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
        /// ПЕРЕМЕРЕНО 23.09.2026 (Foundation/A1): старый разрыв ЗАКРЫТ — мир этого
        /// теста раньше собирался БЕЗ PopulationStep/CityWorksStep вообще
        /// (голый DayProcessor.DefaultSteps()), население было константой не
        /// потому, что отток не считался, а потому, что шаг, который его
        /// считает, не стоял в конвейере. Через FirstHourWorld он стоит, и
        /// население ДВИЖЕТСЯ: было 200 → стало 0 во всех 12 кампаниях.
        ///
        /// Ноль — это НОВЫЙ, другой разрыв, и его надо зафиксировать тем же
        /// приёмом: ни одна из ботовых политик (Passive/PatrolEveryNight/
        /// AggressiveChoices) никогда не ставит никого на settlement_farms —
        /// расстановка постов остаётся решением игрока (шаг 2 сборки, ещё не
        /// сделан), а у бота её просто нет. Без еды голод давит на население
        /// каждый голодный день (Поправка №4), и за 200 суток общины без
        /// хозяина съедает досуха. Чинится не здесь: нужен политик-бот, который
        /// умеет расставлять посты (Steward/IBotPolicy — будущие пакеты), а не
        /// правка конвейера дня.
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

        [Test]
        public void Pacing_BandChangeCanBeMute_KnownGap()
        {
            int mute = All().Sum(m => m.BandChangesWithoutSignal);

            Assert.Greater(mute, 0,
                "ИЗВЕСТНЫЙ РАЗРЫВ: инвариант 4 требует, чтобы смена полосы порождала наблюдаемый сигнал. " +
                "Принудительный сигнал только ДОБАВЛЯЕТ кандидата, слота не резервирует, поэтому в шумный " +
                "день его вытесняет бюджет. Когда это починят, тест упадёт");
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
