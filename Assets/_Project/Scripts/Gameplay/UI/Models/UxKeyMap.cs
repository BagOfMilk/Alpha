using System.Collections.Generic;

namespace Game.Gameplay.UI
{
    /// <summary>Де діє клавіша: у різних областях одна клавіша може мати різних власників.</summary>
    public enum UxKeyScope
    {
        /// <summary>Будь-який стан гри (Esc — пауза).</summary>
        Global = 0,
        /// <summary>Село і інтер'єри: ходьба, E, Tab, панелі.</summary>
        World,
        /// <summary>Відкрита модалка (<c>Widgets.Modal</c>).</summary>
        Modal,
        /// <summary>Портретна сцена гри.</summary>
        Scene,
        /// <summary>Вітрина сцени (<c>ScenePlayer</c>, поза партією).</summary>
        Showcase,
        /// <summary>Тактичний бій.</summary>
        Battle
    }

    /// <summary>Рядок реєстру: клавіша, область, файл-власник, дія.</summary>
    public sealed class UxKeyBinding
    {
        public readonly string Key;
        public readonly UxKeyScope Scope;
        public readonly string OwnerFile;
        public readonly string Purpose;

        public UxKeyBinding(string key, UxKeyScope scope, string ownerFile, string purpose)
        {
            Key = key;
            Scope = scope;
            OwnerFile = ownerFile;
            Purpose = purpose;
        }
    }

