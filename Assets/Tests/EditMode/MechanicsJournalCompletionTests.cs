using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Checks;
using Game.Core.Combat;
using Game.Core.Expeditions;
using Game.Core.Items;
using Game.Core.Loop;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Пряме доручення власника (25.09.2026, дослівно): «Ти протестив що гру
    /// можна почати, створити персонажа, взяти в команду і тд та пройти першу
    /// кризу? Усі механіки можна виконати по журналу?».
    ///
    /// Один "журнальний гравець" — сценарій, що веде сесію рівно тими самими
    /// командами <see cref="GameSession"/>, якими водить справжня Unity-збірка
    /// (Gameplay/UI/*.cs → GameShell.cs → GameSession) — від титулу (створення
    /// персонажа НЕ пропущено, §7.19/R12) до вільної гри далеко за великим
    /// природним бунтом (Поправка №7.9), і наприкінці стверджує, що
    /// <see cref="GameSession.GetMechanicsJournal"/> позначає ВСІ 44 записи
    /// побаченими — саме те, що тестер бачить у вкладці «Журнал механік»
    /// (HubScreen.DrawMechanicsJournal).
    ///
    /// Дні 1–3 ведуться вручну (навмисно програний бій вузла 1 → полоса
    /// Base/Worst → "defector_seeded" → нічна конфронтація доби 3 →
    /// "звинуватити" → негайна дефекція) — жодна з готових ботівських
    /// політик (BotRunner.cs) не форсує саме цю гілку, а вона єдина, що
    /// доводить одразу defection/roster_drama/betrayal_confrontation. З доби 4
    /// сесію веде <see cref="BotRunner.Drive"/> під <see cref="JournalPolicy"/>
    /// (нижче) — рівно тими самими публічними командами, що й UI-кнопки.
    /// </summary>
    public class MechanicsJournalCompletionTests
    {
        private const int MaxDriveDays = 40;

        // ==== "журнальний гравець": один намір на всю решту партії =============

        /// <summary>
        /// Політика гравця, що йде по журналу (§ клас вище): тихо на кожному
        /// звичайному рішенні (кров вузла 1 і конфронтація доби 3 вже
        /// відіграні вручну до передачі керма сюди), кровавий фінал доби 5
        /// (readiness_finale/tactical_combat/auto_resolve), патруль по
        /// парних добах (night_patrol, а не "завжди" — той самий контраст,
        /// що AllMechanicsCoverageTests.Row07 тримає для Steward), жадібний
        /// данж (dungeon_delve/loot/craft-матеріал), «Автобій» усюди (решта
        /// тактичного бою і оверватч доводяться окремо, тренувальним боєм
        /// покроково — див. основний тест).
        /// </summary>
        private sealed class JournalPolicy : IBotPolicy
        {
            public string Name => "JournalPlayer";

            public IncidentPath ChooseIncidentPath(PendingOfferView offer)
            {
                if (offer != null && offer.TopicId == "finale") return IncidentPath.Bloody;
                return IncidentPath.Quiet;
            }

            public int ChooseQuestOption(QuestOfferView offer)
            {
                if (offer?.Options == null || offer.Options.Count == 0) return 0;
                if (offer.QuestId == BotSupport.DungeonEventQuestId)
                    return BotSupport.ClampIndex(0, offer.Options.Count);
                return 0;
            }

            public int ChooseSceneOption(SceneStepView step) => BotSupport.ChooseSceneDefault(step);

            public bool ChoosePatrol(SessionView view) => view.Day % 2 == 0;

            public IReadOnlyDictionary<string, string> ChooseAssignments(RosterView roster, CityView city)
                => BotSupport.DefaultAssignments(roster);

            public ExpeditionChoice? ChooseExpedition(SessionView view)
            {
                // Ні Мирослави (дефектила доба 3, вручну до передачі керма —
                // ExpeditionRunner.Depart сам відмовляє Antagonist, R73),
                // ні Максима (навмисно поранений тим самим кривавим вузлом
                // 1 — ExpeditionRunner.Depart відмовляє й IsInjured): у полі
                // лишається протагоніст + Захар (пост-НЕ-протагоніст,
                // здоровий і не в антагоністах — єдиний склад, з яким
                // вилазка справді відбувається щоразу).
                //
                // Чергуємо звичайну вилазку і данж (§6.1 №19/20/22, фікс-ревью
                // MechanicsJournalCompletionTests): дженерик-лут для крафту
                // крапає лише зі ЗВИЧАЙНОЇ вилазки на поверненні
                // (GameSession.TickExpeditionReturnIfAny -> DefaultItems.
                // DropTable().Roll(band)) — Delve цей шлях СВІДОМО пропускає
                // (ExpeditionRunner.Depart: "Delve пропускає резолв"), його
                // єдиний предмет — іменний scout_horn зі схованки (кімната 2),
                // який CraftSystem.TryUpgrade відмовляє одразу (IsNamed).
                // Без парного дня зі звичайною вилазкою "craft" фізично
                // недосяжний для політики, що завжди штовхає лише данж.
                return view.Day % 2 == 0
                    ? new ExpeditionChoice("outskirts", ExpeditionApproach.Quiet, new[] { GameSession.ProtagonistId, "zakhar" }, 2)
                    : new ExpeditionChoice(Game.Core.Dungeons.DefaultDungeon.AbandonedCamp,
                        ExpeditionApproach.Delve, new[] { GameSession.ProtagonistId, "zakhar" }, 2);
            }

            public CombatAction ChooseCombatAction(BattleView battle) => new CombatAction(CombatIntent.AttackNearest);

            public bool ChooseAutoResolve(BattleView battle) => true;

            public bool ChoosePushDeeper(DungeonView view) => true;
        }

        // ==== хелпери (той самий контракт, що GameSessionTests.cs) =============

        private static MechanicJournalEntryView FindJournalEntry(IReadOnlyList<MechanicJournalEntryView> journal, string id)
            => journal.First(e => e.Id == id);

        private static CompanionSummary FindCompanion(RosterView roster, string id)
            => roster.Companions.FirstOrDefault(c => c.Id == id);

        /// <summary>Доганяє поточну сцену до кінця, беручи варіант 0 на кожному Choice-кроці — зміст вибору тут не важливий, лише те, що сцена доходить до фінішу (portrait_scenes) і хоч раз обирає (dialogue_choice).</summary>
        private static SceneStepView RunSceneToFinish(GameSession s)
        {
            SceneStepView step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            return step;
        }

        /// <summary>Доганяє звичайну (не сюжетну) чергу рішень фази тихим шляхом.</summary>
        private static void ResolveAllQuiet(GameSession s, DayReportView report)
        {
            while (report != null && report.AwaitsDecision)
                report = s.ResolveIncident(IncidentPath.Quiet);
        }

        /// <summary>
        /// Веде бій вузла 1 ПОКРОКОВО, наївною тактикою обох сторін
        /// (BotRunner.ExecuteCombatAction: "йди до найближчого і бий" —
        /// ТА САМА команда, якою всі 5 існуючих бот-політик грають звичайний
        /// бій, не "розумний" CombatAi.TryAct/SmartAiTurn автобою). Емпірично
        /// (25.09.2026, ArcDebugTests, вилучені після діагностики):
        /// автобій (CombatAi.AutoResolve) на цій парі 3v2 дає полосу Good
        /// БЕЗ жодної жертви (§Row32/33 коментар класу — "здоровий баланс"
        /// після фіксу Accuracy); чиста пасивність гравця (CombatEndTurn на
        /// кожному ході) дає полосу Base/Worst, але через ~11 ударів по
        /// Максиму НАСПРАВДІ його вбиває (cas.Dead, не Downed) — назавжди
        /// обриває його арку (CompanionArc.Refresh: Antagonist/Dead -> Aborted),
        /// а "companion_arc"/"arc_chapter" стають фізично недосяжними
        /// (єдині два визначені CompanionArc — Мирослава й Максим, і Мирослава
        /// того самого дня піде в антагоністи розв'язкою вузла 1 + конфронтацією
        /// доби 3). Наївна тактика ОБОХ сторін — єдина з трьох перевірених, що
        /// дає полосу Base (миша.left_settlement -> "рана виконавцю") ЖИВИМ
        /// Максимом (scar.granted, не companion.died) — саме цим шляхом
        /// журнальний гравець і веде цей бій.
        /// </summary>
        private static List<string> FightBattleNaivelyCollectingCombatKeys(GameSession s)
        {
            var keys = new List<string>();
            int guard = 0;
            while (s.State == SessionState.Battle && guard++ < 2000)
            {
                int before = s.DayLog.Count;
                var view = s.GetBattleView();
                var current = BotSupport.FindCurrent(view);
                var target = current != null ? BotSupport.FindNearestOpposite(view, current) : null;
                if (target == null || s.CombatAttack(target.Id) != CombatActionResult.Success)
                {
                    var stepPos = current != null && target != null ? BotSupport.StepToward(view, current, target.Pos) : null;
                    if (!stepPos.HasValue || s.CombatMove(new GridPos(stepPos.Value.X, stepPos.Value.Y)) != CombatActionResult.Success)
                        s.CombatEndTurn();
                }

                for (int i = before; i < s.DayLog.Count; i++)
                    if (s.DayLog[i].Key != null && s.DayLog[i].Key.StartsWith("combat.attack.", StringComparison.Ordinal))
                        keys.Add(s.DayLog[i].Key);
            }
            Assert.AreEqual(SessionState.Scene, s.State, "бій вузла 1 мав завершитись розв'язкою (Scene) до ліміту guard");
            return keys;
        }

        private static IReadOnlyDictionary<string, string> AssignAll(GameSession s)
        {
            var plan = BotSupport.DefaultAssignments(s.GetRosterView());
            foreach (var kv in plan) s.Assign(kv.Key, kv.Value);
            return plan;
        }

        // =====================================================================

        [Test]
        public void JournalPlayer_OnePlaythrough_SeesAllFortyFourEntries()
        {
            var unseenTrail = new List<string>(); // компактний журнал команд по добах — повертається в описі результату через Assert.Fail на кінці, якщо щось не зійшлось.
            var commandLog = new List<string>();
            void Cmd(string s0) { commandLog.Add(s0); }

            // ---- Титул -> Створення (R12, НЕ пропущено) ------------------------
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold, Seed = 1 });
            Assert.AreEqual(SessionState.Creation, s.State);
            Cmd("Title: «Нова гра» (Пропустити створення = вимкнено)");

            s.SetProtagonistName("Ярина");
            s.SetProtagonistGender(Gender.Female);
            // Учень знахарки (Healer) — навмисно НЕ Warrior: наївна тактика
            // бою вузла 1 нижче рахує саме на слабшого в бою протагоніста —
            // з Warrior (Melee 6) той самий наївний бій вигравається чисто
            // (Good, перевірено емпірично), і полоса Base/Worst (потрібна
            // для betrayal_confrontation/defection) не настає ніколи.
            s.SetProtagonistBackground(Backgrounds.Healer().Id);
            s.ConfirmCreation();
            Cmd("Creation: ім'я «Ярина», передісторія healer, «Вирушати»");
            Assert.IsTrue(FindJournalEntry(s.GetMechanicsJournal(), "creation").Seen, "creation мав стати Seen одразу після ConfirmCreation");

            // ---- Відкриваюча портретна сцена -----------------------------------
            Assert.AreEqual(SessionState.Scene, s.State);
            RunSceneToFinish(s);
            Cmd("Scene(opening): «Далі»/вибір до кінця");
            Assert.AreEqual(SessionState.Morning, s.State);
            Assert.IsTrue(FindJournalEntry(s.GetMechanicsJournal(), "portrait_scenes").Seen, "portrait_scenes (scene.finished) мав прийти з відкриваючою сценою");

            // ---- Доба 1: розстановка, кривавий шлях, НАВМИСНО програний бій ----
            var plan1 = AssignAll(s);
            Cmd("Morning d0: Пости — " + string.Join(", ", plan1.Select(kv => kv.Key + "->" + kv.Value)));
            Assert.IsTrue(FindJournalEntry(s.GetMechanicsJournal(), "assignment").Seen, "assignment (assign.made) мав прийти з першою розстановкою");

            s.ConfirmMorning();
            var d1 = s.AdvanceDay();
            Cmd("Morning d0: «Почати день»");
            Assert.IsTrue(d1.AwaitsDecision, "вузол 1 (доба 1) мав відкрити рішення (Перевал)");

            s.ResolveIncident(IncidentPath.Bloody);
            Assert.AreEqual(SessionState.Battle, s.State);
            Cmd("Decision(вузол 1): «Криваво»");

            var combatKeysDay1 = FightBattleNaivelyCollectingCombatKeys(s);
            Cmd("Battle(вузол 1, покроково): «Атакувати»/«Рух» наївною тактикою обох сторін — до Base/Worst без смерті Максима");
            Assert.Greater(combatKeysDay1.Count, 0, "покроковий бій вузла 1 мав дати хоч один combat.attack.* — доказ, що це НЕ автобій");

            bool sawLeft = s.DayLog.Any(e => e.Key == "companion.left_settlement" && e.Args != null && e.Args.TryGetValue("companionId", out var cid) && cid == "myroslava");
            Assert.IsTrue(sawLeft, "полоса Base/Worst мала посіяти «Мирослава йде за батьком» — сценарій рахує саме на цю гілку для betrayal_confrontation/defection");

            bool maksymDied = s.DayLog.Any(e => e.Key == "companion.died" && e.Args != null && e.Args.TryGetValue("companionId", out var mid) && mid == "maksym");
            Assert.IsFalse(maksymDied, "наївна тактика обох сторін мала лишити Максима пораненим (scar.granted), не мертвим — інакше companion_arc/arc_chapter стають недосяжними (єдина його арка Aborted)");

            RunSceneToFinish(s);
            Cmd("Scene(розв'язка вузла 1): «Далі» до кінця");
            Assert.AreEqual(SessionState.Evening, s.State);

            s.SetPatrol(true); // night_patrol
            s.ConfirmEvening();
            Cmd("Evening d1: Патруль = так, «Підтвердити вечір»");
            var n1 = s.AdvanceNight();
            ResolveAllQuiet(s, n1);
            Cmd("Night d1: «Пройти ніч» (тихо на будь-яких рішеннях)");
            Assert.IsTrue(FindJournalEntry(s.GetMechanicsJournal(), "night_patrol").Seen, "night_patrol мав стати Seen після патрульної ночі 1");

            // ---- Доба 2: тихо, спимо (контраст парності для night_patrol/forewarn) ----
            Assert.IsTrue(s.State == SessionState.Morning || s.State == SessionState.FreePlay);
            AssignAll(s);
            s.ConfirmMorning();
            var d2 = s.AdvanceDay();
            ResolveAllQuiet(s, d2);
            Cmd("Day d2: тихо на всіх рішеннях");
            if (s.State == SessionState.Scene) RunSceneToFinish(s);
            s.SetPatrol(false);
            s.ConfirmEvening();
            Cmd("Evening d2: Патруль = ні (спимо), «Підтвердити вечір»");
            var n2 = s.AdvanceNight();
            ResolveAllQuiet(s, n2);
            Cmd("Night d2: «Пройти ніч»");
            Assert.AreEqual(2, s.CurrentView.Day);

            // ---- Доба 3, увечері: конфронтація Мирослави («Нічна розмова») -----
            AssignAll(s);
            s.ConfirmMorning();
            var d3 = s.AdvanceDay();
            ResolveAllQuiet(s, d3);
            Cmd("Day d3: тихо на всіх рішеннях");
            if (s.State == SessionState.Scene) RunSceneToFinish(s);
            Assert.AreEqual(SessionState.Evening, s.State);
            Assert.AreEqual(3, s.CurrentView.Day);

            var confrontation = s.OfferMyroslavaEveningScene();
            Assert.IsNotNull(confrontation, "зрада насуває (defector_seeded) — доба 3 мала відкрити саме конфронтацію");
            Cmd("Evening d3: «Нічна розмова» з Мирославою відкрилась сама");
            while (confrontation != null && !confrontation.IsChoice && !confrontation.IsFinished)
                confrontation = s.AdvanceScene();
            Assert.IsTrue(confrontation.IsChoice, "конфронтація зупиняється на виборі (переконати/звинуватити/відпустити)");

            int accuseIndex = -1;
            for (int i = 0; i < confrontation.Options.Count; i++)
                if (confrontation.Options[i].SkillKey == SkillKeys.Intimidate.Id) accuseIndex = i;
            Assert.GreaterOrEqual(accuseIndex, 0, "варіант «звинуватити» (Залякування) мав бути серед опцій конфронтації");

            var afterChoice = s.ChooseSceneOption(accuseIndex);
            Cmd("Scene(конфронтація): вибір «Звинуватити» (Залякування)");
            while (afterChoice != null && !afterChoice.IsFinished)
                afterChoice = afterChoice.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();

            Assert.IsTrue(s.DayLog.Any(e => e.Key == "scene.betrayal_confrontation.begun"), "betrayal_confrontation: сцена конфронтації мала залогуватись як «насувана зрада», не тиха перевірка");
            Assert.IsTrue(s.DayLog.Any(e => e.Key == "companion.defected" && e.Args != null && e.Args.TryGetValue("companionId", out var did) && did == "myroslava"), "«звинуватити» мало дефектити Мирославу негайно");
            Assert.IsTrue(s.DayLog.Any(e => e.Key != null && e.Key.StartsWith("roster.rippled", StringComparison.Ordinal)), "дефекція мала дати рябь по решті ростера (roster_drama)");

            var journalAfterDay3 = s.GetMechanicsJournal();
            foreach (var id in new[] { "betrayal_confrontation", "defection", "roster_drama", "dialogue_choice" })
                Assert.IsTrue(FindJournalEntry(journalAfterDay3, id).Seen, id + " мав стати Seen одразу після вибору «звинуватити»");

            Assert.AreEqual(Game.Core.Characters.CompanionStatus.Antagonist, FindCompanion(s.GetRosterView(), "myroslava")?.Status);

            AssignAll(s);
            s.SetPatrol(true);
            s.ConfirmEvening();
            Cmd("Evening d3: Патруль = так, «Підтвердити вечір»");
            var n3 = s.AdvanceNight();
            ResolveAllQuiet(s, n3);
            Cmd("Night d3: «Пройти ніч»");
            Assert.AreEqual(3, s.CurrentView.Day, "рівно доба 3 завершена вручну — з доби 4 керма бере JournalPolicy/BotRunner");

            // ---- «Взяти в команду» / роста: перевірити ЩО САМЕ це в цій збірці ----
            // (див. текстовий підсумок у StructuredOutput нижче) — каст фіксований
            // (Поправка №5), новий Companion за грою не з'являється; "команда" —
            // це РОЗСТАНОВКА наявних іменних напарників по постах/вилазці/партії,
            // не вербування. Перевіряємо факт: склад ростера НЕ змінився кількісно
            // (крім статусу Мирослави), а призначення (Assign) — ЄДИНИЙ спосіб
            // "додати в команду" когось на пост.
            var rosterAfterD3 = s.GetRosterView();
            Assert.AreEqual(6, rosterAfterD3.Companions.Count, "каст фіксований (Поправка №5): протагоніст + 5 іменних напарників (zakhar/keeper/healer/maksym/myroslava) — ніхто новий не приєднався і ніхто фізично не зник з ростера (Мирослава лишається записом, лише зі статусом Antagonist)");

            // ---- З доби 4 — JournalPolicy/BotRunner тими самими командами UI ----
            var policy = new JournalPolicy();
            var fullLog = new List<GameEvent>();
            bool equipped = false, crafted = false, trainedMidGame = false, savedAndLoaded = false;
            int daysDriven = 0;

            while (daysDriven < MaxDriveDays)
            {
                BotRunner.Drive(s, policy, 1, fullLog);
                daysDriven++;
                Cmd("BotRunner d" + s.CurrentView.Day + ": розстановка/рада/вилазка/данж/ніч/рішення — JournalPolicy");

                if (s.State != SessionState.Morning && s.State != SessionState.FreePlay) continue;

                // ---- гір/крафт при першій нагоді ----
                if (!equipped || !crafted)
                {
                    var stash = s.GetStash();
                    if (stash != null && stash.Count > 0)
                    {
                        if (!equipped)
                        {
                            var roster = s.GetRosterView();
                            foreach (var item in stash)
                            {
                                bool done = false;
                                foreach (var c in roster.Companions)
                                    if (s.Equip(c.Id, item.InstanceId, item.Slot)) { done = true; break; }
                                if (done) { equipped = true; Cmd("Спорядження d" + s.CurrentView.Day + ": «Одягнути» " + item.Definition.Id); break; }
                            }
                        }
                        if (!crafted)
                        {
                            var city = s.GetCityView();
                            bool workshopBuilt = city?.Built != null && city.Built.Any(b => b.Id == Game.Core.Base.DefaultBuildings.Workshop);
                            if (workshopBuilt)
                                foreach (var item in s.GetStash())
                                    if (s.CraftUpgrade(item.InstanceId) == CraftResult.Success)
                                    { crafted = true; Cmd("Спорядження d" + s.CurrentView.Day + ": «Покращити» " + item.Definition.Id); break; }
                        }
                    }
                }

                // ---- тренувальний бій ПОСЕРЕД партії (вкладка Готовність) — покроково, для оверватчу ----
                if (!trainedMidGame)
                {
                    s.NewTrainingBattle(new TrainingBattleOptions { HitRule = s.HitRule });
                    Cmd("Готовність d" + s.CurrentView.Day + ": «Тренувальний бій»");
                    Assert.AreEqual(SessionState.Battle, s.State);
                    var trainingLog = new List<GameEvent>();
                    BotRunner.Drive(s, new StepwiseOverwatchPolicy(), 0, trainingLog);
                    fullLog.AddRange(trainingLog);
                    Cmd("Battle(тренування, покроково): Дозор/Атака/Кінець ходу через BotRunner");
                    trainedMidGame = true;
                    Assert.IsTrue(s.State == SessionState.Morning || s.State == SessionState.FreePlay, "тренувальний бій мав повернути в ту саму фазу кампанії, не в Title");
                }

                // ---- Збереження -> «титул» -> Продовжити (в НОВИЙ інстанс, як і реальний relaunch) ----
                if (!savedAndLoaded)
                {
                    string blob = s.SaveState(0);
                    Cmd("Збереження d" + s.CurrentView.Day + ": слот 0, «Підтвердити»");

                    var reloaded = new GameSession();
                    reloaded.PreloadSlot(0, blob);
                    bool ok = reloaded.ContinueGame(0);
                    Assert.IsTrue(ok, "«Продовжити» зі слота 0 мало вдатись у свіжому інстансі (FreshSessionRestoreTests-контракт)");
                    Cmd("Title: «Продовжити» (свіжий запуск застосунку, слот 0)");

                    // Прогрес журналу ДО збереження мав пережити Save/Load —
                    // саме той трап, на який натякали FACTS: NewGame() у
                    // ContinueGame() безумовно чистить _seenEventKeys ДО
                    // LoadState() — без фіксу ComposeSave/ApplySave (journalSeen=)
                    // усе побачене тут стало б знову непобаченим.
                    var beforeIds = s.GetMechanicsJournal().Where(e => e.Seen).Select(e => e.Id).ToList();
                    var afterIds = new HashSet<string>(reloaded.GetMechanicsJournal().Where(e => e.Seen).Select(e => e.Id));
                    var lost = beforeIds.Where(id => !afterIds.Contains(id)).ToList();
                    Assert.IsEmpty(lost, "Save/Load через титул НЕ мав загубити вже побачені записи журналу: " + string.Join(",", lost));
                    Assert.IsTrue(FindJournalEntry(reloaded.GetMechanicsJournal(), "save_load").Seen, "save_load мав стати Seen одразу після ContinueGame (game.saved+game.loaded)");

                    s = reloaded; // партія триває у щойно завантаженому інстансі — той самий "один прогін", як після реального relaunch.
                    savedAndLoaded = true;
                }

                var seenNow = s.GetMechanicsJournal();
                if (seenNow.All(e => e.Seen)) break; // усе побачено — далі ганяти дні нема сенсу
            }

            // ---- Підсумок / вільна гра: доганяємо, якщо цикл вище вийшов по MaxDriveDays, а Summary/FreePlay ще не діткнуто явно ----
            for (int guard = 0; guard < 10 && s.State != SessionState.Summary && s.State != SessionState.FreePlay && !FindJournalEntry(s.GetMechanicsJournal(), "free_play").Seen; guard++)
                BotRunner.Drive(s, policy, 1, fullLog);

            if (s.State == SessionState.Summary)
            {
                s.AcknowledgeSummary();
                Cmd("Summary d" + s.CurrentView.Day + ": «Грати далі»");
            }

            var finalJournal = s.GetMechanicsJournal();
            var stillUnseen = finalJournal.Where(e => !e.Seen).Select(e => e.Id).ToList();

            Assert.IsEmpty(stillUnseen,
                "журнальний гравець мав побачити ВСІ 44 записи за один прогін (" + daysDriven + " днів бот-водія + 3 ручні); " +
                "непобачені: " + string.Join(", ", stillUnseen) + " | команди: " + string.Join(" | ", commandLog));

            Assert.AreEqual(44, finalJournal.Count, "реєстр журналу (MechanicsJournalRegistry) мав лишитись з 44 записами — тест писано під це число з FACTS завдання");
        }

        /// <summary>
        /// Той самий прийом, що AllMechanicsCoverageTests.Row33_Overwatch_Triggered:
        /// у першому раунді бою — «Дозор», далі — штовхаємо вперед, щоб
        /// той, хто чекає в дозорі, отримав шанс СПРАЦЮВАТИ на чужому русі
        /// (BotRunner.ExecuteCombatAction сам перекладає намір Overwatch/
        /// AttackNearest у CombatMove/CombatAttack/CombatEnterOverwatch).
        /// </summary>
        private sealed class StepwiseOverwatchPolicy : IBotPolicy
        {
            public string Name => "StepwiseOverwatch";
            public IncidentPath ChooseIncidentPath(PendingOfferView offer) => IncidentPath.Quiet;
            public int ChooseQuestOption(QuestOfferView offer) => 0;
            public int ChooseSceneOption(SceneStepView step) => 0;
            public bool ChoosePatrol(SessionView view) => false;
            public IReadOnlyDictionary<string, string> ChooseAssignments(RosterView roster, CityView city) => null;
            public ExpeditionChoice? ChooseExpedition(SessionView view) => null;
            public CombatAction ChooseCombatAction(BattleView battle) => new CombatAction(CombatIntent.Overwatch);
            public bool ChooseAutoResolve(BattleView battle) => false;
            public bool ChoosePushDeeper(DungeonView view) => false;
        }
    }
}
