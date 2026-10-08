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
            Assert.IsTrue(AnimStateTable.IsRanged(AnimStateTable.StyleOf(AnimStateTable.EnemyWeaponFor("archer_2", false))));
            Assert.IsFalse(AnimStateTable.IsRanged(AnimStateTable.StyleOf(AnimStateTable.EnemyWeaponFor("raider_1", true))));
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
            Assert.AreEqual(CharacterAnimState.CoverIdle, AnimStateTable.BattleIdleFor(false, false, null, false, true));
            Assert.AreEqual(CharacterAnimState.Overwatch, AnimStateTable.BattleIdleFor(false, false, null, true, true));
        }

        /// <summary>
        /// Лук і рушниця — свої кліпи, а не пістоля (власник 08.10.2026: «лук стріляє кліпом пістоля… так не повинно
        /// буть»); спис і сокира — свої удари.
        /// </summary>
        [Test]
        public void BowRifleSpearAxe_HaveTheirOwnClips()
        {
            Assert.AreEqual(WeaponStyle.Bow, AnimStateTable.StyleOf("wpn_bow"));
            Assert.AreEqual(WeaponStyle.Ranged, AnimStateTable.StyleOf("wpn_musket"));
            foreach (WeaponStyle style in System.Enum.GetValues(typeof(WeaponStyle)))
                foreach (var state in new[] { CharacterAnimState.CombatIdle, CharacterAnimState.Overwatch, CharacterAnimState.Attack })
                    Assert.IsFalse(AnimStateTable.For(state, style).Clip.StartsWith("Pistol_"), style + "/" + state);
            Assert.AreEqual("Bow_Shoot", AnimStateTable.For(CharacterAnimState.Attack, WeaponStyle.Bow).Clip);
            Assert.AreEqual("Rifle_Shoot", AnimStateTable.For(CharacterAnimState.Attack, WeaponStyle.Ranged).Clip);
            Assert.AreEqual("Spear_Thrust", AnimStateTable.For(CharacterAnimState.Attack, WeaponStyle.Polearm).Clip);
            Assert.AreEqual("Axe_Chop", AnimStateTable.For(CharacterAnimState.Attack, WeaponStyle.Heavy).Clip);
            Assert.AreNotEqual("Hit_Knockback", AnimStateTable.For(CharacterAnimState.HitHeavy, WeaponStyle.Blade).Clip,
                "критичний удар не кидає на землю — падає лише той, хто впав");
        }

        /// <summary>Мить удару — всередині кліпу: реакція цілі не раніше замаху й не після кінця.</summary>
        [Test]
        public void ImpactMoment_IsInsideEveryAttackClip()
        {
            foreach (WeaponStyle style in System.Enum.GetValues(typeof(WeaponStyle)))
            {
                float at = AnimStateTable.ImpactAt(AnimStateTable.For(CharacterAnimState.Attack, style).Clip);
                Assert.That(at, Is.InRange(0.1f, 0.8f), style.ToString());
            }
        }

        /// <summary>У руці — те, чим б'ється: лучник — лук, рушничник — рушниця, з бойовою ближньою — ближня.</summary>
        [Test]
        public void VisibleWeapon_MatchesTheBattleWeapon()
        {
            Assert.AreEqual("wpn_bow", AnimStateTable.VisualForWeapon("weapon.horde_bow", false));
            Assert.AreEqual("wpn_musket", AnimStateTable.VisualForWeapon("weapon.musket", false));
            Assert.AreEqual("wpn_spear", AnimStateTable.VisualForWeapon("weapon.horde_spear", true));
            Assert.AreEqual("wpn_axe", AnimStateTable.VisualForWeapon("weapon.bandit_cleaver", true));
            Assert.AreEqual("wpn_sabre", AnimStateTable.VisualForWeapon("weapon.boyar_saber", true));
            Assert.IsNull(AnimStateTable.VisualForWeapon(null, true), "без зброї — кулаки, без впізнаваної зброї в руці");
            foreach (var id in new[] { "weapon.axe", "weapon.club", "weapon.dagger", "weapon.mace", "weapon.sword", "weapon.curved_blade",
                                       "weapon.horde_axe", "weapon.burunda_mace", "weapon.sabre" })
            {
                var key = AnimStateTable.VisualForWeapon(id, true);
                Assert.IsTrue(KitParts.Weapons.Contains(key), id + " → " + key);
                Assert.IsFalse(AnimStateTable.IsRanged(AnimStateTable.StyleOf(key)), id);
            }
        }

        /// <summary>Здібність показується своїм рухом, а не «закляттям» на всі випадки.</summary>
        [Test]
        public void Abilities_UseFittingMotions()
        {
            Assert.AreEqual(CharacterAnimState.Attack, AnimStateTable.AbilityStateFor("ability.volley"));
            Assert.AreEqual(CharacterAnimState.Attack, AnimStateTable.AbilityStateFor("ability.lunge"));
            Assert.AreEqual(CharacterAnimState.Social, AnimStateTable.AbilityStateFor("ability.rally"));
            Assert.AreEqual(CharacterAnimState.Throw, AnimStateTable.AbilityStateFor("ability.net"));
            Assert.AreEqual(CharacterAnimState.Interact, AnimStateTable.AbilityStateFor("ability.set_trap"));
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
