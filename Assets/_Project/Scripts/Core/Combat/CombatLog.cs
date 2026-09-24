using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>
    /// Запись журнала боя для игрока (R7): текстовый ключ + аргументы — id
    /// юнитов, числа и токены (<c>status</c>, <c>damageType</c>, <c>abilityId</c>),
    /// но НИ ОДНОГО готового слова. Слова живут в Gameplay (UkrainianText):
    /// Core не знает, на каком языке его покажут.
    ///
    /// Рождается в паре со строкой диагностического трейса
    /// (<see cref="CombatState.Log"/>, internal) одним вызовом
    /// <c>CombatState.Record</c> — забыть одну из двух нельзя физически.
    ///
    /// Имена аргументов одни на весь журнал: <c>unitId</c> — подлежащее строки
    /// (кто действует или с кем что-то происходит), <c>targetId</c> — второй
    /// участник, если он есть. Числа — InvariantCulture.
    /// </summary>
    public sealed class CombatLogEntry
    {
        /// <summary>Раунд, в котором случилось событие (0 — до начала боя).</summary>
        public readonly int Round;

        /// <summary>Ключ из закрытого списка <see cref="CombatLogKeys.All"/>.</summary>
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
    /// Закрытый список ключей журнала боя — всё, что <see cref="CombatState"/>
    /// может записать. Тест сверяет его с текстовой таблицей, поэтому новый
    /// ключ без текста не доживёт до экрана: сначала константа здесь и строка
    /// в UkrainianText, потом вызов в CombatState.
    /// </summary>
    public static class CombatLogKeys
    {
        // ---- рамка боя ----
        public const string Started = "combat.log.started";
        public const string RoundStarted = "combat.log.round";
        public const string Retreat = "combat.log.retreat";
        public const string DrawForced = "combat.log.draw.forced";
        public const string DrawRoundCap = "combat.log.draw.round_cap";
        public const string Victory = "combat.log.victory";
        public const string Defeat = "combat.log.defeat";

        // ---- действия текущего юнита ----
        public const string Move = "combat.log.move";
        public const string OverwatchSet = "combat.log.overwatch.set";
        public const string Strike = "combat.log.strike";
        public const string AttackMiss = "combat.log.attack.miss";
        public const string AttackGraze = "combat.log.attack.graze";
        public const string AttackHit = "combat.log.attack.hit";
        public const string AttackCrit = "combat.log.attack.crit";
        public const string Stabilize = "combat.log.stabilize";

        // ---- способности и их эффекты ----
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

        // ---- дозор ----
        public const string OverwatchFired = "combat.log.overwatch.fired";
        public const string OverwatchExpired = "combat.log.overwatch.expired";
        public const string OverwatchLostDisplaced = "combat.log.overwatch.lost.displaced";
        public const string OverwatchLostHacked = "combat.log.overwatch.lost.hacked";
        public const string OverwatchLostStunned = "combat.log.overwatch.lost.stunned";
        public const string OverwatchLostKnockedDown = "combat.log.overwatch.lost.knocked_down";
        public const string OverwatchLostOut = "combat.log.overwatch.lost.out";

        // ---- ловушки ----
        public const string TrapTriggered = "combat.log.trap.triggered";
        public const string TrapDamage = "combat.log.trap.damage";

        // ---- состояния ----
        public const string StatusApplied = "combat.log.status.applied";
        public const string StatusRemoved = "combat.log.status.removed";
        public const string StatusExpired = "combat.log.status.expired";
        public const string StatusDot = "combat.log.status.dot";
        public const string StandUp = "combat.log.stand_up";
        public const string StunnedSkip = "combat.log.stunned_skip";

        // ---- даун и смерть ----
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
        /// Токен состояния для аргумента <c>status</c>. Явное сопоставление, а
        /// не PascalCase→snake_case: новое значение <see cref="StatusType"/> без
        /// своей строки должно упасть громко (тест прогоняет все значения), а не
        /// молча дать ключ, которого нет в таблице.
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

        /// <summary>Токен типа урона для аргумента <c>damageType</c> — тот же принцип, что у <see cref="StatusId"/>.</summary>
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
