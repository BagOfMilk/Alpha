using System.Collections.Generic;
using Game.Core.Checks;

namespace Game.Core.Expeditions
{
    /// <summary>
    /// Стартовые точки вылазок — затравка контента, как DefaultContent для базы.
    ///
    /// Три точки с разными порогами и разной ценой подхода: это минимум, при
    /// котором истощение имеет смысл. С одной точкой игроку некуда переходить,
    /// и механика вырождается в «добыча падает, и всё».
    /// </summary>
    public static class DefaultSites
    {
        /// <summary>Ближний обход: дёшево, безопасно, быстро иссякает.</summary>
        public static ExpeditionSite Outskirts()
        {
            return new ExpeditionSite("outskirts", "Ближние развалины")
            {
                DomainTag = "road",
                QuietDays = 4, ForcefulDays = 2,
                QuietSkill = SkillKeys.Survival, ForcefulSkill = SkillKeys.Melee,
                Threshold = 3,
                BaseMaterials = 2, BaseGold = 8
            };
        }

        /// <summary>Заброшенная мастерская: материалов больше, но нужен механик.</summary>
        public static ExpeditionSite Workshop()
        {
            return new ExpeditionSite("old_workshop", "Заброшенная мастерская")
            {
                DomainTag = "craft",
                QuietDays = 6, ForcefulDays = 3,
                QuietSkill = SkillKeys.Mechanics, ForcefulSkill = SkillKeys.Ranged,
                Threshold = 5,
                BaseMaterials = 4, BaseGold = 6
            };
        }

        /// <summary>Дальний тракт: золото у торговых обозов, но и планка выше.</summary>
        public static ExpeditionSite Highway()
        {
            return new ExpeditionSite("far_highway", "Дальний тракт")
            {
                DomainTag = "trade",
                QuietDays = 8, ForcefulDays = 4,
                QuietSkill = SkillKeys.Trade, ForcefulSkill = SkillKeys.Ranged,
                Threshold = 7,
                BaseMaterials = 3, BaseGold = 20
            };
        }

        public static List<ExpeditionSite> All()
        {
            return new List<ExpeditionSite> { Outskirts(), Workshop(), Highway() };
        }
    }
}
