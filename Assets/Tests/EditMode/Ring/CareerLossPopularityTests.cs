using System.Collections.Generic;
using System.Text.RegularExpressions;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// P1-6 (рішення власника за планом П5): поразка чи нічия дає популярність лише з ажіотажем фіналу ≥ 9 і раз на сезон.
    /// Справжні матчі кар'єри (бот програє «гучно»: б'ється до ажіотажу 8, далі лягає) — три сезони, вісім кар'єр.
    /// </summary>
    public class CareerLossPopularityTests
    {
        private static readonly MatchGoal LoseLoud = new MatchGoal { Aim = MatchAim.Lose, Hype = HypeAim.High };

        [Test]
        public void Loss_GivesPopularity_OnlyWithHypeNine_AndOncePerSeason()
        {
            int losses = 0, gained = 0, blockedHype = 0, blockedSeason = 0;
            for (int v = 0; v < 8; v++)
            {
                var s = CareerKit.NewAtTitle((ulong)(900 + v));
                var gainsBySeason = new Dictionary<int, int>();
                FeudBotDriver.PlayCareer(s, v, FeudEnding.HonorableLoss, _ => LoseLoud, r =>
                {
                    if (!r.Played) return;
                    // «Злам плану» (перк Бунтаря) — окреме правило: звернутий план дає +1 і поза P1-6
                    int perk = r.PlanOutcome == FeudPlanOutcome.Swerved && s.Feud.State.Perks.Contains(PerkCatalog.PlanBreaker) ? 1 : 0;
                    int delta = r.PopularityAfter - r.PopularityBefore;
                    if (r.Winner == MatchWinner.Player) { Assert.AreEqual(0, r.LossPopularityBlocked, "перемога правилом не зачеплена"); return; }
                    losses++;
                    if (r.LossPopularityBlocked != 0) Assert.LessOrEqual(delta, perk, "заблоковано — популярність не росте (крім перка)");
                    if (r.LossPopularityBlocked == 1) { blockedHype++; Assert.Less(r.Hype, CareerRules.LossPopularityHype); }
                    if (r.LossPopularityBlocked == 2) { blockedSeason++; Assert.AreEqual(1, gainsBySeason[s.State.Season]); }
                    if (delta > perk)
                    {
                        gained++;
                        Assert.GreaterOrEqual(r.Hype, CareerRules.LossPopularityHype, "поразка дала популярність з ажіотажем " + r.Hype);
                        gainsBySeason[s.State.Season] = gainsBySeason.TryGetValue(s.State.Season, out var n) ? n + 1 : 1;
                        Assert.AreEqual(1, gainsBySeason[s.State.Season], "двічі за сезон");
                        Assert.AreEqual(s.State.Season, s.State.LossPopularitySeason);
                    }
                }, 3);
            }
            TestContext.Out.WriteLine("поразок " + losses + ", з популярністю " + gained + ", заблоковано: ажіотаж " + blockedHype + ", сезон " + blockedSeason);
            Assert.Greater(blockedHype, 0, "прогін має зачепити поразку з ажіотажем 7–8");
            Assert.Greater(gained, 0, "прогін має зачепити поразку з ажіотажем ≥ 9");
            Assert.Greater(blockedSeason, 0, "прогін має зачепити другу «гучну» поразку сезону");
        }

        /// <summary>Новий ключ сейву (losspop) і 24-те поле підсумку шоу — відновлення у свіжу сесію; старі зліпки — значення за замовчуванням.</summary>
        [Test]
        public void LossPopularity_SurvivesRestoreIntoAFreshSession_AndOldSnapshotsDefault()
        {
            var c = CareerKit.Content();
            var s = CareerKit.NewHero(41);
            s.State.LossPopularitySeason = 1;
            s.State.Last = new ShowResult { Played = true, OpponentId = "baron", Hype = 8, LossPopularityBlocked = 1 };
            string blob = s.CaptureState();

            var fresh = CareerKit.Fresh(41, c);
            fresh.RestoreState(blob);
            Assert.AreEqual(1, fresh.State.LossPopularitySeason);
            Assert.AreEqual(1, fresh.State.Last.LossPopularityBlocked);
            Assert.AreEqual(blob, fresh.CaptureState());

            // зліпок до 11.11.2026: без losspop і з 23 полями підсумку шоу
            string last = s.State.Last.Capture();
            string oldLast = last.Substring(0, last.LastIndexOf(','));
            string old = Regex.Replace(blob, @"\|losspop=\d+", "").Replace(System.Uri.EscapeDataString(last), System.Uri.EscapeDataString(oldLast));
            Assert.AreNotEqual(blob, old);
            var legacy = CareerKit.Fresh(41, c);
            legacy.RestoreState(old);
            Assert.AreEqual(0, legacy.State.LossPopularitySeason);
            Assert.AreEqual(0, legacy.State.Last.LossPopularityBlocked);
            Assert.AreEqual("baron", legacy.State.Last.OpponentId);
        }
    }
}
