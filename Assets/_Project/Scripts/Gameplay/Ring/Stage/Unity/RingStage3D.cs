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
        private RingRopes _ropes;
        private RingVenueDress _dress;
        private float _budgetAt;
        private int _budgetShots;
        private StageCam _budgetShot = (StageCam)(-1);

        // ---------- промо ----------
        private string _promoSpeaker;
        private string _lastPromoSpeaker;
        private int _promoSameCount;
        private DialogueCameraPose _promoPose;
        private bool _hasPromoPose;
        private float _promoPoseAge;
        private bool _promoWide;

        private enum RingStageMode { Idle = 0, Match = 1, Promo = 2 }

        public bool IsBusy { get { return (_director != null && _director.IsBusy) || (_show != null && _show.Busy); } }

        private RingShow _show;
        private RingSound _sound;
        public RingSound Sound { get { return _sound; } }
        public RingShow Show { get { return _show; } }

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
            BuildDress();
            _director = gameObject.AddComponent<RingMatchDirector>();
            _director.Init(this);
            _show = gameObject.AddComponent<RingShow>();
            _show.Init(this);
            _sound = gameObject.AddComponent<RingSound>();
            _sound.Init(assets);
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
            _cam.backgroundColor = new Color(0.012f, 0.012f, 0.02f, 1f);
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 70f;
            _cam.fieldOfView = _camFov;
            _cam.allowHDR = true;
        }

        /// <summary>
        /// Світло матчу за сценою «match» з venue_*_cues: ринг яскравий і теплий (прожектори над ним), зала темніша
        /// (низький амбієнт, туман, віньєтка в пост-обробці), з глядацького боку - холодний підсвіт, збоку - тепле
        /// контр-світло. Єдине світло з тінями - ключове; решта приладів без тіней (PERF-01: дешево на слабких машинах).
        /// </summary>
        private void BuildLights()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.050f, 0.054f, 0.075f);
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0.12f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.016f, 0.017f, 0.028f);
            RenderSettings.fogDensity = 0.034f;

            // Ключове: одне світло з тінями - зверху-спереду, тепле (прожектори над рингом).
            var key = new GameObject("Key").AddComponent<Light>();
            key.transform.SetParent(transform, false);
            key.type = LightType.Directional;
            key.transform.rotation = Quaternion.Euler(62f, 14f, 0f);
            key.color = new Color(1f, 0.90f, 0.76f);
            key.intensity = 0.60f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.82f;
            key.shadowBias = 0.03f;
            key.shadowNormalBias = 0.4f;

            // Чотири прожектори над кутами рингу (PAR): тепле біле, пляма на полотні й борцях, зала лишається темнішою.
            Vector3[] par = { new Vector3(-2.6f, 6.4f, -2.6f), new Vector3(2.6f, 6.4f, -2.6f), new Vector3(-2.6f, 6.4f, 2.6f), new Vector3(2.6f, 6.4f, 2.6f) };
            for (int i = 0; i < par.Length; i++)
                AddSpot("PAR" + i, par[i], new Vector3(0f, 1.05f, 0f), new Color(1f, 0.88f, 0.70f), 22f, 46f);

            // Холодний підсвіт з боку глядачів і тепле контр-світло: відділяють борців від тла.
            // М'яке фронтальне заповнення з боку камери: канати й боки борців не чорніють.
            AddSpot("Fill_front", new Vector3(0f, 3.4f, -7.5f), new Vector3(0f, 1.6f, 0f), new Color(0.86f, 0.90f, 1f), 14f, 52f);
            AddSpot("Rim_cool", new Vector3(-6.5f, 5.2f, 5.5f), new Vector3(0f, 1.4f, 0f), new Color(0.40f, 0.55f, 1f), 30f, 44f);
            AddSpot("Rim_warm", new Vector3(6.5f, 5.2f, 5.5f), new Vector3(0f, 1.4f, 0f), new Color(1f, 0.52f, 0.30f), 24f, 44f);
        }

        private void AddSpot(string name, Vector3 pos, Vector3 target, Color c, float intensity, float angle)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.type = LightType.Spot;
            l.transform.position = pos;
            l.transform.rotation = Quaternion.LookRotation(target - pos, Vector3.up);
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.5f;
            l.range = 18f;
            l.intensity = intensity;
            l.color = c;
            l.shadows = LightShadows.None;
        }

        /// <summary>Канати, стільці й натовп. Відвідуваність - параметр (-ring-crowd N у командному рядку).</summary>
        private void BuildDress()
        {
            var actors = new[] { _player, _rival, _ref };
            var ropeGo = new GameObject("Ropes");
            ropeGo.transform.SetParent(transform, false);
            _ropes = ropeGo.AddComponent<RingRopes>();
            _ropes.Build(assets.venueAnchors, assets.wrestlerTemplate, actors);

            var dressGo = new GameObject("VenueDress");
            dressGo.transform.SetParent(transform, false);
            _dress = dressGo.AddComponent<RingVenueDress>();
            string n = ReadArg("-ring-crowd");
            if (n != null) { int v; if (int.TryParse(n, out v)) _dress.attendance = Mathf.Clamp(v, 0, 200); }
            _dress.BuildChairs(assets.props, assets.venueProps, assets.wrestlerTemplate);
            if (_dress.attendance > 0) _dress.BuildCrowd(assets.crowd, assets.venueCrowd, assets.crowdClips, assets.crowdAvatar);
        }

        private void BuildActors()
        {
            _player = Spawn("Гравець", assets.bodyPlayer, 0, new Vector3(1f, 1f, 1f), TintPlayer);
            _rival = Spawn("Молот", assets.bodyRival, 1, new Vector3(1.1f, 1.05f, 1.1f), TintRival);
            _ref = Spawn("Рефері", assets.bodyRef, 2, Vector3.one, 0);
            // Справжній одяг з набору замість фарби форми на голому тілі, зачіски замість лисин (власник 03.11.2026:
            // «Суддя виглядає як слендер мен»). Борці лишаються в трусах з текстури — їм лише волосся.
            Color black = new Color(0.06f, 0.06f, 0.065f);
            RingDress.Apply(_ref.Model, assets.kitMale, new[]
            {
                new RingDress.Piece("shirt", Color.white, true),
                new RingDress.Piece("trousers", black),
                new RingDress.Piece("shoes", black),
                new RingDress.Piece("hair_short02", new Color(0.16f, 0.11f, 0.08f)),
            });
            RingDress.Apply(_player.Model, assets.kitMale, new[] { new RingDress.Piece("hair_short01", new Color(0.12f, 0.08f, 0.05f)) });
            RingDress.Apply(_rival.Model, assets.kitMale, new[]
            {
                new RingDress.Piece("hair_short04", new Color(0.55f, 0.42f, 0.26f)),
                new RingDress.Piece("beard_short", new Color(0.5f, 0.38f, 0.24f)),
            });
            if (assets.mic != null)
            {
                _mic = Instantiate(assets.mic, transform);
                _mic.name = "Mic";
                foreach (var c in _mic.GetComponentsInChildren<Collider>()) Destroy(c);
                _mic.SetActive(false);
            }
        }

        // Гравець - синій одяг, Молот - темно-червоний; шкіра лишається зі своєї текстури (маска тонує лише зони одягу),
        // з м'яким підбором відтінку: світла скандинавська шкіра Молота без рожевого (трохи засмаги й рум'янцю).
        private const int TintPlayer = 1, TintRival = 2;

        private RingActor Spawn(string name, GameObject prefab, int texIndex, Vector3 scale, int tint)
        {
            return SpawnWith(name, prefab, scale, MakeMaterial(texIndex, tint));
        }

        private RingActor SpawnWith(string name, GameObject prefab, Vector3 scale, Material mat)
        {
            var a = new RingActor(name, prefab, transform);
            a.Model.transform.localScale = scale;
            foreach (var r in a.Model.GetComponentsInChildren<Renderer>())
            {
                // Очі, брови й вії мають свої матеріали: під текстурою тіла обличчя ставало порожнім манекеном.
                if (Characters.CharacterAssembler.IsFacePart(Characters.CharacterAssembler.PartOf(r.name)))
                {
                    var face = r as SkinnedMeshRenderer;
                    if (face != null) face.updateWhenOffscreen = true;
                    // Білки очей у темній залі світилися (знімок промо 03.11.2026) — приглушити, не прибираючи текстуру.
                    if (Characters.CharacterAssembler.PartOf(r.name).ToLowerInvariant() == "low-poly")
                    {
                        var block = new MaterialPropertyBlock();
                        block.SetColor("_BaseColor", new Color(0.62f, 0.6f, 0.58f));
                        r.SetPropertyBlock(block);
                    }
                    continue;
                }
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

        private Material MakeMaterial(int i, int tint)
        {
            Texture2D albedo = assets.albedo[i];
            if (tint != 0 && albedo != null && assets.mask[i] != null)
            {
                Texture2D t = tint == TintPlayer
                    ? RingTint.Grade(albedo, assets.mask[i], 0.60f, 1.35f, new Color(0.98f, 0.90f, 0.84f))
                    : RingTint.Grade(albedo, assets.mask[i], 0.995f, 1.05f, new Color(0.94f, 0.80f, 0.74f));
                if (t != null) albedo = t;
            }
            return MaterialFrom(albedo, assets.normal[i]);
        }

        private Material MaterialFrom(Texture2D albedo, Texture2D normal)
        {
            Material template = assets.wrestlerTemplate;
            var m = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (albedo != null) { m.SetTexture("_BaseMap", albedo); m.SetTexture("_MainTex", albedo); }
            m.SetColor("_BaseColor", Color.white);
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
            }
            m.SetFloat("_Smoothness", 0.16f);
            m.SetFloat("_Metallic", 0f);
            return m;
        }

        // ============================ хто на рингу ============================

        private string _lookPlayer, _lookRival;

        /// <summary>
        /// Зовнішність борців під кар'єру: тіло набору (<c>body_m_latin</c>…), колір екіпіровки (0xRRGGBB), зачіска (ключ
        /// частини набору або порожньо), масштаб суперника. Та сама зовнішність — нічого не перебудовується.
        /// </summary>
        public void SetWrestlers(string playerBody, int playerRgb, string playerHair, string rivalBody, int rivalRgb, string rivalHair, float rivalScale)
        {
            string lp = playerBody + "|" + playerRgb + "|" + playerHair, lr = rivalBody + "|" + rivalRgb + "|" + rivalHair + "|" + rivalScale;
            bool changed = false;
            if (lp != _lookPlayer)
            {
                var a = Rebuild("Гравець", playerBody, playerRgb, playerHair, 1f);
                if (a != null) { if (_player != null) Kill(_player); _player = a; _lookPlayer = lp; changed = true; }
            }
            if (lr != _lookRival)
            {
                var a = Rebuild("Суперник", rivalBody, rivalRgb, rivalHair, rivalScale);
                if (a != null) { if (_rival != null) Kill(_rival); _rival = a; _lookRival = lr; changed = true; }
            }
            if (!changed) return;
            HideMic();
            if (_ropes != null) _ropes.SetActors(new[] { _player, _rival, _ref });
            if (_mode == RingStageMode.Idle && (_show == null || !_show.Busy)) ShowIdle();
        }

        private void Kill(RingActor a)
        {
            a.Destroy();
            if (a.Slot != null) Destroy(a.Slot.gameObject);
        }

        private RingActor Rebuild(string name, string body, int rgb, string hair, float scale)
        {
            int i = Array.FindIndex(assets.bodies, b => b != null && b.name == body);
            if (i < 0) { Debug.LogWarning("[Ринг] 3D: немає тіла " + body); return null; }
            Texture2D albedo = assets.bodyAlbedo[i];
            var c = new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
            float h, sat, v;
            Color.RGBToHSV(c, out h, out sat, out v);
            if (albedo != null && assets.bodyMask[i] != null)
            {
                var t = RingTint.Grade(albedo, assets.bodyMask[i], h, 0.8f + v * 0.7f, new Color(0.97f, 0.9f, 0.84f));
                if (t != null) albedo = t;
            }
            float hgt = 1f + (scale - 1f) * 0.5f;
            var a = SpawnWith(name, assets.bodies[i], new Vector3(scale, hgt, scale), MaterialFrom(albedo, assets.bodyNormal[i]));
            bool female = body.StartsWith("body_f_");
            if (!string.IsNullOrEmpty(hair))
                RingDress.Apply(a.Model, female ? assets.kitFemale : assets.kitMale, new[] { new RingDress.Piece(hair, HairColor(body)) });
            return a;
        }

        private static Color HairColor(string body)
        {
            if (body.Contains("nordic")) return new Color(0.55f, 0.42f, 0.26f);
            if (body.Contains("ukrainian")) return new Color(0.2f, 0.13f, 0.08f);
            if (body.Contains("mediterranean") || body.Contains("latin")) return new Color(0.1f, 0.07f, 0.05f);
            return new Color(0.04f, 0.035f, 0.03f);
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

        public static Vector3 RefPos { get { return new Vector3(0f, MatY, 1.5f); } }

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
            LeaveShow();
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
            LeaveShow();
            _mode = RingStageMode.Match;
            HideMic();
            _ref.Model.SetActive(true);
            _director.Begin(engine);
            CrowdMood(engine != null ? engine.State.Hype : 0);
            if (_sound != null) { _sound.MusicOff(); if (engine != null && engine.State.Log.Count <= 1) _sound.BellStart(); }
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
            LeaveShow();
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
            // Мікрофон завжди в руці - від першої секунди в гравця; далі переходить до того, хто говорить.
            AttachMic(_player);
            _promoWide = true;
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
                // Оповідач, публіка, букер: обидва стоять, мікрофон лишається в руці того, хто говорив останнім.
                if (idle != null) { _player.PlayHere(idle, true); _rival.PlayHere(idle, true); }
                if (_micHolder == null) AttachMic(_player);
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
            _mic.transform.localScale = Vector3.one * 1.45f;      // справжній мікрофон ~20 см: на загальному плані видно лише збільшений
            _micHolder = holder;
        }

        private void HideMic()
        {
            if (_mic == null) return;
            _mic.transform.SetParent(transform, false);
            _mic.SetActive(false);
            _micHolder = null;
        }

        // ============================ шоу: вступ, вихід, закулісся ============================

        private void LeaveShow()
        {
            if (_show == null) return;
            _show.LeaveBackstage();
            if (_show.Busy) _show.Skip();
        }

        public void BeginIntro(string title, string subtitle)
        {
            _mode = RingStageMode.Idle;
            if (_director != null) _director.EndMatch();
            HideMic();
            _show.LeaveBackstage();
            _show.BeginIntro(title, subtitle);
            if (_sound != null) { _sound.TitleMusic(); _sound.Crowd(0); }
        }

        public void BeginEntrance(bool player, string title, string subtitle, int rgb)
        {
            _mode = RingStageMode.Idle;
            if (_director != null) _director.EndMatch();
            HideMic();
            _show.LeaveBackstage();
            var c = new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
            _show.BeginEntrance(player ? _player : _rival, title, subtitle, c);
            if (_sound != null) { _sound.Crowd(1); _sound.EntranceMusic(player, !player && _lookRival != null && _lookRival.StartsWith("body_m_nordic")); }
        }

        /// <summary>Настрій залу за ажіотажем матчу (0…10): холодний, оплески, крики, кипить.</summary>
        public void CrowdMood(int hype)
        {
            if (_dress == null) return;
            int mood = hype <= 2 ? 0 : hype <= 5 ? 1 : hype <= 8 ? 2 : 3;
            _dress.SetMood(mood, false);
            if (_sound != null) _sound.Crowd(mood);
        }

        public void CrowdPop() { if (_dress != null) _dress.Pop(); if (_sound != null) _sound.Pop(); }

        public bool ShowBusy { get { return _show != null && _show.Busy; } }

        public void SkipShow() { if (_show != null) _show.Skip(); }

        public void ShowBackstage()
        {
            _mode = RingStageMode.Idle;
            if (_director != null) _director.EndMatch();
            HideMic();
            _show.ShowBackstage(assets.bodyRef, assets.kitMale);
            if (_sound != null) { _sound.MusicOff(); _sound.Crowd(-1); }
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
            if (_show != null) _show.TickBooker(Time.deltaTime);
            if (_cam == null) return;
            _shotAge += dt;

            Vector3 pos, look;
            float fov;
            bool show = _show != null && _show.CamActive;
            if (show)
            {
                pos = _show.CamPos;
                look = _show.CamLook;
                fov = _show.CamFov;
                if (_show.CamCut) { _camInit = false; _show.CamCut = false; }
            }
            else if (_mode == RingStageMode.Promo && _hasPromoPose)
            {
                _promoPoseAge += dt;
                RecomputePromoPose();
                // Двоє в кадрі: голови тримають камеру (вони дихають), тож позу перераховуємо щокадру від живих голів.
                var p = _promoPose;
                pos = ToV(DialogueDirector.PushIn(p, _promoPoseAge / 7f));
                look = ToV(p.LookAt);
                fov = p.Fov;
                if (_promoWide)
                {
                    // Загальний план промо: двоє в кадрі по пояс-повний зріст, камера з боку глядачів, трохи збоку й зверху.
                    Vector3 m = (_player.HeadPos + _rival.HeadPos) * 0.5f;
                    look = new Vector3(m.x, MatY + 1.1f, m.z);
                    pos = look + new Vector3(1.1f, 1.4f, -5.0f);
                    fov = 32f;
                }
            }
            else
            {
                Vector3 mid = (_player.PosePos + _rival.PosePos) * 0.5f;
                switch (_shot)
                {
                    case StageCam.Close:
                        // Обидва борці в кадрі приблизно по пояс і вище, без обрізання голів; рефері лишається збоку.
                        look = mid + new Vector3(0f, 1.25f, 0f);
                        pos = look + new Vector3(1.3f, 1.5f, -7.4f);
                        fov = 33f;
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
            Vector3 aim = look - up * (halfH * (show ? 0f : _frameShift));
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
            LogBudget(dt);
        }

        /// <summary>
        /// Бюджет кадру для Android (Статут PERF-01): трикутники видимих сіток і кількість викликів малювання (оцінка:
        /// видимі рендерери x підсітки; тіні ключового світла не враховані). Рядок «[Ринг] бюджет» - у Player.log на
        /// старті й коли міняється ракурс (раз на ракурс, через пів секунди після зміни).
        /// </summary>
        private void LogBudget(float dt)
        {
            _budgetAt += dt;
            bool changed = _shot != _budgetShot || (_mode == RingStageMode.Promo && _budgetShots == 0);
            if (_budgetShots == 0 && _budgetAt > 1.2f) changed = true;
            if (!changed || _budgetAt < 0.8f) return;
            if (_budgetShots > 14) return;
            _budgetShot = _shot;
            _budgetShots++;
            _budgetAt = 0f;
            long tris = 0; int calls = 0, rends = 0;
            var byCat = new System.Collections.Generic.Dictionary<string, long[]>();
            var passes = new System.Collections.Generic.HashSet<string>();
            var matSet = new System.Collections.Generic.HashSet<int>();
            int lodded = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || !r.isVisible) continue;
                Mesh m = null;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) m = smr.sharedMesh;
                else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; }
                if (m == null) continue;
                rends++;
                int subs = Mathf.Min(m.subMeshCount, Mathf.Max(1, r.sharedMaterials.Length));
                long t0 = tris; int c0 = calls;
                int lod = MeshLodGuess(r, m);
                if (lod > 0) lodded++;
                for (int i = 0; i < subs; i++)
                {
                    tris += (lod > 0 ? m.GetLod(i, lod).indexCount : (int)m.GetIndexCount(i)) / 3; calls++;
                    if (i < r.sharedMaterials.Length && r.sharedMaterials[i] != null) passes.Add(r.sharedMaterials[i].shader.name);
                    if (i < r.sharedMaterials.Length && r.sharedMaterials[i] != null) matSet.Add(r.sharedMaterials[i].GetInstanceID());
                }
                string cat = BudgetCategory(r.transform);
                long[] acc;
                if (!byCat.TryGetValue(cat, out acc)) { acc = new long[2]; byCat[cat] = acc; }
                acc[0] += tris - t0; acc[1] += calls - c0;
            }
            var parts = new System.Text.StringBuilder();
            foreach (var kv in byCat) parts.Append(" | " + kv.Key + " " + kv.Value[0] + "/" + kv.Value[1]);
            if (_budgetShots == 2 && _dress != null) _dress.Diagnose();
            if (_budgetShots == 2 && _dress != null && _dress.FanCount > 4 && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ring-crowd-debug") >= 0)
                _dress.DebugShot(_cam, System.IO.Path.Combine(Application.dataPath, "..", "crowd_debug.png"));
            Debug.Log("[Ринг] бюджет: " + (_mode == RingStageMode.Promo ? "промо" : _shot.ToString()) + " трикутників " + tris + ", рендерерів " + rends + ", викликів ~" + calls
                + ", матеріалів " + matSet.Count + ", шейдерів " + passes.Count + ", на Mesh LOD > 0: " + lodded
                + ", фанатів " + (_dress != null ? _dress.FanCount : 0) + parts);
        }

        /// <summary>
        /// Оцінка рівня Mesh LOD рушія (зали імпортуються з generateMeshLods): площа рендерера на екрані в пікселях і
        /// крива вибору сітки — lodSlope·log2(площа) + lodBias. Це оцінка для рядка бюджету, не те, що вибрав рушій.
        /// </summary>
        private int MeshLodGuess(Renderer r, Mesh m)
        {
            if (m.lodCount <= 1 || _cam == null) return 0;
            float d = Vector3.Distance(_cam.transform.position, r.bounds.center);
            float radius = r.bounds.extents.magnitude;
            float px = radius / Mathf.Max(0.01f, d * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * Screen.height * 0.5f;
            float area = Mathf.PI * px * px;
            var curve = m.lodSelectionCurve;
            int lod = Mathf.RoundToInt(curve.lodSlope * Mathf.Log(Mathf.Max(1f, area), 2f) + curve.lodBias);
            return Mathf.Clamp(lod, 0, m.lodCount - 1);
        }

        /// <summary>Категорія рендерера для рядка бюджету: фанати, стільці або ім'я кореня під сценою.</summary>
        private static string BudgetCategory(Transform t)
        {
            var chain = new System.Collections.Generic.List<Transform>();
            for (var c = t; c != null; c = c.parent)
            {
                if (c.name.StartsWith("fan_")) return "фанати";
                if (c.name.StartsWith("chairs_")) return "стільці";
                chain.Add(c);
            }
            // Корінь сцени і його дитина — завжди спільні; категорія — третій рівень (арена, борець, реквізит).
            Transform top = chain[Mathf.Max(0, chain.Count - 3)];
            return top.name.Length > 18 ? top.name.Substring(0, 18) : top.name;
        }

        public bool InDemo { get; set; }
    }
}
