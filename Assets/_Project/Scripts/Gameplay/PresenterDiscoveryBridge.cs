using Game.Gameplay.UI;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Пошук реалізацій швів E1b/E2 (<see cref="IBattlePresenter"/>/
    /// <see cref="IPortraitProvider"/>) у сцені під час виконання — рівно
    /// той приклад, що описує ownership-контракт E1b: "FindObjectsByType
    /// &lt;MonoBehaviour&gt;(FindObjectsInactive.Include, FindObjectsSortMode.None)
    /// + 'is IBattlePresenter'". <see cref="Object.FindObjectsByType{T}"/> —
    /// Unity 6-API, якого немає в заглушці <c>Game.Gameplay.Lint</c>
    /// (навмисно вузька поверхня стаба, див. коментар у ньому) — тому цей
    /// файл виключений із лінту (той самий приём, що вже виключив
    /// VillageStage.cs/VillageStageBridge.cs), а <see cref="GameShell"/>
    /// (лінтується) кличе його через рефлексію.
    /// </summary>
    public static class PresenterDiscoveryBridge
    {
        public static object FindBattlePresenter()
        {
            var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
                if (all[i] is IBattlePresenter presenter) return presenter;
            return null;
        }

        public static object FindPortraitProvider()
        {
            var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
                if (all[i] is IPortraitProvider provider) return provider;
            return null;
        }
    }
}
