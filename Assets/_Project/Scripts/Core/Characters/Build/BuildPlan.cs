using System.Collections.Generic;
using Game.Core.Characters.Perks;
using Game.Core.Stats;

namespace Game.Core.Characters.Build
{
    /// <summary>
    /// Намір гравця: куди вкласти очки і які перки взяти. Окремий тип, а
    /// не пряма правка бійця, бо план живе ДО підтвердження — його
    /// показують, перезбирають і кидають без наслідків (US-2.3).
    ///
    /// Порядок перків значущий: перк плану може бути передумовою наступного.
    /// </summary>
    public sealed class BuildPlan
    {
        private readonly Dictionary<SkillType, int> _invest = new Dictionary<SkillType, int>();
        private readonly List<SkillType> _order = new List<SkillType>();
        private readonly List<PerkDefinition> _perks = new List<PerkDefinition>();

        /// <summary>Скіли, у які вкладено, у порядку першого звернення.</summary>
        public IReadOnlyList<SkillType> Skills => _order;

        public IReadOnlyList<PerkDefinition> Perks => _perks;

        /// <summary>Нічого не заплановано (у тому числі після підтвердження).</summary>
        public bool IsEmpty => _order.Count == 0 && _perks.Count == 0;

        /// <summary>Скільки очок просить план: одне очко — один крок скіла.</summary>
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
        /// Вкласти очки в скіл. Повторний виклик за тим самим скілом складається:
        /// гравець тисне «плюс» кілька разів, а не перезбирає план заново.
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
                if (_perks[i].Id == perk.Id) return this; // двічі один перк не беруть
            _perks.Add(perk);
            return this;
        }

        /// <summary>План витрачено (підтверджено) або скасовано гравцем.</summary>
        public void Clear()
        {
            _invest.Clear();
            _order.Clear();
            _perks.Clear();
        }
    }
}
