using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Постать на рингу: «слот» (де стоїть) + модель із Animator (Humanoid) і власний простий граф плейблів із двома входами
    /// (перехід між кліпами). Кліпи рингу запечені в позу (лише поза без root motion), тому справжнє місце постаті —
    /// таз: <see cref="PosePos"/>/<see cref="PoseYaw"/> читаються з кісток, а <see cref="Commit"/> переносить слот на таз
    /// у ТОЙ САМИЙ кадр, коли стартує наступний кліп (інакше зсув подвоїться).
    /// Не лінтується: плейбли й аніматор — глибокий API рушія (як FigureAnimation).
    /// </summary>
    public sealed class RingActor
    {
        public readonly string Name;
        public readonly Transform Slot;
        public readonly GameObject Model;
        public readonly Animator Anim;
        public readonly Transform Hips, LeftLeg, RightLeg, Head, RightHand;

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mix;
        private readonly AnimationClipPlayable[] _p = new AnimationClipPlayable[2];
        private int _cur = -1;
        private AnimationClip _clip;
        private bool _loop;
        private float _fade, _fadeT;
        private int _fadeFrom = -1;
        private bool _finished;
        private float _speed = 1f;

        public AnimationClip CurrentClip { get { return _clip; } }
        public bool Finished { get { return _finished; } }

        /// <summary>Секунди від початку поточного кліпа (з урахуванням швидкості).</summary>
        public float ClipTime { get { return _cur >= 0 && _p[_cur].IsValid() ? (float)_p[_cur].GetTime() : 0f; } }

        public RingActor(string name, GameObject prefab, Transform parent)
        {
            Name = name;
            Slot = new GameObject(name + " (слот)").transform;
            Slot.SetParent(parent, false);
            Model = Object.Instantiate(prefab, Slot);
            Model.name = name;
            Model.transform.localPosition = Vector3.zero;
            Model.transform.localRotation = Quaternion.identity;
            Anim = Model.GetComponentInChildren<Animator>();
            if (Anim == null) Anim = Model.AddComponent<Animator>();
            Anim.applyRootMotion = false;
            Anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Anim.runtimeAnimatorController = null;
            Hips = Bone(HumanBodyBones.Hips);
            LeftLeg = Bone(HumanBodyBones.LeftUpperLeg);
            RightLeg = Bone(HumanBodyBones.RightUpperLeg);
            Head = Bone(HumanBodyBones.Head);
            RightHand = Bone(HumanBodyBones.RightHand);

            _graph = PlayableGraph.Create(name + ".ring");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, name, Anim);
            _mix = AnimationMixerPlayable.Create(_graph, 2);
            output.SetSourcePlayable(_mix);
            _graph.Play();
        }

        /// <summary>Кістка гуманоїда за ім'ям (для канатів: капсули тіла). Null, якщо аватара немає.</summary>
        public Transform GetBone(HumanBodyBones b) { return Bone(b); }

        private Transform Bone(HumanBodyBones b)
        {
            return Anim != null && Anim.avatar != null && Anim.isHuman ? Anim.GetBoneTransform(b) : null;
        }

        public void Destroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        // ---------------- кліпи ----------------

        /// <summary>
        /// Зіграти кліп. <paramref name="fade"/> — перехід від поточного, с (0 — різка). <paramref name="loop"/>: петля
        /// (для Idle_*), інакше після кінця тримається останній кадр і виставляється <see cref="Finished"/>.
        /// </summary>
        public void Play(AnimationClip clip, bool loop, float fade, float speed = 1f)
        {
            if (clip == null) return;
            int slot = _cur < 0 ? 0 : 1 - _cur;
            if (_p[slot].IsValid())
            {
                _graph.Disconnect(_mix, slot);
                _p[slot].Destroy();
            }
            var pl = AnimationClipPlayable.Create(_graph, clip);
            pl.SetApplyFootIK(false);
            pl.SetSpeed(speed);
            pl.SetTime(0);
            _graph.Connect(pl, 0, _mix, slot);
            _p[slot] = pl;
            if (fade <= 0f || _cur < 0)
            {
                if (_cur >= 0 && _p[_cur].IsValid())
                {
                    _graph.Disconnect(_mix, _cur);
                    _p[_cur].Destroy();
                }
                _mix.SetInputWeight(slot, 1f);
                _mix.SetInputWeight(1 - slot, 0f);
                _fadeFrom = -1;
            }
            else
            {
                _mix.SetInputWeight(slot, 0f);
                _fadeFrom = _cur;
                _fade = fade;
                _fadeT = 0f;
            }
            _cur = slot;
            _clip = clip;
            _loop = loop;
            _finished = false;
            _speed = speed;
        }

        /// <summary>
        /// Різкий старт кліпа з тазом у заданій точці й курсом. Кліпи рингу несуть запечений зсув (абсолютне місце з
        /// авторської сцени), тож слот ставиться за тазом першого кадру: граф обчислюється одразу (Evaluate), таз читається
        /// й слот зсувається — до малювання кадру постать уже на місці.
        /// </summary>
        public void PlayAt(AnimationClip clip, bool loop, Vector3 pos, float yaw, float speed = 1f)
        {
            Play(clip, loop, 0f, speed);
            _graph.Evaluate(0f);
            SlideBegin();
            SlideSet(pos, yaw);
        }

        /// <summary>Те саме на місці, де постать стоїть зараз (за тазом).</summary>
        public void PlayHere(AnimationClip clip, bool loop, float speed = 1f)
        {
            PlayAt(clip, loop, PosePos, PoseYaw, speed);
        }

        public void Tick(float dt)
        {
            if (_cur < 0) return;
            if (_fadeFrom >= 0)
            {
                _fadeT += dt;
                float w = _fade > 0f ? Mathf.Clamp01(_fadeT / _fade) : 1f;
                _mix.SetInputWeight(_cur, w);
                _mix.SetInputWeight(_fadeFrom, 1f - w);
                if (w >= 1f)
                {
                    if (_p[_fadeFrom].IsValid())
                    {
                        _graph.Disconnect(_mix, _fadeFrom);
                        _p[_fadeFrom].Destroy();
                    }
                    _fadeFrom = -1;
                }
            }
            var p = _p[_cur];
            if (!p.IsValid() || _clip == null) return;
            double t = p.GetTime();
            double len = _clip.length;
            if (_loop)
            {
                if (t >= len) p.SetTime(t % len);
            }
            else if (!_finished && t >= len - 0.02)
            {
                p.SetTime(len - 0.02);
                p.SetSpeed(0);
                _finished = true;
            }
        }

        // ---------------- місце (таз) ----------------

        /// <summary>Де постать зараз насправді (проєкція тазу), світ.</summary>
        public Vector3 PosePos
        {
            get
            {
                Vector3 p = Hips != null ? Hips.position : Slot.position;
                return new Vector3(p.x, Slot.position.y, p.z);
            }
        }

        /// <summary>Курс постаті з лінії стегон (вперед = праве стегно мінус ліве, поворот на 90).</summary>
        public float PoseYaw
        {
            get
            {
                if (LeftLeg == null || RightLeg == null) return Slot.eulerAngles.y;
                Vector3 right = RightLeg.position - LeftLeg.position;
                right.y = 0f;
                if (right.sqrMagnitude < 1e-4f) return Slot.eulerAngles.y;
                Vector3 fwd = Vector3.Cross(right.normalized, Vector3.up);
                return Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            }
        }

        private Vector3 _hipLocal;
        private float _yawOff;

        /// <summary>Запам'ятати, де таз відносно слота (для плавного пересування утримуваної пози).</summary>
        public void SlideBegin()
        {
            Vector3 d = PosePos - Slot.position;
            d.y = 0f;
            _hipLocal = Quaternion.Inverse(Slot.rotation) * d;
            _yawOff = Mathf.DeltaAngle(Slot.eulerAngles.y, PoseYaw);
        }

        /// <summary>Поставити слот так, щоб таз (поза) опинився в точці з курсом; використовує знімок <see cref="SlideBegin"/>.</summary>
        public void SlideSet(Vector3 pos, float yaw)
        {
            float slotYaw = yaw - _yawOff;
            var rot = Quaternion.Euler(0f, slotYaw, 0f);
            Vector3 p = pos - rot * _hipLocal;
            Slot.SetPositionAndRotation(new Vector3(p.x, Slot.position.y, p.z), rot);
        }

        /// <summary>Слот у точку тазу (викликати в ТОЙ САМИЙ кадр, що й Play нового кліпа). Повертає зсув, м.</summary>
        public float Commit()
        {
            Vector3 hp = PosePos;
            float yaw = PoseYaw;
            float moved = new Vector2(hp.x - Slot.position.x, hp.z - Slot.position.z).magnitude;
            Slot.SetPositionAndRotation(new Vector3(hp.x, Slot.position.y, hp.z), Quaternion.Euler(0f, yaw, 0f));
            return moved;
        }

        public void SetHeight(float y)
        {
            var p = Slot.position;
            Slot.position = new Vector3(p.x, y, p.z);
        }

        public Vector3 HeadPos { get { return Head != null ? Head.position : Slot.position + Vector3.up * 1.7f; } }
    }
}
