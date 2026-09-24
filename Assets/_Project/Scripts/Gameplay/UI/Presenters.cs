using Game.Core.Session;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>Шов между оболочкой экранов (E1b) и 3D-боем (E2): оболочка находит реализацию во время выполнения.</summary>
    public interface IBattlePresenter
    {
        void Enter(GameSession session);
        void Exit();
        bool IsActive { get; }
        void DrawHud(GameSession session);

        /// <summary>
        /// Фаза F (UI-tour autoplay): чи показано модалку результату бою, яку
        /// ще не підтверджено (той самий прапорець, що читає
        /// <see cref="IBattleHudData.ResultPending"/> у BattleHudScreen.cs).
        /// Обидва члени вже публічно реалізовані <c>BattleArenaController</c>
        /// (E2) — інтерфейс лише називає їх, жодної нової логіки презентеру
        /// не потрібно.
        /// </summary>
        bool ResultPending { get; }

        /// <summary>Той самий виклик, що й кнопка "Далі" на панелі результату — автопрогону потрібен без кліку по HUD.</summary>
        void AcknowledgeResult();
    }

    /// <summary>Шов для портретов: живой рендер модели персонажа (E2) или именная заглушка.</summary>
    public interface IPortraitProvider
    {
        Texture2D GetPortrait(string characterId);
    }
}
