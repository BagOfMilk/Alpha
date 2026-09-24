using System.Collections.Generic;
using Game.Core.Expeditions;

namespace Game.Core.Session.Views
{
    public sealed class ExpeditionPreviewView
    {
        public string SiteId;
        public ExpeditionApproach Approach;
        public int Threshold;
        public int PartyValue;
        public int Days;
        public string ExpectedBand;
        public int ExpectedMaterials;
        public int ExpectedGold;
        public int ExpectedWounded;
        public bool IsDelve;

        /// <summary>Лише коли <see cref="IsDelve"/> — прев'ю першої кімнати данжу.</summary>
        public DungeonRoomView FirstRoom;
    }

    public sealed class DungeonView
    {
        public int Depth;
        public string ThreatBand;
        public int RoomsCleared;
        public int UnbankedGold;
        public int UnbankedMaterials;
        public DungeonRoomView CurrentRoom;

        /// <summary>Прогін завершено (Extracted/Wiped/Abandoned) — назва полоси Outcome.</summary>
        public string Outcome;

        /// <summary>Зараз чекає результату бою бойової кімнати.</summary>
        public bool AwaitingBattle;

        /// <summary>
        /// Полірування (ціль 6 «Рішення», owner: "dungeon room card shows
        /// the party"). Хто пішов у цей данж — companionId, той самий
        /// порядок, що DungeonRun.PartyIds.
        /// </summary>
        public IReadOnlyList<string> PartyIds;
    }

    public sealed class DungeonRoomView
    {
        public string Id, DisplayName, Type; // "Combat"|"Treasure"|"Event"
        public bool HasQuietBypass;
        public string QuietSkillKey;
        public int QuietThreshold;
        public string BloodySkillKey;
        public int BloodyThreshold;
        public IReadOnlyList<string> EventOptionKeys;

        /// <summary>
        /// Полірування (ціль 6 «Рішення», owner: "the quiet candidate ...
        /// expected band"). Найкращий член ПАРТІЇ данжу (не всього ростеру)
        /// для тихого обходу цієї кімнати — <c>ISettlementActor.GetCheckValue</c>,
        /// та сама формула, що резолвить сам обхід (DungeonRun.BestQuietBand).
        /// </summary>
        public string QuietBestActorId;
        public bool QuietHasCandidate;

        /// <summary>
        /// Полірування (ціль 6 «Рішення», owner: "тактичний бій: N ворогів").
        /// Скільки ворогів у бойовій кімнаті — з <c>DungeonRoomDefinition.EnemyIds</c>,
        /// 0 для не-бойових кімнат.
        /// </summary>
        public int EnemyCount;
    }
}
