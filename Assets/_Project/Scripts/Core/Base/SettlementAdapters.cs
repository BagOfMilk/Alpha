using System;
using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Stats;
using Game.Core.World;

namespace Game.Core.Base
{
    /// <summary>
    /// Мост между городским слоем и нынешней моделью персонажа.
    ///
    /// Живёт ЗДЕСЬ, в умирающем namespace, а не в Game.Core.Checks — это
    /// сознательно: когда модель персонажа перепишут под GDD (4 атрибута +
    /// 10 скилов), умрёт только этот файл, а городской слой не заметит.
    /// Весь маппинг «ключ навыка → стат» собран в одном месте.
    /// </summary>
    public sealed class CompanionActorAdapter : ISettlementActor
    {
        private static readonly Dictionary<string, StatType> Map = new Dictionary<string, StatType>
        {
            // Временное сопоставление на статы Итерации 1. После перестройки
            // ядра здесь будут настоящие скилы GDD.
            { "persuade", StatType.Charisma },
            { "intimidate", StatType.Will },
            { "trade", StatType.Charisma },
            { "medicine", StatType.Medicine },
            { "mechanics", StatType.Engineering },
            { "survival", StatType.Survival },
            { "lockpick", StatType.Tech },
            { "tactics", StatType.Leadership },
            { "ranged", StatType.Aim },
            { "melee", StatType.Aim }
        };

        private readonly Companion _companion;

        public CompanionActorAdapter(Companion companion, bool isProtagonist = false)
        {
            _companion = companion ?? throw new ArgumentNullException(nameof(companion));
            IsProtagonist = isProtagonist;
        }

        public string Id => _companion.Id;
        public bool IsProtagonist { get; }

        public bool IsPresentInSettlement =>
            _companion.Status != CompanionStatus.OnMission &&
            _companion.Status != CompanionStatus.Dead;

        public string HeldPositionId => _companion.AssignedSlotId;

        public int GetCheckValue(SkillKey skill)
        {
            if (skill.IsNone) return 0;
            return Map.TryGetValue(skill.Id, out var stat) ? _companion.GetStat(stat) : 0;
        }

        /// <summary>Трейтов в модели пока нет — появятся при перестройке ядра.</summary>
        public int GetTraitModifier(SkillKey skill) => 0;

        internal Companion Companion => _companion;
    }

    /// <summary>Взгляд городского слоя на ростер.</summary>
    public sealed class RosterAdapter : IRosterView, ICasualtySink
    {
        private readonly Roster _roster;
        private readonly string _protagonistId;
        private readonly List<ISettlementActor> _buffer = new List<ISettlementActor>();

        public RosterAdapter(Roster roster, string protagonistId = null)
        {
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _protagonistId = protagonistId;
        }

        public IReadOnlyList<ISettlementActor> PresentActors
        {
            get
            {
                _buffer.Clear();
                var all = _roster.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var actor = new CompanionActorAdapter(all[i], IsProtagonist(all[i].Id));
                    if (actor.IsPresentInSettlement) _buffer.Add(actor);
                }
                return _buffer;
            }
        }

        public ISettlementActor Protagonist
        {
            get
            {
                if (string.IsNullOrEmpty(_protagonistId)) return null;
                var c = _roster.Get(_protagonistId);
                return c == null ? null : new CompanionActorAdapter(c, true);
            }
        }

        /// <summary>
        /// Кого кризис вообще может тронуть. Протагонист исключён (US-4.4),
        /// мёртвые исключены. Список отсортирован — выбор жертвы обязан быть
        /// воспроизводимым.
        /// </summary>
        public IReadOnlyList<string> KillableActorIds
        {
            get
            {
                var ids = new List<string>();
                var all = _roster.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var c = all[i];
                    if (c.IsDead || IsProtagonist(c.Id)) continue;
                    if (c.Status == CompanionStatus.OnMission) continue;
                    ids.Add(c.Id);
                }
                ids.Sort(StringComparer.Ordinal);
                return ids;
            }
        }

        public string ActorOnPosition(string positionId)
        {
            if (string.IsNullOrEmpty(positionId)) return null;
            var all = _roster.All;
            for (int i = 0; i < all.Count; i++)
                if (!all[i].IsDead &&
                    string.Equals(all[i].AssignedSlotId, positionId, StringComparison.Ordinal))
                    return all[i].Id;
            return null;
        }

        public void Kill(string actorId)
        {
            var c = _roster.Get(actorId);
            if (c == null || IsProtagonist(actorId)) return;
            c.MarkDead();
        }

        public void Wound(string actorId, double injuryPoints)
        {
            var c = _roster.Get(actorId);
            if (c == null || c.IsDead) return;
            c.InjuryPoints += injuryPoints;
            if (c.Status != CompanionStatus.OnMission)
                c.Status = CompanionStatus.Injured;
        }

        private bool IsProtagonist(string id) =>
            !string.IsNullOrEmpty(_protagonistId) &&
            string.Equals(id, _protagonistId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Учёт повторных обращений по темам. Третий подход к одной теме за неделю
    /// дороже первого — защита от спама без единой случайности.
    /// </summary>
    public sealed class RepeatTracker : IRepeatTracker
    {
        private readonly Dictionary<string, List<int>> _byTopic = new Dictionary<string, List<int>>();

        public int AttemptsInWindow(string topicId, int day, int windowDays)
        {
            if (string.IsNullOrEmpty(topicId) || !_byTopic.TryGetValue(topicId, out var days)) return 0;

            int count = 0;
            for (int i = 0; i < days.Count; i++)
                if (day - days[i] < windowDays) count++;
            return count;
        }

        public void Register(string topicId, int day)
        {
            if (string.IsNullOrEmpty(topicId)) return;
            if (!_byTopic.TryGetValue(topicId, out var days))
            {
                days = new List<int>();
                _byTopic[topicId] = days;
            }
            days.Add(day);
        }
    }
}
