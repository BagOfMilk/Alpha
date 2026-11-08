using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Постать на рингу: «слот» (де стоїть) + модель із Animator (Humanoid) і власний простий граф плейблів із трьома входами
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

        /// <summary>Висота тазу над підлогою слота у випрямленій позі, м (з моделі при створенні).</summary>
        public readonly float StandHip;

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mix;
        /// <summary>
        /// Входів мікшера три (08.11.2026): новий перехід посеред ще не завершеного (біг → канат за 0,1 с після старту бігу)
        /// раніше обривав кліп, що згасав, — поза стрибала. Тепер новий кліп займає вільний чи найслабший вхід, а решта
        /// згасають пропорційно.
        /// </summary>
        private const int Inputs = 3;
        private readonly AnimationClipPlayable[] _p = new AnimationClipPlayable[Inputs];
        private readonly bool[] _pLoop = new bool[Inputs];
        private readonly float[] _w0 = new float[Inputs];
        private int _cur = -1;
        private AnimationClip _clip;
        private bool _loop;
        private float _fade, _fadeT;
        private bool _fading;
        private bool _finished;
        private float _speed = 1f;

        /// <summary>Foot IK у кліпах (лукбук порівнює з ним і без нього).</summary>
        public static bool FootIK = true;

        /// <summary>Імена всіх кліпів, зіграних за запуск (огляд рахує, скільки логічних кліпів грає за сезон).</summary>
        public static readonly System.Collections.Generic.HashSet<string> Played = new System.Collections.Generic.HashSet<string>();

        public AnimationClip CurrentClip { get { return _clip; } }
        public bool Finished { get { return _finished; } }

        /// <summary>Секунди від початку поточного кліпа (з урахуванням швидкості).</summary>
        public float ClipTime { get { return _cur >= 0 && _p[_cur].IsValid() ? (float)_p[_cur].GetTime() : 0f; } }

        /// <summary>Час поточного кліпа з початку гри цього кліпа, з усіма обертами петлі (для лічби циклів ходи).</summary>
        public float ClipTimeTotal { get { return _clip != null ? _curLoops * _clip.length + ClipTime : 0f; } }
        private int _curLoops;

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
            // Висота кісток стоп у бінд-позі (у просторі моделі): там підошва стоїть рівно на підлозі слота.
            _feet = new[] { Bone(HumanBodyBones.LeftFoot), Bone(HumanBodyBones.RightFoot), Bone(HumanBodyBones.LeftToes), Bone(HumanBodyBones.RightToes) };
            _feetBind = new float[_feet.Length];
            for (int i = 0; i < _feet.Length; i++)
                if (_feet[i] != null) _feetBind[i] = Model.transform.InverseTransformPoint(_feet[i].position).y;
            // Висота тазу у випрямленій (бінд-) позі — мірило «стоїть / ні» для постатей різного зросту.
            StandHip = Hips != null ? Mathf.Max(0.5f, Hips.position.y - Slot.position.y) : 0.95f;

            _graph = PlayableGraph.Create(name + ".ring");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, name, Anim);
            _mix = AnimationMixerPlayable.Create(_graph, Inputs);
            output.SetSourcePlayable(_mix);
            _graph.Play();
        }

        /// <summary>Кістка гуманоїда за ім'ям (для канатів: капсули тіла). Null, якщо аватара немає.</summary>
        public Transform GetBone(HumanBodyBones b) { return Bone(b); }

        /// <summary>
        /// Стопа <paramref name="i"/> (0 — ліва, 1 — права) для фаз-тесту: точка носка (немає — стопи) у світі й висота над
        /// її бінд-висотою, м (≈0 — стоїть на маті).
        /// </summary>
        public float FootLift(int i, out Vector3 pos)
        {
            int k = _feet[2 + i] != null ? 2 + i : i;
            var f = _feet[k];
            if (f == null) { pos = PosePos; return 1f; }
            pos = f.position;
            return pos.y - (Slot.position.y + _feetBind[k] * Model.transform.lossyScale.y);
        }

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
            Played.Add(clip.name);
            bool blend = fade > 0f && _cur >= 0;
            // Вхід для нового кліпа: вільний, інакше найслабший (не поточний).
            int slot = -1;
            for (int i = 0; i < Inputs && slot < 0; i++) if (i != _cur && !_p[i].IsValid()) slot = i;
            if (slot < 0)
            {
                float wmin = float.MaxValue;
                for (int i = 0; i < Inputs; i++)
                {
                    if (i == _cur) continue;
                    float w = _mix.GetInputWeight(i);
                    if (w < wmin) { wmin = w; slot = i; }
                }
            }
            Drop(slot);
            var pl = AnimationClipPlayable.Create(_graph, clip);
            // Foot IK: стопи там, де їх поставив автор кліпа. Без нього перенесення на інші пропорції тіла зводило коліна
            // всередину, розводило стопи й топило підошви в мат на 4–5 см (огляд 04.11.2026).
            pl.SetApplyFootIK(FootIK);
            pl.SetSpeed(speed);
            pl.SetTime(0);
            _graph.Connect(pl, 0, _mix, slot);
            _p[slot] = pl;
            _pLoop[slot] = loop;
            if (loop) Net(clip);
            if (!blend)
            {
                _slotBlend = false;
                for (int i = 0; i < Inputs; i++) if (i != slot) Drop(i);
                for (int i = 0; i < Inputs; i++) _mix.SetInputWeight(i, i == slot ? 1f : 0f);
                _fading = false;
            }
            else
            {
                // Решта згасають від своїх нинішніх ваг (нормованих до 1 — сума ваг гуманоїда не менша за 1).
                float sum = 0f;
                for (int i = 0; i < Inputs; i++)
                {
                    _w0[i] = i == slot || !_p[i].IsValid() ? 0f : _mix.GetInputWeight(i);
                    sum += _w0[i];
                }
                if (sum <= 1e-4f) { _w0[_cur] = 1f; sum = 1f; }
                for (int i = 0; i < Inputs; i++) { _w0[i] /= sum; _mix.SetInputWeight(i, _w0[i]); }
                _fading = true;
                _fade = fade;
                _fadeT = 0f;
            }
            _cur = slot;
            _clip = clip;
            _loop = loop;
            _finished = false;
            _speed = speed;
            _curLoops = 0;
        }

        private void Drop(int i)
        {
            if (i < 0 || !_p[i].IsValid()) return;
            _graph.Disconnect(_mix, i);
            _p[i].Destroy();
            _mix.SetInputWeight(i, 0f);
        }

        private struct LoopNet { public Vector3 Hip0, HipEnd; public float Yaw; }
        private static readonly System.Collections.Generic.Dictionary<string, LoopNet> _nets = new System.Collections.Generic.Dictionary<string, LoopNet>();

        /// <summary>
        /// Хід петлі за один оберт у системі слота: таз на старті й наприкінці (екстраполяція до повної довжини — на самій
        /// довжині петля вже перемотана) і поворот. Для бігу ~2,6 м, для ходи 0,36 м, для кружляння — ще й 13°.
        /// </summary>
        private LoopNet Net(AnimationClip c)
        {
            LoopNet n;
            string key = Name + "|" + c.name;
            if (_nets.TryGetValue(key, out n)) return n;
            float len = c.length, e = Mathf.Min(1f / 120f, len * 0.25f);
            Vector3 h0, h1;
            float y0, y1;
            SampleLocal(c, 0f, out h0, out y0);
            SampleLocal(c, len - e, out h1, out y1);
            float k = len / Mathf.Max(1e-4f, len - e);
            n.Hip0 = new Vector3(h0.x, 0f, h0.z);
            n.HipEnd = n.Hip0 + new Vector3(h1.x - h0.x, 0f, h1.z - h0.z) * k;
            n.Yaw = Mathf.DeltaAngle(y0, y1) * k;
            _nets[key] = n;
            return n;
        }

        /// <summary>Швидкість ходу петлі на цьому тілі, м/с (хід тазу за оберт / довжина) — для ходи виходу без ковзання.</summary>
        public float LoopSpeed(AnimationClip c)
        {
            if (c == null || c.length <= 1e-3f) return 0f;
            var n = Net(c);
            Vector3 d = n.HipEnd - n.Hip0;
            d.y = 0f;
            return d.magnitude / c.length;
        }

        /// <summary>
        /// Петля з запеченим ходом перемоталась (таз у кліпі стрибнув назад на хід петлі): слот переноситься вперед на хід
        /// з вагою входу, тож таз на екрані йде далі без стрибка (огляд 08.11.2026, фаз-тест: Run_Loop — «телепорт 2,6 м»).
        /// </summary>
        private void Carry(AnimationClip c, float w)
        {
            var n = Net(c);
            Vector3 hipEnd = Vector3.Lerp(n.Hip0, n.HipEnd, w);
            // Під час переходу таз інтегрується, а слот щокадру ставиться під виміряну позу — перемотування поглинається само.
            if (_slotBlend) return;
            var rot = Slot.rotation;
            var rot2 = rot * Quaternion.Euler(0f, n.Yaw * w, 0f);
            Vector3 p = Slot.position + rot * hipEnd - rot2 * n.Hip0;
            Slot.SetPositionAndRotation(new Vector3(p.x, Slot.position.y, p.z), rot2);
        }

        /// <summary>
        /// Різкий старт кліпа з тазом у заданій точці й курсом. Кліпи рингу несуть запечений зсув (абсолютне місце з
        /// авторської сцени), тож слот ставиться за тазом першого кадру: граф обчислюється одразу (Evaluate), таз читається
        /// й слот зсувається — до малювання кадру постать уже на місці.
        /// </summary>
        public void PlayAt(AnimationClip clip, bool loop, Vector3 pos, float yaw, float speed = 1f)
        {
            PlayAtTime(clip, 0f, loop, pos, yaw, speed);
        }

        /// <summary>Те саме на місці, де постать стоїть зараз (за тазом).</summary>
        public void PlayHere(AnimationClip clip, bool loop, float speed = 1f)
        {
            PlayAt(clip, loop, PosePos, PoseYaw, speed);
        }

        /// <summary>
        /// Різкий старт кліпа з миті <paramref name="t"/> (с часу кліпа), таз цієї миті — у точці з курсом. Для прийомів з
        /// розбігом: кліп починається вже на бігу, ближче до суперника.
        /// </summary>
        public void PlayAtTime(AnimationClip clip, float t, bool loop, Vector3 pos, float yaw, float speed = 1f)
        {
            if (clip == null) return;
            // Вирівнювання — за вибіркою нового кліпа на миті t (таз і курс у системі слота), а не за позою після
            // _graph.Evaluate: той після вибірок тимчасовим графом нічого не записує, і слот ставився за чужою позою
            // (налагодження 08.11.2026).
            Vector3 hip;
            float hy;
            SampleLocal(clip, t, out hip, out hy);
            // Досі на екрані таз тут; новий кліп ставить його в pos. Невелику різницю (до 0,45 м) не стрибком, а
            // поправкою на перші 0,35 с кліпа — «залишок — поправкою, розподіленою на рухомий відрізок».
            Vector3 was = _hasPrevHip ? _prevHip : PosePos;
            Vector3 jump = new Vector3(was.x - pos.x, 0f, was.z - pos.z);
            Play(clip, loop, 0f, speed);
            if (_cur >= 0 && _p[_cur].IsValid()) { _p[_cur].SetTime(t); _p[_cur].SetTime(t); }
            var rot = Quaternion.Euler(0f, yaw - hy, 0f);
            Vector3 p = pos - rot * new Vector3(hip.x, 0f, hip.z);
            _easeLeft = 0f;
            if (_hasPrevHip && jump.magnitude > 0.02f && jump.magnitude <= 0.45f)
            {
                p += jump;
                _easeOff = jump;
                _easeLeft = _easeDur = 0.35f;
            }
            Slot.SetPositionAndRotation(new Vector3(p.x, Slot.position.y, p.z), rot);
            _graph.Evaluate(0f);
            _hasPrevHip = false;
            _hipVel = Vector3.zero;
        }

        private Vector3 _easeOff;
        private float _easeLeft, _easeDur;

        private bool _slotBlend;
        private Quaternion _slotToRot;

        /// <summary>
        /// Кросфейд у кліп з миті <paramref name="t"/>, таз цієї миті — у точці <paramref name="pos"/> з курсом (зазвичай — де
        /// постать зараз). Під час переходу таз на екрані — змішування двох передбачень (старий рух триває, новий кліп іде від
        /// точки входу за своїм часом), курс — коротшим шляхом до курсу нового кліпа; наприкінці слот — точно у вирівнюванні
        /// нового кліпа. Так біг переходить у канат, хода — у стійку.
        /// </summary>
        /// <param name="travel">Курс <paramref name="yaw"/> — напрям ходу кліпа (вісь «уперед» слота), а не лінія стегон: для
        /// бігу й канатів (стегна на бігу крутяться ±10°, і з вирівнюванням за ними біг ішов навскіс — фаз-тест 08.11.2026).</param>
        public void PlayAtBlend(AnimationClip clip, float t, bool loop, Vector3 pos, float yaw, float fade, float speed = 1f, bool travel = false)
        {
            if (clip == null) return;
            if (fade <= 0f || _cur < 0) { PlayAtTime(clip, t, loop, pos, yaw, speed); return; }
            Vector3 hip;
            float hy;
            SampleLocal(clip, t, out hip, out hy);
            _slotToRot = Quaternion.Euler(0f, travel ? yaw : yaw - hy, 0f);
            Play(clip, loop, fade, speed);
            if (_cur >= 0 && _p[_cur].IsValid()) { _p[_cur].SetTime(t); _p[_cur].SetTime(t); }
            // Хід тазу нового кліпа на час переходу (система слота; петля — розгорнута на її хід), швидкість тазу до переходу
            // і звідки інтегрувати таз.
            // Вікно — з запасом на повільні кадри (огляд іде ~10 кадрів/с: час кліпа виходив за вибірку ще до кінця переходу,
            // швидкість обрізалась до нуля, і таз «завмирав» на кадр — відставання 0,4–0,5 м, демо 08.11.2026).
            float span = Mathf.Max(fade * Mathf.Max(0.05f, speed) * 1.5f, 0.15f) + 0.45f * Mathf.Max(0.5f, speed), len = clip.length;
            for (int i = 0; i < BlendN; i++)
            {
                float ti = t + span * i / (BlendN - 1);
                _bT[i] = ti;
                _bQ[i] = loop ? Mathf.Repeat(ti, len) : Mathf.Min(ti, len - 1e-3f);
            }
            SamplePath(clip, _bQ, _bH, _bY, true);
            if (loop)
            {
                var n = Net(clip);
                for (int i = 0; i < BlendN; i++) _bH[i] += (n.HipEnd - n.Hip0) * Mathf.Floor(_bT[i] / len);
            }
            _bV0 = Vector3.ClampMagnitude(_hipVel, 7f);
            // Перехід у нерухомий кліп (стійка, очікування): старий рух не триває — таз зупиняється одразу, стопи кінця
            // кроку вже там, де в стійці (хода → стійка ковзала стопами на 15 см, налагодження 08.11.2026).
            Vector3 travelN = _bH[BlendN - 1] - _bH[0];
            travelN.y = 0f;
            if (travelN.magnitude < 0.06f)
            {
                _bV0 = Vector3.zero;
                // Старі кліпи завмирають на своїй позі: хода, зупинена на межі циклу, інакше починала наступний крок під
                // час переходу, і стійка тягла стопу назад (10–20 см ковзання, налагодження 08.11.2026).
                for (int i = 0; i < Inputs; i++) if (i != _cur && _p[i].IsValid()) _p[i].SetSpeed(0);
            }
            // Звідки інтегрувати — таз, який був на екрані (записаний наприкінці минулого кадру): синхронні обчислення графа
            // вище (вибірки кліпа) уже зрушили поточну позу (налагодження 08.11.2026: «−0,84 м» на вході в канат).
            _blendH = _hasPrevHip ? _prevHip : PosePos;
            _blendH.y = 0f;
            _easeLeft = 0f;
            _bPos = new Vector3(pos.x, 0f, pos.z);
            _bAge = 0f;
            _bFirst = true;
            _blendYaw0 = PoseYaw;
            _blendTail = 1;
            _slotBlend = true;
        }

        private float _blendYaw0, _bAge;
        private Vector3 _bPos;
        private bool _bFirst;

        /// <summary>Курс пози нового кліпа в його слоті (°) на миті його часу — з вибірки переходу.</summary>
        private float BlendYaw(float t)
        {
            int j = 0;
            while (j < BlendN - 2 && t > _bT[j + 1]) j++;
            float u = Mathf.Clamp01((t - _bT[j]) / Mathf.Max(1e-4f, _bT[j + 1] - _bT[j]));
            return Mathf.LerpAngle(_bY[j], _bY[j + 1], u);
        }

        private Vector3 _bV0, _blendH, _prevHip, _hipVel;
        private bool _hasPrevHip;
        private int _blendTail;

        private const int BlendN = 10;
        private readonly float[] _bT = new float[BlendN], _bQ = new float[BlendN], _bY = new float[BlendN];
        private readonly Vector3[] _bH = new Vector3[BlendN];

        /// <summary>Таз нового кліпа (система слота, без висоти) на миті його часу <paramref name="t"/> — з вибірки переходу.</summary>
        private Vector3 BlendHip(float t)
        {
            int j = 0;
            while (j < BlendN - 2 && t > _bT[j + 1]) j++;
            // За межами вибірки — продовження останнього відрізка (не обрізання: обрізання давало нульову швидкість).
            float u = Mathf.Max(0f, (t - _bT[j]) / Mathf.Max(1e-4f, _bT[j + 1] - _bT[j]));
            Vector3 h = Vector3.LerpUnclamped(_bH[j], _bH[j + 1], u);
            h.y = 0f;
            return h;
        }

        /// <summary>Таз кліпа на миті t у системі слота (м, без підйому) і курс відносно слота (°).</summary>
        public void SampleLocal(AnimationClip clip, float t, out Vector3 hip, out float yaw)
        {
            hip = Vector3.zero; yaw = 0f;
            if (clip == null || Anim == null || Hips == null) return;
            SaveBones();
            var g = PlayableGraph.Create(Name + ".probe1");
            g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var o = AnimationPlayableOutput.Create(g, Name + ".probe1", Anim);
            var pl = AnimationClipPlayable.Create(g, clip);
            pl.SetApplyFootIK(FootIK);
            o.SetSourcePlayable(pl);
            pl.SetTime(t);
            pl.SetTime(t);
            g.Evaluate(0f);
            hip = Quaternion.Inverse(Slot.rotation) * (Hips.position - Slot.position);
            yaw = Mathf.DeltaAngle(Slot.eulerAngles.y, PoseYaw);
            g.Destroy();
            RestoreBones();
        }

        private Transform[] _bones;
        private Vector3[] _bonePos;
        private Quaternion[] _boneRot;

        /// <summary>
        /// Вибірка кліпа тимчасовим графом лишає кістки в позі останньої вибірки до наступного обчислення аніматора
        /// (_graph.Evaluate основного графа після неї нічого не записує — налагодження 08.11.2026: таз «стрибав» на 0,6–0,8 м
        /// у тому самому кадрі). Тому поза на екрані зберігається до вибірки й повертається після — вибірка без слідів.
        /// </summary>
        private void SaveBones()
        {
            if (_bones == null)
            {
                _bones = Model.GetComponentsInChildren<Transform>(true);
                _bonePos = new Vector3[_bones.Length];
                _boneRot = new Quaternion[_bones.Length];
            }
            for (int i = 0; i < _bones.Length; i++)
            {
                _bonePos[i] = _bones[i].localPosition;
                _boneRot[i] = _bones[i].localRotation;
            }
        }

        private void RestoreBones()
        {
            if (_bones == null) return;
            for (int i = 0; i < _bones.Length; i++)
            {
                _bones[i].localPosition = _bonePos[i];
                _bones[i].localRotation = _boneRot[i];
            }
        }

        /// <summary>
        /// Таз (м) і курс (°) кліпа на мітях <paramref name="times"/> — у системі пози кліпа на першій миті з масиву
        /// (x — праворуч, z — уперед). Окремий тимчасовий граф на тому ж аніматорі, обчислений вручну; головний граф одразу
        /// обчислюється знову, тож до малювання кадру постать така сама, як була. Так режисер рахує шлях кліпа на цьому тілі
        /// (Humanoid масштабує зсув тазу під пропорції постаті).
        /// </summary>
        /// <param name="slotFrame">Таз і курс — у системі слота кліпа (вісь «уперед» — хід кліпа, від початку слота), без
        /// відліку від першої миті: для канатів, де кліп вирівнюється за ходом (<see cref="PlayAtBlend"/> з travel).</param>
        public void SamplePath(AnimationClip clip, float[] times, Vector3[] pos, float[] yaw, bool slotFrame = false)
        {
            if (clip == null || Anim == null || Hips == null) return;
            var g = PlayableGraph.Create(Name + ".probe");
            g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            SaveBones();
            var o = AnimationPlayableOutput.Create(g, Name + ".probe", Anim);
            var pl = AnimationClipPlayable.Create(g, clip);
            pl.SetApplyFootIK(FootIK);
            o.SetSourcePlayable(pl);
            Vector3 p0 = Vector3.zero;
            float y0 = 0f;
            for (int i = 0; i < times.Length; i++)
            {
                pl.SetTime(times[i]);
                pl.SetTime(times[i]);
                g.Evaluate(0f);
                Vector3 hp = Quaternion.Inverse(Slot.rotation) * (Hips.position - Slot.position);
                float yw = Mathf.DeltaAngle(Slot.eulerAngles.y, PoseYaw);
                if (slotFrame) { pos[i] = hp; yaw[i] = yw; continue; }
                if (i == 0) { p0 = hp; y0 = yw; }
                Vector3 d = hp - p0;
                d.y = hp.y;
                pos[i] = Quaternion.Euler(0f, -y0, 0f) * new Vector3(d.x, 0f, d.z) + Vector3.up * d.y;
                yaw[i] = Mathf.DeltaAngle(y0, yw);
            }
            g.Destroy();
            RestoreBones();
        }

        private readonly Transform[] _feet;
        private readonly float[] _feetBind;
        private float _lift;

        /// <summary>
        /// Стопи не крізь підлогу: кліпи рингу опускають таз без прив'язки ступень (стійка — підошва на 4–5 см у маті,
        /// огляд 04.11.2026). Після анімації модель піднімається рівно на глибину, на яку найнижча кістка стопи пішла нижче
        /// своєї бінд-висоти над слотом; вниз не тягне ніколи (стрибки, кидки — як у кліпі). Стеля — 15 см. Лише коли таз
        /// високо (стоїть, присів): лежачи й на колінах стопа на боці, бінд-висота нічого не означає. Підйом плавний.
        /// </summary>
        public void Ground(float dt)
        {
            if (_feet == null) return;
            float scale = Model.transform.lossyScale.y;
            float baseY = Slot.position.y;
            float need = 0f;
            for (int i = 0; i < _feet.Length; i++)
            {
                if (_feet[i] == null) continue;
                float y = _feet[i].position.y - _lift;                   // без поточного підйому
                need = Mathf.Max(need, baseY + _feetBind[i] * scale - y);
            }
            if (Hips == null || (Hips.position.y - _lift - baseY) / Mathf.Max(1e-4f, scale) < 0.6f) need = 0f;
            float target = Mathf.Min(need, 0.15f);
            _lift = dt <= 0f ? target : Mathf.MoveTowards(_lift, target, 0.6f * dt);
            Model.transform.localPosition = new Vector3(0f, _lift / Mathf.Max(1e-4f, Slot.lossyScale.y), 0f);
        }

        public void Tick(float dt)
        {
            Ground(dt);
            if (_cur < 0) return;
            float f = 1f;
            if (_fading)
            {
                _fadeT += dt;
                f = _fade > 0f ? Mathf.Clamp01(_fadeT / _fade) : 1f;
                for (int i = 0; i < Inputs; i++)
                    if (_p[i].IsValid()) _mix.SetInputWeight(i, i == _cur ? f : _w0[i] * (1f - f));
            }
            // Петлі (поточна й ті, що згасають): перемотування — зі слотом, перенесеним на хід петлі.
            bool wrapped = false;
            for (int i = 0; i < Inputs; i++)
            {
                if (!_p[i].IsValid() || !_pLoop[i]) continue;
                var c = _p[i].GetAnimationClip();
                if (c == null) continue;
                double len = c.length, t = _p[i].GetTime();
                if (len <= 0.0 || t < len) continue;
                if (i == _cur) _curLoops += (int)(t / len);
                _p[i].SetTime(t % len);
                _p[i].SetTime(t % len);
                float w = _mix.GetInputWeight(i);
                if (w > 0.01f) Carry(c, w);
                wrapped = true;
            }
            if (wrapped) _graph.Evaluate(0f);
            if (_slotBlend && dt > 0f)
            {
                // Таз на екрані під час переходу — інтеграл швидкості: (1−f)·швидкість до переходу + f·швидкість нового кліпа
                // (з вибірки його тазу, у новому вирівнюванні); слот щокадру ставиться так, щоб виміряна змішана поза мала таз
                // саме там. Таз не стрибає за побудовою: змішаний таз гуманоїда — не лінійна суміш тазів кліпів (налагодження
                // 08.11.2026: посеред «кружляння → біг» таз лишався, де в кружлянні), тож формули «від двох вирівнювань»
                // давали стрибки на 0,7–1,5 м.
                // Курс на екрані — від курсу до переходу до курсу нового кліпа коротшим шляхом; слот = ціль − виміряний
                // курс пози в слоті. (Slerp слота сам по собі крутив тіло на повний оберт, коли поза старого кліпа вже
                // розвернута на 180° у своєму слоті: відскок → біг назад, демо 08.11.2026.)
                Vector3 h = Quaternion.Inverse(Slot.rotation) * (PosePos - Slot.position);
                h.y = 0f;
                float tn = (float)_p[_cur].GetTime();
                if (_loop && tn < _bT[0] - 1e-3f && _clip != null) tn += _clip.length;
                float localYaw = Mathf.DeltaAngle(Slot.eulerAngles.y, PoseYaw);
                float newYaw = _slotToRot.eulerAngles.y + BlendYaw(tn);
                float targetYaw = _blendYaw0 + Mathf.DeltaAngle(_blendYaw0, newYaw) * f;
                // Наприкінці — точно задумане вирівнювання нового кліпа (вимір курсу пози за кадр шумить, а на швидкому
                // розвороті кліпа помилка повертала весь подальший шлях — B ішов уздовж каната вбік на 0,9 м).
                float toYaw = _slotToRot.eulerAngles.y;
                var rot = Quaternion.Euler(0f, Mathf.LerpAngle(targetYaw - localYaw, toYaw, f * f), 0f);
                // Новий плейбл на першому кадрі не просувається в часі — підштовхнути, щоб кліп ішов за годинником.
                if (_bFirst)
                {
                    _bFirst = false;
                    // Таз цього кадру — за очікуваним часом (поза відстає на кадр, таз — ні: інакше ривок 0,2–0,4 м наступного кадру).
                    if (tn <= _bT[0] + 1e-4f) { float nt = tn + dt * _speed; _p[_cur].SetTime(nt); _p[_cur].SetTime(nt); tn = nt; }
                }
                _bAge += dt;
                // Таз — змішування двох передбачень: старий рух триває з колишньою швидкістю, новий кліп іде від точки входу
                // за своїм справжнім часом. Без інтегрування: наприкінці таз рівно там, де задумано, за будь-якої частоти
                // кадрів (інтеграл на 7–10 кадрах/с огляду давав розкид ±0,5 м біля каната, демо 08.11.2026).
                Vector3 oldP = _blendH + _bV0 * _bAge;
                Vector3 newP = _bPos + _slotToRot * (BlendHip(tn) - BlendHip(_bT[0]));
                Vector3 H = Vector3.Lerp(oldP, newP, f);
                H.y = 0f;
                Vector3 sp = H - rot * h;
                Slot.SetPositionAndRotation(new Vector3(sp.x, Slot.position.y, sp.z), rot);
                // Поза з вагами цього кадру з'явиться лише наступного (граф обчислюється до LateUpdate) — ще кадр «хвоста»,
                // інакше таз відкочувався на 7 см одразу після переходу (налагодження 08.11.2026).
                if (f >= 1f && _blendTail-- <= 0) _slotBlend = false;
            }
            if (_fading && f >= 1f)
            {
                for (int i = 0; i < Inputs; i++) if (i != _cur) Drop(i);
                _fading = false;
            }
            // Поправка після різкого старту кліпа — тане рівномірно.
            if (_easeLeft > 0f && !_slotBlend && dt > 0f)
            {
                float k = Mathf.Min(dt, _easeLeft) / Mathf.Max(1e-4f, _easeDur);
                Slot.position -= _easeOff * k;
                _easeLeft -= dt;
            }
            // Швидкість тазу на екрані (для переходів).
            Vector3 hp = PosePos;
            if (_hasPrevHip && dt > 0f)
            {
                Vector3 vv = (hp - _prevHip) / dt;
                vv.y = 0f;
                _hipVel = Vector3.Lerp(_hipVel, vv, 0.5f);
            }
            _prevHip = hp;
            _hasPrevHip = true;
            var p = _p[_cur];
            if (!p.IsValid() || _clip == null || _loop) return;
            double tc = p.GetTime();
            double lc = _clip.length;
            if (!_finished && tc >= lc - 0.02)
            {
                p.SetTime(lc - 0.02);
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
            _slotBlend = false;
            _easeLeft = 0f;
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

        /// <summary>Іде перехід між кліпами (кросфейд).</summary>
        public bool Blending { get { return _fading || _slotBlend; } }

        /// <summary>Налагодження руху: входи мікшера (кліп@час×вага), слот і таз — одним рядком.</summary>
        public string DebugMix()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Inputs; i++)
            {
                if (!_p[i].IsValid()) continue;
                var c = _p[i].GetAnimationClip();
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0}{1}@{2:0.00}x{3:0.00} ", i == _cur ? "*" : "",
                    c != null ? c.name : "?", _p[i].GetTime(), _mix.GetInputWeight(i));
            }
            Vector3 h = Hips != null ? Hips.position : Slot.position;
            Vector3 hl = Quaternion.Inverse(Slot.rotation) * (h - Slot.position);
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "| slot ({0:0.000},{1:0.000}) {2:0.0}° | hips ({3:0.000},{4:0.000}) local ({5:0.000},{6:0.000}) | blend {7} fade {8:0.00}",
                Slot.position.x, Slot.position.z, Slot.eulerAngles.y, h.x, h.z, hl.x, hl.z, _slotBlend, _fading ? Mathf.Clamp01(_fadeT / Mathf.Max(1e-4f, _fade)) : 1f);
            return sb.ToString();
        }

        /// <summary>
        /// Поворот слота навколо таза (таз на місці): кермо ходи — напрям ходу кліпа доводиться на ціль дугою, а не
        /// ковзанням (правило режисера прев'ю: «курс — дугою»).
        /// </summary>
        public void TurnAboutHips(float deg)
        {
            if (Mathf.Abs(deg) < 1e-4f) return;
            Vector3 h = PosePos;
            Vector3 local = Quaternion.Inverse(Slot.rotation) * (h - Slot.position);
            var rot = Quaternion.Euler(0f, deg, 0f) * Slot.rotation;
            Vector3 p = h - rot * local;
            Slot.SetPositionAndRotation(new Vector3(p.x, Slot.position.y, p.z), rot);
        }

        /// <summary>Зсунути слот (поправка ходи, розподілена по кадрах).</summary>
        public void Nudge(Vector3 d)
        {
            Slot.position += new Vector3(d.x, 0f, d.z);
        }

        public void SetHeight(float y)
        {
            var p = Slot.position;
            Slot.position = new Vector3(p.x, y, p.z);
        }

        public Vector3 HeadPos { get { return Head != null ? Head.position : Slot.position + Vector3.up * 1.7f; } }
    }
}
