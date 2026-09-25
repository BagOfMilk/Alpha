using System.Collections.Generic;
using Game.Core.Characters.Creation;
using Game.Gameplay.UI;
using Game.Gameplay.Walk;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Герой на мапі села — прогулянка у дусі CRPG (власник, 25.09.2026: «Я
    /// хотів шоб я міг бігати як у CRPG»). Поки <see cref="GameShell.Exploring"/>,
    /// ходить за WASD/стрілками (відносно ізометричної камери) або за кліком
    /// мишею по землі — шлях в обхід хат рахує <see cref="WalkGrid"/>; Shift —
    /// біг. Камера йде слідом, коліщатко наближає. Біля місця (пост, ділянка,
    /// орієнтир) пише його в <see cref="GameShell.SetNearbyPlace"/> — сама
    /// оболонка показує «E — зайти» і відкриває вкладку.
    ///
    /// Жодної логіки гри: компонент лише переносить фігурку і камеру; усе, що
    /// можна «вирішити», вирішується тими самими вкладками хаба. Не лінтується
    /// (Camera/Renderer/Input/плейбли — глибокий рушій), як і VillageStage.
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

        /// <summary>Підписи місць видно лише в цьому радіусі від героя — решта кадру не захаращена.</summary>
        public float labelRadius = 7f;
        public float minZoom = 4f;
        public float maxZoom = 11f;

        private const float Cell = 0.25f;

        private GameShell _shell;
        private WalkGrid _grid;
        private readonly List<WalkPlace> _places = new List<WalkPlace>();
        private List<WalkPoint> _path = new List<WalkPoint>();
        private int _pathIndex;
        private bool _pathRuns;
        private float _gridAge = float.MaxValue;

        private bool _camReady;
        private Vector3 _camHomePos;
        private float _camHomeSize;
        private Vector3 _camOffset;
        private float _zoom;

        private GUIStyle _labelStyle;
        private GUIStyle _nearStyle;

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
            RebuildPlaces();
            RebuildGrid();
        }

        private void Update()
        {
            if (_shell == null) _shell = FindAnyObjectByType<GameShell>();
            if (_shell == null) return;

            SyncModel();

            float gait = 0f;
            bool exploring = _shell.Exploring;
            if (exploring)
            {
                _gridAge += Time.deltaTime;
                if (_gridAge > 1.5f) RebuildGrid();
                if (!_shell.EscapeOpen) gait = MoveHero();
                _shell.SetNearbyPlace(VillagePlaces.Nearest(_places, Here()));
                var here = Here();
                _shell.WalkDebug = "герой (" + here.X.ToString("0.00") + "; " + here.Z.ToString("0.00") + ")"
                                   + ", вільно: " + (_grid != null && _grid.IsFree(here))
                                   + ", маршрут " + _pathIndex + "/" + _path.Count
                                   + ", запит: " + _lastRequest
                                   + ", поруч: " + (_shell.NearbyPlace != null ? _shell.NearbyPlace.Id : "-");
            }
            else
            {
                _path.Clear();
                _pathIndex = 0;
            }

            var anim = ActiveAnimation();
            if (anim != null) anim.Gait = gait;
            UpdateCamera(exploring);
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

            bool real = !_ignoreRealInput;
            bool run = real && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            float ix = !real ? 0f : (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                       - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float iz = !real ? 0f : (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                       - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);

            if ((ix != 0f || iz != 0f) && hubCamera != null && _grid != null)
            {
                _path.Clear();
                _pathIndex = 0;
                var f = hubCamera.transform.forward; f.y = 0f; f.Normalize();
                var r = hubCamera.transform.right; r.y = 0f; r.Normalize();
                var dir = (r * ix + f * iz).normalized;
                float speed = run ? runSpeed : walkSpeed;
                var next = _grid.Slide(pos, dir.x * speed * dt, dir.z * speed * dt);
                Face(dir);
                SetHere(next);
                return run ? 2f : 1f;
            }

            if (real && Input.GetMouseButtonDown(0) && !PointerOverUi())
            {
                WalkPoint target;
                if (GroundUnderMouse(out target)) StartPath(pos, target, run);
            }

            string request = _shell.ConsumeWalkRequest();
            if (request != null)
            {
                var place = VillagePlaces.Find(_places, request);
                if (place != null) StartPath(pos, new WalkPoint(place.X, place.Z), false);
                _lastRequest = request + (place == null ? " (місця немає)" : " (шлях " + _path.Count + ")");
                if (place == null || _path.Count == 0)
                    Debug.LogWarning("[Прогулянка] запит «" + request + "» не дав шляху: " + _lastRequest);
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

        // ===================== світ =====================

        /// <summary>
        /// Перешкоди — габарити моделей під <see cref="obstacleRoots"/>: стіни
        /// хат, млин, ліс, добудовані ділянки. Плоске (земля, дороги) і все, що
        /// висить над головою (дахи, підписи), не заважає. Перебудовується
        /// раз на півтори секунди — ділянки добудовуються під час гри.
        /// </summary>
        private void RebuildGrid()
        {
            _gridAge = 0f;
            _grid = new WalkGrid(areaMin.x, areaMin.y, areaMax.x, areaMax.y, Cell);
            BlockRoots(obstacleRoots, trunksOnly: false);
            BlockRoots(trunkRoots, trunksOnly: true);
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

        private void RebuildPlaces()
        {
            _places.Clear();
            if (postsRoot != null)
                foreach (Transform child in postsRoot)
                    if (child.name.StartsWith("post:"))
                    {
                        var place = VillagePlaces.Post(child.name.Substring(5), child.position.x, child.position.z);
                        if (place != null) _places.Add(place);
                    }

            if (plotsRoot != null)
                foreach (Transform child in plotsRoot)
                    if (child.name.StartsWith("plot:"))
                    {
                        // Центр ділянки — там, де висить її підпис (KitBuilder.Plot).
                        var label = child.Find("label");
                        var center = label != null ? label.position : child.position;
                        float half = label != null
                            ? Mathf.Max(Mathf.Abs(label.localPosition.x), Mathf.Abs(label.localPosition.z))
                            : 1f;
                        _places.Add(VillagePlaces.Plot(child.name.Substring(5), center.x, center.z, half));
                    }

            if (landmarksRoot != null)
                foreach (Transform child in landmarksRoot)
                {
                    if (child.name == "place:" + VillagePlaces.NoticeBoardId)
                        _places.Add(VillagePlaces.NoticeBoard(child.position.x, child.position.z));
                    else if (child.name == "place:" + VillagePlaces.TrainingGroundId)
                        _places.Add(VillagePlaces.TrainingGround(child.position.x, child.position.z));
                }
        }

        private void SyncModel()
        {
            bool female = _shell.ProtagonistGender == Gender.Female;
            if (maleModel != null && maleModel.activeSelf == female) maleModel.SetActive(!female);
            if (femaleModel != null && femaleModel.activeSelf != female) femaleModel.SetActive(female);
        }

        private FigureAnimation ActiveAnimation()
        {
            var model = femaleModel != null && femaleModel.activeSelf ? femaleModel : maleModel;
            return model != null ? model.GetComponent<FigureAnimation>() : null;
        }

        // ===================== камера =====================

        private void UpdateCamera(bool exploring)
        {
            if (!_camReady || hubCamera == null || !hubCamera.isActiveAndEnabled) return;

            Vector3 targetPos;
            float targetSize;
            if (exploring)
            {
                float wheel = _ignoreRealInput ? 0f : Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f && !PointerOverUi())
                    _zoom = Mathf.Clamp(_zoom - wheel * 0.8f, minZoom, maxZoom);
                targetPos = transform.position + _camOffset;
                targetSize = _zoom;
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

        // ===================== підписи місць =====================

        /// <summary>Назви місць над ними — лише на прогулянці; найближче підсвічене.</summary>
        private void OnGUI()
        {
            if (_shell == null || !_shell.Exploring || hubCamera == null || !hubCamera.isActiveAndEnabled) return;
            GUI.depth = 10; // під інтерфейсом оболонки
            if (_labelStyle == null)
            {
                GUI.skin = AlphaSkin.Build();
                _labelStyle = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                _nearStyle = new GUIStyle(_labelStyle) { fontStyle = FontStyle.Bold };
                _nearStyle.normal.textColor = new Color(1f, 0.75f, 0.35f);
            }

            var g = _shell.ProtagonistGender;
            var near = _shell.NearbyPlace;
            var here = Here();
            for (int i = 0; i < _places.Count; i++)
            {
                var place = _places[i];
                bool isNear = near != null && near.Id == place.Id;
                if (!isNear && WalkPoint.Distance(here, new WalkPoint(place.X, place.Z)) > labelRadius) continue;
                var screen = hubCamera.WorldToScreenPoint(new Vector3(place.X, 1.8f, place.Z));
                if (screen.z < 0f) continue;
                string text = VillagePlaces.LabelFor(place, g);
                var style = isNear ? _nearStyle : _labelStyle;
                var size = style.CalcSize(new GUIContent(text));
                var rect = new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y, size.x, size.y);
                if (OverlapsUi(rect)) continue; // під шапкою, стрічкою чи нижньою панеллю — не видно
                GUI.Label(rect, text, style);
            }
        }
    }
}
