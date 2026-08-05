using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Quests;

namespace Game.Core.Companions
{
    /// <summary>Жизненный цикл главы/арки напарника.</summary>
    public enum ArcState
    {
        Locked = 0,     // гейт не пройден (лояльность/прогресс)
        Available = 1,  // можно начать
        InProgress = 2, // глава идёт
        Completed = 3,  // вся арка завершена
        Aborted = 4     // напарник погиб/предал — арка оборвана (US-9.5)
    }

    /// <summary>
    /// Глава арки: содержание — обычный <see cref="QuestDefinition"/> (играется через
    /// QuestRun). Гейтится полосой лояльности и опц. прогресс-флагом (прошлая глава);
    /// при завершении ставит свой флаг, открывая следующую.
    /// </summary>
    public sealed class ArcChapter
    {
        public string Id;
        public LoyaltyBand RequiredLoyalty = LoyaltyBand.Steady;
        public string RequiresFlag;
        public string CompletionFlag;
        public QuestDefinition Quest;

        public ArcChapter(string id, QuestDefinition quest)
        {
            Id = id;
            Quest = quest;
        }

        public ArcChapter Loyalty(LoyaltyBand band) { RequiredLoyalty = band; return this; }
        public ArcChapter NeedsFlag(string flag) { RequiresFlag = flag; return this; }
        public ArcChapter SetsFlag(string flag) { CompletionFlag = flag; return this; }
    }

    /// <summary>Авторская личная арка напарника (US-9.5): многоэтапная, привязана к именному напарнику.</summary>
    public sealed class CompanionArc
    {
        public string Id;
        public string CompanionId;
        public string Title;
        public readonly List<ArcChapter> Chapters = new List<ArcChapter>();

        public CompanionArc(string id, string companionId, string title)
        {
            Id = id;
            CompanionId = companionId;
            Title = title;
        }

        public CompanionArc Chapter(ArcChapter chapter)
        {
            Chapters.Add(chapter);
            return this;
        }
    }

    /// <summary>
    /// Прохождение арки: гейтит главы лояльностью/прогрессом и обрывается смертью или
    /// предательством напарника (US-9.5). Содержание главы играет вызывающий через
    /// QuestRun; по успеху зовёт <see cref="CompleteChapter"/>.
    /// </summary>
    public sealed class CompanionArcRun
    {
        private readonly CompanionArc _arc;
        private readonly ICollection<string> _flags;

        public ArcState State { get; private set; } = ArcState.Locked;
        public int ChapterIndex { get; private set; }

        public CompanionArcRun(CompanionArc arc, ICollection<string> flags)
        {
            _arc = arc ?? throw new System.ArgumentNullException(nameof(arc));
            _flags = flags ?? new HashSet<string>();
        }

        public CompanionArc Arc => _arc;
        public ArcChapter CurrentChapter => ChapterIndex < _arc.Chapters.Count ? _arc.Chapters[ChapterIndex] : null;
        public bool IsFinished => State == ArcState.Completed || State == ArcState.Aborted;

        /// <summary>
        /// Пересчёт доступности: смерть/уход в антагонисты → Aborted; иначе гейт
        /// текущей главы по полосе лояльности и прогресс-флагу. Идущую главу
        /// (InProgress) не трогает (кроме обрыва).
        /// </summary>
        public void Refresh(Companion companion)
        {
            if (IsFinished) return;

            if (companion == null || !companion.IsOnPlayerSide)
            {
                State = ArcState.Aborted; // обрыв арки (US-9.5)
                return;
            }
            if (State == ArcState.InProgress) return;

            var ch = CurrentChapter;
            if (ch == null) { State = ArcState.Completed; return; }

            bool flagOk = string.IsNullOrEmpty(ch.RequiresFlag) || _flags.Contains(ch.RequiresFlag);
            bool loyaltyOk = companion.LoyaltyBand >= ch.RequiredLoyalty;
            State = (flagOk && loyaltyOk) ? ArcState.Available : ArcState.Locked;
        }

        /// <summary>Начинает доступную главу (её содержание дальше играет QuestRun).</summary>
        public bool Begin(Companion companion)
        {
            Refresh(companion);
            if (State != ArcState.Available) return false;
            State = ArcState.InProgress;
            return true;
        }

        /// <summary>Завершает текущую главу (после успешного QuestRun): ставит флаг, двигает дальше.</summary>
        public void CompleteChapter()
        {
            if (IsFinished) return;
            var ch = CurrentChapter;
            if (ch != null && !string.IsNullOrEmpty(ch.CompletionFlag)) _flags.Add(ch.CompletionFlag);
            ChapterIndex++;
            State = CurrentChapter == null ? ArcState.Completed : ArcState.Locked;
        }

        /// <summary>Восстановление прогресса из сейва (US-16.1). Только для SaveSystem.</summary>
        internal void RestoreState(ArcState state, int chapterIndex)
        {
            State = state;
            ChapterIndex = chapterIndex < 0 ? 0 : chapterIndex;
        }
    }
}
