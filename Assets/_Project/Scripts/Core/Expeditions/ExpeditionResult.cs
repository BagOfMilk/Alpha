using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Checks;

namespace Game.Core.Expeditions
{
    /// <summary>Кого и насколько задело.</summary>
    public struct ExpeditionWound
    {
        public string ActorId;
        public WoundTier Tier;
    }

    /// <summary>
    /// Что игрок видит ДО отправки (US-17.3: пороги показываются заранее).
    /// Ровно те же числа, что применит резолв — считает их один и тот же код.
    /// </summary>
    public sealed class ExpeditionPreview
    {
        public string SiteId;
        public ExpeditionApproach Approach;

        public int Threshold;
        public int PartyValue;
        public int Margin;
        public OutcomeBand Band;

        public int Days;
        public int Materials;
        public int Gold;

        /// <summary>Сколько раз точку уже отрабатывали и во сколько это обошлось добыче.</summary>
        public int TimesWorked;
        public double YieldMultiplier;

        /// <summary>Сколько человек вернётся ранеными. Показывается заранее — это и есть цена.</summary>
        public int ExpectedWounded;

        public bool HasParty => PartyValue > 0 || Threshold <= 0;
    }

    /// <summary>Итог вылазки.</summary>
    public sealed class ExpeditionResult
    {
        public string SiteId;
        public ExpeditionApproach Approach;
        public OutcomeBand Band;

        public int Days;
        public int Materials;
        public int Gold;

        /// <summary>Люди, найденные на точке. Приходят в город через городские работы.</summary>
        public int People;

        public List<ExpeditionWound> Wounded = new List<ExpeditionWound>();

        /// <summary>Кто ходил — в том же порядке, в каком их отправили.</summary>
        public List<string> PartyIds = new List<string>();
    }
}
