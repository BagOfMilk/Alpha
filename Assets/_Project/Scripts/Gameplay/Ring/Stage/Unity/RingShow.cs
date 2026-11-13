using System.Collections;
using System.Collections.Generic;
using Game.Core.Ring.Career;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.UI;
using Game.Gameplay.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Шоу навколо матчу: екран виходу над завісою, персональний вихід борця від завіси до пози в рингу, вступна пролітка
    /// камери перед меню, закулісся (роздягальня й букер). Власник 03.11.2026: «ти робиш тут тільки товпу, виходи,
    /// титантрони, промо, суддю, закулісся та вступну анимацію до гри перед меню».
    /// Вихід (пакет «Відео огляд» 08.11.2026, п. 4): ролик екрана за рецептом (ТЗ B07, <see cref="EntranceScreenPlayer"/>) і
    /// п'ять бітів за профілем (docs/ring/ENTRANCE_PROFILE.md): завіса — жест ґіміка й шляху на ударі ролика, хода — петля
    /// профілю цілими циклами, зупинка біля залу, вхід у ринг (сходи → канати / перемах / закочування), поза (центр, другий
    /// канат у куті, канати, на місці) і пояс чемпіона; світло, CO2 й піро — на ударі (<see cref="HitIn"/>).
    /// Камера: поки шоу грає, <see cref="RingStage3D"/> бере ракурс звідси (<see cref="CamActive"/>).
    /// Написи — лише текстом через UI Toolkit у текстуру (рядки дає UI). Не лінтується (тека Stage/Unity).
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

        // ---------- екран над завісою ----------
        private RenderTexture _tronRt;
        private PanelSettings _tronPanel;
        private Label _tronTitle, _tronSub;
        private VisualElement _tronRoot, _titleLayer, _screenLayer;
        private Light _tronLight;
        private EntranceScreenPlayer _screen;

        /// <summary>Програвач екрана виходу (для огляду: що грає, скільки шарів).</summary>
        public EntranceScreenPlayer Screen { get { return _screen; } }
        /// <summary>Огляд крокує ролик вручну (точні миті удару) — шоу його не тікає.</summary>
        public bool ManualScreen { get; set; }
        /// <summary>Текстура екрана 1024×512 (огляд знімає її в PNG — щоб читати написи й мотиви зблизька).</summary>
        public RenderTexture ScreenTexture { get { return _tronRt; } }

        // ---------- стан виходу (для огляду й фаз-тесту) ----------
        /// <summary>Хто зараз виходить (фаз-тест стежить і за ним).</summary>
        public RingActor EntranceActor { get; private set; }
        /// <summary>Біт виходу: curtain, walk, barrier, steps, entry, pose, belt; порожньо — вихід не йде.</summary>
        public string Beat { get; private set; }
        /// <summary>Мить початку біта (Time.time).</summary>
        public float BeatAt { get; private set; }
        /// <summary>Секунд від початку виходу до удару ролика (стінгер, CO2, жест біля завіси).</summary>
        public double HitIn { get; private set; }
        /// <summary>Мить удару (Time.time).</summary>
        public float HitAt { get; private set; }
        /// <summary>Рецепт і профіль поточного виходу (огляд пише їх у звіт).</summary>
        public EntranceRecipe Recipe { get; private set; }
        public EntranceProfile Profile { get; private set; }

        // ---------- хода (таз приколочено до шляху) ----------
        private RingActor _walker;
        private Vector3 _walkPos;
        private float _walkYaw;

        // ---------- пояс ----------
        private GameObject _belt;
        private RingActor _beltHolder;
        private Vector3 _beltLen = Vector3.up, _beltFace = Vector3.back, _beltCenter;

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

        /// <summary>
        /// Закочування під нижнім канатом (Entrance_Roll_In): таз на старті — за 0,36 м від краю помосту (половина помосту
        /// з якорів залу, у спортзалі 2,438 м), на підлозі, обличчям до рингу (+X); кліп сам піднімає таз на мат.
        /// </summary>
        private static Vector3 RollStart { get { return new Vector3(-(RingStage3D.Venue.Plat + 0.357f), 0f, 0f); } }

        /// <summary>
        /// За шторою виходу (дверні панелі арки — x ≈ −14,9; кадри огляду 08.11.2026: тут борець стояв і раніше, лише його не
        /// було видно): до удару ролика борець тут, на ударі виходить крізь штору.
        /// </summary>
        private static readonly Vector3 CurtainBack = new Vector3(-15.4f, 0f, 0f);

        /// <summary>Перед аркою: тут жести завіси (жест ґіміка й шляху) — на весь зріст, з екраном над головою.</summary>
        private static readonly Vector3 CurtainOut = new Vector3(-14.3f, 0f, 0f);

        /// <summary>
        /// Шлях виходу (Unity, Y = підлога зали): від арки вздовж проходу до сходинок північно-західного кута. У середині
        /// проходу — правіше від оператора з камерою (anchor_camop_5, (−7; 0,55)): старий шлях ішов за 0,2 м від нього, і борець
        /// проходив крізь оператора (огляд виходів 08.11.2026).
        /// </summary>
        internal static readonly Vector3[] Path =
        {
            CurtainOut, new Vector3(-12f, 0f, 0f), new Vector3(-8f, 0f, -0.15f), new Vector3(-6.2f, 0f, -0.25f), new Vector3(-4.6f, 0f, 1.0f),
            ClimbStart
        };

        /// <summary>Шлях до закочування: той самий прохід, далі прямо до західного боку рингу.</summary>
        internal static readonly Vector3[] RollPath =
        {
            CurtainOut, new Vector3(-12f, 0f, 0f), new Vector3(-8f, 0f, -0.15f), new Vector3(-6.2f, 0f, -0.25f), new Vector3(-4.5f, 0f, -0.1f), RollStart
        };

        /// <summary>Вершина шляху, біля якої зупинка до залу (ENTRANCE_PROFILE.md: точка (-8, 0, 0)).</summary>
        private const int BarrierVertex = 2;

        /// <summary>Перетин канатів північно-західного кута (anchor_rope_NW, Unity) на висоті мату — поза на другому канаті.</summary>
        private static Vector3 CornerNW { get { float c = RingStage3D.Venue.RopeLine; return new Vector3(-c, RingStage3D.MatY, c); } }

        // Над завісою, вище банера зали (на 3,4 м екран закривав банер — огляд 04.11.2026).
        private static readonly Vector3 CurtainTop = new Vector3(-15.9f, 5.3f, 0f);
        /// <summary>Множник швидкості петлі ходи виходу (було: Entrance_Walk_Loop 1 м/с на ×1,15).</summary>
        private const float WalkPlay = 1.15f;

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

        // ============================ екран над завісою ============================

        private void BuildTitantron()
        {
            // Мипмапи й трилінійний фільтр (ТЗ B07 п. 3): тонкі лінії й растр мотивів на екрані в 3D інакше мерехтять (муар).
            _tronRt = new RenderTexture(1024, 512, 0, RenderTextureFormat.ARGB32)
            {
                name = "entrance_screen", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            _tronRt.Create();
            var asset = Resources.Load<PanelSettings>(HudToolkitTheme.PanelResourcePath);
            _tronPanel = asset != null ? Instantiate(asset) : ScriptableObject.CreateInstance<PanelSettings>();
            _tronPanel.name = "EntranceScreenPanel";
            _tronPanel.targetTexture = _tronRt;
            _tronPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
            _tronPanel.scale = 1f;
            _tronPanel.clearColor = true;
            _tronPanel.colorClearValue = RingTheme.Black;
            // Прозорість шарів як у генераторі директора (змішування в sRGB) — у програвачі (α^2,2, EntranceScreenPlayer.Apply).
            // PanelSettings.forceGammaRendering лише віддає гамма-кольори з шейдера: у sRGB-текстуру екрана це запише їх ще
            // раз перетвореними (світліше), а лінійна текстура показала б їх як лінійні — потрібна була б ще одна копія щокадру.
            if (_tronPanel.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>(HudToolkitTheme.ThemeResourcePath);
                if (theme != null) _tronPanel.themeStyleSheet = theme;
            }
            var host = new GameObject("Entrance screen (UI)");
            host.transform.SetParent(transform, false);
            host.SetActive(false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = _tronPanel;
            host.SetActive(true);
            _tronRoot = doc.rootVisualElement;
            _tronRoot.style.position = Position.Absolute;
            _tronRoot.style.left = 0; _tronRoot.style.right = 0; _tronRoot.style.top = 0; _tronRoot.style.bottom = 0;

            // Шар ролика виходу (B07) — програвач будує дерево в ньому.
            _screenLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _screenLayer.style.position = Position.Absolute;
            _screenLayer.style.left = 0; _screenLayer.style.top = 0; _screenLayer.style.width = 1024; _screenLayer.style.height = 512;
            _tronRoot.Add(_screenLayer);
            _screen = new EntranceScreenPlayer(_screenLayer, _s.assets.tronPools, _s.assets.tronSprites, _s.assets.tronSeal);

            // Шар назви гри для вступу (окремий рецепт B01 — не цей пакет): дві мітки по центру.
            _titleLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _titleLayer.style.position = Position.Absolute;
            _titleLayer.style.left = 0; _titleLayer.style.right = 0; _titleLayer.style.top = 0; _titleLayer.style.bottom = 0;
            _titleLayer.style.justifyContent = Justify.Center;
            _titleLayer.style.alignItems = Align.Center;
            _tronTitle = new Label();
            _tronTitle.style.fontSize = 110;
            _tronTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _tronTitle.style.color = RingTheme.Paper;
            _tronTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _tronSub = new Label();
            _tronSub.style.fontSize = 44;
            _tronSub.style.color = RingTheme.Gold;   // токен (ТЗ B00): було #FFD966
            _tronSub.style.unityTextAlign = TextAnchor.MiddleCenter;
            // Шрифт — лише через роль (ТЗ B00 §2).
            RingFonts.Apply(_tronTitle, RingFontRole.Text);
            RingFonts.Apply(_tronSub, RingFontRole.Text);
            _titleLayer.Add(_tronTitle);
            _titleLayer.Add(_tronSub);
            _tronRoot.Add(_titleLayer);

            // Екран над завісою: рамка й полотно, лицем до рингу (+X).
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Entrance screen frame";
            Destroy(frame.GetComponent<Collider>());
            frame.transform.SetParent(transform, false);
            frame.transform.position = CurtainTop + new Vector3(-0.08f, 0f, 0f);
            frame.transform.localScale = new Vector3(0.12f, 2.5f, 4.6f);
            frame.GetComponent<Renderer>().sharedMaterial = Lit(RingTheme.Ink0);
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Entrance screen";
            Destroy(screen.GetComponent<Collider>());
            screen.transform.SetParent(transform, false);
            screen.transform.position = CurtainTop;
            screen.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(0f, 180f, 0f);
            screen.transform.localScale = new Vector3(4.3f, 2.15f, 1f);
            // Шейдер — з шаблону екрана (URP/Lit з емісією; Shader.Find віддає null на вирізаних шейдерах). Картинка — лише в
            // емісії, базовий колір чорний: екран світиться сам і не підсвічується світлом зали (раніше база біла + емісія ×1,6
            // — пересвіт; Статут: «жодних білих полів», яскравість LED).
            var m = _s.assets.screenTemplate != null ? new Material(_s.assets.screenTemplate) : Lit(RingTheme.Black);
            m.name = "entrance_screen";
            m.SetColor("_BaseColor", RingTheme.Black);
            m.SetTexture("_BaseMap", null);
            m.SetTexture("_EmissionMap", _tronRt);
            m.SetColor("_EmissionColor", RingTheme.Paper * 1.15f);
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            screen.GetComponent<Renderer>().sharedMaterial = m;
            screen.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Бічні LED-екрани зали (LED_Content у venue_*.fbx, розгортка 0..1): без картинки вони світили чистим білим ×5 —
            // «яскраво-білий прямокутник» біля входу (зауваження «Відео огляду» до сезону s11). Тепер на них той самий ролик
            // виходу, що й над завісою; без виходу — темні.
            foreach (var r in _s.GetComponentsInChildren<Renderer>(true))
            {
                var sm = r.sharedMaterial;
                if (sm == null || !sm.name.StartsWith("LED_Content", System.StringComparison.Ordinal)) continue;
                r.sharedMaterial = m;
                Debug.Log("[Ринг] екран виходу: бічний LED " + r.name + " показує ролик");
            }
            // Відсвіт екрана на прохід.
            _tronLight = new GameObject("Entrance screen glow").AddComponent<Light>();
            _tronLight.transform.SetParent(transform, false);
            _tronLight.type = LightType.Point;
            _tronLight.transform.position = CurtainTop + new Vector3(1.5f, -1.2f, 0f);
            _tronLight.range = 9f;
            _tronLight.intensity = 0f;
            _tronLight.shadows = LightShadows.None;
            ScreenOff();
        }

        /// <summary>Темний екран: ролик зупинено, назви немає, відсвіт вимкнено.</summary>
        public void ScreenOff()
        {
            if (_screen != null) _screen.Stop();
            _tronTitle.text = "";
            _tronSub.text = "";
            _titleLayer.style.backgroundColor = RingTheme.WithAlpha(RingTheme.Black, 0f);
            _tronLight.intensity = 0f;
        }

        /// <summary>Назва гри на екрані (вступ, рецепт B01): напис, підпис і колір тла — токен, прозорістю з кроків екрана.</summary>
        private void ShowTitle(string title, string sub, Color tokenColor)
        {
            if (_screen != null) _screen.Stop();
            _tronTitle.text = title ?? "";
            _tronSub.text = sub ?? "";
            _titleLayer.style.backgroundColor = RingTheme.WithAlpha(tokenColor, BrandTokens.AlphaTronMid);
            _tronLight.color = tokenColor;
            _tronLight.intensity = string.IsNullOrEmpty(title) ? 0f : 6f;
        }

        private void Update()
        {
            if (_screen != null && _screen.Playing && !ManualScreen) _screen.Tick(Time.deltaTime);
            if (_lamp != null && InBackstage)
                // мерехтіння лампи паркінгу; «Спалахи» вимкнено (Налаштування → Доступність) — рівне світло
                _lamp.intensity = !_flicker ? 4.5f : !Game.Gameplay.Ring.UI.Menus.RingAccessibility.Flashes ? 3.2f
                    : (Mathf.PerlinNoise(Time.time * 7f, 0.3f) > 0.32f ? 3.2f : 0.6f);
            if (_guest != null && InBackstage) _guest.Tick(Time.deltaTime);
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

        private static Color RoleColor(string role)
        {
            switch (role)
            {
                case "villain": return RingTheme.Villain;
                case "rebel": return RingTheme.Rebel;
                default: return RingTheme.Hero;
            }
        }

        /// <summary>Кольори голів профілю: перша — костюм, решта — токени (Статут COL-01).</summary>
        private static Color[] HeadColors(EntranceProfile p, Color costume)
        {
            var tokens = p.HeadTokens ?? new[] { EntranceProfile.CostumeHead };
            var c = new Color[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                uint rgb;
                c[i] = tokens[i] == EntranceProfile.CostumeHead ? costume
                    : EntranceScreenSlots.TryRgb(tokens[i], out rgb) ? RingTheme.Rgb(rgb) : RingTheme.Paper;
            }
            return c;
        }

        // ============================ вихід ============================

        /// <summary>
        /// Вихід борця за рецептом екрана й профілем бітів. Інший борець і рефері на час виходу сховані. Кінець —
        /// <see cref="Busy"/> = false; далі UI починає матч. <paramref name="repeat"/> — борець уже виходив за сесію: ролик
        /// скорочений (B07 п. 4).
        /// </summary>
        public void BeginEntrance(RingActor a, EntranceRecipe recipe, EntranceProfile profile, Color costume, bool female, bool repeat)
        {
            StopAllCoroutines();
            BeltOff();
            _walker = null;
            _skip = false;
            Busy = true;
            Recipe = recipe;
            Profile = profile;
            // Ролик стартує одразу (синхронно): мить удару потрібна звуку ще в цьому кадрі.
            bool ok = recipe != null && _screen.Play(recipe, repeat, Game.Gameplay.GraphicsTier.IsLow);
            if (ok) _titleLayer.style.display = DisplayStyle.None;
            float bpm = ok ? (float)_screen.Bpm : profile.Bpm;
            HitIn = ok ? _screen.HitIn : 2.0 * 60.0 / bpm;
            HitAt = Time.time + (float)HitIn;
            _tronLight.color = RoleColor(recipe != null ? recipe.Role : "hero");
            _tronLight.intensity = ok ? 5f : 0f;
            if (ok) Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[Ринг] вихід: {0} — екран {1}, шарів {2}, удар через {3:0.00} с; завіса {4}{5}, хода {6}{7}, вхід {8}{9}, поза {10} ({11}){12}{13}",
                recipe.Name, _screen.Describe, _screen.LayerCount, HitIn, profile.CurtainGimmick, profile.CurtainPath != null ? " + " + profile.CurtainPath : "",
                profile.WalkLoop, profile.Barrier != null ? " + " + profile.Barrier : "", profile.Entry, profile.EntrySpeed < 1f ? " ×0,8" : "",
                profile.Pose, profile.PoseSpot, profile.Belt ? " + пояс" : "", female ? " (жін.)" : ""));
            StartCoroutine(Entrance(a, profile, costume, female, bpm, ok));
        }

        private void SetBeat(string b)
        {
            Beat = b;
            BeatAt = Time.time;
        }

        private AnimationClip ClipF(string name, bool female) { return name != null ? _s.Clip(name, female) : null; }

        private IEnumerator Entrance(RingActor a, EntranceProfile prof, Color costume, bool female, float bpm, bool screenOk)
        {
            Busy = true;
            EntranceActor = a;
            foreach (var other in new[] { _s.Actor(Core.Ring.Match.MatchSide.Player), _s.Actor(Core.Ring.Match.MatchSide.Rival), _s.Referee })
                if (other != a) other.Model.SetActive(false);
            a.Model.SetActive(true);
            var fx = _s.Fx;
            if (fx != null) fx.Entrance(a.Hips != null ? a.Hips : a.Slot, HeadColors(prof, costume), prof.HeadGain, bpm, (float)HitIn, prof.Sparks, prof.HallLight);
            float t0 = Time.time;

            // ---- 1. Завіса. Кадр з проходу: і арка, і екран над нею (B07: «у кадрі проходу екран бачимо над завісою»). До
            // удару — тизер ролика, борець за шторою; на ударі — виходить крізь штору (цілий цикл ходи) і перед аркою робить
            // жест ґіміка, далі жест шляху.
            SetBeat("curtain");
            Look(new Vector3(-6.5f, 2.2f, -2.4f), new Vector3(-16f, 3.2f, 0f), 42f, true);
            var hype = _s.Clip("Idle_Hype_Loop") ?? _s.Clip("Ring_Stance_Loop");
            var walk = ClipF(prof.WalkLoop, female) ?? ClipF(EntranceProfile.WalkFallback, female) ?? _s.Clip("Ref_Walk_Loop");
            float stride = walk != null ? a.LoopSpeed(walk) * walk.length : 1f;
            if (stride < 0.3f) stride = 1f;
            a.SetHeight(0f);
            if (hype != null) a.PlayAt(hype, true, CurtainBack, 90f);
            _s.Watch.Forget(a);
            yield return Wait((float)HitIn);
            float hold = (float)EntranceScreenMath.CurtainHoldSec(HitIn);
            if (walk != null && !_skip)
            {
                var outPath = new[] { CurtainBack, CurtainOut };
                yield return WalkLeg(a, walk, outPath, Cumulative(outPath), 0f, Vector3.Distance(CurtainBack, CurtainOut), stride, false);
                _walker = null;
            }
            bool close = false;
            foreach (var g in new[] { ClipF(prof.CurtainGimmick, female), ClipF(prof.CurtainPath, female) })
            {
                if (g == null || _skip) continue;
                a.PlayAtBlend(g, 0f, false, a.PosePos, 90f, 0.2f);
                float end = Time.time + g.length;
                while (Time.time < end && !_skip)
                {
                    // Після 2,2 с від удару — ближче, але екран лишається в кадрі: знизу, борець ~40 % висоти кадру, над ним екран
                    // (зауваження «Відео огляду» 08.11.2026 до s15: на ближньому плані від екрана лишалась смужка — ролик гравець не бачив).
                    // Камера на 7 м перед аркою, на 1,2 м; ціль — між грудьми борця й низом екрана; кут 38° — від колін до верху екрана.
                    if (!close && Time.time > HitAt + 2.2f)
                    {
                        close = true;
                        Look(CurtainOut + new Vector3(6.6f, 1.2f, -2.2f), new Vector3(CurtainOut.x - 0.7f, 2.6f, 0f), 38f, true);
                    }
                    yield return null;
                }
            }
            while (Time.time < t0 + hold && !_skip) yield return null;

            // ---- 2. Хода петлею профілю, цілими циклами; зупинка біля залу — на межі циклу коло точки (-8, 0, 0).
            var path = prof.UsesSteps ? Path : RollPath;
            float[] cum = Cumulative(path);
            float total = cum[cum.Length - 1];
            float stopAt = total;
            var barrier = ClipF(prof.Barrier, female);
            if (barrier != null) stopAt = Mathf.Clamp(Mathf.Round(cum[BarrierVertex] / stride) * stride, stride, total - stride);
            if (walk != null && !_skip)
            {
                SetBeat("walk");
                yield return WalkLeg(a, walk, path, cum, 0f, stopAt, stride, true);
                if (barrier != null && !_skip)
                {
                    SetBeat("barrier");
                    _walker = null;
                    a.PlayAtBlend(barrier, 0f, false, a.PosePos, _walkYaw, 0.2f);
                    Vector3 p = a.PosePos;
                    Vector3 fwd = Quaternion.Euler(0f, _walkYaw, 0f) * Vector3.forward;
                    Vector3 right = Quaternion.Euler(0f, 90f, 0f) * fwd;
                    // Збоку, з правого плеча: борець і зала ліворуч від нього в одному кадрі.
                    Look(p + right * 3.0f + fwd * 1.2f + Vector3.up * 1.6f, p - right * 0.6f + Vector3.up * 1.2f, 40f, true);
                    yield return Wait(barrier.length);
                    if (!_skip)
                    {
                        SetBeat("walk");
                        yield return WalkLeg(a, walk, path, cum, stopAt, total, stride, true);
                    }
                }
            }
            _walker = null;

            // ---- 3. Вхід у ринг.
            float climbYaw = RingStage3D.YawTo(Vector3.zero, ClimbDir);
            Vector3 side = Quaternion.Euler(0f, 90f, 0f) * ClimbDir;
            float sp = prof.EntrySpeed > 0f ? prof.EntrySpeed : 1f;
            var entry = ClipF(prof.EntryClip, female);
            if (prof.UsesSteps)
            {
                var climb = ClipF(EntranceProfile.Steps, female);
                if (climb != null && entry != null && !_skip)
                {
                    SetBeat("steps");
                    a.SetHeight(0f);
                    a.PlayAt(climb, false, ClimbStart, climbYaw, sp);
                    // Підйом на метр угору: камера далі й вище, ціль — на рівні голови посеред підйому (огляд 05.11.2026).
                    Vector3 steps = new Vector3(StepsAnchor.x, 0f, StepsAnchor.z);
                    Look(steps + side * 4.2f + ClimbDir * 0.4f + Vector3.up * 2.3f, steps + ClimbDir * 0.8f + Vector3.up * 2.0f, 44f, true);
                    yield return Wait(climb.length / sp);
                    // Кінець підйому: таз над фартухом. Кліп входу відраховує висоту від фартуха (MatY) — слот на рівні мату.
                    SetBeat("entry");
                    Vector3 onApron = a.PosePos;
                    a.SetHeight(RingStage3D.MatY);
                    a.PlayAt(entry, false, new Vector3(onApron.x, RingStage3D.MatY, onApron.z), climbYaw, sp);
                    Vector3 apron = new Vector3(onApron.x, RingStage3D.MatY, onApron.z);
                    if (prof.Entry == EntranceRingEntry.StepsVault)
                        Look(apron + side * 3.4f + Vector3.up * 1.3f - ClimbDir * 0.2f, apron + ClimbDir * 0.4f + Vector3.up * 1.6f, 44f, true);
                    else
                        Look(apron + side * 3.0f + Vector3.up * 1.6f - ClimbDir * 0.6f, apron + ClimbDir * 0.3f + Vector3.up * 1.3f, 44f, true);
                    yield return Wait(entry.length / sp);
                }
            }
            else if (entry != null && !_skip)
            {
                // Закочування: слот на підлозі, кліп сам піднімає таз на мат; камера збоку, нижче верхнього каната.
                SetBeat("entry");
                a.SetHeight(0f);
                a.PlayAt(entry, false, RollStart, 90f, sp);
                // З південного заходу поза рингом, повз кутову стійку (кадр 290 огляду: стійка й оператор закривали борця).
                Look(new Vector3(-3.7f, 1.65f, -3.8f), new Vector3(-2.0f, 1.05f, 0.1f), 42f, true);
                yield return Wait(entry.length / sp);
            }
            if (_skip) { EndEntrance(a); yield break; }
            // Далі — на маті. Після сходів слот уже на висоті мату — перехід у стійку плавний; після закочування слот на
            // підлозі (кліп сам підняв таз на метр) — слот піднімається на мат і стійка стає склейкою в тому ж кадрі (з
            // переходом таз на мить злітав би на метр угору).
            {
                Vector3 here = a.PosePos;
                float yaw = a.PoseYaw;
                var stance = _s.Clip("Ring_Stance_Loop") ?? hype;
                bool rolled = !prof.UsesSteps;
                a.SetHeight(RingStage3D.MatY);
                if (stance != null)
                {
                    if (rolled) a.PlayAt(stance, true, new Vector3(here.x, RingStage3D.MatY, here.z), yaw);
                    else a.PlayAtBlend(stance, 0f, true, new Vector3(here.x, RingStage3D.MatY, here.z), yaw, 0.2f);
                }
            }

            // ---- 4. Поза.
            SetBeat("pose");
            if (fx != null) fx.EntranceRing();
            var pose = ClipF(prof.Pose, female) ?? ClipF(EntranceProfile.PoseFallback, female) ?? hype;
            var idle = _s.Clip("Ring_Stance_Loop") ?? hype;
            var dir = _s.Director;
            Vector3 spot = a.PosePos;
            float spotYaw = a.PoseYaw;
            switch (prof.PoseSpot)
            {
                case EntrancePoseSpot.Center:
                    {
                        // До центру — ходою рингу; курс — той, яким ішов (без розвороту на місці).
                        Vector3 d = new Vector3(-spot.x, 0f, -spot.z);
                        spotYaw = d.sqrMagnitude > 0.04f ? RingStage3D.YawTo(Vector3.zero, d) : spotYaw;
                        spot = new Vector3(0f, RingStage3D.MatY, 0f) - (d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.zero) * 0.25f;
                        break;
                    }
                case EntrancePoseSpot.Turnbuckle:
                    // Обличчям до кутової стійки, за 0,9 м від перетину канатів (кліп сам стає на нижній, тоді на середній канат).
                    spot = CornerNW + ClimbDir * 0.9f;
                    spotYaw = climbYaw + 180f;
                    break;
                case EntrancePoseSpot.Ropes:
                    // Обличчям до західних канатів (до проходу й завіси), площина канатів — за 0,62 м попереду.
                    spot = new Vector3(-2.1f + 0.62f, RingStage3D.MatY, 0.7f);
                    spotYaw = -90f;
                    break;
            }
            spot.y = RingStage3D.MatY;
            if (prof.PoseSpot != EntrancePoseSpot.Here && dir != null && idle != null)
            {
                // Збоку, обидва кінці ходи в кадрі (спереду борець підходив до камери впритул — кадр 094 огляду).
                if (!FrameWalk(a, spot))
                {
                    Vector3 f0 = Quaternion.Euler(0f, a.PoseYaw, 0f) * Vector3.forward;
                    Look(a.PosePos + f0 * 3.2f + Vector3.up * 1.7f, a.PosePos + Vector3.up * 1.2f, 44f, true);
                }
                // Хода — окремою корутиною: пропуск (будь-яка клавіша) зупиняє її одразу, а не після приходу.
                bool walked = false;
                var wco = StartCoroutine(RunThen(dir.WalkFree(a, spot, spotYaw, idle), () => walked = true));
                while (!walked && !_skip) yield return null;
                if (!walked) { StopCoroutine(wco); dir.StopWalkFree(a); }
            }
            if (_skip) { EndEntrance(a); yield break; }
            PoseShot(prof.PoseSpot, a);
            // Кут і канати — геометрія кліпа (перетин канатів за 0,9 м, площина канатів за 0,62 м): старт — точно в задуманій
            // точці; центр і «на місці» — звідти, де стоїть.
            bool exact = prof.PoseSpot == EntrancePoseSpot.Turnbuckle || prof.PoseSpot == EntrancePoseSpot.Ropes;
            if (pose != null)
                a.PlayAtBlend(pose, 0f, false, exact ? spot : a.PosePos, prof.PoseSpot == EntrancePoseSpot.Here ? a.PoseYaw : spotYaw, 0.2f);
            // Велике шоу — фонтани на позі кожного (плейтест 08.11.2026, P1-7); інакше — за бітом 5 профілю.
            if ((prof.Pyro || (fx != null && fx.Big)) && fx != null) fx.RingPyro();
            float poseEnd = Time.time + Mathf.Max(pose != null ? pose.length : 0f, prof.PoseSeconds);
            bool held = false;
            while (Time.time < poseEnd && !_skip)
            {
                // Харизма ≥ 2: поза довша за кліп — тримає стійку на публіку.
                if (!held && pose != null && Time.time > poseEnd - (prof.PoseSeconds - pose.length) && prof.PoseSeconds > pose.length && hype != null)
                {
                    held = true;
                    a.PlayAtBlend(hype, 0f, true, a.PosePos, a.PoseYaw, 0.3f);
                }
                yield return null;
            }

            // ---- 4б. Чемпіон: пояс над головою (кліп тримає обидві кисті над головою).
            var beltClip = prof.Belt ? ClipF(EntranceProfile.BeltRaise, female) : null;
            if (beltClip != null && !_skip)
            {
                SetBeat("belt");
                a.PlayAtBlend(beltClip, 0f, false, a.PosePos, a.PoseYaw, 0.25f);
                BeltOn(a);
                if (!FramePose(a, EntrancePoseSpot.Here, a.PoseYaw, 0f))
                {
                    Vector3 f = Quaternion.Euler(0f, a.PoseYaw, 0f) * Vector3.forward;
                    Look(a.PosePos + f * 2.9f + Vector3.up * 1.35f, a.PosePos + Vector3.up * 1.75f, 38f, true);
                }
                yield return Wait(beltClip.length);
                BeltOff();
            }
            EndEntrance(a);
        }

        private IEnumerator RunThen(IEnumerator it, System.Action done)
        {
            yield return it;
            done();
        }

        private void EndEntrance(RingActor a)
        {
            _walker = null;
            // Пропущений вихід: тема й гонг удару не мають прозвучати вже в матчі (рецензія 08.11.2026).
            _s.CancelEntranceSound();
            BeltOff();
            ScreenOff();
            if (_s.Fx != null) _s.Fx.Match();
            foreach (var other in new[] { _s.Actor(Core.Ring.Match.MatchSide.Player), _s.Actor(Core.Ring.Match.MatchSide.Rival), _s.Referee })
                other.Model.SetActive(true);
            SetBeat("");
            EntranceActor = null;
            CamActive = false;
            Busy = false;
        }

        /// <summary>
        /// План пози й пояса розкладкою кадру (<see cref="RingCine.FrameAny"/>, як кіноплани): стопи, голова й підняті руки (на
        /// канаті — ще +0,86 м) — у кадрі з полями; камера спереду борця, трохи знизу, поза смугою канатів (кадри огляду
        /// 08.11.2026: жорсткий ракурс різав голову). Не вміщується — запасний жорсткий ракурс.
        /// </summary>
        private bool FramePose(RingActor a, EntrancePoseSpot spot, float yaw, float raise)
        {
            Vector3 feet = new Vector3(a.PosePos.x, RingStage3D.MatY + raise, a.PosePos.z);
            float h = Mathf.Max(1.5f, a.HeadPos.y - a.Slot.position.y + 0.12f);
            var pts = new List<Vector3> { feet, feet + Vector3.up * (h + 0.5f) };
            Vector3 head = feet + Vector3.up * (h - 0.1f), hips = feet + Vector3.up * (h * 0.55f);
            bool outside = spot == EntrancePoseSpot.Turnbuckle || spot == EntrancePoseSpot.Ropes;
            float edge = RingStage3D.RingHalf;
            // Центр і «на місці»: спершу камера в рингу (без канатів між нею й борцем), тоді — поза рингом.
            foreach (bool inRing in outside ? new[] { false } : new[] { true, false })
            {
                bool ir = inRing;
                System.Func<Vector3, bool> ok = p =>
                {
                    float m = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z));
                    if (p.y < RingStage3D.MatY + 0.5f || m > 7f) return false;
                    if (ir ? m > edge - 0.45f : m < edge + 0.6f) return false;
                    // Кутова стійка не закриває ні голову, ні таз (кадри 095–098 огляду: стійка на весь кадр замість борця).
                    return !PostBlocks(p, head) && !PostBlocks(p, hips);
                };
                // У рингу місця мало (до канатів ~1,6 м): ширші кути, щоб борець на весь зріст уміщався без канатів у кадрі.
                var fovs = ir ? new[] { 40f, 48f, 56f, 64f } : new[] { 36f, 42f, 48f, 54f };
                foreach (float dy in new[] { 0f, 20f, -20f, 35f, -35f, 55f, -55f })
                {
                    var shot = RingCine.FrameAny(pts, yaw + 180f + dy, -5f, fovs, _s.CamAspect, 0.1f, 0.92f, 0.12f, ok, 1.4f, 7f);
                    if (!shot.Ok) continue;
                    Look(shot.Pos, shot.Look, shot.Fov, true);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Чи кутова стійка рингу (верх — 1,5 м над матом) стоїть між камерою й точкою.</summary>
        private static bool PostBlocks(Vector3 cam, Vector3 target)
        {
            const float r = 0.32f;
            float post = RingStage3D.Venue.RopeLine, top = RingStage3D.MatY + 1.5f;
            Vector2 a = new Vector2(cam.x, cam.z), b = new Vector2(target.x, target.z), ab = b - a;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var c = new Vector2(sx * post, sz * post);
                    float t = Mathf.Clamp01(Vector2.Dot(c - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    if (t <= 0.02f || t >= 0.98f) continue;
                    if ((a + ab * t - c).magnitude > r) continue;
                    float y = Mathf.Lerp(cam.y, target.y, t);
                    if (y < top) return true;
                }
            return false;
        }

        /// <summary>Хода до місця пози — збоку, у кадрі і де борець зараз, і куди йде; стійки не закривають.</summary>
        private bool FrameWalk(RingActor a, Vector3 spot)
        {
            Vector3 from = new Vector3(a.PosePos.x, RingStage3D.MatY, a.PosePos.z), to = new Vector3(spot.x, RingStage3D.MatY, spot.z);
            Vector3 d = to - from;
            d.y = 0f;
            float yaw = d.sqrMagnitude > 1e-4f ? RingStage3D.YawTo(Vector3.zero, d) : a.PoseYaw;
            var pts = new List<Vector3> { from, from + Vector3.up * 2.0f, to, to + Vector3.up * 2.0f };
            Vector3 mid = (from + to) * 0.5f + Vector3.up * 1.4f;
            // Спершу камера в рингу (без каната через кадр — кадри 091–094 огляду), тоді поза рингом.
            foreach (bool inRing in new[] { true, false })
            {
                bool ir = inRing;
                System.Func<Vector3, bool> ok = p =>
                {
                    float m = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z));
                    if (p.y < RingStage3D.MatY + 0.5f || m > 7f) return false;
                    if (ir ? m > RingStage3D.RingHalf - 0.45f : m < RingStage3D.RingHalf + 0.6f) return false;
                    return !PostBlocks(p, mid);
                };
                var fovs = ir ? new[] { 46f, 54f, 62f, 68f } : new[] { 40f, 46f, 52f, 58f };
                foreach (float side in new[] { 90f, -90f, 60f, -60f, 120f, -120f })
                {
                    var shot = RingCine.FrameAny(pts, yaw + side, -3f, fovs, _s.CamAspect, 0.12f, 0.9f, 0.1f, ok, 1.2f, 7f);
                    if (!shot.Ok) continue;
                    Look(shot.Pos, shot.Look, shot.Fov, true);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Камера пози: спереду від борця, трохи знизу; на канаті — з-за кута, на канатах — з-за канатів.</summary>
        private void PoseShot(EntrancePoseSpot spot, RingActor a)
        {
            if (FramePose(a, spot, a.PoseYaw, spot == EntrancePoseSpot.Turnbuckle ? 0.86f : 0f)) return;
            Vector3 p = a.PosePos;
            Vector3 f = Quaternion.Euler(0f, a.PoseYaw, 0f) * Vector3.forward;
            Vector3 r = Quaternion.Euler(0f, 90f, 0f) * f;
            switch (spot)
            {
                case EntrancePoseSpot.Turnbuckle:
                    // Борець стає на другий канат обличчям до залу: камера за кутом, на рівні його грудей.
                    Look(CornerNW - ClimbDir * 2.6f + Vector3.up * 2.0f + r * 0.6f, CornerNW + ClimbDir * 0.5f + Vector3.up * 1.75f, 42f, true);
                    break;
                case EntrancePoseSpot.Ropes:
                    Look(new Vector3(-4.3f, 2.25f, 1.6f), new Vector3(-1.7f, 1.9f, 0.7f), 40f, true);
                    break;
                default:
                    Look(p + f * 3.1f + r * 0.7f + Vector3.up * 1.15f, p + Vector3.up * 1.45f, 40f, true);
                    break;
            }
        }

        private static float[] Cumulative(Vector3[] path)
        {
            var c = new float[path.Length];
            for (int i = 1; i < path.Length; i++) c[i] = c[i - 1] + Vector3.Distance(path[i - 1], path[i]);
            return c;
        }

        private static Vector3 PointAt(Vector3[] path, float[] cum, float s, out float yaw)
        {
            int i = 1;
            while (i < path.Length - 1 && cum[i] < s) i++;
            float seg = cum[i] - cum[i - 1];
            float t = seg > 1e-4f ? Mathf.Clamp01((s - cum[i - 1]) / seg) : 1f;
            yaw = RingStage3D.YawTo(path[i - 1], path[i]);
            return Vector3.Lerp(path[i - 1], path[i], t);
        }

        /// <summary>
        /// Відрізок ходи від <paramref name="s0"/> до <paramref name="s1"/> метрів шляху. Цілими циклами петлі (на межі циклу
        /// стопи стоять — перехід у наступний кліп не ковзає); залишок до точної довжини — поправкою на швидкість шляху,
        /// розподіленою на весь відрізок (правило режисера прев'ю). Таз приколочено до шляху (<see cref="LateUpdate"/>).
        /// </summary>
        private IEnumerator WalkLeg(RingActor a, AnimationClip walk, Vector3[] path, float[] cum, float s0, float s1, float stride, bool camera)
        {
            float D = s1 - s0;
            if (D < 0.05f) yield break;
            int n = Mathf.Max(1, Mathf.RoundToInt(D / stride));
            float dur = n * walk.length / WalkPlay;
            float yaw0;
            _walkPos = PointAt(path, cum, s0, out yaw0);
            if (s0 <= 0f) _walkYaw = yaw0;
            a.Play(walk, true, 0.2f, WalkPlay);
            _walker = a;
            for (float t = 0f; t < dur && !_skip; t += Time.deltaTime)
            {
                float yaw;
                _walkPos = PointAt(path, cum, s0 + D * Mathf.Clamp01(t / dur), out yaw);
                _walkYaw = Mathf.LerpAngle(_walkYaw, yaw, 1f - Mathf.Exp(-8f * Time.deltaTime));
                if (camera) WalkCamera();
                yield return null;
            }
            // Пропуск (будь-яка клавіша) — без доведення таза в кінець відрізка: інакше на кадр до склейки в матч постать
            // стрибала (сезон огляду s15: «телепорт 1,27 м» на виході з-за штори).
            if (!_skip) _walkPos = PointAt(path, cum, s1, out yaw0);
        }

        private void WalkCamera()
        {
            Vector3 fwd = Quaternion.Euler(0f, _walkYaw, 0f) * Vector3.forward;
            Vector3 ahead = _walkPos + fwd * 3.4f + new Vector3(0.9f, 1.7f, 0f);
            if (Mathf.Abs(ahead.x) > 4.2f || Mathf.Abs(ahead.z) > 4.2f)
                Look(ahead, _walkPos + new Vector3(0f, 1.3f, 0f), 38f, false);
            else
            {
                // Біля рингу камера попереду в'їжджала у сходи, фартух і огорожу (огляд 05.11.2026) — тут вона з боку рингу,
                // вище верхнього каната, дивиться на борця згори-збоку.
                Vector3 toRing = new Vector3(-_walkPos.x, 0f, -_walkPos.z).normalized;
                Vector3 cam = _walkPos + toRing * 3.2f + Vector3.up * 3.0f;
                // Над рингом уздовж осі X висить світлова ферма (venue_gym: ±0,3 м по Z, до 5 м заввишки) — камера не ближче
                // 0,9 м до неї (кадр 075 огляду виходів: камера всередині ферми).
                if (Mathf.Abs(cam.x) < 4.8f && Mathf.Abs(cam.z) < 0.9f) cam.z = (cam.z >= 0f ? 1f : -1f) * 0.9f;
                Look(cam, _walkPos + Vector3.up * 1.1f, 42f, false);
            }
        }

        private void LateUpdate()
        {
            // Пояс між кистями: центр — посередині, вздовж лінії кистей, пластини — до залу (уперед від борця).
            if (_belt != null && _beltHolder != null)
            {
                var lh = _beltHolder.GetBone(HumanBodyBones.LeftHand);
                var rh = _beltHolder.GetBone(HumanBodyBones.RightHand);
                if (lh != null && rh != null)
                {
                    Vector3 fwd = Quaternion.Euler(0f, _beltHolder.PoseYaw, 0f) * Vector3.forward;
                    var local = Quaternion.LookRotation(_beltFace, _beltLen);
                    // Над головою (обидві кисті вище голови) — пояс між кистями, пластинами до залу; доти — звисає з правої
                    // руки перед тілом (ENTRANCE_PROFILE.md: «пояс — реквізит на hand_r»). Між кистями навскіс від плеча до
                    // стегна він проходив крізь тулуб (кадр 298 огляду).
                    float headY = _beltHolder.HeadPos.y;
                    float w = Mathf.Clamp01((Mathf.Min(lh.position.y, rh.position.y) - (headY - 0.05f)) / 0.15f);
                    Vector3 len = rh.position - lh.position;
                    Vector3 face = len.sqrMagnitude > 1e-4f ? Vector3.ProjectOnPlane(fwd, len.normalized) : fwd;
                    if (face.sqrMagnitude < 1e-4f) face = fwd;
                    var overhead = Quaternion.LookRotation(face.normalized, len.sqrMagnitude > 1e-4f ? len.normalized : Vector3.up) * Quaternion.Inverse(local);
                    var hang = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Inverse(local);
                    Vector3 posOver = (lh.position + rh.position) * 0.5f;
                    Vector3 posHang = rh.position + fwd * 0.12f + Vector3.down * 0.55f;
                    var rot = Quaternion.Slerp(hang, overhead, w);
                    _belt.transform.rotation = rot;
                    _belt.transform.position = Vector3.Lerp(posHang, posOver, w) - rot * _beltCenter;
                }
            }
        }

        /// <summary>
        /// Таз ходака приколочено до точки шляху: слот зсувається на стільки, на скільки кліп ушов уперед, тож стопи стоять,
        /// поки кліп іде, а на петлі (таз стрибає назад на хід) слот у тому ж кадрі стрибає вперед. Кличе сцена одразу після
        /// тіку постатей і до фаз-тесту й камери (власний LateUpdate ішов у довільному порядку з тіком — на перемотуванні
        /// петлі таз на кадр стрибав на 0,19 м, сезон огляду s15).
        /// </summary>
        public void PinWalker()
        {
            if (_walker == null) return;
            var a = _walker;
            Vector3 hip = a.PosePos - a.Slot.position;
            hip.y = 0f;
            float yawOff = Mathf.DeltaAngle(a.Slot.eulerAngles.y, a.PoseYaw);
            var rot2 = Quaternion.Euler(0f, _walkYaw - yawOff, 0f);
            Vector3 local2 = Quaternion.Inverse(a.Slot.rotation) * hip;
            Vector3 p = _walkPos - rot2 * local2;
            a.Slot.SetPositionAndRotation(new Vector3(p.x, 0f, p.z), rot2);
        }

        // ============================ пояс ============================

        private void BeltOn(RingActor a)
        {
            if (_s.assets.beltProp == null) return;
            if (_belt == null)
            {
                _belt = Instantiate(_s.assets.beltProp, transform);
                _belt.name = "Пояс чемпіона";
                var t = _s.assets.wrestlerTemplate;
                foreach (var r in _belt.GetComponentsInChildren<Renderer>())
                {
                    var m = t != null ? new Material(t) : new Material(r.sharedMaterial);
                    m.name = "belt";
                    if (_s.assets.beltAlbedo != null) m.SetTexture("_BaseMap", _s.assets.beltAlbedo);
                    if (_s.assets.beltNormal != null) { m.SetTexture("_BumpMap", _s.assets.beltNormal); m.EnableKeyword("_NORMALMAP"); }
                    r.sharedMaterial = m;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                // Осі пояса — з його меж у власній системі: найдовша вісь — довжина, найтонша — бік пластин (пластини дають
                // зсув меж від початку координат у свій бік: bbox y −0,004…+0,016 у props.json).
                _belt.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var b = new Bounds(Vector3.zero, Vector3.zero);
                bool any = false;
                foreach (var r in _belt.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                Vector3 e = b.extents;
                int li = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
                int ti = e.x <= e.y && e.x <= e.z ? 0 : e.y <= e.z ? 1 : 2;
                _beltLen = Axis(li);
                float sideSign = Mathf.Sign(b.max[ti] + b.min[ti]);
                _beltFace = Axis(ti) * (sideSign == 0f ? 1f : sideSign);
                _beltCenter = new Vector3(0f, 0f, 0f);
            }
            _beltHolder = a;
            _belt.SetActive(true);
        }

        private static Vector3 Axis(int i) { return i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward; }

        private void BeltOff()
        {
            _beltHolder = null;
            if (_belt != null) _belt.SetActive(false);
        }

        // ============================ вступ ============================

        /// <summary>
        /// Вступ перед меню (~20 с): порожня темна зала, камера пролітає від стелі над рядами до рингу, вмикається світло,
        /// екран показує назву гри (рецепт B01 — окремий від екранів виходу). Будь-яка клавіша пропускає (<see cref="Skip"/>).
        /// </summary>
        public void BeginIntro(string title, string sub)
        {
            StopAllCoroutines();
            BeltOff();
            _walker = null;
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
            ScreenOff();
            _titleLayer.style.display = DisplayStyle.Flex;

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
            ShowTitle(title, sub, RingTheme.Crimson);
            if (_s.Fx != null) _s.Fx.TitleHit();
            yield return Wait(2.4f);
            if (!_skip)
            {
                hero.Model.SetActive(true);
                // Вихід героя у вступі — базовий профіль (шоумен-герой), без ролика: на екрані лишається назва гри.
                var prof = EntranceProfile.Compose(new EntranceInput { Player = true, Gimmick = Gimmick.Showman, Path = Core.Ring.RingPath.Face });
                Recipe = null;
                Profile = prof;
                HitIn = 0;
                HitAt = Time.time;
                yield return Entrance(hero, prof, RingTheme.Crimson, _s.IsFemale(hero), prof.Bpm, false);
                Busy = true;
            }
            ShowTitle(title, sub, RingTheme.Crimson);
            _s.Referee.Model.SetActive(true);
            _s.Actor(Core.Ring.Match.MatchSide.Rival).Model.SetActive(true);
            hero.Model.SetActive(true);
            CamActive = false;
            Busy = false;
        }

        /// <summary>
        /// Відкриття великого шоу перед виходами (плейтест 08.11.2026, P1-7: велике шоу знімалося тим самим планом, що й
        /// щотижневе): пролітка над рядами до рингу, назва вечора на екрані над завісою, іскри й CO2, фонтани на стійках. Борці
        /// й рефері на цей час сховані. Пропуск — будь-яка клавіша.
        /// </summary>
        public void BeginOpen(string title, string sub)
        {
            StopAllCoroutines();
            BeltOff();
            _walker = null;
            _skip = false;
            StartCoroutine(Open(title, sub));
        }

        private IEnumerator Open(string title, string sub)
        {
            Busy = true;
            var hero = _s.Actor(Core.Ring.Match.MatchSide.Player);
            var rival = _s.Actor(Core.Ring.Match.MatchSide.Rival);
            _s.Referee.Model.SetActive(false);
            hero.Model.SetActive(false);
            rival.Model.SetActive(false);
            ScreenOff();
            _titleLayer.style.display = DisplayStyle.Flex;
            ShowTitle(title, sub, RingTheme.Gold);
            // З протилежної від камери матчу трибуни (північ) — на ринг, далі збоку вздовж рядів — на завісу (інакше, ніж вступ
            // гри з півдня). Зала спортзалу: x −18…18, z −12…14, стеля 8 м, ферма PAR — 6,2 м над кутами рингу: камера нижче 6,1 м
            // і далі 7 м від центру, поки вище ферми.
            Vector3[] fly = { new Vector3(-5f, 6.0f, 11.5f), new Vector3(5f, 5.0f, 10.5f), new Vector3(10f, 3.6f, 4.0f) };
            float seg = 2.4f;
            for (int i = 0; i + 1 < fly.Length && !_skip; i++)
                for (float t = 0f; t < seg && !_skip; t += Time.deltaTime)
                {
                    float sm = Mathf.SmoothStep(0f, 1f, t / seg);
                    Vector3 look = Vector3.Lerp(new Vector3(0f, 1.2f, 0f), CurtainTop + new Vector3(0f, -1.6f, 0f), Mathf.Clamp01((i + sm) / 2f));
                    Look(Vector3.Lerp(fly[i], fly[i + 1], sm), look, 46f, i == 0 && t == 0f);
                    if (i == 1 && t == 0f && _s.Fx != null) { _s.Fx.TitleHit(); _s.Fx.RingPyro(); }
                    yield return null;
                }
            if (!_skip) yield return Wait(1.6f);
            _s.Referee.Model.SetActive(true);
            hero.Model.SetActive(true);
            rival.Model.SetActive(true);
            ScreenOff();
            CamActive = false;
            Busy = false;
        }

        // ============================ закулісся ============================

        /// <summary>
        /// Закулісся (тиждень): роздягальня окремою кімнатою поза залою — шафки, лавка, лампа, двері; герой і букер (постать
        /// букера дає сцена — <see cref="SetBooker"/>). Камера — фіксований ракурс з кута кімнати. Знову на ринг —
        /// <see cref="LeaveBackstage"/>.
        /// </summary>
        public void ShowBackstage()
        {
            StopAllCoroutines();
            Busy = false;
            if (_backstage == null) BuildBackstage();
            _backstage.SetActive(true);
            var hero = _s.Actor(Core.Ring.Match.MatchSide.Player);
            Vector3 o = BackstageOrigin;
            _s.Place(hero, o + HeroSpot, 70f, "Ring_Stance_Loop");
            if (_booker != null) _s.Place(_booker, o + BookerSpot, -110f, "Ref_Idle_Loop");
            Look(o + new Vector3(0.6f, 1.75f, -3.6f), o + new Vector3(0f, 1.2f, 0.4f), 46f, true);
        }

        // Місця в роздягальні від BackstageOrigin: герой біля лавки, букер біля свого столу; гість, що стоїть поруч із букером
        // (подія з обома), — ближче до дверей, обличчям до героя.
        private static readonly Vector3 HeroSpot = new Vector3(-0.7f, 0f, 0.2f);
        private static readonly Vector3 BookerSpot = new Vector3(0.8f, 0f, 0.5f);
        private static readonly Vector3 GuestBesideSpot = new Vector3(1.4f, 0f, -0.5f);

        /// <summary>Мовець букера, чия постать стоїть у роздягальні (null — ще нікого).</summary>
        public string BookerId { get; private set; }

        /// <summary>Постаті роздягальні — букер і гість події (огляд облич; null — немає).</summary>
        public RingActor Booker { get { return _booker; } }
        public RingActor Guest { get { return _guest; } }

        /// <summary>Огляд (RingStageDemo): камера показу склейкою на точку — крупні плани людей у роздягальні.</summary>
        public void ReviewLook(Vector3 pos, Vector3 look, float fov) { Look(pos, look, fov, true); }

        /// <summary>
        /// Постать букера федерації (№ 10 рішень 12.11.2026): попередня зникає, нова — біля столу; поки в сцені гість на місці
        /// букера, вона схована.
        /// </summary>
        public void SetBooker(RingActor booker, string speaker)
        {
            if (_booker != null) { _booker.Destroy(); Destroy(_booker.Slot.gameObject); }
            _booker = booker;
            BookerId = speaker;
            _lastTalker = null;
            if (booker == null) return;
            if (_backstage != null) booker.Slot.SetParent(_backstage.transform, true);
            _s.Place(booker, BackstageOrigin + BookerSpot, -110f, "Ref_Idle_Loop");
            booker.Model.SetActive(_guest == null || _guestBeside);
        }

        public bool InBackstage { get { return _backstage != null && _backstage.activeSelf; } }

        /// <summary>Щойно вийшли з закулісся — камера рингу має стати на місце різко (RingStage3D читає й скидає).</summary>
        public bool LeftBackstage { get; set; }

        /// <summary>
        /// Камера розмови в роздягальні: той, хто говорить, — через плече героя; герой — через плече останнього, хто до нього
        /// говорив; оповідач — загальний план. Хто говорить: гість події (його мовець) або букер; у сцені з гостем на місці
        /// букера — гість за будь-кого, крім героя й оповідача.
        /// </summary>
        public void BackstageSpeaker(string speaker)
        {
            if (!InBackstage || (_booker == null && _guest == null)) return;
            var hero = _s.Actor(Core.Ring.Match.MatchSide.Player);
            RingActor talker = null;
            if (speaker != "player" && speaker != "narrator" && speaker != null)
                talker = _guest != null && (!_guestBeside || speaker == _guestSpeaker || _booker == null) ? _guest : _booker;
            if (talker != null) _lastTalker = talker;
            var other = _lastTalker ?? _guest ?? _booker;
            Vector3 b = other.HeadPos, h = hero.HeadPos;
            Vector3 across = Vector3.Cross(Vector3.up, (b - h).normalized);
            if (talker != null) Look(h + (h - b).normalized * 0.9f + across * 0.45f + Vector3.up * 0.1f, b, 34f, true);
            else if (speaker == "player") Look(b + (b - h).normalized * 0.9f - across * 0.45f + Vector3.up * 0.1f, h, 34f, true);
            else { var o = BackstageOrigin; Look(o + new Vector3(0.6f, 1.75f, -3.6f), o + new Vector3(0f, 1.2f, 0.4f), 46f, false); }
        }

        private RingActor _guest;
        private string _guestKey, _guestSpeaker;
        private bool _guestBeside;
        private RingActor _lastTalker;
        private Light _lamp;

        /// <summary>
        /// Хто в кадрі сцени закулісся: розмова — букер федерації; подія — гість на його місці (Барон на парковці, Сталевий у
        /// лазареті, ведуча інтерв'ю, скаут із контрактом, менеджерка), букера немає; <paramref name="beside"/> — подія, де
        /// говорять обидва (зрада напарника): гість стоїть поруч, букер лишається. <paramref name="speaker"/> — мовець гостя
        /// (камера на того, хто говорить). Парковка — холодна лампа, що мерехтить.
        /// </summary>
        public void SetGuest(RingActor guest, string key, bool parking, string speaker = null, bool beside = false)
        {
            if (_guestKey == key) return;
            if (_guest != null) { _guest.Destroy(); Destroy(_guest.Slot.gameObject); _guest = null; }
            _guestKey = key;
            _guest = guest;
            _guestSpeaker = speaker;
            _guestBeside = guest != null && beside;
            _lastTalker = null;
            Vector3 o = BackstageOrigin;
            if (_booker != null) _booker.Model.SetActive(guest == null || _guestBeside);
            if (guest != null)
            {
                guest.Slot.SetParent(_backstage.transform, true);
                if (_guestBeside)
                {
                    Vector3 at = o + GuestBesideSpot;
                    _s.Place(guest, at, RingStage3D.YawTo(at, o + HeroSpot), "Ref_Idle_Loop");
                }
                else _s.Place(guest, o + BookerSpot, -110f, "Ref_Idle_Loop");
            }
            if (_lamp != null)
            {
                _lamp.color = parking ? new Color(0.62f, 0.75f, 1f) : new Color(1f, 0.86f, 0.62f);
                _flicker = parking;
            }
        }

        private bool _flicker;

        public void LeaveBackstage()
        {
            if (InBackstage) LeftBackstage = true;
            if (_backstage != null) _backstage.SetActive(false);
            CamActive = false;
        }

        public void TickBooker(float dt) { if (_booker != null) _booker.Tick(dt); }

        private void BuildBackstage()
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
            // Букер — постать федерації кар'єри, її ставить сцена (RingStage3D.ShowBackstage → SetBooker; BackstageCast).
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
