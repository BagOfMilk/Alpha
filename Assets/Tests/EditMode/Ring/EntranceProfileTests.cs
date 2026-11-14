using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Персональний вихід (<c>docs/ring/ENTRANCE_PROFILE.md</c>): кожне правило вибору — рядок таблиці специфікації,
    /// усі кліпи профілю є в обох наборах (<c>ring_entrance_manifest.txt</c>, <c>ring_entrance_f_manifest.txt</c>),
    /// кольори голів — лише токени, золото — лише гравцеві (Статут вигляду COL-01, COL-02).
    /// </summary>
    public class EntranceProfileTests
    {
        private static EntranceInput In(Gimmick g, RingPath p, int str = 0, int agi = 0, int tech = 0, int cha = 0)
        {
            return new EntranceInput { Gimmick = g, Path = p, Strength = str, Agility = agi, Technique = tech, Charisma = cha };
        }

        private static EntranceProfile C(EntranceInput i) { return EntranceProfile.Compose(i); }

        /// <summary>Усі ґіміки ядра, а не чотири перші: ґіміки 5–8 «Кар'єри» теж виходять (рев'ю ПМ 12.11.2026).</summary>
        private static readonly Gimmick[] AllGimmicks = (Gimmick[])System.Enum.GetValues(typeof(Gimmick));

        /// <summary>
        /// Кожен ґімік має явне сімейство виходу — один із чотирьох, для яких зняті кліпи й пули. Ґіміки 5–8: Бувалий — технік,
        /// Хардкорник і Силач — монстр, Самородок — шоумен. Ґімік у сімействі виходить точно як сімейство за тих самих даних (кліп завіси,
        /// хода, вхід, поза, темп) — тож нових кліпів не треба, а «default» більше нікого мовчки не ловить.
        /// </summary>
        [Test]
        public void EveryGimmick_HasExplicitFamily_AndExitsExactlyAsIt()
        {
            Assert.GreaterOrEqual(AllGimmicks.Length, 8, "ґіміків ядра менше восьми — тест бачить не той перелік");
            foreach (var fam in EntranceProfile.FamilyGimmicks)
                Assert.AreEqual(fam, EntranceProfile.Family(fam), fam + ": ґімік із власними кліпами — сам собі сімейство");
            Assert.AreEqual(Gimmick.Technician, EntranceProfile.Family(Gimmick.Veteran));
            Assert.AreEqual(Gimmick.Monster, EntranceProfile.Family(Gimmick.Hardcore));
            Assert.AreEqual(Gimmick.Showman, EntranceProfile.Family(Gimmick.Rookie));
            Assert.AreEqual(Gimmick.Monster, EntranceProfile.Family(Gimmick.Strongman));
            foreach (var g in AllGimmicks)
            {
                Assert.IsTrue(EntranceProfile.HasFamily(g), g + ": немає рядка в таблиці сімейств виходу — він вийшов би запасним Шоуменом");
                var fam = EntranceProfile.Family(g);
                CollectionAssert.Contains(EntranceProfile.FamilyGimmicks, fam, g + ": сімейство без власних кліпів");
                foreach (RingPath p in new[] { RingPath.Face, RingPath.Heel, RingPath.Rebel })
                    for (int s = -1; s <= 3; s++)
                    {
                        var mine = C(In(g, p, s, s, s, s));
                        var theirs = C(In(fam, p, s, s, s, s));
                        CollectionAssert.AreEqual(theirs.Clips(), mine.Clips(), g + "/" + p + "/" + s + ": кліпи не як у сімейства " + fam);
                        Assert.AreEqual(theirs.Entry, mine.Entry, g + "/" + p + "/" + s + ": вхід у ринг");
                        Assert.AreEqual(theirs.PoseSpot, mine.PoseSpot, g + "/" + p + "/" + s + ": місце пози");
                        Assert.AreEqual(theirs.EntrySpeed, mine.EntrySpeed, 1e-6, g + "/" + p + "/" + s + ": швидкість входу");
                        Assert.AreEqual(theirs.Bpm, mine.Bpm, g + "/" + p + "/" + s + ": темп");
                    }
            }
        }

        [Test]
        public void Curtain_GimmickGesture_ThenPathGestureFromCharismaOne()
        {
            Assert.AreEqual("Entrance_Curtain_Monster", C(In(Gimmick.Monster, RingPath.Face)).CurtainGimmick);
            Assert.AreEqual("Entrance_Curtain_Flyer", C(In(Gimmick.Flyer, RingPath.Face)).CurtainGimmick);
            Assert.AreEqual("Entrance_Curtain_Technician", C(In(Gimmick.Technician, RingPath.Face)).CurtainGimmick);
            Assert.AreEqual("Entrance_Curtain_Showman", C(In(Gimmick.Showman, RingPath.Face)).CurtainGimmick);
            Assert.IsNull(C(In(Gimmick.Showman, RingPath.Face, cha: 0)).CurtainPath, "Харизма 0 — без жесту шляху");
            Assert.AreEqual("Entrance_Curtain_Face_Point", C(In(Gimmick.Showman, RingPath.Face, cha: 1)).CurtainPath);
            Assert.AreEqual("Entrance_Curtain_Heel_Dismiss", C(In(Gimmick.Showman, RingPath.Heel, cha: 1)).CurtainPath);
            Assert.IsNull(C(In(Gimmick.Showman, RingPath.Rebel, cha: 3)).CurtainPath, "бунтар — без жесту");
        }

        [Test]
        public void Walk_FirstMatchingRowWins()
        {
            var limp = In(Gimmick.Monster, RingPath.Face, str: 3); limp.Injured = true;
            Assert.AreEqual("Entrance_Walk_Limp_Loop", C(limp).WalkLoop, "травма — найперша");
            Assert.AreEqual("Entrance_Walk_Heavy_Loop", C(In(Gimmick.Technician, RingPath.Face, str: 3, agi: 3)).WalkLoop, "Сила 3 раніше за Спритність");
            Assert.AreEqual("Entrance_Walk_Bouncy_Loop", C(In(Gimmick.Flyer, RingPath.Face, str: 3)).WalkLoop, "літун із Силою 3 — не важка хода");
            Assert.AreEqual("Entrance_Walk_Bouncy_Loop", C(In(Gimmick.Technician, RingPath.Face, agi: 3)).WalkLoop);
            Assert.AreEqual("Entrance_Walk_Heavy_Loop", C(In(Gimmick.Monster, RingPath.Face, agi: 3)).WalkLoop, "монстр зі Спритністю 3 — за ґіміком");
            Assert.AreEqual("Entrance_Walk_Heavy_Loop", C(In(Gimmick.Monster, RingPath.Face)).WalkLoop);
            Assert.AreEqual("Entrance_Walk_Bouncy_Loop", C(In(Gimmick.Flyer, RingPath.Face)).WalkLoop);
            Assert.AreEqual("Entrance_Walk_Focused_Loop", C(In(Gimmick.Technician, RingPath.Face)).WalkLoop);
            Assert.AreEqual("Entrance_Walk_Strut_Loop", C(In(Gimmick.Showman, RingPath.Face)).WalkLoop);
        }

        [Test]
        public void Barrier_FaceWithPopularity_HeelWithHeat()
        {
            var face = In(Gimmick.Showman, RingPath.Face); face.Popularity = 4;
            Assert.AreEqual("Entrance_Barrier_High_Five", C(face).Barrier);
            face.Popularity = 3;
            Assert.IsNull(C(face).Barrier);
            var heel = In(Gimmick.Showman, RingPath.Heel); heel.Heat = 2;
            Assert.AreEqual("Entrance_Barrier_Heel_Taunt", C(heel).Barrier);
            heel.Heat = 1;
            Assert.IsNull(C(heel).Barrier);
            var rebel = In(Gimmick.Showman, RingPath.Rebel); rebel.Popularity = 10; rebel.Heat = 4;
            Assert.IsNull(C(rebel).Barrier, "бунтар — без зупинки");
        }

        [Test]
        public void RingEntry_Chains()
        {
            var m = C(In(Gimmick.Monster, RingPath.Heel, str: 2));
            Assert.AreEqual(EntranceRingEntry.StepsThroughRopes, m.Entry, "монстр із Силою 2 — сходи й канати навіть лиходієм");
            Assert.AreEqual(0.8f, m.EntrySpeed, 1e-6);
            var f = C(In(Gimmick.Flyer, RingPath.Heel, agi: 2));
            Assert.AreEqual(EntranceRingEntry.StepsVault, f.Entry);
            Assert.AreEqual("Entrance_Vault_Over_Top", f.EntryClip);
            Assert.AreEqual(EntranceRingEntry.RollIn, C(In(Gimmick.Showman, RingPath.Heel)).Entry, "лиходій закочується");
            Assert.AreEqual(EntranceRingEntry.RollIn, C(In(Gimmick.Technician, RingPath.Face, agi: 2)).Entry);
            Assert.IsFalse(C(In(Gimmick.Technician, RingPath.Face, agi: 2)).UsesSteps, "закочування — без сходів");
            var plain = C(In(Gimmick.Technician, RingPath.Face));
            Assert.AreEqual(EntranceRingEntry.StepsThroughRopes, plain.Entry);
            Assert.AreEqual(1f, plain.EntrySpeed, 1e-6);
            Assert.AreEqual(EntranceRingEntry.StepsThroughRopes, C(In(Gimmick.Monster, RingPath.Face, str: 1)).Entry, "монстр із Силою 1 — звичайний вхід");
        }

        [Test]
        public void Pose_ByGimmickAndPath_DurationAndBelt()
        {
            Assert.AreEqual("Entrance_Pose_Monster_Roar", C(In(Gimmick.Monster, RingPath.Face)).Pose);
            Assert.AreEqual(EntrancePoseSpot.Center, C(In(Gimmick.Monster, RingPath.Heel)).PoseSpot);
            Assert.AreEqual("Entrance_Pose_Turnbuckle_Point", C(In(Gimmick.Flyer, RingPath.Rebel)).Pose);
            Assert.AreEqual(EntrancePoseSpot.Turnbuckle, C(In(Gimmick.Flyer, RingPath.Face)).PoseSpot);
            Assert.AreEqual("Entrance_Pose_Heel_Shush", C(In(Gimmick.Flyer, RingPath.Heel)).Pose);
            Assert.AreEqual("Entrance_Pose_Technician_Stretch", C(In(Gimmick.Technician, RingPath.Face)).Pose);
            Assert.AreEqual(EntrancePoseSpot.Ropes, C(In(Gimmick.Technician, RingPath.Face)).PoseSpot);
            Assert.AreEqual("Entrance_Pose_Heel_Mock", C(In(Gimmick.Technician, RingPath.Heel)).Pose);
            Assert.AreEqual("Entrance_Pose_Turnbuckle_Arms", C(In(Gimmick.Showman, RingPath.Face)).Pose);
            Assert.AreEqual("Entrance_Pose_Heel_Shush", C(In(Gimmick.Showman, RingPath.Heel)).Pose);
            Assert.AreEqual(2.6f, C(In(Gimmick.Showman, RingPath.Face, cha: 1)).PoseSeconds, 1e-6);
            Assert.AreEqual(4.0f, C(In(Gimmick.Showman, RingPath.Face, cha: 2)).PoseSeconds, 1e-6);
            var champ = In(Gimmick.Showman, RingPath.Face); champ.Champion = true;
            Assert.IsTrue(C(champ).Belt);
            Assert.IsTrue(C(champ).Pyro, "чемпіон — піро");
            Assert.IsFalse(C(In(Gimmick.Showman, RingPath.Face)).Belt);
        }

        [Test]
        public void Show_PyroSparksCrowd()
        {
            var i = In(Gimmick.Showman, RingPath.Face, cha: 2);
            Assert.IsTrue(C(i).Sparks);
            Assert.IsFalse(C(In(Gimmick.Showman, RingPath.Face, cha: 1)).Sparks);
            Assert.IsFalse(C(i).Pyro);
            i.MainEvent = true; Assert.IsTrue(C(i).Pyro);
            i.MainEvent = false; i.Title = true; Assert.IsTrue(C(i).Pyro);
            var face = In(Gimmick.Showman, RingPath.Face);
            face.Popularity = 0; Assert.AreEqual(1, C(face).Crowd);
            face.Popularity = 10; Assert.AreEqual(4, C(face).Crowd);
            var heel = In(Gimmick.Showman, RingPath.Heel); heel.Heat = 4;
            Assert.AreEqual(3, C(heel).Crowd); Assert.IsTrue(C(heel).CrowdBoo); Assert.AreEqual(0.7f, C(heel).HallLight, 1e-6);
            Assert.AreEqual(2, C(In(Gimmick.Showman, RingPath.Rebel)).Crowd);
            Assert.AreEqual(96, C(In(Gimmick.Monster, RingPath.Face)).Bpm);
            Assert.AreEqual(132, C(In(Gimmick.Flyer, RingPath.Face)).Bpm);
        }

        [Test]
        public void Show_HeadColoursAreTokens_GoldOnlyForThePlayer()
        {
            foreach (Gimmick g in AllGimmicks)
                foreach (RingPath p in new[] { RingPath.Face, RingPath.Heel, RingPath.Rebel })
                    foreach (bool player in new[] { false, true })
                    {
                        var i = In(g, p); i.Player = player;
                        var prof = C(i);
                        Assert.AreEqual(4, prof.HeadTokens.Length);
                        Assert.AreEqual(4, prof.HeadGain.Length);
                        Assert.AreEqual(EntranceProfile.CostumeHead, prof.HeadTokens[0]);
                        foreach (var t in prof.HeadTokens.Skip(1))
                        {
                            uint rgb;
                            Assert.IsTrue(EntranceScreenSlots.TryRgb(t, out rgb), g + "/" + p + ": " + t + " — не токен (COL-01)");
                            if (!player) Assert.AreNotEqual("gold", t, g + "/" + p + ": золото в NPC (COL-02)");
                        }
                    }
        }

        private static HashSet<string> Manifest(string file)
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets", "Art", "Animations", file);
            return new HashSet<string>(File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#"))
                .Select(l => l.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries)[0]));
        }

        [Test]
        public void EveryProfileClip_ExistsInBothSets()
        {
            var m = Manifest("ring_entrance_manifest.txt");
            var f = Manifest("ring_entrance_f_manifest.txt");
            var all = new HashSet<string>();
            foreach (Gimmick g in AllGimmicks)
                foreach (RingPath p in new[] { RingPath.Face, RingPath.Heel, RingPath.Rebel })
                    for (int s = -1; s <= 3; s++)
                        foreach (bool inj in new[] { false, true })
                        {
                            var i = In(g, p, s, s, s, s); i.Injured = inj; i.Popularity = 10; i.Heat = 4; i.Champion = s == 3;
                            foreach (var c in C(i).Clips()) all.Add(c);
                        }
            all.Add(EntranceProfile.WalkFallback); all.Add(EntranceProfile.PoseFallback);
            foreach (var c in all)
            {
                Assert.IsTrue(m.Contains(c), "немає в ring_entrance_manifest.txt: " + c);
                Assert.IsTrue(f.Contains(c), "немає в ring_entrance_f_manifest.txt: " + c);
            }
            Assert.GreaterOrEqual(all.Count, 26, "профіль має дістати всі кліпи виходу");
        }
    }
}
