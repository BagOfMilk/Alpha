using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using Game.Gameplay.UI;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Один набір бойових one-shot кліпів для конкретної моделі Kenney Mini
    /// Characters (Бій v2, docs/COMBAT_V2.md §3): idle/walk/sprint — той самий
    /// мікс, що й раніше; решта — одноразові такти. <see cref="Game.Gameplay.EditorTools.BattleArenaBuilder"/>
    /// будує масив паралельно пулу префабів (той самий індекс — та сама
    /// модель), контролер бере пару "префаб/кліпи" за одним і тим самим
    /// детермінованим хешем id юніта.
    /// </summary>
    public sealed class BattleCharacterClips
    {
        public AnimationClip Idle;
        public AnimationClip Walk;
        public AnimationClip Sprint;
        public AnimationClip AttackMelee;
        public AnimationClip HoldingShoot;
        public AnimationClip Die;
        public AnimationClip Interact;
        public AnimationClip Crouch;
    }

    /// <summary>
    /// Бій v2 — 3D-презентація тактичного бою (docs/COMBAT_V2.md): реалізує
    /// <see cref="IBattlePresenter"/> і повний контракт <see cref="IBattleHudData"/>/
    /// <see cref="IBattleInput"/> над <c>GameSession.GetBattleView()</c>.
    /// Будує грид/юнітів з <see cref="Game.Gameplay.EditorTools.BattleArenaBuilder"/>-заготовлених
    /// префабів Kenney, читає мишу через <c>Physics.Raycast</c>, виконує
    /// <c>GameSession.Combat*</c>.
    ///
    /// ЛІНТ-ВИКЛЮЧЕНО (Physics/Renderer/Camera/Collider/Playable — та сама
    /// причина, що в <c>VillageStage</c>/<c>GameSceneBuilder</c>, §5
    /// TEST_BUILD.md): заглушка UnityEngine чесно не покриває цю глибину API.
    /// Уся розкладкова математика — окремо, у <see cref="BattleArenaView"/> і
    /// <see cref="BattleTurnDirector"/>/<see cref="BattleTactParser"/> (чистий
    /// C#, і лінтяться, і вкриті тестами).
    ///
    /// РЕЖИСЕР ХОДУ ВОРОГА (§5, головна скарга власника «гра тупо
    /// зупинилась»): <see cref="BattleTurnDirector"/> вирішує, коли презентер
    /// сам кличе <c>GameSession.CombatAiStepOneAction()</c> — раніше цей
    /// виклик не звучав ЖОДНОГО разу поза автопрогоном.
    /// </summary>
    public sealed class BattleArenaController : MonoBehaviour, IBattlePresenter, IBattleHudData
    {
        // ================= камера: константи спільні з Editor (докорінно §4) =================

        public const float CameraTiltDegrees = 52f;
        public const float InitialYawDegrees = 45f;

        // ================= призначається BattleArenaBuilder (Editor) =================

        public GameObject ArenaRoot;
        public Camera ArenaCamera;
        public string HubCameraName = "HubCamera";
        public string HubRootName = "World/Hub";

        public GameObject[] MaleCharacterPrefabs = new GameObject[0];
        public GameObject[] FemaleCharacterPrefabs = new GameObject[0];
        public GameObject[] CoverHalfPrefabs = new GameObject[0];
        public GameObject[] CoverFullPrefabs = new GameObject[0];
        public GameObject TileGroundPrefab;

        /// <summary>Бій v2: набори бойових кліпів — той самий індекс, що відповідний елемент <see cref="MaleCharacterPrefabs"/>/<see cref="FemaleCharacterPrefabs"/>.</summary>
        public BattleCharacterClips[] MaleClipSets = new BattleCharacterClips[0];
        public BattleCharacterClips[] FemaleClipSets = new BattleCharacterClips[0];

        // ================= рантайм-стан: сцена =================

        private GameSession _session;
        private bool _active;
        private bool _initialized;

        private Transform _tileRoot;
        private Transform _unitRoot;
        private Transform _propRoot;
        private Material _tileMaterial;
        private Camera _hubCamera;
        private GameObject _hubRoot;

        private BattleView _lastView;
        private int _gridWidth, _gridHeight;
        private Gender _protagonistGender = Gender.Male;
        private readonly Dictionary<string, GameObject> _tileObjects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, MaterialPropertyBlock> _tileBlocks = new Dictionary<string, MaterialPropertyBlock>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> _unitObjects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, MaterialPropertyBlock> _unitBlocks = new Dictionary<string, MaterialPropertyBlock>(StringComparer.Ordinal);
        private readonly Dictionary<string, Renderer[]> _unitRenderers = new Dictionary<string, Renderer[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, FigureAnimation> _unitAnimations = new Dictionary<string, FigureAnimation>(StringComparer.Ordinal);
        private readonly Dictionary<string, BattleCharacterClips> _unitClipSets = new Dictionary<string, BattleCharacterClips>(StringComparer.Ordinal);

        /// <summary>Видима позиція/поворот юніта — може відставати від логічної (<c>BattleUnitView.Pos</c>) на час такту руху (§6).</summary>
        private readonly Dictionary<string, Vector3> _unitVisualPos = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        private readonly Dictionary<string, Quaternion> _unitVisualRot = new Dictionary<string, Quaternion>(StringComparer.Ordinal);

        private sealed class HitReaction { public Color FlashColor; public Vector3 RecoilDir; public float StartTime; public float Duration; }
        private readonly Dictionary<string, HitReaction> _unitHitReactions = new Dictionary<string, HitReaction>(StringComparer.Ordinal);

        private ArmedAction _armed = ArmedAction.None;
        private string _armedAbilityId;

        /// <summary>Справжнє наведення миші (Physics.Raycast) — <see cref="HoveredUnitId"/>/<see cref="HasHoveredTile"/> читають ЦЕ або симульоване, дивись <see cref="_hasSimulatedHover"/>.</summary>
        private GridPos? _hoveredTile;
        private string _hoveredUnitId;
        private Vector3 _lastMousePosition;
        private bool _lastMousePositionKnown;

        /// <summary>Бій v2 §7: наведення "як мишею" для автотуру/знімків — справжній рух миші скидає його (<see cref="UpdateHover"/>).</summary>
        private bool _hasSimulatedHover;
        private string _simulatedHoverUnitId;
        private GridPos? _simulatedHoverTile;

        private int _hoveredHitChance;
        private int _hoveredDamageMin, _hoveredDamageMax, _hoveredDamageCrit;

        /// <summary>Масштаб моделі юніта відносно вихідного розміру Kenney Mini Characters.</summary>
        private const float UnitVisualScale = 1.35f;

        private bool _resultPending;
        private string _resultOutcomeKey;
        private string _resultRounds;
        private readonly List<string> _resultCasualtyLines = new List<string>();
        private readonly List<string> _logLines = new List<string>();
        private readonly List<BattleLogEntryUi> _logEntries = new List<BattleLogEntryUi>();
        private const int MaxLogLines = 40;

        private int _battleLogCursor;
        private int _lastKnownDayLogCount;
        private string _lastRejectionText = string.Empty;

        // ================= Бій v2: HUD-прямокутники (§7.4 SetHudRects) =================

        private IReadOnlyList<Rect> _hudRects = Array.Empty<Rect>();

        // ================= Бій v2: намір гравця =================

        private bool _fastEnemyTurns;
        private bool _enemyAiEnabled = true;

        // ================= Бій v2: режисер ходу ворога =================

        private readonly BattleTurnDirector _turnDirector = new BattleTurnDirector();

        // ================= Бій v2: такти (§6) =================

        private enum TactKind { Move, Attack, Ability, DownedOrDeath, Skip }

        private sealed class PendingTact { public BattleLogLineView Entry; public TactKind Kind; }

        private sealed class ActiveTact
        {
            public BattleLogLineView Entry;
            public TactKind Kind;
            public string ActorId;
            public string TargetId;
            public IReadOnlyList<GridPosView> Path;
            public int PathIndex;
            public float Duration;
            public float Elapsed;
            public bool FloatingSpawned;
        }

        private readonly Queue<PendingTact> _pendingTacts = new Queue<PendingTact>();
        private ActiveTact _activeTact;

        // ================= Бій v2: камера (§4) =================

        private const float CameraDistance = 24f;
        private const float CameraMinZoom = 4f;
        private const float CameraMaxZoom = 12f;
        private const float CameraPanSpeed = 5f;
        private const float CameraFlyDuration = 0.45f;
        private const float CameraPanMargin = 3f;

        private float _cameraYawDegrees = InitialYawDegrees;
        private float _cameraZoom = 8f;
        private Vector3 _cameraPanFocus;
        private Vector3 _cameraFlyStart;
        private Vector3? _cameraFlyTarget;
        private float _cameraFlyElapsed;
        private bool _cameraInitialized;

        // ================= Бій v2: банер ходу (§3, §6) =================

        private BattleTurnBanner _banner;
        private float _bannerTimer;
        private string _lastBannerUnitId = "@none@";

        // ================= Бій v2: спливаючі написи (§6) =================

        /// <summary>Бій v2, раунд 2 (п.6): запас над верхом імені/HP-смужки юніта, щоб напис не стартував їх перекриваючи.</summary>
        private const float FloatingSpawnGapAboveNamePx = 16f;

        /// <summary>Бій v2, раунд 2 (п.6): «піднімаються помітно (~40–60 px)» — 42px/с даю ~42px за звичайні 1.0с і ~59px за Big 1.4с.</summary>
        private const float FloatingRiseSpeedPxPerSecond = 42f;

        /// <summary>
        /// Напис прив'язаний до ТОЧКИ У СВІТІ над ціллю (World), а екранна позиція
        /// перераховується щокадру: раніше вона бралась один раз при появі, і
        /// під час перельоту камери напис «відставав» від цілі на півекрана
        /// (знімки раунду 2: «Промах»/«Влучання» посеред поля).
        /// </summary>
        private sealed class FloatingRuntime { public BattleFloatingText Data; public Vector3 World; public float Age; public float Duration; }
        private readonly List<FloatingRuntime> _floatingRuntime = new List<FloatingRuntime>();
        private readonly List<BattleFloatingText> _floatingTextsExposed = new List<BattleFloatingText>();

        // ================= Бій v2: оверлеї над юнітами (§6) =================

        private readonly List<BattleUnitOverlay> _overlays = new List<BattleUnitOverlay>();

        // ================= Бій v2: приціл дозору / тайли під загрозою (§5) =================

        private readonly HashSet<string> _overwatchAimTileKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _overwatchThreatTileKeys = new HashSet<string>(StringComparer.Ordinal);

        // ================= Бій v2: прев'ю атаки/шляху (кеш за наміром, §6) =================

        private AttackPreviewView _hoverAttackCache;
        private MovePathView _hoverPathCache;
        private (string current, string hovered, ArmedAction armed, string ability, int logCount, string tile)? _hoverKey;

        // ================= Бій v2: лінія пострілу =================

        private LineRenderer _shotLine;
        private float _shotLineUntil;

        // ================= Бій v2, раунд 2: лінія руху до наведеного тайла (§2) =================

        private const float PathLineWidth = 0.08f;
        private const float PathLineHeight = 0.08f;

        private LineRenderer _pathLine;
        private GameObject _pathEndMarker;
        private Material _emissiveMaterial;
        private readonly List<Vector3> _pathLinePoints = new List<Vector3>();

        // ================= публічний зріз для IBattleHudData =================

        public BattleView View => _lastView;
        public bool ResultPending => _resultPending;
        public string ResultOutcomeKey => _resultOutcomeKey;
        public string ResultRounds => _resultRounds;
        public IReadOnlyList<string> ResultCasualtyLines => _resultCasualtyLines;
        public IReadOnlyList<string> LogLines => _logLines;
        public IReadOnlyList<BattleLogEntryUi> LogEntries => _logEntries;
        public ArmedAction Armed => _armed;
        public string ArmedAbilityId => _armedAbilityId;

        public string HoveredUnitId => _hasSimulatedHover ? _simulatedHoverUnitId : _hoveredUnitId;
        public bool HasHoveredTile => _hasSimulatedHover ? _simulatedHoverTile.HasValue : _hoveredTile.HasValue;
        public int HoveredTileX => (_hasSimulatedHover ? _simulatedHoverTile : _hoveredTile)?.X ?? 0;
        public int HoveredTileY => (_hasSimulatedHover ? _simulatedHoverTile : _hoveredTile)?.Y ?? 0;

        /// <summary>
        /// Бій v2, раунд 2 (§7.4, HoveredTileScreenX/Y): екранна точка центру
        /// наведеного тайла — та сама проекція (<see cref="WorldToGui"/>), що
        /// й оверлеї над юнітами, трохи над землею, щоб не тонула в самому
        /// тайлі. Має сенс лише коли <see cref="HasHoveredTile"/>; інакше —
        /// (0,0), як і решта Hovered*-полів без наведення.
        /// </summary>
        public float HoveredTileScreenX => HasHoveredTile ? HoveredTileScreenPoint().x : 0f;
        public float HoveredTileScreenY => HasHoveredTile ? HoveredTileScreenPoint().y : 0f;

        public int HoveredHitChance => _hoveredHitChance;
        public int HoveredDamageMin => _hoveredDamageMin;
        public int HoveredDamageMax => _hoveredDamageMax;
        public int HoveredDamageCrit => _hoveredDamageCrit;

        public AttackPreviewView HoverAttack { get { RefreshHoverCaches(); return _hoverAttackCache; } }
        public MovePathView HoverPath { get { RefreshHoverCaches(); return _hoverPathCache; } }

        public IReadOnlyList<BattleUnitOverlay> Overlays => _overlays;
        public IReadOnlyList<BattleFloatingText> FloatingTexts => _floatingTextsExposed;
        public BattleTurnBanner Banner => _banner;

        public bool FastEnemyTurns { get => _fastEnemyTurns; set => _fastEnemyTurns = value; }
        public bool EnemyAiEnabled { get => _enemyAiEnabled; set => _enemyAiEnabled = value; }
        public string LastRejectionText => _lastRejectionText;

        public bool IsBusy => _activeTact != null || _pendingTacts.Count > 0;

        public bool IsPlayerTurn
        {
            get
            {
                var unit = CurrentUnit();
                return unit != null && string.Equals(unit.Side, "Player", StringComparison.Ordinal);
            }
        }

        public string ResolveDisplayName(BattleUnitView unit) => ResolveDisplayNameInternal(unit);

        public void SetHudRects(IReadOnlyList<Rect> guiRects) => _hudRects = guiRects ?? Array.Empty<Rect>();

        // ================= IBattlePresenter =================

        public bool IsActive => _active;

        public void Enter(GameSession session)
        {
            EnsureInitialized();

            _session = session;
            _active = true;
            _armed = ArmedAction.None;
            _armedAbilityId = null;
            _resultPending = false;
            _resultCasualtyLines.Clear();
            _logLines.Clear();
            _logEntries.Clear();
            _battleLogCursor = 0;
            _lastRejectionText = string.Empty;
            _pendingTacts.Clear();
            _activeTact = null;
            _turnDirector.Reset();
            _banner = null;
            _lastBannerUnitId = "@none@";
            _floatingRuntime.Clear();
            _floatingTextsExposed.Clear();
            _unitVisualPos.Clear();
            _unitVisualRot.Clear();
            _unitHitReactions.Clear();
            _hasSimulatedHover = false;
            _simulatedHoverUnitId = null;
            _simulatedHoverTile = null;
            _lastMousePositionKnown = false;
            _hoverKey = null;
            _cameraInitialized = false;

            _protagonistGender = _session?.GetProtagonistCreationView()?.Gender ?? Gender.Male;
            var portraitRig = ArenaRoot != null ? ArenaRoot.GetComponent<PortraitRig>() : null;
            if (portraitRig != null) portraitRig.ProtagonistGender = _protagonistGender;

            if (ArenaRoot != null) ArenaRoot.SetActive(true);
            SwapToArenaCamera();

            _lastView = _session?.GetBattleView();
            RebuildGrid(_lastView);
            RebuildUnits(_lastView);
            InitializeCamera(_lastView);
            ProcessNewBattleLog(_lastView);

            _lastKnownDayLogCount = _session?.DayLog.Count ?? 0;
        }

        public void Exit() => TeardownAndDeactivate();

        public void DrawHud(GameSession session)
        {
            if (!_active) return;
            _session = session;
            Refresh();
            BattleHudScreen.Draw(this);
        }

        public void AcknowledgeResult() => TeardownAndDeactivate();

        // ================= намір гравця =================

        public void ArmAbility(string abilityId) { if (IsPlayerTurn && !IsBusy) { _armed = ArmedAction.Ability; _armedAbilityId = abilityId; } }
        public void ArmOverwatchAim() { if (IsPlayerTurn && !IsBusy) { _armed = ArmedAction.OverwatchAim; _armedAbilityId = null; } }
        public void CancelArmed() { _armed = ArmedAction.None; _armedAbilityId = null; }

        public void RequestEndTurn() { if (IsPlayerTurn && !IsBusy) RunCommand(() => _session.CombatEndTurn()); }

        public void RequestAutoResolve()
        {
            if (_session == null) return;
            int before = _session.DayLog.Count;
            try { _session.CombatAutoResolve(); }
            catch (InvalidOperationException) { NoteRejection(UkrainianText.Get("ui.battle.action.rejected", Gender.Male)); }
            AfterCommand(before);
        }

        public bool RequestStabilize(string targetId)
        {
            if (!IsPlayerTurn || IsBusy || string.IsNullOrEmpty(targetId)) return false;
            return RunCommand(() => _session.CombatStabilize(targetId));
        }

        /// <summary>
        /// Бій v2, раунд 2 (п.4, доручення власника): переліт до юніта веде
        /// не в геометричний центр ЕКРАНА, а в центр ВІЛЬНОЇ від HUD області
        /// (<see cref="FocusPointForFreeArea"/>) — інакше на 1080p (вузька
        /// нижня панель дій + права панель журналу) діючий боєць опиняється
        /// під панеллю, як на знімку 06/1080p.
        /// </summary>
        public void FocusCamera(string unitId)
        {
            var unit = FindUnitById(unitId);
            if (unit == null) return;
            var world = BattleArenaView.TileToWorld(unit.Pos.X, unit.Pos.Y);
            var target = new Vector3(world.X, 0f, world.Z);
            _cameraFlyStart = _cameraPanFocus;
            _cameraFlyTarget = FocusPointForFreeArea(target);
            _cameraFlyElapsed = 0f;
        }

        public void SimulateHoverUnit(string unitId)
        {
            _hasSimulatedHover = true;
            _simulatedHoverUnitId = unitId;
            _simulatedHoverTile = null;
        }

        public void SimulateHoverTile(int x, int y)
        {
            _hasSimulatedHover = true;
            _simulatedHoverTile = new GridPos(x, y);
            _simulatedHoverUnitId = null;
        }

        public void ClearSimulatedHover()
        {
            _hasSimulatedHover = false;
            _simulatedHoverUnitId = null;
            _simulatedHoverTile = null;
        }

        // ================= IBattleInput: команди тайл/юніт (§7: ТІ САМІ шляхи, що ЛКМ) =================

        public bool ClickUnit(string unitId)
        {
            if (!IsPlayerTurn || IsBusy || string.IsNullOrEmpty(unitId) || _session == null) return false;
            switch (_armed)
            {
                case ArmedAction.None:
                    return RunCommand(() => _session.CombatAttack(unitId));
                case ArmedAction.OverwatchAim:
                    var aimUnit = FindUnitById(unitId);
                    if (aimUnit == null) return false;
                    return RunCommand(() => _session.CombatEnterOverwatch(new GridPos(aimUnit.Pos.X, aimUnit.Pos.Y)));
                case ArmedAction.Ability:
                    if (string.IsNullOrEmpty(_armedAbilityId)) return false;
                    string abilityId = _armedAbilityId;
                    return RunCommand(() => _session.CombatUseAbility(abilityId, unitId, null));
                default:
                    return false;
            }
        }

        public bool ClickTile(int x, int y)
        {
            if (!IsPlayerTurn || IsBusy || _session == null) return false;
            var tile = new GridPos(x, y);
            switch (_armed)
            {
                case ArmedAction.None:
                    return RunCommand(() => _session.CombatMove(tile));
                case ArmedAction.OverwatchAim:
                    return RunCommand(() => _session.CombatEnterOverwatch(tile));
                case ArmedAction.Ability:
                    if (string.IsNullOrEmpty(_armedAbilityId)) return false;
                    string abilityId = _armedAbilityId;
                    return RunCommand(() => _session.CombatUseAbility(abilityId, null, tile));
                default:
                    return false;
            }
        }

        // ================= кадровий цикл =================

        private void Update()
        {
            if (!_active) return;
            if (_resultPending)
            {
                return;
            }

            float dt = Time.deltaTime;

            UpdateHover();
            Refresh();
            HandleMouseClicks();
            HandleHotkeys();
            UpdateActiveTact(dt);
            UpdateEnemyTurnDirector(dt);
            UpdateBannerAndAutoFocus(_lastView);
            UpdateFloatingTexts(dt);
            RebuildOverlays();
            UpdateHoverPathLine();
            UpdateCamera(dt);

            if (_session != null && _session.State != SessionState.Battle)
                DetectExternalResolution();
        }

        private void DetectExternalResolution() => AfterCommand(_lastKnownDayLogCount);

        private void Refresh()
        {
            if (_session == null) return;
            var view = _session.GetBattleView();
            if (view != null)
            {
                _lastView = view;
                RefreshIntentOverlayTiles();
                ApplyUnitPositionsAndHighlights(view);
                ProcessNewBattleLog(view);
            }
        }

        // ================= побудова сцени =================

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            if (ArenaRoot == null) ArenaRoot = gameObject;

            var tiles = new GameObject("Tiles");
            tiles.transform.SetParent(ArenaRoot.transform, false);
            _tileRoot = tiles.transform;

            var units = new GameObject("Units");
            units.transform.SetParent(ArenaRoot.transform, false);
            _unitRoot = units.transform;

            var props = new GameObject("Cover");
            props.transform.SetParent(ArenaRoot.transform, false);
            _propRoot = props.transform;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            _tileMaterial = shader != null ? new Material(shader) : null;
        }

        private void RebuildGrid(BattleView view)
        {
            ClearChildren(_tileRoot);
            ClearChildren(_propRoot);
            _tileObjects.Clear();
            _tileBlocks.Clear();

            if (view?.Grid == null) { _gridWidth = 0; _gridHeight = 0; return; }

            _gridWidth = view.Grid.Width;
            _gridHeight = view.Grid.Height;

            for (int y = 0; y < _gridHeight; y++)
            for (int x = 0; x < _gridWidth; x++)
            {
                int index = x + y * _gridWidth;
                string cover = view.Grid.TileCover != null && index < view.Grid.TileCover.Count ? view.Grid.TileCover[index] : "None";
                bool walkable = view.Grid.TileWalkable == null || index >= view.Grid.TileWalkable.Count || view.Grid.TileWalkable[index];

                var world = BattleArenaView.TileToWorld(x, y);
                GameObject tile = BuildTileGameObject(world);
                tile.name = "tile:" + x + "_" + y;

                var renderers = tile.GetComponentsInChildren<Renderer>();
                if (_tileMaterial != null)
                    foreach (var r in renderers) r.sharedMaterial = _tileMaterial;

                string key = x + "_" + y;
                _tileObjects[key] = tile;
                ApplyTileTint(tile, key, cover, walkable, isReachable: false, isCurrent: false, isHovered: false,
                    isHoveredUnreachable: false, isAbilityRange: false, isOverwatchAim: false, isOverwatchThreat: false);

                if (!string.Equals(cover, "None", StringComparison.Ordinal))
                    PlaceCoverProp(x, y, cover, world);
            }
        }

        private GameObject BuildTileGameObject(WorldPos world)
        {
            if (TileGroundPrefab == null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.transform.SetParent(_tileRoot, false);
                quad.transform.localPosition = new Vector3(world.X, world.Y, world.Z);
                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                quad.transform.localScale = new Vector3(BattleArenaView.TileSize * 0.985f, BattleArenaView.TileSize * 0.985f, 1f);
                return quad;
            }

            var tile = Instantiate(TileGroundPrefab, _tileRoot);
            tile.transform.SetParent(_tileRoot, false);
            tile.transform.localPosition = new Vector3(world.X, world.Y, world.Z);
            tile.transform.localRotation = Quaternion.identity;

            foreach (var stale in tile.GetComponentsInChildren<Collider>()) Destroy(stale);
            var box = tile.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.05f, 0f);
            box.size = new Vector3(BattleArenaView.TileSize, 0.1f, BattleArenaView.TileSize);
            return tile;
        }

        private void PlaceCoverProp(int x, int y, string cover, WorldPos world)
        {
            var pool = string.Equals(cover, "Full", StringComparison.Ordinal) ? CoverFullPrefabs : CoverHalfPrefabs;
            if (pool == null || pool.Length == 0) return;

            int index = (int)(BattleArenaView.Hash01("cover_" + x + "_" + y) * pool.Length);
            if (index >= pool.Length) index = pool.Length - 1;
            var prefab = pool[index];
            if (prefab == null) return;

            var go = Instantiate(prefab, _propRoot);
            go.name = "cover:" + x + "_" + y;
            go.transform.localPosition = new Vector3(world.X, world.Y, world.Z);
            go.transform.localRotation = Quaternion.Euler(0f, BattleArenaView.Hash01("cover_yaw_" + x + "_" + y) * 360f, 0f);

            foreach (var collider in go.GetComponentsInChildren<Collider>()) Destroy(collider);
        }

        private void RebuildUnits(BattleView view)
        {
            ClearChildren(_unitRoot);
            _unitObjects.Clear();
            _unitBlocks.Clear();
            _unitRenderers.Clear();
            _unitAnimations.Clear();
            _unitClipSets.Clear();

            if (view?.Units == null) return;
            foreach (var unit in view.Units) SpawnOrUpdateUnit(unit);
        }

        private void SpawnOrUpdateUnit(BattleUnitView unit)
        {
            if (!_unitObjects.TryGetValue(unit.Id, out var go) || go == null)
            {
                PickCharacter(unit.Id, out var prefab, out var clips);
                go = prefab != null ? Instantiate(prefab, _unitRoot) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "unit:" + unit.Id;
                go.transform.SetParent(_unitRoot, false);
                go.transform.localScale = Vector3.one * UnitVisualScale;

                foreach (var stale in go.GetComponentsInChildren<Collider>()) Destroy(stale);
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.8f, 0f);
                box.size = new Vector3(0.6f, 1.6f, 0.6f);

                BuildSideRing(go, unit);

                _unitObjects[unit.Id] = go;
                _unitBlocks[unit.Id] = new MaterialPropertyBlock();
                _unitRenderers[unit.Id] = go.GetComponentsInChildren<Renderer>();
                _unitClipSets[unit.Id] = clips;

                if (go.GetComponentInChildren<Animator>() != null)
                {
                    var anim = go.AddComponent<FigureAnimation>();
                    anim.idle = clips?.Idle;
                    anim.walk = clips?.Walk;
                    anim.sprint = clips?.Sprint;
                    anim.phase = BattleArenaView.Hash01(unit.Id) * 0.9f;
                    _unitAnimations[unit.Id] = anim;
                }

                var world0 = BattleArenaView.TileToWorld(unit.Pos.X, unit.Pos.Y);
                _unitVisualPos[unit.Id] = new Vector3(world0.X, 0f, world0.Z);
                _unitVisualRot[unit.Id] = Quaternion.identity;
            }

            ApplyUnitVisual(go, unit);
        }

        private void PickCharacter(string unitId, out GameObject prefab, out BattleCharacterClips clips)
        {
            bool female = IsFemaleCompanion(unitId);
            var pool = female ? FemaleCharacterPrefabs : MaleCharacterPrefabs;
            var clipPool = female ? FemaleClipSets : MaleClipSets;
            prefab = null;
            clips = null;
            if (pool == null || pool.Length == 0) return;
            int index = (int)(BattleArenaView.Hash01(unitId ?? "unit") * pool.Length);
            if (index >= pool.Length) index = pool.Length - 1;
            prefab = pool[index];
            if (clipPool != null && index < clipPool.Length) clips = clipPool[index];
        }

        private void BuildSideRing(GameObject unitGo, BattleUnitView unit)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "ring";
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(unitGo.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ring.transform.localScale = new Vector3(0.55f, 0.02f, 0.55f);

            var renderer = ring.GetComponent<Renderer>();
            if (renderer != null && _tileMaterial != null)
            {
                renderer.sharedMaterial = _tileMaterial;
                var block = new MaterialPropertyBlock();
                bool enemy = !string.Equals(unit.Side, "Player", StringComparison.Ordinal);
                block.SetColor("_BaseColor", enemy ? new Color(0.75f, 0.20f, 0.18f) : new Color(0.25f, 0.45f, 0.85f));
                renderer.SetPropertyBlock(block);
            }
        }

        // ================= щокадрове оновлення виду =================

        private void ApplyUnitPositionsAndHighlights(BattleView view)
        {
            var alive = new HashSet<string>(StringComparer.Ordinal);
            if (view.Units != null)
                foreach (var unit in view.Units)
                {
                    alive.Add(unit.Id);
                    SpawnOrUpdateUnit(unit);
                }

            var stale = new List<string>();
            foreach (var kv in _unitObjects)
                if (!alive.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale)
            {
                if (_unitObjects[id] != null) Destroy(_unitObjects[id]);
                _unitObjects.Remove(id);
                _unitBlocks.Remove(id);
                _unitRenderers.Remove(id);
                _unitAnimations.Remove(id);
                _unitClipSets.Remove(id);
                _unitVisualPos.Remove(id);
                _unitVisualRot.Remove(id);
                _unitHitReactions.Remove(id);
            }

            // Бій v2, раунд 2 (п.3, доручення власника): досяжність показуємо
            // ЛИШЕ на ході гравця, коли презентер вільний (не йдуть такти) —
            // інакше на ході ворога `view.ReachableTiles` (рахований для
            // ПОТОЧНОГО, тобто ворожого юніта) підсвічує всю його зону, і
            // гравець читає це як "оце все — моя досяжність".
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            if (IsPlayerTurn && !IsBusy && view.ReachableTiles != null)
                foreach (var pos in view.ReachableTiles) reachable.Add(pos.X + "_" + pos.Y);

            var current = CurrentUnit();
            bool armedNone = _armed == ArmedAction.None;

            for (int y = 0; y < _gridHeight; y++)
            for (int x = 0; x < _gridWidth; x++)
            {
                string key = x + "_" + y;
                if (!_tileObjects.TryGetValue(key, out var tile) || tile == null) continue;

                int index = x + y * _gridWidth;
                string cover = view.Grid.TileCover != null && index < view.Grid.TileCover.Count ? view.Grid.TileCover[index] : "None";
                bool walkable = view.Grid.TileWalkable == null || index >= view.Grid.TileWalkable.Count || view.Grid.TileWalkable[index];
                bool isReachable = reachable.Contains(key);
                bool isHovered = HasHoveredTile && HoveredTileX == x && HoveredTileY == y;
                bool isHoveredUnreachable = isHovered && armedNone && !isReachable;
                bool isOverwatchAim = _overwatchAimTileKeys.Contains(key);
                bool isOverwatchThreat = _overwatchThreatTileKeys.Contains(key);

                // Тайл поточного юніта перефарбовується другим проходом нижче.
                ApplyTileTint(tile, key, cover, walkable, isReachable, false, isHovered,
                    isHoveredUnreachable, isAbilityRange: false, isOverwatchAim: isOverwatchAim, isOverwatchThreat: isOverwatchThreat);
            }

            string currentKey = current != null ? current.Pos.X + "_" + current.Pos.Y : null;
            if (currentKey != null && _tileObjects.TryGetValue(currentKey, out var currentTile) && currentTile != null)
            {
                int index = current.Pos.X + current.Pos.Y * _gridWidth;
                string cover = view.Grid.TileCover != null && index < view.Grid.TileCover.Count ? view.Grid.TileCover[index] : "None";
                bool walkable = view.Grid.TileWalkable == null || index >= view.Grid.TileWalkable.Count || view.Grid.TileWalkable[index];
                bool isHovered = HasHoveredTile && HoveredTileX == current.Pos.X && HoveredTileY == current.Pos.Y;
                ApplyTileTint(currentTile, currentKey, cover, walkable, false, true, isHovered,
                    isHoveredUnreachable: false, isAbilityRange: false, isOverwatchAim: false, isOverwatchThreat: false);
            }

            UpdateHitChancePreview(current);
        }

        private void ApplyTileTint(GameObject tile, string key, string cover, bool walkable, bool isReachable,
            bool isCurrent, bool isHovered, bool isHoveredUnreachable, bool isAbilityRange, bool isOverwatchAim, bool isOverwatchThreat)
        {
            var renderers = tile.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var tint = BattleArenaView.TintForIntent(cover, walkable, isReachable, isCurrent, isHovered,
                isHoveredUnreachable, isAbilityRange, isOverwatchAim, isOverwatchThreat);
            if (!_tileBlocks.TryGetValue(key, out var block) || block == null)
            {
                block = new MaterialPropertyBlock();
                _tileBlocks[key] = block;
            }
            block.Clear();
            block.SetColor("_BaseColor", new Color(tint.R, tint.G, tint.B, tint.A));
            foreach (var renderer in renderers) renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// Видима позиція юніта: під час такту руху (§6) веде
        /// <see cref="_unitVisualPos"/> сам (<see cref="UpdateMoveTact"/>) —
        /// тут ми лише читаємо її, не підміняючи миттєво логічною позицією
        /// (інакше рух знову читався б як телепорт). Поза тактом — синхронна
        /// з <c>BattleUnitView.Pos</c>.
        /// </summary>
        private void ApplyUnitVisual(GameObject go, BattleUnitView unit)
        {
            bool movingThis = _activeTact != null && _activeTact.Kind == TactKind.Move &&
                               string.Equals(_activeTact.ActorId, unit.Id, StringComparison.Ordinal);

            float sink = unit.IsDowned ? BattleArenaView.DownedSink : 0f;
            Vector3 basePos;
            if (movingThis && _unitVisualPos.TryGetValue(unit.Id, out var moving))
            {
                basePos = new Vector3(moving.x, sink, moving.z);
            }
            else
            {
                var world = BattleArenaView.TileToWorld(unit.Pos.X, unit.Pos.Y);
                basePos = new Vector3(world.X, sink, world.Z);
                _unitVisualPos[unit.Id] = basePos;
            }

            var rotation = _unitVisualRot.TryGetValue(unit.Id, out var rot) ? rot : Quaternion.identity;

            var recoil = Vector3.zero;
            var palette = BattleArenaView.CharacterTint(unit.Id, unit.Side, unit.DisplayNameKey);
            var tintColor = new Color(palette.R, palette.G, palette.B, unit.IsDowned ? 0.55f : 1f);

            if (_unitHitReactions.TryGetValue(unit.Id, out var reaction))
            {
                float age = Time.time - reaction.StartTime;
                if (age >= 0f && age < reaction.Duration)
                {
                    float frac = 1f - age / reaction.Duration;
                    tintColor = Color.Lerp(tintColor, reaction.FlashColor, frac);
                    recoil = reaction.RecoilDir * (0.12f * frac);
                }
                else
                {
                    _unitHitReactions.Remove(unit.Id);
                }
            }

            go.transform.localPosition = basePos + recoil;
            go.transform.localRotation = rotation;

            var renderers = _unitRenderers.TryGetValue(unit.Id, out var cachedRenderers) && cachedRenderers != null
                ? cachedRenderers
                : go.GetComponentsInChildren<Renderer>();
            var block = _unitBlocks.TryGetValue(unit.Id, out var b) ? b : new MaterialPropertyBlock();
            block.Clear();
            block.SetColor("_BaseColor", tintColor);
            foreach (var r in renderers)
            {
                string n = r.gameObject.name;
                if (n == "ring" || n == "overwatch") continue;
                r.SetPropertyBlock(block);
            }

            var overwatchMarker = go.transform.Find("overwatch");
            bool wantOverwatch = unit.IsOverwatching;
            if (wantOverwatch && overwatchMarker == null)
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "overwatch";
                Destroy(ring.GetComponent<Collider>());
                ring.transform.SetParent(go.transform, false);
                ring.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                ring.transform.localScale = new Vector3(0.75f, 0.015f, 0.75f);
                var r = ring.GetComponent<Renderer>();
                if (r != null && _tileMaterial != null)
                {
                    r.sharedMaterial = _tileMaterial;
                    var ob = new MaterialPropertyBlock();
                    ob.SetColor("_BaseColor", new Color(0.35f, 0.85f, 0.95f));
                    r.SetPropertyBlock(ob);
                }
            }
            else if (!wantOverwatch && overwatchMarker != null)
            {
                Destroy(overwatchMarker.gameObject);
            }
        }

        // ================= прев'ю шансу (перехідне — §HoveredHitChance/Damage*) =================

        private void UpdateHitChancePreview(BattleUnitView current)
        {
            _hoveredHitChance = 0;
            _hoveredDamageMin = _hoveredDamageMax = _hoveredDamageCrit = 0;
            if (_session == null || current == null || string.IsNullOrEmpty(HoveredUnitId)) return;
            if (string.Equals(HoveredUnitId, current.Id, StringComparison.Ordinal)) return;
            _hoveredHitChance = _session.PreviewHitChance(current.Id, HoveredUnitId);
            _session.PreviewDamage(current.Id, HoveredUnitId, out _hoveredDamageMin, out _hoveredDamageMax, out _hoveredDamageCrit);
        }

        private BattleUnitView CurrentUnit()
        {
            if (_lastView?.Units == null || string.IsNullOrEmpty(_lastView.CurrentUnitId)) return null;
            foreach (var u in _lastView.Units)
                if (string.Equals(u.Id, _lastView.CurrentUnitId, StringComparison.Ordinal)) return u;
            return null;
        }

        private BattleUnitView FindUnitById(string id)
        {
            if (string.IsNullOrEmpty(id) || _lastView?.Units == null) return null;
            foreach (var u in _lastView.Units)
                if (string.Equals(u.Id, id, StringComparison.Ordinal)) return u;
            return null;
        }

        private GameObject UnitGo(string id) => _unitObjects.TryGetValue(id ?? string.Empty, out var go) ? go : null;

        // ================= ввід миші =================

        /// <summary>Лише читає, куди дивиться курсор — жодної команди. Справжній рух миші скидає симульоване наведення (§7.4 <see cref="ClearSimulatedHover"/>).</summary>
        private void UpdateHover()
        {
            var mouse = Input.mousePosition;
            if (_lastMousePositionKnown && _hasSimulatedHover && (mouse - _lastMousePosition).sqrMagnitude > 0.25f)
                ClearSimulatedHover();
            _lastMousePosition = mouse;
            _lastMousePositionKnown = true;

            _hoveredTile = null;
            _hoveredUnitId = null;
            if (ArenaCamera == null || IsPointerOverHud()) return;

            var ray = ArenaCamera.ScreenPointToRay(mouse);
            if (!Physics.Raycast(ray, out var hit, 500f)) return;

            string n = hit.collider != null ? hit.collider.gameObject.name : null;
            if (n != null && n.StartsWith("unit:", StringComparison.Ordinal))
            {
                _hoveredUnitId = n.Substring(5);
            }
            else if (n != null && n.StartsWith("tile:", StringComparison.Ordinal))
            {
                var parts = n.Substring(5).Split('_');
                if (parts.Length == 2 && int.TryParse(parts[0], out int tx) && int.TryParse(parts[1], out int ty))
                    _hoveredTile = new GridPos(tx, ty);
            }
        }

        /// <summary>Курсор над будь-якою панеллю HUD (<see cref="SetHudRects"/>) — GUI-простір (початок зверху-зліва).</summary>
        private bool IsPointerOverHud()
        {
            if (_hudRects == null || _hudRects.Count == 0) return false;
            var guiPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            for (int i = 0; i < _hudRects.Count; i++)
                if (_hudRects[i].width > 0f && _hudRects[i].height > 0f && _hudRects[i].Contains(guiPos)) return true;
            return false;
        }

        private void HandleMouseClicks()
        {
            if (IsPointerOverHud()) return;

            if (Input.GetMouseButtonDown(1))
            {
                CancelArmed();
                return;
            }

            if (!Input.GetMouseButtonDown(0) || !IsPlayerTurn || IsBusy) return;

            if (!string.IsNullOrEmpty(_hoveredUnitId)) ClickUnit(_hoveredUnitId);
            else if (_hoveredTile.HasValue) ClickTile(_hoveredTile.Value.X, _hoveredTile.Value.Y);
        }

        /// <summary>Гарячі клавіші (§7): Пробіл, 1..9, O, Q/E — камера окремо в <see cref="UpdateCamera"/>. Esc НЕ обробляється тут (GameShell).</summary>
        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (IsPlayerTurn && !IsBusy) RequestEndTurn();
                else if (!IsPlayerTurn) FastEnemyTurns = !FastEnemyTurns;
            }

            if (IsPlayerTurn && !IsBusy)
            {
                var current = CurrentUnit();
                if (current?.Abilities != null)
                    for (int i = 0; i < current.Abilities.Count && i < 9; i++)
                        if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                            ArmAbility(current.Abilities[i].Id);

                if (Input.GetKeyDown(KeyCode.O)) ArmOverwatchAim();
            }

            if (Input.GetKeyDown(KeyCode.Q)) _cameraYawDegrees = Wrap360(_cameraYawDegrees - 90f);
            if (Input.GetKeyDown(KeyCode.E)) _cameraYawDegrees = Wrap360(_cameraYawDegrees + 90f);
        }

        private static float Wrap360(float deg)
        {
            deg %= 360f;
            if (deg < 0f) deg += 360f;
            return deg;
        }

        // ================= прев'ю за наміром (кеш, §6-§7) =================

        private void RefreshHoverCaches()
        {
            if (_session == null || _lastView == null) { _hoverAttackCache = null; _hoverPathCache = null; return; }

            string currentId = _lastView.CurrentUnitId;
            string hoveredId = HoveredUnitId;
            bool hasTile = HasHoveredTile;
            string tileKey = hasTile ? HoveredTileX + "_" + HoveredTileY : null;
            int logCount = _lastView.Log?.Count ?? 0;
            var key = (currentId, hoveredId, _armed, _armedAbilityId, logCount, tileKey);

            if (_hoverKey.HasValue && _hoverKey.Value.Equals(key)) return;
            _hoverKey = key;

            _hoverAttackCache = null;
            if (!string.IsNullOrEmpty(currentId) && !string.IsNullOrEmpty(hoveredId))
            {
                try { _hoverAttackCache = _session.PreviewAttack(currentId, hoveredId, _armed == ArmedAction.Ability ? _armedAbilityId : null); }
                catch (NotImplementedException) { _hoverAttackCache = null; }
            }

            _hoverPathCache = null;
            if (IsPlayerTurn && hasTile)
            {
                try { _hoverPathCache = _session.PreviewMovePath(new GridPos(HoveredTileX, HoveredTileY)); }
                catch (NotImplementedException) { _hoverPathCache = null; }
            }
        }

        /// <summary>Тайли приціла дозору (озброєна дія) і тайли шляху під ворожим дозором (наведений рух) — §5.</summary>
        private void RefreshIntentOverlayTiles()
        {
            _overwatchAimTileKeys.Clear();
            _overwatchThreatTileKeys.Clear();
            if (_session == null || _lastView == null) return;

            if (_armed == ArmedAction.OverwatchAim && HasHoveredTile)
            {
                try
                {
                    var cone = _session.PreviewOverwatchCone(new GridPos(HoveredTileX, HoveredTileY));
                    if (cone != null) foreach (var t in cone) _overwatchAimTileKeys.Add(t.X + "_" + t.Y);
                }
                catch (NotImplementedException) { /* «ядро» Бою v2 ще не готове — приціл просто без підсвітки тайлів */ }
            }

            var path = HoverPath;
            if (_armed == ArmedAction.None && path?.OverwatchThreatTiles != null)
                foreach (var t in path.OverwatchThreatTiles) _overwatchThreatTileKeys.Add(t.X + "_" + t.Y);
        }

        // ================= такти: відтворення результату дії (§6) =================

        private bool IsFastNow() => _fastEnemyTurns && _lastView != null && _lastView.IsAiTurn;

        private void UpdateActiveTact(float dt)
        {
            if (_activeTact == null)
            {
                if (_pendingTacts.Count > 0) StartNextTact();
                return;
            }

            if (_activeTact.Kind == TactKind.Move) UpdateMoveTact(dt);
            else UpdateTimedTact(dt);
        }

        private void StartNextTact()
        {
            var pending = _pendingTacts.Dequeue();
            var entry = pending.Entry;
            var args = entry.Args;
            string unitId = Arg(args, "unitId");
            string targetId = Arg(args, "targetId");

            _activeTact = new ActiveTact { Entry = entry, Kind = pending.Kind, ActorId = unitId, TargetId = targetId };
            bool fast = IsFastNow();

            switch (pending.Kind)
            {
                case TactKind.Move:
                    _activeTact.Path = BattleTactParser.ParsePath(Arg(args, "path"));
                    _activeTact.PathIndex = 0;
                    if (!_unitVisualPos.ContainsKey(unitId) && UnitGo(unitId) != null)
                        _unitVisualPos[unitId] = UnitGo(unitId).transform.localPosition;
                    SetGait(unitId, 1f);
                    break;

                case TactKind.Attack:
                    _activeTact.Duration = fast ? 0.15f : 0.5f;
                    FaceTowards(unitId, targetId);
                    PlayAttackClip(unitId, targetId);
                    break;

                case TactKind.Ability:
                    _activeTact.Duration = fast ? 0.12f : 0.4f;
                    if (!string.IsNullOrEmpty(targetId)) FaceTowards(unitId, targetId);
                    PlayOneShot(unitId, clips => clips?.Interact, hold: false);
                    break;

                case TactKind.DownedOrDeath:
                    _activeTact.Duration = fast ? 0.2f : 0.6f;
                    PlayOneShot(unitId, clips => clips?.Die, hold: true);
                    break;
            }
        }

        private void UpdateMoveTact(float dt)
        {
            var tact = _activeTact;
            if (tact.Path == null || tact.PathIndex >= tact.Path.Count) { CompleteTact(); return; }

            float tileDuration = IsFastNow() ? 0.09f : 0.18f;
            float speed = BattleArenaView.TileSize / tileDuration;

            var currentPos = _unitVisualPos.TryGetValue(tact.ActorId, out var p) ? p : Vector3.zero;
            var targetTile = tact.Path[tact.PathIndex];
            var targetWorld = BattleArenaView.TileToWorld(targetTile.X, targetTile.Y);
            var targetPos = new Vector3(targetWorld.X, currentPos.y, targetWorld.Z);

            var dir = targetPos - currentPos;
            float dist = dir.magnitude;
            float step = speed * dt;

            if (dist <= step || dist < 0.0001f)
            {
                _unitVisualPos[tact.ActorId] = targetPos;
                tact.PathIndex++;
                if (tact.PathIndex >= tact.Path.Count)
                {
                    SetGait(tact.ActorId, 0f);
                    CompleteTact();
                }
            }
            else
            {
                var facing = new Vector3(dir.x, 0f, dir.z);
                if (facing.sqrMagnitude > 0.0001f) _unitVisualRot[tact.ActorId] = Quaternion.LookRotation(facing.normalized, Vector3.up);
                _unitVisualPos[tact.ActorId] = currentPos + dir.normalized * step;
            }
        }

        private void UpdateTimedTact(float dt)
        {
            var tact = _activeTact;
            tact.Elapsed += dt;

            if (tact.Kind == TactKind.Attack && !tact.FloatingSpawned && tact.Elapsed >= tact.Duration * 0.5f)
            {
                tact.FloatingSpawned = true;
                SpawnFloatingForEntry(tact.Entry);
                ApplyHitReaction(tact);
            }

            if (tact.Elapsed >= tact.Duration) CompleteTact();
        }

        private void CompleteTact()
        {
            if (_activeTact != null && _activeTact.Kind == TactKind.Move) SetGait(_activeTact.ActorId, 0f);
            _activeTact = null;
        }

        private void SetGait(string unitId, float gait)
        {
            if (_unitAnimations.TryGetValue(unitId ?? string.Empty, out var anim) && anim != null) anim.Gait = gait;
        }

        private void PlayOneShot(string unitId, Func<BattleCharacterClips, AnimationClip> select, bool hold)
        {
            if (string.IsNullOrEmpty(unitId)) return;
            if (!_unitAnimations.TryGetValue(unitId, out var anim) || anim == null) return;
            _unitClipSets.TryGetValue(unitId, out var clips);
            var clip = select(clips);
            if (clip != null) anim.PlayOnce(clip, hold);
        }

        /// <summary>Ближній/дальній замах — <c>WeaponIsMelee</c> (BattleUnitView, §7.1); дальній ще й лишає лінію пострілу на короткий час.</summary>
        private void PlayAttackClip(string attackerId, string targetId)
        {
            var attacker = FindUnitById(attackerId);
            bool melee = attacker != null && attacker.WeaponIsMelee;
            PlayOneShot(attackerId, clips => melee ? clips?.AttackMelee : clips?.HoldingShoot, hold: false);
            if (!melee) StartShotLine(attackerId, targetId);
        }

        private void FaceTowards(string actorId, string targetId)
        {
            var actor = FindUnitById(actorId);
            var target = FindUnitById(targetId);
            if (actor == null || target == null) return;
            var from = BattleArenaView.TileToWorld(actor.Pos.X, actor.Pos.Y);
            var to = BattleArenaView.TileToWorld(target.Pos.X, target.Pos.Y);
            var dir = new Vector3(to.X - from.X, 0f, to.Z - from.Z);
            if (dir.sqrMagnitude > 0.0001f) _unitVisualRot[actorId] = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        /// <summary>
        /// Тексти написів — виключно <see cref="BattleLogText.Floating"/> (не
        /// вигадувати самому, §2 доручення). Бій v2, раунд 2 (п.6, доручення
        /// власника): старт — трохи ВИЩЕ за верх імені/HP-смужки юніта (той
        /// самий якір, що й оверлей, <c>NameLabelHeight</c>), з фіксованим
        /// екранним запасом — інакше напис стартує НИЖЧЕ картки (перекриває
        /// її, а не піднімається над нею), як на знімках без цього фіксу.
        /// </summary>
        private void SpawnFloatingForEntry(BattleLogLineView entry)
        {
            var spec = BattleLogText.Floating(entry, _lastView, _protagonistGender);
            if (spec == null || string.IsNullOrEmpty(spec.UnitId)) return;
            var unit = FindUnitById(spec.UnitId);
            if (unit == null || ArenaCamera == null) return;

            var world = BattleArenaView.TileToWorld(unit.Pos.X, unit.Pos.Y);
            var nameTopGui = WorldToGui(new Vector3(world.X, BattleArenaView.NameLabelHeight, world.Z));

            var text = new BattleFloatingText
            {
                Text = spec.Text,
                Kind = spec.Kind,
                ScreenX = nameTopGui.x,
                ScreenY = nameTopGui.y - FloatingSpawnGapAboveNamePx,
                Alpha = 1f,
                Big = spec.Big
            };
            _floatingRuntime.Add(new FloatingRuntime
            {
                Data = text,
                World = new Vector3(world.X, BattleArenaView.NameLabelHeight, world.Z),
                Age = 0f,
                Duration = spec.Big ? 1.4f : 1.0f
            });
        }

        private void ApplyHitReaction(ActiveTact tact)
        {
            var kind = BattleLogText.KindOf(tact.Entry);
            if (kind == BattleLogKind.Miss || string.IsNullOrEmpty(tact.TargetId)) return;

            var flash = kind == BattleLogKind.Crit ? new Color(1f, 0.80f, 0.30f) : new Color(0.95f, 0.30f, 0.25f);
            var reaction = new HitReaction { FlashColor = flash, StartTime = Time.time, Duration = 0.18f, RecoilDir = Vector3.zero };

            var attacker = FindUnitById(tact.ActorId);
            var target = FindUnitById(tact.TargetId);
            if (attacker != null && target != null)
            {
                var from = BattleArenaView.TileToWorld(attacker.Pos.X, attacker.Pos.Y);
                var to = BattleArenaView.TileToWorld(target.Pos.X, target.Pos.Y);
                var away = new Vector3(to.X - from.X, 0f, to.Z - from.Z);
                if (away.sqrMagnitude > 0.0001f) reaction.RecoilDir = away.normalized;
            }
            _unitHitReactions[tact.TargetId] = reaction;
        }

        private void StartShotLine(string attackerId, string targetId)
        {
            var attacker = FindUnitById(attackerId);
            var target = FindUnitById(targetId);
            if (attacker == null || target == null) return;

            var line = EnsureShotLine();
            if (line == null) return;

            var from = BattleArenaView.TileToWorld(attacker.Pos.X, attacker.Pos.Y);
            var to = BattleArenaView.TileToWorld(target.Pos.X, target.Pos.Y);
            line.SetPosition(0, new Vector3(from.X, 0.9f, from.Z));
            line.SetPosition(1, new Vector3(to.X, 0.9f, to.Z));
            line.enabled = true;
            _shotLineUntil = Time.time + 0.15f;
        }

        private LineRenderer EnsureShotLine()
        {
            if (_shotLine != null) return _shotLine;
            if (ArenaRoot == null) return null;

            var go = new GameObject("shot_line");
            go.transform.SetParent(ArenaRoot.transform, false);
            _shotLine = go.AddComponent<LineRenderer>();
            _shotLine.positionCount = 2;
            _shotLine.startWidth = 0.04f;
            _shotLine.endWidth = 0.04f;
            _shotLine.useWorldSpace = true;
            // Бій v2, раунд 2 (п.2): лінія без тіні — інакше кидає чорну
            // пунктирну тінь на землю (видно на 04-after-attack).
            _shotLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _shotLine.receiveShadows = false;
            if (_tileMaterial != null) _shotLine.material = _tileMaterial;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", new Color(0.95f, 0.85f, 0.45f));
            _shotLine.SetPropertyBlock(block);
            _shotLine.enabled = false;
            return _shotLine;
        }

        /// <summary>
        /// Бій v2, раунд 2 (п.2, доручення власника): лінія руху до
        /// наведеного ДОСЯЖНОГО тайла (раніше не малювалась узагалі —
        /// підказка «Рух: −N ОД» була, лінії не було, 07-hover-tile) —
        /// над землею, яскраво-біла, той самий рецепт емісії, що
        /// <c>VillageStage.ShowMarks</c> (URP/Lit + <c>_EMISSION</c>), щоб
        /// точно рендерилась у білді, а не губилась на тлі трави.
        /// </summary>
        private LineRenderer EnsurePathLine()
        {
            if (_pathLine != null) return _pathLine;
            if (ArenaRoot == null) return null;

            var go = new GameObject("hover_path_line");
            go.transform.SetParent(ArenaRoot.transform, false);
            _pathLine = go.AddComponent<LineRenderer>();
            _pathLine.positionCount = 0;
            _pathLine.startWidth = PathLineWidth;
            _pathLine.endWidth = PathLineWidth;
            _pathLine.useWorldSpace = true;
            _pathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _pathLine.receiveShadows = false;
            _pathLine.material = EnsureEmissiveMaterial();
            _pathLine.enabled = false;
            return _pathLine;
        }

        /// <summary>Маркер кінцевої клітинки шляху (§2) — тонкий яскравий диск, той самий матеріал, що <see cref="EnsurePathLine"/>.</summary>
        private GameObject EnsurePathEndMarker()
        {
            if (_pathEndMarker != null) return _pathEndMarker;
            if (ArenaRoot == null) return null;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "hover_path_marker";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(ArenaRoot.transform, false);
            marker.transform.localScale = new Vector3(0.42f, 0.02f, 0.42f);

            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = EnsureEmissiveMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            marker.SetActive(false);
            _pathEndMarker = marker;
            return marker;
        }

        /// <summary>
        /// Той самий рецепт, що <c>VillageStage.ShowMarks</c> (URP/Lit,
        /// непрозорий, з увімкненою емісією — гарантовано рендериться в
        /// білді, §2): кольори — прямо на матеріалі, а не через
        /// MaterialPropertyBlock, бо матеріал належить лише лінії шляху й
        /// маркеру кінця, обом — той самий яскраво-білий колір.
        /// </summary>
        private Material EnsureEmissiveMaterial()
        {
            if (_emissiveMaterial != null) return _emissiveMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;

            _emissiveMaterial = new Material(shader);
            var white = new Color(1f, 1f, 1f);
            _emissiveMaterial.SetColor("_BaseColor", white);
            _emissiveMaterial.EnableKeyword("_EMISSION");
            _emissiveMaterial.SetColor("_EmissionColor", white * 1.6f);
            return _emissiveMaterial;
        }

        /// <summary>
        /// Показує/ховає лінію руху й маркер кінця шляху за наведенням
        /// (§2, §6 ходу гравця): лише коли можна рухатися просто зараз —
        /// хід гравця, презентер вільний, нічого не озброєно (як і
        /// <see cref="RefreshIntentOverlayTiles"/> для тайлів під загрозою
        /// дозору), і сам шлях справді досяжний (Result == "Success").
        /// </summary>
        private void UpdateHoverPathLine()
        {
            var line = EnsurePathLine();
            var marker = EnsurePathEndMarker();
            if (line == null || marker == null) return;

            var current = CurrentUnit();
            var path = (_armed == ArmedAction.None && IsPlayerTurn && !IsBusy) ? HoverPath : null;
            bool show = current != null && path != null && path.Tiles != null && path.Tiles.Count > 0 &&
                        string.Equals(path.Result, "Success", StringComparison.Ordinal);

            if (!show)
            {
                line.enabled = false;
                marker.SetActive(false);
                return;
            }

            const float height = PathLineHeight;
            _pathLinePoints.Clear();
            var start = BattleArenaView.TileToWorld(current.Pos.X, current.Pos.Y);
            _pathLinePoints.Add(new Vector3(start.X, height, start.Z));
            foreach (var tile in path.Tiles)
            {
                var w = BattleArenaView.TileToWorld(tile.X, tile.Y);
                _pathLinePoints.Add(new Vector3(w.X, height, w.Z));
            }

            line.positionCount = _pathLinePoints.Count;
            line.SetPositions(_pathLinePoints.ToArray());
            line.enabled = true;

            var last = path.Tiles[path.Tiles.Count - 1];
            var lastWorld = BattleArenaView.TileToWorld(last.X, last.Y);
            marker.transform.localPosition = new Vector3(lastWorld.X, height, lastWorld.Z);
            marker.SetActive(true);
        }

        private static string Arg(IReadOnlyDictionary<string, string> args, string name)
        {
            if (args == null) return null;
            return args.TryGetValue(name, out var v) ? v : null;
        }

        private static TactKind ClassifyTact(BattleLogLineView entry)
        {
            var kind = BattleLogText.KindOf(entry);
            switch (kind)
            {
                case BattleLogKind.Move: return TactKind.Move;
                case BattleLogKind.Miss:
                case BattleLogKind.Graze:
                case BattleLogKind.Hit:
                case BattleLogKind.Crit: return TactKind.Attack;
                case BattleLogKind.Ability: return TactKind.Ability;
                case BattleLogKind.Downed:
                case BattleLogKind.Death:
                    return entry.Key == "combat.log.downed" || entry.Key == "combat.log.died"
                        ? TactKind.DownedOrDeath
                        : TactKind.Skip;
                default: return TactKind.Skip;
            }
        }

        // ================= режисер ходу ворога (§5, §7) =================

        private void UpdateEnemyTurnDirector(float dt)
        {
            if (_session == null || _lastView == null) return;

            var decision = _turnDirector.Tick(dt, _lastView.IsAiTurn, _enemyAiEnabled, IsBusy, _fastEnemyTurns, _lastView.CurrentUnitId);
            switch (decision)
            {
                case BattleTurnDirectorAction.StepAi:
                    FocusCamera(_lastView.CurrentUnitId);
                    TryStepAi();
                    break;
                case BattleTurnDirectorAction.ForceEndTurn:
                    if (_turnDirector.LastWarning != null) Debug.LogWarning(_turnDirector.LastWarning);
                    RunCommand(() => _session.CombatEndTurn());
                    break;
            }
        }

        private void TryStepAi()
        {
            if (_session == null) return;
            int before = _session.DayLog.Count;
            try { _session.CombatAiStepOneAction(); }
            catch (InvalidOperationException) { /* бій щойно завершився цим самим кроком (Core.RequireBattle) */ }
            AfterCommand(before);
            _turnDirector.RecordStepOutcome(ComputeStateSignature());
        }

        private string ComputeStateSignature()
        {
            if (_lastView == null) return "none";
            var current = CurrentUnit();
            return (_lastView.Log?.Count ?? 0).ToString(CultureInfo.InvariantCulture) + ":" +
                   (current?.Ap ?? -1).ToString(CultureInfo.InvariantCulture) + ":" +
                   (current?.Hp ?? -1).ToString(CultureInfo.InvariantCulture) + ":" +
                   (current != null ? current.Pos.X + "," + current.Pos.Y : "-");
        }

        // ================= банер ходу + автофокус камери (§3, §4) =================

        private void UpdateBannerAndAutoFocus(BattleView view)
        {
            string currentId = view?.CurrentUnitId;
            if (string.Equals(currentId, _lastBannerUnitId, StringComparison.Ordinal))
            {
                UpdateBannerFade(Time.deltaTime);
                return;
            }
            _lastBannerUnitId = currentId;

            var unit = FindUnitById(currentId);
            if (unit != null)
            {
                bool playerSide = string.Equals(unit.Side, "Player", StringComparison.Ordinal);
                string key = playerSide ? "ui.battle.banner.player" : "ui.battle.banner.enemy";
                _banner = new BattleTurnBanner
                {
                    Text = UkrainianText.Format(key, false, "name", ResolveDisplayNameInternal(unit)),
                    PlayerSide = playerSide,
                    Alpha = 1f
                };
                _bannerTimer = 0f;
                FocusCamera(currentId);
            }
            UpdateBannerFade(Time.deltaTime);
        }

        private void UpdateBannerFade(float dt)
        {
            if (_banner == null) return;
            const float total = 0.9f, holdUntil = 0.5f;
            _bannerTimer += dt;
            float alpha = _bannerTimer <= holdUntil ? 1f : 1f - Mathf.Clamp01((_bannerTimer - holdUntil) / (total - holdUntil));
            _banner.Alpha = alpha;
            if (_bannerTimer >= total) _banner = null;
        }

        // ================= спливаючі написи: старіння (§6) =================

        private void UpdateFloatingTexts(float dt)
        {
            for (int i = _floatingRuntime.Count - 1; i >= 0; i--)
            {
                var r = _floatingRuntime[i];
                r.Age += dt;
                if (ArenaCamera != null)
                {
                    var anchor = WorldToGui(r.World);
                    r.Data.ScreenX = anchor.x;
                    r.Data.ScreenY = anchor.y - FloatingSpawnGapAboveNamePx - FloatingRiseSpeedPxPerSecond * r.Age;
                }
                else
                {
                    r.Data.ScreenY -= FloatingRiseSpeedPxPerSecond * dt;
                }
                r.Data.Alpha = Mathf.Clamp01(1f - r.Age / r.Duration);
                if (r.Age >= r.Duration) _floatingRuntime.RemoveAt(i);
            }

            _floatingTextsExposed.Clear();
            foreach (var r in _floatingRuntime) _floatingTextsExposed.Add(r.Data);

            if (_shotLine != null && _shotLine.enabled && Time.time >= _shotLineUntil) _shotLine.enabled = false;
        }

        // ================= оверлеї над юнітами (§6) =================

        private void RebuildOverlays()
        {
            _overlays.Clear();
            if (_lastView?.Units == null || ArenaCamera == null) return;

            string hoveredId = HoveredUnitId;
            string currentId = _lastView.CurrentUnitId;
            var hoverAttack = HoverAttack;

            foreach (var unit in _lastView.Units)
            {
                var pos = _unitVisualPos.TryGetValue(unit.Id, out var p) ? p : DefaultWorldPos(unit);
                var world = pos + Vector3.up * BattleArenaView.NameLabelHeight;
                var sp = ArenaCamera.WorldToScreenPoint(world);
                bool onScreen = sp.z > 0f && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;
                bool isHovered = string.Equals(unit.Id, hoveredId, StringComparison.Ordinal);
                bool isTargetable = isHovered && hoverAttack != null &&
                    string.Equals(hoverAttack.TargetId, unit.Id, StringComparison.Ordinal) &&
                    string.Equals(hoverAttack.Result, "Success", StringComparison.Ordinal);

                _overlays.Add(new BattleUnitOverlay
                {
                    UnitId = unit.Id,
                    ScreenX = sp.x,
                    ScreenY = Screen.height - sp.y,
                    OnScreen = onScreen,
                    IsCurrent = string.Equals(unit.Id, currentId, StringComparison.Ordinal),
                    IsHovered = isHovered,
                    IsTargetable = isTargetable
                });
            }
        }

        private static Vector3 DefaultWorldPos(BattleUnitView unit)
        {
            var world = BattleArenaView.TileToWorld(unit.Pos.X, unit.Pos.Y);
            return new Vector3(world.X, 0f, world.Z);
        }

        /// <summary>Екранна точка центру наведеного тайла (§7.4 HoveredTileScreenX/Y) — трохи над землею, як і маркер кінця шляху (<see cref="EnsurePathEndMarker"/>).</summary>
        private Vector2 HoveredTileScreenPoint()
        {
            var world = BattleArenaView.TileToWorld(HoveredTileX, HoveredTileY);
            return WorldToGui(new Vector3(world.X, 0.15f, world.Z));
        }

        private Vector2 WorldToGui(Vector3 world)
        {
            if (ArenaCamera == null) return Vector2.zero;
            var sp = ArenaCamera.WorldToScreenPoint(world);
            return new Vector2(sp.x, Screen.height - sp.y);
        }

        // ================= камера (§4) =================

        /// <summary>
        /// Початковий кадр на вхід у бій — миттєво, без пом'якшення (інакше
        /// перший кадр «летів би» з початку координат). Бій v2, раунд 2
        /// (доручення власника, п.4 «Початкове кадрування — увесь грід у
        /// вільній області»): зум рахує ПОВНИЙ грід у вільну від HUD частину
        /// екрана (<see cref="BattleArenaView.OrthographicSizeForFreeArea"/>),
        /// а центр — після того, як камера вже виставлена під цей зум,
        /// зсувається так, щоб грід опинився в центрі вільної області, не
        /// всього екрана (<see cref="FocusPointForFreeArea"/> — той самий
        /// зсув, що й переліт до діючого юніта, <see cref="FocusCamera"/>).
        /// </summary>
        private void InitializeCamera(BattleView view)
        {
            if (view?.Grid == null) return;

            var margins = CurrentHudMargins();
            float orthoSize = BattleArenaView.OrthographicSizeForFreeArea(view.Grid.Width, view.Grid.Height, Screen.width, Screen.height, margins);
            _cameraYawDegrees = InitialYawDegrees;
            _cameraZoom = Mathf.Clamp(orthoSize, CameraMinZoom, CameraMaxZoom);
            _cameraFlyTarget = null;

            var naturalFrame = BattleArenaView.FrameGrid(view.Grid.Width, view.Grid.Height);
            _cameraPanFocus = new Vector3(naturalFrame.CenterX, 0f, naturalFrame.CenterZ);

            if (ArenaCamera == null) { _cameraInitialized = true; return; }

            // Спершу виставляємо камеру під природний центр гріда з новим
            // зумом — без цього кроку ScreenPointToRay (у FocusPointForFreeArea)
            // рахував би промені під СТАРОЮ позою камери (з попереднього бою).
            ApplyCameraPose(_cameraPanFocus);
            _cameraPanFocus = FocusPointForFreeArea(_cameraPanFocus);
            ApplyCameraPose(_cameraPanFocus);

            _cameraInitialized = true;
        }

        /// <summary>Миттєво (без згладжування) ставить камеру в позу під поточні tilt/yaw/zoom і задану точку фокуса.</summary>
        private void ApplyCameraPose(Vector3 focus)
        {
            if (ArenaCamera == null) return;
            var offset = BattleArenaView.CameraOffsetFromFocus(CameraTiltDegrees, _cameraYawDegrees, CameraDistance);
            ArenaCamera.transform.position = focus + new Vector3(offset.X, offset.Y, offset.Z);
            ArenaCamera.transform.rotation = Quaternion.LookRotation(-new Vector3(offset.X, offset.Y, offset.Z).normalized, Vector3.up);
            if (ArenaCamera.orthographic) ArenaCamera.orthographicSize = _cameraZoom;
        }

        /// <summary>Поточні поля HUD — з останніх прямокутників <see cref="SetHudRects"/>, або розумний дефолт, поки їх ще не було цього бою.</summary>
        private BattleArenaView.HudMargins CurrentHudMargins()
        {
            if (_hudRects == null || _hudRects.Count == 0)
                return BattleArenaView.DefaultHudMargins(Screen.width, Screen.height);

            var converted = new List<BattleArenaView.GuiRect>(_hudRects.Count);
            for (int i = 0; i < _hudRects.Count; i++)
            {
                var r = _hudRects[i];
                converted.Add(new BattleArenaView.GuiRect(r.x, r.y, r.width, r.height));
            }
            return BattleArenaView.MarginsFromRects(Screen.width, Screen.height, converted);
        }

        /// <summary>Центр вільної від HUD області, у екранних координатах (Y згори — те, що приймає <c>Camera.ScreenPointToRay</c>/<c>WorldToScreenPoint</c>).</summary>
        private Vector2 FreeAreaCenterScreen()
        {
            var gui = BattleArenaView.FreeAreaCenter(Screen.width, Screen.height, CurrentHudMargins());
            return new Vector2(gui.X, Screen.height - gui.Y);
        }

        /// <summary>
        /// Перетин променя ортографічної камери (промені паралельні — Unity
        /// сам це гарантує для <c>Camera.orthographic == true</c>) із площиною
        /// арени (Y=0) для довільної екранної точки — БЕЗ Physics.Raycast
        /// (арену ще не обов'язково добудовано під час InitializeCamera).
        /// </summary>
        private Vector3 GroundPointAtScreen(float screenX, float screenY)
        {
            var ray = ArenaCamera.ScreenPointToRay(new Vector3(screenX, screenY, 0f));
            if (Mathf.Abs(ray.direction.y) < 1e-6f) return ray.origin;
            float t = -ray.origin.y / ray.direction.y;
            return ray.origin + ray.direction * t;
        }

        /// <summary>
        /// Зсуває фокус камери так, щоб <paramref name="targetGround"/> (Y=0)
        /// рендерився в центрі вільної від HUD області, а не всього екрана
        /// (§4, Бій v2 раунд 2). Працює на живій позі <see cref="ArenaCamera"/>
        /// (tilt/yaw/zoom — байдуже, який саме фокус камера тримає ЗАРАЗ:
        /// ортографічна панорама лінійна, тож зсув виходить той самий).
        /// </summary>
        private Vector3 FocusPointForFreeArea(Vector3 targetGround)
        {
            if (ArenaCamera == null) return targetGround;
            var desiredScreen = FreeAreaCenterScreen();
            var groundAtDesired = GroundPointAtScreen(desiredScreen.x, desiredScreen.y);
            return _cameraPanFocus + (targetGround - groundAtDesired);
        }

        private void UpdateCamera(float dt)
        {
            if (ArenaCamera == null) return;
            if (!_cameraInitialized) { InitializeCamera(_lastView); return; }

            bool overHud = IsPointerOverHud();

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && !overHud)
                _cameraZoom = Mathf.Clamp(_cameraZoom - wheel * 0.6f, CameraMinZoom, CameraMaxZoom);

            float ix = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                     - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float iz = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                     - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            if (ix != 0f || iz != 0f)
            {
                var right = ArenaCamera.transform.right; right.y = 0f; right.Normalize();
                var forward = ArenaCamera.transform.forward; forward.y = 0f; forward.Normalize();
                var dir = right * ix + forward * iz;
                if (dir.sqrMagnitude > 0.0001f) dir.Normalize();
                float speed = CameraPanSpeed * (_cameraZoom / 8f);
                _cameraPanFocus += dir * speed * dt;
                _cameraFlyTarget = null; // ручна панорама скасовує будь-який політ, що ще триває
            }

            if (_cameraFlyTarget.HasValue)
            {
                _cameraFlyElapsed += dt;
                float t = Mathf.Clamp01(_cameraFlyElapsed / CameraFlyDuration);
                float eased = t * t * (3f - 2f * t);
                _cameraPanFocus = Vector3.Lerp(_cameraFlyStart, _cameraFlyTarget.Value, eased);
                if (t >= 1f) _cameraFlyTarget = null;
            }

            var clamped = BattleArenaView.ClampPanTarget(_cameraPanFocus.x, _cameraPanFocus.z, _gridWidth, _gridHeight, CameraPanMargin);
            _cameraPanFocus = new Vector3(clamped.X, 0f, clamped.Z);

            var offset = BattleArenaView.CameraOffsetFromFocus(CameraTiltDegrees, _cameraYawDegrees, CameraDistance);
            var targetPos = _cameraPanFocus + new Vector3(offset.X, offset.Y, offset.Z);
            var targetRot = Quaternion.LookRotation(-new Vector3(offset.X, offset.Y, offset.Z).normalized, Vector3.up);

            float k = 1f - Mathf.Exp(-8f * dt);
            ArenaCamera.transform.position = Vector3.Lerp(ArenaCamera.transform.position, targetPos, k);
            ArenaCamera.transform.rotation = Quaternion.Slerp(ArenaCamera.transform.rotation, targetRot, k);
            if (ArenaCamera.orthographic)
                ArenaCamera.orthographicSize = Mathf.Lerp(ArenaCamera.orthographicSize, _cameraZoom, k);
        }

        // ================= виконання команд + переклад стрічки подій =================

        private bool RunCommand(Func<CombatActionResult> command)
        {
            if (_session == null) return false;
            int before = _session.DayLog.Count;
            CombatActionResult result;
            try { result = command(); }
            catch (InvalidOperationException) { result = CombatActionResult.InvalidAction; }
            catch (NotImplementedException) { result = CombatActionResult.InvalidAction; }

            bool success = result == CombatActionResult.Success;
            if (!success) NoteRejection(RejectionLogLine(result));
            else _lastRejectionText = string.Empty;

            AfterCommand(before);
            return success;
        }

        private void NoteRejection(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _logLines.Add(text);
            _logEntries.Add(new BattleLogEntryUi { Round = _lastView?.Round ?? 0, Text = text, Kind = BattleLogKind.Rejection });
            _lastRejectionText = text;
            TrimLog();
        }

        private static string RejectionLogLine(CombatActionResult result)
        {
            string key;
            switch (result)
            {
                case CombatActionResult.NotEnoughAp: key = "ui.battle.action.rejected.notenoughap"; break;
                case CombatActionResult.OutOfRange: key = "ui.battle.action.rejected.outofrange"; break;
                case CombatActionResult.NoLineOfSight: key = "ui.battle.action.rejected.nolineofsight"; break;
                case CombatActionResult.NotReachable: key = "ui.battle.action.rejected.notreachable"; break;
                case CombatActionResult.InvalidTarget: key = "ui.battle.action.rejected.invalidtarget"; break;
                case CombatActionResult.OnCooldown: key = "ui.battle.action.rejected.oncooldown"; break;
                default: key = "ui.battle.action.rejected"; break;
            }
            return UkrainianText.Get(key, Gender.Male);
        }

        private void AfterCommand(int dayLogCountBefore)
        {
            var log = _session.DayLog;
            for (int i = dayLogCountBefore; i < log.Count; i++)
                ConsumeEvent(log[i]);
            _lastKnownDayLogCount = log.Count;

            _armed = ArmedAction.None;
            _armedAbilityId = null;

            var freshView = _session.GetBattleView();
            if (freshView != null)
            {
                _lastView = freshView;
                RefreshIntentOverlayTiles();
                ProcessNewBattleLog(freshView);
            }
        }

        /// <summary>
        /// Новий рядок <c>BattleView.Log</c> — і легасі-рядок (<see cref="LogLines"/>,
        /// сумісність зі старим HUD), і типовий (<see cref="LogEntries"/>, §7.4:
        /// <c>BattleLogText.Entry</c>), і, якщо відповідний сенс має такт (§6) —
        /// у чергу <see cref="_pendingTacts"/>. Один курсор на все трьох.
        /// </summary>
        private void ProcessNewBattleLog(BattleView view)
        {
            if (view?.Log == null) return;
            if (_battleLogCursor > view.Log.Count) _battleLogCursor = 0; // інший бій без Enter — почати спочатку

            for (; _battleLogCursor < view.Log.Count; _battleLogCursor++)
            {
                var entry = view.Log[_battleLogCursor];

                string legacyLine = BattleLogText.Line(entry, view.IsHitRulePercent, NameForUnitId, IsFemaleCompanion);
                if (!string.IsNullOrEmpty(legacyLine)) _logLines.Add(legacyLine);

                var typed = BattleLogText.Entry(entry, view, _protagonistGender);
                if (typed != null && !string.IsNullOrEmpty(typed.Text)) _logEntries.Add(typed);

                var kind = ClassifyTact(entry);
                if (kind != TactKind.Skip) _pendingTacts.Enqueue(new PendingTact { Entry = entry, Kind = kind });
            }
            TrimLog();
        }

        private void ConsumeEvent(GameEvent evt)
        {
            switch (evt.Key)
            {
                case "combat.battle.resolved":
                case "combat.autoresolved":
                    _resultPending = true;
                    evt.Args.TryGetValue("outcome", out _resultOutcomeKey);
                    evt.Args.TryGetValue("rounds", out _resultRounds);
                    if (string.Equals(evt.Key, "combat.autoresolved", StringComparison.Ordinal))
                        _logLines.Add(UkrainianText.Get("combat.autoresolved", Gender.Male));
                    break;
                case "companion.died":
                    AppendDeathCasualty(evt);
                    break;
                case "scar.granted":
                    AppendScarCasualty(evt);
                    break;
            }
            TrimLog();
        }

        private void AppendDeathCasualty(GameEvent evt)
        {
            evt.Args.TryGetValue("companionId", out var companionId);
            bool female = IsFemaleCompanion(companionId);
            string name = ResolveCompanionName(companionId, female);
            string line = UkrainianText.Format(female ? "companion.died.f" : "companion.died.m", female, "companionId", name);
            _resultCasualtyLines.Add(line);
        }

        private void AppendScarCasualty(GameEvent evt)
        {
            evt.Args.TryGetValue("companionId", out var companionId);
            evt.Args.TryGetValue("scarId", out var scarId);
            bool female = IsFemaleCompanion(companionId);
            string name = ResolveCompanionName(companionId, female);
            string scarText = UkrainianText.Get("scar." + scarId, female);
            string line = UkrainianText.Format("scar.granted", female, "companionId", name, "scarId", scarText);
            _resultCasualtyLines.Add(line);
        }

        private void TrimLog()
        {
            while (_logLines.Count > MaxLogLines) _logLines.RemoveAt(0);
            while (_logEntries.Count > MaxLogLines) _logEntries.RemoveAt(0);
        }

        // ================= імена/рід =================

        private string NameForUnitId(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return UkrainianText.MissingMarker(null);
            if (_lastView?.Units != null)
                foreach (var u in _lastView.Units)
                    if (string.Equals(u.Id, unitId, StringComparison.Ordinal))
                        return ResolveDisplayNameInternal(u);
            return UkrainianText.MissingMarker(unitId);
        }

        private string ResolveCompanionName(string companionId, bool female)
        {
            if (string.IsNullOrEmpty(companionId)) return UkrainianText.MissingMarker(null);
            string protagonistName = TryResolveProtagonistDisplayName(companionId);
            if (protagonistName != null) return protagonistName;
            string key = "char." + companionId;
            return UkrainianText.Has(key, female) ? UkrainianText.Get(key, female) : UkrainianText.MissingMarker(key);
        }

        private string TryResolveProtagonistDisplayName(string bareId)
        {
            if (!string.Equals(bareId, GameSession.ProtagonistId, StringComparison.Ordinal)) return null;
            var protagonist = ScreenText.FindCompanion(_session?.GetRosterView(), GameSession.ProtagonistId);
            return !string.IsNullOrEmpty(protagonist?.DisplayName) &&
                   protagonist.DisplayName != Game.Core.Scenes.OpeningScenes.ProtagonistPlaceholderName
                ? protagonist.DisplayName : null;
        }

        /// <summary>Ім'я з порядковим номером (§7.4 ResolveDisplayName): <c>BattleUnitView.Ordinal</c> &gt; 0 додає «I»/«II»/… через <see cref="BattleArenaView.WithOrdinal"/>.</summary>
        private string ResolveDisplayNameInternal(BattleUnitView unit)
        {
            if (unit == null) return UkrainianText.MissingMarker(null);

            string protagonistName = TryResolveProtagonistDisplayName(BareUnitId(unit.Id));
            if (protagonistName != null) return BattleArenaView.WithOrdinal(protagonistName, unit.Ordinal);

            string key = ResolveNameKey(unit);
            bool female = IsFemaleCompanion(unit.Id);

            if (key != null && UkrainianText.Has(key, female))
                return BattleArenaView.WithOrdinal(UkrainianText.Get(key, female), unit.Ordinal);
            if (!string.IsNullOrEmpty(unit.DisplayNameKey) && UkrainianText.Has(unit.DisplayNameKey, female))
                return BattleArenaView.WithOrdinal(UkrainianText.Get(unit.DisplayNameKey, female), unit.Ordinal);

            if (string.Equals(unit.Side, "Player", StringComparison.Ordinal) && !string.IsNullOrEmpty(unit.DisplayNameKey))
                return BattleArenaView.WithOrdinal(unit.DisplayNameKey, unit.Ordinal);

            return UkrainianText.MissingMarker(key ?? unit.DisplayNameKey ?? unit.Id);
        }

        private static string ResolveNameKey(BattleUnitView unit)
        {
            string id = unit.Id ?? string.Empty;
            if (id.StartsWith("u_", StringComparison.Ordinal)) return "char." + id.Substring(2);
            if (id.StartsWith("defector_", StringComparison.Ordinal)) return "char." + id.Substring(9);

            if (!string.Equals(unit.Side, "Player", StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(unit.DisplayNameKey))
                return "enemy." + unit.DisplayNameKey;

            return null;
        }

        private static string BareUnitId(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return unitId;
            if (unitId.StartsWith("u_", StringComparison.Ordinal)) return unitId.Substring(2);
            if (unitId.StartsWith("defector_", StringComparison.Ordinal)) return unitId.Substring(9);
            return unitId;
        }

        private bool IsFemaleCompanion(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return false;
            if (string.Equals(BareUnitId(unitId), GameSession.ProtagonistId, StringComparison.Ordinal))
                return _protagonistGender == Gender.Female;
            return unitId.IndexOf("myroslava", StringComparison.Ordinal) >= 0;
        }

        // ================= камера/тіньова оболонка =================

        private void SwapToArenaCamera()
        {
            if (_hubCamera == null)
            {
                var hub = GameObject.Find(HubCameraName);
                _hubCamera = hub != null ? hub.GetComponent<Camera>() : null;
            }
            if (_hubRoot == null) _hubRoot = GameObject.Find(HubRootName);

            if (_hubCamera != null) _hubCamera.gameObject.SetActive(false);
            if (_hubRoot != null) _hubRoot.SetActive(false);
            if (ArenaCamera != null) ArenaCamera.gameObject.SetActive(true);
        }

        private void RestoreHubCamera()
        {
            if (ArenaCamera != null) ArenaCamera.gameObject.SetActive(false);
            if (_hubCamera != null) _hubCamera.gameObject.SetActive(true);
            if (_hubRoot != null) _hubRoot.SetActive(true);
        }

        private void TeardownAndDeactivate()
        {
            _active = false;
            _resultPending = false;
            _armed = ArmedAction.None;
            _session = null;
            if (_shotLine != null) _shotLine.enabled = false;
            if (_pathLine != null) _pathLine.enabled = false;
            if (_pathEndMarker != null) _pathEndMarker.SetActive(false);

            RestoreHubCamera();
            if (ArenaRoot != null) ArenaRoot.SetActive(false);
        }

        private static void ClearChildren(Transform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);
        }
    }
}
