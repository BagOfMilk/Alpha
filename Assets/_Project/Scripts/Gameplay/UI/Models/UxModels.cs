using System;
using System.Collections.Generic;
using Game.Core.Session;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Сенс кольору за семантичною палітрою HUD (docs/HUD_DESIGN.md §6.1):
    /// колір береться з теми HUD, модель знає лише сенс (UI-01).
    /// </summary>
    public enum UxTone
    {
        Neutral = 0,
        Good,
        Bad,
        Threat,
        Own
    }

    /// <summary>Вага кнопки дії: одна Primary на вигляд (UX_DESIGN §6, компонент 1).</summary>
    public enum UxIntent
    {
        Primary = 0,
        Secondary,
        Danger
    }

    /// <summary>Чип: ціна, поріг чи стан — коротким текстом і сенсом кольору.</summary>
    public sealed class UxChip
    {
        public readonly string Text;
        public readonly UxTone Tone;

        public UxChip(string text, UxTone tone = UxTone.Neutral)
        {
            Text = text ?? string.Empty;
            Tone = tone;
        }
    }

    /// <summary>
    /// Діалог підтвердження — лише для незворотного (UX-12): заголовок-питання,
    /// перелік «що втрачаєш», дієслово небезпечної кнопки. «Скасувати» — за
    /// замовчуванням.
    /// </summary>
    public sealed class UxConfirm
    {
        public readonly string Question;
        public readonly IReadOnlyList<string> Losses;
        public readonly string ConfirmVerb;

        public UxConfirm(string question, string confirmVerb, IReadOnlyList<string> losses = null)
        {
            Question = question ?? string.Empty;
            ConfirmVerb = confirmVerb ?? string.Empty;
            Losses = losses ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Дія на картці (UX_DESIGN §6, компонент 1). Дієслово першим, ціна й поріг —
    /// чипами до кліку (UI-02), недоступна — з причиною (UI-03, UI-04).
    /// Виконання — через <see cref="UxCommandRunner"/>: той самий шлях кличуть
    /// і рендер, і автотур (UI-14, UX-04).
    /// </summary>
    public sealed class UxAction
    {
        public string Id;
        public string Label;
        public UxIntent Intent;
        public readonly List<UxChip> Chips = new List<UxChip>();
        /// <summary>Причина недоступності людською мовою; null — доступна (якщо стан дозволяє).</summary>
        public string DisabledReason;
        /// <summary>Стани гри, у яких дія має сенс; порожньо — будь-який. Перевіряється до виклику ядра.</summary>
        public readonly List<SessionState> AllowedStates = new List<SessionState>();
        public UxConfirm Confirm;
        /// <summary>Назва клавіші-підказки (як у <c>KeyCode</c>) або null.</summary>
        public string Hotkey;
        /// <summary>Перемикач у стані «обрано» (точка вилазки, підхід, людина в загоні) — рендер підсвічує.</summary>
        public bool Selected;
        /// <summary>Виконання: повертає наслідок (успіх, відмова, помилка).</summary>
        public Func<UxOutcome> Execute;

        public bool AllowedIn(SessionState state) => AllowedStates.Count == 0 || AllowedStates.Contains(state);

        /// <summary>Причина, чому дію зараз не виконати, або null. Стан гри перевіряється раніше за власну причину дії.</summary>
        public string ReasonIn(SessionState state, bool female)
        {
            if (!AllowedIn(state)) return UxErrorText.OnlyIn(AllowedStates, female);
            return string.IsNullOrEmpty(DisabledReason) ? null : DisabledReason;
        }
    }

    /// <summary>Картка: заголовок, підзаголовок, чипи, рядки, позначки стадій, дії. Таблиці як примітиву немає (UX_DESIGN §6).</summary>
    public sealed class UxCard
    {
        /// <summary>Підвкладка панелі (не більше чотирьох на панель, UX_DESIGN §3.3); null — без поділу.</summary>
        public string Section;
        public string Title;
        public string Subtitle;
        /// <summary>
        /// Місце в селі, куди веде картка («Показати в селі», UX-04): дія живе
        /// там, панель лише веде. null — картка без посилання.
        /// </summary>
        public string LinkPlaceId;
        public readonly List<UxChip> Chips = new List<UxChip>();
        public readonly List<string> Lines = new List<string>();
        /// <summary>Позначки стадій (будівництво): заповнено / усього; 0 з 0 — немає.</summary>
        public int PipsFilled;
        public int PipsTotal;
        public readonly List<UxAction> Actions = new List<UxAction>();
    }

    /// <summary>Панель — упорядкований набір карток і порожній стан, що навчає (UX-13).</summary>
    public sealed class UxPanelModel
    {
        public UxPanelId Id;
        public string Title;
        public readonly List<UxCard> Cards = new List<UxCard>();
        /// <summary>Текст порожнього стану: чому порожньо і що зробити.</summary>
        public string EmptyText;
    }
}
