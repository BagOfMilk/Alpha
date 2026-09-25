using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Game.Gameplay
{
    /// <summary>
    /// Анімація фігурки Kenney Mini Characters без контролера аніматора:
    /// граф плейблів змішує три кліпи з того самого FBX — стоїть / іде / біжить.
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

        /// <summary>0 — стоїть, 1 — іде, 2 — біжить; проміжні значення змішують сусідні кліпи.</summary>
        public float Gait { get; set; }

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable[] _clips;
        private readonly float[] _weights = new float[3];
        private bool _ready;

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
            _mixer = AnimationMixerPlayable.Create(_graph, 3);
            output.SetSourcePlayable(_mixer);

            var source = new[] { idle, walk != null ? walk : idle, sprint != null ? sprint : (walk != null ? walk : idle) };
            _clips = new AnimationClipPlayable[3];
            for (int i = 0; i < 3; i++)
            {
                _clips[i] = AnimationClipPlayable.Create(_graph, source[i]);
                _clips[i].SetTime(phase);
                _graph.Connect(_clips[i], 0, _mixer, i);
                _weights[i] = i == 0 ? 1f : 0f;
                _mixer.SetInputWeight(i, _weights[i]);
            }

            _graph.Play();
            _ready = true;
        }

        private void OnDisable()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _ready = false;
        }

        private void Update()
        {
            if (!_ready) return;

            float gait = Mathf.Clamp(Gait, 0f, 2f);
            float wIdle = Mathf.Clamp01(1f - gait);
            float wWalk = gait <= 1f ? gait : 2f - gait;
            float wRun = Mathf.Clamp01(gait - 1f);
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);

            _weights[0] = Mathf.Lerp(_weights[0], wIdle, k);
            _weights[1] = Mathf.Lerp(_weights[1], wWalk, k);
            _weights[2] = Mathf.Lerp(_weights[2], wRun, k);

            for (int i = 0; i < 3; i++)
            {
                _mixer.SetInputWeight(i, _weights[i]);
                var clip = _clips[i].GetAnimationClip();
                double length = clip != null && clip.length > 0.01f ? clip.length : 1.0;
                double time = _clips[i].GetTime();
                if (time >= length) _clips[i].SetTime(time % length);
            }
        }
    }
}
