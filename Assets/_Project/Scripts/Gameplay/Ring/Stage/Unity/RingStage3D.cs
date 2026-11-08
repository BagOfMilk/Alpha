using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Ring.Match;
using Game.Core.Scenes;
using Game.Gameplay.Ring.UI;
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
        /// <summary>Вузол пози в кінці кліпа (stance, lying_back, …; «-» — невідомо).</summary>
        public string EndNode;
        public bool EndsLying { get { return EndNode != null && EndNode.StartsWith("lying"); } }
        public bool EndsStanding { get { return EndNode == "stance" || EndNode == "ref_stand"; } }
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

        // ---------------- відчуття удару: такт, тремтіння камери, час ----------------

        public int BeatSeq { get; private set; }
        public MatchEvent BeatEvent { get; private set; }
        public int BeatPhase { get; private set; }
        public int ShownSeq { get; private set; } = int.MaxValue;

        /// <summary>Мітки подій кліпів (контакт, канат, відлік рефері) — такт режисера матчу.</summary>
        public RingClipEvents Events { get; private set; } = new RingClipEvents();

        /// <summary>Режисер: ця подія показується зараз (контакт; для утримання — «раз», «два», розв'язка).</summary>
        public void Beat(MatchEvent ev, int phase = 0) { BeatEvent = ev; BeatPhase = phase; BeatSeq++; }

        /// <summary>Режисер: подія показана повністю (такт настав) — журнал може її показати.</summary>
        public void Reveal(int seq) { ShownSeq = seq; }

        private readonly HashSet<string> _matchClips = new HashSet<string>();
        private readonly Dictionary<string, float> _motion = new Dictionary<string, float>();

        /// <summary>Зсув кореня кліпа від першого до останнього кадру, одиниці ригу (ring_anims_motion.txt); 0 — невідомо.</summary>
        public float MotionNet(string clip)
        {
            float v;
            return _motion.TryGetValue(clip, out v) ? v : 0f;
        }

        public string PlayedReport()
        {
            Func<string, string> logical = n => n.EndsWith("_A") || n.EndsWith("_B") ? n.Substring(0, n.Length - 2) : n;
            var played = new HashSet<string>();
            foreach (var n in RingActor.Played) played.Add(logical(n));
            int inMatch = 0;
            var runRope = new List<string>();
            foreach (var n in _matchClips)
            {
                if (!played.Contains(n)) continue;
                inMatch++;
                if (n.StartsWith("Run_") || n.StartsWith("Rope_") || n.StartsWith("Whip_") || n.StartsWith("Move_Rope")) runRope.Add(n);
            }
            runRope.Sort(StringComparer.Ordinal);
            return string.Format(CultureInfo.InvariantCulture, "[Ринг] кліпи: зіграно {0} логічних з {1} у маніфесті рингу (усього різних {2}); біг, канати, кидки: {3}",
                inMatch, _matchClips.Count, played.Count, runRope.Count > 0 ? string.Join(", ", runRope) : "—") + "\n" + Watch.Report();
        }

        private float _shakeAmp, _shakeT, _shakeDur;

        /// <summary>
        /// Тремтіння камери (м, с): удар — ледь, фінішер і падіння після кидка — сильніше. Налаштування → Доступність:
        /// «Тремтіння екрана» вимкнено або «Менше руху» — камера не тремтить (ТЗ B01 §7.8).
        /// </summary>
        public void Shake(float amp, float dur)
        {
            if (!Game.Gameplay.Ring.UI.Menus.RingAccessibility.ScreenShake) return;
            if (amp < _shakeAmp * (1f - _shakeT / Mathf.Max(0.01f, _shakeDur))) return;
            _shakeAmp = amp; _shakeDur = dur; _shakeT = 0f;
        }

        private Coroutine _timeCo;

        /// <summary>
        /// Час на мить: стоп-кадр удару (mul ≈ 0,05 на 0,06 с) чи сповільнення фінішера (0,4 на ~0,9 с). Відносно базового
        /// темпу (бот огляду грає на ×3), тривалість — реальні секунди без пауз. Темп — через <see cref="Game.Gameplay.Ring.Models.RingTime"/>: кінець
        /// сповільнення під час паузи гри не відпускає паузу.
        /// </summary>
        public void TimeWarp(float mul, float realSeconds)
        {
            if (_timeCo != null) StopCoroutine(_timeCo);
            _timeCo = StartCoroutine(TimeWarpCo(mul, realSeconds));
        }

        private static void SetWarp(float w)
        {
            Game.Gameplay.Ring.Models.RingTime.Warp = w;
            Time.timeScale = Game.Gameplay.Ring.Models.RingTime.Scale;
        }

        private System.Collections.IEnumerator TimeWarpCo(float mul, float realSeconds)
        {
            SetWarp(mul);
            for (float t = 0f; t < realSeconds; )
            {
                yield return null;
                if (!Game.Gameplay.Ring.Models.RingTime.Paused) t += Time.unscaledDeltaTime;
            }
            // Плавне повернення за 0,15 с — без смикання.
            for (float t = 0f; t < 0.15f; )
            {
                SetWarp(Mathf.Lerp(mul, 1f, t / 0.15f));
                yield return null;
                if (!Game.Gameplay.Ring.Models.RingTime.Paused) t += Time.unscaledDeltaTime;
            }
            SetWarp(1f);
            _timeCo = null;
        }
        /// <summary>Частка половини висоти кадру, на яку ринг піднято над центром (внизу — панель дій).</summary>
        private float _frameShift = 0.27f;

        // ---------- постаті ----------
        private RingActor _player, _rival, _ref;
        private GameObject _mic;
        private RingActor _micHolder;
        private readonly Dictionary<string, RingClipInfo> _info = new Dictionary<string, RingClipInfo>();
        private readonly Dictionary<string, AnimationClip> _clips = new Dictionary<string, AnimationClip>();
        /// <summary>Жіночий набір виходу (RING_Entrance_F.fbx): ті самі імена — окремий словник, щоб не затерти чоловічі.</summary>
        private readonly Dictionary<string, AnimationClip> _clipsF = new Dictionary<string, AnimationClip>();
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
        private RingFx _fx;
        public RingFx Fx { get { return _fx; } }
        public RingShow Show { get { return _show; } }

        /// <summary>Аргумент запуску; у Play Mode редактора — ще й з файла RingInEditor (див. <see cref="RingLaunchArgs"/>).</summary>
        public static string ReadArg(string name) { return RingLaunchArgs.Value(name); }

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
            _fx = gameObject.AddComponent<RingFx>();
            _fx.Init(this, assets.particleAdd, assets.particleAlpha);
            RingStageHub.Current = this;
            ShowIdle();
            SnapCamera();
            string demo = ReadArg("-ring-demo");
            if (demo != null) StartCoroutine(RingStageDemo.Run(this, _director, demo));
        }

        private void OnDestroy()
        {
            if (_timeCo != null) SetWarp(1f);   // сповільнення не переживає сцену
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
            if (assets.clipsFemale != null)
                foreach (var c in assets.clipsFemale)
                    if (c != null && !c.name.StartsWith("__preview__")) _clipsF[c.name] = c;
            ParseManifest(assets.manifest);
            foreach (var k in _info.Keys) _matchClips.Add(k.EndsWith("_A") || k.EndsWith("_B") ? k.Substring(0, k.Length - 2) : k);
            ParseManifest(assets.promoManifest);
            ParseManifest(assets.entranceManifest);
            Events = RingClipEvents.Parse(assets.events != null ? assets.events.text : null);
            Events.Add(assets.eventsF != null ? assets.eventsF.text : null);
            if (assets.motion != null)
                foreach (var raw in assets.motion.text.Split('\n'))
                {
                    var p = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    float dx, dy;
                    if (p.Length < 3 || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out dx)
                        || !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out dy)) continue;
                    _motion[p[0]] = Mathf.Sqrt(dx * dx + dy * dy);
                }
            Debug.Log("[Ринг] 3D: кліпів " + _clips.Count + ", у маніфесті " + _info.Count + ", міток подій " + Events.Count);
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
                    Rotation = F(p[7]),
                    // Колонки 9–10 — вузол пози на початку і в кінці (ring_anims.py). Раніше бралась 9-та (початок), і
                    // «лежить після кліпа» не спрацьовувало: кинутий після Bodyslam ще й продавав падіння.
                    EndNode = p.Length > 9 ? p[9] : p[8]
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

        /// <summary>Кліп для постаті певної статі: жінці — з жіночого набору виходу, якщо він там є; інакше — чоловічий.</summary>
        public AnimationClip Clip(string name, bool female)
        {
            AnimationClip c;
            if (female && name != null && _clipsF.TryGetValue(name, out c)) return c;
            return name != null ? Clip(name) : null;
        }

        /// <summary>Постать — з жіночим тілом набору (body_f_*): виходить жіночими кліпами (ENTRANCE_PROFILE.md, «стать»).</summary>
        public bool IsFemale(RingActor a)
        {
            string look = a == _player ? _lookPlayer : a == _rival ? _lookRival : null;
            return look != null && look.StartsWith("body_f_", StringComparison.Ordinal);
        }

        internal RingMatchDirector Director { get { return _director; } }

        /// <summary>Скасувати відкладений звук удару виходу (тема, гонг, оплески) — вихід закінчився чи пропущений.</summary>
        internal void CancelEntranceSound() { if (_sound != null) _sound.CancelEntranceHit(); }

        private void BuildArena()
        {
            var v = Instantiate(assets.venue, transform);
            v.name = "Venue";
            // Kit_* — колір вершин × сіра карта набору (у Unity були світло-сірими без текстури: плейтест 08.11.2026, P3).
            RingVenueKit.Apply(v, null, assets.kitSets, assets.kitAlbedo, assets.kitNormal, assets.kitTile);
            RingVenueKit.TameEmission(v, "Fridge_Inner", 0.18f);   // білий «!» за рингом у промо
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
                RingVenueKit.DropSubmesh(_mic, "Mic_Cable");   // бездротовий: кабель був жорстким стрижнем до підлоги
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
            if (_mode == RingStageMode.Idle && (_show == null || !_show.Busy))
            {
                if (_heroOnly) HeroAlone(_heroGood, _heroCreate, true);
                else ShowIdle();
            }
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
            ShowAll();
            // Світло зали — робоче: холодне світло поразки лишалося на картці й промо наступного тижня (плейтест 08.11.2026, P3:
            // «картка шоу тижня 2 і далі має холодне синє світло»).
            if (_fx != null) _fx.Match();
            Place(_player, ZonePos(MatchSide.Player, Zone.Center), 90f, "Ring_Stance_Loop");
            Place(_rival, ZonePos(MatchSide.Rival, Zone.Center), -90f, "Ring_Stance_Loop");
            Place(_ref, RefPos, 180f, "Ref_Idle_Loop");
            SetShot(StageCam.Overview);
            var rr = _player.Model.GetComponentsInChildren<Renderer>();
            Debug.Log("[Ринг] 3D: гравець slot=" + _player.Slot.position + " renderers=" + rr.Length + (rr.Length > 0 ? " bounds=" + rr[0].bounds + " vis=" + rr[0].isVisible : "") + " scale=" + _player.Model.transform.lossyScale);
        }

        private readonly float[] _camTime = new float[5];

        /// <summary>Лог частки планів камери за матч (поки режисер грає дії) і скидання лічильника.</summary>
        private void LogCamShare()
        {
            float sum = 0f;
            for (int i = 0; i < _camTime.Length; i++) sum += _camTime[i];
            if (sum <= 0f) return;
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] камера матчу: огляд {0:P0}, ближче {1:P0}, кіно {2:P0}, низька {3:P0}, кіноплан {4:P0} ({5:F0} с дій)",
                _camTime[0] / sum, _camTime[1] / sum, _camTime[2] / sum, _camTime[3] / sum, _camTime[4] / sum, sum));
            for (int i = 0; i < _camTime.Length; i++) _camTime[i] = 0f;
        }

        public void ShowAftermath(bool playerWon)
        {
            LogCamShare();
            LeaveShow();
            _mode = RingStageMode.Idle;
            if (_director != null) _director.EndMatch();
            HideMic();
            ShowAll();
            var w = playerWon ? _player : _rival;
            var l = playerWon ? _rival : _player;
            // Переможець — у центрі ближче до камери, руки вгору до залу; рефері поруч; той, хто програв, — нокаут позаду.
            Place(w, new Vector3(-0.3f, MatY, -0.6f), 180f, Clip("Entrance_Corner_Pose") != null ? "Entrance_Corner_Pose" : "Idle_Hype_Loop");
            Place(_ref, new Vector3(0.6f, MatY, -0.3f), 200f, "Ref_Idle_Loop");
            var ko = Clip("Bump_Back_Sell_KO") ?? Clip("Bump_Back");
            if (ko != null)
            {
                l.PlayAt(ko, false, new Vector3(0.4f, MatY, 0.9f), 120f);
                l.SetHeight(MatY);
            }
            if (_fx != null)
            {
                var t = playerWon ? w : l;
                if (playerWon) _fx.VictoryLights(t.Hips != null ? t.Hips : t.Slot);
                else _fx.Defeat(t.Hips != null ? t.Hips : t.Slot);
            }
            CrowdMood(playerWon ? 3 : 1);
            SetShot(StageCam.Close);
            SnapCamera();
        }

        public void BeginMatch(MatchEngine engine)
        {
            LeaveShow();
            _mode = RingStageMode.Match;
            ForgetWatch();
            HideMic();
            ShowAll();
            _director.Begin(engine);
            if (_fx != null) _fx.Match();
            CrowdMood(engine != null ? engine.State.Hype : 0);
            if (_sound != null) { _sound.MusicOff(); if (engine != null && engine.State.Log.Count <= 1) _sound.BellStart(); }
            SetShot(StageCam.Overview);
        }

        /// <summary>Демо-режим (-ring-demo): матч без рушія, події подає RingStageDemo.</summary>
        public RingMatchDirector BeginDemoMatch()
        {
            _mode = RingStageMode.Match;
            ForgetWatch();
            HideMic();
            _ref.Model.SetActive(true);
            _director.BeginDemo();
            SetShot(StageCam.Overview);
            return _director;
        }

        /// <summary>Усі троє на рингу видимі (кінець сезону й створення ховають суперника й рефері).</summary>
        private void ShowAll()
        {
            _heroOnly = false;
            _player.Model.SetActive(true);
            _rival.Model.SetActive(true);
            _ref.Model.SetActive(true);
        }

        public void SetBigShow(bool big) { if (_fx != null && _fx.Big != big) _fx.SetBig(big); }

        public void BeginShowOpen(string title, string subtitle)
        {
            _heroOnly = false;
            _mode = RingStageMode.Idle;
            if (_director != null) _director.EndMatch();
            HideMic();
            _show.LeaveBackstage();
            if (_fx != null) _fx.Match();
            _show.BeginOpen(title, subtitle);
            if (_sound != null) _sound.Crowd(3);
        }

        public void ShowSeasonEnd(bool good) { HeroAlone(good, false, false); }

        public void ShowCreate() { HeroAlone(false, true, false); }

        // «Герой сам» (створення, кінець сезону): тримається й після перебудови зовнішності (SetWrestlers).
        private bool _heroOnly, _heroGood, _heroCreate;

        /// <summary>
        /// Герой сам у центрі рингу обличчям до камери, суперник і рефері сховані; кіноплан — герой у лівій частині кадру
        /// (праворуч — колонка екрана). Створення — ближче, по коліна (плейтест 08.11.2026, P3: на створенні камера показувала
        /// весь ринг із двома борцями, і було неясно, хто «я»); кінець сезону — ширше, добрий сезон — свято (P1-7).
        /// <paramref name="again"/> — після перебудови моделі: без нового конфеті.
        /// </summary>
        private void HeroAlone(bool good, bool create, bool again)
        {
            if (!again)
            {
                LeaveShow();
                _mode = RingStageMode.Idle;
                if (_director != null) _director.EndMatch();
                HideMic();
            }
            ShowAll();
            _rival.Model.SetActive(false);
            _ref.Model.SetActive(false);
            _heroOnly = true; _heroGood = good; _heroCreate = create;
            string pose = good && Clip("Entrance_Corner_Pose") != null ? "Entrance_Corner_Pose" : good ? "Idle_Hype_Loop" : "Ring_Stance_Loop";
            // Герой ближче до дальніх канатів, камера — всередині рингу: зовні верхній канат перетинав героя на рівні рук
            // (огляд pt1, кадр 015 створення).
            var at = new Vector3(0f, MatY, 1.3f);
            Place(_player, at, 180f, pose);
            if (!again && _fx != null)
            {
                if (good) _fx.Victory(_player.Hips != null ? _player.Hips : _player.Slot);
                else _fx.Match();
            }
            if (!again) CrowdMood(good ? 3 : 1);
            // Вертикальний кут 30° (створення) / 40°: півширина кадру на відстані d — d·tan(fov/2)·аспект; герой — на 0,36 півширини
            // лівіше центру (≈32 % ширини екрана). Камера на z = 1,3 − d ≈ −1,8 — всередині канатів (2,04).
            float fov = create ? 30f : 40f, d = 3.1f;
            float half = d * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * CamAspect;
            var look = at + new Vector3(0.36f * half, create ? 1.15f : 1.25f, 0f);
            Cine(look + new Vector3(0.5f, 0.15f, -d), look, fov, !again);
        }

        public void EndMatch()
        {
            LogCamShare();
            if (_mode == RingStageMode.Match) ShowIdle();
        }

        public void BeginPromo()
        {
            LeaveShow();
            _mode = RingStageMode.Promo;
            if (_director != null) _director.EndMatch();
            ShowAll();
            if (_fx != null) _fx.Match();
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

        private Coroutine _gestureRun;
        private int _gestureIx;
        private static readonly string[] Gestures = { "Promo_Mic_Point_Crowd", "Promo_Mic_Fist", "Promo_Mic_Point", "Promo_Mic_Turn_Crowd" };

        private System.Collections.IEnumerator GestureThenTalk(RingActor talker, AnimationClip talk)
        {
            var g = Clip(Gestures[_gestureIx++ % Gestures.Length]);
            if (g != null)
            {
                talker.PlayHere(g, false);
                yield return new WaitForSeconds(g.length);
            }
            if (talk != null && _mode == RingStageMode.Promo) talker.PlayHere(talk, true);
            _gestureRun = null;
        }

        public void SetPromoSpeaker(string speaker)
        {
            // Сцена закулісся йде в роздягальні: камера — на того, хто говорить (букер, герой) або загальний план.
            if (_show != null && _show.InBackstage) { _show.BackstageSpeaker(speaker); return; }
            if (_mode != RingStageMode.Promo || string.IsNullOrEmpty(speaker)) return;
            if (speaker == _promoSpeaker) return;
            _promoSpeaker = speaker;
            bool player = speaker == "player", rival = speaker == "rival";

            // Мікрофон — у того, хто з рингу говорить; решта (оповідач, букер, ветеран, натовп) — без нього.
            var talker = player ? _player : rival ? _rival : null;
            var listener = player ? _rival : rival ? _player : null;
            var idle = Clip("Ring_Stance_Loop");
            var stare = Clip("Promo_Mic_Stare_Loop") ?? idle;
            var talk = Clip("Promo_Mic_Talk_Loop");
            if (talker != null)
            {
                // Кожна нова репліка — жест (вказати на зал, кулак, обернутися до залу), потім розмова; той, хто слухає, —
                // мовчазний погляд (власник 04.11.2026: «жести промо»).
                if (_gestureRun != null) StopCoroutine(_gestureRun);
                _gestureRun = StartCoroutine(GestureThenTalk(talker, talk));
                if (stare != null && listener != null) listener.PlayHere(stare, true);
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
            _heroOnly = false;
            _mode = RingStageMode.Idle;
            if (_director != null) _director.EndMatch();
            HideMic();
            _show.LeaveBackstage();
            _show.BeginIntro(title, subtitle);
            if (_sound != null) { _sound.TitleMusic(); _sound.Crowd(0); }
        }

        /// <summary>Хто вже виходив за цю сесію (B07 п. 4: повтор — скорочений, без тизера й тремтіння).</summary>
        private readonly HashSet<string> _entered = new HashSet<string>();
        private string _tronTier;

        /// <summary>
        /// Щабель екрана виходу: нині в грі лише спортзал (gym); для огляду — <c>-tron-tier hall|arena</c> (ТЗ B07, «Рецепт»).
        /// </summary>
        private string TronTier(string asked)
        {
            if (_tronTier == null) _tronTier = ReadArg("-tron-tier") ?? "";
            return _tronTier.Length > 0 ? _tronTier : string.IsNullOrEmpty(asked) ? "gym" : asked;
        }

        public void BeginEntrance(bool player, EntranceRecipe recipe, EntranceProfile profile, int costumeRgb)
        {
            _heroOnly = false;
            _mode = RingStageMode.Idle;
            if (_director != null) _director.EndMatch();
            HideMic();
            _show.LeaveBackstage();
            if (recipe == null) recipe = new EntranceRecipe { Player = player, Role = player ? "hero" : "villain" };
            if (profile == null) profile = EntranceProfile.Compose(new EntranceInput { Player = player });
            recipe.Tier = TronTier(recipe.Tier);
            var a = player ? _player : _rival;
            bool repeat = !_entered.Add(recipe.Name ?? "");
            var costume = RingTheme.Rgb((uint)costumeRgb);
            _show.BeginEntrance(a, recipe, profile, costume, IsFemale(a), repeat);
            if (_sound != null)
            {
                _sound.Crowd(profile.Crowd);
                _sound.EntranceMusic(player, !player && _lookRival != null && _lookRival.StartsWith("body_m_nordic"), _show.HitIn, !profile.CrowdBoo);
            }
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
        public int FxMoments { get { return _fx != null ? _fx.Moments : 0; } }

        public void LookbookShot()
        {
            ShowIdle();
            SetShot(StageCam.Close);
            SnapCamera();
        }
        public string FxLastMoment { get { return _fx != null ? _fx.LastMoment : null; } }

        private bool _feetCam;

        public void LookbookClip(string clip, bool footIk)
        {
            RingActor.FootIK = footIk;
            var c = Clip(clip);
            if (c != null && _player != null) _player.PlayAt(c, false, new Vector3(-0.6f, MatY, 0f), 90f);
            RingActor.FootIK = true;
        }

        public void LookbookFeet(bool on)
        {
            _feetCam = on;
            SnapCamera();
            if (!on) return;
            // Верх полотна: найвища точка рендерерів зали над центром рингу (у межах 0.5 м по горизонталі), нижче 2 м.
            float top = float.MinValue;
            foreach (var r in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (r.sharedMesh == null || r.GetComponentInParent<Animator>() != null) continue;
                var b = r.GetComponent<Renderer>();
                if (b == null || !b.bounds.Contains(new Vector3(0f, b.bounds.center.y, 0f))) continue;
                if (b.bounds.max.y < 2f && b.bounds.max.y > top) top = b.bounds.max.y;
            }
            foreach (var a in new[] { _player, _rival, _ref })
            {
                if (a == null || a.Anim == null || !a.Anim.isHuman) continue;
                float lowest = float.MaxValue;
                foreach (var sm in a.Model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var baked = new Mesh();
                    sm.BakeMesh(baked, true);
                    var m = sm.transform.localToWorldMatrix;
                    foreach (var v in baked.vertices) lowest = Mathf.Min(lowest, m.MultiplyPoint3x4(v).y);
                    Destroy(baked);
                }
                Debug.Log(string.Format("[Ринг] ноги: {0} slot={1:F3} стопа={2:F3} пальці={3:F3} найнижча вершина={4:F3} верх полотна={5:F3} MatY={6:F3}",
                    a.Slot.name, a.Slot.position.y, a.Anim.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,
                    a.Anim.GetBoneTransform(HumanBodyBones.LeftToes) != null ? a.Anim.GetBoneTransform(HumanBodyBones.LeftToes).position.y : -1f,
                    lowest, top, MatY));
            }
        }

        public void SkipShow() { if (_show != null) _show.Skip(); }

        /// <summary>Постать у сорочці, штанях і взутті поверх тіла з текстурою борця (букер, гості закулісся).</summary>
        public RingActor MakeDressed(string name, string body, int rgb, string hair, Color shirt)
        {
            var a = Rebuild(name, body, rgb, hair, 1f);
            if (a == null) return null;
            bool f = body.StartsWith("body_f_");
            RingDress.Apply(a.Model, f ? assets.kitFemale : assets.kitMale, new[]
            {
                new RingDress.Piece("shirt", shirt), new RingDress.Piece("trousers", new Color(0.12f, 0.12f, 0.13f)),
                new RingDress.Piece("shoes", new Color(0.08f, 0.06f, 0.05f)),
            });
            return a;
        }

        /// <summary>Сцена закулісся: хто на місці Ліни (подія — гість), парковка — холодна лампа.</summary>
        public void SetBackstageScene(string sceneId)
        {
            if (_show == null || !_show.InBackstage) return;
            string body = null, hair = ""; int rgb = 0x5A6A78; bool dress = true;
            switch (sceneId)
            {
                case "ev_parking": { var l = CareerLooks.ForNpc("baron"); body = l.Body; rgb = l.Rgb; hair = l.Hair; break; }
                case "ev_friend": { var l = CareerLooks.ForNpc("stalevyi"); body = l.Body; rgb = l.Rgb; hair = l.Hair; dress = false; break; }
                case "ev_interview": body = "body_f_south_asian"; hair = "hair_bob02"; rgb = 0x3B5BA0; break;
                case "ev_contract": body = "body_m_east_asian"; hair = "hair_short02"; rgb = 0x202024; break;
            }
            RingActor guest = null;
            if (body != null)
            {
                guest = Rebuild("Гість", body, rgb, hair, 1f);
                if (guest != null && dress)
                {
                    bool f = body.StartsWith("body_f_");
                    var c = new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
                    RingDress.Apply(guest.Model, f ? assets.kitFemale : assets.kitMale, new[]
                    {
                        new RingDress.Piece("shirt", c), new RingDress.Piece("trousers", new Color(0.12f, 0.12f, 0.13f)),
                        new RingDress.Piece("shoes", new Color(0.08f, 0.06f, 0.05f)),
                    });
                }
            }
            _show.SetGuest(guest, sceneId ?? "", sceneId == "ev_parking");
        }

        public void ShowBackstage()
        {
            _heroOnly = false;
            _mode = RingStageMode.Idle;
            if (_director != null) _director.EndMatch();
            HideMic();
            // Букер — Ліна Ворон (сцени закулісся): жіноче тіло набору у власних матеріалах FBX.
            int bi = Array.FindIndex(assets.bodies, b => b != null && b.name == "body_f_mediterranean");
            _show.ShowBackstage(bi >= 0 ? assets.bodies[bi] : assets.bodyRef, bi >= 0 ? assets.kitFemale : assets.kitMale);
            if (_sound != null) { _sound.MusicOff(); _sound.Crowd(-1); }
        }

        // ============================ камера ============================

        public void SetShot(StageCam cam)
        {
            // Низький план утримання — склейкою: камера, що пливе з загального плану всередину рингу, пролітала крізь канат.
            if (cam == StageCam.Low && _shot != StageCam.Low) _camInit = false;
            // Вихід із кіноплану — теж склейкою: наїзд із кіноплану на загальний перелітав ринг (кадр «між планами»).
            if (_shot == StageCam.Cine && cam != StageCam.Cine) _camInit = false;
            if (_shot != cam) _shotAge = 0f;
            _shot = cam;
        }

        private Vector3 _cinePos, _cineLook;
        private float _cineFov = 34f;

        /// <summary>
        /// Кіноплан, порахований режисером (<see cref="RingCine"/>): камера стоїть, де сказано, і дивиться точно на
        /// <paramref name="look"/> (без зсуву рингу вгору під панель — кадр уже розкладено з урахуванням виносок і панелі).
        /// <paramref name="cut"/> — склейкою (фінішер, перемога), інакше камера доганяє.
        /// </summary>
        public void Cine(Vector3 pos, Vector3 look, float fov, bool cut)
        {
            _cinePos = pos; _cineLook = look; _cineFov = fov;
            if (cut) _camInit = false;
            if (_shot != StageCam.Cine) _shotAge = 0f;
            _shot = StageCam.Cine;
        }

        /// <summary>Пропорції кадру камери (ширина / висота) — для розкладки кіноплану.</summary>
        public float CamAspect { get { return _cam != null ? _cam.aspect : 16f / 9f; } }

        private void SnapCamera() { _camInit = false; }

        /// <summary>Фаз-тест матчу (телепорти, крізь канати, ковзання стоп) — підсумок у <see cref="PlayedReport"/>.</summary>
        public RingStageWatch Watch { get; } = new RingStageWatch();
        private readonly RingActor[] _watchActors = new RingActor[3];
        private readonly RingActor[] _watchShow = new RingActor[1];

        /// <summary>Постаті переставлено поза кадром (новий матч) — фаз-тест починає відлік з нових місць.</summary>
        private void ForgetWatch()
        {
            Watch.Forget(_player); Watch.Forget(_rival); Watch.Forget(_ref);
        }

        private void LateUpdate()
        {
            // реальний час — щоб камера жила й під стоп-кадром; але на паузі гри стоїть і вона (рев'ю 08.11.2026)
            float dt = Game.Gameplay.Ring.Models.RingTime.Paused ? 0f : Time.unscaledDeltaTime;
            if (_player != null) _player.Tick(Time.deltaTime);
            if (_rival != null) _rival.Tick(Time.deltaTime);
            if (_ref != null) _ref.Tick(Time.deltaTime);
            if (_show != null) _show.PinWalker();
            if (_mode == RingStageMode.Match)
            {
                _watchActors[0] = _player; _watchActors[1] = _rival; _watchActors[2] = _ref;
                Watch.Tick(_watchActors, Time.deltaTime, !_camInit);
            }
            else if (_show != null && _show.Busy && _show.EntranceActor != null)
            {
                // Фаз-тест і для виходу (пакет «Відео огляд», п. 4): телепорти, крізь канати, ковзання стоп того, хто виходить.
                _watchShow[0] = _show.EntranceActor;
                Watch.Tick(_watchShow, Time.deltaTime, !_camInit || _show.CamCut);
            }
            if (_show != null) _show.TickBooker(Time.deltaTime);
            if (_cam == null) return;
            _shotAge += dt;
            if (_mode == RingStageMode.Match && _director != null && _director.IsBusy) _camTime[(int)_shot] += dt;

            Vector3 pos, look;
            float fov;
            if (_show != null && _show.LeftBackstage) { _show.LeftBackstage = false; _camInit = false; }
            bool show = _show != null && _show.CamActive;
            if (_feetCam && _player != null && _rival != null)
            {
                Vector3 m = (_player.PosePos + _rival.PosePos) * 0.5f;
                look = new Vector3(m.x, MatY + 0.25f, m.z);
                pos = look + new Vector3(0f, 0.02f, -3.2f);
                fov = 30f;
                show = true;
            }
            else if (show)
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
                    case StageCam.Low:
                        // Утримання: камера низько біля мату, збоку — накриття, плече на маті й рука рефері, що б'є «раз — два».
                        look = mid + new Vector3(0f, 0.3f, 0f);
                        // Камера з боку, протилежного рефері (він за парою, z+), вздовж тіл; ринг тісний — ширший кут замість відстані.
                        pos = look + new Vector3(1.7f, 0.95f, -1.5f);
                        // Біля канатів пара зсунута — камера лишається всередині рингу, канат не перетинає кадр.
                        float lim = RingHalf - 0.35f;
                        pos.x = Mathf.Clamp(pos.x, -lim, lim);
                        pos.z = Mathf.Clamp(pos.z, -lim, lim);
                        fov = 50f;
                        break;
                    case StageCam.Big:
                        look = mid + new Vector3(0f, 1.2f, 0f);
                        pos = look + new Vector3(-2.4f, 1.1f, -5.2f);
                        fov = 32f;
                        break;
                    case StageCam.Cine:
                        pos = _cinePos;
                        look = _cineLook;
                        fov = _cineFov;
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
            bool cine = !show && _mode != RingStageMode.Promo && _shot == StageCam.Cine;
            Vector3 aim = look - up * (halfH * (show || cine ? 0f : _frameShift));
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
            Vector3 shake = Vector3.zero;
            if (_shakeAmp > 0f)
            {
                _shakeT += dt;
                float k2 = 1f - _shakeT / Mathf.Max(0.01f, _shakeDur);
                if (k2 <= 0f) _shakeAmp = 0f;
                else
                {
                    float a2 = _shakeAmp * k2 * k2;
                    shake = new Vector3(Mathf.PerlinNoise(_shakeT * 31f, 0.1f) - 0.5f, Mathf.PerlinNoise(0.7f, _shakeT * 29f) - 0.5f, 0f) * (2f * a2);
                }
            }
            _cam.transform.SetPositionAndRotation(_camPos + _camRot * shake, _camRot);
            if (_dress != null) _dress.Occlude(_camPos, look);
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
            if (_budgetShots == 2 && _dress != null && _dress.FanCount > 4 && RingLaunchArgs.Has("-ring-crowd-debug"))
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
