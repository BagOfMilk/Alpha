using System.Collections.Generic;
using Game.Core.Checks;

namespace Game.Core.Session.Views
{
    /// <summary>Один шлях розв'язку (§4.2 TEST_BUILD.md) — порогом видно заздалегідь (інваріант 8).</summary>
    public sealed class DecisionOptionView
    {
        public IncidentPathView Path;
        public string SkillKey;
        public int Threshold;
        public string Form;
        public string BestActorId;
        public bool HasCandidate;
        public string ExpectedBand;

        /// <summary>Лише для варіантів квесту-вибору (§4.1 QuestOfferView): ключ репліки варіанту, а не скіл.</summary>
        public string TextKey;

        /// <summary>
        /// Полірування (ціль 6 «Рішення», owner: "the option text says so
        /// (тактичний бій: N ворогів), not just a skill threshold"). >0 —
        /// цей шлях веде у тактичний бій (порогу немає — кроваво на такому
        /// вузлі завжди бій, IncidentResolver.Resolve не викликається
        /// взагалі); 0 — звичайна перевірка. Наразі заповнюється лише для
        /// вузла 1 (pass_vanguard, кроваво) — дунж/фінал мають власні View.
        /// </summary>
        public int TacticalBattleEnemyCount;
    }

    /// <summary>Дзеркало <see cref="Loop.IncidentPath"/> — щоб View-шар не тягнув Core.Loop у публічний контракт напряму.</summary>
    public enum IncidentPathView { Quiet = 0, Bloody = 1 }

    /// <summary>Пропозиція, що чекає на хід гравця (§4.2): інцидент/квест/криза/фінал.</summary>
    public class PendingOfferView
    {
        public string Kind; // "Incident" | "Quest" | "Crisis" | "Finale"
        public string TopicId;
        public bool IsCrisis;
        public IReadOnlyList<DecisionOptionView> Options;
    }

    public sealed class QuestOfferView : PendingOfferView
    {
        public string QuestId;
        public int Stage;
    }
}