    /// <summary>
    /// Реєстр клавіш — одне джерело правди для статуту UI-11 («у клавіші один
    /// власник») і таблиці docs/UX_DESIGN.md §3.6. Назви — як у <c>KeyCode</c>.
    /// Охоронці (<c>UxFoundationTests</c>): жодна пара «клавіша + область» не
    /// має двох власників, і кожне <c>KeyCode.X</c> у коді Gameplay читає саме
    /// той файл, що записаний тут власником. Нова клавіша спершу з'являється
    /// тут — інакше тест упаде.
    /// </summary>
    public static class UxKeyMap
    {
        private static readonly UxKeyBinding[] Entries =
        {
            new UxKeyBinding("Escape",     UxKeyScope.Global,   "GameShell.cs",               "пауза; стек шарів (UX-05)"),
            new UxKeyBinding("Escape",     UxKeyScope.Modal,    "Widgets.cs",                 "закрити модалку"),
            new UxKeyBinding("Tab",        UxKeyScope.World,    "GameShell.Ux.cs",            "Огляд міста"),
            new UxKeyBinding("E",          UxKeyScope.World,    "GameShell.Ux.cs",            "взаємодія з найближчим місцем"),
            new UxKeyBinding("W",          UxKeyScope.World,    "HeroWalker.cs",              "рух"),
            new UxKeyBinding("A",          UxKeyScope.World,    "HeroWalker.cs",              "рух"),
            new UxKeyBinding("S",          UxKeyScope.World,    "HeroWalker.cs",              "рух"),
            new UxKeyBinding("D",          UxKeyScope.World,    "HeroWalker.cs",              "рух"),
            new UxKeyBinding("UpArrow",    UxKeyScope.World,    "HeroWalker.cs",              "рух"),
            new UxKeyBinding("DownArrow",  UxKeyScope.World,    "HeroWalker.cs",              "рух"),
            new UxKeyBinding("LeftArrow",  UxKeyScope.World,    "HeroWalker.cs",              "рух"),
            new UxKeyBinding("RightArrow", UxKeyScope.World,    "HeroWalker.cs",              "рух"),
            new UxKeyBinding("LeftShift",  UxKeyScope.World,    "HeroWalker.cs",              "біг"),
            new UxKeyBinding("RightShift", UxKeyScope.World,    "HeroWalker.cs",              "біг"),
            new UxKeyBinding("N",          UxKeyScope.World,    "GameShell.Ux.cs",            "Дошка наряду (U5)"),
            new UxKeyBinding("C",          UxKeyScope.World,    "GameShell.Ux.cs",            "Люди (U5)"),
            new UxKeyBinding("J",          UxKeyScope.World,    "GameShell.Ux.cs",            "Журнал (U5)"),
            new UxKeyBinding("I",          UxKeyScope.World,    "GameShell.Ux.cs",            "Спорядження — «лялька» і кузня (№19.3)"),
            new UxKeyBinding("L",          UxKeyScope.World,    "GameShell.Ux.cs",            "Хроніка (U5)"),
            new UxKeyBinding("F1",         UxKeyScope.Global,   "GameShell.Ux.cs",            "довідка клавіш (U5)"),
            new UxKeyBinding("F10",        UxKeyScope.Global,   "GameShell.Ux.cs",            "журнал механік, тестова збірка (U5)"),
            new UxKeyBinding("Space",      UxKeyScope.Scene,    "SceneScreen.cs",             "далі"),
            new UxKeyBinding("Space",      UxKeyScope.Showcase, "ScenePlayer.cs",             "далі"),
            new UxKeyBinding("Space",      UxKeyScope.Battle,   "BattleHudScreen.cs",         "кінець ходу / прискорити хід ворога"),
            // Здібності 1–9: у коді лише KeyCode.Alpha1 + i, тому всі дев'ять рядків
            // записано явно — інакше охоронець «один власник» не бачив би Alpha2–Alpha9.
            new UxKeyBinding("Alpha1",     UxKeyScope.Battle,   "BattleHudScreen.cs",         "здібність 1"),
            new UxKeyBinding("Alpha2",     UxKeyScope.Battle,   "BattleHudScreen.cs",         "здібність 2"),
            new UxKeyBinding("Alpha3",     UxKeyScope.Battle,   "BattleHudScreen.cs",         "здібність 3"),
            new UxKeyBinding("Alpha4",     UxKeyScope.Battle,   "BattleHudScreen.cs",         "здібність 4"),
            new UxKeyBinding("Alpha5",     UxKeyScope.Battle,   "BattleHudScreen.cs",         "здібність 5"),
            new UxKeyBinding("Alpha6",     UxKeyScope.Battle,   "BattleHudScreen.cs",         "здібність 6"),
            new UxKeyBinding("Alpha7",     UxKeyScope.Battle,   "BattleHudScreen.cs",         "здібність 7"),
            new UxKeyBinding("Alpha8",     UxKeyScope.Battle,   "BattleHudScreen.cs",         "здібність 8"),
            new UxKeyBinding("Alpha9",     UxKeyScope.Battle,   "BattleHudScreen.cs",         "здібність 9"),
            new UxKeyBinding("O",          UxKeyScope.Battle,   "BattleHudScreen.cs",         "дозор"),
            new UxKeyBinding("Q",          UxKeyScope.Battle,   "BattleArenaController.cs",   "поворот камери"),
            new UxKeyBinding("E",          UxKeyScope.Battle,   "BattleArenaController.cs",   "поворот камери"),
            new UxKeyBinding("W",          UxKeyScope.Battle,   "BattleArenaController.cs",   "рух камери"),
            new UxKeyBinding("A",          UxKeyScope.Battle,   "BattleArenaController.cs",   "рух камери"),
            new UxKeyBinding("S",          UxKeyScope.Battle,   "BattleArenaController.cs",   "рух камери"),
            new UxKeyBinding("D",          UxKeyScope.Battle,   "BattleArenaController.cs",   "рух камери"),
            new UxKeyBinding("UpArrow",    UxKeyScope.Battle,   "BattleArenaController.cs",   "рух камери"),
            new UxKeyBinding("DownArrow",  UxKeyScope.Battle,   "BattleArenaController.cs",   "рух камери"),
            new UxKeyBinding("LeftArrow",  UxKeyScope.Battle,   "BattleArenaController.cs",   "рух камери"),
            new UxKeyBinding("RightArrow", UxKeyScope.Battle,   "BattleArenaController.cs",   "рух камери"),
        };

        public static IReadOnlyList<UxKeyBinding> All => Entries;

        /// <summary>Чи записаний <paramref name="ownerFile"/> власником клавіші в будь-якій області.</summary>
        public static bool IsOwner(string key, string ownerFile)
        {
            foreach (var e in Entries)
                if (e.Key == key && e.OwnerFile == ownerFile) return true;
            return false;
        }
    }
}
