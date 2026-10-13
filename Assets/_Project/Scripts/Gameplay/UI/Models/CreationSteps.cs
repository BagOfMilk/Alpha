namespace Game.Gameplay.UI
{
    /// <summary>
    /// Кроки екрана створення героя (плейтест 13.10.2026: «ЧОму ти не зробив як в інших Сrpg…»): походження → клас →
    /// атрибути й навички → зовнішність → ім'я, як у CRPG. Чистий C#: екран (UI Toolkit, поза лінтом) і автотур читають
    /// одне й те саме.
    /// </summary>
    public static class CreationSteps
    {
        public const int Origin = 0, Class = 1, Attributes = 2, Look = 3, Name = 4, Count = 5;

        /// <summary>Автотур ставить крок сам, щоб зняти кожен; null — крок обирає гравець.</summary>
        public static int? TourStep;

        public static readonly string[] TitleKeys =
        {
            "ui.creation.step.origin", "ui.creation.step.class", "ui.creation.step.attributes", "ui.creation.step.look",
            "ui.creation.step.name"
        };

        /// <summary>Наскільки камера прев'ю наближена до обличчя: 0 — на повний зріст, 1 — портрет.</summary>
        public static float PreviewZoom(int step) => step == Name ? 1f : 0f; // зовнішність — на повний зріст: видно вбрання

        public static int Clamp(int step) => step < 0 ? 0 : step >= Count ? Count - 1 : step;
    }
}
