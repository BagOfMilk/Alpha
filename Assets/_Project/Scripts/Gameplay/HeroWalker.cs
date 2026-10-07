using System.Collections.Generic;
using Game.Core.Characters.Creation;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.UI;
using Game.Gameplay.Walk;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Герой у селі й у кімнаті будівлі (власник, 25.09.2026: «Я хотів шоб я
    /// міг бігати як у CRPG»; 30.09.2026: «досі неможна зайти до будівлі і
    /// поговорити з персонажем»). Ходить за WASD/стрілками (відносно
    /// ізометричної камери) або за кліком мишею — шлях рахує
    /// <see cref="WalkGrid"/>; Shift — біг, коліщатко — наближення. Клік по
    /// будівлі, людині чи станції — підійти й взаємодіяти (як «E»). Біля дверей
    /// зведеної будівлі — увійти: затемнення, сіра кімната зі станціями
    /// (<see cref="Interiors"/>), вихід — двері.
    ///
    /// Жодної логіки гри: компонент лише переносить фігурку й камеру і пише
    /// місця в оболонку; що відкриває місце — вирішують чисті моделі
    /// (<see cref="VillagePlaces"/>, <see cref="BuildingCatalog"/>). Не
    /// лінтується (Camera/Renderer/Input — глибокий рушій), як і VillageStage.
    /// </summary>
    public sealed class HeroWalker : MonoBehaviour
    {
        [Header("Сцена")]
        public Camera hubCamera;
        public GameObject maleModel;
        public GameObject femaleModel;
        public Transform postsRoot;
        public Transform plotsRoot;
        public Transform landmarksRoot;
        public Transform villagersRoot;
        /// <summary>Сіра кімната (<see cref="Interiors"/>): діти «slot:0..4» — п'єдестали станцій.</summary>
        public Transform interiorRoot;
        public Transform[] obstacleRoots;
        /// <summary>Дерева: блокує лише стовбур, а не крону — під кроною пройти можна.</summary>
        public Transform[] trunkRoots;

        [Header("Рух")]
        public float walkSpeed = 2.2f;
        public float runSpeed = 4.4f;
        public float heroRadius = 0.3f;
        public Vector2 areaMin = new Vector2(-10.4f, -8.4f);
        public Vector2 areaMax = new Vector2(11.4f, 7.4f);

        /// <summary>Поворот моделі відносно напрямку руху (градуси): фігурки набору дивляться вздовж +Z.</summary>
        public float modelYawOffset;

        [Header("Камера")]
        public float exploreZoom = 4.5f;
        public float interiorZoom = 4.2f;

        /// <summary>Підписи місць — лише найближчого, того, що під курсором, і в огляді (Tab); кадр не захаращений.</summary>
        public float labelRadius = 2.4f;
        public float minZoom = 4f;
        public float maxZoom = 11f;

        private const float Cell = 0.25f;
        private const float FadeSeconds = 0.25f;

        private GameShell _shell;
        private WalkGrid _grid;
        private readonly List<WalkPlace> _places = new List<WalkPlace>();
        private readonly List<Pickable> _pickables = new List<Pickable>();
        private readonly List<WalkPlace> _pickablePlaces = new List<WalkPlace>();
        private List<WalkPoint> _path = new List<WalkPoint>();
        private int _pathIndex;
        private bool _pathRuns;
        private float _worldAge = float.MaxValue;

        /// <summary>Місце, до якого герой іде, щоб з ним взаємодіяти (клік по будівлі чи людині).</summary>
        private string _pendingInteract;
        private WalkPlace _hovered;

        /// <summary>Будівля, в кімнаті якої герой; null — село.</summary>
        private string _inside;
        private Vector3 _villageReturn;

        private enum Transition { None, FadeOutToEnter, FadeOutToExit, FadeIn }
        private Transition _transition;
        private string _transitionTarget;
        private float _fade;

        private bool _camReady;
        private Vector3 _camHomePos;
        private float _camHomeSize;
        private Vector3 _camOffset;
        private float _zoom;

        private GUIStyle _labelStyle;
        private GUIStyle _nearStyle;
        private GUIStyle _markStyle;
        private GUIStyle _titleStyle;
        private Texture2D _black;

        /// <summary>
        /// Автотур веде героя лише запитами шляху: справжні миша й клавіатура
        /// вимкнені. Вікно туру з'являється на екрані власника, і випадковий
        /// клік по ньому скасовував маршрут — тур падав «герой не дійшов».
        /// </summary>
        private bool _ignoreRealInput;
        private string _lastRequest = "-";

        private void Start()
        {
            _ignoreRealInput = AutoplayBootstrap.RequestedFromCommandLine();
            _zoom = exploreZoom;
            if (hubCamera != null)
            {
                _camHomePos = hubCamera.transform.position;
                _camHomeSize = hubCamera.orthographicSize;
                // Точка землі в центрі кадру за замовчуванням: камера тримає той
                // самий зсув від героя, що й від неї, — ракурс не міняється.
                var forward = hubCamera.transform.forward;
                float t = Mathf.Abs(forward.y) > 0.01f ? -_camHomePos.y / forward.y : 40f;
                _camOffset = _camHomePos - (_camHomePos + forward * t);
                _camReady = true;
            }
            ShowInteriorSlots(0);
        }

        private void Update()
        {
            if (_shell == null) _shell = FindAnyObjectByType<GameShell>();
            if (_shell == null) return;

            SyncModel();

            // Стан пішов з ранку (почався день, бій) — з кімнати виходимо одразу, без затемнення.
            if (_inside != null && !_shell.CanExplore) LeaveInterior(instant: true);

            float gait = 0f;
            bool exploring = _shell.Exploring;
            if (exploring)
            {
                _worldAge += Time.deltaTime;
                if (_worldAge > 1.5f) RebuildWorld();
                StepTransition();
                if (_transition == Transition.None)
                {
                    if (_shell.PendingEnter != null) BeginEnter(_shell.PendingEnter);
                    else if (_shell.PendingExit && _inside != null) BeginExit();
                }
                if (!_shell.EscapeOpen && _transition == Transition.None) gait = MoveHero();
                var near = VillagePlaces.Nearest(_places, Here());
                _shell.SetNearbyPlace(near);
                if (_pendingInteract != null && near != null && near.Id == _pendingInteract && _pathIndex >= _path.Count)
                {
                    _pendingInteract = null;
                    _shell.Interact(near);
                }
                // Рядок налагодження будується лише тоді, коли його читають (автотур при збої),
                // а не щокадру — Статут PERF-01: жодних алокацій на кадр без потреби.
                if (_shell.WalkDebugSource == null) _shell.WalkDebugSource = DescribeWalk;
            }
            else
            {
                _path.Clear();
                _pathIndex = 0;
                _pendingInteract = null;
            }

            var anim = ActiveAnimation();
            if (anim != null) anim.Gait = gait;
            Footsteps(gait);
            UpdateCamera(exploring);
        }

        // Кроки (віха M1.19): такт на кожен крок — частіше, коли біжить; у будівлі — по дошках.
        private float _stepTimer;

        private void Footsteps(float gait)
        {
            if (gait < 0.3f) { _stepTimer = 0f; return; }
            _stepTimer -= Time.deltaTime;
            if (_stepTimer > 0f) return;
            _stepTimer = gait > 1.5f ? 0.3f : 0.45f;
            SoundSettings.Request(_inside != null ? SoundCue.FootstepWood : SoundCue.FootstepGrass);
        }

        // ===================== рух =====================

        private float MoveHero()
        {
            float dt = Time.deltaTime;
            var pos = Here();
            if (_grid != null && !_grid.IsFree(pos))
            {
                pos = _grid.NearestFreePoint(pos);
                SetHere(pos);
            }

            bool real = !_ignoreRealInput && !PlaytestLog.NoteOpen; // тестер пише нотатку — WASD не веде героя
            bool run = real && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            float ix = !real ? 0f : (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                       - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float iz = !real ? 0f : (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                       - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);

            if ((ix != 0f || iz != 0f) && hubCamera != null && _grid != null)
            {
                _path.Clear();
                _pathIndex = 0;
                _pendingInteract = null;
                var f = hubCamera.transform.forward; f.y = 0f; f.Normalize();
                var r = hubCamera.transform.right; r.y = 0f; r.Normalize();
                var dir = (r * ix + f * iz).normalized;
                float speed = run ? runSpeed : walkSpeed;
                var next = _grid.Slide(pos, dir.x * speed * dt, dir.z * speed * dt);
                Face(dir);
                SetHere(next);
                return run ? 2f : 1f;
            }

            _hovered = real && !PointerOverUi() ? PickUnderMouse() : null;
            if (real && Input.GetMouseButtonDown(0) && !PointerOverUi())
            {
                if (_hovered != null)
                {
                    // Клік по місцю: підійти до нього (до дверей — для будівлі) і взаємодіяти, як «E».
                    StartPath(pos, new WalkPoint(_hovered.X, _hovered.Z), run);
                    _pendingInteract = _hovered.Id;
                }
                else
                {
                    WalkPoint target;
                    if (GroundUnderMouse(out target)) StartPath(pos, target, run);
                    _pendingInteract = null;
                }
            }

            string request = _shell.ConsumeWalkRequest();
            if (request != null)
            {
                var place = VillagePlaces.Find(_places, request);
                if (place != null) StartPath(pos, new WalkPoint(place.X, place.Z), false);
                _lastRequest = request + (place == null ? " (місця немає)" : " (шлях " + _path.Count + ")");
                if (place == null || _path.Count == 0)
                    Debug.LogWarning("[Село] запит «" + request + "» не дав шляху: " + _lastRequest);
            }

            if (_pathIndex < _path.Count)
            {
                bool running = _pathRuns || run;
                float speed = running ? runSpeed : walkSpeed;
                float step = speed * dt;
                while (_pathIndex < _path.Count && step > 0f)
                {
                    var target = _path[_pathIndex];
                    float dx = target.X - pos.X, dz = target.Z - pos.Z;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist > 0.001f) Face(new Vector3(dx, 0f, dz));
                    if (dist <= step)
                    {
                        pos = target;
                        step -= dist;
                        _pathIndex++;
                    }
                    else
                    {
                        pos = new WalkPoint(pos.X + dx / dist * step, pos.Z + dz / dist * step);
                        step = 0f;
                    }
                }
                SetHere(pos);
                return running ? 2f : 1f;
            }
            return 0f;
        }

        private void StartPath(WalkPoint from, WalkPoint to, bool run)
        {
            if (_grid == null) return;
            _path = _grid.FindPath(from, to);
            _pathIndex = 0;
            _pathRuns = run;
        }

        private WalkPoint Here()
        {
            var p = transform.position;
            return new WalkPoint(p.x, p.z);
        }

        private void SetHere(WalkPoint p)
        {
            transform.position = new Vector3(p.X, transform.position.y, p.Z);
        }

        private void Face(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            var target = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, modelYawOffset, 0f);
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, k);
        }

        private bool GroundUnderMouse(out WalkPoint point)
        {
            point = default(WalkPoint);
            if (hubCamera == null) return false;
            var ray = hubCamera.ScreenPointToRay(Input.mousePosition);
            var ground = new Plane(Vector3.up, Vector3.zero);
            float distance;
            if (!ground.Raycast(ray, out distance)) return false;
            var hit = ray.GetPoint(distance);
            point = new WalkPoint(hit.x, hit.z);
            return true;
        }

        /// <summary>Місце під курсором: промінь камери проти коробок місць (<see cref="WorldPicker"/>, без фізики).</summary>
        private WalkPlace PickUnderMouse()
        {
            if (hubCamera == null || _pickables.Count == 0) return null;
            var ray = hubCamera.ScreenPointToRay(Input.mousePosition);
            int index = WorldPicker.PickIndex(ray.origin.x, ray.origin.y, ray.origin.z,
                ray.direction.x, ray.direction.y, ray.direction.z, _pickables, out float _);
            return index >= 0 ? _pickablePlaces[index] : null;
        }

        private bool OverlapsUi(Rect rect)
        {
            var rects = _shell.ExploreUiRects;
            for (int i = 0; i < rects.Count; i++)
                if (rects[i].Overlaps(rect)) return true;
            return false;
        }

        private bool PointerOverUi()
        {
            var mouse = Input.mousePosition;
            var gui = new Vector2(mouse.x, Screen.height - mouse.y);
            var rects = _shell.ExploreUiRects;
            for (int i = 0; i < rects.Count; i++)
                if (rects[i].Contains(gui)) return true;
            return false;
        }

        // ===================== світ: місця і перешкоди =====================

        /// <summary>
        /// Місця й перешкоди раз на півтори секунди: ділянки добудовуються,
        /// люди стають на пости й ідуть на вилазки — двері й люди з'являються
        /// самі. У кімнаті — станції й вихід цієї будівлі.
        /// </summary>
        private void RebuildWorld()
        {
            _worldAge = 0f;
            _places.Clear();
            _pickables.Clear();
            _pickablePlaces.Clear();
            if (_inside != null)
            {
                _places.AddRange(Interiors.PlacesFor(_inside));
                foreach (var p in _places) AddPickable(p, 0.5f, 1.1f);
                RebuildInteriorGrid();
            }
            else
            {
                BuildVillagePlaces();
                RebuildVillageGrid();
            }
            _shell.SetPlaces(new List<WalkPlace>(_places));
        }

        private void BuildVillagePlaces()
        {
            var session = _shell.Session;
            var city = session != null ? session.GetCityView() : null;
            var roster = session != null ? session.GetRosterView() : null;

            var plots = new List<PlotAnchor>();
            var plotModels = new Dictionary<string, Transform>();
            if (plotsRoot != null)
                foreach (Transform child in plotsRoot)
                {
                    if (!child.name.StartsWith("plot:")) continue;
                    string id = child.name.Substring(5);
                    var label = child.Find("label");
                    var door = child.Find("door");
                    var center = label != null ? label.position : child.position;
                    float half = label != null ? Mathf.Max(Mathf.Abs(label.localPosition.x), Mathf.Abs(label.localPosition.z)) : 1f;
                    var doorAt = door != null ? door.position : center;
                    plots.Add(new PlotAnchor { BuildingId = id, CenterX = center.x, CenterZ = center.z, Half = half, DoorX = doorAt.x, DoorZ = doorAt.z });
                    var model = child.Find("model");
                    if (model != null) plotModels[id] = model;
                }

            var openAir = new Dictionary<string, WalkPoint>();
            var postAnchors = PostAnchors();
            foreach (var station in BuildingCatalog.OpenAirStations)
            {
                WalkPoint at;
                if (station.PostId != null && postAnchors.TryGetValue(station.PostId, out at)) openAir[station.Id] = at;
            }
            WalkPoint? board = null, training = null;
            if (landmarksRoot != null)
                foreach (Transform child in landmarksRoot)
                {
                    if (child.name == "place:" + VillagePlaces.NoticeBoardId) board = new WalkPoint(child.position.x, child.position.z);
                    else if (child.name == "place:" + VillagePlaces.TrainingGroundId) training = new WalkPoint(child.position.x, child.position.z);
                }

            _places.AddRange(VillagePlaces.BuildVillage(plots, openAir, board, training,
                id => { bool built; return UxBricks.Stage(city, id, out built); }));

            foreach (var spot in VillagePeople.Arrange(roster, PostFigures(), IdleSpots()))
                _places.Add(VillagePlaces.Person(spot.CompanionId, spot.X, spot.Z));

            foreach (var p in _places)
            {
                Transform model;
                if ((p.Kind == PlaceKind.Building || p.Kind == PlaceKind.Plot) && plotModels.TryGetValue(p.BuildingId, out model)
                    && model.gameObject.activeInHierarchy && TryBounds(model, out Bounds b))
                {
                    _pickables.Add(new Pickable(p.Id, p.Kind == PlaceKind.Plot ? PickKind.Plot : PickKind.Building,
                        b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z));
                    _pickablePlaces.Add(p);
                }
                else if (p.Kind == PlaceKind.Plot)
                    AddPickable(p, 1f, 0.2f);
                else
                    AddPickable(p, p.Kind == PlaceKind.Person ? 0.35f : 0.6f, p.Kind == PlaceKind.Person ? 1.1f : 1.6f);
            }
        }

        private void AddPickable(WalkPlace p, float half, float height)
        {
            var kind = p.Kind == PlaceKind.Exit ? PickKind.Exit : p.Kind == PlaceKind.Landmark ? PickKind.Landmark : PickKind.Station;
            _pickables.Add(new Pickable(p.Id, kind, p.LabelX - half, 0f, p.LabelZ - half, p.LabelX + half, height, p.LabelZ + half));
            _pickablePlaces.Add(p);
        }

        private Dictionary<string, WalkPoint> PostAnchors()
        {
            var anchors = new Dictionary<string, WalkPoint>();
            if (postsRoot != null)
                foreach (Transform child in postsRoot)
                    if (child.name.StartsWith("post:"))
                        anchors[child.name.Substring(5)] = new WalkPoint(child.position.x, child.position.z);
            return anchors;
        }

        /// <summary>Де стоїть фігура працівника кожного поста (villager:&lt;postId&gt;).</summary>
        private Dictionary<string, WalkPoint> PostFigures()
        {
            var figures = new Dictionary<string, WalkPoint>();
            if (villagersRoot != null)
                foreach (Transform child in villagersRoot)
                    if (child.name.StartsWith("villager:"))
                        figures[child.name.Substring(9)] = new WalkPoint(child.position.x, child.position.z);
            return figures;
        }

        /// <summary>Місця біля вогнища Віча (idle:0..3) — там стоять ті, хто без поста.</summary>
        private List<WalkPoint> IdleSpots()
        {
            var spots = new List<WalkPoint>();
            if (villagersRoot == null) return spots;
            for (int i = 0; i < VillagePeople.IdleSpotCount; i++)
            {
                var spot = villagersRoot.Find("idle:" + i);
                if (spot != null) spots.Add(new WalkPoint(spot.position.x, spot.position.z));
            }
            return spots;
        }

        private static bool TryBounds(Transform root, out Bounds bounds)
        {
            bounds = default(Bounds);
            var renderers = root.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) return false;
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        /// <summary>
        /// Перешкоди села — габарити моделей під <see cref="obstacleRoots"/>:
        /// стіни хат, млин, ліс, добудовані ділянки. Плоске (земля, дороги) і
        /// все, що висить над головою (дахи), не заважає.
        /// </summary>
        private void RebuildVillageGrid()
        {
            _grid = new WalkGrid(areaMin.x, areaMin.y, areaMax.x, areaMax.y, Cell);
            BlockRoots(obstacleRoots, trunksOnly: false);
            BlockRoots(trunkRoots, trunksOnly: true);
        }

        /// <summary>Кімната: межі підлоги й п'єдестали станцій.</summary>
        private void RebuildInteriorGrid()
        {
            _grid = new WalkGrid(Interiors.MinX + 0.35f, Interiors.MinZ + 0.35f, Interiors.MaxX - 0.35f, Interiors.MaxZ - 0.35f, Cell);
            var building = BuildingCatalog.Get(_inside);
            int n = building != null ? Mathf.Min(building.Stations.Count, Interiors.SlotCount) : 0;
            float half = Interiors.StationSize * 0.5f + heroRadius;
            for (int i = 0; i < n; i++)
            {
                var slot = Interiors.Slot(i);
                _grid.Block(slot.X - half, slot.Z - half, slot.X + half, slot.Z + half);
            }
        }

        private void BlockRoots(Transform[] roots, bool trunksOnly)
        {
            if (roots == null) return;
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null || !root.gameObject.activeInHierarchy) continue;
                var renderers = root.GetComponentsInChildren<Renderer>(false);
                for (int j = 0; j < renderers.Length; j++)
                {
                    var b = renderers[j].bounds;
                    // Трава, дрібне каміння, дороги — під ногами; дахи й підписи — над головою.
                    if (b.max.y < 0.45f || b.min.y > 0.9f) continue;
                    if (trunksOnly)
                    {
                        const float trunk = 0.2f;
                        float cx = b.center.x, cz = b.center.z;
                        _grid.Block(cx - trunk - heroRadius, cz - trunk - heroRadius, cx + trunk + heroRadius, cz + trunk + heroRadius);
                    }
                    else
                    {
                        _grid.Block(b.min.x - heroRadius, b.min.z - heroRadius, b.max.x + heroRadius, b.max.z + heroRadius);
                    }
                }
            }
        }

        // ===================== вхід і вихід =====================

        private void BeginEnter(string buildingId)
        {
            SoundSettings.Request(SoundCue.DoorOpen);
            var building = BuildingCatalog.Get(buildingId);
            if (building == null || !building.Enterable) { _shell.MarkVillage(); return; }
            _transition = Transition.FadeOutToEnter;
            _transitionTarget = buildingId;
            _path.Clear();
            _pathIndex = 0;
            _pendingInteract = null;
        }

        private void BeginExit()
        {
            SoundSettings.Request(SoundCue.DoorClose);
            _transition = Transition.FadeOutToExit;
            _path.Clear();
            _pathIndex = 0;
            _pendingInteract = null;
        }

        /// <summary>Затемнення ≤ 0,4 с (UX_DESIGN §6, компонент 18): темніє → переносимо → світлішає.</summary>
        private void StepTransition()
        {
            if (_transition == Transition.None) return;
            float step = Time.unscaledDeltaTime / FadeSeconds;
            if (_ignoreRealInput) step = 1f; // автотур не чекає кіно
            if (_transition == Transition.FadeIn)
            {
                _fade = Mathf.Max(0f, _fade - step);
                if (_fade <= 0f) _transition = Transition.None;
                return;
            }
            _fade = Mathf.Min(1f, _fade + step);
            if (_fade < 1f) return;
            if (_transition == Transition.FadeOutToEnter) EnterInterior(_transitionTarget);
            else LeaveInterior(instant: false);
            _transition = Transition.FadeIn;
        }

        private void EnterInterior(string buildingId)
        {
            _villageReturn = transform.position;
            var door = VillagePlaces.Find(_places, VillagePlaces.BuildingPrefix + buildingId);
            if (door != null) _villageReturn = new Vector3(door.X, transform.position.y, door.Z);
            _inside = buildingId;
            var building = BuildingCatalog.Get(buildingId);
            ShowInteriorSlots(building != null ? building.Stations.Count : 0);
            var spawn = Interiors.Spawn;
            SetHere(spawn);
            transform.rotation = Quaternion.Euler(0f, modelYawOffset, 0f);
            _shell.MarkInterior(buildingId);
            _worldAge = float.MaxValue;
            RebuildWorld();
            SnapCamera();
        }

        private void LeaveInterior(bool instant)
        {
            if (_inside == null) { _shell.MarkVillage(); return; }
            _inside = null;
            ShowInteriorSlots(0);
            transform.position = _villageReturn;
            _shell.MarkVillage();
            _worldAge = float.MaxValue;
            if (_shell.Exploring) RebuildWorld();
            if (_grid != null && !_grid.IsFree(Here())) SetHere(_grid.NearestFreePoint(Here()));
            if (instant) { _transition = Transition.None; _fade = 0f; }
            SnapCamera();
        }

        private void ShowInteriorSlots(int count)
        {
            if (interiorRoot == null) return;
            interiorRoot.gameObject.SetActive(count > 0 || _inside != null);
            for (int i = 0; i < Interiors.SlotCount; i++)
            {
                var slot = interiorRoot.Find("slot:" + i);
                if (slot != null) slot.gameObject.SetActive(i < count);
            }
        }

        // Поправка №19: герой — постать із модульного набору за образом зі створення й надітим.
        private Game.Gameplay.Characters.KitFigure _kit;
        private int _kitLogCount = -1;
        private List<string> _kitEquip;

        private bool SyncKit()
        {
            var session = _shell.Session;
            if (session == null) return false;
            if (_kit == null)
            {
                var holder = new GameObject("kit");
                holder.transform.SetParent(transform, false);
                // Корінь героя повернутий на modelYawOffset під фігурки Kenney — постать набору дивиться прямо.
                holder.transform.localRotation = Quaternion.Euler(0f, -modelYawOffset, 0f);
                holder.layer = gameObject.layer;
                _kit = holder.AddComponent<Game.Gameplay.Characters.KitFigure>();
            }
            if (_kitEquip == null || session.DayLog.Count != _kitLogCount)
            {
                var sheet = session.GetCharacterSheet(GameSession.ProtagonistId);
                _kitEquip = Game.Gameplay.UI.InventoryModel.VisualKeys(sheet != null ? sheet.Equipment : null);
                _kitLogCount = session.DayLog.Count;
            }
            return _kit.Show(session.GetAppearance(GameSession.ProtagonistId), _kitEquip,
                Game.Gameplay.UI.CharacterAnimState.Idle, 1f, 0f, gameObject.layer);
        }

        private void SyncModel()
        {
            if (SyncKit())
            {
                if (maleModel != null && maleModel.activeSelf) maleModel.SetActive(false);
                if (femaleModel != null && femaleModel.activeSelf) femaleModel.SetActive(false);
                return;
            }
            bool female = _shell.ProtagonistGender == Gender.Female;
            if (maleModel != null && maleModel.activeSelf == female) maleModel.SetActive(!female);
            if (femaleModel != null && femaleModel.activeSelf != female) femaleModel.SetActive(female);
        }

        private FigureAnimation ActiveAnimation()
        {
            if (_kit != null && _kit.Model != null) return _kit.Animation;
            var model = femaleModel != null && femaleModel.activeSelf ? femaleModel : maleModel;
            return model != null ? model.GetComponent<FigureAnimation>() : null;
        }

        // ===================== камера =====================

        /// <summary>Стан героя одним рядком для логу автотуру: позиція, кімната, маршрут, найближче місце.</summary>
        private string DescribeWalk()
        {
            var here = Here();
            return "герой (" + here.X.ToString("0.00") + "; " + here.Z.ToString("0.00") + ")"
                   + (_inside != null ? " у «" + _inside + "»" : "")
                   + ", вільно: " + (_grid != null && _grid.IsFree(here))
                   + ", маршрут " + _pathIndex + "/" + _path.Count
                   + ", запит: " + _lastRequest
                   + ", поруч: " + (_shell != null && _shell.NearbyPlace != null ? _shell.NearbyPlace.Id : "-");
        }

        private void UpdateCamera(bool exploring)
        {
            if (!_camReady || hubCamera == null || !hubCamera.isActiveAndEnabled) return;

            Vector3 targetPos;
            float targetSize;
            if (exploring)
            {
                float wheel = _ignoreRealInput ? 0f : Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f && !PointerOverUi() && _inside == null)
                    _zoom = Mathf.Clamp(_zoom - wheel * 0.8f, minZoom, maxZoom);
                targetPos = transform.position + _camOffset;
                targetSize = _inside != null ? interiorZoom : _zoom;
            }
            else
            {
                targetPos = _camHomePos;
                targetSize = _camHomeSize;
            }

            float k = 1f - Mathf.Exp(-6f * Time.deltaTime);
            hubCamera.transform.position = Vector3.Lerp(hubCamera.transform.position, targetPos, k);
            hubCamera.orthographicSize = Mathf.Lerp(hubCamera.orthographicSize, targetSize, k);
        }

        /// <summary>Після перенесення камера стає на місце одразу — інакше кадр «їхав» би через пів світу.</summary>
        private void SnapCamera()
        {
            if (!_camReady || hubCamera == null) return;
            hubCamera.transform.position = transform.position + _camOffset;
            hubCamera.orthographicSize = _inside != null ? interiorZoom : _zoom;
        }

        // ===================== підписи місць =====================

        /// <summary>
        /// Один підпис на місце (UX_DESIGN §4.8): найближче, під курсором і в
        /// огляді (Tab); над людиною, якій є що сказати, — «!» (UX-15).
        /// </summary>
        private void OnGUI()
        {
            if (_shell == null || !_shell.Exploring || hubCamera == null || !hubCamera.isActiveAndEnabled) return;
            GUI.depth = 10; // під інтерфейсом оболонки
            EnsureStyles();

            var g = _shell.ProtagonistGender;
            var near = _shell.NearbyPlace;
            var here = Here();
            RosterView roster = null;
            var city = _shell.Session != null ? _shell.Session.GetCityView() : null;
            // Спершу те, що поруч і під курсором, потім решта; підпис, що налазить
            // на вже намальований, пропускаємо — нагромадження написів нечитне.
            _labelOrder.Clear();
            for (int i = 0; i < _places.Count; i++)
            {
                var p = _places[i];
                if ((near != null && near.Id == p.Id) || (_hovered != null && _hovered.Id == p.Id)) _labelOrder.Insert(0, p);
                else _labelOrder.Add(p);
            }
            _drawnLabels.Clear();
            for (int i = 0; i < _labelOrder.Count; i++)
            {
                var place = _labelOrder[i];
                bool isNear = near != null && near.Id == place.Id;
                bool isHovered = _hovered != null && _hovered.Id == place.Id;
                bool hasNews = place.Kind == PlaceKind.Person && UxTalkPanel.HasSomethingToSay(_shell, place.TargetId);
                // У кімнаті місць мало — підписані всі; у селі — лише поруч.
                bool inRange = _inside != null || WalkPoint.Distance(here, new WalkPoint(place.X, place.Z)) <= labelRadius;
                if (!isNear && !isHovered && !_shell.Overview && !inRange && !hasNews) continue;

                float height = place.Kind == PlaceKind.Person ? 1.5f : (place.Kind == PlaceKind.Building || place.Kind == PlaceKind.Plot ? 2.6f : 1.8f);
                var screen = hubCamera.WorldToScreenPoint(new Vector3(place.LabelX, height, place.LabelZ));
                if (screen.z < 0f) continue;

                if (!isNear && !isHovered && !_shell.Overview && !inRange && hasNews)
                {
                    DrawMark(screen);
                    continue;
                }

                if (roster == null && _shell.Session != null) roster = _shell.Session.GetRosterView();
                bool built;
                int stage = string.IsNullOrEmpty(place.BuildingId) ? 0 : UxBricks.Stage(city, place.BuildingId, out built);
                string text = VillagePlaces.Describe(place, g, stage, roster);
                var style = isNear || isHovered ? _nearStyle : _labelStyle;
                var size = style.CalcSize(new GUIContent(text));
                var rect = new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y, size.x, size.y);
                if (OverlapsUi(rect)) continue; // під шапкою, стрічкою, панеллю чи нижньою смугою — не видно
                if (OverlapsDrawn(rect)) continue;
                _drawnLabels.Add(rect);
                GUI.Label(rect, text, style);
                if (hasNews) DrawMark(new Vector3(screen.x, screen.y + size.y, screen.z));
            }

            if (_inside != null)
            {
                string title = UkrainianText(g, "building." + _inside);
                var size = _titleStyle.CalcSize(new GUIContent(title));
                var rect = new Rect((Screen.width - size.x) * 0.5f, Screen.height * 0.16f, size.x, size.y);
                if (!OverlapsUi(rect)) GUI.Label(rect, title, _titleStyle);
            }

            if (_fade > 0f)
            {
                var previous = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, _fade);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _black);
                GUI.color = previous;
            }
        }

        private readonly List<WalkPlace> _labelOrder = new List<WalkPlace>();
        private readonly List<Rect> _drawnLabels = new List<Rect>();

        private bool OverlapsDrawn(Rect rect)
        {
            for (int i = 0; i < _drawnLabels.Count; i++)
                if (_drawnLabels[i].Overlaps(rect)) return true;
            return false;
        }

        private void DrawMark(Vector3 screen)
        {
            var rect = new Rect(screen.x - 14f, Screen.height - screen.y - 34f, 28f, 30f);
            if (!OverlapsUi(rect)) GUI.Label(rect, "!", _markStyle);
        }

        private static string UkrainianText(Gender g, string key) => Game.Gameplay.Text.UkrainianText.Get(key, g);

        private void EnsureStyles()
        {
            if (_labelStyle != null) return;
            GUI.skin = AlphaSkin.Build();
            // UI v2 (шкурка BG3): мітка місця — компактна панель-підказка, а не
            // велика панель вікна з кутовими шпильками; найближче — світла бронза.
            _labelStyle = new GUIStyle(AlphaSkin.TooltipPanel)
            {
                fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = false,
                padding = new RectOffset(10, 10, 4, 4)
            };
            _nearStyle = new GUIStyle(_labelStyle) { font = AlphaSkin.StrongFont };
            _nearStyle.normal.textColor = AlphaSkin.BronzeHi;
            _markStyle = new GUIStyle(AlphaSkin.TooltipPanel) { fontSize = 20, alignment = TextAnchor.MiddleCenter, font = AlphaSkin.StrongFont };
            _markStyle.normal.textColor = AlphaSkin.BronzeHi;
            _titleStyle = new GUIStyle(AlphaSkin.TooltipPanel)
            {
                fontSize = 26, alignment = TextAnchor.MiddleCenter, wordWrap = false,
                padding = new RectOffset(16, 16, 6, 6), font = AlphaSkin.StrongFont
            };
            _black = new Texture2D(1, 1);
            _black.SetPixel(0, 0, Color.white);
            _black.Apply();
        }
    }
}
