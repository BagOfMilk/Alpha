using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Stats;
using Game.Core.World;

namespace Game.Core.Base
{
    /// <summary>
    /// Мост между городским слоем и моделью персонажа.
    ///
    /// Живёт ЗДЕСЬ, а не в Game.Core.Checks — сознательно: городской слой знает
    /// только строковый SkillKey, а про атрибуты, скилы и трейты не знает
    /// ничего. Перестройка модели меняет этот файл и больше ничей.
    ///
    /// Сопоставления «ключ городского слоя → скил» здесь больше нет: оно живёт
    /// в Skills.KeyId рядом с самим enum. Две таблицы разъезжаются, одна — нет.
    /// </summary>
    public sealed class CompanionActorAdapter : ISettlementActor
    {
        private readonly Companion _companion;
        private readonly BalanceConfig _balance;

        public CompanionActorAdapter(Companion companion, bool isProtagonist = false, BalanceConfig balance = null)
        {
            _companion = companion ?? throw new ArgumentNullException(nameof(companion));
            IsProtagonist = isProtagonist;
            _balance = balance;
        }

        public string Id => _companion.Id;
        public bool IsProtagonist { get; }

        public bool IsPresentInSettlement =>
            _companion.Status != CompanionStatus.OnMission &&
            _companion.Status != CompanionStatus.Dead;

        public string HeldPositionId => _companion.AssignedSlotId;

        public int GetCheckValue(SkillKey skill)
        {
            var s = Resolve(skill);
            return s == SkillType.None ? 0 : _companion.Skill(s);
        }

        /// <summary>
        /// Всё, что ложится поверх голого скила: трейты, шрамы, перки.
        ///
        /// Считается как «резолвнутое минус база», а не суммированием нужных
        /// модификаторов вручную. Это ровно то разбиение, при котором
        /// CheckResolver складывает GetCheckValue + GetTraitModifier и получает
        /// резолвнутое значение без двойного счёта — по построению, а не по
        /// договорённости.
        /// </summary>
        public int GetTraitModifier(SkillKey skill)
        {
            var s = Resolve(skill);
            if (s == SkillType.None) return 0;
            return _companion.Resolve(_balance).Skill(s) - _companion.Skill(s);
        }

        private static SkillType Resolve(SkillKey skill)
            => skill.IsNone ? SkillType.None : Skills.FromKeyId(skill.Id);

        internal Companion Companion => _companion;
    }

    /// <summary>Взгляд городского слоя на ростер.</summary>
    public sealed class RosterAdapter : IRosterView, ICasualtySink
    {
        private readonly Roster _roster;
        private readonly string _protagonistId;
        private readonly BalanceConfig _balance;
        private readonly List<ISettlementActor> _buffer = new List<ISettlementActor>();

        /// <summary>
        /// Кто уходит в вылазку. Только для симуляционного харнеса: у самой
        /// игры партию будет задавать экран сборов, которого пока нет.
        /// </summary>
        internal List<Companion> PartyForSim { get; set; }
        public RosterAdapter(Roster roster, string protagonistId = null, BalanceConfig balance = null)
        {
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _protagonistId = protagonistId;
            _balance = balance;
        }

        public IReadOnlyList<ISettlementActor> PresentActors
        {
            get
            {
                _buffer.Clear();
                var all = _roster.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var actor = new CompanionActorAdapter(all[i], IsProtagonist(all[i].Id), _balance);
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
                return c == null ? null : new CompanionActorAdapter(c, true, _balance);
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
    public sealed class RepeatTracker : IRepeatTracker, Game.Core.Loop.IStateBlob
    {
        private readonly Dictionary<string, List<int>> _byTopic = new Dictionary<string, List<int>>();

        /// <summary>
        /// Без этого штраф за повторы обнулялся бы каждой загрузкой, и сейв
        /// становился бы способом снять наказание.
        /// </summary>
        public string CaptureState()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var pair in _byTopic)
            {
                if (pair.Value.Count == 0) continue;
                if (sb.Length > 0) sb.Append('~');
                sb.Append(pair.Key).Append('#');
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(pair.Value[i]);
                }
            }
            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            _byTopic.Clear();
            if (string.IsNullOrEmpty(blob)) return;

            foreach (var entry in blob.Split('~'))
            {
                int hash = entry.IndexOf('#');
                if (hash <= 0) continue;

                var days = new List<int>();
                foreach (var d in entry.Substring(hash + 1).Split(','))
                {
                    int v;
                    if (int.TryParse(d, out v)) days.Add(v);
                }
                _byTopic[entry.Substring(0, hash)] = days;
            }
        }

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
