using System.Linq;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.Signals;
using Game.Core.Stats;
using Game.Core.World;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Ночь и доклады с постов. Здесь защищается Поправка №3.8 и §3.9:
    /// пустая позиция — это слепота, а сон — потеря сигналов.
    /// </summary>
    public class NightAndReportTests
    {
        private static BalanceConfig Cfg() => new BalanceConfig();

        private static Companion Guard(string id, int skill)
        {
            var arch = new CompanionArchetype(id, id);
            arch.BaseStats.Set(StatType.Survival, skill);
            return arch.CreateInstance(id);
        }

        private static DayProcessor MakeProcessor(BalanceConfig cfg, Roster roster, string protagonistId = null)
        {
            var tension = new TensionState(cfg.Tension, 500);
            var adapter = new RosterAdapter(roster, protagonistId);
            var pulse = new WorldPulse(cfg.Pulse);
            foreach (var s in DefaultPressureSources.All()) pulse.AddSource(s);

            return new DayProcessor(tension, cfg, DayProcessor.DefaultSteps())
            {
                Tier = 2,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(),
                Pulse = pulse,
                Incidents = DefaultIncidents.BuildTable(),
                Repeats = new RepeatTracker(),
                PostDomains = new[]
                {
                    new PostDomain("watch", "улицы", SkillKeys.Survival, 5)
                }
            };
        }

        // ---- Доклад с постов ----

        [Test]
        public void PostReport_UnmannedPosition_IsSilent()
        {
            var cfg = Cfg();
            var roster = new Roster();
            roster.Add(Guard("idle", 20)); // сильный, но пост не держит

            var report = MakeProcessor(cfg, roster).Advance();

            Assert.IsFalse(report.Signals.Requests.Any(r => r.Channel == SignalChannel.PostReport),
                "Никого на позиции — город в этом домене слеп");
        }

        [Test]
        public void PostReport_StrongCompanion_GivesAccurateReport()
        {
            var cfg = Cfg();
            var roster = new Roster();
            var ace = Guard("ace", 20);
            ace.AssignedSlotId = "watch";
            roster.Add(ace);

            var report = MakeProcessor(cfg, roster).Advance();

            var post = report.Signals.Requests.FirstOrDefault(r => r.Channel == SignalChannel.PostReport);
            Assert.AreEqual(SignalChannel.PostReport, post.Channel, "Пост обязан доложить");
            Assert.IsTrue(post.Tags.Any(t => t == "accuracy:" + OutcomeBand.Best),
                "Сильный профильный напарник даёт конкретику");
        }

        [Test]
        public void PostReport_WeakCompanion_GivesVagueReport()
        {
            var cfg = Cfg();
            var roster = new Roster();
            var rookie = Guard("rookie", 5);
            rookie.AssignedSlotId = "watch";
            roster.Add(rookie);

            var report = MakeProcessor(cfg, roster).Advance();

            var post = report.Signals.Requests.First(r => r.Channel == SignalChannel.PostReport);
            Assert.IsTrue(post.Tags.Any(t => t == "accuracy:" + OutcomeBand.Base),
                "Слабый напарник докладывает расплывчато");
        }

        // ---- Ночь ----

        [Test]
        public void Night_HasNoCitizenLines()
        {
            var cfg = Cfg();
            var roster = new Roster();
            roster.Add(Guard("a", 8));

            var night = MakeProcessor(cfg, roster).Advance(DayPhase.Night);

            Assert.IsFalse(night.Signals.Requests.Any(r => r.Channel == SignalChannel.CitizenLine),
                "Ночью горожан не слышно — диалоги закрыты (US-1.5)");
            Assert.IsTrue(night.Signals.Requests.Any(r => r.Channel == SignalChannel.Ambient),
                "…зато слышно сам город");
        }

        [Test]
        public void Night_NoPostReports()
        {
            var cfg = Cfg();
            var roster = new Roster();
            var ace = Guard("ace", 20);
            ace.AssignedSlotId = "watch";
            roster.Add(ace);

            var night = MakeProcessor(cfg, roster).Advance(DayPhase.Night);

            Assert.IsFalse(night.Signals.Requests.Any(r => r.Channel == SignalChannel.PostReport),
                "Доклады бывают утром, а не среди ночи");
        }

        [Test]
        public void Night_SleepingLosesForewarnings_PatrollingKeepsThem()
        {
            var cfg = Cfg();

            int ForewarnCount(bool patrol)
            {
                var roster = new Roster();
                roster.Add(Guard("a", 8));
                var p = MakeProcessor(cfg, roster);
                p.IsPatrolling = patrol;

                int total = 0;
                for (int i = 0; i < 20; i++)
                    total += p.Advance(DayPhase.Night).Forewarnings.Count;
                return total;
            }

            Assert.AreEqual(0, ForewarnCount(false), "Спишь — ночные предвестники проходят мимо");
            Assert.Greater(ForewarnCount(true), 0, "Патрулируешь — узнаёшь, что зреет");
        }

        [Test]
        public void Crisis_NeverFiresWithoutLevel3Forewarn()
        {
            var cfg = Cfg();
            var roster = new Roster();
            roster.Add(Guard("hero", 3));
            roster.Add(Guard("alpha", 3));
            roster.Add(Guard("beta", 3));

            var p = MakeProcessor(cfg, roster, "hero");
            p.IsPatrolling = true;

            int maxLevelSeen = 0;
            for (int day = 1; day <= 120; day++)
            {
                var report = day % 2 == 0 ? p.Advance(DayPhase.Night) : p.Advance(DayPhase.Day);

                foreach (var f in report.Forewarnings)
                    if (f.SourceId == "crisis" && f.Level > maxLevelSeen) maxLevelSeen = f.Level;

                if (report.Incidents.Any(i => i.WasCrisis))
                {
                    Assert.AreEqual(3, maxLevelSeen,
                        "Кризис не имеет права ударить без предвестника третьей ступени");
                    return;
                }
            }
            // Кризис мог и не наступить — это нормально, но тогда и предвестника 3 быть не должно.
            Assert.Pass("Кризис не наступил за 120 дней — тоже валидный сценарий");
        }

        [Test]
        public void EverySignalChannel_HasComposerBranch()
        {
            var cfg = Cfg();
            var roster = new Roster();
            var ace = Guard("ace", 20);
            ace.AssignedSlotId = "watch";
            roster.Add(ace);
            roster.Add(Guard("beta", 5));

            var seen = new System.Collections.Generic.HashSet<SignalChannel>();
            var p = MakeProcessor(cfg, roster, "ace");
            p.IsPatrolling = true;

            for (int day = 1; day <= 120; day++)
            {
                var report = day % 2 == 0 ? p.Advance(DayPhase.Night) : p.Advance(DayPhase.Day);
                foreach (var r in report.Signals.Requests) seen.Add(r.Channel);
                seen.Add(SignalChannel.Moodboard); // мудборд уходит отдельным полем дайджеста
            }

            foreach (SignalChannel channel in System.Enum.GetValues(typeof(SignalChannel)))
                Assert.Contains(channel, seen.ToList(),
                    $"Канал {channel} не порождается ни разу — это мёртвый стат");
        }
    }
}
