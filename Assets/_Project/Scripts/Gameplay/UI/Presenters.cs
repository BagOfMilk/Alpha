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
    }

    /// <summary>Шов для портретов: живой рендер модели персонажа (E2) или именная заглушка.</summary>
    public interface IPortraitProvider
    {
        Texture2D GetPortrait(string characterId);
    }
}
