using Game.Core.Session;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>Шов між оболонкою екранів (E1b) і 3D-боєм (E2): оболонка знаходить реалізацію під час виконання.</summary>
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

    /// <summary>Шов для портретів: живий рендер моделі персонажа (E2) або іменна заглушка.</summary>
    public interface IPortraitProvider
    {
        Texture2D GetPortrait(string characterId);
    }
}
