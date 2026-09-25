using System.Collections.Generic;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Settlement;
using Game.Core.Stats;
using Game.Core.World;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Село живе добами: конвеєр дня крутиться прямо в сцені, і все, що він
    /// видає, видно очима — світло змінюється з днем і ніччю, жителі розходяться
    /// по постах і йдуть спати, над місцем події зводиться мітка, а стрічка
    /// зліва пише, що сталося і що чутно.
    ///
    /// ЩО ТУТ НЕ МОЖНА. Компонент не знає жодного числа прихованих шкал:
    /// Напруга, заряди накопичувачів і пороги в ядрі internal (інваріант 3).
    /// Назовні приходять фаза, доба, сигнали з ключами й тегами, наслідки
    /// інцидентів і мудборд — з них і збирається картинка. Дашборд загрози
    /// зібрати фізично нема з чого, і це навмисно.
    ///
    /// Розділення праці: ЩО показати рахує <see cref="VillageView"/> (чистий
    /// C#, під тестами), а цей компонент лише застосовує — рухає світло,
    /// ховає жителів, ставить мітки.
    /// </summary>
    public sealed class VillageLife : MonoBehaviour
    {
        [Header("Темп")]
        [Tooltip("Сколько секунд идёт одна фаза: день, потом ночь.")]
        [Min(0.1f)] public float secondsPerPhase = 2.5f;
        public bool runOnStart = true;
        [Tooltip("Ночью патрулировать вместо сна: узнаёшь больше, но никто не отдыхает.")]
        public bool patrolAtNight;
        [Range(1, 4)] public int tier = 1;

        [Header("Хозяйство (Поправка №6)")]
        [Tooltip("Рачительный хозяин сам строит, зовёт облаву и принимает людей.\n" +
                 "Выключи — и увидишь, как город живёт без заботы.")]
        public bool autoSteward = true;
        [Min(0)] public int startGold = 150;
        [Tooltip("Материалы город не производит: стартовый запас — то, что община\n" +
                 "принесла с собой. Дальше — только вылазки.")]
        [Min(0)] public int startMaterials = 14;
        [Min(0)] public int startFood = 120;

        [Header("Сцена")]
        public Light sun;
        public Camera view;
        public Transform postsRoot;
        public Transform villagersRoot;
        public Transform plotsRoot;
        public TextMesh headline;
        public TextMesh log;

        [Header("Лента")]
        [Min(1)] public int logLines = 9;

        private SettlementCycle _cycle;
        private CityWorks _works;
        private readonly Steward _steward = new Steward();
        private BalanceConfig _balance;
        private BaseState _base;
        private Roster _roster;
        private DayPhase _next = DayPhase.Day;
        private float _timer;
        private readonly List<string> _lines = new List<string>();
        private readonly List<GameObject> _marks = new List<GameObject>();

        /// <summary>Пости за ідентифікатором: якорі в сцені, до них прив'язані жителі й мітки.</summary>
        private readonly Dictionary<string, Transform> _posts = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Transform> _villagers = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Transform> _plots = new Dictionary<string, Transform>();

        public DayReport Last { get; private set; }

        private void Start()
        {
            if (runOnStart) Initialize();
        }

        private void Update()
        {
            if (_cycle == null) return;

            _timer += Time.deltaTime;
            if (_timer < secondsPerPhase) return;

            _timer = 0f;
            AdvancePhase();
        }

        /// <summary>
        /// Збирає поселення. Окремо від Start, тому що редактор ганяє
        /// доби без режиму гри — так знімається плівка діб.
        /// </summary>
        public void Initialize()
        {
            BindScene();

            var balance = new BalanceConfig();
            _balance = balance;

            _roster = BuildRoster();
            _base = new BaseState(_roster, new Game.Core.Economy.ResourceLedger(), balance);

            foreach (var slot in DefaultContent.AllSlots())
                _base.AddSlot(slot);

            // Хутір зустрічає тим, що в громади вже є; решта будується,
            // і пост без своєї будівлі закритий (Поправка №6.1).
            _works = new CityWorks(DefaultBuildings.StartingSet);
            _works.ApplyToSlots(_base);

            _base.Resources.Add(Game.Core.Economy.ResourceType.Gold, startGold);
            _base.Resources.Add(Game.Core.Economy.ResourceType.Materials, startMaterials);
            _base.Resources.Add(Game.Core.Economy.ResourceType.Food, startFood);

            // Людей менше, ніж постів — так і задумано (Поправка №5.1):
            // розстановка стає рішенням, а не формальністю.
            foreach (var pair in PostByVillager)
                _base.TryAssign(pair.Key, pair.Value);

            var adapter = new RosterAdapter(_roster, "hero", balance);

            var pulse = new WorldPulse(balance.Pulse);
            foreach (var source in DefaultPressureSources.All()) pulse.AddSource(source);

            var production = new ProductionStep(_base);
            var steps = new List<IDayStep>(SettlementCycle.BuildSteps(production))
            {
                new CityWorksStep(_works, _base),
                new PopulationStep(_works)
            };
            var processor = new DayProcessor(new TensionState(balance.Tension), balance, steps)
            {
                CityState = _works,
                Tier = tier,
                Roster = adapter,
                Casualties = adapter,
                Population = new PopulationState(80),   // хутір: до ста людей (Поправка №5.0)
                Pulse = pulse,
                Incidents = DefaultIncidents.BuildTable(),
                Repeats = new RepeatTracker(),
                IsPatrolling = patrolAtNight,
                PostDomains = new[]
                {
                    new PostDomain("storehouse_dock", "склад", SkillKeys.Survival, 5),
                    new PostDomain("settlement_market", "рынок", SkillKeys.Trade, 5),
                    new PostDomain("infirmary_bed", "лазарет", SkillKeys.Medicine, 5),
                    new PostDomain("council_seat", "совет", SkillKeys.Persuade, 5)
                }
            };

            _cycle = new SettlementCycle(_base, processor, production);
            _next = DayPhase.Day;
            _timer = 0f;
            _lines.Clear();

            Say(VillageView.OpeningLine(_roster.All.Count));
            Apply(null, DayPhase.Day);
        }

        /// <summary>Одна фаза: доба йде, картинка наздоганяє.</summary>
        public DayReport AdvancePhase()
        {
            if (_cycle == null) Initialize();

            var phase = _next;

            // Хазяїн вирішує перед днем: вночі стройка і рада не працюють.
            if (autoSteward && phase == DayPhase.Day)
                SayOrders(_steward.Act(_works, _base, _cycle.Processor, _balance));

            var report = _cycle.AdvanceDay(phase);
            _next = phase == DayPhase.Day ? DayPhase.Night : DayPhase.Day;

            Last = report;

            foreach (var line in VillageView.Lines(report)) Say(line);
            Apply(report, phase);

            return report;
        }

        // ================= застосування =================

        private void Apply(DayReport report, DayPhase phase)
        {
            var mood = report != null && report.Signals != null
                ? report.Signals.Moodboard
                : new Game.Core.Signals.MoodboardState(0, 0, null);   // до перших діб — хутір

            var sunPose = VillageView.SunFor(phase);
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(sunPose.Pitch, sunPose.Yaw, 0f);
                sun.intensity = sunPose.Intensity;
                sun.color = new Color(sunPose.R, sunPose.G, sunPose.B);
            }

            var ambient = VillageView.AmbientFor(phase, mood);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(ambient.SkyR, ambient.SkyG, ambient.SkyB);
            RenderSettings.ambientEquatorColor = new Color(
                (ambient.SkyR + ambient.GroundR) * 0.5f,
                (ambient.SkyG + ambient.GroundG) * 0.5f,
                (ambient.SkyB + ambient.GroundB) * 0.5f);
            RenderSettings.ambientGroundColor = new Color(ambient.GroundR, ambient.GroundG, ambient.GroundB);

            if (view != null)
                view.backgroundColor = new Color(ambient.BackR, ambient.BackG, ambient.BackB);

            ShowVillagers(phase);
            ShowMarks(report);
            ShowPlots();

            if (headline != null)
                headline.text = report != null
                    ? VillageView.Headline(report, mood)
                    : VillageView.OpeningHeadline(mood);

            if (log != null) log.text = string.Join("\n", _lines.ToArray());
        }

        /// <summary>
        /// Вночі село пустіє: жителі по домівках. Патруль — єдина причина
        /// лишитися на вулиці, і це видно без жодного підпису.
        /// </summary>
        private void ShowVillagers(DayPhase phase)
        {
            bool outside = phase == DayPhase.Day || patrolAtNight;

            foreach (var pair in _villagers)
            {
                var villager = pair.Value;
                if (villager == null) continue;

                villager.gameObject.SetActive(outside && Staffed(pair.Key));
            }
        }

        /// <summary>
        /// Фігура стоїть на посту, лише якщо там справді хтось є: пост
        /// відкритий, зайнятий і той, хто зайняв, живий. Порожній лазарет виглядає порожнім.
        /// </summary>
        private bool Staffed(string postId)
        {
            if (_base == null) return true;

            var slot = _base.GetSlot(postId);
            if (slot == null || !slot.Unlocked || !slot.IsOccupied) return false;

            var companion = _roster.Get(slot.AssignedCompanionId);
            return companion != null && !companion.IsDead;
        }

        /// <summary>
        /// Стройка видна очима: п'ять стадій (US-7.3). Ліси ростуть знизу вгору,
        /// готова будівля встає в повний зріст і отримує підпис. Стадія —
        /// чиста функція минулих діб з ядра, окремого геймплею нема.
        /// </summary>
        private void ShowPlots()
        {
            if (_works == null) return;

            foreach (var pair in _plots)
            {
                int stage = _works.StageOf(pair.Key);
                var model = pair.Value.Find("model");
                var label = pair.Value.Find("label");

                if (model != null)
                {
                    model.gameObject.SetActive(stage > 0);
                    float height = stage >= 5 ? 1f : Mathf.Max(0.15f, stage / 5f);
                    model.localScale = new Vector3(1f, height, 1f);
                }

                if (label != null) label.gameObject.SetActive(stage >= 5);
            }
        }

        /// <summary>Мітка над місцем події: колір — полоса наслідку.</summary>
        private void ShowMarks(DayReport report)
        {
            for (int i = 0; i < _marks.Count; i++)
                if (_marks[i] != null) DestroyImmediate(_marks[i]);
            _marks.Clear();

            if (report == null) return;

            for (int i = 0; i < report.Incidents.Count; i++)
            {
                var outcome = report.Incidents[i];

                Transform anchor;
                if (string.IsNullOrEmpty(outcome.DomainTag)) continue;
                if (!TryFindAnchorByDomain(outcome.DomainTag, out anchor)) continue;

                var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mark.name = "Происшествие: " + outcome.IncidentId;
                mark.transform.SetParent(transform, false);
                mark.transform.position = anchor.position + new Vector3(0f, 2.2f, 0f);
                mark.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
                mark.transform.rotation = Quaternion.Euler(45f, 45f, 0f);

                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null)
                {
                    var material = new Material(lit);
                    material.SetColor("_BaseColor", MarkColor(outcome));
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", MarkColor(outcome) * 1.6f);
                    mark.GetComponent<Renderer>().sharedMaterial = material;
                }

                _marks.Add(mark);
            }
        }

        private static Color MarkColor(Game.Core.World.IncidentOutcome outcome)
        {
            if (outcome.WasCrisis) return new Color(0.85f, 0.10f, 0.10f);
            switch (outcome.Band)
            {
                case OutcomeBand.Best:
                case OutcomeBand.Good: return new Color(0.35f, 0.75f, 0.35f);
                case OutcomeBand.Base: return new Color(0.90f, 0.70f, 0.20f);
                default: return new Color(0.85f, 0.30f, 0.15f);
            }
        }

        private bool TryFindAnchorByDomain(string domain, out Transform anchor)
        {
            // Домен приходить словом («склад», «рынок»), пост — ідентифікатором.
            // Зв'язок тримаємо тут: ядру про сцену знати нема чого.
            string postId;
            switch (domain)
            {
                case "склад": postId = "storehouse_dock"; break;
                case "рынок": postId = "settlement_market"; break;
                case "припасы": postId = "settlement_farms"; break;
                case "лазарет": postId = "infirmary_bed"; break;
                case "площадь": postId = "council_seat"; break;
                case "окраина": postId = "scouting_post"; break;
                case "ночь": postId = "storehouse_dock"; break;
                default: postId = "council_seat"; break;
            }
            return _posts.TryGetValue(postId, out anchor);
        }

        /// <summary>
        /// Що замовив хазяїн — словами, щоб у стрічці було видно, чому місто
        /// змінюється. Слова рахує <see cref="VillageView.OrderLines"/> за
        /// ключами таблиці (R7): тут ні літералів, ні DisplayName з ядра.
        /// </summary>
        private void SayOrders(string did)
        {
            foreach (var line in VillageView.OrderLines(did)) Say(line);
        }

        private void Say(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            // Повтор поспіль не пишемо: «Тихо. Тихо. Тихо» — це шпалери, а не стрічка.
            if (_lines.Count > 0 && _lines[_lines.Count - 1] == line) return;

            _lines.Add(line);
            while (_lines.Count > logLines) _lines.RemoveAt(0);
        }

        // ================= сцена і ростер =================

        /// <summary>Пости й жителі шукаються за іменами: сцена зібрана збирачем.</summary>
        private void BindScene()
        {
            _posts.Clear();
            _villagers.Clear();

            if (postsRoot != null)
                foreach (Transform child in postsRoot)
                    if (child.name.StartsWith("post:"))
                        _posts[child.name.Substring(5)] = child;

            if (villagersRoot != null)
                foreach (Transform child in villagersRoot)
                    if (child.name.StartsWith("villager:"))
                        _villagers[child.name.Substring(9)] = child;

            _plots.Clear();
            if (plotsRoot != null)
                foreach (Transform child in plotsRoot)
                    if (child.name.StartsWith("plot:"))
                        _plots[child.name.Substring(5)] = child;
        }

        /// <summary>
        /// Шість людей на сім постів. Імена поки службові: іменний ростер
        /// прийде з карткою персонажа (Поправка №5.2).
        /// </summary>
        private static readonly Dictionary<string, string> PostByVillager = new Dictionary<string, string>
        {
            { "hero", "council_seat" },
            { "guard", "storehouse_dock" },
            { "trader", "settlement_market" },
            { "medic", "infirmary_bed" },
            { "farmer", "settlement_farms" },
            { "scout", "scouting_post" }
        };

        private static Roster BuildRoster()
        {
            var roster = new Roster();
            roster.Add(Make("hero", "Лидер", SkillType.Persuade, 8));
            roster.Add(Make("guard", "Дозорный", SkillType.Survival, 7));
            roster.Add(Make("trader", "Меняла", SkillType.Trade, 7));
            roster.Add(Make("medic", "Лекарь", SkillType.Medicine, 7));
            roster.Add(Make("farmer", "Хозяин", SkillType.Survival, 6));
            roster.Add(Make("scout", "Ходок", SkillType.Survival, 6));
            return roster;
        }

        private static Companion Make(string id, string name, SkillType skill, int value)
        {
            var archetype = new CompanionArchetype(id, name)
                .SetSkill(skill, value)
                .SetSkill(SkillType.Persuade, value - 2)
                .SetSkill(SkillType.Intimidate, value - 3);
            return archetype.CreateInstance(id);
        }
    }
}
