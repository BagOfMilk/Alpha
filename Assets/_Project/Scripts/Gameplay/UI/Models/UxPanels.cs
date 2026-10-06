using System;
using System.Collections.Generic;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Іменовані панелі шару «на вимогу» замість магічних індексів вкладок
    /// хаба 0..10 (docs/UX_DESIGN.md §3.2). Число вкладки більше ніде не
    /// ходить: прогулянка, автотур і тести звертаються до панелі за іменем.
    /// </summary>
    public enum UxPanelId
    {
        None = 0,
        DutyBoard,
        Blueprints,
        CouncilTable,
        Muster,
        Stash,
        Workbench,
        People,
        NoticeBoard,
        Journal,
        TrainingGround,
        Save,
        MechanicsJournal,
        Chronicle,
        BuildingCard,
        PlotCard,
        Settings,
        /// <summary>Віче просто неба (№12.9): облава, прибульці, підготовка, радник, полонені.</summary>
        Veche,
        /// <summary>Розмова з людиною на її місці (власник, 30.09.2026: «поговорити з персонажем»).</summary>
        Talk,
        /// <summary>Станція в будівлі або просто неба; контекст — id станції (<c>BuildingCatalog</c>).</summary>
        Station,
        /// <summary>Намет героя біля Віча (Поправка №18): розвиток героя — місце у світі.</summary>
        HeroTent
    }

    /// <summary>Опис панелі: заголовок (ключ тексту), клавіша, стара вкладка хаба, слаг знімка автотуру.</summary>
    public sealed class UxPanelInfo
    {
        public readonly UxPanelId Id;
        public readonly string TitleKey;
        /// <summary>Назва клавіші (як у <c>KeyCode</c>) або null — панель відкривається лише з місця.</summary>
        public readonly string Hotkey;
        /// <summary>Вкладка старого хаба 0..10, функції якої переїхали сюди, або −1.</summary>
        public readonly int LegacyHubTab;
        public readonly string Slug;

        public UxPanelInfo(UxPanelId id, string slug, string hotkey, int legacyHubTab)
        {
            Id = id;
            Slug = slug;
            TitleKey = "ux.panel." + slug;
            Hotkey = hotkey;
            LegacyHubTab = legacyHubTab;
        }
    }

    /// <summary>
    /// Єдиний каталог панелей (UX_DESIGN §3.2 — «єдине джерело правди»). Кожна
    /// вкладка старого хаба має рівно одну домівку тут; охоронець —
    /// <c>UxFoundationTests</c>.
    /// </summary>
    public static class UxPanelCatalog
    {
        /// <summary>Кількість вкладок старого хаба (0..10).</summary>
        public const int LegacyHubTabCount = 11;

        private static readonly UxPanelInfo[] Entries =
        {
            new UxPanelInfo(UxPanelId.DutyBoard,        "duty_board",        "N",   0),
            new UxPanelInfo(UxPanelId.Blueprints,       "blueprints",        null,  1),
            new UxPanelInfo(UxPanelId.Veche,            "veche",             null,  2),
            new UxPanelInfo(UxPanelId.Muster,           "muster",            null,  3),
            new UxPanelInfo(UxPanelId.Stash,            "stash",             null,  4),
            new UxPanelInfo(UxPanelId.People,           "people",            "C",   5),
            new UxPanelInfo(UxPanelId.NoticeBoard,      "notice_board",      null,  6),
            new UxPanelInfo(UxPanelId.Journal,          "journal",           "J",   7),
            new UxPanelInfo(UxPanelId.TrainingGround,   "training_ground",   null,  8),
            new UxPanelInfo(UxPanelId.Save,             "save",              null,  9),
            new UxPanelInfo(UxPanelId.MechanicsJournal, "mechanics_journal", "F10", 10),
            // Хроніка (L) — за треком HUD; клавіші немає, доки панель порожня (UX-10).
            new UxPanelInfo(UxPanelId.Chronicle,        "chronicle",         null,  -1),
            new UxPanelInfo(UxPanelId.Workbench,        "workbench",         null,  -1),
            new UxPanelInfo(UxPanelId.BuildingCard,     "building_card",     null,  -1),
            new UxPanelInfo(UxPanelId.PlotCard,         "plot_card",         null,  -1),
            new UxPanelInfo(UxPanelId.Settings,         "settings",          null,  -1),
            new UxPanelInfo(UxPanelId.CouncilTable,     "council_table",     null,  -1),
            new UxPanelInfo(UxPanelId.Talk,             "talk",              null,  -1),
            new UxPanelInfo(UxPanelId.Station,          "station",           null,  -1),
            new UxPanelInfo(UxPanelId.HeroTent,         "hero_tent",         null,  -1),
        };

        public static IReadOnlyList<UxPanelInfo> All => Entries;

        public static UxPanelInfo Get(UxPanelId id)
        {
            foreach (var e in Entries)
                if (e.Id == id) return e;
            throw new ArgumentOutOfRangeException(nameof(id), "Панелі немає в каталозі: " + id);
        }

        /// <summary>Нова домівка функцій старої вкладки хаба; <see cref="UxPanelId.None"/>, якщо індекс поза 0..10.</summary>
        public static UxPanelId ForLegacyHubTab(int tab)
        {
            foreach (var e in Entries)
                if (e.LegacyHubTab == tab) return e.Id;
            return UxPanelId.None;
        }

        /// <summary>Стара вкладка для панелі — міст на час міграції (U2–U5): поки панелей немає, вони відкривають вкладку.</summary>
        public static int LegacyHubTabOf(UxPanelId id)
        {
            foreach (var e in Entries)
                if (e.Id == id) return e.LegacyHubTab;
            return -1;
        }
    }
}
