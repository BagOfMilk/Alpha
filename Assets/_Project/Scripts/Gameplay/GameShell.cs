using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Місце, зарезервоване для фасаду <c>Game.Core.Session.GameSession</c>,
    /// який зараз пишеться паралельно в трунку (§4.1 TEST_BUILD.md). Поки
    /// його немає в цьому воркчасті — навмисно жодної згадки цього типу тут.
    ///
    /// Сьогодні компонент лише перевіряє, що шкурка (<see cref="AlphaSkin"/>)
    /// і хелпери (<see cref="Game.Gameplay.UI.Widgets"/>) працюють у зібраній
    /// сцені «Гра»: панель і заглушка кнопки «Нова гра». Наступні пакети E1
    /// (титул/створення/хаб-екрани) заповнять цей компонент справжніми
    /// екранами й підключать сюди фасад, коли він приїде.
    /// </summary>
    public sealed class GameShell : MonoBehaviour
    {
        private void OnGUI()
        {
            GUI.skin = Game.Gameplay.UI.AlphaSkin.Build();

            var area = new Rect(24f, 24f, 460f, 200f);
            GUILayout.BeginArea(area);
            Game.Gameplay.UI.Widgets.Panel("Alpha — перевірка шкурки", DrawPlaceholder);
            GUILayout.EndArea();
        }

        private void DrawPlaceholder()
        {
            GUILayout.Label("Шкурка згенерована в рантаймі, без завантажень.");
            Game.Gameplay.UI.Widgets.TooltipLine("Фасад ще не підключено — заглушка пакету E1.");

            if (Game.Gameplay.UI.Widgets.PrimaryButton("Нова гра"))
                Debug.Log("[GameShell] «Нова гра»: заглушка, GameSession ще не приїхав із трунку.");
        }
    }
}
