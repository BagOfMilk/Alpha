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

        /// <summary>
        /// Кеш карткового фону за id (§DrawNameCardFallback) — інакше кожен
        /// прохід OnGUI (Layout/Repaint щокадру, поки живий портрет ще не
        /// зрендерився — <c>PortraitRig.GetPortrait</c> віддає null перший
        /// кадр запиту) створював би нову <c>Texture2D</c> тим самим кольором.
        /// Той самий принцип, що <c>_tileBlocks</c>/<c>_unitBlocks</c> у
        /// <see cref="BattleArenaController"/>.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> _cardTextureCache =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

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
            // Полірування (ціль B «Портрети», owner: "a large portrait panel
            // (~300x380 at 1600x900) per speaker"): попередній «VN-бюст»
            // 120–200px квадратом читався як маленька іконка, не як портрет
            // мовця. Частки екрана (не фіксовані пікселі) — той самий портрет
            // лишається пропорційним на інших роздільностях, а на цільових
            // 1600×900 дає рівно ~300×380.
            float portraitWidth = Mathf01Clamp(Screen.width * 0.1875f, 180f, 340f);
            float portraitHeight = Mathf01Clamp(Screen.height * 0.4222f, 220f, 420f);
            float overlap = portraitHeight * 0.12f;
            float top = dialogueRect.y - portraitHeight + overlap;
            if (top < 8f) top = 8f;

            if (hasSecond)
            {
                DrawPortrait(shell, current.ActorId, g, new Rect(dialogueRect.x + 16f, top, portraitWidth, portraitHeight));
                DrawPortrait(shell, current.SecondActorId, g, new Rect(dialogueRect.x + dialogueRect.width - portraitWidth - 16f, top, portraitWidth, portraitHeight));
            }
            else
            {
                DrawPortrait(shell, current.ActorId, g, new Rect(dialogueRect.x + 16f, top, portraitWidth, portraitHeight));
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
            string name = ScreenText.ResolveCompanionName(characterId, g, shell.Session.GetRosterView());
            Texture2D portrait = shell.PortraitProvider?.GetPortrait(characterId);
            if (portrait != null)
                GUI.DrawTexture(rect, portrait, ScaleMode.ScaleToFit);
            else
                DrawNameCardFallback(rect, characterId, name);

            var nameRect = new Rect(rect.x, rect.y - 24f, rect.width, 22f);
            if (nameRect.y < 0f) nameRect.y = 0f;
            GUI.Label(nameRect, name, AlphaSkin.Body);
        }

        /// <summary>
        /// Полірування (ціль B «Портрети», owner: "if unavailable a styled
        /// name card (initials, colour by id, name) — never a bare «?»"):
        /// раніше тут стояв <c>GUI.Box(rect, "?")</c> — той самий бляклий
        /// прямокутник ДЛЯ БУДЬ-КОГО, поки живий рендер/PNG не готовий (перший
        /// кадр після запиту, §PortraitRig.GetPortrait — черга на LateUpdate).
        /// Тепер — карткою: та сама детермінована палітра, що в
        /// <see cref="PortraitRig.TintModel"/> (кольором за id, а не одним
        /// сірим для всіх), великі ініціали й повне ім'я — читається як
        /// свідомо намальована заглушка персонажа, не як «зламане місце».
        /// </summary>
        private static void DrawNameCardFallback(Rect rect, string characterId, string name)
        {
            if (!_cardTextureCache.TryGetValue(characterId ?? string.Empty, out var cardTexture) || cardTexture == null)
            {
                var palette = BattleArenaView.CharacterTint("u_" + characterId, "Player", characterId);
                var cardColor = new Color32(
                    (byte)(palette.R * 255f), (byte)(palette.G * 255f), (byte)(palette.B * 255f), 255);
                cardTexture = AlphaSkin.SolidTexture(cardColor);
                _cardTextureCache[characterId ?? string.Empty] = cardTexture;
            }

            // Стаб лінту (Game.Gameplay.Lint/UnityEngineStub.cs) свідомо
            // покриває лише мінімум IMGUI: GUI.DrawTexture тут вимагає
            // scaleMode явно (не переобтяжує заглушку зайвим перевантаженням),
            // а TextAnchor стубу має лише UpperLeft/MiddleCenter — тих самих
            // числових значень, що й справжній UnityEngine.TextAnchor, тож
            // MiddleCenter у власному вузькому Rect нижче й так читається як
            // «по центру нижньої смуги», без LowerCenter.
            GUI.DrawTexture(rect, cardTexture, ScaleMode.StretchToFill);

            float minSide = rect.width < rect.height ? rect.width : rect.height;
            var initialsStyle = new GUIStyle(AlphaSkin.Header)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = (int)(minSide * 0.4f)
            };
            initialsStyle.normal.textColor = new Color(0.08f, 0.07f, 0.06f, 0.85f); // темний — картка сама світла/насичена
            GUI.Label(rect, Initials(name), initialsStyle);

            var nameStyle = new GUIStyle(AlphaSkin.Body) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            nameStyle.normal.textColor = new Color(0.08f, 0.07f, 0.06f, 0.9f);
            var nameBand = new Rect(rect.x + 4f, rect.y + rect.height - 34f, rect.width - 8f, 30f);
            GUI.Label(nameBand, name, nameStyle);
        }

        /// <summary>Перші літери першого й (за наявності) другого слова — «Тугар Вовк» → «ТВ», «Провідниця» → «ПР».</summary>
        private static string Initials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            var words = name.Split(' ');
            if (words.Length >= 2 && words[0].Length > 0 && words[1].Length > 0)
                return char.ToUpperInvariant(words[0][0]).ToString() + char.ToUpperInvariant(words[1][0]);
            return words[0].Length >= 2
                ? words[0].Substring(0, 2).ToUpperInvariant()
                : words[0].Substring(0, 1).ToUpperInvariant();
        }

        private static float Mathf01Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
