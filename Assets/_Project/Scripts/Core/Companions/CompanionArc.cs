using System.Collections.Generic;
using Game.Core.Characters;

namespace Game.Core.Companions
{
    /// <summary>Життєвий цикл глави/арки напарника (US-9.5, порт B4).</summary>
    public enum ArcState
    {
        Locked = 0,     // гейт не пройдений (лояльність/прогрес)
        Available = 1,  // можна почати
        InProgress = 2, // глава йде
        Completed = 3,  // вся арка завершена
        Aborted = 4     // напарник загинув/зрадив — арка обірвана (US-9.5)
    }

    /// <summary>
    /// Глава арки: зміст — квест за id (<see cref="QuestId"/>), НЕ
    /// вбудований тип квеста. Core/Quests належить пакету B6 і в цьому
    /// дереві недоступний (§1.1 «фаза B — справді паралельна») — декаплінг
    /// рядком, як і вимагає інтеграційний контракт; playthrough зв'язує
    /// D1/B6 після фази C (шов у seamsForD1). Гейтиться смугою лояльності і
    /// опц. прогрес-флагом (минула глава); при завершенні ставить свій флаг,
    /// відкриваючи наступну. <see cref="TitleKey"/> — ключ тексту (R7), не текст.
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

    /// <summary>Авторська особиста арка напарника (US-9.5, порт B4): багатоетапна, прив'язана до іменного напарника за id.</summary>
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
    /// Проходження арки: гейтить глави лояльністю/прогресом і обривається
    /// смертю або переходом в антагоністи (US-9.5). Зміст глави грає
    /// викликач (через майбутній QuestRun, B6/D1); по успіху кличе
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
        /// Перерахунок доступності: смерть/перехід в антагоністи → Aborted; інакше
        /// гейт поточної глави за смугою лояльності і прогрес-флагом. Главу, що
        /// йде (InProgress), не чіпає (крім обриву). Повертає true, якщо
        /// саме цим викликом глава стала доступною вперше — сигнальна точка
        /// для події «arc.chapter_opened» (§2 №26, інваріант 4): D1 порівнює
        /// повернення, а не робить polls State сам.
        /// </summary>
        public bool Refresh(Companion companion)
        {
            bool wasAvailable = State == ArcState.Available;
            if (IsFinished) return false;

            if (companion == null || companion.IsDead || companion.Status == CompanionStatus.Antagonist)
            {
                State = ArcState.Aborted; // обрив арки (US-9.5)
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

        /// <summary>Починає доступну главу (її зміст далі грає викликач).</summary>
        public bool Begin(Companion companion)
        {
            Refresh(companion);
            if (State != ArcState.Available) return false;
            State = ArcState.InProgress;
            return true;
        }

        /// <summary>Завершує поточну главу (після успішного проходження): ставить флаг, рухає далі.</summary>
        public void CompleteChapter()
        {
            if (IsFinished) return;
            var ch = CurrentChapter;
            if (ch != null && !string.IsNullOrEmpty(ch.CompletionFlag)) _flags.Add(ch.CompletionFlag);
            ChapterIndex++;
            State = CurrentChapter == null ? ArcState.Completed : ArcState.Locked;
        }

        /// <summary>Відновлення прогресу з сейву. Тільки для системи збережень.</summary>
        internal void RestoreState(ArcState state, int chapterIndex)
        {
            State = state;
            ChapterIndex = chapterIndex < 0 ? 0 : chapterIndex;
        }
    }
}
