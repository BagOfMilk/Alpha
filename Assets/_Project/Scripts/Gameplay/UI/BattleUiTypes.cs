using System.Collections.Generic;

namespace Game.Gameplay
{
    // Бій v2 — чисті типи контракту (docs/COMBAT_V2.md §7.4). ЗАМОРОЖЕНО разом
    // із BattleContract.cs. Жодного типу рушія: файл компілюють і лінт, і
    // headless-тести (BattleLogText спирається на BattleLogKind).

    /// <summary>
    /// Озброєна дія гравця. <see cref="None"/> — «розумний клік»: досяжний тайл —
    /// рух, ворог — атака зброєю.
    /// </summary>
    public enum ArmedAction { None, Ability, OverwatchAim }

    /// <summary>Сенс рядка логу / спливаючого напису — визначає колір (docs/COMBAT_V2.md §2).</summary>
    public enum BattleLogKind
    {
        Neutral, Round, Move, Miss, Graze, Hit, Crit, Damage, Heal,
        Status, Overwatch, Ability, Downed, Death, Victory, Defeat, Rejection
    }

    /// <summary>Рядок журналу бою, готовий до показу.</summary>
    public sealed class BattleLogEntryUi
    {
        public int Round;
        public string Text;
        public BattleLogKind Kind;
    }

    /// <summary>
    /// Точка над головою юніта в координатах GUI (0,0 — лівий верхній кут,
    /// як у OnGUI): сюди HUD малює ім'я, смужку HP, стани.
    /// </summary>
    public sealed class BattleUnitOverlay
    {
        public string UnitId;
        public float ScreenX, ScreenY;
        /// <summary>false — точка поза кадром або за камерою: не малювати.</summary>
        public bool OnScreen;
        public bool IsCurrent, IsHovered;
        /// <summary>Під курсором і є валідною ціллю поточної дії.</summary>
        public bool IsTargetable;
    }

    /// <summary>Спливаючий напис над юнітом (у координатах GUI).</summary>
    public sealed class BattleFloatingText
    {
        public string Text;
        public BattleLogKind Kind;
        public float ScreenX, ScreenY;
        /// <summary>0..1 — згасання.</summary>
        public float Alpha;
        /// <summary>Крит, «Дозор!» тощо — більшим кеглем.</summary>
        public bool Big;
    }

    /// <summary>Банер ходу сторони (docs/COMBAT_V2.md §3). null у HUD-даних — банера нема.</summary>
    public sealed class BattleTurnBanner
    {
        public string Text;
        public bool PlayerSide;
        /// <summary>0..1 — поява/згасання.</summary>
        public float Alpha;
    }

    /// <summary>
    /// Що показати спливаючим написом для рядка логу (BattleLogText.Floating):
    /// над ким, який текст, якого сенсу. Презентер додає екранну позицію.
    /// </summary>
    public sealed class BattleFloatingSpec
    {
        public string UnitId;
        public string Text;
        public BattleLogKind Kind;
        public bool Big;
    }
}
