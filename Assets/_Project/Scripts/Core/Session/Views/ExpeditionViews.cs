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
        public int ExpectedBuildComponent;
        public int ExpectedCraftComponent;
        public int ExpectedGold;
        public int ExpectedWounded;
        public bool IsDelve;

        /// <summary>Лише коли <see cref="IsDelve"/> — прев'ю першої кімнати данжу.</summary>
        public DungeonRoomView FirstRoom;

        /// <summary>
        /// Поправка №15.1: відсутній фахівець (<see cref="Session.
        /// ArrivalsPool.ExpeditionSiteOf"/>), якого «бачили» саме на цій
        /// точці — id, або null, коли на точці ніхто не чекає (звичайна
        /// точка без прив'язки) чи фахівець уже прибув.
        /// </summary>
        public string WaitingSpecialistId;
    }

    /// <summary>
    /// Збори (Поправка №8.3, M1.6): які пости звільнить загін і хто вільний
    /// їх заступити. Заступника обирає гравець (<c>GameSession.DepartExpedition</c>
    /// із заступниками), автопризначення немає.
    /// </summary>
    public sealed class MusterView
    {
        /// <summary>Пости, що спорожніють, у порядку загону.</summary>
        public IReadOnlyList<MusterVacancyView> Vacancies;

        /// <summary>Хто вільний заступити будь-який із постів (без поста, не поранений, не в загоні), за id.</summary>
        public IReadOnlyList<string> FreeIds;

        /// <summary>Є хоч один пост і є кому його заступити — тоді рішення обов'язкове.</summary>
        public bool NeedsChoice => Vacancies != null && Vacancies.Count > 0 && FreeIds != null && FreeIds.Count > 0;
    }

    public sealed class MusterVacancyView
    {
        public string SlotId;
        /// <summary>Хто стоїть на посту і йде.</summary>
        public string HolderId;
        /// <summary>Кого можна поставити замість нього (спільний список для всіх постів загону).</summary>
        public IReadOnlyList<string> CandidateIds;
    }

    public sealed class DungeonView
    {
        public int Depth;
        public string ThreatBand;
        public int RoomsCleared;
        public int UnbankedGold;
        public int UnbankedBuildComponent;
        public int UnbankedCraftComponent;
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

        /// <summary>
        /// Поправка №14.1 (видно ДО вибору, Статут UI-02): як почнеться бій,
        /// якщо обрати кривавий шлях ("FirstStrike"|"Ambush"), і якщо тихий
        /// обхід зірветься ("Spotted"|"UnderFire"). Словом, без чисел; null —
        /// не бойова кімната або прогону ще немає (прев'ю вилазки).
        /// </summary>
        public string BloodyOpening;
        public string QuietFailOpening;

        /// <summary>
        /// Розмова перед боєм (docs/ABILITIES.md §4.6) — «Слово миру», «Скласти зброю!»,
        /// «Відкуп»: поріг, ціна і хто відгукнеться — до кліку (інваріант 8, UI-02).
        /// null — не бойова кімната або прев'ю вилазки.
        /// </summary>
        public IReadOnlyList<ParleyView> Parley;
    }

    /// <summary>Одна форма розмови перед боєм — що з чим порівнюється і що буде.</summary>
    public sealed class ParleyView
    {
        /// <summary>"peace" | "surrender" | "bribe".</summary>
        public string Form;
        /// <summary>"persuade" | "intimidate" | "trade".</summary>
        public string SkillKey;
        /// <summary>Найкраща навичка в загоні і скільки треба (Воля ватажка + надбавка; страх громади — дорожче).</summary>
        public int ParleyValue, ParleyThreshold;
        public bool Passes;
        /// <summary>Чому не можна: "immune" (ніхто не здається) | "not_for_sale" | "poor" | "no_room"; null — можна.</summary>
        public string BlockKey;
        /// <summary>«Відкуп»: скільки золота з казни.</summary>
        public int GoldCost;
        /// <summary>Скільки ворогів відгукнеться (піде, здасться, візьме гроші) і скільки лишиться битися.</summary>
        public int LeavingCount, RemainingCount;
    }
}
