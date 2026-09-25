using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Scenes;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Інтерпретатор портретної сцени (Поправка №5.8): малює те, що порахував
    /// <see cref="ScenePlayback"/>. Сам нічого не вирішує — це принципово:
    /// «хто в кадрі і що говорить» перевірено тестами ядра без редактора, а
    /// тут лише показ.
    ///
    /// Портрети беруться за КЛЮЧЕМ: Resources/Portraits/&lt;id персонажа&gt;.
    /// Нема файлу — малюється іменна заглушка, і сцена все одно грається. Це
    /// не латка: так художник кладе картинку пізніше, нічого не займаючи в коді
    /// (той самий принцип, що й у реплік за ключами).
    ///
    /// IMGUI, а не UI Toolkit: постановку треба побачити сьогодні, а не після
    /// верстки. Коли з'явиться справжній екран, дані лишаться ті самі.
    /// </summary>
    public sealed class ScenePlayer : MonoBehaviour
    {
        [Tooltip("Сколько держать шаг без явной паузы, секунд.")]
        public float stepSeconds = 2.2f;

        [Tooltip("Играть автоматически. Выключено — шаг по клику или пробелу.")]
        public bool autoAdvance = true;

        [Tooltip("Рід протагоніста — обирає варіант .m/.f у UkrainianText, коли репліка розщеплена (E3b/R7).")]
        public Gender protagonistGender = Gender.Male;

        private ScenePlayback _play;
        private List<CharacterCard> _cast;
        private float _hold;
        private readonly Dictionary<string, Texture2D> _portraits = new Dictionary<string, Texture2D>();

        private void Start()
        {
            _cast = OpeningCast.All();
            _cast.Add(OpeningScenes.Protagonist());
            _play = new ScenePlayback(OpeningScenes.NeighbourWithADemand());
            Step();
        }

        private void Update()
        {
            if (_play == null || _play.IsFinished) return;

            bool asked = Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0);
            _hold -= Time.deltaTime;

            if (asked || (autoAdvance && _hold <= 0f)) Step();
        }

        private void Step()
        {
            if (!_play.Next())
            {
                Debug.Log("[Сцена] закончилась переходом: " + _play.TransitionKey);
                return;
            }
            _hold = _play.HoldSeconds > 0 ? (float)_play.HoldSeconds : stepSeconds;

            var effect = _play.Current.EffectKey;
            if (!string.IsNullOrEmpty(effect)) Debug.Log("[Эффект] " + effect);
        }

        private void OnGUI()
        {
            if (_play == null) return;

            var frame = _play.Current;
            float w = Screen.width, h = Screen.height;

            GUI.Box(new Rect(0, 0, w, h), GUIContent.none);

            if (frame.Framing == ShotFraming.Empty)
            {
                Centered(new Rect(0, h * 0.35f, w, 40), UkrainianText.Get("scene.player.empty_framing", protagonistGender));
            }
            else if (frame.Framing == ShotFraming.Two)
            {
                Portrait(new Rect(w * 0.08f, h * 0.12f, w * 0.36f, h * 0.55f), frame.ActorId);
                Portrait(new Rect(w * 0.56f, h * 0.12f, w * 0.36f, h * 0.55f), frame.SecondActorId);
            }
            else
            {
                Portrait(new Rect(w * 0.30f, h * 0.10f, w * 0.40f, h * 0.60f), frame.ActorId);
            }

            // Репліка внизу — там, де її шукають очима.
            var line = new Rect(w * 0.08f, h * 0.74f, w * 0.84f, h * 0.20f);
            GUI.Box(line, GUIContent.none);
            if (!string.IsNullOrEmpty(frame.LineKey))
            {
                GUI.Label(new Rect(line.x + 16, line.y + 10, line.width - 32, 26), NameOf(frame.SpeakerId));
                GUI.Label(new Rect(line.x + 16, line.y + 40, line.width - 32, line.height - 50),
                    UkrainianText.Get(frame.LineKey, protagonistGender));
            }
            else if (_play.IsFinished)
            {
                GUI.Label(new Rect(line.x + 16, line.y + 20, line.width - 32, 40),
                    UkrainianText.Get("scene.player.finished", protagonistGender));
            }

            GUI.Label(new Rect(12, h - 24, w - 24, 20), UkrainianText.Get("scene.player.hint", protagonistGender));
        }

        /// <summary>Портрет за ключем; нема файлу — іменна заглушка, сцена все одно йде.</summary>
        private void Portrait(Rect rect, string actorId)
        {
            if (string.IsNullOrEmpty(actorId)) return;

            var texture = Load(actorId);
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
            else
            {
                GUI.Box(rect, GUIContent.none);
                Centered(new Rect(rect.x, rect.y + rect.height * 0.45f, rect.width, 30), NameOf(actorId));
                Centered(new Rect(rect.x, rect.y + rect.height * 0.45f + 26, rect.width, 22),
                    UkrainianText.Get("scene.player.no_portrait", protagonistGender));
            }
        }

        private Texture2D Load(string actorId)
        {
            Texture2D texture;
            if (_portraits.TryGetValue(actorId, out texture)) return texture;

            texture = Resources.Load<Texture2D>("Portraits/" + actorId);
            _portraits[actorId] = texture;
            return texture;
        }

        /// <summary>
        /// Ім'я за ключем "char.&lt;id&gt;" (R7: Core-контентний
        /// <see cref="CharacterCard.DisplayName"/> гравець не бачить) — карта
        /// <see cref="_cast"/> лишається лише щоб перевірити, що актор узагалі
        /// в касті (сирий id як фолбек, якщо ключа в таблиці ще нема).
        /// </summary>
        private string NameOf(string actorId)
        {
            if (string.IsNullOrEmpty(actorId)) return "";
            bool known = false;
            for (int i = 0; i < _cast.Count; i++)
                if (_cast[i].Id == actorId) { known = true; break; }
            if (!known) return actorId;

            string key = "char." + actorId;
            return UkrainianText.Has(key, protagonistGender) ? UkrainianText.Get(key, protagonistGender) : actorId;
        }

        private static void Centered(Rect rect, string text)
        {
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(rect, text, style);
        }
    }
}
