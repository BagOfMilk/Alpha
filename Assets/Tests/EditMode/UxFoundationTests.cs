using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Core.Session;
using Game.Gameplay.Text;
using Game.Gameplay.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Фундамент UX поза HUD (docs/UX_DESIGN.md §7, крок U2): каталог панелей,
    /// реєстр клавіш, людські відмови, стек шарів, тости. Кожен охоронець
    /// перевірено навмисною мутацією (статут PROC-04) — див. коментарі.
    /// </summary>
    public class UxFoundationTests
    {
        private static readonly Regex Latin = new Regex("[A-Za-z]");

        // ---------------- каталог панелей (UX_DESIGN §3.2) ----------------

        [Test]
        public void PanelCatalog_EveryPanelHasOneEntry_UniqueSlug_AndUkrainianTitle()
        {
            // Мутація: прибрати рядок каталогу або ключ ux.panel.* — тест падає.
            var ids = Enum.GetValues(typeof(UxPanelId)).Cast<UxPanelId>().Where(i => i != UxPanelId.None).ToList();
            foreach (var id in ids)
                Assert.That(UxPanelCatalog.All.Count(e => e.Id == id), Is.EqualTo(1), "панель без рядка каталогу: " + id);

            var slugs = UxPanelCatalog.All.Select(e => e.Slug).ToList();
            Assert.That(slugs.Distinct().Count(), Is.EqualTo(slugs.Count), "повторений слаг панелі");

            foreach (var e in UxPanelCatalog.All)
                Assert.IsTrue(UkrainianText.Has(e.TitleKey, false), "немає тексту заголовка " + e.TitleKey);
        }

        [Test]
        public void PanelCatalog_EveryLegacyHubTab_HasExactlyOneHome()
        {
            // Мутація: віддати вкладці 9 (Збереження) −1 — «функція без домівки».
            for (int tab = 0; tab < UxPanelCatalog.LegacyHubTabCount; tab++)
                Assert.That(UxPanelCatalog.All.Count(e => e.LegacyHubTab == tab), Is.EqualTo(1),
                    "вкладка старого хаба " + tab + " має мати рівно одну нову домівку (UX_DESIGN §3.2)");
            Assert.AreEqual(UxPanelId.None, UxPanelCatalog.ForLegacyHubTab(UxPanelCatalog.LegacyHubTabCount));
        }

        [Test]
        public void PanelCatalog_EveryHotkey_IsOwnedByGameShell()
        {
            foreach (var e in UxPanelCatalog.All.Where(e => e.Hotkey != null))
                Assert.IsTrue(UxKeyMap.IsOwner(e.Hotkey, "GameShell.cs"),
                    "клавіша панелі " + e.Slug + " (" + e.Hotkey + ") не записана за GameShell у реєстрі клавіш");
        }

        // ---------------- клавіші (статут UI-11) ----------------

        [Test]
        public void KeyMap_OneOwnerPerKeyAndScope()
        {
            // Мутація: другий рядок «Tab / World» за HeroWalker — тест падає.
            var dup = UxKeyMap.All.GroupBy(b => b.Key + "|" + b.Scope).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.IsEmpty(dup, "у клавіші в одній області два власники: " + string.Join(", ", dup));
        }

        [Test]
        public void KeyMap_ComputedAbilityKeys_AreAllRegistered()
        {
            // BattleHudScreen читає Alpha1 + i для дев'яти здібностей — сканер бачить
            // лише Alpha1. Мутація: прибрати рядок Alpha5 — тест падає.
            for (int i = 1; i <= 9; i++)
                Assert.IsTrue(UxKeyMap.IsOwner("Alpha" + i, "BattleHudScreen.cs"), "Alpha" + i);
            StringAssert.Contains("KeyCode.Alpha1 + i", File.ReadAllText(Path.Combine(GameplayDir(), "UI", "BattleHudScreen.cs")).Replace("(int)", ""),
                "BattleHudScreen більше не рахує клавіші від Alpha1 — перевір реєстр");
        }

        [Test]
        public void KeyMap_EveryKeyCodeReadInGameplay_IsRegisteredForThatFile()
        {
            // Мутація: додати `KeyCode.J` у BattleHudScreen.cs — файл не записаний
            // власником J, тест падає. Реєстр — перший, хто дізнається про нову клавішу.
            var usage = new Regex(@"KeyCode\.(\w+)");
            var problems = new List<string>();
            foreach (var path in Directory.GetFiles(GameplayDir(), "*.cs", SearchOption.AllDirectories))
            {
                string file = Path.GetFileName(path);
                if (file == "UxKeyMap.cs") continue;
                foreach (Match m in usage.Matches(StripComments(File.ReadAllText(path))))
                    if (!UxKeyMap.IsOwner(m.Groups[1].Value, file))
                        problems.Add(file + " читає KeyCode." + m.Groups[1].Value);
            }
            Assert.IsEmpty(problems.Distinct().ToList(),
                "клавіші поза реєстром UxKeyMap (UX_DESIGN §3.6):\n" + string.Join("\n", problems.Distinct()));
        }

        // ---------------- моделі без рушія (HUD HP-4) ----------------

        [Test]
        public void UxModels_AreEngineFree()
        {
            // Мутація: `using UnityEngine;` в UxModels.cs — тест падає.
            var files = Directory.GetFiles(Path.Combine(GameplayDir(), "UI", "Models"), "Ux*.cs");
            Assert.That(files.Length, Is.GreaterThanOrEqualTo(6), "моделі UX не знайдено — змінився шлях?");
            foreach (var f in files)
                StringAssert.DoesNotContain("UnityEngine", StripComments(File.ReadAllText(f)), Path.GetFileName(f));
        }

        // ---------------- людські відмови (UX-11) ----------------

        [Test]
        public void ErrorText_StateGateFromRealSession_BecomesHumanPhrase()
        {
            // Справжній виняток ядра: рада в стані «титул» (Morning/FreePlay потрібні).
            // Мутація: повернути ex.Message у Humanize — латиниця на екрані, тест падає.
            var s = new GameSession();
            string raw = CatchMessage(() => s.OrderRaid());
            StringAssert.Contains("Title", raw, "формат винятку ядра змінився — перевір UxErrorText");

            string human = UxErrorText.Humanize(raw, false);
            Assert.IsFalse(Latin.IsMatch(human), "латиниця на екрані: " + human);
            StringAssert.Contains("вранці", human);
            StringAssert.Contains("у вільній грі", human);
            Assert.AreEqual(UxErrorText.OnlyIn(new[] { SessionState.Morning, SessionState.FreePlay }, false), human,
                "та сама відмова з ядра і з перевірки картки мусить звучати однаково");
        }

        [Test]
        public void ErrorText_KnownCoreMessages_NeverShowLatinOrRuleCodes()
        {
            // Тексти винятків GameSession (станом на 29.09.2026) — усе, що могло
            // дійти до рядка відгуку. Мутація: прибрати гілку «лише в Morning/FreePlay» —
            // збереження показало б латиницю.
            string[] raws =
            {
                "Команда недоступна у стані Evening (потрібен Morning).",
                "Команда недоступна у стані FreePlay (потрібен один з: Morning, Evening, Night).",
                "Збереження лише в Morning/FreePlay (R13).",
                "ConfirmMorning лише з Morning/FreePlay.",
                "Доба 5, ніч: спершу ResolveFinale(path) — фінал не можна пропустити мовчки (R8, §7.15 «жодна полоса не чиста перемога»).",
                "PercentRule потребує IDiceRoller, injected ззовні (Game.Gameplay.Combat.SeededDiceRoller) у конструктор GameSession або NewGameOptions.Roller — Core сам кубик не створює (R1).",
                "Ця глава — квестова (BeginArcChapterQuest), не сценова.",
                "Вікно реакції на кризу закрите.",
                "Фінал лише на добу 5, вночі.",
            };
            foreach (var raw in raws)
            {
                string human = UxErrorText.Humanize(raw, false);
                Assert.IsFalse(Latin.IsMatch(human), raw + " → " + human);
                StringAssert.DoesNotContain("(R", human, raw);
                Assert.IsNotEmpty(human, raw);
            }
            Assert.AreEqual("Вікно реакції на кризу закрите.", UxErrorText.Humanize("Вікно реакції на кризу закрите.", false),
                "українське речення ядра показується як є");
            StringAssert.Contains("у вільній грі", UxErrorText.Humanize("Збереження лише в Morning/FreePlay (R13).", false));
            // Збереження іншої версії — назавжди, а не «зараз» (рев'ю U2): окремий текст.
            string save = UxErrorText.Humanize("Збереження іншої версії гри: alpha0", false);
            StringAssert.Contains("іншою версією", save);
            Assert.AreNotEqual(UkrainianText.Get("ux.error.generic", false), save);
        }

        [Test]
        public void Invoke_ChecksStateBeforeCallingCore()
        {
            // Мутація: прибрати перевірку AllowedStates в UxCommandRunner.Invoke —
            // ядро кличеться в чужому стані, тест падає.
            bool called = false;
            var action = new UxAction { Id = "test.raid", Label = "Облава", Execute = () => { called = true; return UxOutcome.Success(); } };
            action.AllowedStates.Add(SessionState.Morning);
            action.AllowedStates.Add(SessionState.FreePlay);

            var refused = UxCommandRunner.Invoke(action, SessionState.Evening, false);
            Assert.IsFalse(refused.Ok);
            Assert.IsFalse(called, "у чужому стані ядро не кличеться");
            StringAssert.Contains("вранці", refused.Refusal);

            var ok = UxCommandRunner.Invoke(action, SessionState.Morning, false);
            Assert.IsTrue(ok.Ok);
            Assert.IsTrue(called);
        }

        [Test]
        public void Invoke_DisabledReason_IsShownAndCoreNotCalled()
        {
            bool called = false;
            var action = new UxAction { Id = "test.build", DisabledReason = "Бракує 5 будматеріалу.", Execute = () => { called = true; return UxOutcome.Success(); } };
            var r = UxCommandRunner.Invoke(action, SessionState.Morning, false);
            Assert.IsFalse(r.Ok);
            Assert.AreEqual("Бракує 5 будматеріалу.", r.Refusal);
            Assert.IsFalse(called);
        }

        [Test]
        public void Runner_CatchesCoreRefusal_KeepsRawOnlyForLog()
        {
            var s = new GameSession();
            var r = UxCommandRunner.Run(() => s.OrderRaid(), false);
            Assert.IsFalse(r.Ok);
            Assert.IsFalse(Latin.IsMatch(r.Refusal), r.Refusal);
            StringAssert.Contains("Title", r.RawError, "сирий текст лишається для логу");
        }

        [Test]
        public void InlineRefusals_LiveUnderTheirAction_UntilNextAction()
        {
            var refusals = new UxInlineRefusals();
            refusals.Record("a", UxOutcome.Refused("ні"));
            Assert.AreEqual("ні", refusals.For("a"));
            Assert.IsNull(refusals.For("b"));
            refusals.Record("b", UxOutcome.Success());
            Assert.IsNull(refusals.For("a"), "наступна дія знімає стару відмову");
        }

        // ---------------- стек шарів і Esc (UX-05) ----------------

        [Test]
        public void Layers_Escape_ClosesConfirmThenPanelThenTogglesPause_NeverLeavesBuilding()
        {
            // Мутація: поміняти порядок перевірок у OnEscape — тест падає.
            var l = new UxLayerState();
            l.EnterInterior("council_hall");
            l.OpenPanelOf(UxPanelId.CouncilTable);
            l.AskConfirm();
            Assert.AreEqual(UxLayer.Confirm, l.Top);

            Assert.AreEqual(UxLayer.Confirm, l.OnEscape());
            Assert.AreEqual(UxLayer.Panel, l.Top);
            Assert.AreEqual(UxLayer.Panel, l.OnEscape());
            Assert.AreEqual(UxLayer.Interior, l.Top);
            Assert.AreEqual(UxLayer.Pause, l.OnEscape(), "без панелі Esc відкриває паузу");
            Assert.IsTrue(l.PauseOpen);
            Assert.AreEqual(UxLayer.Pause, l.OnEscape(), "і закриває її");
            Assert.IsTrue(l.InInterior, "Esc з будівлі не виводить — лише двері чи «Вийти»");
        }

        [Test]
        public void Layers_OnlyOnePanel_AndLeavingBuildingClosesItsPanel()
        {
            var l = new UxLayerState();
            l.OpenPanelOf(UxPanelId.People);
            l.OpenPanelOf(UxPanelId.Journal);
            Assert.AreEqual(UxPanelId.Journal, l.OpenPanel);

            l.EnterInterior("storehouse");
            l.OpenPanelOf(UxPanelId.Stash);
            l.ForceLeaveWorld();
            Assert.IsFalse(l.InInterior);
            Assert.AreEqual(UxPanelId.None, l.OpenPanel);
            Assert.AreEqual(UxLayer.Village, l.Top);
        }

        // ---------------- тости (§5.15) ----------------

        [Test]
        public void Toasts_OneAtATime_ForTheirDuration_OldestDroppedOnOverflow()
        {
            // Мутація: не обнуляти поточний тост після строку — другий не з'явиться.
            double now = 0;
            var q = new UxToastQueue(() => now, 3.5);
            Assert.IsNull(q.Current);

            q.Push("перший");
            q.Push("другий");
            Assert.AreEqual("перший", q.Current);
            now = 3.4;
            Assert.AreEqual("перший", q.Current);
            now = 3.6;
            Assert.AreEqual("другий", q.Current);
            now = 8;
            Assert.IsNull(q.Current);

            for (int i = 0; i < UxToastQueue.MaxQueued + 2; i++) q.Push("т" + i);
            Assert.AreEqual(UxToastQueue.MaxQueued, q.PendingCount);
            Assert.AreEqual("т2", q.Current, "найстаріші з надлишку відкинуто");
        }

        // ---------------- відмінювання діб ----------------

        [Test]
        public void DayCount_DeclinesLikeUkrainian_NoOneDib()
        {
            // Мутація: повернути шаблон «{days} діб» — тест падає (тур 29.09.2026: «1 діб», «за 3 діб»).
            Assert.AreEqual("1 доба", UkrainianText.DayCount(1));
            Assert.AreEqual("3 доби", UkrainianText.DayCount(3));
            Assert.AreEqual("5 діб", UkrainianText.DayCount(5));
            Assert.AreEqual("11 діб", UkrainianText.DayCount(11));
            Assert.AreEqual("14 діб", UkrainianText.DayCount(14));
            Assert.AreEqual("21 доба", UkrainianText.DayCount(21));
            Assert.AreEqual("22 доби", UkrainianText.DayCount(22));
            Assert.AreEqual("1 добу", UkrainianText.DayCount(1, accusative: true));

            var def = new Game.Core.Base.BuildingDefinition { Id = "infirmary", GoldCost = 30, Days = 4 };
            StringAssert.EndsWith(", 1 доба", ScreenText.BuildingCostLine(def, Game.Core.Characters.Creation.Gender.Male, true));
            StringAssert.EndsWith(", 4 доби", ScreenText.BuildingCostLine(def, Game.Core.Characters.Creation.Gender.Male));

            var evt = new GameEvent("arrivals.tavern.announced", 3, Game.Core.Loop.DayPhase.Day,
                new Dictionary<string, string> { { "companionId", "goban" }, { "days", "3" } });
            StringAssert.Contains("за 3 доби", ScreenText.EventLine(evt, Game.Core.Characters.Creation.Gender.Male, null));
        }

        // ---------------- допоміжне ----------------

        private static string CatchMessage(Action a)
        {
            try { a(); }
            catch (InvalidOperationException ex) { return ex.Message; }
            Assert.Fail("очікувалась відмова ядра");
            return null;
        }

        private static string GameplayDir() =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets", "_Project", "Scripts", "Gameplay");

        private static string StripComments(string src)
        {
            src = Regex.Replace(src, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            return Regex.Replace(src, @"//[^\n]*", string.Empty);
        }
    }
}
