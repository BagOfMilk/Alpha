using System.Linq;
using System.Reflection;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Session;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Трек C7 — досьє іменного ворога (Поправка №14.6; власник: «го»). Контакт
    /// відкриває роль і здоров'я; розвідка перед боєм (Виживання чи Кмітливість) або
    /// сам бій — прийоми, опори, умову здачі. Незнане — «?», шанс влучання видно
    /// завжди, прев'ю шкоди невивченого — з «?». Знання переживає свіжу сесію.
    /// </summary>
    public class EnemyDossierTests
    {
        [Test]
        public void Book_OnlyGoesUp_AndSurvivesCaptureRestore()
        {
            var book = new EnemyDossierBook();
            Assert.AreEqual(DossierLevel.Unknown, book.LevelOf("enemy.tuhar_boyar"));
            Assert.IsTrue(book.Raise("enemy.tuhar_boyar", DossierLevel.Contact));
            Assert.IsTrue(book.Raise("enemy.tuhar_boyar", DossierLevel.Studied));
            Assert.IsFalse(book.Raise("enemy.tuhar_boyar", DossierLevel.Contact), "знання не спадає");
            book.Raise("enemy.horde_scout", DossierLevel.Contact);

            var copy = new EnemyDossierBook();
            copy.RestoreState(book.CaptureState());
            Assert.AreEqual(DossierLevel.Studied, copy.LevelOf("enemy.tuhar_boyar"));
            Assert.AreEqual(DossierLevel.Contact, copy.LevelOf("enemy.horde_scout"));
            StringAssert.DoesNotContain(";", book.CaptureState());
            StringAssert.DoesNotContain("=", book.CaptureState());
        }

        private static GameSession Morning(int scoutSurvival, int scoutWits)
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var step = s.AdvanceScene();
            while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            // Єдина рефлексія у файлі: поріг розвідки — щоб загін точно (не) розвідував.
            var cfg = (BalanceConfig)typeof(GameSession).GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(s);
            cfg.Combat.DossierScoutSurvival = scoutSurvival;
            cfg.Combat.DossierScoutWits = scoutWits;
            return s;
        }

        /// <summary>Бій проти боярина Тугара через рейд — найкоротший шлях до справжнього бою з хабу.</summary>
        private static void RaidAgainstBoyar(GameSession s, int skip = 0)
        {
            var who = s.GetRosterView().Companions
                .Where(c => c.Id != GameSession.ProtagonistId && c.Status != CompanionStatus.Dead && c.Status != CompanionStatus.Captive)
                .Skip(skip).First();
            Assert.IsTrue(s.TakeCaptive(who.Id, "enemy.tuhar_boyar", 1, null, new[] { "enemy.tuhar_boyar" }));
            Assert.IsTrue(s.RaidCaptors(who.Id));
            Assert.AreEqual(SessionState.Battle, s.State);
        }

        [Test]
        public void FirstContact_ShowsRoleAndHp_TheRestIsAQuestionMark_ButTheChanceIsAlwaysThere()
        {
            var s = Morning(99, 99);
            RaidAgainstBoyar(s);
            var view = s.GetBattleView();
            var boyar = view.Units.Single(u => u.Side == "Enemy");

            Assert.AreEqual("Contact", boyar.Dossier);
            Assert.AreEqual("Breacher", boyar.Role, "роль — з першого контакту");
            Assert.Greater(boyar.HpMax, 0, "здоров'я — з першого контакту");
            Assert.AreEqual(0, boyar.Abilities.Count, "прийоми ще невідомі");
            Assert.IsFalse(boyar.CanSurrender, "умова здачі ще невідома");
            Assert.AreEqual(0, boyar.SurrenderAtHpPercent);
            Assert.IsNull(boyar.ResistNotes);

            var preview = s.PreviewAttack(view.CurrentUnitId, boyar.Id);
            Assert.IsTrue(preview.HasAttackRoll, "шанс влучання видно завжди (UI-02)");
            Assert.IsTrue(preview.DamageUncertain, "шкода по невивченому — з «?»");
        }

        [Test]
        public void TheFight_StudiesTheEnemy_WithASignal_AndTheKnowledgeSurvivesAFreshSession()
        {
            var s = Morning(99, 99);
            RaidAgainstBoyar(s);
            s.CombatAutoResolve();
            Assert.IsTrue(s.DayLog.Any(e => e.Key == "dossier.studied"), "досьє поповнено — у стрічці (інваріант 6)");

            string blob = s.SaveState(0);
            var fresh = new GameSession();
            fresh.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            fresh.RestoreFromBlob(blob);
            var cfg = (BalanceConfig)typeof(GameSession).GetField("_cfg", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(fresh);
            cfg.Combat.DossierScoutSurvival = 99;
            cfg.Combat.DossierScoutWits = 99;

            RaidAgainstBoyar(fresh, skip: 1);
            var boyar = fresh.GetBattleView().Units.Single(u => u.Side == "Enemy");
            Assert.AreEqual("Studied", boyar.Dossier, "знання — у зліпку сейву");
            Assert.IsTrue(boyar.Abilities.Any(a => a.Id == "ability.lunge"), "тепер видно прийоми");
            Assert.IsTrue(boyar.CanSurrender, "і умову здачі");
            Assert.Greater(boyar.SurrenderAtHpPercent, 0);
            Assert.IsFalse(fresh.PreviewAttack(fresh.GetBattleView().CurrentUnitId, boyar.Id).DamageUncertain);
        }

        [Test]
        public void AScoutInTheParty_StudiesBeforeTheFight()
        {
            var s = Morning(0, 0);
            RaidAgainstBoyar(s);
            Assert.AreEqual("Studied", s.GetBattleView().Units.Single(u => u.Side == "Enemy").Dossier);
            Assert.IsTrue(s.DayLog.Any(e => e.Key == "dossier.studied" && e.Args != null && e.Args.Any(kv => kv.Value == "scout")));
        }

        [Test]
        public void Training_IsASandbox_EverythingVisible_NothingWritten()
        {
            var s = new GameSession();
            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold });
            foreach (var enemy in s.GetBattleView().Units.Where(u => u.Side == "Enemy"))
                Assert.AreEqual("Studied", enemy.Dossier);
            Assert.IsFalse(s.DayLog.Any(e => e.Key == "dossier.studied"));
        }
    }
}
