namespace Game.Gameplay.UI
{
    /// <summary>Шари над світом (UX-05): що зараз зверху.</summary>
    public enum UxLayer
    {
        Village = 0,
        Interior,
        Panel,
        Confirm,
        Pause
    }

    /// <summary>
    /// Стек шарів і правило Esc (docs/UX_DESIGN.md §3.5–3.6, UX-05). Над світом
    /// відкритий один верхній шар. Esc знімає його по черзі: діалог
    /// підтвердження → панель → пауза (відкрити/закрити). З будівлі Esc **не**
    /// виводить — вихід лише дверима або кнопкою «Вийти», щоб випадкове Esc не
    /// викидало гравця з інтер'єру.
    /// </summary>
    public sealed class UxLayerState
    {
        public bool InInterior { get; private set; }
        public string InteriorId { get; private set; }
        public UxPanelId OpenPanel { get; private set; }
        public bool ConfirmPending { get; private set; }
        public bool PauseOpen { get; private set; }

        public UxLayer Top
        {
            get
            {
                if (PauseOpen) return UxLayer.Pause;
                if (ConfirmPending) return UxLayer.Confirm;
                if (OpenPanel != UxPanelId.None) return UxLayer.Panel;
                return InInterior ? UxLayer.Interior : UxLayer.Village;
            }
        }

        public void EnterInterior(string interiorId)
        {
            InInterior = true;
            InteriorId = interiorId;
        }

        /// <summary>Вихід із будівлі закриває й панель станції: вона належить будівлі.</summary>
        public void ExitInterior()
        {
            InInterior = false;
            InteriorId = null;
            OpenPanel = UxPanelId.None;
            ConfirmPending = false;
        }

        /// <summary>Відкрита лише одна панель (UX_DESIGN §3.3): нова замінює стару.</summary>
        public void OpenPanelOf(UxPanelId panel)
        {
            OpenPanel = panel;
            ConfirmPending = false;
        }

        public void ClosePanel()
        {
            OpenPanel = UxPanelId.None;
            ConfirmPending = false;
        }

        public void AskConfirm() => ConfirmPending = true;

        public void ResolveConfirm() => ConfirmPending = false;

        /// <summary>Esc: що зняли. Порядок — підтвердження, панель, пауза.</summary>
        public UxLayer OnEscape()
        {
            if (PauseOpen)
            {
                PauseOpen = false;
                return UxLayer.Pause;
            }
            if (ConfirmPending)
            {
                ConfirmPending = false;
                return UxLayer.Confirm;
            }
            if (OpenPanel != UxPanelId.None)
            {
                OpenPanel = UxPanelId.None;
                return UxLayer.Panel;
            }
            PauseOpen = true;
            return UxLayer.Pause;
        }

        /// <summary>Стан гри покинув ранок / вільну гру (UX_DESIGN §3.5): примусовий вихід з будівлі.</summary>
        public void ForceLeaveWorld()
        {
            ExitInterior();
        }
    }
}
