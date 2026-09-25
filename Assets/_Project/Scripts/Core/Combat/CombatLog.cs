using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>
    /// Запис журналу бою для гравця (R7): текстовий ключ + аргументи — id
    /// юнітів, числа й токени (<c>status</c>, <c>damageType</c>, <c>abilityId</c>),
    /// але ЖОДНОГО готового слова. Слова живуть у Gameplay (UkrainianText):
    /// Core не знає, якою мовою його покажуть.
    ///
    /// Народжується в парі з рядком діагностичного трейсу
    /// (<see cref="CombatState.Log"/>, internal) одним викликом
    /// <c>CombatState.Record</c> — забути один із двох не можна фізично.
    ///
    /// Імена аргументів одні на весь журнал: <c>unitId</c> — підмет рядка
    /// (хто діє або з ким щось відбувається), <c>targetId</c> — другий
    /// учасник, якщо він є. Числа — InvariantCulture.
    /// </summary>
    public sealed class CombatLogEntry
    {
        /// <summary>Раунд, у якому сталася подія (0 — до початку бою).</summary>
        public readonly int Round;

        /// <summary>Ключ із закритого списку <see cref="CombatLogKeys.All"/>.</summary>
        public readonly string Key;

        public readonly IReadOnlyDictionary<string, string> Args;

        public CombatLogEntry(int round, string key, IReadOnlyDictionary<string, string> args)
        {
            Round = round;
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Args = args ?? EmptyArgs;
        }

        private static readonly Dictionary<string, string> EmptyArgs = new Dictionary<string, string>();
    }

    /// <summary>
    /// Закритий список ключів журналу бою — усе, що <see cref="CombatState"/>
    /// може записати. Тест звіряє його з текстовою таблицею, тому новий
    /// ключ без тексту не доживе до екрана: спершу константа тут і рядок
    /// в UkrainianText, потім виклик у CombatState.
    /// </summary>
    public static class CombatLogKeys
    {
        // ---- рамка бою ----
        public const string Started = "combat.log.started";
        public const string RoundStarted = "combat.log.round";
        public const string Retreat = "combat.log.retreat";
        public const string DrawForced = "combat.log.draw.forced";
        public const string DrawRoundCap = "combat.log.draw.round_cap";
        public const string Victory = "combat.log.victory";
        public const string Defeat = "combat.log.defeat";

        // ---- дії поточного юніта ----
        public const string Move = "combat.log.move";
        public const string OverwatchSet = "combat.log.overwatch.set";
        public const string Strike = "combat.log.strike";
        public const string AttackMiss = "combat.log.attack.miss";
        public const string AttackGraze = "combat.log.attack.graze";
        public const string AttackHit = "combat.log.attack.hit";
        public const string AttackCrit = "combat.log.attack.crit";
        public const string Stabilize = "combat.log.stabilize";

        // ---- здібності та їхні ефекти ----
        public const string Ability = "combat.log.ability";
        public const string Damage = "combat.log.damage";
        public const string Shred = "combat.log.shred";
        public const string Heal = "combat.log.heal";
        public const string ApGranted = "combat.log.ap_granted";
        public const string Lunge = "combat.log.lunge";
        public const string Repositioned = "combat.log.repositioned";
        public const string TrapPlaced = "combat.log.trap.placed";
        public const string HackedToPlayer = "combat.log.hacked.to_player";
        public const string HackedToEnemy = "combat.log.hacked.to_enemy";

        // ---- дозор (overwatch) ----
        public const string OverwatchFired = "combat.log.overwatch.fired";
        public const string OverwatchExpired = "combat.log.overwatch.expired";
        public const string OverwatchLostDisplaced = "combat.log.overwatch.lost.displaced";
        public const string OverwatchLostHacked = "combat.log.overwatch.lost.hacked";
        public const string OverwatchLostStunned = "combat.log.overwatch.lost.stunned";
        public const string OverwatchLostKnockedDown = "combat.log.overwatch.lost.knocked_down";
        public const string OverwatchLostOut = "combat.log.overwatch.lost.out";

        // ---- пастки ----
        public const string TrapTriggered = "combat.log.trap.triggered";
        public const string TrapDamage = "combat.log.trap.damage";

        // ---- стани ----
        public const string StatusApplied = "combat.log.status.applied";
        public const string StatusRemoved = "combat.log.status.removed";
        public const string StatusExpired = "combat.log.status.expired";
        public const string StatusDot = "combat.log.status.dot";
        public const string StandUp = "combat.log.stand_up";
        public const string StunnedSkip = "combat.log.stunned_skip";

        // ---- даун і смерть ----
        public const string Downed = "combat.log.downed";
        public const string BleedingOut = "combat.log.bleeding_out";
        public const string WindowExpired = "combat.log.window_expired";
        public const string Survived = "combat.log.survived";
        public const string Died = "combat.log.died";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Started, RoundStarted, Retreat, DrawForced, DrawRoundCap, Victory, Defeat,
            Move, OverwatchSet, Strike, AttackMiss, AttackGraze, AttackHit, AttackCrit, Stabilize,
            Ability, Damage, Shred, Heal, ApGranted, Lunge, Repositioned, TrapPlaced, HackedToPlayer, HackedToEnemy,
            OverwatchFired, OverwatchExpired, OverwatchLostDisplaced, OverwatchLostHacked, OverwatchLostStunned,
            OverwatchLostKnockedDown, OverwatchLostOut,
            TrapTriggered, TrapDamage,
            StatusApplied, StatusRemoved, StatusExpired, StatusDot, StandUp, StunnedSkip,
            Downed, BleedingOut, WindowExpired, Survived, Died
        };

        public static string Attack(AttackOutcome outcome)
        {
            switch (outcome)
            {
                case AttackOutcome.Miss: return AttackMiss;
                case AttackOutcome.Graze: return AttackGraze;
                case AttackOutcome.Crit: return AttackCrit;
                default: return AttackHit;
            }
        }

        /// <summary>
        /// Токен стану для аргументу <c>status</c>. Явне зіставлення, а
        /// не PascalCase→snake_case: нове значення <see cref="StatusType"/> без
        /// свого рядка мусить впасти голосно (тест проганяє всі значення), а не
        /// мовчки дати ключ, якого немає в таблиці.
        /// </summary>
        public static string StatusId(StatusType type)
        {
            switch (type)
            {
                case StatusType.Bleeding: return "bleeding";
                case StatusType.Stunned: return "stunned";
                case StatusType.Suppressed: return "suppressed";
                case StatusType.KnockedDown: return "knocked_down";
                case StatusType.Marked: return "marked";
                case StatusType.Burning: return "burning";
                case StatusType.Poisoned: return "poisoned";
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "Состояние без токена журнала боя");
            }
        }

        /// <summary>Токен типу урону для аргументу <c>damageType</c> — той самий принцип, що й у <see cref="StatusId"/>.</summary>
        public static string DamageTypeId(DamageType type)
        {
            switch (type)
            {
                case DamageType.True: return "true";
                case DamageType.Ballistic: return "ballistic";
                case DamageType.Fire: return "fire";
                case DamageType.Toxin: return "toxin";
                case DamageType.Energy: return "energy";
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "Тип урона без токена журнала боя");
            }
        }
    }
}
