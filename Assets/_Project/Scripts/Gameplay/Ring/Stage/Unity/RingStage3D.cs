using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Ring.Match;
using Game.Core.Scenes;
using Game.Gameplay.UI;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>Розбір рядка маніфесту кліпів рингу: роль, довжина, петля, зсув B і поворот B (вже в осях Unity).</summary>
    public sealed class RingClipInfo
    {
        public string Name;
        public string Role;
        public float Length;
        public bool Loop;
        /// <summary>Зсув B у просторі A, м (Unity: x вправо від A, z вперед).</summary>
        public Vector3 Offset;
        /// <summary>Поворот B відносно A, градуси (Blender, проти годинникової зверху; в Unity курс B = курс A мінус це).</summary>
        public float Rotation;
    }

    /// <summary>
    /// 3D-ринг під UI «Рингу» (ітерація 1): арена-спортзал, двоє борців і рефері, камера. Усе будується в рантаймі з
    /// ассетів, які поклав збирач сцени (<see cref="RingStageAssets"/>). Матч веде <see cref="RingMatchDirector"/>
    /// (кліпи на події рушія), промо — тут (двоє обличчям одне до одного, мікрофон, камера діалогу на
    /// <see cref="DialogueDirector"/> — та сама режисура, що й у BG3-діалогу села).
    /// Бюджет: одна арена (~89 тис. трикутників) + три постаті по ~3 тис.; тіні лише від одного світла.
    /// </summary>
    public sealed class RingStage3D : MonoBehaviour, IRingStage
    {
        // ---------- геометрія рингу (м, Unity: Y угору, центр рингу — початок) ----------
        public const float MatY = 1.0f;
        public const float RingHalf = 2.038f;

        public const float PlaySpeed = 1.2f;

        public RingStageAssets assets;

        // ---------- камера ----------
        private Camera _cam;
        private Vector3 _camPos;
        private Quaternion _camRot;
        private float _camFov = 34f;
        private bool _camInit;
        private StageCam _shot = StageCam.Overview;
        private float _shotAge;
        private const float CamRate = 3.2f;
        /// <summary>Частка половини висоти кадру, на яку ринг піднято над центром (внизу — панель дій).</summary>
        private float _frameShift = 0.27f;

        // ---------- постаті ----------
        private RingActor _player, _rival, _ref;
        private GameObject _mic;
        private RingActor _micHolder;
        private readonly Dictionary<string, RingClipInfo> _info = new Dictionary<string, RingClipInfo>();
        private readonly Dictionary<string, AnimationClip> _clips = new Dictionary<string, AnimationClip>();
        private RingMatchDirector _director;
        private RingStageMode _mode = RingStageMode.Idle;
        private Material _litCache;

        // ---------- промо ----------
        private string _promoSpeaker;
        private string _lastPromoSpeaker;
        private int _promoSameCount;
        private DialogueCameraPose _promoPose;
        private bool _hasPromoPose;
        private float _promoPoseAge;
        private bool _promoWide;

        private enum RingStageMode { Idle = 0, Match = 1, Promo = 2 }

        public bool IsBusy { get { return _director != null && _director.IsBusy; } }

        public static string ReadArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        // ============================ запуск ============================

        private void Awake()
        {
            if (assets == null) assets = GetComponent<RingStageAssets>();
            if (assets == null || assets.venue == null)
            {
                Debug.LogError("[Ринг] 3D: немає RingStageAssets — сцена без ринга.");
                enabled = false;
                return;
            }
            LoadClips();
            BuildArena();
            BuildCamera();
            BuildLights();
            BuildActors();
            _director = gameObject.AddComponent<RingMatchDirector>();
            _director.Init(this);
            RingStageHub.Current = this;
            ShowIdle();
            SnapCamera();
            string demo = ReadArg("-ring-demo");
            if (demo != null) StartCoroutine(RingStageDemo.Run(this, _director, demo));
        }

        private void OnDestroy()
        {
            if (RingStageHub.Current == (IRingStage)this) RingStageHub.Current = null;
            if (_player != null) _player.Destroy();
            if (_rival != null) _rival.Destroy();
            if (_ref != null) _ref.Destroy();
        }

        private void LoadClips()
        {
            foreach (var c in assets.clips)
            {
                if (c == null || c.name.StartsWith("__preview__")) continue;
                _clips[c.name] = c;
            }
            ParseManifest(assets.manifest);
            ParseManifest(assets.promoManifest);
            Debug.Log("[Ринг] 3D: кліпів " + _clips.Count + ", у маніфесті " + _info.Count);
        }

        private void ParseManifest(TextAsset t)
        {
            if (t == null) return;
            foreach (var raw in t.text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 9) continue;
                var ci = new RingClipInfo
                {
                    Name = p[0],
                    Role = p[1],
                    Length = F(p[2]),
                    Loop = p[3] == "1",
                    // Blender: A дивиться в −Y, праворуч від A — −X. Unity: вперед +Z, вправо +X: (x,y) -> (-x, -y).
                    Offset = new Vector3(-F(p[4]), 0f, -F(p[5])),
                    Rotation = F(p[7])
                };
                _info[ci.Name] = ci;
            }
        }

        private static float F(string s)
        {
            return float.Parse(s, CultureInfo.InvariantCulture);
        }

        public RingClipInfo Info(string clip)
        {
            RingClipInfo ci;
            return _info.TryGetValue(clip, out ci) ? ci : null;
        }

        public AnimationClip Clip(string name)
        {
            AnimationClip c;
            if (_clips.TryGetValue(name, out c)) return c;
            Debug.LogWarning("[Ринг] 3D: немає кліпа " + name);
            return null;
        }

        private void BuildArena()
        {
            var v = Instantiate(assets.venue, transform);
            v.name = "Venue";
            v.transform.localPosition = Vector3.zero;
            // FBX арени імпортується без конвертації осей: Unity бачить (x, y_blender, -z_blender). Поворот +90° навколо X дає
            // (x, z_blender, y_blender) — рівно як у Blender, лицьовий бік (hardcam, −Y) дивиться на −Z.
            v.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            v.transform.localScale = Vector3.one;
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in v.GetComponentsInChildren<Renderer>())
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            Debug.Log("[Ринг] 3D: арена bounds center=" + b.center + " size=" + b.size + " scale=" + v.transform.localScale + " children=" + v.transform.childCount);
            foreach (var r in v.GetComponentsInChildren<Renderer>())
            {
                var rb = r.bounds;
                if (Mathf.Abs(rb.center.x) < 2.5f && Mathf.Abs(rb.center.z) < 2.5f && rb.min.y > -0.5f && rb.min.y < 3f && rb.size.x > 1.5f)
                    Debug.Log("[Ринг] 3D: арена біля центру " + r.name + " min=" + rb.min + " max=" + rb.max);
            }
        }

        private void BuildCamera()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                _cam = go.AddComponent<Camera>();
            }
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.03f, 0.03f, 0.04f, 1f);
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 80f;
            _cam.fieldOfView = _camFov;
        }

        private void BuildLights()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.17f, 0.17f, 0.22f);
            RenderSettings.fog = false;

            // Ключове: єдине світло з тінями — зверху-спереду (як прожектори над рингом).
            var key = new GameObject("Key").AddComponent<Light>();
            key.transform.SetParent(transform, false);
            key.type = LightType.Directional;
            key.transform.rotation = Quaternion.Euler(58f, 18f, 0f);
            key.color = new Color(1f, 0.96f, 0.9f);
            key.intensity = 0.55f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.75f;

            // Заповнювальне без тіней: холодне з боку глядачів.
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.transform.SetParent(transform, false);
            fill.type = LightType.Directional;
            fill.transform.rotation = Quaternion.Euler(28f, 200f, 0f);
            fill.color = new Color(0.55f, 0.62f, 0.85f);
            fill.intensity = 0.22f;
            fill.shadows = LightShadows.None;

            // Пляма світла над рингом (без тіней).
            var spot = new GameObject("RingSpot").AddComponent<Light>();
            spot.transform.SetParent(transform, false);
            spot.type = LightType.Spot;
            spot.transform.position = new Vector3(0f, 8f, 0f);
            spot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            spot.spotAngle = 70f;
            spot.range = 14f;
            spot.intensity = 3f;
            spot.color = new Color(1f, 0.95f, 0.85f);
            spot.shadows = LightShadows.None;
        }

        private void BuildActors()
        {
            _player = Spawn("Гравець", assets.bodyPlayer, 0, new Vector3(1f, 1f, 1f), true);
            _rival = Spawn("Молот", assets.bodyRival, 1, new Vector3(1.1f, 1.05f, 1.1f), false);
            _ref = Spawn("Рефері", assets.bodyRef, 2, Vector3.one, false);
            if (assets.mic != null)
            {
                _mic = Instantiate(assets.mic, transform);
                _mic.name = "Mic";
                foreach (var c in _mic.GetComponentsInChildren<Collider>()) Destroy(c);
                _mic.SetActive(false);
            }
        }

        private RingActor Spawn(string name, GameObject prefab, int texIndex, Vector3 scale, bool tint)
        {
            var a = new RingActor(name, prefab, transform);
            a.Model.transform.localScale = scale;
            var mat = MakeMaterial(texIndex, tint);
            foreach (var r in a.Model.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
                // Межі скін-меша стоять у бінд-позі набору (авторовані осторонь), а кліпи рингу запечені з абсолютним
                // зсувом: без цього постать відсікається камерою, щойно її таз відходить від старих меж.
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) smr.updateWhenOffscreen = true;
            }
            return a;
        }

        private Material MakeMaterial(int i, bool tint)
        {
            Material template = assets.wrestlerTemplate;
            var m = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Texture2D albedo = assets.albedo[i];
            if (tint && albedo != null && assets.mask[i] != null)
            {
                var t = RingTint.TeamTint(albedo, assets.mask[i], 0.60f);
                if (t != null) albedo = t;
            }
            if (albedo != null) { m.SetTexture("_BaseMap", albedo); m.SetTexture("_MainTex", albedo); }
            m.SetColor("_BaseColor", Color.white);
            if (assets.normal[i] != null)
            {
                m.SetTexture("_BumpMap", assets.normal[i]);
                m.EnableKeyword("_NORMALMAP");
            }
            m.SetFloat("_Smoothness", 0.22f);
            m.SetFloat("_Metallic", 0f);
            return m;
        }

        // ============================ місця ============================

        public RingActor Actor(MatchSide s) { return s == MatchSide.Player ? _player : _rival; }
        public RingActor Referee { get { return _ref; } }

        /// <summary>Канонічне місце борця в зоні (світ). Центр — навпроти суперника, канати й кут — біля дальнього боку, поза рингом — на підлозі.</summary>
        public static Vector3 ZonePos(MatchSide s, Zone z)
        {
            float sx = s == MatchSide.Player ? -1f : 1f;
            switch (z)
            {
                case Zone.Ropes: return new Vector3(sx * 0.9f, MatY, 1.5f);
                case Zone.Corner: return new Vector3(sx * 1.5f, MatY, 1.5f);
                case Zone.Outside: return new Vector3(sx * 3.55f, 0f, -0.2f);
                default: return new Vector3(sx * 0.85f, MatY, 0f);
            }
        }

        public static Vector3 RefPos { get { return new Vector3(0f, MatY, 1.3f); } }

        public static float YawTo(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return 0f;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        /// <summary>Поставити постать прямо (без ковзання) і зіграти кліп.</summary>
        public void Place(RingActor a, Vector3 pos, float yaw, string clip, bool loop = true)
        {
            a.Slot.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var c = Clip(clip);
            if (c != null) a.PlayAt(c, loop, pos, yaw);
        }

        // ============================ режими ============================

        public void ShowIdle()
        {
            _mode = RingStageMode.Idle;
            if (_director != null) _director.EndMatch();
            HideMic();
            _ref.Model.SetActive(true);
            Place(_player, ZonePos(MatchSide.Player, Zone.Center), 90f, "Ring_Stance_Loop");
            Place(_rival, ZonePos(MatchSide.Rival, Zone.Center), -90f, "Ring_Stance_Loop");
            Place(_ref, RefPos, 180f, "Ref_Idle_Loop");
            SetShot(StageCam.Overview);
            var rr = _player.Model.GetComponentsInChildren<Renderer>();
            Debug.Log("[Ринг] 3D: гравець slot=" + _player.Slot.position + " renderers=" + rr.Length + (rr.Length > 0 ? " bounds=" + rr[0].bounds + " vis=" + rr[0].isVisible : "") + " scale=" + _player.Model.transform.lossyScale);
        }

        public void BeginMatch(MatchEngine engine)
        {
            _mode = RingStageMode.Match;
            HideMic();
            _ref.Model.SetActive(true);
            _director.Begin(engine);
            SetShot(StageCam.Overview);
        }

        /// <summary>Демо-режим (-ring-demo): матч без рушія, події подає RingStageDemo.</summary>
        public RingMatchDirector BeginDemoMatch()
        {
            _mode = RingStageMode.Match;
            HideMic();
            _ref.Model.SetActive(true);
            _director.BeginDemo();
            SetShot(StageCam.Overview);
            return _director;
        }

        public void EndMatch()
        {
            if (_mode == RingStageMode.Match) ShowIdle();
        }

        public void BeginPromo()
        {
            _mode = RingStageMode.Promo;
            if (_director != null) _director.EndMatch();
            _ref.Model.SetActive(false);
            Place(_player, new Vector3(-1.1f, MatY, 0f), 90f, "Ring_Stance_Loop");
            Place(_rival, new Vector3(1.1f, MatY, 0f), -90f, "Ring_Stance_Loop");
            _promoSpeaker = null;
            _lastPromoSpeaker = null;
            _promoSameCount = 0;
            _hasPromoPose = false;
            HideMic();
            // Перший план промо — двоє в кадрі, поки ніхто не заговорив.
            _promoWide = false;
            ComputePromoShot(DialogueShotKind.Two, "player", "rival", 0);
        }

        public void SetPromoSpeaker(string speaker)
        {
            if (_mode != RingStageMode.Promo || string.IsNullOrEmpty(speaker)) return;
            if (speaker == _promoSpeaker) return;
            _promoSpeaker = speaker;
            bool player = speaker == "player", rival = speaker == "rival";

            // Мікрофон — у того, хто з рингу говорить; решта (оповідач, букер, ветеран, натовп) — без нього.
            var talker = player ? _player : rival ? _rival : null;
            var listener = player ? _rival : rival ? _player : null;
            var idle = Clip("Ring_Stance_Loop");
            var talk = Clip("Promo_Mic_Talk_Loop");
            if (talker != null)
            {
                if (talk != null) talker.PlayHere(talk, true);
                if (idle != null && listener != null) listener.PlayHere(idle, true);
                AttachMic(talker);
            }
            else
            {
                if (idle != null) { _player.PlayHere(idle, true); _rival.PlayHere(idle, true); }
                HideMic();
            }

            // Камера — режисура діалогу села (DialogueDirector): план і бік за правилом 180°.
            if (talker == null)
            {
                _promoWide = true;
                ComputePromoShot(DialogueShotKind.Wide, "player", "rival", 0);
            }
            else
            {
                _promoWide = false;
                string other = player ? "rival" : "player";
                _promoSameCount = speaker == _lastPromoSpeaker ? _promoSameCount + 1 : 0;
                var shot = DialogueDirector.Plan(ShotFraming.Two, "player", "rival", speaker, "player");
                var kind = shot != null ? shot.Kind : DialogueShotKind.OverShoulder;
                int variant = 0;
                if (_promoSameCount >= 1 && _promoSameCount % 2 == 1) { kind = DialogueShotKind.Close; variant = (_promoSameCount / 2) % 2; }
                ComputePromoShot(kind, speaker, other, variant);
            }
            _lastPromoSpeaker = speaker;
        }

        private DialogueShotKind _promoKind;
        private string _promoSubject, _promoOther;
        private int _promoVariant;

        private void ComputePromoShot(DialogueShotKind kind, string subjectId, string otherId, int variant)
        {
            _promoKind = kind; _promoSubject = subjectId; _promoOther = otherId; _promoVariant = variant;
            _hasPromoPose = true;
            _promoPoseAge = 0f;
        }

        /// <summary>Поза камери діалогу від живих голів (вони дихають і рухаються, тож перераховуємо щокадру).</summary>
        private void RecomputePromoPose()
        {
            var subject = _promoSubject == "rival" ? _rival : _player;
            var other = _promoOther == "rival" ? _rival : _player;
            // Вісь сцени: суперник → герой, тоді «ліворуч» — південь (бік глядачів), як камера матчу.
            _promoPose = DialogueDirector.Pose(_promoKind, ToD(subject.HeadPos), ToD(other.HeadPos), ToD(_rival.HeadPos), ToD(_player.HeadPos), 1.75f, _promoVariant);
        }

        private static DialogueVec ToD(Vector3 v) { return new DialogueVec(v.x, v.y, v.z); }
        private static Vector3 ToV(DialogueVec v) { return new Vector3(v.X, v.Y, v.Z); }

        private void AttachMic(RingActor holder)
        {
            if (_mic == null || holder == null || holder.RightHand == null) return;
            _mic.SetActive(true);
            _mic.transform.SetParent(holder.RightHand, false);
            // Кріплення з маніфесту промо (простір кістки Blender: початок — центр рукояті).
            _mic.transform.localPosition = new Vector3(-0.0053f, 0.0510f, -0.0086f);
            _mic.transform.localRotation = new Quaternion(0.26931f, 0.31194f, 0.74840f, 0.51968f);
            _mic.transform.localScale = Vector3.one;
            _micHolder = holder;
        }

        private void HideMic()
        {
            if (_mic == null) return;
            _mic.transform.SetParent(transform, false);
            _mic.SetActive(false);
            _micHolder = null;
        }

        // ============================ камера ============================

        public void SetShot(StageCam cam)
        {
            if (_shot != cam) _shotAge = 0f;
            _shot = cam;
        }

        private void SnapCamera() { _camInit = false; }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (_player != null) _player.Tick(Time.deltaTime);
            if (_rival != null) _rival.Tick(Time.deltaTime);
            if (_ref != null) _ref.Tick(Time.deltaTime);
            if (_cam == null) return;
            _shotAge += dt;

            Vector3 pos, look;
            float fov;
            if (_mode == RingStageMode.Promo && _hasPromoPose)
            {
                _promoPoseAge += dt;
                RecomputePromoPose();
                // Двоє в кадрі: голови тримають камеру (вони дихають), тож позу перераховуємо щокадру від живих голів.
                var p = _promoPose;
                pos = ToV(DialogueDirector.PushIn(p, _promoPoseAge / 7f));
                look = ToV(p.LookAt);
                fov = p.Fov;
            }
            else
            {
                Vector3 mid = (_player.PosePos + _rival.PosePos) * 0.5f;
                switch (_shot)
                {
                    case StageCam.Close:
                        look = mid + new Vector3(0f, 1.35f, 0f);
                        pos = look + new Vector3(1.0f, 1.3f, -6.2f);
                        fov = 34f;
                        break;
                    case StageCam.Big:
                        look = mid + new Vector3(0f, 1.2f, 0f);
                        pos = look + new Vector3(-2.4f, 1.1f, -5.2f);
                        fov = 32f;
                        break;
                    default:
                        look = new Vector3(0f, 1.2f, 0.2f);
                        pos = new Vector3(0f, 6.4f, -8.2f);
                        fov = 33f;
                        break;
                }
            }

            // Ринг підняти над центром кадру: знизу панель дій (UI).
            Vector3 fwd = (look - pos).normalized;
            Vector3 up = Vector3.Cross(Vector3.Cross(fwd, Vector3.up), fwd).normalized;
            float halfH = (look - pos).magnitude * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            Vector3 aim = look - up * (halfH * _frameShift);
            Quaternion rot = Quaternion.LookRotation(aim - pos, Vector3.up);

            if (!_camInit)
            {
                _camPos = pos; _camRot = rot; _camFov = fov; _camInit = true;
            }
            else
            {
                float k = 1f - Mathf.Exp(-CamRate * dt);
                _camPos = Vector3.Lerp(_camPos, pos, k);
                _camRot = Quaternion.Slerp(_camRot, rot, k);
                _camFov = Mathf.Lerp(_camFov, fov, k);
            }
            _cam.transform.SetPositionAndRotation(_camPos, _camRot);
            _cam.fieldOfView = _camFov;
        }

        public bool InDemo { get; set; }
    }
}
