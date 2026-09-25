using System.Collections.Generic;
using Game.Core.Checks;

namespace Game.Core.Expeditions
{
    /// <summary>
    /// Стартові точки вилазок — затравка контенту, як DefaultContent для бази.
    ///
    /// Три точки з різними порогами і різною ціною підходу: це мінімум, за
    /// якого виснаження має сенс. З однією точкою гравцю нема куди переходити,
    /// і механіка вироджується в «здобич падає, і все».
    /// </summary>
    public static class DefaultSites
    {
        /// <summary>Ближній обхід: дешево, безпечно, швидко вичерпується.</summary>
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

        /// <summary>Покинута майстерня: матеріалів більше, але потрібен механік.</summary>
        public static ExpeditionSite Workshop()
        {
            return new ExpeditionSite("old_workshop", "Заброшенная мастерская")
            {
                DomainTag = "craft",
                QuietDays = 6, ForcefulDays = 3,
                QuietSkill = SkillKeys.Mechanics, ForcefulSkill = SkillKeys.Ranged,
                Threshold = 5,
                BaseMaterials = 4, BaseGold = 6,
                // На цій точці живуть ті, хто вцілів: на хорошій полосі відряд приводить їх додому.
                PeopleOnGood = 4
            };
        }

        /// <summary>Дальній тракт: золото у торгових обозів, але і планка вища.</summary>
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
