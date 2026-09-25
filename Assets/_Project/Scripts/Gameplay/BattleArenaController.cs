using System;
using System.Collections.Generic;
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
    /// Пакет E2 — 3D-презентація тактичного бою: <see cref="IBattlePresenter"/>
    /// над <c>GameSession.GetBattleView()</c>. Будує грид/юнітів з
    /// <see cref="BattleArenaBuilder"/>-заготовлених префабів Kenney, читає
    /// мишу через <c>Physics.Raycast</c>, виконує <c>GameSession.Combat*</c>.
    ///
    /// ЛІНТ-ВИКЛЮЧЕНО (Physics/Renderer/Camera/Collider — та сама причина, що
    /// в <c>VillageStage</c>/<c>GameSceneBuilder</c>, §5 TEST_BUILD.md):
    /// заглушка UnityEngine чесно не покриває цю глибину API, а фальшиве
    /// «збирається» гірше за відсутність лінту взагалі. Уся раскладкова
    /// математика — окремо, у <see cref="BattleArenaView"/> (чистий C#, і
    /// лінтиться, і вкрита тестами).
    ///
    /// ВІДОМІ РОЗРИВИ КОНТРАКТУ GameSession (не мого володіння — див. звіт
    /// пакета E2): (1) <c>BattleUnitView.DisplayNameKey</c> сьогодні несе НЕ
    /// готовий ключ таблиці, а сирий службовий рядок (<c>Companion.DisplayName</c>
    /// для гравця, короткий id <c>EnemyDefinition.DisplayName</c> для ворога) —
    /// <see cref="ResolveNameKey"/> відновлює справжній ключ за Id/стороною
    /// (<c>BattleUnitView.Side</c>), а для player-side юніта без відомого
    /// префікса id (Тренувальний бій: "trainee_1"/"trainee_2") віддає
    /// <c>DisplayNameKey</c> як є замість позначки відсутнього ключа; (2) рід
    /// протагоніста читається з <c>GameSession.GetProtagonistCreationView().Gender</c>
    /// (сам виклик без охорони стану — безпечно в будь-який момент, кешується в
    /// <c>_protagonistGender</c> на <see cref="Enter"/> і передається сусідньому
    /// <see cref="PortraitRig"/>, бо його <c>IPortraitProvider.GetPortrait</c> не
    /// приймає сесію) — АЛЕ ЦЕ ЧИТАННЯ НЕ ПОВНЕ (фікс-ревью, major, розрив ЯДРА
    /// поза файлами E2): <c>GameSession.ComposeSave</c>/<c>ApplySave</c> не
    /// серіалізують ані <c>_pendingGender</c>, ані <c>_protagonistGender</c>, а
    /// <c>ContinueGame(slot)</c> йде крізь <c>NewGame(SkipCreation:true)</c>, що
    /// оминає гілку скидання <c>_pendingGender</c> на дефолт — у щойно
    /// відкритому процесі після «Продовжити збереження» це поле стоїть на
    /// дефолтному <c>Gender.Male</c> незалежно від того, якою протагоністку
    /// створив гравець, тож і бойовий рід, і портрет <see cref="PortraitRig"/>
    /// мовчки помиляються саме в найпоширенішому потоці (відкрити гру →
    /// продовжити → бій). Не закривається звідси: справжній фікс — персистити
    /// gender= у ComposeSave/ApplySave, це власник GameSession.cs; (3) немає
    /// <c>GameSession.CombatStabilize</c>/<c>CombatRetreat</c> — <c>CombatState</c>
    /// має обидва методи, фасад жоден не обгортає, тому кнопок
    /// «Стабілізувати»/«Відступ» тут немає (сам TEST_BUILD.md позначає
    /// «Відступ» як «якщо існує» — не існує); (4) немає
    /// напрямку прицілу дозору в <c>BattleUnitView</c> — показаний лише
    /// індикатор «у дозорі», без конуса напрямку.
    ///
    /// ЗАКРИТО (25.09.2026, скарга «не можу нормально щось використати в
    /// бою»): пункт (4) старого списку — <c>BattleView</c> тепер несе
    /// <c>BattleUnitView.Abilities</c> (реальний набір ЦЬОГО юніта, з ціною
    /// AP і відкатом), а не фіксований каталог, однаковий для всіх. HUD
    /// (<see cref="Game.Gameplay.UI.BattleHudScreen.DrawAbilities"/>) сірить
    /// кнопку з причиною замість мовчазної відмови по кліку;
    /// <see cref="RunCommand"/> перекладає конкретний <c>CombatActionResult</c>
    /// (не тільки Success/fail) у журнал бою.
    /// </summary>
    public sealed class BattleArenaController : MonoBehaviour, IBattlePresenter, IBattleHudData
    {
        // ================= призначається BattleArenaBuilder (Editor) =================

        public GameObject ArenaRoot;
        public Camera ArenaCamera;
        public string HubCameraName = "HubCamera";

        /// <summary>
        /// Фікс-ревью (minor, раунд 2, знайдено QA): <c>World/Hub</c> і
        /// <c>World/BattleArena</c> ділять ту саму систему координат
        /// (GameSceneBuilder жодного разу не зсуває арену — обидва корені
        /// починаються з (0,0,0) під <c>World</c>), тому грид бою (світовий
        /// X/Z 0..~10, <see cref="BattleArenaView.TileToWorld"/>) впритул
        /// накладається на розкладку хутора (пости/будівлі приблизно в тому
        /// самому діапазоні). Раніше <see cref="SwapToArenaCamera"/> міняла
        /// лише КАМЕРИ — хаб (жителі на постах тощо) лишався активним і
        /// потрапляв у кадр камери бою згори як непідписана фігура без
        /// кільця сторони. Повний шлях (не голе "Hub" — під <c>UI/</c> є
        /// однойменний порожній корінь) знаходить саме 3D-хаб.
        /// </summary>
        public string HubRootName = "World/Hub";

        public GameObject[] MaleCharacterPrefabs = new GameObject[0];
        public GameObject[] FemaleCharacterPrefabs = new GameObject[0];
        public GameObject[] CoverHalfPrefabs = new GameObject[0];
        public GameObject[] CoverFullPrefabs = new GameObject[0];

        /// <summary>
        /// Фікс-ревью (major, знайдено QA): земля тайла — більше не голий
        /// <c>PrimitiveType.Quad</c> з одним суцільним кольором (§RebuildGrid).
        /// <c>ground_grass.fbx</c> (Kenney Nature Kit) — виміряно РІВНО 1×0×1 з
        /// центром у (0,0,0), той самий тайл-модуль, що вже дає
        /// <c>cliff_block_rock.fbx</c> для Full-укриття: підганяти масштаб не
        /// треба. <c>null</c> — фолбек на старий Quad (наприклад, якщо
        /// BattleArenaBuilder не знайшов модель), не порожня арена.
        /// </summary>
        public GameObject TileGroundPrefab;

        // ================= рантайм-стан =================

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

        private ArmedAction _armed = ArmedAction.None;
        private string _armedAbilityId;
        private GridPos? _hoveredTile;
        private string _hoveredUnitId;
        private int _hoveredHitChance;
        private int _hoveredDamageMin, _hoveredDamageMax, _hoveredDamageCrit;

        /// <summary>Масштаб моделі юніта відносно вихідного розміру Kenney Mini Characters (§SpawnOrUpdateUnit) — той самий множник контр-масштабує підпис імені (§BuildNameLabel), щоб текст не ріс разом із фігурою.</summary>
        private const float UnitVisualScale = 1.35f;

        private bool _resultPending;
        private string _resultOutcomeKey;
        private string _resultRounds;
        private readonly List<string> _resultCasualtyLines = new List<string>();
        private readonly List<string> _logLines = new List<string>();
        private const int MaxLogLines = 40;

        /// <summary>
        /// Скільки рядків <c>BattleView.Log</c> (журнал бою, ключі combat.log.*)
        /// уже перекладено в <see cref="_logLines"/>. Журнал лише росте, поки
        /// живе бій, тож курсор — просто лічильник; новий бій починає з нуля
        /// (<see cref="Enter"/>).
        /// </summary>
        private int _battleLogCursor;

        /// <summary>
        /// Фаза F: скільки записів <c>_session.DayLog</c> уже пройшло крізь
        /// <see cref="AfterCommand"/>. RunCommand/RequestAutoResolve рахують
        /// свій власний "before" ЛОКАЛЬНО (бо самі й викликали команду щойно
        /// перед цим) — це поле держить той самий курсор МІЖ кадрами, щоб
        /// <see cref="DetectExternalResolution"/> (Update(), не команда)
        /// знала, з якого місця читати нові записи.
        /// </summary>
        private int _lastKnownDayLogCount;

        // ================= публічний зріз для BattleHudScreen =================

        public BattleView View => _lastView;
        public bool ResultPending => _resultPending;
        public string ResultOutcomeKey => _resultOutcomeKey;
        public string ResultRounds => _resultRounds;
        public IReadOnlyList<string> ResultCasualtyLines => _resultCasualtyLines;
        public IReadOnlyList<string> LogLines => _logLines;
        public ArmedAction Armed => _armed;
        public string ArmedAbilityId => _armedAbilityId;
        public string HoveredUnitId => _hoveredUnitId;
        public int HoveredHitChance => _hoveredHitChance;
        public int HoveredDamageMin => _hoveredDamageMin;
        public int HoveredDamageMax => _hoveredDamageMax;
        public int HoveredDamageCrit => _hoveredDamageCrit;

        public bool IsPlayerTurn
        {
            get
            {
                var unit = CurrentUnit();
                return unit != null && string.Equals(unit.Side, "Player", StringComparison.Ordinal);
            }
        }

        public string ResolveDisplayName(BattleUnitView unit) => ResolveDisplayNameInternal(unit);

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
            _battleLogCursor = 0;

            // Рід протагоніста — за GameSession.GetProtagonistCreationView() (без
            // охорони стану, безпечно в будь-який момент, §GetProtagonistCreationView
            // реального коду): PortraitRig не бачить GameSession (фіксована сигнатура
            // IPortraitProvider.GetPortrait не приймає сесію), тож контролер — єдине
            // місце шва, що його знає, — передає рід сусідньому компоненту напряму.
            _protagonistGender = _session?.GetProtagonistCreationView()?.Gender ?? Gender.Male;
            var portraitRig = ArenaRoot != null ? ArenaRoot.GetComponent<PortraitRig>() : null;
            if (portraitRig != null) portraitRig.ProtagonistGender = _protagonistGender;

            if (ArenaRoot != null) ArenaRoot.SetActive(true);
            SwapToArenaCamera();

            _lastView = _session?.GetBattleView();
            RebuildGrid(_lastView);
            RebuildUnits(_lastView);
            FrameCamera(_lastView);
            AppendNewBattleLog(_lastView);

            // Фаза F: курсор DayLog стартує від ПОТОЧНОГО розміру — не 0, щоб
            // не перечитувати записи з-ДО цього бою (вони однаково без
            // combat.*-ключів, ConsumeEvent їх ігнорує, але навіщо зайва
            // робота щоразу, коли бій розв'язується зовнішньою командою).
            _lastKnownDayLogCount = _session?.DayLog.Count ?? 0;
        }

        public void Exit()
        {
            TeardownAndDeactivate();
        }

        public void DrawHud(GameSession session)
        {
            if (!_active) return;
            _session = session;
            Refresh();
            BattleHudScreen.Draw(this);
        }

        /// <summary>Гравець підтвердив панель результату («Далі») — тепер справді виходимо з презентера.</summary>
        public void AcknowledgeResult()
        {
            TeardownAndDeactivate();
        }

        // ================= намір гравця =================

        public void ArmAbility(string abilityId) { if (IsPlayerTurn) { _armed = ArmedAction.Ability; _armedAbilityId = abilityId; } }
        public void ArmOverwatchAim() { if (IsPlayerTurn) { _armed = ArmedAction.OverwatchAim; _armedAbilityId = null; } }
        public void CancelArmed() { _armed = ArmedAction.None; _armedAbilityId = null; }

        public void RequestEndTurn() => RunCommand(() => _session.CombatEndTurn());

        public void RequestAutoResolve()
        {
            if (_session == null) return;
            int before = _session.DayLog.Count;
            try
            {
                _session.CombatAutoResolve();
            }
            catch (InvalidOperationException)
            {
                // Див. коментар у RunCommand — той самий перегон "бій щойно
                // розв'язався кліком, що протік крізь HUD" стосується й
                // кнопки Автобою (фікс-ревью, major).
                _logLines.Add(UkrainianText.Get("ui.battle.action.rejected", Gender.Male));
            }
            AfterCommand(before);
        }

        // ================= кадровий цикл =================

        private void Update()
        {
            if (!_active || _resultPending) return;
            UpdateHover();  // спершу курсор — щоб підсвітка й прев'ю нижче бачили цей самий кадр, не попередній
            Refresh();
            HandleClicks();

            // Фаха F знахідка (тур-автоплей): бій може розв'язатись командою,
            // що обійшла RunCommand/RequestAutoResolve — напр.
            // AutoplayGameDriver кличе GameSession.Combat* напряму через
            // shell.TryRun (як і IMGUI-фолбек BattleScreen.cs). GameSession.
            // State вже пішов ДАЛІ (OnBattleResolved зсуває його синхронно
            // всередині самої команди), а презентер про завершення бою не
            // дізнався б: жоден AfterCommand не викликався, _resultPending
            // лишався б false НАЗАВЖДИ, GameShell більше не малює DrawBattle()
            // для стану поза Battle — і TeardownAndDeactivate ніколи не
            // спрацьовував би. Арена (юніти, підписи, камера) лишалась би
            // видимою У ФОНІ кожного наступного екрана до кінця гри.
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
                ApplyUnitPositionsAndHighlights(view);
                // Хід, зроблений повз RunCommand (автопрогон, фолбек), теж потрапляє в журнал.
                AppendNewBattleLog(view);
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
                ApplyTileTint(tile, key, cover, walkable, isReachable: false, isCurrent: false, isHovered: false);

                // Фікс-ревью (major, знайдено QA): раніше вимагало "walkable
                // &&" — непрохідні тайли з укриттям (Full-камінь/скеля
                // ЗАЗВИЧАЙ саме непрохідні) лишались БЕЗ моделі, тільки
                // тьмяний тінт (§ApplyTileTint, walkable=false завжди темний
                // незалежно від cover) — гравець бачив однакову темну
                // пляму замість каменя/скелі. PlaceCoverProp сам знімає
                // колайдери моделі (декоративне, не ціль променя), тож
                // непрохідність тайла це не ламає.
                if (!string.Equals(cover, "None", StringComparison.Ordinal))
                    PlaceCoverProp(x, y, cover, world);
            }
        }

        /// <summary>
        /// Один тайл ґрунту: реальна модель Kenney (<see cref="TileGroundPrefab"/>,
        /// 1×0×1 з центром у (0,0,0) — жодного підбору масштабу не треба, §поле
        /// TileGroundPrefab) або фолбек-Quad, коли модель не призначено (Editor
        /// не знайшов файл — арена все одно має чим стояти). FBX-префаб не несе
        /// власного колайдера (на відміну від <c>CreatePrimitive</c>, що додає
        /// його сам) — тому колайдер тут завжди свій, один, на корені з ім'ям
        /// "tile:x_y" (задає викликач одразу після повернення): той самий
        /// контракт, що й <see cref="SpawnOrUpdateUnit"/> для юнітів, потрібен
        /// <see cref="UpdateHover"/> (читає ім'я об'єкта під променем напряму).
        /// </summary>
        private GameObject BuildTileGameObject(WorldPos world)
        {
            if (TileGroundPrefab == null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.transform.SetParent(_tileRoot, false);
                quad.transform.localPosition = new Vector3(world.X, world.Y, world.Z);
                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                // Полірування (ціль 3 «Бойові декорації», owner: "a grid
                // shown as subtle lines"): 0.985 лишає межу тайла тонкою
                // лінією, не суцільною чорною сіткою (§ стара версія цього
                // коментаря вище RebuildGrid).
                quad.transform.localScale = new Vector3(BattleArenaView.TileSize * 0.985f, BattleArenaView.TileSize * 0.985f, 1f);
                return quad;
            }

            var tile = Instantiate(TileGroundPrefab, _tileRoot);
            tile.transform.SetParent(_tileRoot, false);
            tile.transform.localPosition = new Vector3(world.X, world.Y, world.Z);
            tile.transform.localRotation = Quaternion.identity; // модель уже лежить лицем угору — не Quad, 90° по X тут зайві

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

            // Декоративне — не ціль для мишачого променя: прибираємо колайдери
            // моделі, щоб клік по тайлу під укриттям не губився на камені/тину.
            foreach (var collider in go.GetComponentsInChildren<Collider>()) Destroy(collider);
        }

        private void RebuildUnits(BattleView view)
        {
            ClearChildren(_unitRoot);
            _unitObjects.Clear();
            _unitBlocks.Clear();
            _unitRenderers.Clear();

            if (view?.Units == null) return;
            foreach (var unit in view.Units) SpawnOrUpdateUnit(unit);
        }

        private void SpawnOrUpdateUnit(BattleUnitView unit)
        {
            if (!_unitObjects.TryGetValue(unit.Id, out var go) || go == null)
            {
                var prefab = PickCharacterPrefab(unit.Id);
                go = prefab != null ? Instantiate(prefab, _unitRoot) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "unit:" + unit.Id;
                go.transform.SetParent(_unitRoot, false);
                // Полірування (ціль 3 «Бойові декорації», owner: "units
                // scaled up to read well"): Kenney Mini Characters дрібні
                // проти клітини 1×1 — на камері зверху фігура губилась між
                // підписом і кільцем сторони.
                go.transform.localScale = Vector3.one * UnitVisualScale;

                // Модель Kenney може нести власні колайдери на дочірніх об'єктах —
                // прибираємо їх усі й тримаємо РІВНО один, на корені, з відомим
                // ім'ям "unit:<id>": так HandleInput читає ціль напряму з
                // hit.collider.gameObject.name, без непевного пошуку по батьках.
                foreach (var stale in go.GetComponentsInChildren<Collider>()) Destroy(stale);
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.8f, 0f);
                box.size = new Vector3(0.6f, 1.6f, 0.6f);

                BuildSideRing(go, unit);
                BuildNameLabel(go, unit);

                _unitObjects[unit.Id] = go;
                _unitBlocks[unit.Id] = new MaterialPropertyBlock();
                // Кешуємо набір рендерів РІВНО раз при спавні: тіло моделі не
                // змінюється між кадрами (кільце "overwatch" додається/знімається
                // окремо і в цей масив не входить — ApplyUnitVisual все одно
                // фільтрує його за ім'ям, тож відсутність у кеші нешкідлива).
                _unitRenderers[unit.Id] = go.GetComponentsInChildren<Renderer>();
            }

            ApplyUnitVisual(go, unit);
        }

        private GameObject PickCharacterPrefab(string unitId)
        {
            var pool = IsFemaleCompanion(unitId) ? FemaleCharacterPrefabs : MaleCharacterPrefabs;
            if (pool == null || pool.Length == 0) return null;
            int index = (int)(BattleArenaView.Hash01(unitId ?? "unit") * pool.Length);
            if (index >= pool.Length) index = pool.Length - 1;
            return pool[index];
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

        /// <summary>
        /// Фікс-ревью (ціль А «Бойові декорації», owner: "readable name labels
        /// that don't cover neighbours" — знайдено тур-автоплеєм): фіксований
        /// <c>characterSize=0.1</c> давав ширину підпису, що росте прямо
        /// пропорційно довжині імені, без стелі. На тісному строю (два
        /// сусідні тайли — 1 світова одиниця) довгі імена на кшталт
        /// "Розвідник орди"/"Застрільник орди" налягали на сусідній підпис
        /// суцільним нечитабельним текстом. Обидва фікси тут:
        /// (1) <see cref="UnitVisualScale"/> тепер масштабує саму фігуру —
        /// підпис контр-масштабується на той самий множник, щоб не рости
        /// разом з нею (інакше довгі імена стали б ще ширшими, ніж до
        /// збільшення юнітів); (2) розмір символу обернено пропорційний
        /// довжині імені — короткі імена лишаються великими (стеля = старий
        /// дефолт 0.1), довгі стають дрібнішими, і сумарна ширина підпису
        /// тримається приблизно в межах одного тайла незалежно від довжини.
        /// </summary>
        private void BuildNameLabel(GameObject unitGo, BattleUnitView unit)
        {
            var label = new GameObject("label");
            label.transform.SetParent(unitGo.transform, false);
            label.transform.localPosition = new Vector3(0f, BattleArenaView.NameLabelHeight, 0f);
            // Камера арени дивиться зверху вниз (GameSceneBuilder.BuildArenaCamera,
            // поворот 90° по X) — підпис лежить лицем угору, а не крутиться до
            // камери: той самий підхід, що KitBuilder.Plot для ізометрії хаба.
            label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Батько (unitGo) тепер масштабований на UnitVisualScale — без
            // контр-масштабу тут підпис ріс би разом із фігурою.
            label.transform.localScale = Vector3.one / UnitVisualScale;

            string name = ResolveDisplayNameInternal(unit);
            int len = Mathf.Max(name != null ? name.Length : 0, 9);

            var text = label.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 32;
            text.characterSize = Mathf.Clamp(0.75f / len, 0.04f, 0.095f);
            text.anchor = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = name;

            var renderer = label.GetComponent<MeshRenderer>();
            if (renderer != null && text.font != null) renderer.sharedMaterial = text.font.material;
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

            // Юніт зник зі списку (вибув насовсім) — прибираємо модель.
            var stale = new List<string>();
            foreach (var kv in _unitObjects)
                if (!alive.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale)
            {
                if (_unitObjects[id] != null) Destroy(_unitObjects[id]);
                _unitObjects.Remove(id);
                _unitBlocks.Remove(id);
                _unitRenderers.Remove(id);
            }

            var reachable = new HashSet<string>(StringComparer.Ordinal);
            if (view.ReachableTiles != null)
                foreach (var pos in view.ReachableTiles) reachable.Add(pos.X + "_" + pos.Y);

            for (int y = 0; y < _gridHeight; y++)
            for (int x = 0; x < _gridWidth; x++)
            {
                string key = x + "_" + y;
                if (!_tileObjects.TryGetValue(key, out var tile) || tile == null) continue;

                int index = x + y * _gridWidth;
                string cover = view.Grid.TileCover != null && index < view.Grid.TileCover.Count ? view.Grid.TileCover[index] : "None";
                bool walkable = view.Grid.TileWalkable == null || index >= view.Grid.TileWalkable.Count || view.Grid.TileWalkable[index];
                bool isReachable = reachable.Contains(key);
                bool isHovered = _hoveredTile.HasValue && _hoveredTile.Value.X == x && _hoveredTile.Value.Y == y;

                // Тайл поточного юніта перефарбовується другим проходом нижче
                // (isCurrent тут завжди false) — так координата не рахується двічі.
                ApplyTileTint(tile, key, cover, walkable, isReachable, false, isHovered);
            }

            var current = CurrentUnit();
            string currentKey = current != null ? current.Pos.X + "_" + current.Pos.Y : null;
            if (currentKey != null && _tileObjects.TryGetValue(currentKey, out var currentTile) && currentTile != null)
            {
                int index = current.Pos.X + current.Pos.Y * _gridWidth;
                string cover = view.Grid.TileCover != null && index < view.Grid.TileCover.Count ? view.Grid.TileCover[index] : "None";
                bool walkable = view.Grid.TileWalkable == null || index >= view.Grid.TileWalkable.Count || view.Grid.TileWalkable[index];
                bool isHovered = _hoveredTile.HasValue && _hoveredTile.Value.X == current.Pos.X && _hoveredTile.Value.Y == current.Pos.Y;
                ApplyTileTint(currentTile, currentKey, cover, walkable, isReachable: false, isCurrent: true, isHovered: isHovered);
            }

            UpdateHitChancePreview(current);
        }

        /// <summary>
        /// Перефарбовує тайл щокадрово (Refresh -&gt; ApplyUnitPositionsAndHighlights
        /// для всього грида, до 10×10 — §TEST_BUILD.md R9) — тому, як і
        /// <see cref="ApplyUnitVisual"/> з <c>_unitBlocks</c>, тримаємо ОДИН
        /// <see cref="MaterialPropertyBlock"/> на тайл у <see cref="_tileBlocks"/>
        /// замість <c>new MaterialPropertyBlock()</c> щокадру на кожен з ~100 тайлів.
        /// </summary>
        private void ApplyTileTint(GameObject tile, string key, string cover, bool walkable, bool isReachable, bool isCurrent, bool isHovered)
        {
            // Фікс-ревью (major, знайдено QA): тайл тепер може бути моделлю
            // Kenney (§BuildTileGameObject), не гарантовано ОДНИМ рендером,
            // як був голий Quad — той самий MaterialPropertyBlock іде на всі
            // рендери під коренем, інакше частина меша лишалась би в
            // кольорі матеріалу за замовчуванням, поки решта фарбувалась.
            var renderers = tile.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var tint = BattleArenaView.TintFor(cover, walkable, isReachable, isCurrent, isHovered);
            if (!_tileBlocks.TryGetValue(key, out var block) || block == null)
            {
                block = new MaterialPropertyBlock();
                _tileBlocks[key] = block;
            }
            block.Clear();
            block.SetColor("_BaseColor", new Color(tint.R, tint.G, tint.B, tint.A));
            foreach (var renderer in renderers) renderer.SetPropertyBlock(block);
        }

        private void ApplyUnitVisual(GameObject go, BattleUnitView unit)
        {
            var world = BattleArenaView.TileToWorld(unit.Pos.X, unit.Pos.Y);
            float sink = unit.IsDowned ? BattleArenaView.DownedSink : 0f;
            go.transform.localPosition = new Vector3(world.X, sink, world.Z);
            go.transform.localRotation = unit.IsDowned ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;

            var renderers = _unitRenderers.TryGetValue(unit.Id, out var cachedRenderers) && cachedRenderers != null
                ? cachedRenderers
                : go.GetComponentsInChildren<Renderer>();
            var palette = BattleArenaView.CharacterTint(unit.Id, unit.Side, unit.DisplayNameKey);
            var block = _unitBlocks.TryGetValue(unit.Id, out var b) ? b : new MaterialPropertyBlock();
            block.Clear();
            block.SetColor("_BaseColor", new Color(palette.R, palette.G, palette.B, unit.IsDowned ? 0.55f : 1f));
            foreach (var r in renderers)
            {
                // "ring"/"overwatch" тримають власний колір (сторона/дозор), "label" — текст імені:
                // жоден із трьох не мав би щокадру перефарбовуватися в колір тіла персонажа.
                string n = r.gameObject.name;
                if (n == "ring" || n == "overwatch" || n == "label") continue;
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

        // ================= прев'ю шансу =================

        private void UpdateHitChancePreview(BattleUnitView current)
        {
            _hoveredHitChance = 0;
            _hoveredDamageMin = _hoveredDamageMax = _hoveredDamageCrit = 0;
            if (_session == null || current == null || string.IsNullOrEmpty(_hoveredUnitId)) return;
            if (string.Equals(_hoveredUnitId, current.Id, StringComparison.Ordinal)) return;
            _hoveredHitChance = _session.PreviewHitChance(current.Id, _hoveredUnitId);
            _session.PreviewDamage(current.Id, _hoveredUnitId, out _hoveredDamageMin, out _hoveredDamageMax, out _hoveredDamageCrit);
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

        // ================= ввід миші =================

        /// <summary>Лише читає, куди дивиться курсор — жодної команди. Викликається до <see cref="Refresh"/>, щоб підсвітка/прев'ю шансу цього ж кадру бачили свіжий наведений тайл/юніт.</summary>
        private void UpdateHover()
        {
            _hoveredTile = null;
            _hoveredUnitId = null;
            if (ArenaCamera == null || IsPointerOverHud()) return;

            var ray = ArenaCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 500f)) return;

            // Колайдер юніта — рівно один, на корені "unit:<id>" (усі власні
            // колайдери моделі знято при спавні, див. SpawnOrUpdateUnit), тож
            // ім'я самого колайдера — вже потрібна відповідь, без ходіння по батьках.
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

        /// <summary>
        /// Фікс-ревью (блокер): HUD (<see cref="BattleHudScreen"/>) малює IMGUI-
        /// панель у лівій третині екрана (padding..padding+width), а
        /// <see cref="FrameCamera"/> кадрує ВВЕСЬ грід під ортографічною
        /// камерою — тобто арена рендериться і під панеллю теж, і без цієї
        /// перевірки Physics.Raycast з тієї ж точки екрана однаково влучає в
        /// реальний тайл/юніт під кнопкою HUD. GUI-простір (початок
        /// зверху-зліва), тому Y віддзеркалюємо від Unity screen-простору
        /// <c>Input.mousePosition</c> (початок знизу-зліва) — той самий
        /// перехід, що GUIUtility.ScreenToGUIPoint без залежності від неї.
        /// </summary>
        private static bool IsPointerOverHud()
        {
            var panel = BattleHudScreen.PanelRect;
            if (panel.width <= 0f || panel.height <= 0f) return false;
            var guiPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return panel.Contains(guiPos);
        }

        private void HandleClicks()
        {
            // Фікс-ревью (блокер): курсор над панеллю BattleHudScreen — жодного
            // 3D-кліку цього кадру взагалі (навіть ПКМ-скасування: ПКМ по
            // кнопці HUD — не жест скасування прицілу). UpdateHover() того ж
            // кадру вже не поставив _hoveredTile/_hoveredUnitId у цьому
            // випадку, тож None/OverwatchAim-гілки нижче й так нічого б не
            // зробили — але ArmedAction.Ability кличе CombatUseAbility
            // незалежно від наведення (порожня ціль → ядро відхилить), і клік
            // по кнопці HUD не мусить витрачати цю спробу.
            if (IsPointerOverHud()) return;

            if (Input.GetMouseButtonDown(1))
            {
                CancelArmed();
                return;
            }

            if (!Input.GetMouseButtonDown(0) || !IsPlayerTurn) return;

            switch (_armed)
            {
                case ArmedAction.None:
                    // «Розумний клік»: ворог під курсором — атака, інакше тайл — рух.
                    // Ядро саме відхилить недосяжний тайл/ціль поза дальністю
                    // (CombatActionResult != Success) — тут не дублюємо цю перевірку.
                    if (!string.IsNullOrEmpty(_hoveredUnitId)) RunCommand(() => _session.CombatAttack(_hoveredUnitId));
                    else if (_hoveredTile.HasValue) RunCommand(() => _session.CombatMove(_hoveredTile.Value));
                    break;
                case ArmedAction.OverwatchAim:
                    if (_hoveredTile.HasValue)
                    {
                        RunCommand(() => _session.CombatEnterOverwatch(_hoveredTile.Value));
                    }
                    else if (!string.IsNullOrEmpty(_hoveredUnitId))
                    {
                        // Курсор навів на модель юніта (тайл/юніт-колайдери взаємовиключні,
                        // UpdateHover ставить рівно одне з двох) — CombatState.Overwatch
                        // приймає зайнятий тайл у межах грида так само, як порожній: клік по
                        // ворогу мусить прицілити дозор на клітину під ним, а не мовчки
                        // нічого не робити.
                        var aimUnit = FindUnitById(_hoveredUnitId);
                        if (aimUnit != null)
                            RunCommand(() => _session.CombatEnterOverwatch(new GridPos(aimUnit.Pos.X, aimUnit.Pos.Y)));
                    }
                    break;
                case ArmedAction.Ability:
                    if (!string.IsNullOrEmpty(_armedAbilityId))
                    {
                        // Знімаємо ДО RunCommand: AfterCommand скидає
                        // _armedAbilityId на null щойно команда відпрацює.
                        string abilityId = _armedAbilityId;
                        // Окреме підтвердження "здібність застосовано" більше не
                        // потрібне: журнал бою сам пише combat.log.ability і наслідки
                        // (ривок, пастка, перестановка) — AppendNewBattleLog.
                        RunCommand(() => _session.CombatUseAbility(abilityId, _hoveredUnitId, _hoveredTile));
                    }
                    break;
            }
        }

        // ================= виконання команд + переклад стрічки подій =================

        /// <summary>Повертає true, коли команда справді пройшла (Success) — виклики, яким важливо це знати (озброєна здібність, §HandleClicks), дописують власний рядок логу лише в цьому разі.</summary>
        private bool RunCommand(Func<CombatActionResult> command)
        {
            if (_session == null) return false;
            int before = _session.DayLog.Count;
            CombatActionResult result;
            try
            {
                result = command();
            }
            catch (InvalidOperationException)
            {
                // Фікс-ревью (major): жоден GameSession.Combat*-метод не ловився
                // тут — усі йдуть крізь RequireBattle(), яка кидає це саме
                // виключення, щойно State != Battle / _battle вже null. Саме
                // такий перегон відкриває клік-протік крізь HUD (фікс-ревью,
                // блокер вище): смарт-клік розв'язує бій, а той самий кадр ще
                // встигає натиснути кнопку HUD, що кличе Combat* на вже
                // порожньому бою. Без catch виняток летів би крізь
                // Update()/OnGUI() і лишав розбалансованим стек
                // GUILayout.Begin/End-груп для цього кадру. RejectionLogLine
                // нижче не має свого ключа під цей перегін — падає на
                // дефолтний "ui.battle.action.rejected", і це чесно: бою вже
                // немає, конкретики "чому" тут просто не існує.
                result = CombatActionResult.InvalidAction;
            }
            bool success = result == CombatActionResult.Success;
            if (!success)
                _logLines.Add(RejectionLogLine(result));
            AfterCommand(before);
            return success;
        }

        /// <summary>
        /// Фікс (той самий розрив, що <see cref="Game.Gameplay.UI.BattleHudScreen.DrawAbilities"/>,
        /// протилежний бік — тайл/юніт, не здібність): раніше будь-яка відмова
        /// показувала ОДИН загальний рядок "Дію неможливо виконати зараз.",
        /// хоча ядро вже точно знає причину (<c>CombatActionResult</c> — 6
        /// конкретних значень окрім Success). "Не можу нормально нічого
        /// використати" — це і є мовчазна відмова без пояснення; ядро тут не
        /// винне, воно й так рахує причину, просто ніхто її не читав.
        /// </summary>
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
            _lastKnownDayLogCount = log.Count; // Фаза F: курсор для DetectExternalResolution/наступного AfterCommand

            // Кожна дія — одноразовий намір: гравець свідомо озброює наступну
            // (менше випадкових повторних кліків, ніж «здібність лишається
            // озброєною»). Якщо бій щойно розв'язався, GetBattleView() уже
            // null (GameSession.OnBattleResolved скидає _battle синхронно в
            // тому самому виклику команди — §4.1, читано з реального коду) —
            // ResultPending виставила ConsumeEvent вище, за DayLog.
            _armed = ArmedAction.None;
            _armedAbilityId = null;

            var freshView = _session.GetBattleView();
            if (freshView != null)
            {
                _lastView = freshView;
                AppendNewBattleLog(freshView);
            }
        }

        /// <summary>
        /// Журнал бою — з <c>BattleView.Log</c> (ключі combat.log.* з аргументами),
        /// слова — <see cref="BattleLogText"/>, імена — тим самим
        /// <see cref="NameForUnitId"/>, що й решта HUD. Раніше лог збирався з
        /// DayLog і знав лише атаки та постріл дозору: рух, стани, дозор,
        /// пастки, падіння й смерть гравець не бачив узагалі.
        ///
        /// Якщо команда сама завершила бій, <c>GetBattleView()</c> уже null і
        /// останні рядки сюди не доходять — їх і не видно: одразу відкривається
        /// панель результату (<see cref="ResultPending"/>).
        /// </summary>
        private void AppendNewBattleLog(BattleView view)
        {
            if (view?.Log == null) return;
            if (_battleLogCursor > view.Log.Count) _battleLogCursor = 0; // інший бій без Enter — почати спочатку
            for (; _battleLogCursor < view.Log.Count; _battleLogCursor++)
            {
                string line = BattleLogText.Line(view.Log[_battleLogCursor], view.IsHitRulePercent, NameForUnitId, IsFemaleCompanion);
                if (!string.IsNullOrEmpty(line)) _logLines.Add(line);
            }
            TrimLog();
        }

        private void ConsumeEvent(GameEvent evt)
        {
            // combat.attack.* / combat.overwatch.triggered тут більше не
            // перекладаються: ті самі удари вже є в журналі бою
            // (AppendNewBattleLog), разом із усім, чого DayLog не знає.
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
            // Фікс-ревью (Фаза F, знайдено тур-автоплеєм): таблиця тримає
            // плейсхолдер "{companionId}" (той самий рядок аргументу, що йде
            // з GameEvent.Args — див. UkrainianText.cs "companion.died.*"),
            // не "{companion}" — панель результату бою показувала гравцю
            // буквальний рядок "{companionId} загинув на цьому шляху."
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
            // Той самий фікс, що й AppendDeathCasualty вище: таблиця "scar.granted"
            // тримає "{companionId}"/"{scarId}", не "{companion}"/"{scar}".
            string line = UkrainianText.Format("scar.granted", female, "companionId", name, "scarId", scarText);
            _resultCasualtyLines.Add(line);
        }

        private void TrimLog()
        {
            while (_logLines.Count > MaxLogLines) _logLines.RemoveAt(0);
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

        /// <summary>
        /// Фікс-ревью раунд 2 (QA, major): ця "char."+id-заглушка була ТРЕТЬОЮ
        /// окремою реалізацією імені — раунд 1 (<c>f677f18</c>/<c>6543768</c>)
        /// уже навчив протагоніста-спецвипадку і <see cref="ResolveDisplayNameInternal"/>
        /// (черга ходу, підпис на гріді, лог бою), і <c>ScreenText.ResolveCompanionName</c>
        /// (портрет/сцена/стрічка), але панель результату "Втрати:" (<see
        /// cref="AppendDeathCasualty"/>/<see cref="AppendScarCasualty"/>) досі
        /// йшла сюди напряму й показувала заглушку поля вводу "Провідниця"/
        /// "Провідник" замість обраного гравцем імені в кожному бою. Тепер
        /// метод сам перевіряє протагоніста тим самим способом, тож нової,
        /// четвертої розбіжної реалізації тут більше не буде.
        /// </summary>
        private string ResolveCompanionName(string companionId, bool female)
        {
            if (string.IsNullOrEmpty(companionId)) return UkrainianText.MissingMarker(null);
            string protagonistName = TryResolveProtagonistDisplayName(companionId);
            if (protagonistName != null) return protagonistName;
            string key = "char." + companionId;
            return UkrainianText.Has(key, female) ? UkrainianText.Get(key, female) : UkrainianText.MissingMarker(key);
        }

        /// <summary>Обране гравцем ім'я протагоніста з ростера, якщо <paramref name="bareId"/> — це він; інакше null.</summary>
        private string TryResolveProtagonistDisplayName(string bareId)
        {
            if (!string.Equals(bareId, GameSession.ProtagonistId, StringComparison.Ordinal)) return null;
            var protagonist = ScreenText.FindCompanion(_session?.GetRosterView(), GameSession.ProtagonistId);
            return !string.IsNullOrEmpty(protagonist?.DisplayName) ? protagonist.DisplayName : null;
        }

        private string ResolveDisplayNameInternal(BattleUnitView unit)
        {
            if (unit == null) return UkrainianText.MissingMarker(null);

            // Фікс-ревью (major, знайдено QA, той самий фікс, що
            // ScreenText.ResolveCompanionName дістав у f677f18): протагоніста
            // читаємо з ростера ДО загального пошуку "char."+id — той фікс
            // торкнувся портрета/підпису мовця сцени/стрічки подій, але не цю,
            // ПАРАЛЕЛЬНУ реалізацію імен бойового екрана (черга ходу й підпис
            // на гріді) — вона й далі підміняла ОБРАНЕ гравцем ім'я
            // ("Оксана") заглушкою-підказкою поля вводу "char.protagonist.m/
            // .f" ("Провідниця"/"Провідник") у кожному бою.
            string protagonistName = TryResolveProtagonistDisplayName(BareUnitId(unit.Id));
            if (protagonistName != null) return protagonistName;

            string key = ResolveNameKey(unit);
            bool female = IsFemaleCompanion(unit.Id);

            if (key != null && UkrainianText.Has(key, female)) return UkrainianText.Get(key, female);
            if (!string.IsNullOrEmpty(unit.DisplayNameKey) && UkrainianText.Has(unit.DisplayNameKey, female))
                return UkrainianText.Get(unit.DisplayNameKey, female);

            // Player-side юніт без ключа таблиці (Тренувальний бій, §2 рядок 32:
            // Core.DefaultCombatContent.Training() дає id "trainee_1"/"trainee_2"
            // без "u_"-префіксу й DisplayNameKey = вже готовий український текст
            // "Провідник"/"Максим", не ключ) — показуємо це ім'я як є, а не
            // позначкою [ключ]: гравець ніколи не бачить сирий маркер там, де
            // текст уже український і готовий до показу.
            if (string.Equals(unit.Side, "Player", StringComparison.Ordinal) && !string.IsNullOrEmpty(unit.DisplayNameKey))
                return unit.DisplayNameKey;

            return UkrainianText.MissingMarker(key ?? unit.DisplayNameKey ?? unit.Id);
        }

        /// <summary>Див. пункт (1) у зведенні розривів у шапці файлу.</summary>
        private static string ResolveNameKey(BattleUnitView unit)
        {
            string id = unit.Id ?? string.Empty;
            if (id.StartsWith("u_", StringComparison.Ordinal)) return "char." + id.Substring(2);
            if (id.StartsWith("defector_", StringComparison.Ordinal)) return "char." + id.Substring(9);

            // Сторона, не лише префікс id, вирішує «ворог це чи ні» — інакше
            // player-side юніт з несподіваним id (Тренувальний бій:
            // "trainee_1"/"trainee_2", без "u_") хибно йде в "enemy."-ключ,
            // якого в таблиці нема, і падає на MissingMarker. Тут повертаємо
            // null — ResolveDisplayNameInternal сам впаде на DisplayNameKey
            // напряму для Player-сторони.
            if (!string.Equals(unit.Side, "Player", StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(unit.DisplayNameKey))
                return "enemy." + unit.DisplayNameKey;

            return null;
        }

        /// <summary>Префікс "u_"/"defector_" знятий — той самий "голий" id, яким таблиця й ростер знають персонажа.</summary>
        private static string BareUnitId(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return unitId;
            if (unitId.StartsWith("u_", StringComparison.Ordinal)) return unitId.Substring(2);
            if (unitId.StartsWith("defector_", StringComparison.Ordinal)) return unitId.Substring(9);
            return unitId;
        }

        /// <summary>Див. пункт (2) у зведенні розривів у шапці файлу — тепер зважає на справжній рід протагоніста.</summary>
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
            // Ховаємо і сам 3D-хаб (див. коментар на HubRootName) — інакше
            // жителі на постах лишаються в кадрі арени, накладеної на ту
            // саму систему координат.
            if (_hubRoot != null) _hubRoot.SetActive(false);
            if (ArenaCamera != null) ArenaCamera.gameObject.SetActive(true);
        }

        private void RestoreHubCamera()
        {
            if (ArenaCamera != null) ArenaCamera.gameObject.SetActive(false);
            if (_hubCamera != null) _hubCamera.gameObject.SetActive(true);
            if (_hubRoot != null) _hubRoot.SetActive(true);
        }

        /// <summary>
        /// Фікс-ревью (major, знайдено тур-автоплеєм): камера кадрувала ввесь
        /// грід по центру ВСЬОГО екрана, не рахуючи ліву панель HUD
        /// (<see cref="BattleHudScreen"/>, ~34% ширини) — перші 1-2 колонки
        /// грида (і підписи юнітів на них, напр. "Мирослава"/"Максим Беркут")
        /// опинялись під панеллю, обрізані. Камера — ортографічна згори
        /// (<c>GameSceneBuilder.BuildArenaCamera</c>, поворот 90° по X): її
        /// світовий X напряму мапиться на горизонталь екрана, тож зсуваємо
        /// центр кадру вправо (камеру — вліво) рівно на стільки, щоб лівий
        /// край грида (світовий X=0) опинявся не під панеллю, а одразу за нею.
        /// </summary>
        private void FrameCamera(BattleView view)
        {
            if (ArenaCamera == null || view?.Grid == null) return;
            var frame = BattleArenaView.FrameGrid(view.Grid.Width, view.Grid.Height);

            float shiftX = HudPanelShiftWorldX(frame);
            ArenaCamera.transform.position = new Vector3(frame.CenterX - shiftX, ArenaCamera.transform.position.y, frame.CenterZ);
            if (ArenaCamera.orthographic) ArenaCamera.orthographicSize = frame.OrthographicSize;
        }

        private static float HudPanelShiftWorldX(CameraFrame frame)
        {
            if (Screen.width <= 0 || Screen.height <= 0) return 0f;

            float aspect = (float)Screen.width / Screen.height;
            float halfWorldWidth = frame.OrthographicSize * aspect;
            float worldPerPixel = (halfWorldWidth * 2f) / Screen.width;
            if (worldPerPixel <= 0f) return 0f;

            // Де на екрані сьогодні опиняється лівий край грида (світовий X=0),
            // за формулою кадру ДО зсуву.
            float gridLeftEdgeScreenX = (halfWorldWidth - frame.CenterX) / worldPerPixel;
            float targetLeftEdgeScreenX = BattleHudScreen.PanelRightEdgePixels() + 24f; // трохи запасу, щоб колонка не впиралась прямо в рамку

            float shiftWorld = 0f;
            if (gridLeftEdgeScreenX < targetLeftEdgeScreenX)
                shiftWorld = (targetLeftEdgeScreenX - gridLeftEdgeScreenX) * worldPerPixel;

            // Не даємо зсуву виштовхнути правий край грида за екран (вузькі
            // грiди на широких екранах мають достатньо запасу, але захист
            // лишається явним, а не «зазвичай працює»).
            float maxShift = halfWorldWidth * 0.7f;
            return shiftWorld > maxShift ? maxShift : shiftWorld;
        }

        private void TeardownAndDeactivate()
        {
            _active = false;
            _resultPending = false;
            _armed = ArmedAction.None;
            _session = null;

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
