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

            // Фікс-ревью (блокер, знайдено тур-автоплеєм): рект діалогової
            // панелі ЩОЙНО повністю розкладений (GetLastRect всередині того ж
            // BeginArea, ДО EndArea — координати екрана, бо area починається з
            // (0,0)). Під час події Layout цей рект ще нульовий — портрети
            // нижче тоді просто не малюються цього проходу, і це нешкідливо:
            // Layout нічого не рендерить на екран, лише рахує розміри.
            var dialogueRect = GUILayoutUtility.GetLastRect();

            GUILayout.Space(24f);
            GUILayout.EndArea();

            DrawPortraits(shell, _current, g, dialogueRect);

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
        /// Фікс-ревью (блокер, знайдено тур-автоплеєм): раніше портрет(и)
        /// малювались фіксованим великим розміром (до 360px) по центру
        /// ЕКРАНА, над діалоговою панеллю, — квадрат аж на чверть екрана,
        /// що ніяк не пов'язаний позицією з панеллю знизу. Тепер портрет —
        /// невеликий «бюст» (VN-стиль), прикріплений до ВЕРХНЬОГО краю
        /// діалогової панелі (<paramref name="dialogueRect"/>, щойно знятий
        /// <c>GUILayoutUtility.GetLastRect()</c> у <see cref="Draw"/>), трохи
        /// заходить на нього — читається як «портрет належить цій панелі»,
        /// а не як окремий, нічим не пояснений об'єкт посеред сцени.
        /// </summary>
        private static void DrawPortraits(GameShell shell, SceneStepView current, Game.Core.Characters.Creation.Gender g, Rect dialogueRect)
        {
            // Подія Layout — рект ще Rect.zero (GetLastRect до першого Repaint):
            // нічого не малюємо цього проходу, Layout однаково не рендерить пікселі.
            if (dialogueRect.width <= 0f || dialogueRect.height <= 0f) return;

            bool hasSecond = !string.IsNullOrEmpty(current.SecondActorId);
            float portraitSize = Mathf01Clamp(Screen.height * 0.22f, 120f, 200f);
            float overlap = portraitSize * 0.12f;
            float top = dialogueRect.y - portraitSize + overlap;
            if (top < 8f) top = 8f;

            if (hasSecond)
            {
                DrawPortrait(shell, current.ActorId, g, new Rect(dialogueRect.x + 16f, top, portraitSize, portraitSize));
                DrawPortrait(shell, current.SecondActorId, g, new Rect(dialogueRect.x + dialogueRect.width - portraitSize - 16f, top, portraitSize, portraitSize));
            }
            else
            {
                DrawPortrait(shell, current.ActorId, g, new Rect(dialogueRect.x + 16f, top, portraitSize, portraitSize));
            }
        }

        /// <summary>
        /// Живий рендер (E2's <see cref="IPortraitProvider"/>) — абсолютний Rect
        /// через <see cref="GUI.DrawTexture"/>, а не GUILayout: стаб лінту не
        /// знає перевантаження <c>GUILayout.Box(Texture,...)</c>, і додавати
        /// його заради одного кадру дорожче, ніж малювати руками. Підпис імені —
        /// НАД портретом (не під ним, як раніше): знизу тепер одразу починається
        /// діалогова панель, і підпис під портретом ліг би просто на неї.
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

            var nameRect = new Rect(rect.x, rect.y - 24f, rect.width, 22f);
            if (nameRect.y < 0f) nameRect.y = 0f;
            GUI.Label(nameRect, ScreenText.ResolveCompanionName(characterId, g, shell.Session.GetRosterView()), AlphaSkin.Body);
        }

        private static float Mathf01Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
