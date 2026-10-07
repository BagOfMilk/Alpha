using System;
using System.Collections.Generic;

namespace Game.Gameplay.UI
{
    /// <summary>Що зараз робить постать — стан для анімації (Поправка №18.2, віха M1.18).</summary>
    public enum CharacterAnimState
    {
        Idle = 0,
        Walk = 1,
        Run = 2,
        Sprint = 3,
        Talk = 4,
        Carry = 5,
        Sit = 6,
        // Робота на посту (село).
        WorkFarm = 10,
        WorkCraft = 11,
        WorkChop = 12,
        WorkHeal = 13,
        WorkTrade = 14,
        WorkGuard = 15,
        WorkCouncil = 16,
        // Бій.
        CombatIdle = 20,
        CoverIdle = 21,
        CoverMove = 22,
        Overwatch = 23,
        Attack = 24,
        Ability = 25,
        Social = 26,
        Block = 27,
        Hit = 28,
        HitHeavy = 29,
        Stunned = 30,
        Down = 31,
        GetUp = 32,
        Surrender = 33,
        Flee = 34,
        Reload = 35,
        Interact = 36,
        Victory = 37
    }

    /// <summary>Чим б'ється постать — від ключа зброї набору (<c>wpn_*</c>).</summary>
    public enum WeaponStyle
    {
        Unarmed = 0,
        Blade = 1,
        Heavy = 2,
        Polearm = 3,
        Ranged = 4
    }

    /// <summary>Кліп для стану: ім'я в <c>Assets/Art/Animations/anim_manifest.txt</c>, чи петля, чи тримати останній кадр.</summary>
    public struct AnimClipChoice
    {
        public string Clip;
        public bool Loop;
        public bool HoldLastFrame;

        public AnimClipChoice(string clip, bool loop, bool hold = false)
        {
            Clip = clip;
            Loop = loop;
            HoldLastFrame = hold;
        }
    }

    /// <summary>
    /// Таблиця «стан → кліп» (віха M1.18): одна на село й бій, чистий C# під тестами (<c>AnimStateTableTests</c>
    /// звіряє кожен кліп із маніфестом). Кліпи — Quaternius UAL1/UAL2 (CC0); бойові залежать від стилю зброї.
    /// Сцена лише застосовує (<c>KitAnimator</c>).
    /// </summary>
    public static class AnimStateTable
    {
        public static WeaponStyle StyleOf(string weaponVisualKey)
        {
            switch (weaponVisualKey)
            {
                case "wpn_sword": case "wpn_sabre": case "wpn_katana": case "wpn_dagger": return WeaponStyle.Blade;
                case "wpn_axe": case "wpn_mace": case "wpn_club": return WeaponStyle.Heavy;
                case "wpn_spear": return WeaponStyle.Polearm;
                case "wpn_bow": case "wpn_musket": return WeaponStyle.Ranged;
                default: return WeaponStyle.Unarmed;
            }
        }

        /// <summary>Стан роботи на посту за id поста (якір у сцені).</summary>
        public static CharacterAnimState WorkFor(string postId)
        {
            switch (postId)
            {
                case "settlement_farms": return CharacterAnimState.WorkFarm;
                case "workshop_bench": return CharacterAnimState.WorkCraft;
                case "storehouse_dock": return CharacterAnimState.WorkChop;
                case "infirmary_bed": return CharacterAnimState.WorkHeal;
                case "settlement_market": return CharacterAnimState.WorkTrade;
                case "scouting_post": return CharacterAnimState.WorkGuard;
                case "council_seat": return CharacterAnimState.WorkCouncil;
                default: return CharacterAnimState.Idle;
            }
        }

