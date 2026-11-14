using System;
using System.Collections.Generic;
using System.IO;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Петлі очікування не в такт (арт-директор, 14.11.2026, за дорученням власника «добавь более живие айдл анимации когда
    /// просто стояние происходит, что бы чуть живее выглядело»): гравець, суперник і рефері стартують петлю з різної миті
    /// й дихають у різному темпі, детерміновано. Петлі з ритмом режисера (відлік за рингом, утримання) не чіпаються.
    /// </summary>
    public class RingIdlePhaseTests
    {
        private static readonly RingIdleRole[] Roles =
            { RingIdleRole.Player, RingIdleRole.Rival, RingIdleRole.Referee, RingIdleRole.Other };

        private static string Root() { return Path.GetDirectoryName(Application.dataPath); }

        /// <summary>Відстань двох фаз по колу петлі (0…0,5).</summary>
        private static float CircularGap(float a, float b)
        {
            float d = Math.Abs(a - b) % 1f;
            return Math.Min(d, 1f - d);
        }

        [Test]
        public void Roles_StartLoopAtDistinctMoments()
        {
            for (int i = 0; i < Roles.Length; i++)
            {
                float fi = RingIdlePhase.StartFraction(Roles[i]);
                Assert.That(fi, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f), Roles[i].ToString());
                for (int j = i + 1; j < Roles.Length; j++)
                {
                    float gap = CircularGap(fi, RingIdlePhase.StartFraction(Roles[j]));
                    Assert.That(gap, Is.GreaterThanOrEqualTo(0.2f),
                        Roles[i] + " і " + Roles[j] + " стартують петлю надто близько — хитатимуться в такт");
                }
            }
        }

        [Test]
        public void PairAndReferee_BreatheAtDifferentTempo_WithinNaturalRange()
        {
            var seen = new HashSet<float>();
            foreach (var r in new[] { RingIdleRole.Player, RingIdleRole.Rival, RingIdleRole.Referee })
            {
                float s = RingIdlePhase.SpeedFactor(r);
                Assert.That(s, Is.InRange(0.9f, 1.1f), r + ": темп поза природним — видно прискорення чи сповільнення");
                Assert.That(seen.Add(s), Is.True, r + ": той самий темп, що в іншої ролі");
            }
        }

        [Test]
        public void StartTime_IsFractionOfLength_ForWaitingLoops()
        {
            Assert.That(RingIdlePhase.StartTime("Ring_Stance_Loop", 2.4f, RingIdleRole.Player), Is.EqualTo(0f));
            Assert.That(RingIdlePhase.StartTime("Ring_Stance_Loop", 2.4f, RingIdleRole.Rival), Is.EqualTo(0.43f * 2.4f).Within(1e-5f));
            Assert.That(RingIdlePhase.StartTime("Ref_Idle_Loop", 3.2f, RingIdleRole.Referee), Is.EqualTo(0.71f * 3.2f).Within(1e-5f));
            Assert.That(RingIdlePhase.StartTime("Ring_Stance_Loop", 0f, RingIdleRole.Rival), Is.EqualTo(0f));
        }

        [Test]
        public void DirectorRhythmLoops_AreNotShifted()
        {
            foreach (var clip in new[] { "Ref_Count_Outside_Loop", "Armbar_Hold_Loop_A", "Cover_Hold_Loop_A", "Bodyslam_A" })
                foreach (var r in Roles)
                {
                    Assert.That(RingIdlePhase.StartTime(clip, 3f, r), Is.EqualTo(0f), clip + " / " + r);
                    Assert.That(RingIdlePhase.Speed(clip, r), Is.EqualTo(1f), clip + " / " + r);
                }
        }

        /// <summary>Кожна петля очікування є в маніфестах обох наборів і справді петля (стовпець «петля» = 1).</summary>
        [Test]
        public void WaitingLoops_ExistAsLoopsInBothManifests()
        {
            string dir = Path.Combine(Root(), "Assets", "Art", "Animations");
            foreach (var man in new[] { "ring_anims_manifest.txt", "ring_anims_f_manifest.txt" })
            {
                var loops = new Dictionary<string, bool>();
                foreach (var raw in File.ReadAllLines(Path.Combine(dir, man)))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 4) continue;
                    loops[p[0]] = p[3] == "1";
                }
                foreach (var clip in new[] { "Ring_Stance_Loop", "Idle_Tired_Loop", "Idle_Hurt_Head_Loop", "Ref_Idle_Loop" })
                {
                    Assert.That(RingIdlePhase.IsWaitingLoop(clip), Is.True, clip);
                    Assert.That(loops.ContainsKey(clip), Is.True, man + ": немає " + clip);
                    Assert.That(loops[clip], Is.True, man + ": " + clip + " не петля — зсув фази зламає старт");
                }
            }
        }
    }
}
