using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Loop;
using Game.Core.Scenes;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Core.Story;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Склад кривавого фіналу (Поправка №17.2, власник 01.10.2026: «Гравець обирає отряд,
    /// але не може обрати тех хто на ролі назначений в місті»; ROADMAP M1.3) і супутні
    /// виправлення того самого шматка: B2 (прапор ради Захара мав читача), B3 (пом'якшення
    /// кризи золотом справді коштує), B5 (прев'ю ворогів = реальний бій).
    ///
    /// Кожен охоронець має мутацію (PROC-04), перевірену вручну: прибрати відповідний
    /// предикат/читача у <c>GameSession</c> — тест червоніє (див. підсумок у коміті).
    /// </summary>
    public class FinaleSquadTests
    {
        private const string PostedId = "maksym";
        private const string PostId = "scouting_post";

        // ---------- хелпери: доганяємо партію до ночі доби 5 ----------

        private static NewGameOptions Options() =>
            new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold, TestBuildOneDayConstruction = true };

        private static void RunSceneToFinish(GameSession s)
        {
            var step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
        }

        private static void PlayFullDayQuiet(GameSession s)
        {
            s.ConfirmMorning();
            var report = s.AdvanceDay();
            while (report != null && report.AwaitsDecision)
                report = s.ResolveIncident(IncidentPath.Quiet);
            if (report != null && report.Pending == null && s.State == SessionState.Scene)
                RunSceneToFinish(s);

            if (s.State == SessionState.Evening) s.ConfirmEvening();
            if (s.State == SessionState.Night)
            {
                var night = s.AdvanceNight();
                while (night != null && night.AwaitsDecision)
                    night = s.ResolveIncident(IncidentPath.Quiet);
            }
        }

        /// <summary>Ранок доби 5: четверта доба позаду, ConfirmMorning ще не викликано.</summary>
        private static GameSession ToDay5Morning()
        {
            var s = new GameSession();
            s.NewGame(Options());
            RunSceneToFinish(s);
            for (int day = 1; day <= 4; day++) PlayFullDayQuiet(s);
            Assert.AreEqual(SessionState.Morning, s.State);
            return s;
        }

        /// <summary>Від ранку доби 5 до відкритого вікна реакції на кризу (стан Evening).</summary>
        private static void ToCrisisWindow(GameSession s)
        {
            s.ConfirmMorning();
            var report = s.AdvanceDay();
            while (report != null && report.AwaitsDecision)
                report = s.ResolveIncident(IncidentPath.Quiet);
            if (s.State == SessionState.Scene) RunSceneToFinish(s);
        }

        private static void ClearAllPosts(GameSession s)
        {
            foreach (var c in s.GetRosterView().Companions.Where(c => !string.IsNullOrEmpty(c.AssignedSlotId)).ToList())
                s.Unassign(c.AssignedSlotId);
        }

        /// <summary>
        /// Ніч доби 5. <paramref name="morning"/> — налаштування вранці доби 5 (єдиний момент,
        /// коли посади міняються), <paramref name="beforeNight"/> — хук перед відкриттям ночі
        /// (прапори сюжету тощо).
        /// </summary>
        private static GameSession ToFinaleNight(Action<GameSession> morning = null, Action<GameSession> beforeNight = null)
        {
            var s = ToDay5Morning();
            morning?.Invoke(s);
            ToCrisisWindow(s);
            s.ReactToCrisis(CrisisReaction.SpendGold);
            beforeNight?.Invoke(s);
            s.ConfirmEvening();
            Assert.AreEqual(SessionState.Night, s.State);
            return s;
        }

        private static void SetFlag(GameSession s, string flag)
        {
            var field = typeof(GameSession).GetField("_flags", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "GameSession._flags мало існувати");
            var flags = (StoryFlags)field.GetValue(s);
            flags.Set(flag);
        }

        private static int Gold(GameSession s)
        {
            var field = typeof(GameSession).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "GameSession._state мало існувати");
            var state = (Game.Core.Base.BaseState)field.GetValue(s);
            return state.Resources.Get(ResourceType.Gold);
        }

        private static void DrainGold(GameSession s)
        {
            var field = typeof(GameSession).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            var state = (Game.Core.Base.BaseState)field.GetValue(s);
            int gold = state.Resources.Get(ResourceType.Gold);
            if (gold > 0) Assert.IsTrue(state.Resources.TrySpend(ResourceType.Gold, gold));
            Assert.AreEqual(0, state.Resources.Get(ResourceType.Gold));
        }

        private static List<string> PlayerUnitIds(GameSession s)
        {
            var battle = s.GetBattleView();
            Assert.IsNotNull(battle, "бій фіналу має бути підвішений");
            // Бойовий юніт напарника має префікс «u_» (BuildBattleSetup); тут повертаємо id напарника.
            return battle.Units.Where(u => u.Side == "Player")
                .Select(u => u.Id.StartsWith("u_", StringComparison.Ordinal) ? u.Id.Substring(2) : u.Id).ToList();
        }

        private static int EnemyUnitCount(GameSession s) => s.GetBattleView().Units.Count(u => u.Side == "Enemy");

        // ---------- склад: хто кандидат ----------

        [Test]
        public void Candidates_NeverContainProtagonist_AndPostedOnesAreNotSelectable()
        {
            var s = ToFinaleNight(morning: m =>
            {
                ClearAllPosts(m);
                Assert.AreEqual(Game.Core.Base.AssignmentResult.Success, m.Assign(PostedId, PostId),
                    "передумова: Максим має стати на пост перед фіналом");
            });

            var view = s.GetFinaleView();
            Assert.AreEqual(GameSession.ProtagonistId, view.ProtagonistId);
            Assert.IsFalse(view.Candidates.Any(c => c.CompanionId == GameSession.ProtagonistId),
                "протагоніст іде завжди — його не обирають");

            var posted = view.Candidates.SingleOrDefault(c => c.CompanionId == PostedId);
            Assert.IsNotNull(posted, "той, хто на посту, лишається в списку — як сірий, з причиною");
            Assert.IsFalse(posted.Selectable);
            Assert.AreEqual(FinaleBlock.OnPost, posted.Block);
            Assert.AreEqual(PostId, posted.PostSlotId);

            // Кожен вибираний кандидат справді не стоїть на посту (звірка з ростером, а не з самим видом).
            var roster = s.GetRosterView().Companions.ToDictionary(c => c.Id);
            foreach (var candidate in view.Candidates.Where(c => c.Selectable))
                Assert.IsTrue(string.IsNullOrEmpty(roster[candidate.CompanionId].AssignedSlotId),
                    candidate.CompanionId + ": обираний кандидат не може стояти на посту");
        }

        [Test]
        public void Bloody_RefusesCompanionOnPost_AndChangesNothing()
        {
            var s = ToFinaleNight(morning: m =>
            {
                ClearAllPosts(m);
                m.Assign(PostedId, PostId);
            });

            var ex = Assert.Throws<InvalidOperationException>(
                () => s.ResolveFinale(IncidentPath.Bloody, new[] { PostedId }));
            StringAssert.Contains(PostedId, ex.Message);
            StringAssert.Contains(FinaleBlock.OnPost.ToString(), ex.Message);

            Assert.AreEqual(SessionState.Night, s.State, "відмова не підвішує бій");
            // Склад можна виправити й піти далі: відмова не з'їла фінал.
            var free = s.GetFinaleView().Candidates.Where(c => c.Selectable).Select(c => c.CompanionId).Take(1).ToList();
            s.ResolveFinale(IncidentPath.Bloody, free);
            Assert.AreEqual(SessionState.Battle, s.State);
        }

        [Test]
        public void Bloody_ChosenSquad_IsExactlyWhoFights()
        {
            var s = ToFinaleNight(morning: ClearAllPosts);
            var selectable = s.GetFinaleView().Candidates.Where(c => c.Selectable).Select(c => c.CompanionId).ToList();
            Assert.GreaterOrEqual(selectable.Count, 2, "передумова: після зняття з постів вільних щонайменше двоє");

            var chosen = new List<string> { selectable[1] }; // свідомо НЕ перший у ростері: вибір гравця, а не порядок
            s.ResolveFinale(IncidentPath.Bloody, chosen);

            var fighters = PlayerUnitIds(s);
            CollectionAssert.AreEquivalent(new[] { GameSession.ProtagonistId }.Concat(chosen), fighters,
                "на полі рівно протагоніст і обрані — ніхто «за замовчуванням»");
        }

        [Test]
        public void Bloody_EmptySquad_ProtagonistGoesAlone()
        {
            var s = ToFinaleNight(morning: ClearAllPosts);
            s.ResolveFinale(IncidentPath.Bloody, new string[0]);
            CollectionAssert.AreEqual(new[] { GameSession.ProtagonistId }, PlayerUnitIds(s));
        }

        [Test]
        public void Bloody_DefaultSquad_IsFreeCompanionsUpToLimit_NeverPosted()
        {
            var s = ToFinaleNight(morning: m =>
            {
                ClearAllPosts(m);
                m.Assign(PostedId, PostId);
            });
            var view = s.GetFinaleView();
            var expected = new[] { GameSession.ProtagonistId }
                .Concat(view.Candidates.Where(c => c.Selectable).Select(c => c.CompanionId).Take(view.PartyMax - 1))
                .ToList();

            s.ResolveFinale(IncidentPath.Bloody); // склад за замовчуванням — шлях ботів і тестів

            var fighters = PlayerUnitIds(s);
            CollectionAssert.AreEquivalent(expected, fighters);
            CollectionAssert.DoesNotContain(fighters, PostedId, "той, хто на посту, не воює навіть за замовчуванням");
            Assert.LessOrEqual(fighters.Count, view.PartyMax);
        }

        [Test]
        public void Bloody_RejectsUnknownProtagonistDuplicateAndOversizeSquads()
        {
            var s = ToFinaleNight(morning: ClearAllPosts);
            var view = s.GetFinaleView();
            var selectable = view.Candidates.Where(c => c.Selectable).Select(c => c.CompanionId).ToList();
            var one = selectable.First();

            Assert.Throws<InvalidOperationException>(() => s.ResolveFinale(IncidentPath.Bloody, new[] { "nobody_here" }));
            Assert.Throws<InvalidOperationException>(() => s.ResolveFinale(IncidentPath.Bloody, new[] { GameSession.ProtagonistId }),
                "протагоніст уже в складі — вказувати його не можна");
            Assert.Throws<InvalidOperationException>(() => s.ResolveFinale(IncidentPath.Bloody, new[] { one, one }));

            // Завеликий склад: PartyMax РІЗНИХ валідних людей + протагоніст > ліміту. Різних — щоб
            // відмову спричиняв саме ліміт, а не дубль (інакше тест не ловив би зняття ліміту).
            Assert.GreaterOrEqual(selectable.Count, view.PartyMax, "передумова: вільних щонайменше PartyMax");
            var oversize = selectable.Take(view.PartyMax).ToList();
            Assert.Throws<InvalidOperationException>(() => s.ResolveFinale(IncidentPath.Bloody, oversize));
            Assert.AreEqual(SessionState.Night, s.State, "жодна відмова не підвісила бій");
            // Рівно до ліміту (PartyMax - 1 людей) — приймається.
            var withinLimit = selectable.Take(view.PartyMax - 1).ToList();
            s.ResolveFinale(IncidentPath.Bloody, withinLimit);
            Assert.AreEqual(SessionState.Battle, s.State);
            Assert.AreEqual(view.PartyMax, PlayerUnitIds(s).Count, "на полі рівно ліміт: протагоніст + PartyMax - 1");
        }

        [Test]
        public void Quiet_Path_NeedsNoSquad()
        {
            var s = ToFinaleNight();
            var report = s.ResolveFinale(IncidentPath.Quiet);
            Assert.IsNotNull(report, "тихий шлях розв'язується одразу, без бою й без вибору складу");
            Assert.AreNotEqual(SessionState.Battle, s.State);
        }

        [Test]
        public void MorningOfDay5_WarnsThatPostedPeopleCannotJoinTheSquad_OnlyWhenSomeoneIsPosted()
        {
            var posted = ToDay5Morning();
            ClearAllPosts(posted);
            posted.Assign(PostedId, PostId);
            CollectionAssert.Contains(UxPreflight.Check(posted, Game.Core.Characters.Creation.Gender.Male),
                Game.Gameplay.Text.UkrainianText.Get("ux.preflight.finale_squad", Game.Core.Characters.Creation.Gender.Male),
                "хтось стоїть на посту — гравця треба попередити вранці, уночі посади вже не змінити");

            var free = ToDay5Morning();
            ClearAllPosts(free);
            CollectionAssert.DoesNotContain(UxPreflight.Check(free, Game.Core.Characters.Creation.Gender.Male),
                Game.Gameplay.Text.UkrainianText.Get("ux.preflight.finale_squad", Game.Core.Characters.Creation.Gender.Male),
                "посад ніхто не тримає — попереджати нема про що");
        }

        // ---------- прев'ю = реальний бій (B5) і читач прапора Захара (B2) ----------

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, true, true)]
        public void Preview_EqualsRealBattle_ForEveryFlagCombination(bool release, bool zakhar, bool defectorSeeded)
        {
            var s = ToFinaleNight(beforeNight: m =>
            {
                if (release) SetFlag(m, Game.Core.Scenes.CompanionScenes.MyroslavaConfrontedReleaseFlag);
                if (zakhar) SetFlag(m, Game.Core.Scenes.CompanionScenes.ZakharPreparedAssaultFlag);
                if (defectorSeeded) SetFlag(m, PassVanguardOutcome.DefectorSeededFlag);
            });

            int previewed = s.GetFinaleEnemyCount();
            Assert.AreEqual(previewed, s.GetFinaleView().EnemyCount, "два прев'ю не можуть розходитися");

            s.ResolveFinale(IncidentPath.Bloody);
            Assert.AreEqual(previewed, EnemyUnitCount(s), "прев'ю мало назвати ту саму кількість, що вийшла на поле");
        }

        [Test]
        public void ZakharPreparedAssaultFlag_RemovesExactlyOneRegularEnemy()
        {
            var without = ToFinaleNight();
            int baseline = without.GetFinaleEnemyCount();
            Assert.Greater(baseline, 2, "передумова: на цій полосі є з кого зняти рядового (бос лишається)");

            var with = ToFinaleNight(beforeNight: m => SetFlag(m, Game.Core.Scenes.CompanionScenes.ZakharPreparedAssaultFlag));
            Assert.AreEqual(baseline - 1, with.GetFinaleEnemyCount(), "рада Захара «тримати перевал» м'якшить штурм рівно на одного рядового");

            with.ResolveFinale(IncidentPath.Bloody);
            Assert.IsTrue(with.GetBattleView().Units.Any(u => u.Id.Contains("burunda")), "боса рада Захара не знімає");
        }

        // ---------- B3: пом'якшення кризи купується ----------

        [Test]
        public void SpendGold_PaysThePrice_AndMitigates()
        {
            var s = ToDay5Morning();
            ToCrisisWindow(s);
            int price = new BalanceConfig().CrisisMitigationGold;
            int before = Gold(s);
            Assert.GreaterOrEqual(before, price, "передумова: на добу 5 вистачає золота на пом'якшення");

            s.ReactToCrisis(CrisisReaction.SpendGold);

            Assert.AreEqual(before - price, Gold(s), "золото справді списано за ціною з BalanceConfig");
            Assert.IsTrue(s.DayLog.Any(e => e.Key == "crisis.test.mitigated"));
        }

        [Test]
        public void SpendGold_WithoutGold_DoesNotMitigate_AndSays()
        {
            var s = ToDay5Morning();
            ToCrisisWindow(s);
            DrainGold(s);

            s.ReactToCrisis(CrisisReaction.SpendGold);

            Assert.AreEqual(0, Gold(s), "золото не пішло в мінус");
            Assert.IsFalse(s.DayLog.Any(e => e.Key == "crisis.test.mitigated"), "без золота вогонь не гасять — раніше пом'якшення було безкоштовним");
            Assert.IsTrue(s.DayLog.Any(e => e.Key == "crisis.test.no_gold"), "гравець має почути причину");

            // Вікно лишилось відкритим: можна передумати й вибрати інше.
            Assert.DoesNotThrow(() => s.ReactToCrisis(CrisisReaction.Ignore));
        }
    }
}
