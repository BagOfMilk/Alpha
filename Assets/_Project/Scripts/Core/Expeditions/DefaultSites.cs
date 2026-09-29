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
    ///
    /// Поправка №12.5 (два компоненти): точки різняться й тим, ЩО дають.
    /// Ближні руїни — будівельний (колоди, камінь, цвяхи з розібраних хат);
    /// покинута майстерня — переважно крафтовий (інструмент, залізо, шкіра);
    /// дальній тракт — золото обозів і трохи обох. Хочеш будуватись — ходиш
    /// у руїни, хочеш кувати — у майстерню, і виснаження точки штовхає
    /// міняти маршрут. Числа — ПЛЕЙСХОЛДЕР.
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
                BaseBuildComponent = 3, BaseCraftComponent = 0, BaseGold = 8
            };
        }

        /// <summary>Покинута майстерня: головний кран крафтового компонента, але потрібен механік.</summary>
        public static ExpeditionSite Workshop()
        {
            return new ExpeditionSite("old_workshop", "Заброшенная мастерская")
            {
                DomainTag = "craft",
                QuietDays = 6, ForcefulDays = 3,
                QuietSkill = SkillKeys.Mechanics, ForcefulSkill = SkillKeys.Ranged,
                Threshold = 5,
                BaseBuildComponent = 1, BaseCraftComponent = 4, BaseGold = 6,
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
                BaseBuildComponent = 2, BaseCraftComponent = 1, BaseGold = 20
            };
        }

        public static List<ExpeditionSite> All()
        {
            return new List<ExpeditionSite> { Outskirts(), Workshop(), Highway() };
        }
    }
}
