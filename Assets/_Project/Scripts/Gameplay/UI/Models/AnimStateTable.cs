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
        Victory = 37,
        /// <summary>Кидок (сітка — <c>ability.net</c>).</summary>
        Throw = 38
    }

    /// <summary>Чим б'ється постать — від ключа зброї набору (<c>wpn_*</c>).</summary>
    public enum WeaponStyle
    {
        Unarmed = 0,
        Blade = 1,
        Heavy = 2,
        Polearm = 3,
        /// <summary>Вогнепал (кремінна рушниця).</summary>
        Ranged = 4,
        /// <summary>Лук — свої кліпи натягу (власник 08.10.2026: «лук стріляє кліпом пістоля… так не повинно буть»).</summary>
        Bow = 5
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
    /// звіряє кожен кліп із маніфестом). Кліпи — Quaternius UAL1/UAL2 (CC0) і власні <c>ALPHA_Weapons</c>
    /// (<c>tools/blender/alpha_anims.py</c>: лук, рушниця до плеча, спис, удар сокирою згори, здача навколішки — у UAL
    /// їх немає); бойові залежать від стилю зброї. Сцена лише застосовує.
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
                case "wpn_bow": return WeaponStyle.Bow;
                case "wpn_musket": return WeaponStyle.Ranged;
                default: return WeaponStyle.Unarmed;
            }
        }

        /// <summary>
        /// Чи тримає постать зброю в руці в цьому стані. Лише бойова стійка, дозор, удар, блок і влучання мають кліпи з
        /// хватом зброї; решта (рух, укриття, здача, падіння, окрик, кидок, допомога, перемога, мирні стани) — руки інші,
        /// і зброя пройшла б крізь кисть чи тіло: тоді вона за спиною (власник 08.10.2026: «Хоочу щоб Эквіп ніколи не був
        /// так, а нормально Не скрізь руку чи тіло»).
        /// </summary>
        public static bool HoldsWeapon(CharacterAnimState state)
        {
            switch (state)
            {
                case CharacterAnimState.CombatIdle:
                case CharacterAnimState.Overwatch:
                case CharacterAnimState.Attack:
                case CharacterAnimState.Block:
                case CharacterAnimState.Hit:
                case CharacterAnimState.HitHeavy:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Стріляє (лук чи рушниця), а не б'є.</summary>
        public static bool IsRanged(WeaponStyle style) => style == WeaponStyle.Ranged || style == WeaponStyle.Bow;

        /// <summary>
        /// Видима зброя за бойовою (<c>BattleUnitView.WeaponId</c>, напр. «weapon.horde_bow»): у руці має бути те, чим
        /// постать б'ється. Раніше видиму брали з образу (впізнавана зброя), а удар — з бойової: меч стріляв.
        /// null — без зброї (б'ється кулаками).
        /// </summary>
        public static string VisualForWeapon(string weaponId, bool melee)
        {
            string id = weaponId ?? string.Empty;
            if (id.Length == 0) return melee ? null : "wpn_bow";
            if (id.Contains("bow")) return "wpn_bow";
            if (id.Contains("musket") || id.Contains("gun") || id.Contains("rifle") || id.Contains("pistol")) return "wpn_musket";
            if (id.Contains("spear") || id.Contains("pike")) return "wpn_spear";
            if (id.Contains("axe") || id.Contains("cleaver")) return "wpn_axe";
            if (id.Contains("mace")) return "wpn_mace";
            if (id.Contains("club")) return "wpn_club";
            if (id.Contains("sabre") || id.Contains("saber") || id.Contains("curved")) return "wpn_sabre";
            if (id.Contains("katana")) return "wpn_katana";
            if (id.Contains("dagger") || id.Contains("knife")) return "wpn_dagger";
            if (id.Contains("sword")) return "wpn_sword";
            return melee ? "wpn_sword" : "wpn_bow";
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
                    switch (style)
                    {
                        case WeaponStyle.Bow: return Loop("Bow_Idle_Loop");
                        case WeaponStyle.Ranged: return Loop("Rifle_Idle_Loop");
                        case WeaponStyle.Polearm: return Loop("Spear_Idle_Loop");
                        case WeaponStyle.Heavy: return Loop("Axe_Idle_Loop");
                        case WeaponStyle.Blade: return Loop("Sword_Idle");
                        default: return Loop("Idle_Loop");
                    }
                case CharacterAnimState.CoverIdle: return Loop("Crouch_Idle_Loop");
                case CharacterAnimState.CoverMove: return Loop("Crouch_Fwd_Loop");
                case CharacterAnimState.Overwatch:
                    switch (style)
                    {
                        case WeaponStyle.Bow: return Loop("Bow_Aim_Loop");
                        case WeaponStyle.Ranged: return Loop("Rifle_Aim_Loop");
                        case WeaponStyle.Polearm: return Loop("Spear_Idle_Loop");
                        default: return Loop("Idle_Shield_Loop");
                    }
                case CharacterAnimState.Attack:
                    switch (style)
                    {
                        // Лук, рушниця, спис, сокира — власні кліпи (лукбук бою 08.10.2026: з кліпами пістоля лук
                        // тримали біля обличчя, рушницю — на витягнутій руці; спис махав як меч, сокира після удару
                        // застигала в глибокому випаді).
                        case WeaponStyle.Bow: return Once("Bow_Shoot");
                        case WeaponStyle.Ranged: return Once("Rifle_Shoot");
                        // Удари на місці. Sword_Regular_A/B/C — комбо з кроками: тіло відлітало від клітинки на 0,6–1,7 м
                        // і крутилось до 160° (аудит кліпів лукбука, 07.10.2026).
                        case WeaponStyle.Blade: return Once("Sword_Attack");
                        case WeaponStyle.Heavy: return Once("Axe_Chop");
                        case WeaponStyle.Polearm: return Once("Spear_Thrust");
                        default: return Once("Punch_Cross");
                    }
                case CharacterAnimState.Ability: return Once("Spell_Simple_Shoot");
                case CharacterAnimState.Throw: return Once("OverhandThrow");
                case CharacterAnimState.Social: return Once("Idle_Rail_Call");
                case CharacterAnimState.Block: return Once("Sword_Block");
                case CharacterAnimState.Hit: return Once("Hit_Chest");
                // Hit_Knockback кидав постать на землю — і той, хто пережив критичний удар, лежав, а потім стрибав у
                // стійку (лукбук бою 08.10.2026). Падіння — лише Down.
                case CharacterAnimState.HitHeavy: return Once("Hit_Head");
                case CharacterAnimState.Stunned: return Loop("Zombie_Idle_Loop");
                case CharacterAnimState.Down: return new AnimClipChoice("Death01", false, hold: true);
                case CharacterAnimState.GetUp: return Once("LayToIdle");
                case CharacterAnimState.Surrender: return Loop("Surrender_Loop");
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

        /// <summary>
        /// Стійка юніта в бою за його станом (BattleUnitView): впав → лежить; здається → навколішки;
        /// оглушений → хитається; у дозорі → цілиться; інакше — бойова стійка зі своєю зброєю.
        /// </summary>
        public static CharacterAnimState BattleIdleFor(bool downed, bool surrendering, IEnumerable<string> statuses, bool overwatching)
            => BattleIdleFor(downed, surrendering, statuses, overwatching, false);

        /// <param name="besideLowCover">Поруч низьке укриття — присід за ним (як у тактиках WL3/XCOM); удар і постріл
        /// постать робить стоячи й вертається в присід.</param>
        public static CharacterAnimState BattleIdleFor(bool downed, bool surrendering, IEnumerable<string> statuses, bool overwatching,
            bool besideLowCover)
        {
            if (downed) return CharacterAnimState.Down;
            if (surrendering) return CharacterAnimState.Surrender;
            if (statuses != null)
                foreach (var s in statuses)
                    if (s == "stunned") return CharacterAnimState.Stunned;
            if (overwatching) return CharacterAnimState.Overwatch;
            if (besideLowCover) return CharacterAnimState.CoverIdle;
            return CharacterAnimState.CombatIdle;
        }

        /// <summary>
        /// Чим постать показує здібність (<c>abilityId</c> рядка журналу): удар чи постріл — своєю зброєю, наказ і
        /// залякування — окриком, сітка — кидком, пастка й допомога — руками біля землі. Раніше будь-яка здібність
        /// була «закляттям» (Spell_Simple_Shoot), і залп з лука теж.
        /// </summary>
        public static CharacterAnimState AbilityStateFor(string abilityId)
        {
            switch (abilityId)
            {
                case "ability.lunge": case "ability.pierce": case "ability.volley": return CharacterAnimState.Attack;
                case "ability.rally": case "ability.move_order": case "ability.enrage": case "ability.intimidate":
                    return CharacterAnimState.Social;
                case "ability.net": return CharacterAnimState.Throw;
                case "ability.set_trap": case "ability.mercy": return CharacterAnimState.Interact;
                default: return CharacterAnimState.Ability;
            }
        }

        /// <summary>
        /// Мить удару в одноразовому кліпі (частка довжини): тоді ціль здригається й спливає шкода. Раніше реакція
        /// стартувала разом із замахом — ціль хиталась ще до удару. Для лука — мить випуску стріли (далі летить
        /// стріла), для рушниці — постріл. Заміряно на плівках лукбука бою (08.10.2026) і з таймінгів alpha_anims.py.
        /// </summary>
        public static float ImpactAt(string clip)
        {
            switch (NormalizeClipName(clip))
            {
                case "Bow_Shoot": return 0.54f;       // випуск 0,86 с з 1,6
                case "Rifle_Shoot": return 0.42f;     // постріл 0,55 с з 1,3
                case "Spear_Thrust": return 0.42f;
                case "Axe_Chop": return 0.47f;
                case "Sword_Attack": return 0.34f;
                case "Punch_Cross": return 0.33f;
                case "Melee_Hook": return 0.45f;
                case "Pistol_Shoot": return 0.15f;
                case "OverhandThrow": return 0.45f;
                case "Spell_Simple_Shoot": return 0.5f;
                default: return 0.4f;
            }
        }

        /// <summary>Реакція цілі на рядок журналу бою (ключ <c>combat.log.*</c>); Idle — без реакції.</summary>
        public static CharacterAnimState ReactionFor(string logKey)
        {
            switch (logKey)
            {
                case "combat.log.attack.graze": return CharacterAnimState.Block;
                case "combat.log.attack.hit": return CharacterAnimState.Hit;
                case "combat.log.attack.crit": return CharacterAnimState.HitHeavy;
                default: return CharacterAnimState.Idle;
            }
        }

        /// <summary>Усі стани — для охоронця покриття.</summary>
        public static IEnumerable<CharacterAnimState> AllStates()
        {
            foreach (CharacterAnimState s in Enum.GetValues(typeof(CharacterAnimState))) yield return s;
        }

        /// <summary>
        /// З якою швидкістю (м/с, постать 1,8 м) «іде» кліп руху — заміряно лукбуком редактора за довжиною кроку
        /// (KitLookbookRender, 07.10.2026). Анімація крутить кліп під справжню швидкість постаті, щоб ноги не
        /// ковзали: герой ішов 5,5 м/с кліпом ходи на ~1 м/с (власник: «анімація фігня»). 0 — невідомо.
        /// </summary>
        public static float NaturalSpeed(string clip)
        {
            switch (NormalizeClipName(clip))
            {
                case "Walk_Loop": return WalkNatural;
                case "Jog_Fwd_Loop": return JogNatural;
                case "Sprint_Loop": return SprintNatural;
                default: return 0f;
            }
        }

        // Хода — обидва заміри збігаються (1,03 м/с чоловік, 0,92 — жінка). У бігу є фаза польоту: за довжиною
        // кроку виходить 2,4 / 3,3 м/с (менше справжнього), за ногою на опорі — 4,7 для підтюпцем; беремо
        // середину. Похибку гасить темп 0,6–1,6× у FigureAnimation.
        public const float WalkNatural = 1.0f;
        public const float JogNatural = 3.4f;
        public const float SprintNatural = 5.8f;

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
