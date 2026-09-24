using Game.Core.Session.Views;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Портретна сцена (§4.1 <c>AdvanceScene</c>): портрет(и) із
    /// <see cref="IPortraitProvider"/> або іменна заглушка, ім'я мовця й
    /// репліка з <see cref="UkrainianText"/>, «Далі» / Пробіл / клік.
    /// </summary>
    public sealed class SceneScreen
    {
        private SceneStepView _current;

        public void Draw(GameShell shell)
        {
            if (_current == null)
                _current = shell.TryRun(() => shell.Session.AdvanceScene());
            if (_current == null) return;

            var g = shell.ProtagonistGender;

            bool hasSecond = !string.IsNullOrEmpty(_current.SecondActorId);
            float portraitSize = Mathf01Clamp(Screen.height * 0.4f, 180f, 360f);
            float portraitTop = Screen.height * 0.12f;
            if (hasSecond)
            {
                DrawPortrait(shell, _current.ActorId, g, new Rect(Screen.width * 0.5f - portraitSize - 20f, portraitTop, portraitSize, portraitSize));
                DrawPortrait(shell, _current.SecondActorId, g, new Rect(Screen.width * 0.5f + 20f, portraitTop, portraitSize, portraitSize));
            }
            else
            {
                DrawPortrait(shell, _current.ActorId, g, new Rect(Screen.width * 0.5f - portraitSize * 0.5f, portraitTop, portraitSize, portraitSize));
            }

            var area = new Rect(0f, 0f, Screen.width, Screen.height);
            GUILayout.BeginArea(area);
            GUILayout.FlexibleSpace();

            Widgets.Panel(null, () =>
            {
                if (!string.IsNullOrEmpty(_current.SpeakerId))
                    GUILayout.Label(ScreenText.ResolveCompanionName(_current.SpeakerId, g, shell.Session.GetRosterView()), AlphaSkin.SubHeader);
                if (!string.IsNullOrEmpty(_current.LineKey))
                    GUILayout.Label(UkrainianText.Get(_current.LineKey, g), AlphaSkin.Body);

                GUILayout.Space(8f);
                GUILayout.BeginHorizontal();
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.scene.next", g), GUILayout.Width(180f)))
                    Advance(shell);
                Widgets.TooltipLine(UkrainianText.Get("ui.scene.hint", g));
                GUILayout.EndHorizontal();
            }, GUILayout.Width(Screen.width * 0.8f));

            GUILayout.Space(24f);
            GUILayout.EndArea();

            // Event-based, не сирий Input.GetKeyDown/GetMouseButtonDown
            // (фікс-ревью, major): OnGUI викликається кілька разів за кадр
            // (Layout, сама подія, Repaint, ...), і обидва прапорці лишаються
            // true в УСІХ цих проходах того самого кадру, тоді як
            // Event.current.type відповідає рівно одному фізичному
            // натисканню — той самий прийом, що вже коректно працює через
            // Update() у прекурсорі ScenePlayer.cs, тут — через сам OnGUI.
            var evt = Event.current;
            bool advanceRequested = evt != null &&
                ((evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Space) ||
                 (evt.type == EventType.MouseDown && evt.button == 0));
            if (advanceRequested)
            {
                evt.Use();
                Advance(shell);
            }
        }

        private void Advance(GameShell shell)
        {
            var next = shell.TryRun(() => shell.Session.AdvanceScene());
            _current = (next != null && !next.IsFinished) ? next : null;
        }

        /// <summary>
        /// Живий рендер (E2's <see cref="IPortraitProvider"/>) — абсолютний Rect
        /// через <see cref="GUI.DrawTexture"/>, а не GUILayout: стаб лінту не
        /// знає перевантаження <c>GUILayout.Box(Texture,...)</c>, і додавати
        /// його заради одного кадру дорожче, ніж малювати руками.
        /// </summary>
        private static void DrawPortrait(GameShell shell, string characterId, Game.Core.Characters.Creation.Gender g, Rect rect)
        {
            Texture2D portrait = shell.PortraitProvider?.GetPortrait(characterId);
            if (portrait != null)
            {
                GUI.DrawTexture(rect, portrait, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.Box(rect, UkrainianText.Get("ui.scene.portrait.placeholder", g));
            }

            var nameRect = new Rect(rect.x, rect.y + rect.height + 4f, rect.width, 32f);
            GUI.Label(nameRect, ScreenText.ResolveCompanionName(characterId, g, shell.Session.GetRosterView()), AlphaSkin.Body);
        }

        private static float Mathf01Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
