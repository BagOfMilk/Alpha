using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Signals;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>Одна доба/фаза словами, а не типами ядра — саме це годуватиме компонент нижче.</summary>
    public enum StagePhase { Day, Night }

    /// <summary>Хто (якщо хто) стоїть на посту цього кадру. Порожній/null <see cref="OccupantId"/> — пост порожній.</summary>
    public sealed class StagePost
    {
        public string PostId;
        public string OccupantId;
    }

    /// <summary>Стадія стройки одного здання, 0..5 (US-7.3): 0 — ще не почато, 5 — готово.</summary>
    public sealed class StagePlot
    {
        public string PlotId;
        public int Stage;
    }

    /// <summary>Місце події: домен (словом, як у сигналах) + полоса виходу, для мітки й кольору.</summary>
    public sealed class StageIncidentMark
    {
        public string DomainTag;
        public OutcomeBand Band;
        public bool WasCrisis;
    }

    /// <summary>
    /// Чисті дані одного кадру села — жодного типу ядра, крім двох
    /// маленьких: <see cref="OutcomeBand"/> (та сама лєстниця скрізь, R17) і
    /// сама структура тут. <see cref="GameShell"/> (коли підключить фасад)
    /// заповнює це з view-шарів <c>GameSession</c> щоразу, коли картинка
    /// має оновитись — <see cref="VillageStage"/> нижче нічого не знає про
    /// те, ЗВІДКИ ці дані взялися.
    /// </summary>
    public sealed class VillageStageData
    {
        public StagePhase Phase;
        public int Tier;
        public bool Patrolling;
        public IReadOnlyList<StagePost> Posts;
        public IReadOnlyList<StagePlot> Plots;
        public IReadOnlyList<StageIncidentMark> Incidents;
    }

    /// <summary>
    /// Застосовує картинку села з готових даних: жодної симуляції всередині
    /// (це вся різниця з <see cref="VillageLife"/> — той сам крутить
    /// <c>SettlementCycle</c>, цей лише малює те, що йому дали). Коли
    /// <c>GameSession</c> приїде з трунку, <c>GameShell</c> буде брати з
    /// нього <c>VillageView</c>/<c>RosterView</c>/<c>CityView</c> щоранку-
    /// щовечора, збирати <see cref="VillageStageData"/> і передавати сюди —
    /// а сьогодні цей компонент можна прогнати вручну (Inspector/тест) із
    /// довільним знімком даних.
    ///
    /// Світло й заливний колір — та сама математика <see cref="VillageView"/>,
    /// що й у <see cref="VillageLife"/> (одне джерело істини «як виглядає
    /// доба»); якорі — та сама конвенція імен («post:&lt;id&gt;»,
    /// «villager:&lt;id&gt;», «plot:&lt;id&gt;»), тому сцену, зібрану
    /// <c>GameSceneBuilder</c>-ом (KitBuilder, як і в «Селі на перевалі»),
    /// можна годувати без жодної переробки якорів.
    /// </summary>
    public sealed class VillageStage : MonoBehaviour
    {
        [Header("Сцена")]
        public Light sun;
        public Camera view;
        public Transform postsRoot;
        public Transform villagersRoot;
        public Transform plotsRoot;

        private readonly Dictionary<string, Transform> _posts = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Transform> _villagers = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Transform> _plots = new Dictionary<string, Transform>();
        private readonly List<GameObject> _marks = new List<GameObject>();

        public VillageStageData Last { get; private set; }

        private void Awake()
        {
            Bind();
        }

        /// <summary>Пости/жителі/ділянки шукаються за іменами — та ж угода, що й у <see cref="VillageLife.BindScene"/>.</summary>
        public void Bind()
        {
            _posts.Clear();
            if (postsRoot != null)
                foreach (Transform child in postsRoot)
                    if (child.name.StartsWith("post:"))
                        _posts[child.name.Substring(5)] = child;

            _villagers.Clear();
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

        /// <summary>Один кадр: світло, жителі на постах, стадії стройки, мітки подій — усе з готових даних.</summary>
        public void Apply(VillageStageData data)
        {
            if (data == null) return;
            Last = data;

            if (_posts.Count == 0 && postsRoot != null) Bind();

            var phase = data.Phase == StagePhase.Night ? Game.Core.Loop.DayPhase.Night : Game.Core.Loop.DayPhase.Day;
            // Декей поки завжди 0: без запущеної симуляції даних про занепад
            // нема (GameSession додасть мудборд пізніше) — і це чесніше, ніж
            // вигадувати число.
            var mood = new MoodboardState(Mathf01(data.Tier - 1), 0, null);

            ApplyLight(phase);
            ApplyAmbient(phase, mood);
            ShowVillagers(phase, data);
            ShowPlots(data.Plots);
            ShowMarks(data.Incidents);
        }

        private void ApplyLight(Game.Core.Loop.DayPhase phase)
        {
            var pose = VillageView.SunFor(phase);
            if (sun == null) return;

            sun.transform.rotation = Quaternion.Euler(pose.Pitch, pose.Yaw, 0f);
            sun.intensity = pose.Intensity;
            sun.color = new Color(pose.R, pose.G, pose.B);
        }

        private void ApplyAmbient(Game.Core.Loop.DayPhase phase, MoodboardState mood)
        {
            var ambient = VillageView.AmbientFor(phase, mood);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(ambient.SkyR, ambient.SkyG, ambient.SkyB);
            RenderSettings.ambientEquatorColor = new Color(
                (ambient.SkyR + ambient.GroundR) * 0.5f,
                (ambient.SkyG + ambient.GroundG) * 0.5f,
                (ambient.SkyB + ambient.GroundB) * 0.5f);
            RenderSettings.ambientGroundColor = new Color(ambient.GroundR, ambient.GroundG, ambient.GroundB);

            if (view != null) view.backgroundColor = new Color(ambient.BackR, ambient.BackG, ambient.BackB);
        }

        /// <summary>
        /// Вночі село пустіє: патруль — єдина причина лишитись, як і в
        /// <see cref="VillageLife"/>. Фигура стоїть на посту, лише якщо
        /// <see cref="StagePost.OccupantId"/> непорожній.
        /// </summary>
        private void ShowVillagers(Game.Core.Loop.DayPhase phase, VillageStageData data)
        {
            bool outside = phase == Game.Core.Loop.DayPhase.Day || data.Patrolling;

            var occupied = new HashSet<string>();
            if (data.Posts != null)
                for (int i = 0; i < data.Posts.Count; i++)
                {
                    var post = data.Posts[i];
                    if (post != null && !string.IsNullOrEmpty(post.OccupantId)) occupied.Add(post.PostId);
                }

            foreach (var pair in _villagers)
            {
                if (pair.Value == null) continue;
                pair.Value.gameObject.SetActive(outside && occupied.Contains(pair.Key));
            }
        }

        /// <summary>Ліси ростуть знизу вгору (US-7.3): та сама формула висоти, що в <see cref="VillageLife.ShowPlots"/>.</summary>
        private void ShowPlots(IReadOnlyList<StagePlot> plots)
        {
            if (plots == null) return;

            for (int i = 0; i < plots.Count; i++)
            {
                var plot = plots[i];
                Transform anchor;
                if (plot == null || !_plots.TryGetValue(plot.PlotId, out anchor) || anchor == null) continue;

                var model = anchor.Find("model");
                var label = anchor.Find("label");

                if (model != null)
                {
                    model.gameObject.SetActive(plot.Stage > 0);
                    float height = plot.Stage >= 5 ? 1f : Mathf.Max(0.15f, plot.Stage / 5f);
                    model.localScale = new Vector3(1f, height, 1f);
                }

                if (label != null) label.gameObject.SetActive(plot.Stage >= 5);
            }
        }

        /// <summary>Мітка над місцем події: колір — полоса виходу, та ж угода кольорів, що в <see cref="VillageLife.MarkColor"/>.</summary>
        private void ShowMarks(IReadOnlyList<StageIncidentMark> incidents)
        {
            for (int i = 0; i < _marks.Count; i++)
                if (_marks[i] != null) DestroyImmediate(_marks[i]);
            _marks.Clear();

            if (incidents == null) return;

            for (int i = 0; i < incidents.Count; i++)
            {
                var mark = incidents[i];
                Transform anchor;
                if (mark == null || string.IsNullOrEmpty(mark.DomainTag) || !TryFindAnchorByDomain(mark.DomainTag, out anchor))
                    continue;

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Подія: " + mark.DomainTag;
                go.transform.SetParent(transform, false);
                go.transform.position = anchor.position + new Vector3(0f, 2.2f, 0f);
                go.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
                go.transform.rotation = Quaternion.Euler(45f, 45f, 0f);

                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null)
                {
                    var material = new Material(lit);
                    var color = MarkColor(mark);
                    material.SetColor("_BaseColor", color);
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", color * 1.6f);
                    go.GetComponent<Renderer>().sharedMaterial = material;
                }

                _marks.Add(go);
            }
        }

        private static Color MarkColor(StageIncidentMark mark)
        {
            if (mark.WasCrisis) return new Color(0.85f, 0.10f, 0.10f);
            switch (mark.Band)
            {
                case OutcomeBand.Best:
                case OutcomeBand.Good: return new Color(0.35f, 0.75f, 0.35f);
                case OutcomeBand.Base: return new Color(0.90f, 0.70f, 0.20f);
                default: return new Color(0.85f, 0.30f, 0.15f);
            }
        }

        /// <summary>
        /// Домен (слово, як у сигналах) → якір посту. Успадковано з
        /// <see cref="VillageLife.TryFindAnchorByDomain"/> буквально — той
        /// самий словник слів, бо це і є домени, які сьогодні віддає ядро;
        /// «майстерня» — припущення (workshop_bench не мав свого домену в
        /// жодному наявному коді на момент написання, див. openIssues E1).
        /// </summary>
        private bool TryFindAnchorByDomain(string domain, out Transform anchor)
        {
            string postId;
            switch (domain)
            {
                case "склад": postId = "storehouse_dock"; break;
                case "рынок": postId = "settlement_market"; break;
                case "припасы": postId = "settlement_farms"; break;
                case "лазарет": postId = "infirmary_bed"; break;
                case "площадь": postId = "council_seat"; break;
                case "окраина": postId = "scouting_post"; break;
                case "мастерская": postId = "workshop_bench"; break;
                case "ночь": postId = "storehouse_dock"; break;
                default: postId = "council_seat"; break;
            }
            return _posts.TryGetValue(postId, out anchor);
        }

        private static int Mathf01(int value)
        {
            return value < 0 ? 0 : value;
        }
    }
}
