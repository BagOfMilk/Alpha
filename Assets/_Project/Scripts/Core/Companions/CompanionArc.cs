using System.Collections.Generic;
using Game.Core.Characters;

namespace Game.Core.Companions
{
    /// <summary>Жизненный цикл главы/арки напарника (US-9.5, порт B4).</summary>
    public enum ArcState
    {
        Locked = 0,     // гейт не пройден (лояльность/прогресс)
        Available = 1,  // можно начать
        InProgress = 2, // глава идёт
        Completed = 3,  // вся арка завершена
        Aborted = 4     // напарник погиб/предал — арка оборвана (US-9.5)
    }

    /// <summary>
    /// Глава арки: содержание — квест по id (<see cref="QuestId"/>), НЕ
    /// встроенный тип квеста. Core/Quests принадлежит пакету B6 и в этом
    /// дереве недоступен (§1.1 «фаза B — справді паралельна») — декаплинг
    /// строкой, как и требует интеграционный контракт; playthrough связывает
    /// D1/B6 после фазы C (шов в seamsForD1). Гейтится полосой лояльности и
    /// опц. прогресс-флагом (прошлая глава); при завершении ставит свой флаг,
    /// открывая следующую. <see cref="TitleKey"/> — ключ текста (R7), не текст.
    /// </summary>
    public sealed class ArcChapter
    {
        public readonly string Id;
        public readonly string QuestId;
        public readonly string TitleKey;
        public LoyaltyBand RequiredLoyalty = LoyaltyBand.Steady;
        public string RequiresFlag;
        public string CompletionFlag;

        public ArcChapter(string id, string questId, string titleKey)
        {
            Id = id;
            QuestId = questId;
            TitleKey = titleKey;
        }

        public ArcChapter Loyalty(LoyaltyBand band) { RequiredLoyalty = band; return this; }
        public ArcChapter NeedsFlag(string flag) { RequiresFlag = flag; return this; }
        public ArcChapter SetsFlag(string flag) { CompletionFlag = flag; return this; }
    }

    /// <summary>Авторская личная арка напарника (US-9.5, порт B4): многоэтапная, привязана к именному напарнику по id.</summary>
    public sealed class CompanionArc
    {
        public readonly string Id;
        public readonly string CompanionId;
        public readonly string TitleKey;
        public readonly List<ArcChapter> Chapters = new List<ArcChapter>();

        public CompanionArc(string id, string companionId, string titleKey)
        {
            Id = id;
            CompanionId = companionId;
            TitleKey = titleKey;
        }

        public CompanionArc Chapter(ArcChapter chapter)
        {
            if (chapter != null) Chapters.Add(chapter);
            return this;
        }
    }

    /// <summary>
    /// Прохождение арки: гейтит главы лояльностью/прогрессом и обрывается
    /// смертью или уходом в антагонисты (US-9.5). Содержание главы играет
    /// вызывающий (через будущий QuestRun, B6/D1); по успеху зовёт
    /// <see cref="CompleteChapter"/>.
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
        /// Пересчёт доступности: смерть/уход в антагонисты → Aborted; иначе
        /// гейт текущей главы по полосе лояльности и прогресс-флагу. Идущую
        /// главу (InProgress) не трогает (кроме обрыва). Возвращает true, если
        /// именно этим вызовом глава стала доступной впервые — сигнальная точка
        /// для события «arc.chapter_opened» (§2 №26, инвариант 4): D1 сравнивает
        /// возврат, а не polls State сам.
        /// </summary>
        public bool Refresh(Companion companion)
        {
            bool wasAvailable = State == ArcState.Available;
            if (IsFinished) return false;

            if (companion == null || companion.IsDead || companion.Status == CompanionStatus.Antagonist)
            {
                State = ArcState.Aborted; // обрыв арки (US-9.5)
                return false;
            }
            if (State == ArcState.InProgress) return false;

            var ch = CurrentChapter;
            if (ch == null) { State = ArcState.Completed; return false; }

            bool flagOk = string.IsNullOrEmpty(ch.RequiresFlag) || _flags.Contains(ch.RequiresFlag);
            bool loyaltyOk = companion.LoyaltyBand >= ch.RequiredLoyalty;
            State = (flagOk && loyaltyOk) ? ArcState.Available : ArcState.Locked;

            return State == ArcState.Available && !wasAvailable;
        }

        /// <summary>Начинает доступную главу (её содержание дальше играет вызывающий).</summary>
        public bool Begin(Companion companion)
        {
            Refresh(companion);
            if (State != ArcState.Available) return false;
            State = ArcState.InProgress;
            return true;
        }

        /// <summary>Завершает текущую главу (после успешного прохождения): ставит флаг, двигает дальше.</summary>
        public void CompleteChapter()
        {
            if (IsFinished) return;
            var ch = CurrentChapter;
            if (ch != null && !string.IsNullOrEmpty(ch.CompletionFlag)) _flags.Add(ch.CompletionFlag);
            ChapterIndex++;
            State = CurrentChapter == null ? ArcState.Completed : ArcState.Locked;
        }

        /// <summary>Восстановление прогресса из сейва. Только для системы сохранений.</summary>
        internal void RestoreState(ArcState state, int chapterIndex)
        {
            State = state;
            ChapterIndex = chapterIndex < 0 ? 0 : chapterIndex;
        }
    }
}
