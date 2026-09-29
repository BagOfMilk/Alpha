namespace Game.Gameplay.UI
{
    /// <summary>
    /// Вхід у панелі й картки шару «на вимогу» (docs/UX_DESIGN.md §7.1). Його
    /// кличуть і рендер (клік людини), і автотур — однаково, тому тур не може
    /// пройти повз інтерфейс (UI-14, UX-04), як уже заведено для бою
    /// (<c>IBattleInput</c>). Реалізація — в оболонці, кроки U3–U5.
    /// </summary>
    public interface IUxInput
    {
        UxLayer Top { get; }
        UxPanelId OpenPanel { get; }

        /// <summary>Відкрити панель; <paramref name="context"/> — місце (будівля, пост), до якого вона прив'язана, або null.</summary>
        UxOutcome TryOpen(UxPanelId panel, string context);

        void ClosePanel();

        /// <summary>Виконати дію відкритої панелі за її ідентифікатором.</summary>
        UxOutcome Invoke(string actionId);

        bool ConfirmPending { get; }
        UxOutcome Confirm();
        void CancelConfirm();
    }

    /// <summary>
    /// Вхід у світ: наведення, ходьба, будівлі (docs/UX_DESIGN.md §4.3). Людина
    /// робить те саме мишею й клавішами; автотур — через цей контракт.
    /// Реалізація — оболонка й <c>HeroWalker</c>, кроки U3–U4.
    /// </summary>
    public interface IWorldInput
    {
        string HoveredPlaceId { get; }
        bool InInterior { get; }
        string InteriorId { get; }

        void RequestWalkTo(string placeId);

        /// <summary>Увійти в будівлю: пішки до дверей або швидким переходом (<paramref name="quick"/>).</summary>
        void RequestEnter(string buildingId, bool quick);

        void RequestExit();
    }
}
