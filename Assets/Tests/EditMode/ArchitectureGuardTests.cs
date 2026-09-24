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

        // ---- B7: уніфікація вилазки (R15), G16, G26 ----

        /// <summary>
        /// R15: `ExpeditionRunner.Send` был отдельным, никем не вызываемым
        /// входом — вылазка была косметической. Он удалён, а не оставлен
        /// мёртвым кодом (риск, что кто-то снова позовёт его напрямую в обход
        /// <c>Depart</c>). Тест держит это решение: метод не должен вернуться.
        /// </summary>
        [Test]
        public void ExpeditionRunner_Send_NoLongerExists()
        {
            var method = typeof(Game.Core.Base.ExpeditionRunner)
                .GetMethod("Send", BindingFlags.Public | BindingFlags.Static);

            Assert.IsNull(method,
                "ExpeditionRunner.Send вернулся — второй, необъединённый вход в вылазку снова возможен (R15)");
        }

        /// <summary>
        /// G16: соц-подходы обязаны добирать контекстный атрибут (GDD:98), а
        /// утилитарные — нет. Тест держит именно распределение по подходам, а
        /// не конкретные числа баланса.
        /// </summary>
        [Test]
        public void ContextAttribute_OnlyAppliesToSocialApproaches()
        {
            var cfg = new Game.Core.Balance.BalanceConfig();
            var arch = new Game.Core.Characters.CompanionArchetype("guard_g16", "guard_g16");
            arch.SetSkill(Game.Core.Stats.SkillType.Persuade, 3);
            arch.SetAttribute(Game.Core.Stats.AttributeType.Wits, 7);
            var companion = arch.CreateInstance("guard_g16", cfg);
            var adapter = new Game.Core.Base.CompanionActorAdapter(companion, false, cfg);

            int neutral = adapter.GetCheckValue(Game.Core.Checks.SkillKeys.Persuade, Game.Core.Checks.ApproachForm.Neutral);
            int persuade = adapter.GetCheckValue(Game.Core.Checks.SkillKeys.Persuade, Game.Core.Checks.ApproachForm.Persuade);

            Assert.AreEqual(3, neutral, "Neutral не добирает атрибут");
            Assert.AreEqual(10, persuade, "Persuade добирает Смекалку поверх голого скила (GDD:98)");
        }
    }
}
