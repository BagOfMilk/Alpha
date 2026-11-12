using System.Collections.Generic;
using Game.Core.Ring.Match;

namespace Game.Core.Ring.Career
{
    /// <summary>Заготовка героя з екрана створення (TRPG_DESIGN §5.1): ґімік, роль, вільний бал, ім'я на рингу, колір костюма.</summary>
    public sealed class CharacterDraft
    {
        public Gimmick Gimmick;
        public CareerRole Role;
        /// <summary>Атрибут, у який іде +1 вільний бал (межі −1…+3).</summary>
        public RingAttr FreePoint;
        public string RingName = "";
        /// <summary>Індекс кольору костюма (0…<see cref="CareerSession.CostumeCount"/>−1) — лише вигляд.</summary>
        public int CostumeColor;
        /// <summary>
        /// Дивізіон (P2-6): жіночий чи чоловічий — суперник за замовчуванням своєї статі. Ставить екран створення з тіла героя;
        /// null — не поставив (хост проставить сам через <see cref="CareerSession.SetFemale"/>), до того — чоловічий.
        /// </summary>
        public bool? Female;
    }

    public sealed class GimmickView
    {
        public GimmickDef Def;
        /// <summary>Атрибути ґіміка до вільного балу (RingAttr за індексом).</summary>
        public int[] BaseAttr;
        public List<MoveDef> StartMoves = new List<MoveDef>();
    }

    public sealed class CreationView
    {
        public List<GimmickView> Gimmicks = new List<GimmickView>();
        public int FreePoints = 1;
        public int MinAttr = WrestlerStats.MinAttr;
        public int MaxAttr = WrestlerStats.MaxAttr;
        public int CostumeCount;
        public int MaxNameLength;
    }

    public sealed class InjuryView
    {
        public BodyZone Zone;
        /// <summary>Накопичене ушкодження зони 0–3 (3 = травма).</summary>
        public int ZoneDamage;
        /// <summary>Тижнів до загоєння травми (0 — здорова).</summary>
        public int WeeksLeft;
    }

    /// <summary>Картка вечора (TRPG_DESIGN §3, §8): суперник, місце в картці, план букера, умови, гонорар.</summary>
    public sealed class ShowCardView
    {
        public bool IsBig;
        public bool IsTitle;
        /// <summary>Велике шоу з етапами ворожнечі з Молотом (проходить екранами RingCampaign).</summary>
        public bool IsFeud;
        /// <summary>Прощальний матч чемпіона перед переходом: план букера «віддай пояс» наступнику (А2).</summary>
        public bool IsFarewell;
        public NpcDef Opponent;
        public int OpponentFeudLevel;
        public CardPosition Position;
        public BookerPlan Plan;
        public MatchStipulations Stipulations;
        /// <summary>Гонорар за місце в картці (без бонусів за перемогу й ажіотаж).</summary>
        public int Fee;
        public int WinBonus;
        public int HypeBonus;
        /// <summary>Ажіотаж-бонус промо, що чекає на матч.</summary>
        public int PromoHypeBonus;
        public bool CanWithdraw;
        /// <summary>
        /// Щаблів сили суперника цього вечора (за місцем у картці, ворожнечею, титулом). Від'ємні (до
        /// <see cref="CareerRules.RivalStepsFloor"/>) — суперник слабший за базу ростеру (реванш, «переріс щабель»).
        /// </summary>
        public int RivalSteps;
        /// <summary>Реванш із тим, хто переміг гравця: суперник слабший (ти знаєш його слабкість).</summary>
        public bool IsRematch;
        /// <summary>Гравець переріс свій щабель картки (CareerSession.IsOutgrown): суперник слабший — пік сили.</summary>
        public bool IsOutgrown;
        public List<InjuryView> Injuries = new List<InjuryView>();
    }

    public sealed class WeekView
    {
        public int Season;
        public int Week;
        public int WeeksInSeason;
        public int ActionsLeft;
        public int ActionsTotal;
        public bool Strained;
        public bool PromoChoicePending;
        public int Money;
        public int Popularity;
        public int Trust;
        public int DevPoints;
        public bool IsBigShowWeek;
        public List<WeekActionKind> ActionsDone = new List<WeekActionKind>();
        public List<InjuryView> Injuries = new List<InjuryView>();
        public ShowCardView Card;
        /// <summary>Чи є що лікувати (для кнопки «Відновлення»).</summary>
        public bool CanRecover;
        /// <summary>Іде закулісна сцена: інші дії тижня й кінець тижня недоступні, доки її не закінчено (<see cref="CareerSession.FinishBackstage"/>).</summary>
        public bool BackstageOpen;
    }

    /// <summary>Результат дії тижня: кидок (якщо був), ступінь, зміни шкал, ключ чутки.</summary>
    public sealed class WeekActionResult
    {
        public WeekActionKind Kind;
        public bool Ok;
        /// <summary>Тренер ростера підняв свій атрибут цим тренуванням.</summary>
        public bool CoachRaised;
        /// <summary>Причина відмови (ключ): "stage", "no_actions", "money", "choice_pending", "nothing", "zone", "scene_open".</summary>
        public string Reason;
        public bool HasRoll;
        public int Roll;
        public int Modifier;
        public int Total;
        public RollTier Tier;
        public int MoneyDelta;
        public int DevPointsDelta;
        public int TrustDelta;
        public int FeudDelta;
        public int HypeBonusDelta;
        public bool RivalBoosted;
        /// <summary>Нова травма від підробітку: зона або −1.</summary>
        public int InjuredZone = -1;
        public string RumorKey;
        /// <summary>«7–9» на промо: треба обрати нагороду (<see cref="CareerSession.ChoosePromoReward"/>).</summary>
        public bool NeedsChoice;
        /// <summary>Закулісся: відкрито сцену (<see cref="CareerSession.CurrentBackstage"/>); дію ще не витрачено — її витрачає FinishBackstage.</summary>
        public bool NeedsScene;
        /// <summary>Id і вид закулісної сцени (відкритої чи щойно закінченої).</summary>
        public string SceneId;
        public BackstageKind SceneKind;
        /// <summary>Підсумок закулісної сцени: зміни популярності, поваги роздягальні, дій тижня (від'ємне — витрачено на сцену).</summary>
        public int PopularityDelta;
        public int LockerDelta;
        public int ActionsDelta;
        /// <summary>Сцена змінила план найближчого шоу.</summary>
        public bool PlanChanged;
        /// <summary>Сцена повернула роль (§5.3): нова роль; null — повороту не було.</summary>
        public RingPath? TurnedTo;
    }

    /// <summary>Рядок списку прийомів матчу: прийом + доступність дії з урахуванням травми зони і бонусу прийому.</summary>
    public sealed class MoveOption
    {
        public MoveDef Move;
        public ActionOption Option;
        /// <summary>Штраф до кидка від травми зони (0 або 1).</summary>
        public int InjuryPenalty;
        /// <summary>Той самий прийом двічі поспіль — −1 ажіотажу.</summary>
        public bool RepeatWarning;
    }

    public sealed class CareerView
    {
        public string RingName;
        public int Costume;
        public Gimmick Gimmick;
        public RingPath Role;
        public int[] Attr = new int[5];
        public int Money;
        public int Popularity;
        public int Trust;
        public int DevPoints;
        public List<MoveDef> Moves = new List<MoveDef>();
        public List<string> Perks = new List<string>();
        public List<InjuryView> Injuries = new List<InjuryView>();
        public bool IsChampion;
        public int Season;
        public int Week;
        public int TitlesWon;
        public int FiredCount;
        public bool FeudResolved;
    }
}
