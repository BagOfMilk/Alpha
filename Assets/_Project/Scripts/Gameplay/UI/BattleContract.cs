using System.Collections.Generic;
using Game.Core.Session.Views;
using UnityEngine;

// Чисті типи контракту (ArmedAction, BattleLogKind, рядки логу, оверлеї,
// написи, банер) — у BattleUiTypes.cs: їх бачать headless-тести.

namespace Game.Gameplay
{
    // =====================================================================
    // Бій v2 — контракт між 3D-подачею (BattleArenaController), HUD
    // (BattleHudScreen) і автотуром (AutoplayGameDriver). Специфікація —
    // docs/COMBAT_V2.md §7.4. Файл ЗАМОРОЖЕНИЙ: додавати члени можна лише
    // узгоджено; перейменовувати й видаляти — ні (три частини пишуться
    // паралельно й зводяться разом).
    //
    // Лежить у Gameplay/UI, а не поруч із контролером: контролер
    // лінт-виключений (Physics/Renderer/Camera), HUD і автотур — ні, тож
    // лінт мусить бачити ці типи, не бачачи самого класу-реалізації.
    // =====================================================================

    /// <summary>
    /// Команди бою — ТІ САМІ шляхи, що миша й клавіші. Автотур ходить у бій
    /// лише через цей інтерфейс (урок «Почати день» і завислого ходу ворога:
    /// водій, що кличе ядро напряму, маскує діри інтерфейсу).
    /// </summary>
    public interface IBattleInput
    {
        /// <summary>Поточний юніт — гравця, і гравець може діяти.</summary>
        bool IsPlayerTurn { get; }

        /// <summary>Відтворюються такти (анімації) або ходить ШІ — ввід гравця заблоковано.</summary>
        bool IsBusy { get; }

        ArmedAction Armed { get; }
        string ArmedAbilityId { get; }

        /// <summary>«Прискорити» хід ворога: коротші паузи й такти (не автобій).</summary>
        bool FastEnemyTurns { get; set; }

        /// <summary>
        /// Презентер сам веде хід ШІ (за замовчуванням true). Вимикає лише
        /// сценарний журнальний тур, що грає обидві сторони наївно; звичайний
        /// і бойовий тури не вимикають ніколи (охоронець AutoplayDriverGuardTests).
        /// </summary>
        bool EnemyAiEnabled { get; set; }

        /// <summary>Текст останньої відмови (червоним у HUD); порожньо, якщо дія пройшла.</summary>
        string LastRejectionText { get; }

        /// <summary>
        /// Меню паузи (Esc) відкрите: бій стоїть — ні кліків по арені, ні
        /// клавіш, ні ходу ШІ, ні тактів. Виставляє GameShell щокадру (рев'ю
        /// Бою v2: раніше за «паузою» бій ішов далі, а клік по кнопці меню
        /// долітав до арени).
        /// </summary>
        bool Paused { get; set; }

        /// <summary>
        /// Двофазна здібність («Наказ пересунутися»: союзник, потім клітинка):
        /// id уже обраного юніта; null — юніта ще не обрано.
        /// </summary>
        string ArmedTargetUnitId { get; }

        /// <summary>ЛКМ по тайлу: рух / приціл дозору / здібність по тайлу. true — дію виконано.</summary>
        bool ClickTile(int x, int y);

        /// <summary>ЛКМ по юніту: атака зброєю / озброєна здібність. true — дію виконано.</summary>
        bool ClickUnit(string unitId);

        void ArmAbility(string abilityId);
        void ArmOverwatchAim();
        void CancelArmed();
        bool RequestStabilize(string targetId);
        void RequestEndTurn();
        void RequestAutoResolve();

        /// <summary>Перелетіти камерою до юніта.</summary>
        void FocusCamera(string unitId);

        /// <summary>Наведення «як мишею» для автотуру і знімків; справжній рух миші скидає його.</summary>
        void SimulateHoverUnit(string unitId);
        void SimulateHoverTile(int x, int y);
        void ClearSimulatedHover();
    }

    /// <summary>
    /// Дані для малювання HUD (BattleHudScreen) — усе, що HUD показує, береться
    /// звідси; HUD сам ніколи не кличе GameSession.
    /// </summary>
    public interface IBattleHudData : IBattleInput
    {
        BattleView View { get; }

        bool ResultPending { get; }
        string ResultOutcomeKey { get; }
        string ResultRounds { get; }
        IReadOnlyList<string> ResultCasualtyLines { get; }

        /// <summary>Журнал бою, найстаріший першим, готові рядки з типом.</summary>
        IReadOnlyList<BattleLogEntryUi> LogEntries { get; }

        string HoveredUnitId { get; }
        bool HasHoveredTile { get; }
        int HoveredTileX { get; }
        int HoveredTileY { get; }

        /// <summary>
        /// Екранна точка центру наведеного тайла (координати GUI, y згори) —
        /// HUD ставить підказку «Рух: −N ОД» поруч із клітинкою, а не в куті.
        /// Має сенс лише коли <see cref="HasHoveredTile"/>. (Бій v2, раунд 2.)
        /// </summary>
        float HoveredTileScreenX { get; }
        float HoveredTileScreenY { get; }

        /// <summary>Прев'ю атаки поточного юніта по наведеній цілі (з урахуванням озброєної здібності); null — не ціль.</summary>
        AttackPreviewView HoverAttack { get; }

        /// <summary>Прев'ю руху до наведеного тайла; null — тайла немає або зараз не хід гравця.</summary>
        MovePathView HoverPath { get; }

        /// <summary>Точки над головами юнітів (координати GUI) — HUD малює там ім'я, смужку HP, стани.</summary>
        IReadOnlyList<BattleUnitOverlay> Overlays { get; }
        IReadOnlyList<BattleFloatingText> FloatingTexts { get; }
        BattleTurnBanner Banner { get; }

        /// <summary>Ім'я юніта з порядковим номером («Розвідник орди II»).</summary>
        string ResolveDisplayName(BattleUnitView unit);

        /// <summary>HUD щокадру повідомляє, де його панелі (координати GUI): клік крізь них не йде в арену, камера кадрує вільну область.</summary>
        void SetHudRects(IReadOnlyList<Rect> guiRects);

        void AcknowledgeResult();
    }
}
