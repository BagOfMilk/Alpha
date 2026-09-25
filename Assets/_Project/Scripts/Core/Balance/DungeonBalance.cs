using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Крос-контентні числа push-your-luck данжу (Core/Dungeons, R4/R14).
    /// Авторський контент — пороги, лут, вороги — живе в самих кімнатах
    /// (<c>DefaultDungeon.cs</c>); тут лише коефіцієнти прихованої шкали Threat,
    /// спільні для будь-якого сайту.
    /// </summary>
    [Serializable]
    public sealed class DungeonBalance
    {
        /// <summary>На скільки росте Threat за кожен Push (уключно з входом у першу кімнату).</summary>
        public int ThreatPerPush = 2;

        /// <summary>Поріг Threat, з якого полоса стає Tense.</summary>
        public int ThreatTenseAt = 2;

        /// <summary>Поріг, з якого полоса стає Dangerous.</summary>
        public int ThreatDangerousAt = 4;

        /// <summary>Поріг, з якого полоса стає Deadly.</summary>
        public int ThreatDeadlyAt = 6;

        /// <summary>
        /// Штраф до порогу тихого обходу за кожну полосу Threat вище Calm
        /// (споживач шкали — інваріант 6): глибше й шумніше — важче
        /// прослизнути повз наступну кімнату непоміченим.
        /// </summary>
        public int ThreatQuietPenaltyStep = 1;
    }
}
