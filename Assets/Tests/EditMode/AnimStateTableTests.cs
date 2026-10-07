using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core.Characters;
using Game.Gameplay.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Анімації (Поправка №18.2, віха M1.18): кожен стан села й бою має кліп, і кожен кліп справді є в
    /// бібліотеці UAL (маніфест <c>Assets/Art/Animations/anim_manifest.txt</c>).
    /// </summary>
    public class AnimStateTableTests
    {
        private static HashSet<string> Manifest() =>
            new HashSet<string>(File.ReadAllLines(Path.Combine(Application.dataPath, "Art", "Animations", "anim_manifest.txt"))
                .Where(l => !l.StartsWith("#") && l.Contains(":")).Select(l => l.Substring(l.IndexOf(':') + 1)));

        [Test]
        public void EveryState_ForEveryWeaponStyle_HasAClipThatExists()
        {
            var clips = Manifest();
            Assert.Greater(clips.Count, 40);
            foreach (var state in AnimStateTable.AllStates())
                foreach (WeaponStyle style in System.Enum.GetValues(typeof(WeaponStyle)))
                {
                    var choice = AnimStateTable.For(state, style);
                    Assert.IsTrue(clips.Contains(choice.Clip), state + "/" + style + ": немає кліпу " + choice.Clip);
                }
        }

        [Test]
        public void EveryWeaponPart_HasAStyle_AndEveryPostHasWork()
        {
            foreach (var w in KitParts.Weapons)
                Assert.AreNotEqual(WeaponStyle.Unarmed, AnimStateTable.StyleOf(w), w);
            foreach (var post in new[] { "council_seat", "storehouse_dock", "settlement_market", "infirmary_bed",
                                          "settlement_farms", "scouting_post", "workshop_bench" })
                Assert.AreNotEqual(CharacterAnimState.Idle, AnimStateTable.WorkFor(post), post);
        }

        [Test]
        public void DownHoldsTheLastFrame_AttacksDoNotLoop()
        {
            Assert.IsTrue(AnimStateTable.For(CharacterAnimState.Down, WeaponStyle.Blade).HoldLastFrame);
            foreach (WeaponStyle style in System.Enum.GetValues(typeof(WeaponStyle)))
                Assert.IsFalse(AnimStateTable.For(CharacterAnimState.Attack, style).Loop);
            Assert.AreNotEqual(AnimStateTable.For(CharacterAnimState.Attack, WeaponStyle.Ranged).Clip,
                AnimStateTable.For(CharacterAnimState.Attack, WeaponStyle.Blade).Clip);
        }

        [Test]
        public void EnemyWeapon_IsStable_AndMatchesAttackType()
        {
            Assert.AreEqual(AnimStateTable.EnemyWeaponFor("raider_1", true), AnimStateTable.EnemyWeaponFor("raider_1", true));
            Assert.AreEqual(WeaponStyle.Ranged, AnimStateTable.StyleOf(AnimStateTable.EnemyWeaponFor("archer_2", false)));
            Assert.AreNotEqual(WeaponStyle.Ranged, AnimStateTable.StyleOf(AnimStateTable.EnemyWeaponFor("raider_1", true)));
            Assert.IsTrue(KitParts.Weapons.Contains(AnimStateTable.EnemyWeaponFor("x", true)));
        }

        [Test]
        public void BattleStance_FollowsUnitState_InPriorityOrder()
        {
            Assert.AreEqual(CharacterAnimState.Down, AnimStateTable.BattleIdleFor(true, true, new[] { "stunned" }, true));
            Assert.AreEqual(CharacterAnimState.Surrender, AnimStateTable.BattleIdleFor(false, true, new[] { "stunned" }, true));
            Assert.AreEqual(CharacterAnimState.Stunned, AnimStateTable.BattleIdleFor(false, false, new[] { "bleeding", "stunned" }, true));
            Assert.AreEqual(CharacterAnimState.Overwatch, AnimStateTable.BattleIdleFor(false, false, null, true));
            Assert.AreEqual(CharacterAnimState.CombatIdle, AnimStateTable.BattleIdleFor(false, false, new[] { "bleeding" }, false));
        }

        [Test]
        public void HitReactions_GrowWithTheBlow_MissHasNone()
        {
            Assert.AreEqual(CharacterAnimState.Idle, AnimStateTable.ReactionFor("combat.log.attack.miss"));
            Assert.AreEqual(CharacterAnimState.Block, AnimStateTable.ReactionFor("combat.log.attack.graze"));
            Assert.AreEqual(CharacterAnimState.Hit, AnimStateTable.ReactionFor("combat.log.attack.hit"));
            Assert.AreEqual(CharacterAnimState.HitHeavy, AnimStateTable.ReactionFor("combat.log.attack.crit"));
        }

        [Test]
        public void ClipNames_LoseTheTakePrefix()
        {
            Assert.AreEqual("Idle_Loop", AnimStateTable.NormalizeClipName("Rig|Rig|Idle_Loop"));
            Assert.AreEqual("Yes", AnimStateTable.NormalizeClipName("Yes"));
        }
    }
}
