using System;
using Game.Core.Characters.Creation;
using Game.Core.Session;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Одна реалізація <see cref="IUxInput"/> для всіх, хто керує панелями:
    /// рендер (клік людини), автотур і headless-бот кличуть ті самі методи
    /// (UI-14, UX-04; Поправка №22 — паритет входу). Тут живуть шари, контекст
    /// відкритої панелі й відкладене підтвердження незворотного (UX-12);
    /// оболонка лише малює й повідомляє про наслідок (<see cref="Reported"/>).
    /// Без рушія: тестується headless.
    /// </summary>
    public sealed class UxInputCore : IUxInput
    {
        private readonly IUxHost _host;
        private Func<UxOutcome> _pendingRun;

        public UxInputCore(IUxHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public UxLayerState Layers { get; } = new UxLayerState();

        /// <summary>Контекст відкритої панелі (станція, людина, будівля) або null.</summary>
        public string Context { get; private set; }

        /// <summary>Підтвердження, що чекає відповіді, або null.</summary>
        public UxConfirm PendingConfirm { get; private set; }

        /// <summary>Id дії, що чекає підтвердження («start_day», «confirm» — для дій поза панелями).</summary>
        public string PendingActionId { get; private set; }

        /// <summary>Кожен наслідок дії (id, наслідок) — оболонка показує відмову біля кнопки й тост.</summary>
        public event Action<string, UxOutcome> Reported;

        /// <summary>Нова панель відкрилась (оболонка скидає відмови біля кнопок).</summary>
        public event Action Opened;

        public UxLayer Top => Layers.Top;
        public UxPanelId OpenPanel => Layers.OpenPanel;
        public bool ConfirmPending => Layers.ConfirmPending && PendingConfirm != null;

        private bool Female => _host.Gender == Gender.Female;
        private SessionState State => _host.Session != null ? _host.Session.State : SessionState.Title;

        /// <summary>Модель відкритої панелі або null.</summary>
        public UxPanelModel CurrentModel() =>
            Layers.OpenPanel == UxPanelId.None ? null : UxPanelFactory.Build(_host, Layers.OpenPanel, Context);

        public UxOutcome TryOpen(UxPanelId panel, string context)
        {
            Open(panel, context);
            return UxOutcome.Success();
        }

        /// <summary>Відкрити панель: нова замінює стару (UX_DESIGN §3.3); None — закрити.</summary>
        public void Open(UxPanelId panel, string context)
        {
            if (panel == UxPanelId.None) { ClosePanel(); return; }
            Layers.OpenPanelOf(panel);
            Context = context;
            ClearConfirm();
            Opened?.Invoke();
        }

        public void ClosePanel()
        {
            Layers.ClosePanel();
            Context = null;
            ClearConfirm();
        }

        public UxOutcome Invoke(string actionId)
        {
            var action = Find(actionId);
            return action == null ? UxOutcome.Refused(null) : Run(action);
        }

        /// <summary>
        /// Дія з картки: недоступна — відмова з причиною (UI-04); незворотна —
        /// спершу підтвердження (<see cref="UxOutcome.Pending"/>); інакше — виконання.
        /// </summary>
        public UxOutcome Run(UxAction action)
        {
            if (action == null) return UxOutcome.Refused(null);
            string reason = action.ReasonIn(State, Female);
            if (reason != null)
            {
                var refused = UxOutcome.Refused(reason);
                Reported?.Invoke(action.Id, refused);
                return refused;
            }
            if (action.Confirm != null)
            {
                var captured = action;
                Ask(action.Confirm, action.Id, () => UxCommandRunner.Invoke(captured, State, Female));
                return UxOutcome.Pending();
            }
            var outcome = UxCommandRunner.Invoke(action, State, Female);
            Reported?.Invoke(action.Id, outcome);
            return outcome;
        }

        /// <summary>Підтвердження незворотного поза панелями (кривавий фінал, «Кинути» данж, «Почати день» з попередженнями).</summary>
        public void Ask(UxConfirm confirm, string actionId, Func<UxOutcome> run)
        {
            if (confirm == null || run == null) return;
            PendingConfirm = confirm;
            PendingActionId = actionId;
            _pendingRun = run;
            Layers.AskConfirm();
        }

        public UxOutcome Confirm()
        {
            var run = _pendingRun;
            string id = PendingActionId;
            ClearConfirm();
            if (run == null) return UxOutcome.Refused(null);
            var outcome = run() ?? UxOutcome.Success();
            Reported?.Invoke(id, outcome);
            return outcome;
        }

        public void CancelConfirm() => ClearConfirm();

        private void ClearConfirm()
        {
            PendingConfirm = null;
            PendingActionId = null;
            _pendingRun = null;
            Layers.ResolveConfirm();
        }

        private UxAction Find(string actionId)
        {
            var model = CurrentModel();
            if (model == null || string.IsNullOrEmpty(actionId)) return null;
            foreach (var card in model.Cards)
                foreach (var a in card.Actions)
                    if (a.Id == actionId) return a;
            return null;
        }
    }
}
