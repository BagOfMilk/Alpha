using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text.RegularExpressions;
using Game.Core.Loop;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Инварианты архитектуры, записанные в CLAUDE.md, проверяются автоматически.
    /// Дизайн-правило, которое держится только на дисциплине, рано или поздно
    /// нарушат; правило, которое роняет тесты, — нет.
    /// </summary>
    public class ArchitectureGuardTests
    {
        private static string CoreRoot =>
            Path.Combine(Application.dataPath, "_Project", "Scripts", "Core");

        /// <summary>
        /// Инвариант №1: в ядре нет System.Random. Планировщик событий —
        /// детерминированный накопитель (Поправка №3.3).
        /// </summary>
        [Test]
        public void Core_ContainsNoRandom()
        {
            var offenders = new List<string>();
            var pattern = new Regex(@"\bSystem\.Random\b|\bnew\s+Random\s*\(|UnityEngine\.Random");

            foreach (var file in Directory.GetFiles(CoreRoot, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (pattern.IsMatch(StripComments(text)))
                    offenders.Add(Path.GetFileName(file));
            }

            Assert.IsEmpty(offenders,
                "В ядре не должно быть случайности: " + string.Join(", ", offenders));
        }

        /// <summary>
        /// Инвариант: городской слой не завязан на модель персонажа и экономику.
        /// Он знает только строковый SkillKey и порты — поэтому перестройка
        /// модели (которая уже случилась) его не касается, и следующая тоже.
        /// </summary>
        [Test]
        public void SettlementLayer_DoesNotReferenceLegacyTypes()
        {
            string[] layers = { "Pressure", "Signals", "Loop", "Checks", "World", "Settlement", "Sim" };
            var forbidden = new Regex(
                @"Game\.Core\.Stats|Game\.Core\.Economy|\bSkillType\b|\bAttributeType\b|\bStatKey\b|\bResourceType\b");
            var offenders = new List<string>();

            foreach (var layer in layers)
            {
                var dir = Path.Combine(CoreRoot, layer);
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (forbidden.IsMatch(StripComments(File.ReadAllText(file))))
                        offenders.Add($"{layer}/{Path.GetFileName(file)}");
                }
            }

            Assert.IsEmpty(offenders,
                "Городской слой обязан развязываться от модели персонажа через порты: "
                + string.Join(", ", offenders));
        }

        /// <summary>
        /// Продвинуть время можно только конвейером.
        ///
        /// BaseState.AdvanceCycle закрыт от Game.Gameplay модификатором доступа,
        /// но модификатор можно вернуть одной правкой, и тогда снова появятся
        /// два дневных цикла. Проверка держит решение как контракт.
        /// </summary>
        [Test]
        public void BaseState_HasNoPublicAdvanceCycle()
        {
            var method = typeof(Game.Core.Base.BaseState)
                .GetMethod("AdvanceCycle", BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNull(method,
                "AdvanceCycle снова публичный — производство можно позвать в обход конвейера дня");
        }

        /// <summary>
        /// Закрытый AdvanceCycle не защищает ничего, если рядом есть открытая
        /// дверь. Так и было: порт конвейера (IDailyCycle) дал базе публичный
        /// RunDay(), который звал AdvanceCycle, — и Game.Gameplay снова могла
        /// прокрутить сутки мимо конвейера, без голода и без отчёта. Порт снесён
        /// 23.09.2026; проверка держит обе двери: база не реализует контрактов
        /// конвейера, и ни один её открытый метод без аргументов не двигает время.
        /// </summary>
        [Test]
        public void BaseState_HasNoBackdoorToAdvanceTime()
        {
            var type = typeof(Game.Core.Base.BaseState);

            var loopContracts = type.GetInterfaces()
                .Where(i => i.Namespace == "Game.Core.Loop")
                .Select(i => i.Name).ToArray();
            Assert.IsEmpty(loopContracts,
                "База реализует контракт конвейера — через него сутки идут в обход моста: " +
                string.Join(", ", loopContracts));

            // Сегодня таких методов нет, и цикл ничего не зовёт — он ждёт новых.
            // Любой будущий открытый метод базы без аргументов будет вызван здесь
            // на ПУСТОЙ базе: он обязан это выдерживать. Если метод законно не
            // может работать без ростера — это повод дать ему аргумент, а не
            // исключать его из проверки.
            var cfg = new Game.Core.Balance.BalanceConfig();
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName || method.GetParameters().Length != 0) continue;

                var state = new Game.Core.Base.BaseState(
                    new Game.Core.Characters.Roster(), new Game.Core.Economy.ResourceLedger(), cfg);
                method.Invoke(state, null);

                Assert.AreEqual(0, state.CurrentCycle,
                    "Открытый метод " + method.Name + " двигает время в обход конвейера дня");
            }
        }

        /// <summary>
        /// Направление зависимости: Base знает про Loop, Loop про Base — никогда.
        ///
        /// Существующий забор ищет конкретные типы Stats и Economy и этого не
        /// ловит: ссылку на сам модуль базы он бы пропустил.
        /// </summary>
        [Test]
        public void Loop_DoesNotReferenceBase()
        {
            var dir = Path.Combine(CoreRoot, "Loop");
            if (!Directory.Exists(dir)) Assert.Pass("слоя Loop нет");

            var forbidden = new Regex(@"Game\.Core\.Base|\bBaseState\b|\bCycleReport\b");
            var offenders = new List<string>();

            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                if (forbidden.IsMatch(StripComments(File.ReadAllText(file))))
                    offenders.Add(Path.GetFileName(file));

            Assert.IsEmpty(offenders,
                "Конвейер дня не должен знать про модуль базы: " + string.Join(", ", offenders));
        }

        /// <summary>
        /// Инвариант №9: названия настольных систем-источников не упоминаются
        /// нигде в проекте (юридическая гигиена, Поправка №3.11).
        /// </summary>
        [Test]
        public void Project_DoesNotNameSourceSystems()
        {
            // Слово собирается из частей намеренно: иначе тест поймал бы сам себя.
            string needle = "GU" + "RPS" + "|" + "Steve" + @"\s+" + "Jackson";
            var forbidden = new Regex(needle, RegexOptions.IgnoreCase);
            var offenders = new List<string>();

            foreach (var file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
                if (forbidden.IsMatch(File.ReadAllText(file)))
                    offenders.Add(Path.GetFileName(file));

            Assert.IsEmpty(offenders, "Источник заимствования не называется: " + string.Join(", ", offenders));
        }

        /// <summary>
        /// Порядок шагов дня объявлен один раз и растёт монотонно: скрытые
        /// зависимости между системами не заводятся.
        /// </summary>
        [Test]
        public void DayStepOrder_IsStrictlyIncreasing()
        {
            int[] order =
            {
                DayStepOrder.Clock, DayStepOrder.Construction, DayStepOrder.Production,
                DayStepOrder.Population, DayStepOrder.Derived, DayStepOrder.Hunger, DayStepOrder.Tension,
                DayStepOrder.Obligations, DayStepOrder.Pulse, DayStepOrder.Incidents,
                DayStepOrder.Healing, DayStepOrder.Signals, DayStepOrder.Report
            };

            for (int i = 1; i < order.Length; i++)
                Assert.Less(order[i - 1], order[i], "Шаги дня обязаны идти строго по возрастанию");
        }

        /// <summary>
        /// Лечение стоит ПОСЛЕ Напряжения и его не трогает — техническая
        /// сторона гарантии US-1.3.
        /// </summary>
        [Test]
        public void Healing_RunsAfterTension()
        {
            Assert.Greater(DayStepOrder.Healing, DayStepOrder.Tension);
        }

        /// <summary>Сигналы собираются последними — по финальному состоянию дня.</summary>
        [Test]
        public void Signals_RunAfterEverythingElse()
        {
            Assert.Greater(DayStepOrder.Signals, DayStepOrder.Incidents);
            Assert.Greater(DayStepOrder.Signals, DayStepOrder.Healing);
            Assert.Less(DayStepOrder.Signals, DayStepOrder.Report);
        }

        private static string StripComments(string source)
        {
            source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            return Regex.Replace(source, @"//.*?$", string.Empty, RegexOptions.Multiline);
        }

        // ==================================================================
        // B4 (Social): аудит CompanionStatus.Antagonist (§4.5) и укрытие
        // сырой Лояльности (инвариант 3, R2). Добавлено в конец класса.
        // ==================================================================

        /// <summary>
        /// Аудит §4.5: ни одна из точек допуска, что владеет файлами B4, не
        /// пускает антагониста — назначение на пост (BaseState.TryAssign),
        /// автоназначение (Steward.Staff), присутствие (CompanionActorAdapter/
        /// RosterAdapter). Четвёртая точка §4.5 — «ExpeditionRunner/новий
        /// GameSession.DepartExpedition» — по §5.1 файл B7-эксклюзивный
        /// (`Core/Expeditions/*`), а сам `GameSession.DepartExpedition` (R15)
        /// ещё не существует в этом воркчасте; тестировать его здесь нечем.
        /// ПРЕЖНЯЯ версия этого теста точечно правила `ExpeditionParty.Depart`
        /// (чужой файл вне §5.1-владения B4) — правка отменена ревью, см.
        /// deviationsFromSpec пакета B4; точка осталась швом для B7/D1 (§4.11:
        /// валидация, включая Antagonist, происходит ПЕРЕД вызовом Depart).
        /// </summary>
        [Test]
        public void Antagonist_NeverAssignable_NeverDispatchable()
        {
            var cfg = new Game.Core.Balance.BalanceConfig();
            var roster = new Game.Core.Characters.Roster();
            var state = new Game.Core.Base.BaseState(roster, new Game.Core.Economy.ResourceLedger(), cfg);
            state.AddSlot(new Game.Core.Base.AssignmentSlotDefinition(
                "post", "Пост", Game.Core.Base.BaseSectionType.Council));

            var antagonist = new Game.Core.Characters.CompanionArchetype("antagonist", "antagonist")
                .CreateInstance("antagonist", cfg);
            roster.Add(antagonist);
            Game.Core.Companions.Defection.Defect(antagonist);
            Assert.AreEqual(Game.Core.Characters.CompanionStatus.Antagonist, antagonist.Status);

            // 1) BaseState.TryAssign — не встаёт на пост.
            var assign = state.TryAssign("antagonist", "post");
            Assert.AreEqual(Game.Core.Base.AssignmentResult.CompanionUnavailable, assign,
                "антагонист не должен быть назначаем на пост");
            Assert.IsNull(state.GetSlot("post").AssignedCompanionId);

            // 2) Steward.Staff — не расставляет антагониста на открытый пост,
            //    даже когда больше некому.
            Game.Core.Base.Steward.Staff(state);
            Assert.IsNull(state.GetSlot("post").AssignedCompanionId,
                "автоназначение хозяина не должно ставить антагониста на пост");

            // 3) Присутствие — адаптеры исключают антагониста.
            var actorAdapter = new Game.Core.Base.CompanionActorAdapter(antagonist);
            Assert.IsFalse(actorAdapter.IsPresentInSettlement,
                "антагонист не присутствует в поселении");
            var rosterAdapter = new Game.Core.Base.RosterAdapter(roster);
            CollectionAssert.DoesNotContain(
                System.Linq.Enumerable.Select(rosterAdapter.PresentActors, a => a.Id), "antagonist",
                "антагонист не должен попадать в список присутствующих");
        }

        /// <summary>
        /// Блокер ревью пакета B4: дневной кризис (`IncidentResolver.ResolveCrisis`
        /// через `Core/Loop/IncidentStep.cs`) выбирает жертву из
        /// `RosterAdapter.KillableActorIds` и бьёт по ней `Kill`/`Wound` — это
        /// тоже точка допуска по `CompanionStatus`, которую пропустил
        /// исходный аудит §4.5 (он назвал только TryAssign/Staff/Presence/
        /// Depart). Без исключения обычный кризис молча стирал необратимый
        /// статус антагониста обратно в Dead/Injured ДО того, как финал успевал
        /// использовать дефектора как босса (R8, FromDefector).
        /// </summary>
        [Test]
        public void Antagonist_NeverKillableByOrdinaryIncident()
        {
            var cfg = new Game.Core.Balance.BalanceConfig();
            var roster = new Game.Core.Characters.Roster();

            var antagonist = new Game.Core.Characters.CompanionArchetype("antagonist2", "antagonist2")
                .CreateInstance("antagonist2", cfg);
            roster.Add(antagonist);
            Game.Core.Companions.Defection.Defect(antagonist);
            Assert.AreEqual(Game.Core.Characters.CompanionStatus.Antagonist, antagonist.Status);

            var rosterAdapter = new Game.Core.Base.RosterAdapter(roster);

            CollectionAssert.DoesNotContain(rosterAdapter.KillableActorIds, "antagonist2",
                "антагонист не должен считаться допустимой жертвой кризиса");

            rosterAdapter.Wound("antagonist2", 30);
            Assert.AreEqual(Game.Core.Characters.CompanionStatus.Antagonist, antagonist.Status,
                "Wound не должен затирать статус антагониста");

            rosterAdapter.Kill("antagonist2");
            Assert.AreEqual(Game.Core.Characters.CompanionStatus.Antagonist, antagonist.Status,
                "Kill не должен затирать статус антагониста");
        }

        /// <summary>
        /// R2/инвариант 3: сырая Лояльность — internal, как и Напряжение.
        /// Game.Gameplay не может прочитать число, только LoyaltyBand.
        /// </summary>
        [Test]
        public void Companion_LoyaltyRaw_IsInternal_NotReadableFromGameplay()
        {
            var type = typeof(Game.Core.Characters.Companion);

            var internalLoyalty = type.GetProperty("Loyalty",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(internalLoyalty, "Companion.Loyalty должен существовать как internal-член");

            var publicLoyalty = type.GetProperty("Loyalty",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNull(publicLoyalty, "Companion.Loyalty не должен быть публичным");

            var band = type.GetProperty("LoyaltyBand", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(band, "LoyaltyBand — единственное, что видно наружу");
        }

        /// <summary>
        /// Блокер фикс-ревью пакета B4: <c>Defection.Defect(c)</c> без явного
        /// <c>BaseState</c> (сигнатура и §7-таблица спецификации не требуют
        /// его передавать) чистит только <c>Companion.AssignedSlotId</c> —
        /// бухгалтерия слота (<c>AssignmentSlot.AssignedCompanionId</c>) не
        /// узнаёт об уходе и раньше оставалась занятой навсегда: пост нельзя
        /// было отдать живому, а дефектор молча продолжал бы производить и
        /// получать опыт с поста каждый цикл (тот же класс дыры, что и G17
        /// для погибших). Фикс — <c>BaseState.IsFallen</c> считает Antagonist
        /// «упавшим» наравне с IsDead, так что и опережающая сверка
        /// (<c>ReleaseFallen</c>, тикает первым шагом AdvanceCycle и перед
        /// Steward.Staff), и сам TryAssign освобождают пост без чьей-либо
        /// подсказки о статусе.
        /// </summary>
        [Test]
        public void Defect_WithoutBaseState_PostIsFreedByReleaseFallenAndStopsProducing()
        {
            var cfg = new Game.Core.Balance.BalanceConfig { FoodUpkeepPerCompanion = 0 };
            var roster = new Game.Core.Characters.Roster();
            var state = new Game.Core.Base.BaseState(roster, new Game.Core.Economy.ResourceLedger(), cfg);

            var arch = new Game.Core.Characters.CompanionArchetype("defector", "Дефектор");
            arch.SetSkill(Game.Core.Stats.SkillType.Mechanics, 10);
            var companion = arch.CreateInstance("defector_1");
            roster.Add(companion);

            state.AddSlot(new Game.Core.Base.AssignmentSlotDefinition("bench", "Верстак",
                Game.Core.Base.BaseSectionType.Workshop)
            {
                OutputKind = Game.Core.Base.SlotOutputKind.Resource,
                OutputResource = Game.Core.Economy.ResourceType.Materials,
                PrimarySkill = Game.Core.Stats.SkillType.Mechanics,
                BaseOutput = 5, OutputPerPrimaryPoint = 1.0, OutputPerSecondaryPoint = 0
            });
            Assert.AreEqual(Game.Core.Base.AssignmentResult.Success, state.TryAssign("defector_1", "bench"));

            // Дефекция БЕЗ baseState — ровно тот вызов, что не покрывал старый
            // аудит §4.5 и не покрывает контрактная таблица §7 спецификации.
            Game.Core.Companions.Defection.Defect(companion);
            Assert.AreEqual(Game.Core.Characters.CompanionStatus.Antagonist, companion.Status);
            Assert.IsNull(companion.AssignedSlotId, "напарник сам себя снял с поста при уходе");

            // Бухгалтерия слота ДО фикса осталась бы занятой навсегда.
            Assert.AreEqual("defector_1", state.GetSlot("bench").AssignedCompanionId,
                "сразу после Defect слот ещё занят — сверка происходит на следующем шаге, не внутри Defect");

            var freed = state.ReleaseFallen();
            Assert.AreEqual(1, freed, "ReleaseFallen должен опознать антагониста как упавшего и освободить пост");
            Assert.IsNull(state.GetSlot("bench").AssignedCompanionId, "пост свободен для живого");

            // Пост можно отдать другому живому.
            var other = new Game.Core.Characters.CompanionArchetype("other", "Другой").CreateInstance("other_1");
            roster.Add(other);
            Assert.AreEqual(Game.Core.Base.AssignmentResult.Success, state.TryAssign("other_1", "bench"),
                "после освобождения пост должен принять нового человека");
            state.Unassign("bench");

            // Второй антагонист на том же посту — AdvanceCycle не должен ничего
            // ему производить/начислять, даже если бы бухгалтерия слота как-то
            // осталась занятой (защита в глубину, не только через ReleaseFallen).
            var second = new Game.Core.Characters.CompanionArchetype("defector2", "Дефектор2");
            second.SetSkill(Game.Core.Stats.SkillType.Mechanics, 10);
            var companion2 = second.CreateInstance("defector2_1");
            roster.Add(companion2);
            Assert.AreEqual(Game.Core.Base.AssignmentResult.Success, state.TryAssign("defector2_1", "bench"));
            Game.Core.Companions.Defection.Defect(companion2);

            var before = state.Resources.Get(Game.Core.Economy.ResourceType.Materials);
            state.AdvanceCycle();
            Assert.AreEqual(before, state.Resources.Get(Game.Core.Economy.ResourceType.Materials),
                "антагонист не должен производить ресурсы с поста, на котором технически остался");
            Assert.IsNull(state.GetSlot("bench").AssignedCompanionId,
                "AdvanceCycle сверяется через тот же ReleaseFallen первым шагом — пост освобождён");
        }
    }
}