        public static AnimClipChoice For(CharacterAnimState state, WeaponStyle style)
        {
            bool ranged = style == WeaponStyle.Ranged;
            switch (state)
            {
                case CharacterAnimState.Walk: return Loop("Walk_Loop");
                case CharacterAnimState.Run: return Loop("Jog_Fwd_Loop");
                case CharacterAnimState.Sprint: return Loop("Sprint_Loop");
                case CharacterAnimState.Talk: return Loop("Idle_Talking_Loop");
                case CharacterAnimState.Carry: return Loop("Walk_Carry_Loop");
                case CharacterAnimState.Sit: return Loop("Sitting_Idle_Loop");
                case CharacterAnimState.WorkFarm: return Loop("Farm_Harvest");
                case CharacterAnimState.WorkCraft: return Loop("Fixing_Kneeling");
                case CharacterAnimState.WorkChop: return Loop("TreeChopping_Loop");
                case CharacterAnimState.WorkHeal: return Loop("Interact");
                case CharacterAnimState.WorkTrade: return Loop("Idle_Talking_Loop");
                case CharacterAnimState.WorkGuard: return Loop("Idle_FoldArms_Loop");
                case CharacterAnimState.WorkCouncil: return Loop("Sitting_Talking_Loop");
                case CharacterAnimState.CombatIdle:
                    return Loop(ranged ? "Pistol_Idle_Loop" : style == WeaponStyle.Unarmed ? "Idle_Loop" : "Sword_Idle");
                case CharacterAnimState.CoverIdle: return Loop("Crouch_Idle_Loop");
                case CharacterAnimState.CoverMove: return Loop("Crouch_Fwd_Loop");
                case CharacterAnimState.Overwatch: return Loop(ranged ? "Pistol_Aim_Neutral" : "Idle_Shield_Loop");
                case CharacterAnimState.Attack:
                    switch (style)
                    {
                        case WeaponStyle.Ranged: return Once("Pistol_Shoot");
                        case WeaponStyle.Blade: return Once("Sword_Regular_A");
                        case WeaponStyle.Heavy: return Once("Sword_Regular_B");
                        case WeaponStyle.Polearm: return Once("Sword_Regular_C");
                        default: return Once("Punch_Cross");
                    }
                case CharacterAnimState.Ability: return Once("Spell_Simple_Shoot");
                case CharacterAnimState.Social: return Once("Idle_Rail_Call");
                case CharacterAnimState.Block: return Once("Sword_Block");
                case CharacterAnimState.Hit: return Once("Hit_Chest");
                case CharacterAnimState.HitHeavy: return Once("Hit_Knockback");
                case CharacterAnimState.Stunned: return Loop("Zombie_Idle_Loop");
                case CharacterAnimState.Down: return new AnimClipChoice("Death01", false, hold: true);
                case CharacterAnimState.GetUp: return Once("LayToIdle");
                case CharacterAnimState.Surrender: return Loop("Crouch_Idle_Loop");
                case CharacterAnimState.Flee: return Loop("Sprint_Loop");
                case CharacterAnimState.Reload: return Once("Pistol_Reload");
                case CharacterAnimState.Interact: return Once("Interact");
                case CharacterAnimState.Victory: return Once("Yes");
                default: return Loop("Idle_Loop");
            }
        }

        private static readonly string[] EnemyMelee = { "wpn_axe", "wpn_club", "wpn_sabre", "wpn_spear", "wpn_mace" };
        private static readonly string[] EnemyRanged = { "wpn_bow", "wpn_musket" };

        /// <summary>Видима зброя ворога без листа персонажа: за типом атаки, стабільно від id (FNV, без випадковості).</summary>
        public static string EnemyWeaponFor(string unitId, bool melee)
        {
            uint h = 2166136261u;
            foreach (char ch in unitId ?? string.Empty) { h ^= ch; h *= 16777619u; }
            var pool = melee ? EnemyMelee : EnemyRanged;
            return pool[(int)(h % (uint)pool.Length)];
        }

        /// <summary>Усі стани — для охоронця покриття.</summary>
        public static IEnumerable<CharacterAnimState> AllStates()
        {
            foreach (CharacterAnimState s in Enum.GetValues(typeof(CharacterAnimState))) yield return s;
        }

        /// <summary>Ім'я кліпу без префікса дубля FBX («Rig|Rig|Idle_Loop» → «Idle_Loop»).</summary>
        public static string NormalizeClipName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            int bar = raw.LastIndexOf('|');
            return bar >= 0 ? raw.Substring(bar + 1) : raw;
        }

        private static AnimClipChoice Loop(string clip) => new AnimClipChoice(clip, true);
        private static AnimClipChoice Once(string clip) => new AnimClipChoice(clip, false);
    }
}
