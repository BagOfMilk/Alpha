using System.Collections.Generic;
using Game.Core.Characters.Perks;
using Game.Core.Stats;

namespace Game.Core.Characters.Build
{
    /// <summary>
    /// Намерение игрока: куда вложить очки и какие перки взять. Отдельный тип, а
    /// не прямая правка бойца, потому что план живёт ДО подтверждения — его
    /// показывают, пересобирают и бросают без последствий (US-2.3).
    ///
    /// Порядок перков значим: перк плана может быть пререквизитом следующего.
    /// </summary>
    public sealed class BuildPlan
    {
        private readonly Dictionary<SkillType, int> _invest = new Dictionary<SkillType, int>();
        private readonly List<SkillType> _order = new List<SkillType>();
        private readonly List<PerkDefinition> _perks = new List<PerkDefinition>();

        /// <summary>Скилы, в которые вложено, в порядке первого обращения.</summary>
        public IReadOnlyList<SkillType> Skills => _order;

        public IReadOnlyList<PerkDefinition> Perks => _perks;

        /// <summary>Ничего не запланировано (в том числе после подтверждения).</summary>
        public bool IsEmpty => _order.Count == 0 && _perks.Count == 0;

        /// <summary>Сколько очков просит план: одно очко — один шаг скила.</summary>
        public int PointCost
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < _order.Count; i++) sum += _invest[_order[i]];
                return sum;
            }
        }

        public int InvestedIn(SkillType skill)
            => _invest.TryGetValue(skill, out var points) ? points : 0;

        /// <summary>
        /// Вложить очки в скил. Повторный вызов по тому же скилу складывается:
        /// игрок жмёт «плюс» несколько раз, а не пересобирает план заново.
        /// </summary>
        public BuildPlan Invest(SkillType skill, int points)
        {
            if (skill == SkillType.None || points <= 0) return this;
            if (!_invest.ContainsKey(skill))
            {
                _invest[skill] = 0;
                _order.Add(skill);
            }
            _invest[skill] += points;
            return this;
        }

        public BuildPlan TakePerk(PerkDefinition perk)
        {
            if (perk == null || string.IsNullOrEmpty(perk.Id)) return this;
            for (int i = 0; i < _perks.Count; i++)
                if (_perks[i].Id == perk.Id) return this; // дважды один перк не берут
            _perks.Add(perk);
            return this;
        }

        /// <summary>План израсходован (подтверждён) либо отменён игроком.</summary>
        public void Clear()
        {
            _invest.Clear();
            _order.Clear();
            _perks.Clear();
        }
    }
}
