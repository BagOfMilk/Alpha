using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Game.Core.Balance;
using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Combat;
using Game.Gameplay.Text;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Журнал бою для гравця (BattleView.Log). Раніше GameSession віддавав туди
    /// внутрішній трейс CombatState — російський текст із сирими іменами enum
    /// ("Бурунда → Протагоніст: промах (56)", "получает состояние KnockedDown
    /// (2 х.)"), і фолбек-екран BattleScreen показував його як є, бо жоден
    /// рядок не був ключем таблиці. Тепер ядро пише ключ + аргументи
    /// (CombatState.Journal), слова додає BattleLogText.
    ///
    /// Охоронці:
    ///   - трейс <c>CombatState.Log</c> — internal: Gameplay його не прочитає;
    ///   - кожна строка трейсу має рівно один запис журналу (Record пише обидва);
    ///   - кожен ключ закритого списку має текст і заповнює всі плейсхолдери;
    ///   - BattleView.Log після GameSession — український текст, без жодної
    ///     російської літери і без сирих імен enum.
    /// </summary>
    public class BattleLogTextTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        /// <summary>Літери, яких немає в українській абетці: рядок із ними — російський текст, що протік до гравця.</summary>
        private static readonly Regex RussianOnlyLetters = new Regex("[ыэъёЫЭЪЁ]", RegexOptions.Compiled);

        private static readonly string[] RawEnumNames =
        {
            "KnockedDown", "Bleeding", "Stunned", "Suppressed", "Marked", "Burning", "Poisoned",
            "Ballistic", "Toxin", "Energy", "Ongoing", "Victory", "Defeat"
        };

        // =====================================================================
        // Межа: трейс ядра гравцеві недоступний
        // =====================================================================

        [Test]
        public void CombatState_Trace_IsNotPublic()
        {
            var prop = typeof(CombatState).GetProperty("Log", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNull(prop,
                "CombatState.Log — внутрішній російський трейс; публічним він знову потече в BattleView/екрани (інваріант 3, той самий прийом)");
            Assert.IsNotNull(typeof(CombatState).GetProperty("Journal", BindingFlags.Instance | BindingFlags.Public),
                "журнал для гравця — CombatState.Journal");
        }

        // =====================================================================
        // Таблиця: закритий список ключів журналу має текст
        // =====================================================================

        [Test]
        public void EveryJournalKey_HasText_AndFillsEveryPlaceholder()
        {
            var problems = new List<string>();
            foreach (var key in CombatLogKeys.All)
            {
                if (!UkrainianText.Has(key, Gender.Male) || !UkrainianText.Has(key, Gender.Female))
                {
                    problems.Add(key + ": немає тексту");
                    continue;
                }

                foreach (bool percent in new[] { true, false })
                {
                    string line = BattleLogText.Line(Entry(key, FullArgs()), percent, id => "Ім'я", id => false);
                    if (line.Contains("{") || line.Contains("}")) problems.Add(key + ": лишився плейсхолдер — " + line);
                    if (line.Contains("[")) problems.Add(key + ": заглушка відсутнього ключа — " + line);
                    if (RussianOnlyLetters.IsMatch(line)) problems.Add(key + ": російська літера — " + line);
                }
            }
            CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void EveryStatusAndDamageTypeToken_HasText()
        {
            foreach (StatusType s in Enum.GetValues(typeof(StatusType)))
            {
                if (s == StatusType.None) continue;
                string key = "combat.status." + CombatLogKeys.StatusId(s);
                Assert.IsTrue(UkrainianText.Has(key, Gender.Male), "стан " + s + " без тексту: " + key);
            }
            foreach (DamageType d in Enum.GetValues(typeof(DamageType)))
            {
                string key = "combat.damage_type." + CombatLogKeys.DamageTypeId(d);
                Assert.IsTrue(UkrainianText.Has(key, Gender.Male), "тип шкоди " + d + " без тексту: " + key);
            }
        }

        [Test]
        public void Chance_FollowsHitRule()
        {
            var args = new Dictionary<string, string> { { "unitId", "a" }, { "targetId", "b" }, { "chance", "56" }, { "damage", "0" } };
            string percent = BattleLogText.Line(Entry(CombatLogKeys.AttackMiss, args), true, id => id == "a" ? "Бурунда" : "Провідник", id => false);
            string threshold = BattleLogText.Line(Entry(CombatLogKeys.AttackMiss, args), false, id => id == "a" ? "Бурунда" : "Провідник", id => false);

            Assert.AreEqual("Бурунда → Провідник: промах (шанс 56%).", percent);
            Assert.AreEqual("Бурунда → Провідник: промах (поріг 56).", threshold);
        }

        [Test]
        public void Status_IsNamedInUkrainian_NotByEnum()
        {
            var args = new Dictionary<string, string> { { "unitId", "u_maksym" }, { "status", "knocked_down" }, { "turns", "2" } };
            string line = BattleLogText.Line(Entry(CombatLogKeys.StatusApplied, args), false, id => "Максим Беркут", id => false);

            Assert.AreEqual("Максим Беркут отримує стан «Збитий з ніг» (ходів: 2).", line);
        }

        // =====================================================================
        // Ядро: кожна строка трейсу — рівно один запис журналу
        // =====================================================================

        [Test]
        public void Journal_MirrorsTrace_LineForLine_WithKnownKeysAndRealUnits()
        {
            foreach (var cs in ScriptedBattles())
            {
                Assert.AreEqual(cs.Log.Count, cs.Journal.Count, "журнал і трейс розійшлися — хтось писав повз Record");
                Assert.Greater(cs.Journal.Count, 1, "бій мав щось записати");

                var ids = new HashSet<string>(cs.Units.Select(u => u.Id));
                foreach (var e in cs.Journal)
                {
                    CollectionAssert.Contains(CombatLogKeys.All, e.Key, "ключ поза закритим списком: " + e.Key);
                    foreach (var name in new[] { "unitId", "targetId" })
                    {
                        string id;
                        if (e.Args.TryGetValue(name, out id))
                            Assert.IsTrue(ids.Contains(id), e.Key + ": " + name + "=" + id + " — не юніт цього бою");
                    }
                }
            }
        }

        /// <summary>
        /// Сценарії, що разом зачіпають рамку бою, рух, атаки, стани (накладення,
        /// DoT, спад, збиття з ніг, оглушення), дозор (постановка, постріл, спад),
        /// падіння й автобій до кінця.
        /// </summary>
        private static IEnumerable<CombatState> ScriptedBattles()
        {
            var threshold = DefaultCombatContent.Training(Cfg, HitRuleKind.Threshold);
            CombatAi.AutoResolve(threshold);
            yield return threshold;

            var percent = DefaultCombatContent.Training(Cfg, HitRuleKind.Percent, new SeededDiceRoller(7));
            CombatAi.AutoResolve(percent);
            yield return percent;

            // Стани руками: DoT, спад, збиття з ніг, оглушення.
            var map = new GridMap(12, 1);
            var cs = new CombatState(map, Cfg, new ThresholdRule(Cfg), null);
            var actor = Unit("actor", Side.Player, 10);
            var other = Unit("other", Side.Player, 5);
            var dummy = Unit("dummy", Side.Enemy, 0, hp: 50);
            cs.AddUnit(actor, new GridPos(0, 0));
            cs.AddUnit(other, new GridPos(2, 0));
            cs.AddUnit(dummy, new GridPos(11, 0));
            cs.Begin();
            cs.ApplyStatus(other, StatusType.Bleeding);
            cs.ApplyStatus(other, StatusType.KnockedDown);
            cs.ApplyStatus(actor, StatusType.Stunned);
            cs.Move(new GridPos(1, 0));
            for (int i = 0; i < 8 && cs.Outcome == CombatOutcome.Ongoing; i++) cs.EndTurn();
            yield return cs;

            // Дозор: постановка і постріл по тому, хто зайшов у сектор.
            var owMap = new GridMap(8, 1);
            var ow = new CombatState(owMap, Cfg, new ThresholdRule(Cfg), null);
            var watcher = Unit("watcher", Side.Player, 10, weapon: DefaultCombatContent.HordeBow());
            var runner = Unit("runner", Side.Enemy, 5, weapon: DefaultCombatContent.HordeSpear());
            ow.AddUnit(watcher, new GridPos(0, 0));
            ow.AddUnit(runner, new GridPos(7, 0));
            ow.Begin();
            ow.Overwatch(new GridPos(7, 0));
            ow.Move(new GridPos(4, 0));
            yield return ow;
        }

        // =====================================================================
        // GameSession: BattleView.Log — журнал, а не трейс, і він український
        // =====================================================================

        [Test]
        public void GameSession_BattleViewLog_IsJournal_RenderedInUkrainian()
        {
            var s = new GameSession();
            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold });
            s.CombatEndTurn();
            s.CombatAttack("training_scout_1");
            s.CombatEndTurn();

            var view = s.GetBattleView();
            Assert.IsNotNull(view);
            Assert.IsNotNull(view.Log);
            Assert.AreEqual(CombatLogKeys.Started, view.Log[0].Key);
            Assert.IsTrue(view.Log.Any(l => l.Key.StartsWith("combat.log.attack.", StringComparison.Ordinal)),
                "атака лука мала лишити рядок журналу");
            foreach (var line in view.Log)
                CollectionAssert.Contains(CombatLogKeys.All, line.Key, "BattleView.Log несе не ключ журналу: " + line.Key);

            var rendered = BattleLogText.RecentLines(view, int.MaxValue, Gender.Female);
            Assert.AreEqual(view.Log.Count, rendered.Count);
            Assert.AreEqual("Бій почався.", rendered[rendered.Count - 1], "найстаріший рядок — останній (найновіший першим)");
            AssertPlayerFacing(rendered);
            Assert.IsTrue(rendered.Any(l => l.StartsWith("Максим → Застрільник орди:", StringComparison.Ordinal) ||
                                            l.StartsWith("Максим → Розвідник орди:", StringComparison.Ordinal)),
                "атака лука має назвати обох учасників по-людськи:\n" + string.Join("\n", rendered));
        }

        /// <summary>
        /// Бої до кінця прямо на CombatState: після розв'язки GameSession уже
        /// прибирає бій (GetBattleView() == null), а тут потрібен повний журнал —
        /// з падіннями, станами, дозором і підсумком. Юніти ручних сценаріїв
        /// названі службовими id без ключа таблиці, тож ім'я підставляємо
        /// сталим — перевіряємо слова журналу, а не розв'язання імен.
        /// </summary>
        [Test]
        public void ScriptedBattles_EveryRenderedLineIsUkrainian()
        {
            foreach (var cs in ScriptedBattles())
            {
                var lines = cs.Journal
                    .Select(e => new BattleLogLineView { Round = e.Round, Key = e.Key, Args = e.Args })
                    .Select(e => BattleLogText.Line(e, cs.IsHitRulePercent, id => "Боєць", id => false))
                    .ToList();
                AssertPlayerFacing(lines);
            }
        }

        // =====================================================================
        // Імена юнітів бою
        // =====================================================================

        [Test]
        public void UnitName_ResolvesBattleIdsThroughTable()
        {
            var view = new BattleView
            {
                Units = new List<BattleUnitView>
                {
                    new BattleUnitView { Id = "u_maksym", DisplayNameKey = "Максим Беркут", Side = "Player" },
                    new BattleUnitView { Id = "u_" + GameSession.ProtagonistId, DisplayNameKey = "Протагоніст", Side = "Player" },
                    new BattleUnitView { Id = "burunda_1", DisplayNameKey = "burunda", Side = "Enemy" },
                    new BattleUnitView { Id = "defector_myroslava", DisplayNameKey = "Мирослава", Side = "Enemy" },
                    new BattleUnitView { Id = "trainee_1", DisplayNameKey = "Провідник", Side = "Player" },
                }
            };

            Assert.AreEqual("Максим Беркут", BattleLogText.UnitName(view, "u_maksym", Gender.Female));
            Assert.AreEqual("Провідниця", BattleLogText.UnitName(view, "u_" + GameSession.ProtagonistId, Gender.Female));
            Assert.AreEqual("Провідник", BattleLogText.UnitName(view, "u_" + GameSession.ProtagonistId, Gender.Male));
            Assert.AreEqual("Бурунда-бегадир", BattleLogText.UnitName(view, "burunda_1", Gender.Male));
            Assert.AreEqual("Мирослава", BattleLogText.UnitName(view, "defector_myroslava", Gender.Male));
            Assert.AreEqual("Провідник", BattleLogText.UnitName(view, "trainee_1", Gender.Male),
                "тренувальний юніт без ключа — його вже український DisplayNameKey");

            Assert.IsTrue(BattleLogText.UnitIsFemale("defector_myroslava", Gender.Male));
            Assert.IsTrue(BattleLogText.UnitIsFemale("u_" + GameSession.ProtagonistId, Gender.Female));
            Assert.IsFalse(BattleLogText.UnitIsFemale("u_maksym", Gender.Female));
        }

        // =====================================================================
        // Спільне
        // =====================================================================

        private static void AssertPlayerFacing(IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                Assert.IsFalse(string.IsNullOrEmpty(line), "порожній рядок журналу");
                Assert.IsFalse(RussianOnlyLetters.IsMatch(line), "російський текст у журналі бою: " + line);
                Assert.IsFalse(line.Contains("{"), "незаповнений плейсхолдер: " + line);
                Assert.IsFalse(line.Contains("["), "заглушка відсутнього ключа: " + line);
                foreach (var raw in RawEnumNames)
                    Assert.IsFalse(line.Contains(raw), "сире ім'я enum у журналі: " + line);
            }
        }

        private static BattleLogLineView Entry(string key, IReadOnlyDictionary<string, string> args)
            => new BattleLogLineView { Round = 1, Key = key, Args = args };

        /// <summary>Усі імена аргументів, які ядро коли-небудь пише в журнал, — з правдоподібними значеннями.</summary>
        private static Dictionary<string, string> FullArgs() => new Dictionary<string, string>
        {
            { "unitId", "u_maksym" }, { "targetId", "burunda_1" }, { "abilityId", "ability.lunge" },
            { "status", "bleeding" }, { "damageType", "fire" }, { "chance", "56" }, { "damage", "4" },
            { "ap", "2" }, { "turns", "2" }, { "amount", "3" }, { "armor", "1" }, { "hp", "9" },
            { "hpMax", "14" }, { "round", "2" }, { "x", "3" }, { "y", "4" }
        };

        private static CombatUnit Unit(string id, Side side, int init, int hp = 10, WeaponDefinition weapon = null)
        {
            var p = new UnitProfile
            {
                DisplayName = id, MaxHp = hp, MaxAp = 8, Accuracy = 70,
                Initiative = init, Resolve = 0, MoveApPerTile = 1, CanBeDowned = side == Side.Player
            };
            return new CombatUnit(id, side, p, weapon);
        }
    }
}
