using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Characters.Creation;

namespace Game.Core.Session.Views
{
    public sealed class FactionSummary
    {
        public string Id;
        public string DisplayName;
        public string Band;
    }

    public sealed class FactionsView
    {
        public IReadOnlyList<FactionSummary> Factions;
    }

    /// <summary>Чому напарника не можна обрати в загін фіналу (Поправка №17.2).</summary>
    public enum FinaleBlock
    {
        /// <summary>Нічого не заважає — можна обрати.</summary>
        None = 0,
        /// <summary>Стоїть на посту в місті — «на ролі призначений» (правило власника: таких обирати не можна).</summary>
        OnPost = 1,
        /// <summary>Поранений.</summary>
        Injured = 2,
        /// <summary>У вилазці (данж) — не вдома.</summary>
        Away = 3,
        /// <summary>У полоні ворога.</summary>
        Captive = 4
    }

    public sealed class FinaleCandidateView
    {
        public string CompanionId;
        /// <summary>Можна обрати в загін (<c>Block == None</c>).</summary>
        public bool Selectable;
        public FinaleBlock Block;
        /// <summary>Пост, на якому стоїть (лише для <c>Block == OnPost</c>), інакше null.</summary>
        public string PostSlotId;
    }

    /// <summary>
    /// Склад кривавого фіналу (Поправка №17.2): протагоніст іде завжди, решту
    /// обирає гравець з кандидатів — але не тих, хто на посту в місті.
    /// </summary>
    public sealed class FinaleView
    {
        public string ProtagonistId;
        /// <summary>Усі напарники, яких гравець бачить (живі, прибулі, не на боці ворога); обрати можна лише <c>Selectable</c>.</summary>
        public IReadOnlyList<FinaleCandidateView> Candidates;
        /// <summary>Склад разом із протагоністом.</summary>
        public int PartyMax;
        /// <summary>Скільки ворогів вийде на поле — той самий план, що й у реальному бою.</summary>
        public int EnemyCount;
    }

    public sealed class ReadinessView
    {
        public string Band;
        public int MilestonesReached;
        public int MilestonesTotal;
    }

    public sealed class SkillChangeView
    {
        public string SkillKey;
        public int From;
        public int To;
    }

    public sealed class BuildPlannerView
    {
        public string CompanionId;
        public int PointsAvailable;
        public IReadOnlyList<SkillChangeView> Preview;
    }

    public sealed class ProtagonistCreationView
    {
        public string Name;
        public Gender Gender;
        public string BackgroundId;
        public IReadOnlyList<string> AvailableBackgrounds;
        /// <summary>Поточна зовнішність героя (Поправка №19.3) — копія; змінювати через SetProtagonistAppearance.</summary>
        public Appearance Appearance;
    }

    public sealed class SummaryView
    {
        public IReadOnlyList<CompanionSummary> FinalRoster;
        public IReadOnlyList<string> BuiltBuildings;
        public EconomyView Wallet;
        public IReadOnlyList<FactionSummary> Factions;
        public string FinaleOutcomeKey;

        /// <summary>
        /// Відлуння рішень (M1.2): ключі тексту рядків «що громада запам'ятала» — з сюжетних прапорів виборів
        /// (<c>StoryEchoes</c>). Порожній список, якщо гравець нічого такого не вирішував. Лише ключі, жодних чисел.
        /// </summary>
        public IReadOnlyList<string> Echoes = System.Array.Empty<string>();
    }

    public sealed class SceneStepView
    {
        public string ActorId, SecondActorId, SpeakerId, LineKey, EffectKey;

        /// <summary>Як знято план (<see cref="Game.Core.Scenes.ShotFraming"/>): крупний, подвійний, порожній — показ ставить камеру діалогу за ним.</summary>
        public Game.Core.Scenes.ShotFraming Framing;

        public bool IsFinished;
        public string TransitionKey;

        /// <summary>
        /// Поправка №7.8: сцена стоїть на виборі репліки — <see cref="Options"/>
        /// несе варіанти (реюз <see cref="DecisionOptionView"/> — та сама форма,
        /// що й у пропозиції квесту/інциденту: ключ тексту, скіл/поріг/полоса-
        /// прев'ю, жодного прихованого числа, R17). Команда розв'язку —
        /// <c>GameSession.ChooseSceneOption(int)</c>.
        /// </summary>
        public bool IsChoice;

        /// <summary>Id поточного вибору (для журналу механік/ботів) — пусто, якщо IsChoice=false.</summary>
        public string ChoiceId;

        public IReadOnlyList<DecisionOptionView> Options;
    }

    /// <summary>
    /// Журнал механік для тестера (Поправка №7.8): які механіки вже
    /// трапились у цій партії, які ще ні, і як їх викликати. Дані,
    /// обчислені з кумулятивних ключів подій сесії — жодного прихованого
    /// числа.
    /// </summary>
    public sealed class MechanicJournalEntryView
    {
        public string Id;
        public string TitleKey;
        public string HintKey;
        public bool Seen;
    }
}
