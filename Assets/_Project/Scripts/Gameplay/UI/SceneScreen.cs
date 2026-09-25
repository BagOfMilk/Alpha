using System.Collections.Generic;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Портретна сцена (§4.1 <c>AdvanceScene</c>): портрет(и) із
    /// <see cref="IPortraitProvider"/> або іменна заглушка, ім'я мовця й
    /// репліка з <see cref="UkrainianText"/>, «Далі» / Пробіл / клік.
    ///
    /// Тест-збірка (Поправка №7.8, п.1): коли поточний крок — Choice
    /// (<see cref="SceneStepView.IsChoice"/>), замість «Далі» малюються
    /// варіанти кнопками (<see cref="ScreenText.SceneOptionLine"/> — текст
    /// варіанту, а за наявності перевірки ще й скіл/поріг/виконавець/
    /// очікувана полоса заздалегідь, інваріант 8). Обраний варіант іде через
    /// <c>GameSession.ChooseSceneOption</c>, а до продовження сцени гравцю
    /// показується наслідок — нові рядки <c>DayLog</c>, які саме цей вибір
    /// щойно дописав (<see cref="_consequenceEvents"/>), а не автоматичний
    /// перехід одразу на наступний кадр.
    /// </summary>
    public sealed class SceneScreen
    {
        private SceneStepView _current;

        /// <summary>Наступний кадр сцени, отриманий від ChooseSceneOption — тримається, поки гравець не закриє панель наслідку.</summary>
        private SceneStepView _pendingAfterChoice;

        /// <summary>Нові події DayLog, які саме щойно зроблений вибір дописав (межа лічена ДО виклику ChooseSceneOption) — порожньо/null, коли наслідку нема що показати.</summary>
        private List<GameEvent> _consequenceEvents;

        /// <summary>Показуємо панель наслідку вибору на екрані замість кнопок варіантів чи «Далі».</summary>
        private bool _showingConsequence;

        /// <summary>
        /// Контекст останньої репліки (Поправка №7.8): Choice-крок сам не
        /// несе ані SpeakerId, ані LineKey (<c>ScenePlayback.Next</c> скидає
        /// їх на будь-якому кроці, включно з Choice) — без цього кешу питання,
        /// щойно сказане перед вибором ("Кажи прямо..."), зникало б з екрана
        /// саме тоді, коли гравцю треба на нього відповісти.
        /// </summary>
        private string _lastSpeakerId, _lastLineKey;

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

        // ===================== API тур-автоплею (Поправка №7.8, п.4) =====================
        //
        // AutoplayGameDriver раніше кликав GameSession.AdvanceScene()/
        // ChooseSceneOption() НАПРЯМУ, в обхід цього екрана — той самий
        // Session, який малює Draw() нижче, отримував ДРУГИЙ, незалежний від
        // UI прохід по сцені. Це не нешкідливо: SceneScreen має ВЛАСНИЙ курсор
        // (_current), що оновлюється лише зі свого Draw()/Advance() — прямий
        // виклик ядра з боку водія рухав _scenePlayback УПЕРЕД, а _current тут
        // лишався незмінним (перший кадр, отриманий Draw() при його першому
        // виклику), тож знімки тура фіксували б один і той самий застиглий
        // кадр, поки водій-звіт по кроках уже пішов далі. Водій відтепер
        // керує ЦИМ САМИМ курсором через методи нижче — так само, як людський
        // клік, лише без клавіатури/миші.

        /// <summary>Поточний кадр, який зараз показаний на екрані — те, що тур-автоплей знімає скріншотом.</summary>
        public SceneStepView Current => _current;

        /// <summary>Панель наслідку вибору зараз на екрані замість кнопок/«Далі».</summary>
        public bool IsShowingConsequence => _showingConsequence;

        /// <summary>Нові рядки DayLog, які щойно зроблений вибір дописав — те, що показує панель наслідку.</summary>
        public IReadOnlyList<GameEvent> ConsequenceEvents => _consequenceEvents;

        /// <summary>Той самий крок, що кнопка «Далі»/клік/Пробіл: просуває сцену на один крок, якщо екран не стоїть на виборі й не показує наслідок.</summary>
        public SceneStepView DriverAdvance(GameShell shell)
        {
            if (_current == null)
            {
                _lastSpeakerId = null;
                _lastLineKey = null;
                SetCurrent(shell.TryRun(() => shell.Session.AdvanceScene()));
                return _current;
            }
            if (_showingConsequence || _current.IsChoice) return _current; // тут крокує ChooseOption/ContinueAfterConsequence, не Advance
            Advance(shell);
            return _current;
        }

        /// <summary>Той самий клік по кнопці варіанту — лише коли екран справді стоїть на виборі.</summary>
        public SceneStepView DriverChoose(GameShell shell, int optionIndex)
        {
            if (_current == null || !_current.IsChoice || _showingConsequence) return _current;
            ChooseOption(shell, optionIndex);
            return _current;
        }

        /// <summary>Той самий клік «Далі» на панелі наслідку — закриває її й відкриває наступний кадр сцени.</summary>
        public SceneStepView DriverContinueConsequence()
        {
            if (_showingConsequence) ContinueAfterConsequence();
            return _current;
        }

        public void Draw(GameShell shell)
        {
            if (_current == null)
            {
                // Межа сцени: нова сцена не має пам'ятати останню репліку
                // ЧУЖОЇ, щойно закінченої сцени (§_lastSpeakerId нижче).
                _lastSpeakerId = null;
                _lastLineKey = null;
                SetCurrent(shell.TryRun(() => shell.Session.AdvanceScene()));
            }
            if (_current == null) return;

            var g = shell.ProtagonistGender;

            var area = new Rect(0f, 0f, Screen.width, Screen.height);
            GUILayout.BeginArea(area);
            GUILayout.FlexibleSpace();

            Widgets.Panel(null, () =>
            {
                // Контекст останньої репліки (§_lastSpeakerId) — ЛИШЕ на
                // Choice-кроці й на панелі наслідку: власні SpeakerId/LineKey
                // кадру там порожні (ScenePlayback скидає їх на будь-якому
                // кроці), і без відкату гравець бачив би порожню панель над
                // кнопками замість щойно сказаної фрази/питання. Для звичайних
                // Shot/Beat-кроків МІЖ репліками (портрет змінюється, тексту
                // немає) фолбек НЕ застосовуємо — це навмисна порожня панель
                // із самим "Далі", як і завжди.
                bool wantsContext = _current.IsChoice || _showingConsequence;
                string speakerId = !string.IsNullOrEmpty(_current.SpeakerId) ? _current.SpeakerId : (wantsContext ? _lastSpeakerId : null);
                string lineKey = !string.IsNullOrEmpty(_current.LineKey) ? _current.LineKey : (wantsContext ? _lastLineKey : null);
                if (!string.IsNullOrEmpty(speakerId))
                    GUILayout.Label(ScreenText.ResolveCompanionName(speakerId, g, shell.Session.GetRosterView()), AlphaSkin.SubHeader);
                if (!string.IsNullOrEmpty(lineKey))
                    GUILayout.Label(UkrainianText.Get(lineKey, g), AlphaSkin.Body);

                GUILayout.Space(8f);
                if (_showingConsequence)
                    DrawConsequence(shell, g);
                else if (_current.IsChoice)
                    DrawChoiceOptions(shell, g);
                else
                {
                    GUILayout.BeginHorizontal();
                    if (Widgets.PrimaryButton(UkrainianText.Get("ui.scene.next", g), GUILayout.Width(180f)))
                        Advance(shell);
                    Widgets.TooltipLine(UkrainianText.Get("ui.scene.hint", g));
                    GUILayout.EndHorizontal();
                }
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
            //
            // Поправка №7.8 (тест-збірка, п.1): на Choice-кроці й на панелі
            // наслідку вибір/продовження йде ЛИШЕ явною кнопкою — випадковий
            // клік по фону сцени більше не проковтує вибір гравця мовчки
            // (раніше й тут спрацював би загальний Advance(), який на
            // Choice-кроці — нешкідливий, але зайвий, но-оп; для консистент-
            // ності з панеллю наслідку — де мовчазний скіп справді був би
            // помилкою — вимикаємо обидва разом).
            var evt = Event.current;
            bool advanceRequested = evt != null && !_current.IsChoice && !_showingConsequence &&
                ((evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Space) ||
                 (evt.type == EventType.MouseDown && evt.button == 0));
            if (advanceRequested)
            {
                evt.Use();
                Advance(shell);
            }
        }

        /// <summary>Варіанти Choice-кроку кнопками (Поправка №7.8, п.1) — текст варіанту, а за наявності перевірки ще й скіл/поріг/виконавець/полоса заздалегідь.</summary>
        private void DrawChoiceOptions(GameShell shell, Game.Core.Characters.Creation.Gender g)
        {
            if (_current.Options == null) return;
            var roster = shell.Session.GetRosterView();
            for (int i = 0; i < _current.Options.Count; i++)
            {
                int index = i;
                string label = ScreenText.SceneOptionLine(_current.Options[i], g, roster);
                if (Widgets.PrimaryButton(label))
                    ChooseOption(shell, index);
            }
        }

        /// <summary>
        /// Розв'язує вибір і одразу лічить, які рядки DayLog саме цей вибір
        /// дописав (межа ДО виклику, а не порівняння знімків — той самий
        /// принцип, що вже коректно рахує вплив вибору в BotRunner.
        /// ChoiceDiagnostic), щоб показати їх панеллю наслідку, а не
        /// проковтнути мовчки, як і будь-яку іншу команду через TryRun.
        /// </summary>
        private void ChooseOption(GameShell shell, int optionIndex)
        {
            int before = shell.Session.DayLog.Count;
            var next = shell.TryRun(() => shell.Session.ChooseSceneOption(optionIndex));
            var log = shell.Session.DayLog;
            var consequence = new List<GameEvent>();
            if (log != null)
                for (int i = before; i < log.Count; i++)
                    consequence.Add(log[i]);

            _consequenceEvents = consequence;
            _pendingAfterChoice = next;
            _showingConsequence = true;
        }

        /// <summary>Панель наслідку (Поправка №7.8, п.1): рядки DayLog, які цей вибір щойно дописав — «Далі» лише тепер відкриває наступний кадр сцени.</summary>
        private void DrawConsequence(GameShell shell, Game.Core.Characters.Creation.Gender g)
        {
            GUILayout.Label(UkrainianText.Get("ui.scene.consequence.title", g), AlphaSkin.SubHeader);
            if (_consequenceEvents == null || _consequenceEvents.Count == 0)
                GUILayout.Label(UkrainianText.Get("ui.common.empty", g), AlphaSkin.Tooltip);
            else
            {
                var roster = shell.Session.GetRosterView();
                foreach (var evt in _consequenceEvents)
                {
                    string line = ScreenText.EventLine(evt, g, roster);
                    if (!string.IsNullOrEmpty(line))
                        GUILayout.Label("• " + line, AlphaSkin.Body);
                }
            }

            GUILayout.Space(8f);
            if (Widgets.PrimaryButton(UkrainianText.Get("ui.scene.next", g), GUILayout.Width(180f)))
                ContinueAfterConsequence();
        }

        /// <summary>Спільний хвіст «Далі» на панелі наслідку — та сама дія, яку тур-автоплей викликає через <see cref="DriverContinueConsequence"/>.</summary>
        private void ContinueAfterConsequence()
        {
            _showingConsequence = false;
            _consequenceEvents = null;
            var next = _pendingAfterChoice;
            _pendingAfterChoice = null;
            SetCurrent(next != null && !next.IsFinished ? next : null);
        }

        /// <summary>Оновлює поточний кадр і, за наявності, пам'ятає останню непорожню репліку (§_lastSpeakerId) для показу над наступним Choice-кроком.</summary>
        private void SetCurrent(SceneStepView view)
        {
            _current = view;
            if (view == null) return;
            if (!string.IsNullOrEmpty(view.SpeakerId)) _lastSpeakerId = view.SpeakerId;
            if (!string.IsNullOrEmpty(view.LineKey)) _lastLineKey = view.LineKey;
        }

        private void Advance(GameShell shell)
        {
            var next = shell.TryRun(() => shell.Session.AdvanceScene());
            SetCurrent(next != null && !next.IsFinished ? next : null);
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
