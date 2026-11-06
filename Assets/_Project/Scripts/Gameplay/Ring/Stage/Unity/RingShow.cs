using System.Collections;
using System.Collections.Generic;
using Game.Gameplay.Ring.UI;
using Game.Gameplay.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Шоу навколо матчу (ітерація 1, грубо): титантрон над завісою, вихід борця від завіси до рингу, вступна пролітка
    /// камери перед меню, закулісся (роздягальня й букер). Власник 03.11.2026: «ти робиш тут тільки товпу, виходи,
    /// титантрони, промо, суддю, закулісся та вступну анимацію до гри перед меню».
    /// Камера: поки шоу грає, <see cref="RingStage3D"/> бере ракурс звідси (<see cref="CamActive"/>).
    /// Написи на титантроні — лише текстом через UI Toolkit у текстуру (правило «написи — текстом», рядки дає UI).
    /// Не лінтується (тека Stage/Unity).
    /// </summary>
    public sealed class RingShow : MonoBehaviour
    {
        private RingStage3D _s;

        // ---------- камера шоу ----------
        public bool CamActive { get; private set; }
        public Vector3 CamPos { get; private set; }
        public Vector3 CamLook { get; private set; }
        public float CamFov { get; private set; }
        /// <summary>Різка зміна ракурсу (без згладжування камери) — на монтажних склейках.</summary>
        public bool CamCut { get; set; }

        public bool Busy { get; private set; }
        private bool _skip;

        // ---------- титантрон ----------
        private RenderTexture _tronRt;
        private PanelSettings _tronPanel;
        private Label _tronTitle, _tronSub;
        private VisualElement _tronRoot;
        private Light _tronLight;

        // ---------- хода (таз приколочено до шляху) ----------
        private RingActor _walker;
        private Vector3 _walkPos;
        private float _walkYaw;

        // ---------- закулісся ----------
        public static readonly Vector3 BackstageOrigin = new Vector3(0f, 0f, 60f);
        private GameObject _backstage;
        private RingActor _booker;

        // Оголошені ДО Path: статичні поля ініціалізуються за порядком тексту — інакше ClimbStart у шляху був нулем, і борець
        // ішов підлогою в центр рингу («по пояс у маті», огляд 04.11.2026).
        /// <summary>
        /// Сходи північно-західного кута (anchor_steps_NW, Unity) і напрям підйому — по діагоналі до кута рингу. Кліп
        /// Entrance_Climb_Steps починається тазом за 0,207 м перед якорем (ring_anims_entrance.py).
        /// </summary>
        private static readonly Vector3 StepsAnchor = new Vector3(-3.528f, 0f, 3.528f);
        private static readonly Vector3 ClimbDir = new Vector3(1f, 0f, -1f).normalized;
        private static readonly Vector3 ClimbStart = StepsAnchor - ClimbDir * 0.207f;

        /// <summary>Шлях виходу (Unity, Y = підлога зали): від завіси вздовж проходу до сходинок північно-західного кута.</summary>
        internal static readonly Vector3[] Path =
        {
            new Vector3(-15.4f, 0f, 0f), new Vector3(-12f, 0f, 0f), new Vector3(-8f, 0f, 0f), new Vector3(-4.5f, 0f, 1.2f),
            ClimbStart
        };

        // Над завісою, вище банера зали (на 3,4 м екран закривав банер — огляд 04.11.2026).
        private static readonly Vector3 CurtainTop = new Vector3(-15.9f, 5.3f, 0f);
        private const float WalkSpeed = 1.15f; // Ref_Walk_Loop: 1 м за цикл 1 с — швидкість = швидкість відтворення

        public void Init(RingStage3D stage)
        {
            _s = stage;
            BuildTitantron();
        }

        private void OnDestroy()
        {
            if (_tronRt != null) _tronRt.Release();
            if (_tronPanel != null) Destroy(_tronPanel);
            if (_booker != null) _booker.Destroy();
        }

        public void Skip() { _skip = true; }

        // ============================ титантрон ============================

        private void BuildTitantron()
        {
            _tronRt = new RenderTexture(1024, 512, 0, RenderTextureFormat.ARGB32) { name = "titantron" };
            _tronRt.Create();
            var asset = Resources.Load<PanelSettings>(HudToolkitTheme.PanelResourcePath);
            _tronPanel = asset != null ? Instantiate(asset) : ScriptableObject.CreateInstance<PanelSettings>();
            _tronPanel.name = "TitantronPanel";
            _tronPanel.targetTexture = _tronRt;
            _tronPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
            _tronPanel.scale = 1f;
            _tronPanel.clearColor = true;
            _tronPanel.colorClearValue = Color.black;
            if (_tronPanel.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>(HudToolkitTheme.ThemeResourcePath);
                if (theme != null) _tronPanel.themeStyleSheet = theme;
            }
            var host = new GameObject("Titantron (UI)");
            host.transform.SetParent(transform, false);
            host.SetActive(false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = _tronPanel;
            host.SetActive(true);
            _tronRoot = doc.rootVisualElement;
            _tronRoot.style.position = Position.Absolute;
            _tronRoot.style.left = 0; _tronRoot.style.right = 0; _tronRoot.style.top = 0; _tronRoot.style.bottom = 0;
            _tronRoot.style.justifyContent = Justify.Center;
            _tronRoot.style.alignItems = Align.Center;
            _tronTitle = new Label();
            _tronTitle.style.fontSize = 110;
            _tronTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _tronTitle.style.color = Color.white;
            _tronTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _tronSub = new Label();
            _tronSub.style.fontSize = 44;
            _tronSub.style.color = RingTheme.Gold;   // токен (ТЗ B00): було #FFD966
            _tronSub.style.unityTextAlign = TextAnchor.MiddleCenter;
            // Шрифт — лише через роль (ТЗ B00 §2); вигляд титантрона без змін до свого пакета.
            RingFonts.Apply(_tronTitle, RingFontRole.Text);
            RingFonts.Apply(_tronSub, RingFontRole.Text);
            _tronRoot.Add(_tronTitle);
            _tronRoot.Add(_tronSub);

            // Екран над завісою: рамка й полотно (Unlit, світиться саме), лицем до рингу (+X).
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Titantron frame";
            Destroy(frame.GetComponent<Collider>());
            frame.transform.SetParent(transform, false);
            frame.transform.position = CurtainTop + new Vector3(-0.08f, 0f, 0f);
            frame.transform.localScale = new Vector3(0.12f, 2.5f, 4.6f);
            frame.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.05f, 0.05f, 0.06f));
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Titantron";
            Destroy(screen.GetComponent<Collider>());
            screen.transform.SetParent(transform, false);
            screen.transform.position = CurtainTop;
            screen.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(0f, 180f, 0f);
            screen.transform.localScale = new Vector3(4.3f, 2.15f, 1f);
            // Шейдер — з шаблону матеріалу борця (URP/Lit у білді є; Shader.Find віддає null на вирізаних шейдерах):
            // полотно чорне, картинка — в емісії, тож екран світиться сам і не залежить від світла зали.
            // Картинка і в базовому кольорі: якщо варіант шейдера з емісією вирізано з білду, екран лишається видимим у
            // відсвіті власного світла.
            var m = _s.assets.screenTemplate != null ? new Material(_s.assets.screenTemplate) : Lit(Color.white);
            m.SetColor("_BaseColor", Color.white);
            m.name = "titantron";
            m.SetTexture("_BaseMap", _tronRt);
            m.SetTexture("_EmissionMap", _tronRt);
            m.SetColor("_EmissionColor", Color.white * 1.6f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            screen.GetComponent<Renderer>().sharedMaterial = m;
            screen.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Відсвіт екрана на прохід.
            _tronLight = new GameObject("Titantron glow").AddComponent<Light>();
            _tronLight.transform.SetParent(transform, false);
            _tronLight.type = LightType.Point;
            _tronLight.transform.position = CurtainTop + new Vector3(1.5f, -1.2f, 0f);
            _tronLight.range = 9f;
            _tronLight.intensity = 0f;
            _tronLight.shadows = LightShadows.None;
            SetTitantron("", "", Color.black);
        }

        /// <summary>Що на екрані: великий напис, підпис, колір тла (колір борця). Порожні рядки — темний екран.</summary>
        public void SetTitantron(string title, string sub, Color color)
        {
            _tronTitle.text = title ?? "";
            _tronSub.text = sub ?? "";
            _tronRoot.style.backgroundColor = Color.Lerp(Color.black, color, 0.55f);
            _tronPanel.colorClearValue = Color.black;
            _tronLight.color = Color.Lerp(Color.white, color, 0.7f);
            _tronLight.intensity = string.IsNullOrEmpty(title) ? 0f : 6f;
        }

        private Material Lit(Color c)
        {
            var t = _s.assets.wrestlerTemplate;
            var m = t != null ? new Material(t) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = "show_lit";
            m.SetTexture("_BaseMap", null);
            m.SetTexture("_MainTex", null);
            m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_NORMALMAP");
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
            return m;
        }

        // ============================ вихід ============================

        /// <summary>
        /// Вихід борця: титантрон з іменем, хода від завіси до сходинок, склейка — борець у куті рингу з позою на публіку.
        /// Інший борець і рефері на час виходу сховані. Кінець — <see cref="Busy"/> = false; далі UI починає матч.
        /// </summary>
        public void BeginEntrance(RingActor a, string title, string sub, Color color)
        {
            StopAllCoroutines();
            _skip = false;
            StartCoroutine(Entrance(a, title, sub, color));
        }

        private IEnumerator Entrance(RingActor a, string title, string sub, Color color)
        {
            Busy = true;
            foreach (var other in new[] { _s.Actor(Core.Ring.Match.MatchSide.Player), _s.Actor(Core.Ring.Match.MatchSide.Rival), _s.Referee })
                if (other != a) other.Model.SetActive(false);
            a.Model.SetActive(true);
            SetTitantron(title, sub, color);
            var fx = _s.Fx;
            if (fx != null) fx.Entrance(a.Hips != null ? a.Hips : a.Slot, color);

            // 1. Темна зала, екран спалахує: камера з рингу на завісу.
            // Кадр з проходу: і борець біля завіси, і екран над нею (огляд 04.11.2026: камера дивилась під титантрон).
            Look(new Vector3(-6.5f, 2.2f, -2.4f), new Vector3(-16f, 2.9f, 0f), 42f, true);
            var walk = _s.Clip("Entrance_Walk_Loop") ?? _s.Clip("Ref_Walk_Loop");
            var hype = _s.Clip("Idle_Hype_Loop") ?? _s.Clip("Ring_Stance_Loop");
            _s.Place(a, Path[0], 90f, "Idle_Hype_Loop");
            a.SetHeight(0f);
            yield return Wait(2.2f);

            // 2. Хода вздовж проходу: камера попереду борця, задкує разом з ним.
            if (walk != null) a.PlayAt(walk, true, Path[0], 90f, WalkSpeed);
            _walker = a;
            for (int i = 0; i + 1 < Path.Length && !_skip; i++)
            {
                Vector3 p0 = Path[i], p1 = Path[i + 1];
                float yaw = RingStage3D.YawTo(p0, p1);
                float dur = Vector3.Distance(p0, p1) / WalkSpeed;
                for (float t = 0f; t < dur && !_skip; t += Time.deltaTime)
                {
                    _walkPos = Vector3.Lerp(p0, p1, t / dur);
                    _walkYaw = Mathf.LerpAngle(_walkYaw, yaw, 1f - Mathf.Exp(-8f * Time.deltaTime));
                    if (i == 0 && t < 0.05f) _walkYaw = yaw;
                    Vector3 fwd = Quaternion.Euler(0f, _walkYaw, 0f) * Vector3.forward;
                    Vector3 ahead = _walkPos + fwd * 3.4f + new Vector3(0.9f, 1.7f, 0f);
                    if (Mathf.Abs(ahead.x) > 4.2f || Mathf.Abs(ahead.z) > 4.2f)
                        Look(ahead, _walkPos + new Vector3(0f, 1.3f, 0f), 38f, false);
                    else
                    {
                        // Біля рингу камера попереду в'їжджала у сходи, фартух і огорожу (огляд 05.11.2026) —
                        // тут вона з боку рингу, вище верхнього каната, дивиться на борця згори-збоку.
                        Vector3 toRing = new Vector3(-_walkPos.x, 0f, -_walkPos.z).normalized;
                        Look(_walkPos + toRing * 3.2f + Vector3.up * 3.0f, _walkPos + Vector3.up * 1.1f, 42f, false);
                    }
                    yield return null;
                }
            }
            _walker = null;

            // 3. Сходами на фартух (висота й крок — у кліпі), між середнім і верхнім канатом у ринг, поза в куті до залу.
            // Без кліпів (старі FBX) — склейка просто в кут.
            float climbYaw = RingStage3D.YawTo(Vector3.zero, ClimbDir);
            var climb = _s.Clip("Entrance_Climb_Steps");
            var ropes = _s.Clip("Entrance_Through_Ropes");
            var pose = _s.Clip("Entrance_Corner_Pose") ?? hype;
            Vector3 side = Quaternion.Euler(0f, 90f, 0f) * ClimbDir;
            if (climb != null && ropes != null && !_skip)
            {
                a.SetHeight(0f);
                a.PlayAt(climb, false, ClimbStart, climbYaw);
                // Підйом на метр угору: камера далі й вище, ціль — на рівні голови посеред підйому (огляд 05.11.2026 — голова за кадром).
                Vector3 steps = new Vector3(StepsAnchor.x, 0f, StepsAnchor.z);
                Look(steps + side * 4.2f + ClimbDir * 0.4f + Vector3.up * 2.3f, steps + ClimbDir * 0.8f + Vector3.up * 2.0f, 44f, true);
                yield return Wait(climb.length);
                // Кінець підйому: таз над фартухом. Канатний кліп відраховує висоту від фартуха (MatY), тож слот — на рівні мату.
                Vector3 onApron = a.PosePos;
                a.SetHeight(RingStage3D.MatY);
                a.PlayAt(ropes, false, new Vector3(onApron.x, RingStage3D.MatY, onApron.z), climbYaw);
                // Борець на фартуху: висота камери й цілі — від фартуха (PosePos.y тут ще підлога, голова була за рамкою).
                Vector3 apron = new Vector3(onApron.x, RingStage3D.MatY, onApron.z);
                Look(apron + side * 3.0f + Vector3.up * 1.6f - ClimbDir * 0.6f, apron + ClimbDir * 0.3f + Vector3.up * 1.3f, 44f, true);
                yield return Wait(ropes.length);
            }
            Vector3 corner = climb != null && ropes != null && !_skip ? a.PosePos : new Vector3(-1.25f, RingStage3D.MatY, 1.25f);
            corner.y = RingStage3D.MatY;
            // Обличчям до залу через діагональ кута (руки вгору до публіки).
            if (pose != null) a.PlayAt(pose, true, corner, climbYaw + 180f);
            a.SetHeight(RingStage3D.MatY);
            // Камера з боку залу (за кутом, вище канатів): борець обличчям до неї й до публіки; зсередини рингу була спина.
            Look(corner - ClimbDir * 3.6f + Vector3.up * 1.9f + side * 1.0f, corner + Vector3.up * 1.4f, 38f, true);
            yield return Wait(2.6f);

            SetTitantron("", "", Color.black);
            if (fx != null) fx.Match();
            foreach (var other in new[] { _s.Actor(Core.Ring.Match.MatchSide.Player), _s.Actor(Core.Ring.Match.MatchSide.Rival), _s.Referee })
                other.Model.SetActive(true);
            CamActive = false;
            Busy = false;
        }

        private void LateUpdate()
        {
            // Таз ходака приколочено до точки шляху: слот зсувається на стільки, на скільки кліп ушов уперед, тож стопи
            // стоять, поки кліп іде, а на петлі (таз стрибає назад на 1 м) слот у тому ж кадрі стрибає вперед.
            if (_walker == null) return;
            var a = _walker;
            Vector3 hip = a.PosePos - a.Slot.position;
            hip.y = 0f;
            float yawOff = Mathf.DeltaAngle(a.Slot.eulerAngles.y, a.PoseYaw);
            var rot = Quaternion.Euler(0f, _walkYaw - yawOff, 0f);
            Vector3 local = Quaternion.Inverse(a.Slot.rotation) * hip;
            Vector3 p = _walkPos - rot * local;
            a.Slot.SetPositionAndRotation(new Vector3(p.x, 0f, p.z), rot);
        }

        // ============================ вступ ============================

        /// <summary>
        /// Вступ перед меню (~20 с): порожня темна зала, камера пролітає від стелі над рядами до рингу, вмикається світло,
        /// титантрон показує назву гри. Будь-яка клавіша пропускає (<see cref="Skip"/>).
        /// </summary>
        public void BeginIntro(string title, string sub)
        {
            StopAllCoroutines();
            _skip = false;
            StartCoroutine(Intro(title, sub));
        }

        private IEnumerator Intro(string title, string sub)
        {
            Busy = true;
            _s.Referee.Model.SetActive(false);
            _s.Actor(Core.Ring.Match.MatchSide.Rival).Model.SetActive(false);
            var hero = _s.Actor(Core.Ring.Match.MatchSide.Player);
            hero.Model.SetActive(false);
            SetTitantron("", "", Color.black);

            // Пролітка: чотири точки над залою — від кута над рядами, вздовж трибуни, до рингу; дивиться в центр.
            Vector3[] fly =
            {
                new Vector3(12f, 7f, -9.5f), new Vector3(8f, 6f, -9.5f), new Vector3(-2f, 4.2f, -9f), new Vector3(-7f, 3.4f, -6.5f)
            };
            float seg = 3.2f;
            for (int i = 0; i + 1 < fly.Length && !_skip; i++)
                for (float t = 0f; t < seg && !_skip; t += Time.deltaTime)
                {
                    float s = Mathf.SmoothStep(0f, 1f, t / seg);
                    Vector3 pos = Vector3.Lerp(fly[i], fly[i + 1], s);
                    Vector3 look = Vector3.Lerp(new Vector3(0f, 1f, 0f), CurtainTop, Mathf.Clamp01((i + s - 1.4f) / 1.6f));
                    Look(pos, look, 44f, i == 0 && t == 0f);
                    yield return null;
                }

            // Екран спалахує назвою гри (іскри й CO2 біля завіси); вихід героя, якщо не пропущено.
            SetTitantron(title, sub, new Color(0.75f, 0.12f, 0.08f));
            if (_s.Fx != null) _s.Fx.TitleHit();
            yield return Wait(2.4f);
            if (!_skip)
            {
                hero.Model.SetActive(true);
                yield return Entrance(hero, title, sub, new Color(0.75f, 0.12f, 0.08f));
                Busy = true;
            }
            SetTitantron(title, sub, new Color(0.75f, 0.12f, 0.08f));
            _s.Referee.Model.SetActive(true);
            _s.Actor(Core.Ring.Match.MatchSide.Rival).Model.SetActive(true);
            hero.Model.SetActive(true);
            CamActive = false;
            Busy = false;
        }

        // ============================ закулісся ============================

        /// <summary>
        /// Закулісся (тиждень): роздягальня окремою кімнатою поза залою — шафки, лавка, лампа, двері; герой і букер.
        /// Камера — фіксований ракурс з кута кімнати. Знову на ринг — <see cref="LeaveBackstage"/>.
        /// </summary>
        public void ShowBackstage(GameObject bookerPrefab, GameObject kit)
        {
            StopAllCoroutines();
            Busy = false;
            if (_backstage == null) BuildBackstage(bookerPrefab, kit);
            _backstage.SetActive(true);
            var hero = _s.Actor(Core.Ring.Match.MatchSide.Player);
            Vector3 o = BackstageOrigin;
            _s.Place(hero, o + new Vector3(-0.7f, 0f, 0.2f), 70f, "Ring_Stance_Loop");
            if (_booker != null) _s.Place(_booker, o + new Vector3(0.8f, 0f, 0.5f), -110f, "Ref_Idle_Loop");
            Look(o + new Vector3(0.6f, 1.75f, -3.6f), o + new Vector3(0f, 1.2f, 0.4f), 46f, true);
        }

        public bool InBackstage { get { return _backstage != null && _backstage.activeSelf; } }

        /// <summary>Щойно вийшли з закулісся — камера рингу має стати на місце різко (RingStage3D читає й скидає).</summary>
        public bool LeftBackstage { get; set; }

        /// <summary>Камера розмови в роздягальні: букер через плече героя, герой через плече букера, інші — загальний план.</summary>
        public void BackstageSpeaker(string speaker)
        {
            if (!InBackstage || _booker == null) return;
            var hero = _s.Actor(Core.Ring.Match.MatchSide.Player);
            var other = _guest ?? _booker;
            Vector3 b = other.HeadPos, h = hero.HeadPos;
            if (speaker != "player" && speaker != "narrator" && speaker != null) speaker = "booker";
            Vector3 across = Vector3.Cross(Vector3.up, (b - h).normalized);
            if (speaker == "booker") Look(h + (h - b).normalized * 0.9f + across * 0.45f + Vector3.up * 0.1f, b, 34f, true);
            else if (speaker == "player") Look(b + (b - h).normalized * 0.9f - across * 0.45f + Vector3.up * 0.1f, h, 34f, true);
            else { var o = BackstageOrigin; Look(o + new Vector3(0.6f, 1.75f, -3.6f), o + new Vector3(0f, 1.2f, 0.4f), 46f, false); }
        }

        private RingActor _guest;
        private string _guestKey;
        private Light _lamp;

        /// <summary>
        /// Хто в кадрі сцени закулісся: розмова — Ліна Ворон; подія — гість на її місці (Барон на парковці, Сталевий у лазареті,
        /// ведучий інтерв'ю, агент із контрактом), Ліни немає. Парковка — холодна лампа, що мерехтить.
        /// </summary>
        public void SetGuest(RingActor guest, string key, bool parking)
        {
            if (_guestKey == key) return;
            if (_guest != null) { _guest.Destroy(); Destroy(_guest.Slot.gameObject); _guest = null; }
            _guestKey = key;
            _guest = guest;
            Vector3 o = BackstageOrigin;
            if (_booker != null) _booker.Model.SetActive(guest == null);
            if (guest != null)
            {
                guest.Slot.SetParent(_backstage.transform, true);
                _s.Place(guest, o + new Vector3(0.8f, 0f, 0.5f), -110f, "Ref_Idle_Loop");
            }
            if (_lamp != null)
            {
                _lamp.color = parking ? new Color(0.62f, 0.75f, 1f) : new Color(1f, 0.86f, 0.62f);
                _flicker = parking;
            }
        }

        private bool _flicker;

        private void Update()
        {
            if (_lamp != null && InBackstage)
                _lamp.intensity = _flicker ? (Mathf.PerlinNoise(Time.time * 7f, 0.3f) > 0.32f ? 3.2f : 0.6f) : 4.5f;
            if (_guest != null && InBackstage) _guest.Tick(Time.deltaTime);
        }

        public void LeaveBackstage()
        {
            if (InBackstage) LeftBackstage = true;
            if (_backstage != null) _backstage.SetActive(false);
            CamActive = false;
        }

        public void TickBooker(float dt) { if (_booker != null) _booker.Tick(dt); }

        private void BuildBackstage(GameObject bookerPrefab, GameObject kit)
        {
            _backstage = new GameObject("Backstage");
            _backstage.transform.SetParent(transform, false);
            Vector3 o = BackstageOrigin;
            var wall = Lit(new Color(0.42f, 0.44f, 0.40f));
            var floor = Lit(new Color(0.22f, 0.21f, 0.2f));
            var locker = Lit(new Color(0.18f, 0.27f, 0.36f));
            var wood = Lit(new Color(0.38f, 0.25f, 0.14f));
            var door = Lit(new Color(0.30f, 0.20f, 0.12f));
            Box("Підлога", o + new Vector3(0f, -0.05f, 0f), new Vector3(7f, 0.1f, 6f), floor);
            Box("Стеля", o + new Vector3(0f, 3.05f, 0f), new Vector3(7f, 0.1f, 6f), wall);
            Box("Стіна зад", o + new Vector3(0f, 1.5f, 3f), new Vector3(7f, 3f, 0.1f), wall);
            Box("Стіна ліва", o + new Vector3(-3.5f, 1.5f, 0f), new Vector3(0.1f, 3f, 6f), wall);
            Box("Стіна права", o + new Vector3(3.5f, 1.5f, 0f), new Vector3(0.1f, 3f, 6f), wall);
            for (int i = 0; i < 7; i++)
            {
                Vector3 c = o + new Vector3(-2.7f + i * 0.62f, 0.95f, 2.65f);
                Box("Шафка", c, new Vector3(0.58f, 1.9f, 0.55f), locker);
                Box("Ручка", c + new Vector3(0.2f, 0.05f, -0.29f), new Vector3(0.03f, 0.18f, 0.03f), floor);
                Box("Щілини", c + new Vector3(0f, 0.7f, -0.28f), new Vector3(0.4f, 0.12f, 0.01f), floor);
            }
            Box("Лавка", o + new Vector3(-0.6f, 0.42f, 1.6f), new Vector3(3.2f, 0.08f, 0.45f), wood);
            Box("Ніжка", o + new Vector3(-2f, 0.2f, 1.6f), new Vector3(0.08f, 0.4f, 0.4f), floor);
            Box("Ніжка", o + new Vector3(0.8f, 0.2f, 1.6f), new Vector3(0.08f, 0.4f, 0.4f), floor);
            Box("Двері", o + new Vector3(3.44f, 1.05f, -1.2f), new Vector3(0.05f, 2.1f, 0.95f), door);
            Box("Стіл букера", o + new Vector3(2.1f, 0.75f, 1.1f), new Vector3(1.4f, 0.06f, 0.7f), wood);
            Box("Ніжка столу", o + new Vector3(2.1f, 0.37f, 1.1f), new Vector3(1.2f, 0.74f, 0.05f), wood);
            var lamp = _lamp = new GameObject("Лампа").AddComponent<Light>();
            lamp.transform.SetParent(_backstage.transform, false);
            lamp.type = LightType.Point;
            lamp.transform.position = o + new Vector3(0f, 2.8f, 0.6f);
            lamp.range = 9f;
            lamp.intensity = 4.5f;
            lamp.color = new Color(1f, 0.86f, 0.62f);
            lamp.shadows = LightShadows.Soft;
            Box("Плафон", o + new Vector3(0f, 2.92f, 0.6f), new Vector3(0.5f, 0.06f, 0.5f), Lit(new Color(1f, 0.95f, 0.8f)));

            // Ліна Ворон: тіло з текстурою шкіри борців (як гості) — з голого FBX обличчя було сірою маскою (лукбук 04.11.2026);
            // хвіст замість каре, що закривало обличчя; сорочка, штани, взуття.
            _booker = _s.MakeDressed("Букер", "body_f_mediterranean", 0x8C9EB3, "hair_ponytail01", new Color(0.55f, 0.62f, 0.7f));
            if (_booker != null) _booker.Slot.SetParent(_backstage.transform, true);
        }

        private void Box(string name, Vector3 pos, Vector3 size, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(_backstage.transform, false);
            g.transform.position = pos;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        // ============================ спільне ============================

        private void Look(Vector3 pos, Vector3 look, float fov, bool cut)
        {
            CamPos = pos;
            CamLook = look;
            CamFov = fov;
            if (cut || !CamActive) CamCut = true;
            CamActive = true;
        }

        private IEnumerator Wait(float s)
        {
            for (float t = 0f; t < s && !_skip; t += Time.deltaTime) yield return null;
        }
    }
}
