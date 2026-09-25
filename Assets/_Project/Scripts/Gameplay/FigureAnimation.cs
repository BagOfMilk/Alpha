using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Game.Gameplay
{
    /// <summary>
    /// Анімація фігурки Kenney Mini Characters без контролера аніматора:
    /// граф плейблів змішує три кліпи з того самого FBX — стоїть / іде / біжить,
    /// плюс ОДИН одноразовий шар (Бій v2, docs/COMBAT_V2.md §3, §6: замах,
    /// удар здібності, смерть) поверх нього.
    ///
    /// Два рівні мікшера: <c>_gaitMixer</c> (3 входи idle/walk/sprint, той самий
    /// принцип, що й раніше — плавно змінює ваги за <see cref="Gait"/>) іде
    /// входом 0 у <c>_topMixer</c> (2 входи); вхід 1 — одноразовий кліп
    /// (<see cref="PlayOnce"/>), що на час своєї гри повністю перекриває базу
    /// (вага топ-мікшера 1/0), а по завершенні або тримає останній кадр
    /// (смерть — <paramref name="holdLastFrame"/>=true), або віддає керування
    /// назад ходьбі/бігу.
    ///
    /// Кліпи набору імпортовані без петлі (loopTime), тож петлю крутимо самі —
    /// правка імпорту зачепила б увесь набір.
    ///
    /// Не лінтується (Game.Gameplay.Lint): плейбли й аніматор — глибокий API
    /// рушія, заглушкою його чесно не покрити; перевіряє сам Unity при збірці.
    /// </summary>
    public sealed class FigureAnimation : MonoBehaviour
    {
        public AnimationClip idle;
        public AnimationClip walk;
        public AnimationClip sprint;

        /// <summary>Зсув фази в секундах, щоб жителі не дихали в унісон (детермінований, задає збирач сцени).</summary>
        public float phase;

        /// <summary>0 — стоїть, 1 — іде, 2 — біжить; проміжні значення змішують сусідні кліпи. Ігнорується, поки грає одноразовий кліп (<see cref="IsPlayingOneShot"/>).</summary>
        public float Gait { get; set; }

        /// <summary>Грає зараз одноразовий такт (замах/здібність/смерть) — <see cref="Gait"/> тимчасово не впливає на позу.</summary>
        public bool IsPlayingOneShot => _oneShotActive;

        private PlayableGraph _graph;
        private AnimationMixerPlayable _gaitMixer;
        private AnimationMixerPlayable _topMixer;
        private AnimationClipPlayable[] _gaitClips;
        private readonly float[] _gaitWeights = new float[3];
        private bool _ready;

        private AnimationClipPlayable _oneShotPlayable;
        private bool _oneShotConnected;
        private bool _oneShotActive;
        private bool _oneShotHold;
        private float _oneShotLength;
        private float _oneShotElapsed;
        private System.Action _oneShotCallback;

        private void OnEnable()
        {
            _ready = false;
            if (idle == null) return;
            var animator = GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogWarning("[Анімація] немає Animator на " + name + " — фігурка стоїть без руху.");
                return;
            }
            animator.applyRootMotion = false;

            _graph = PlayableGraph.Create(name + ".figure");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "figure", animator);

            _gaitMixer = AnimationMixerPlayable.Create(_graph, 3);
            var source = new[] { idle, walk != null ? walk : idle, sprint != null ? sprint : (walk != null ? walk : idle) };
            _gaitClips = new AnimationClipPlayable[3];
            for (int i = 0; i < 3; i++)
            {
                _gaitClips[i] = AnimationClipPlayable.Create(_graph, source[i]);
                _gaitClips[i].SetTime(phase);
                _graph.Connect(_gaitClips[i], 0, _gaitMixer, i);
                _gaitWeights[i] = i == 0 ? 1f : 0f;
                _gaitMixer.SetInputWeight(i, _gaitWeights[i]);
            }

            _topMixer = AnimationMixerPlayable.Create(_graph, 2);
            _graph.Connect(_gaitMixer, 0, _topMixer, 0);
            _topMixer.SetInputWeight(0, 1f);
            _topMixer.SetInputWeight(1, 0f);
            output.SetSourcePlayable(_topMixer);

            _oneShotActive = false;
            _oneShotConnected = false;

            _graph.Play();
            _ready = true;
        }

        private void OnDisable()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _ready = false;
            _oneShotActive = false;
            _oneShotConnected = false;
            _oneShotCallback = null;
        }

        /// <summary>
        /// Один прогін <paramref name="clip"/> поверх ходьби/бігу (Бій v2 §6:
        /// замах атаки, здібність, смерть). <paramref name="holdLastFrame"/> —
        /// true тримає останню позу назавжди (смерть — юніт лишається
        /// звалений, не повертається до idle); false — по завершенні кліпу
        /// сам віддає керування назад <see cref="Gait"/>-мікшеру.
        /// <paramref name="onComplete"/> кличеться РІВНО раз, коли кліп
        /// дограв (чи одразу, якщо анімація не готова/кліп порожній — такт
        /// презентера не має зависнути, чекаючи callback, якого не буде).
        /// </summary>
        public void PlayOnce(AnimationClip clip, bool holdLastFrame, System.Action onComplete = null)
        {
            if (!_ready || clip == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (_oneShotConnected) _graph.Disconnect(_topMixer, 1);
            _oneShotPlayable = AnimationClipPlayable.Create(_graph, clip);
            _oneShotPlayable.SetTime(0);
            _graph.Connect(_oneShotPlayable, 0, _topMixer, 1);
            _oneShotConnected = true;

            _oneShotActive = true;
            _oneShotHold = holdLastFrame;
            _oneShotLength = clip.length > 0.01f ? clip.length : 1f;
            _oneShotElapsed = 0f;
            _oneShotCallback = onComplete;
        }

        /// <summary>Перервати одноразовий кліп і негайно повернути керування ходьбі/бігу (наприклад, такт скасовано зовні).</summary>
        public void CancelOneShot()
        {
            if (!_oneShotActive) return;
            _oneShotActive = false;
            _oneShotCallback = null;
            if (_ready) _topMixer.SetInputWeight(1, 0f);
        }

        private void Update()
        {
            if (!_ready) return;

            if (_oneShotActive)
            {
                _oneShotElapsed += Time.deltaTime;
                if (_oneShotElapsed >= _oneShotLength)
                {
                    _oneShotPlayable.SetTime(_oneShotLength);
                    if (!_oneShotHold)
                    {
                        _oneShotActive = false;
                        _topMixer.SetInputWeight(1, 0f);
                    }
                    var callback = _oneShotCallback;
                    _oneShotCallback = null;
                    callback?.Invoke();
                }
                else
                {
                    _oneShotPlayable.SetTime(_oneShotElapsed);
                }
            }

            _topMixer.SetInputWeight(1, _oneShotActive ? 1f : 0f);
            _topMixer.SetInputWeight(0, _oneShotActive ? 0f : 1f);
            if (_oneShotActive) return; // База заморожена під замахом/смертю — не змінювати ваги під непоказуваним шаром.

            float gait = Mathf.Clamp(Gait, 0f, 2f);
            float wIdle = Mathf.Clamp01(1f - gait);
            float wWalk = gait <= 1f ? gait : 2f - gait;
            float wRun = Mathf.Clamp01(gait - 1f);
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);

            _gaitWeights[0] = Mathf.Lerp(_gaitWeights[0], wIdle, k);
            _gaitWeights[1] = Mathf.Lerp(_gaitWeights[1], wWalk, k);
            _gaitWeights[2] = Mathf.Lerp(_gaitWeights[2], wRun, k);

            for (int i = 0; i < 3; i++)
            {
                _gaitMixer.SetInputWeight(i, _gaitWeights[i]);
                var clip = _gaitClips[i].GetAnimationClip();
                double length = clip != null && clip.length > 0.01f ? clip.length : 1.0;
                double time = _gaitClips[i].GetTime();
                if (time >= length) _gaitClips[i].SetTime(time % length);
            }
        }
    }
}
