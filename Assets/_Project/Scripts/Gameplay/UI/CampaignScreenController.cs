using System.Collections.Generic;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Companions;
using Game.Core.Council;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Items;
using Game.Core.Quests;
using Game.Core.Saves;
using Game.Core.Stats;
using Game.Core.Story;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Кампанийный экран (итерация 16): меню → point-buy создания → город
    /// (ростер/позиции/совет/стройка/«ждать») → карта мира → вылазка (бой в
    /// Battle-сцене через GameFlow) → отчёт возвращения. Ведомый онбординг
    /// подсказывает шаг (US-17.4); скрытые шкалы — полосами и амбиент-репликами
    /// (US-17.2). Автосейв на ходе времени и отправке вылазки; 3 слота + бэкап.
    /// Сцена: Alpha → Create Campaign Scene.
    /// </summary>
    public sealed class CampaignScreenController : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — дефолтный баланс из кода.")]
        public BalanceConfigAsset balanceAsset;

        /// <summary>Кампания сессии (для тестов/оркестрации).</summary>
        public Campaign Campaign => GameFlow.Campaign;

        private BalanceConfig _cfg;
        private OnboardingFlow _onboarding = new OnboardingFlow();
        private ProtagonistBuilder _builder;
        private string _pickedBackgroundId = "leader";
        private WorldMap _worldMap;
        private WorldNode _pickedNode;
        private string _openCharacterId; // открытая карточка бойца (панель персонажа)
        private string _diplomacyTarget; // цель действия совета «Дипломатия» (US-8.4)

        /// <summary>Хроника города живёт в GameFlow — переживает Battle-сцену (US-17.2).</summary>
        private const int ChronicleLimit = 12;
        private static List<string> Chronicle => GameFlow.Chronicle;
        private readonly HashSet<string> _squadPicks = new HashSet<string>();

        private VisualElement _root;
        private readonly Dictionary<string, VisualElement> _panels = new Dictionary<string, VisualElement>();

        /// <summary>Спавны врагов на арене (правый край) — общие для вылазок и квестовых боёв.</summary>
        private static readonly GridPos[] EnemySpawns =
        {
            new GridPos(10, 2), new GridPos(11, 3), new GridPos(10, 4),
            new GridPos(11, 5), new GridPos(10, 6), new GridPos(11, 1),
            new GridPos(10, 0), new GridPos(11, 7),
            // Резерв под волну штурма: без этих мест подкрепления орды (US-11.4)
            // считались, но на арену не попадали — давление было чисто бумажным.
            new GridPos(9, 1), new GridPos(9, 3), new GridPos(9, 5), new GridPos(9, 6)
        };

        private void Awake()
        {
            _cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();
            _worldMap = DefaultWorld.NewMap();
        }

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            foreach (var name in new[] { "panel-menu", "panel-create", "panel-city", "panel-map",
                                         "panel-report", "panel-quest", "panel-character" })
                _panels[name] = _root.Q<VisualElement>(name);

            // Контроллер пересоздаётся при каждом возврате из Battle-сцены, а кампания
            // живёт в статике GameFlow: берём ЕЁ конфиг. Иначе Awake-дефолт
            // (Ironman = false) расходится с кампанией и тихо выключает пермасмерть
            // протагониста в квестовых боях — айронмен переставал быть айронменом.
            if (GameFlow.Campaign != null) _cfg = GameFlow.Campaign.Cfg;

            BindMenu();
            BindCreate();
            BindCity();
            BindMap();
            BindReport();
            BindCharacter();

            // Контроллер пересоздаётся при каждом возврате из Battle-сцены:
            // онбординг фаст-форвардится из персистентных флагов, а не с пролога.
            if (GameFlow.Campaign != null) AdvanceOnboarding(GameFlow.Campaign, silent: true);

            if (GameFlow.LastReport != null) ShowReport();
            else if (GameFlow.Campaign != null && GameFlow.PendingQuest != null) ShowQuest();
            else if (GameFlow.Campaign != null) ShowCity();
            else Show("panel-menu");
        }

        /// <summary>
        /// Фаст-форвард онбординга + телеметрия перехода шага (воронка R5).
        /// silent — восстановление уже достигнутого шага из флагов (пересоздание
        /// контроллера/загрузка), НЕ реальный переход: событие не шлётся, иначе
        /// каждый возврат из Battle дублировал бы достижение шага в воронке.
        /// </summary>
        private void AdvanceOnboarding(Campaign c, bool silent = false)
        {
            var before = _onboarding.Step;
            while (_onboarding.TryAdvance(c)) { }
            if (!silent && _onboarding.Step != before)
                Telemetry.Event("onboarding_step", ("step", _onboarding.Step.ToString()));
        }

        private void Show(string panel)
        {
            foreach (var kv in _panels)
                kv.Value.EnableInClassList("panel--visible", kv.Key == panel);
        }

        // ================= МЕНЮ =================
        private void BindMenu()
        {
            var newGame = _root.Q<Button>("new-game-button");
            newGame.clicked -= OnNewGameClicked;
            newGame.clicked += OnNewGameClicked;

            var contBtn = _root.Q<Button>("continue-autosave-button");
            contBtn.clicked -= OnContinueAutosave;
            contBtn.clicked += OnContinueAutosave;
            contBtn.SetEnabled(SaveSerializer.Exists(SaveSerializer.AutosavePath));

            var slots = _root.Q<VisualElement>("slot-buttons");
            slots.Clear();
            for (int i = 1; i <= SaveSerializer.SlotCount; i++)
            {
                int slot = i;
                bool exists = SaveSerializer.Exists(SaveSerializer.SlotPath(slot));
                var b = new Button(() => LoadFrom(SaveSerializer.SlotPath(slot)))
                { text = UiText.SlotPrefix + slot + (exists ? "" : UiText.EmptySlot) };
                b.SetEnabled(exists);
                slots.Add(b);
            }

            var sandbox = _root.Q<Button>("sandbox-button");
            sandbox.clicked -= OnSandbox;
            sandbox.clicked += OnSandbox;
        }

        private void OnSandbox()
        {
            GameFlow.ClearBattle();
            SceneManager.LoadScene("Battle");
        }

        private void OnNewGameClicked()
        {
            _cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();
            _cfg.Ironman = _root.Q<Toggle>("ironman-toggle").value;
            _pickedBackgroundId = "leader";
            RebuildBuilder();
            Show("panel-create");
        }

        private void OnContinueAutosave() => LoadFrom(SaveSerializer.AutosavePath);

        private void LoadFrom(string path)
        {
            var data = SaveSerializer.LoadFromFile(path);
            if (data == null) return;
            var restored = SaveSystem.Restore(data, _cfg, ContentCatalog.Default());

            // Завершённую кампанию не продолжить: пермасмерть айронмена/победа
            // терминальны (US-16.2) — сейв не обходит их.
            if (restored.Outcome != CampaignOutcome.Ongoing)
            {
                SetMenuNote(restored.Outcome == CampaignOutcome.Won
                    ? "Эта кампания уже выиграна." : "Эта кампания окончена (game over).");
                Show("panel-menu");
                return;
            }

            GameFlow.Campaign = restored;
            GameFlow.ResetChronicle(); // лента принадлежит прогону, а не процессу
            GameFlow.LastReport = null; // отчёт прошлой вылазки к загруженной кампании не относится
            AttachCouncilIfMissing();

            _onboarding = new OnboardingFlow();
            AdvanceOnboarding(restored, silent: true); // фаст-форвард из флагов

            // Загрузки — часть телеметрии (сигнал save-scum: reload после потерь).
            // Сессия открывается ДО событий загрузки: Event при закрытой сессии
            // молча теряется, и «отступление» не попадало бы в JSONL вообще.
            if (!Telemetry.Active) Telemetry.Begin(restored);
            Telemetry.Event("save_loaded", ("file", System.IO.Path.GetFileName(path)));

            // Чекпойнт «бой не доигран»: решение идти уже принято, загрузка его не
            // отматывает — вылазка засчитывается отступлением. Отряд возвращается
            // потрёпанным: иначе выход из проигрышного боя был бы ДЕШЕВЛЕ честного
            // поражения (там Критические ранения всем и возможные смерти).
            if (data.expeditionUnresolved)
            {
                int hurt = 0;
                foreach (var dto in data.companions)
                {
                    if (dto.status != (int)CompanionStatus.InSquad) continue; // кто был в отряде
                    var comp = restored.Roster.Get(dto.id);
                    if (comp == null || !comp.IsAlive) continue;
                    comp.ApplyInjury(Game.Core.Health.InjuryTier.Serious, _cfg, null);
                    hurt++;
                }
                AddChronicle($"[{restored.Base.CurrentDay}] {UiText.ChronicleRetreated}");
                Telemetry.Event("expedition_retreat_on_load", ("wounded", hurt));
                AutoSave.Write(restored); // пометку снимаем: решение отыграно
            }

            // Сейв мог быть сделан ДО завершения пролога (чекпойнт создания или
            // автосейв после пролог-боя): пролог форсируется, а не выбирается с
            // доски — без авто-резюме он терялся бы навсегда, а онбординг клинил
            // на первом шаге (флаг prologue_done ставит только исход пролога).
            restored.Quests.CollectFrom(DefaultQuests.FullPool(),
                restored.Factions, restored.Flags, restored.Roster.All);
            if (!restored.Flags.Contains(DefaultQuests.PrologueDoneFlag)
                && restored.Quests.StatusOf("prologue") == QuestStatus.Available
                && GameFlow.PendingQuest == null)
            {
                BeginQuest("prologue"); // повтор с начала — семантика демоции v4
                return;
            }
            ShowCity();
        }

        private void SetMenuNote(string text)
        {
            var note = _root.Q<Label>("menu-note");
            if (note != null) note.text = text;
        }

        // ================= СОЗДАНИЕ (US-2.7) =================
        private void BindCreate()
        {
            var start = _root.Q<Button>("start-campaign-button");
            start.clicked -= OnStartCampaign;
            start.clicked += OnStartCampaign;
        }

        private void RebuildBuilder()
        {
            var bg = DefaultContent.AllBackgrounds().Find(b => b.Id == _pickedBackgroundId)
                     ?? DefaultContent.Leader();
            _builder = new ProtagonistBuilder(bg, _cfg);
            RefreshCreatePanel();
        }

        private void RefreshCreatePanel()
        {
            var list = _root.Q<VisualElement>("background-list");
            list.Clear();
            foreach (var bg in DefaultContent.AllBackgrounds())
            {
                string id = bg.Id;
                var b = new Button(() => { _pickedBackgroundId = id; RebuildBuilder(); }) { text = bg.DisplayName };
                b.AddToClassList("pick-btn");
                if (id == _pickedBackgroundId) b.AddToClassList("pick-btn--active");
                list.Add(b);
            }

            _root.Q<Label>("attr-budget").text = UiText.AttrPoints + _builder.AttributePointsRemaining;
            var attrRows = _root.Q<VisualElement>("attr-rows");
            attrRows.Clear();
            foreach (AttributeType a in System.Enum.GetValues(typeof(AttributeType)))
            {
                if (a == AttributeType.None) continue; // «пустой» атрибут — не строка выбора
                var attr = a;
                var row = new VisualElement();
                row.AddToClassList("stat-row");
                row.Add(new Label($"{UiText.AttributeName(attr)}: {_builder.Attribute(attr)}"));
                var plus = new Button(() => { _builder.RaiseAttribute(attr); RefreshCreatePanel(); }) { text = "+" };
                plus.SetEnabled(_builder.AttributePointsRemaining > 0
                    && _builder.Attribute(attr) < _cfg.CreationAttributeMax);
                row.Add(plus);
                attrRows.Add(row);
            }

            _root.Q<Label>("skill-budget").text = UiText.SkillPoints + _builder.SkillPointsRemaining;
            var skillRows = _root.Q<VisualElement>("skill-rows");
            skillRows.Clear();
            foreach (SkillType s in System.Enum.GetValues(typeof(SkillType)))
            {
                if (s == SkillType.None) continue;
                var skill = s;
                var row = new VisualElement();
                row.AddToClassList("stat-row");
                row.Add(new Label($"{UiText.SkillName(skill)}: {_builder.Skill(skill)}"));
                var plus = new Button(() => { _builder.RaiseSkill(skill); RefreshCreatePanel(); }) { text = "+" };
                plus.SetEnabled(_builder.SkillPointsRemaining > 0
                    && _builder.Skill(skill) < _cfg.CreationSkillMax);
                row.Add(plus);
                skillRows.Add(row);
            }
        }

        /// <summary>Старт кампании из point-buy (публично — для PlayMode-смоука).</summary>
        public void OnStartCampaign()
        {
            // Даблклик по «Начать кампанию»: первый клик уже создал кампанию и взял
            // пролог — второй пересоздал бы кампанию под чужой прогон квеста.
            if (GameFlow.PendingQuest != null) return;
            if (_builder == null) RebuildBuilder(); // прямой вызов без меню (тесты)
            var name = _root.Q<TextField>("leader-name").value;
            if (!string.IsNullOrEmpty(name)) _builder.DisplayName = name;
            var c = Campaign.NewGame(_cfg, _builder.Build("leader"));
            GameFlow.Campaign = c;
            GameFlow.ResetChronicle(); // лента прошлого прогона не протекает в новый
            AttachCouncilIfMissing();
            _onboarding = new OnboardingFlow();

            Telemetry.Begin(c);
            Telemetry.Event("campaign_start",
                ("ironman", c.Ironman), ("background", _pickedBackgroundId));

            // Чекпойнт «кампания создана» — до пролога (US-17.1: пролог не запирает).
            AutoSave.Write(c);

            // Пролог (US-17.1): реальный прогон квеста — бой на окраине → развилка.
            c.Quests.CollectFrom(DefaultQuests.FullPool(), c.Factions, c.Flags, c.Roster.All);
            BeginQuest("prologue");
        }

        private void AttachCouncilIfMissing()
        {
            var c = GameFlow.Campaign;
            if (c != null && c.Council == null)
                c.AttachCouncil(DefaultCouncil.NewCouncil(c.Factions, c.Base.Resources,
                    c.Base.ThreatsSystem, c.Base));
        }

        // ================= ГОРОД =================
        private void BindCity()
        {
            var wait = _root.Q<Button>("wait-button");
            wait.clicked -= OnWaitDay;
            wait.clicked += OnWaitDay;

            var map = _root.Q<Button>("map-button");
            map.clicked -= OnOpenMap;
            map.clicked += OnOpenMap;

            var saves = _root.Q<VisualElement>("save-slot-buttons");
            saves.Clear();
            for (int i = 1; i <= SaveSerializer.SlotCount; i++)
            {
                int slot = i;
                saves.Add(new Button(() => SaveToSlot(slot)) { text = UiText.SaveToSlot + slot });
            }

            var ack = _root.Q<Button>("intro-ack-button");
            if (ack != null)
            {
                ack.clicked -= OnIntroAck;
                ack.clicked += OnIntroAck;
            }

            var menu = _root.Q<Button>("menu-button");
            if (menu != null)
            {
                menu.clicked -= OnExitToMenu;
                menu.clicked += OnExitToMenu;
            }
        }

        /// <summary>
        /// Выход в главное меню из города: слоты сохранения были write-only —
        /// загрузить их можно только из меню, а пути туда не было (US-16.1).
        /// Сначала фиксируем состояние: стройка/совет/назначения автосейва не пишут.
        /// </summary>
        private void OnExitToMenu()
        {
            var c = GameFlow.Campaign;
            if (c == null) { Show("panel-menu"); return; }
            AutoSave.Write(c);
            Telemetry.Event("campaign_abandoned", ("day", c.Base.CurrentDay));
            Telemetry.End();

            GameFlow.Reset();
            _openCharacterId = null;
            BindMenu(); // пересобрать доступность слотов/автосейва
            SetMenuNote(UiText.SavedToAutosave);
            Show("panel-menu");
        }

        private void OnIntroAck()
        {
            var c = GameFlow.Campaign;
            if (c == null) return;
            _onboarding.AcknowledgeIntro(c);
            Telemetry.Event("onboarding_step", ("step", _onboarding.Step.ToString()));
            RefreshCity();
        }

        private void SaveToSlot(int slot)
        {
            var c = GameFlow.Campaign;
            if (c == null || !c.CanQuickSave) return; // гибрид/айронмен-гейт (US-16.1)
            SaveSerializer.SaveToFile(SaveSystem.Capture(c), SaveSerializer.SlotPath(slot));
            RefreshCity();
        }

        /// <summary>«Ждать день» (публично — для PlayMode-смоука): время + автосейв.</summary>
        public void OnWaitDay()
        {
            var c = GameFlow.Campaign;
            if (c == null || c.Outcome != CampaignOutcome.Ongoing) return;
            RecordCycle(c.AdvanceDays(1));

            // Ход времени опрашивает ростер на уход (US-9.2): боец на дне лояльности
            // уходит к врагу — и возвращается боссом на доске «Тот, кого бросили».
            var defector = c.TryDefectOnTimePass();
            if (defector != null)
            {
                var who = NameOfCompanion(c, defector.CompanionId);
                AddChronicle($"[{c.Base.CurrentDay}] {string.Format(UiText.ChronicleDefected, who)}");
                Telemetry.Event("companion_defected", ("companion", defector.CompanionId));
            }

            AdvanceOnboarding(c);
            Telemetry.Event("day_advanced",
                ("gold", c.Base.Resources.Get(ResourceType.Gold)),
                ("buildMat", c.Base.Resources.Get(ResourceType.BuildingMaterial)),
                ("population", (int)c.Base.Population),
                ("tier", c.Base.CityTier),
                ("tension", c.Base.ThreatsSystem != null ? c.Base.ThreatsSystem.Tension.Band.ToString() : "-"),
                ("readiness", c.Base.ThreatsSystem != null ? c.Base.ThreatsSystem.Readiness.Band.ToString() : "-"));
            AutoSave.Write(c);
            RefreshCity();
        }

        /// <summary>
        /// Хроника города (US-8.2/11.x): раньше CycleReport выбрасывался — инциденты,
        /// кризисы (вплоть до гибели напарника), выздоровления и достройки
        /// происходили молча, и стимул занимать позиции был нечитаем.
        /// </summary>
        private void RecordCycle(Game.Core.Base.CycleReport report)
        {
            if (report == null) return;
            var c = GameFlow.Campaign;

            foreach (var incident in report.Incidents)
            {
                string who = incident.ResolvedById != null
                    ? NameOfCompanion(c, incident.ResolvedById) : UiText.IncidentNobody;
                string line = $"[{report.ToDay}] {incident.DisplayName}: " +
                              (incident.Success ? UiText.IncidentResolved : UiText.IncidentFailed) + $" ({who})";
                if (incident.CrisisApplied != Game.Core.Threats.CrisisEffect.None)
                {
                    line += "\n    ⚠ " + UiText.CrisisName(incident.CrisisApplied);
                    if (incident.CrisisVictimId != null)
                    {
                        line += ": " + NameOfCompanion(c, incident.CrisisVictimId);
                        // Гибель дома отзывается так же, как гибель в бою (US-9.6).
                        foreach (var effect in c.NotifyDeath(incident.CrisisVictimId).Effects)
                            line += $"\n    {NameOfCompanion(c, effect.CompanionId)}: {effect.Note}";
                    }
                }
                AddChronicle(line);
                Telemetry.Incident(incident, "home");
            }

            foreach (var id in report.Recovered)
                AddChronicle($"[{report.ToDay}] {NameOfCompanion(c, id)}: {UiText.ChronicleRecovered}");
            foreach (var built in report.ConstructionCompleted)
                AddChronicle($"[{report.ToDay}] {UiText.ChronicleBuilt} {built}");
            ReportCityGrowth(report.CityTierAdvancedFrom, report.CityTierAdvancedTo, report.ToDay);
        }

        /// <summary>Каждый пройденный тир — отдельной строкой: рост 1→3 не должен
        /// выглядеть как один шаг ни в ленте, ни в воронке телеметрии.</summary>
        private static void ReportCityGrowth(int from, int to, int day)
        {
            if (to <= 0) return;
            int first = from > 0 ? from + 1 : to;
            for (int tier = first; tier <= to; tier++)
            {
                AddChronicle($"[{day}] {string.Format(UiText.ChronicleCityGrew, tier)}");
                Telemetry.Event("city_tier_up", ("tier", tier));
            }
        }

        private static string NameOfCompanion(Campaign c, string id)
        {
            var comp = c != null ? c.Roster.Get(id) : null;
            return comp != null ? comp.DisplayName : id;
        }

        private static void AddChronicle(string line)
        {
            Chronicle.Add(line);
            while (Chronicle.Count > ChronicleLimit) Chronicle.RemoveAt(0);
        }

        private void OnOpenMap()
        {
            var c = GameFlow.Campaign;
            if (c == null || c.Outcome != CampaignOutcome.Ongoing) return;
            _pickedNode = null;
            _squadPicks.Clear();
            RefreshMap();
            Show("panel-map");
        }

        private void ShowCity()
        {
            RefreshCity();
            Show("panel-city");
        }

        private void RefreshCity()
        {
            var c = GameFlow.Campaign;
            if (c == null) return;
            var b = c.Base;

            _root.Q<Label>("city-status").text =
                $"День {b.CurrentDay} · тир {b.CityTier} · население {(int)b.Population} · " +
                $"золото {b.Resources.Get(ResourceType.Gold)} · строймат {b.Resources.Get(ResourceType.BuildingMaterial)} · " +
                $"крафт {b.Resources.Get(ResourceType.CraftingMaterial)}" +
                (c.Ironman ? " · АЙРОНМЕН" : "");

            _root.Q<Label>("onboarding-hint").text = _onboarding.IsDone ? "" : "► " + _onboarding.Hint;

            // Знакомство с поселением (US-17.4): реальный шаг с подтверждением.
            var ack = _root.Q<Button>("intro-ack-button");
            if (ack != null)
                ack.style.display = _onboarding.Step == OnboardingStep.SettlementIntro
                    ? DisplayStyle.Flex : DisplayStyle.None;

            // Скрытые шкалы — полосами и репликами, без чисел (US-17.2).
            var band = b.ThreatsSystem != null ? b.ThreatsSystem.Tension.Band : Game.Core.Threats.TensionBand.Calm;
            var signals = DefaultContent.AmbientSignals(band);
            _root.Q<Label>("city-signals").text =
                $"Обстановка: {band}\n«{signals[0]}»\nГотовность: " +
                (b.ThreatsSystem != null ? b.ThreatsSystem.Readiness.Band.ToString() : "—");

            // Ростер.
            var roster = _root.Q<ScrollView>("roster-list");
            roster.Clear();
            foreach (var comp in c.Roster.All)
            {
                // Карточка кликабельна: экран бойца — там тратятся очки навыков (US-2.3).
                string compId = comp.Id;
                var card = new Button(() => ShowCharacter(compId)) { text = DescribeCompanion(comp) };
                card.AddToClassList("roster-card");
                roster.Add(card);
            }

            // Позиции: dropdown на слот (US-8.1).
            var slotList = _root.Q<VisualElement>("slot-list");
            slotList.Clear();
            foreach (var slot in b.Slots)
            {
                if (!slot.Unlocked) continue;
                // В списке — читаемые имена; id держим в карте label→id
                // (коллизия имён дизамбигуируется добавкой id).
                var choices = new List<string> { UiText.Unassigned };
                var idByLabel = new Dictionary<string, string>();
                foreach (var comp in c.Roster.All)
                {
                    if (!comp.IsAvailableForDuty && comp.Id != slot.AssignedCompanionId) continue;
                    string label = idByLabel.ContainsKey(comp.DisplayName)
                        ? $"{comp.DisplayName} ({comp.Id})" : comp.DisplayName;
                    idByLabel[label] = comp.Id;
                    choices.Add(label);
                }
                string current = UiText.Unassigned;
                if (slot.IsOccupied)
                    foreach (var kv in idByLabel)
                        if (kv.Value == slot.AssignedCompanionId) { current = kv.Key; break; }

                var dd = new DropdownField(slot.Definition.DisplayName, choices, current);
                string slotId = slot.Id;
                dd.RegisterValueChangedCallback(e =>
                {
                    // Сначала освобождаем позицию: иначе выбор нового бойца в занятый
                    // слот тихо проваливался в TryAssign и откатывался при перерисовке.
                    b.Unassign(slotId);
                    if (e.newValue != UiText.Unassigned && idByLabel.TryGetValue(e.newValue, out var compId))
                        b.TryAssign(compId, slotId);
                    AdvanceOnboarding(c);
                    RefreshCity();
                });
                slotList.Add(dd);
            }

            // Стройка (US-7.1): спец-здания с ценой; построенные/идущие — пометкой.
            var buildList = _root.Q<VisualElement>("build-list");
            buildList.Clear();
            foreach (BaseSectionType section in System.Enum.GetValues(typeof(BaseSectionType)))
            {
                if (section == BaseSectionType.None) continue; // «пустая» секция — не здание
                if (b.IsBuilt(section)) continue;
                bool inProgress = false;
                foreach (var con in b.ConstructionQueue)
                    if (con.Section == section) { inProgress = true; break; }

                var blueprint = DefaultContent.Blueprint(section, _cfg);
                // Честный ценник: здания без эффектов (US-6.4 отложен) помечены,
                // чтобы стройка не выглядела покупкой, которая ничего не даёт.
                bool noEffectYet = section == BaseSectionType.Armory || section == BaseSectionType.Laboratory;
                var text = $"{blueprint.DisplayName}: {blueprint.GoldCost} зол" +
                           (blueprint.BuildingMaterialCost > 0 ? $" + {blueprint.BuildingMaterialCost} мат" : "") +
                           (noEffectYet ? UiText.NoEffectYetMark : "") +
                           (inProgress ? UiText.InProgressMark : "");
                var sec = section;
                var btn = new Button(() => { b.StartConstruction(DefaultContent.Blueprint(sec, _cfg)); RefreshCity(); })
                { text = text };
                btn.SetEnabled(!inProgress);
                buildList.Add(btn);
            }

            // Совет (US-8.4): результат действия больше не проглатывается молча,
            // а Дипломатия тянет ВЫБРАННУЮ фракцию, а не всегда Гарнизон.
            var councilList = _root.Q<VisualElement>("council-list");
            councilList.Clear();
            var councilNote = _root.Q<Label>("council-note");
            if (councilNote != null) councilNote.text = ""; // результат — на один рендер
            if (c.Council != null)
            {
                var factionChoices = new List<string>();
                foreach (var st in c.Factions.Standings) factionChoices.Add(st.Faction.DisplayName);
                if (factionChoices.Count > 0)
                {
                    if (_diplomacyTarget == null || !factionChoices.Contains(_diplomacyTarget))
                        _diplomacyTarget = factionChoices[0];
                    var target = new DropdownField(UiText.DiplomacyTarget, factionChoices, _diplomacyTarget);
                    target.RegisterValueChangedCallback(e => { _diplomacyTarget = e.newValue; });
                    councilList.Add(target);
                }

                foreach (var action in c.Council.Actions)
                {
                    int cd = c.Council.CooldownRemaining(action.Id);
                    var btn = new Button { text = $"{action.DisplayName} ({action.GoldCost} зол/{action.InfluenceCost} вл)" +
                                                  (cd > 0 ? $" · КД {cd}" : "") };
                    string id = action.Id;
                    btn.SetEnabled(cd == 0);
                    btn.clicked += () => OnCouncilAction(id);
                    councilList.Add(btn);
                }
            }

            RefreshFactions(c);
            RefreshChronicle();
            RefreshQuestBoard(c);
        }

        private void OnCouncilAction(string actionId)
        {
            var c = GameFlow.Campaign;
            if (c == null || c.Council == null) return;
            var report = c.Council.Execute(actionId, FactionIdByName(c, _diplomacyTarget));
            Telemetry.Event("council_action", ("action", actionId), ("result", report.Result.ToString()));

            // Текст пишем ПОСЛЕ перерисовки: RefreshCity гасит прошлый результат,
            // иначе устаревший отказ висел бы под советом всю кампанию.
            string actionName = actionId;
            foreach (var a in c.Council.Actions)
                if (a.Id == actionId) { actionName = a.DisplayName; break; }
            RefreshCity();
            var note = _root.Q<Label>("council-note");
            if (note != null)
                note.text = report.Result == Game.Core.Council.CouncilActionResult.Success
                    ? UiText.CouncilResultOk + actionName
                    : UiText.CouncilResultFail + UiText.CouncilFailReason(report.Result);
        }

        private static string FactionIdByName(Campaign c, string displayName)
        {
            foreach (var st in c.Factions.Standings)
                if (st.Faction.DisplayName == displayName) return st.Faction.Id;
            return Game.Core.Factions.DefaultFactions.Garrison;
        }

        /// <summary>Отношения (Эпик 10): полосы фракций, репутация города, запас Влияния.</summary>
        private void RefreshFactions(Campaign c)
        {
            var label = _root.Q<Label>("factions-list");
            if (label == null) return;
            var lines = new List<string>
            {
                string.Format(UiText.InfluenceLine, c.Factions.Influence,
                    UiText.RepBandName(c.Factions.RepBand))
            };
            foreach (var st in c.Factions.Standings)
                lines.Add($"{st.Faction.DisplayName}: {UiText.FactionBandName(st.Band)}");
            label.text = string.Join("\n", lines);
        }

        private void RefreshChronicle()
        {
            var list = _root.Q<ScrollView>("chronicle-list");
            if (list == null) return;
            list.Clear();
            if (Chronicle.Count == 0)
            {
                var empty = new Label(UiText.ChronicleEmpty);
                empty.AddToClassList("chronicle-line");
                list.Add(empty);
                return;
            }
            for (int i = Chronicle.Count - 1; i >= 0; i--) // свежее сверху
            {
                var l = new Label(Chronicle[i]);
                l.AddToClassList("chronicle-line");
                list.Add(l);
            }
        }

        /// <summary>Доска квестов (US-14.3): совет базы собирает доступное из авторского пула.</summary>
        private void RefreshQuestBoard(Campaign c)
        {
            var board = _root.Q<VisualElement>("quest-board");
            if (board == null) return;
            board.Clear();

            c.Quests.CollectFrom(DefaultQuests.FullPool(), c.Factions, c.Flags, c.Roster.All);

            // Отложенное дело возвращается сюда (пара к кнопке «Отложить»): без этого
            // выход из панели квеста был бы билетом в один конец.
            if (GameFlow.PendingQuest != null)
                board.Add(new Button(ShowQuest)
                { text = string.Format(UiText.QuestResume, GameFlow.PendingQuest.Def.Title) });

            foreach (var q in c.Quests.Available)
            {
                string id = q.Id;
                if (id == "prologue") continue; // пролог форсируется стартом кампании, не доской
                var btn = new Button(() => BeginQuest(id)) { text = q.Title };
                btn.SetEnabled(GameFlow.PendingQuest == null);
                if (GameFlow.PendingQuest != null) btn.tooltip = UiText.QuestBusyNote;
                board.Add(btn);
            }
            // Личные арки напарников (US-9.5): доступная глава — такой же прогон,
            // но гейтится лояльностью, а не флагами доски.
            foreach (var arcRun in c.Arcs)
            {
                var owner = c.Roster.Get(arcRun.Arc.CompanionId);
                arcRun.Refresh(owner);
                if (arcRun.State != ArcState.Available || arcRun.CurrentChapter == null) continue;

                string arcId = arcRun.Arc.Id;
                var btn = new Button(() => BeginArcChapter(arcId))
                { text = string.Format(UiText.ArcChapter, arcRun.CurrentChapter.Quest.Title,
                                       owner != null ? owner.DisplayName : arcRun.Arc.CompanionId) };
                btn.SetEnabled(GameFlow.PendingQuest == null);
                board.Add(btn);
            }

            foreach (var q in c.Quests.Active)
                board.Add(new Label(q.Title + UiText.QuestInWork));
            if (c.Quests.Completed.Count > 0)
                board.Add(new Label(UiText.QuestCompletedPrefix + c.Quests.Completed.Count));
        }

        private static string DescribeCompanion(Companion comp)
        {
            string state = comp.Status.ToString();
            if (comp.IsInjured) state += $", лечится {comp.RecoveryDaysRemaining:0.#} дн.";
            return $"{comp.DisplayName}{(comp.IsProtagonist ? " ★" : "")} — {state}\n" +
                   $"Лояльность: {comp.LoyaltyBand} · Ур. {comp.Level}" +
                   (comp.UnspentSkillPoints > 0 ? $" · очков: {comp.UnspentSkillPoints}" : "") +
                   (comp.Scars.Scars.Count > 0 ? $" · шрамы: {comp.Scars.Scars.Count}" : "");
        }

        // ================= ПЕРСОНАЖ (US-2.3/5.1) =================
        private void BindCharacter()
        {
            var back = _root.Q<Button>("character-back-button");
            if (back == null) return;
            back.clicked -= ShowCity;
            back.clicked += ShowCity;
        }

        /// <summary>Экран бойца (публично — для PlayMode-смоука): статы, трейты, трата очков.</summary>
        public void ShowCharacter(string companionId)
        {
            var c = GameFlow.Campaign;
            if (c == null || c.Roster.Get(companionId) == null) return;
            _openCharacterId = companionId;
            RefreshCharacterPanel();
            Show("panel-character");
        }

        private void RefreshCharacterPanel()
        {
            var c = GameFlow.Campaign;
            var comp = c != null ? c.Roster.Get(_openCharacterId) : null;
            if (comp == null) return;

            _root.Q<Label>("character-name").text =
                comp.DisplayName + (comp.IsProtagonist ? " ★" : "");

            bool maxLevel = comp.Level >= _cfg.MaxLevel;
            string xpLine = maxLevel
                ? string.Format(UiText.XpMaxLine, comp.Level)
                : string.Format(UiText.XpLine, comp.Level, comp.Xp,
                    Game.Core.Balance.ProgressionMath.XpToNext(comp.Level, _cfg));
            _root.Q<Label>("character-summary").text =
                $"{xpLine} · {comp.Status}" +
                (comp.IsInjured ? $" ({comp.CurrentInjury}, {comp.RecoveryDaysRemaining:0.#} дн.)" : "") +
                $" · лояльность: {comp.LoyaltyBand}";

            // Атрибуты (растут только аугмент-крафтом — US-6.4, пока не реализован).
            var attrs = _root.Q<VisualElement>("character-attrs");
            attrs.Clear();
            foreach (AttributeType a in System.Enum.GetValues(typeof(AttributeType)))
            {
                if (a == AttributeType.None) continue;
                var row = new Label($"{UiText.AttributeName(a)}: {comp.GetAttribute(a)}");
                row.AddToClassList("stat-row");
                attrs.Add(row);
            }

            // Что боец даёт в бою прямо сейчас (после трейтов/шрамов/перков/гира).
            var derived = comp.EffectiveDerived(_cfg);
            // Точность в бою = база + навык ЕГО оружия × AccuracyPerWeaponSkill: без
            // этого слагаемого игрок не видит, за что платит очками (US-2.3).
            var weapon = Armory(comp);
            int weaponSkill = weapon != null ? comp.GetSkill(weapon.Skill) : 0;
            int accuracyInBattle = derived[DerivedStat.Accuracy] + weaponSkill * _cfg.AccuracyPerWeaponSkill;
            _root.Q<Label>("character-derived").text =
                $"HP {derived[DerivedStat.MaxHp]} · AP {derived[DerivedStat.ActionPoints]} · " +
                $"точность {accuracyInBattle} (база {derived[DerivedStat.Accuracy]} + навык оружия) · " +
                $"защита {derived[DerivedStat.Defense]} · " +
                $"инициатива {derived[DerivedStat.Initiative]} · крит {derived[DerivedStat.CritChance]}% · " +
                $"броня {derived[DerivedStat.Armor]} · воля {derived[DerivedStat.Resolve]}";

            var traitLines = new List<string>();
            foreach (var t in comp.Traits.Traits) traitLines.Add(t.DisplayName);
            foreach (var s in comp.Scars.Scars) traitLines.Add("✕ " + s.DisplayName);
            foreach (var p in comp.Perks) traitLines.Add("★ " + p.DisplayName);
            _root.Q<Label>("character-traits").text =
                traitLines.Count > 0 ? string.Join("\n", traitLines) : UiText.TraitsNone;

            // Мёртвым и ушедшим в антагонисты очки не тратятся: карточка остаётся
            // читаемым «досье», но обещать вложение нельзя (US-9.4).
            bool canTrain = CanTrain(comp);
            _root.Q<Label>("character-points").text =
                !canTrain ? (comp.IsAlive ? UiText.NoTrainAntagonist : UiText.NoTrainDead)
                : comp.UnspentSkillPoints > 0
                    ? UiText.SkillPointsPool + comp.UnspentSkillPoints + "\n" + UiText.SpendIrreversible
                    : UiText.NoSkillPoints;

            // Навыки: превью того, ЧТО откроет очко (перки — BuildPlanner, приёмы — каталог).
            var skills = _root.Q<ScrollView>("character-skills");
            skills.Clear();
            var perkCatalog = DefaultContent.PerkCatalog();
            foreach (SkillType s in System.Enum.GetValues(typeof(SkillType)))
            {
                if (s == SkillType.None) continue;
                var skill = s;
                int level = comp.GetSkill(skill);
                var preview = BuildPlanner.PreviewSkillPoint(comp, skill, _cfg, perkCatalog);

                string hint = "";
                if (preview.AtCap)
                {
                    hint = UiText.SkillAtCap;
                }
                else
                {
                    foreach (var perk in preview.PerksUnlocked)
                        hint += string.Format(UiText.UnlocksPerk, perk.DisplayName);
                    foreach (var ability in DefaultContent.AbilityCatalog())
                        if (ability.Skill == skill && ability.RequiredSkillLevel == preview.NewLevel)
                            hint += string.Format(UiText.UnlocksAbility, ability.DisplayName);
                    // Выше порогов контента очко покупает только точность/проверки —
                    // игрок должен понимать, за что платит (US-2.3: осознанность).
                    if (hint.Length == 0 && level >= 5) hint = UiText.SkillOnlyAccuracy;
                }

                var row = new VisualElement();
                row.AddToClassList("skill-row");
                if (hint.Length > 0 && !preview.AtCap) row.AddToClassList("skill-row--unlock");
                row.Add(new Label($"{UiText.SkillName(skill)}: {level}{hint}"));

                var plus = new Button(() => OnSpendSkillPoint(skill)) { text = "+" };
                plus.SetEnabled(canTrain && preview.CanSpend);
                row.Add(plus);
                skills.Add(row);
            }

            RefreshEquipment(c, comp);
        }

        /// <summary>Кого вообще можно тренировать: живого и не ушедшего к врагу.</summary>
        private static bool CanTrain(Companion comp)
            => comp != null && comp.IsAlive && comp.Status != CompanionStatus.Antagonist;

        // ---- Снаряжение: сташ ↔ слоты бойца (US-6.1/6.2) ----
        private void RefreshEquipment(Campaign c, Companion comp)
        {
            var equipment = _root.Q<VisualElement>("character-equipment");
            if (equipment == null) return;
            equipment.Clear();
            bool canEquip = CanTrain(comp);

            foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
            {
                var worn = comp.Equipment.Get(slot);
                var row = new VisualElement();
                row.AddToClassList("item-row");
                if (worn != null && worn.Definition.IsNamed) row.AddToClassList("item-row--named");
                row.Add(new Label($"{UiText.SlotName(slot)}: {DescribeItem(worn)}"));

                if (worn != null && canEquip)
                {
                    var slotCopy = slot;
                    row.Add(new Button(() => OnUnequip(slotCopy)) { text = UiText.Unequip });
                }
                equipment.Add(row);
            }

            var stash = _root.Q<ScrollView>("character-stash");
            if (stash == null) return;
            stash.Clear();

            bool workshop = c.Base.IsBuilt(BaseSectionType.Workshop);
            int craftMats = c.Base.Resources.Get(ResourceType.CraftingMaterial);
            _root.Q<Label>("stash-note").text = c.Base.Inventory.Count == 0
                ? UiText.StashEmpty
                : (workshop ? string.Format(UiText.CraftHint, _cfg.CraftUpgradeCost, craftMats)
                            : UiText.CraftNeedsWorkshop);

            foreach (var item in c.Base.Inventory.Items)
            {
                var it = item;
                var row = new VisualElement();
                row.AddToClassList("item-row");
                if (it.Definition.IsNamed) row.AddToClassList("item-row--named");
                row.Add(new Label(DescribeItem(it)));

                var equip = new Button(() => OnEquip(it)) { text = UiText.Equip };
                equip.SetEnabled(canEquip);
                row.Add(equip);

                // Крафт (US-6.3): единственный сток крафт-компонента.
                bool upgradable = !it.Definition.IsNamed && it.Rarity < Rarity.Epic;
                var upgrade = new Button(() => OnUpgradeItem(it)) { text = UiText.CraftUpgrade };
                upgrade.SetEnabled(workshop && upgradable && craftMats >= _cfg.CraftUpgradeCost);
                row.Add(upgrade);

                stash.Add(row);
            }
        }

        private static string DescribeItem(ItemInstance item)
        {
            if (item == null) return UiText.SlotEmpty;
            string mods = "";
            foreach (var m in item.StatMods)
                mods += $" {UiText.StatShort(m.Stat)}+{(int)m.Value}";
            return $"{item.DisplayName} ({UiText.RarityName(item.Rarity)}){mods}";
        }

        private void OnEquip(ItemInstance item)
        {
            var c = GameFlow.Campaign;
            var comp = c != null ? c.Roster.Get(_openCharacterId) : null;
            if (!CanTrain(comp) || item == null) return;
            if (!c.Base.Inventory.Remove(item)) return;

            var prev = comp.Equipment.Equip(item);
            if (prev != null) c.Base.Inventory.Add(prev); // снятое возвращается в сташ
            Telemetry.Event("item_equipped",
                ("companion", comp.Id), ("item", item.Definition.Id), ("rarity", item.Rarity.ToString()));
            AutoSave.Write(c);
            RefreshCharacterPanel();
        }

        private void OnUnequip(EquipSlot slot)
        {
            var c = GameFlow.Campaign;
            var comp = c != null ? c.Roster.Get(_openCharacterId) : null;
            if (!CanTrain(comp)) return;
            var removed = comp.Equipment.Unequip(slot);
            if (removed == null) return;
            c.Base.Inventory.Add(removed);
            AutoSave.Write(c);
            RefreshCharacterPanel();
        }

        private void OnUpgradeItem(ItemInstance item)
        {
            var c = GameFlow.Campaign;
            if (c == null || item == null) return;
            var rng = new SeededRng(Campaign.DeriveSeed(c.Seed, 500 + c.Base.CurrentDay + c.Base.Inventory.Count));
            var result = CraftSystem.TryUpgrade(item, c.Base.Resources, _cfg.CraftUpgradeCost, rng);
            if (result != CraftResult.Success)
            {
                _root.Q<Label>("stash-note").text = UiText.CraftFailed + result;
                return;
            }
            Telemetry.Event("item_upgraded",
                ("item", item.Definition.Id), ("rarity", item.Rarity.ToString()));
            AutoSave.Write(c);
            RefreshCharacterPanel();
        }

        private void OnSpendSkillPoint(SkillType skill)
        {
            var c = GameFlow.Campaign;
            var comp = c != null ? c.Roster.Get(_openCharacterId) : null;
            // Гард здесь, а не только на кнопке: ShowCharacter публичен, а телеметрия
            // и автосейв не должны срабатывать вхолостую.
            if (!CanTrain(comp) || !comp.SpendSkillPoint(skill, _cfg, DefaultContent.PerkCatalog())) return;

            Telemetry.Event("skill_point_spent",
                ("companion", comp.Id), ("skill", skill.ToString()), ("level", comp.GetSkill(skill)));
            AutoSave.Write(c); // трата необратима — фиксируем как решение (US-16.1)
            RefreshCharacterPanel();
        }

        // ================= КАРТА (US-1.1) =================
        private void BindMap()
        {
            var depart = _root.Q<Button>("depart-button");
            depart.clicked -= OnDepart;
            depart.clicked += OnDepart;

            var back = _root.Q<Button>("map-back-button");
            back.clicked -= ShowCity;
            back.clicked += ShowCity;
        }

        private void RefreshMap()
        {
            var c = GameFlow.Campaign;
            if (c == null) return;

            var nodeList = _root.Q<VisualElement>("node-list");
            nodeList.Clear();
            foreach (var node in _worldMap.Available(c.Base.CityTier, c.Flags))
            {
                var n = node;
                var btn = new Button(() => { _pickedNode = n; RefreshMap(); })
                {
                    text = $"{n.Plan.DisplayName} — дорога {n.Plan.TravelDaysOut}+{n.Plan.TravelDaysBack} дн." +
                           (_pickedNode == n ? "  ← выбрано" : "")
                };
                nodeList.Add(btn);
            }

            // Закрытые точки — с ПРИЧИНОЙ (US-1.1/17.3: игрок должен понимать, что
            // открывает узел, иначе рост города не читается как цель).
            var locked = new List<string>();
            foreach (var node in _worldMap.Locked(c.Base.CityTier, c.Flags))
                locked.Add($"{node.Plan.DisplayName} ({LockReason(node, c)})");
            _root.Q<Label>("locked-nodes").text =
                locked.Count > 0 ? UiText.LockedNodes + "\n" + string.Join("\n", locked) : "";

            var picks = _root.Q<VisualElement>("squad-picks");
            picks.Clear();
            foreach (var comp in c.Roster.All)
            {
                if (!comp.IsAvailableForDuty) continue;
                string id = comp.Id;
                var toggle = new Toggle(comp.DisplayName) { value = _squadPicks.Contains(id) };
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue) _squadPicks.Add(id); else _squadPicks.Remove(id);
                });
                picks.Add(toggle);
            }

            _root.Q<Label>("map-note").text = "";
        }

        /// <summary>Почему точка закрыта: тир города, сюжетная веха или «уже пройдено».</summary>
        private static string LockReason(WorldNode node, Campaign c)
        {
            if (c.Base.CityTier < node.RequiresCityTier)
                return string.Format(UiText.LockNeedTier, node.RequiresCityTier);
            if (!string.IsNullOrEmpty(node.RequiresFlag) && !c.Flags.Contains(node.RequiresFlag))
                return UiText.LockNeedStory;
            return UiText.LockDone;
        }

        private void OnDepart()
        {
            var c = GameFlow.Campaign;
            if (c == null || c.Outcome != CampaignOutcome.Ongoing) return;
            if (_pickedNode == null || _squadPicks.Count == 0)
            {
                _root.Q<Label>("map-note").text = "Выбери точку и отряд.";
                return;
            }
            if (_squadPicks.Count > _cfg.SquadSize)
            {
                _root.Q<Label>("map-note").text = $"Отряд не больше {_cfg.SquadSize} бойцов.";
                return;
            }
            // Предыдущая вылазка ещё не закрыта — читаемый отказ вместо исключения
            // из LaunchExpedition (кнопка молча «не работала бы»).
            if (c.ActiveExpedition != null && c.ActiveExpedition.Phase != ExpeditionPhase.Concluded)
            {
                _root.Q<Label>("map-note").text = UiText.ExpeditionStillOut;
                return;
            }

            var exp = c.LaunchExpedition(_pickedNode.Plan);
            var send = exp.TrySend(new List<string>(_squadPicks));
            if (send != ExpeditionSendResult.Success)
            {
                c.CancelExpedition(); // сбор не удался — вылазка не началась, повтор возможен
                _root.Q<Label>("map-note").text = "Отряд не собран: " + send;
                return;
            }

            RecordCycle(c.DepartExpedition()); // дни дороги тоже приносят события города
            // Решение идти фиксируется ВСЕГДА. В айронмене — особой пометкой:
            // загрузка такого сейва резолвит вылазку отступлением, а не возвращает
            // отряд домой бесплатно (иначе выход из проигрышного боя был откатом).
            if (c.Ironman) AutoSave.WriteDepartCheckpoint(c);
            else AutoSave.Write(c);

            Telemetry.Event("expedition_departed",
                ("node", _pickedNode.Id), ("squad", _squadPicks.Count));

            // Бой вылазки — в Battle-сцене через GameFlow.
            var cs = BuildExpeditionBattle(c, exp, _pickedNode);
            GameFlow.PendingBattle = cs;
            GameFlow.PendingExpedition = exp;
            GameFlow.BattleKind = PendingBattleKind.Expedition;
            Telemetry.Event("battle_started",
                ("kind", GameFlow.PendingFinale ? "finale" : "expedition"), ("node", _pickedNode.Id));
            SceneManager.LoadScene("Battle");
        }

        /// <summary>Состав боя вылазки: по точке карты; финальная окраина — из FinalBattle.</summary>
        private CombatState BuildExpeditionBattle(Campaign c, Expedition exp, WorldNode node)
        {
            var cs = new CombatState(CombatDemo.BuildArena(), _cfg,
                new SeededRng(Campaign.DeriveSeed(c.Seed, 300 + c.Base.CurrentDay)));

            var units = exp.BuildCombatUnits(Armory, DefaultContent.AbilityCatalog());

            // Баф совета «Снаряжение экспедиции» (US-8.4): точность отряду сейчас,
            // бонус золота — при победе (иначе оплаченный баф пропадал впустую).
            var buff = c.Council != null ? c.Council.ConsumeExpeditionBuff() : null;
            int accuracyBonus = buff != null ? buff.AccuracyBonus : 0;
            GameFlow.PendingBuffGold = buff != null ? buff.BonusLootGold : 0;

            List<EnemyDefinition> enemies;
            bool finale = node.Id == "horde_outskirts" && Game.Core.Story.FinalBattle.IsUnlocked(c.Flags);
            GameFlow.PendingFinale = finale;
            if (finale)
            {
                var encounter = Game.Core.Story.FinalBattle.BuildEncounter(c);
                enemies = encounter.Enemies;
                accuracyBonus += encounter.DefenderAccuracyBonus; // Готовность прикрывает своих (US-11.4)

                // Потери ростера НЕ должны наказывать дважды (меньше бойцов → ниже
                // полоса → больше врагов). Волна соразмерна тем, кто реально вышел:
                // сложность составом, а не безнадёжностью (US-3.15).
                int cap = units.Count + FinaleEnemyMargin(encounter.Band);
                if (enemies.Count > cap) enemies.RemoveRange(cap, enemies.Count - cap);
            }
            else if (node.Id == "rusted_works")
                enemies = new List<EnemyDefinition>
                {
                    DefaultContent.RustDrone(), DefaultContent.PlagueBearer(),
                    DefaultContent.ScavGunner(), DefaultContent.FeralGhoul()
                };
            else
                enemies = new List<EnemyDefinition>
                {
                    DefaultContent.RaiderBruiser(), DefaultContent.ScavGunner(), DefaultContent.FeralGhoul()
                };

            // Кривая сложности СОСТАВОМ (US-3.15), а не раздутыми HP: пустошь пустеет
            // не в пользу игрока. Без этого повторная вылазка была тем же боем всю
            // кампанию, пока отряд рос — то есть становилась всё легче.
            // Потери учитываются: волна соразмерна тем, кто реально вышел.
            if (!finale)
            {
                if (c.Base.CurrentDay >= _cfg.ExpeditionEscalationDay1 || c.Base.CityTier >= 2)
                    enemies.Add(DefaultContent.ScavGunner());
                if (c.Base.CurrentDay >= _cfg.ExpeditionEscalationDay2)
                    enemies.Add(DefaultContent.FeralGhoul());
                if (c.Base.CurrentDay >= _cfg.ExpeditionEscalationDay3)
                    enemies.Add(DefaultContent.RustDrone()); // техника подтягивается позже

                int cap = units.Count + 2;
                if (enemies.Count > cap) enemies.RemoveRange(cap, enemies.Count - cap);
            }

            // Отряд: бонус точности (баф совета + Укрепления финала) — до входа в бой.
            if (accuracyBonus != 0)
                foreach (var u in units) u.Profile.Accuracy += accuracyBonus;
            for (int i = 0; i < units.Count && i < CombatDemo.SquadSpawns.Length; i++)
                cs.AddUnit(units[i], CombatDemo.SquadSpawns[i]);

            for (int i = 0; i < enemies.Count && i < EnemySpawns.Length; i++)
                cs.AddUnit(CombatUnit.FromEnemy(enemies[i], $"e{i}"), EnemySpawns[i]);

            return cs;
        }

        /// <summary>
        /// Оружие бойца в бой: НАДЕТОЕ (US-6.2) — иначе именные трофеи квестов и
        /// дроп вылазок не доезжали до боя вообще. Фолбек — стартовый ствол по роли.
        /// Публично: этот же путь проверяется тестами (одна «оружейная» на все бои).
        /// </summary>
        public static WeaponDefinition Armory(Companion c)
        {
            var equipped = c.Equipment.EquippedWeapon;
            if (equipped != null) return equipped;
            switch (c.Id)
            {
                case "brawler": return DefaultContent.Machete();
                case "medic": return DefaultContent.Pistol();
                default: return DefaultContent.Rifle();
            }
        }

        // ================= КВЕСТ (US-13.1/14.3/17.1) =================
        /// <summary>Берёт квест с доски/пролога в работу и открывает панель прогона.</summary>
        private void BeginQuest(string questId)
        {
            var c = GameFlow.Campaign;
            if (c == null || GameFlow.PendingQuest != null) return;

            c.Quests.Start(questId);
            QuestDefinition def = null;
            foreach (var q in c.Quests.Active)
                if (q.Id == questId) { def = q; break; }
            if (def == null) return;

            GameFlow.PendingQuest = new QuestRun(def, c.Base, _cfg, c.Factions,
                c.Base.ThreatsSystem, c.Flags);
            GameFlow.LastQuestStep = null;
            Telemetry.Event("quest_started", ("quest", questId));
            ShowQuest();
        }

        /// <summary>Глава личной арки (US-9.5): играется тем же прогоном, что и квесты доски.</summary>
        private void BeginArcChapter(string arcId)
        {
            var c = GameFlow.Campaign;
            if (c == null || GameFlow.PendingQuest != null) return;

            foreach (var arcRun in c.Arcs)
            {
                if (arcRun.Arc.Id != arcId) continue;
                var owner = c.Roster.Get(arcRun.Arc.CompanionId);
                var chapter = arcRun.CurrentChapter;
                if (chapter == null || !arcRun.Begin(owner)) return;

                GameFlow.PendingQuest = new QuestRun(chapter.Quest, c.Base, _cfg,
                    c.Factions, c.Base.ThreatsSystem, c.Flags);
                GameFlow.LastQuestStep = null;
                GameFlow.PendingArcId = arcId;
                Telemetry.Event("arc_chapter_started", ("arc", arcId), ("chapter", chapter.Id));
                ShowQuest();
                return;
            }
        }

        private void ShowQuest()
        {
            var c = GameFlow.Campaign;
            // Гибель протагониста в квестовом бою (айронмен) терминальна — квест
            // не продолжается, кампания окончена (US-16.2).
            if (c == null || c.Outcome != CampaignOutcome.Ongoing)
            {
                Telemetry.Event("campaign_ended",
                    ("outcome", c != null ? c.Outcome.ToString() : "-"));
                Telemetry.End();
                GameFlow.Reset();
                BindMenu();
                SetMenuNote(UiText.GameOverIronman);
                Show("panel-menu");
                return;
            }
            RefreshQuestPanel();
            Show("panel-quest");
        }

        private void RefreshQuestPanel()
        {
            var run = GameFlow.PendingQuest;
            var c = GameFlow.Campaign;
            if (run == null || c == null) return;

            _root.Q<Label>("quest-title").text = run.Def.Title;

            var log = _root.Q<ScrollView>("quest-log");
            log.Clear();
            void AddLine(string text, string cls)
            {
                var l = new Label(text);
                l.AddToClassList(cls);
                log.Add(l);
            }
            var last = GameFlow.LastQuestStep;
            if (last != null)
            {
                foreach (var line in last.ReactionLines) AddLine(line, "quest-reaction");
                foreach (var note in last.Notes) AddLine(note, "quest-note");
            }

            var actions = _root.Q<VisualElement>("quest-actions");
            actions.Clear();

            // Терминальный исход: текст финального этапа + выход в город.
            if (!run.IsActive)
            {
                _root.Q<Label>("quest-stage-text").text = last != null ? last.Text : "";
                actions.Add(new Button(ConcludeActiveQuest) { text = UiText.QuestContinue });
                return;
            }

            var stage = run.Current;
            _root.Q<Label>("quest-stage-text").text = stage.Text +
                (stage.Kind == QuestStageKind.Check && stage.Lethal ? "\n" + UiText.QuestLethalMark : "");

            switch (stage.Kind)
            {
                case QuestStageKind.Combat:
                    actions.Add(new Button(() => StageQuestBattle(stage)) { text = UiText.QuestToBattle });
                    break;

                case QuestStageKind.Choice:
                    for (int i = 0; i < stage.Options.Count; i++)
                    {
                        int idx = i;
                        var opt = stage.Options[i];
                        bool open = run.OptionAvailable(opt, c.Roster.All);
                        // Телеграфия гейта (US-17.3): закрытая опция объясняет, ЧТО
                        // нужно — tooltip в рантайме UI Toolkit не рендерится.
                        var btn = new Button(() => OnQuestChoice(idx))
                        { text = open ? opt.Label : opt.Label + GateReason(opt, c) };
                        btn.SetEnabled(open);
                        actions.Add(btn);
                    }
                    break;

                case QuestStageKind.Check:
                    string what = stage.IsSocial ? stage.Approach.ToString() : stage.CheckSkill.ToString();
                    actions.Add(new Button(OnQuestCheck)
                    { text = $"{UiText.QuestCheckPrefix}{what} ≥ {stage.Threshold}" });
                    break;
            }

            // АНТИ-СОФТЛОК: панель квеста обязана иметь выход в город. Иначе игрок,
            // взявший дело с боевым этапом при раненом отряде, застревал навсегда:
            // «В бой» отказывал (некому идти), а лечение тикает только в городе.
            // Квест остаётся взятым — вернуться к нему кнопкой на доске.
            actions.Add(new Button(OnPostponeQuest) { text = UiText.QuestPostpone });
        }

        /// <summary>Отложить дело: в город (квест остаётся в работе, шаг сохраняется).</summary>
        private void OnPostponeQuest()
        {
            var c = GameFlow.Campaign;
            if (c == null) return;
            AutoSave.Write(c);
            ShowCity();
        }

        /// <summary>Почему опция закрыта — из данных гейта (первый непройденный).</summary>
        private static string GateReason(QuestOption opt, Campaign c)
        {
            if (opt.RequiresSkill != SkillType.None)
                return string.Format(UiText.GateNeedSkill, opt.RequiresSkill, opt.RequiresSkillLevel);
            if (!string.IsNullOrEmpty(opt.RequiresFaction))
                return string.Format(UiText.GateNeedFaction, opt.RequiresFaction, opt.RequiresBand);
            if (!string.IsNullOrEmpty(opt.RequiresTraitId))
                return string.Format(UiText.GateNeedTrait, TraitName(opt.RequiresTraitId));
            if (!string.IsNullOrEmpty(opt.BlockedByTraitId))
                return string.Format(UiText.GateBlockedByTrait, TraitName(opt.BlockedByTraitId));
            if (!string.IsNullOrEmpty(opt.RequiresAliveCompanionId))
            {
                var comp = c.Roster.Get(opt.RequiresAliveCompanionId);
                return string.Format(UiText.GateNeedAlive,
                    comp != null ? comp.DisplayName : opt.RequiresAliveCompanionId);
            }
            return "";
        }

        private static string TraitName(string traitId)
        {
            var t = ContentCatalog.Default().GetTrait(traitId);
            return t != null ? t.DisplayName : traitId;
        }

        private void OnQuestChoice(int optionIndex)
        {
            var run = GameFlow.PendingQuest;
            var c = GameFlow.Campaign;
            if (run == null || c == null || !run.IsActive) return;
            GameFlow.LastQuestStep = run.Choose(optionIndex, c.Roster.All);
            Telemetry.Event("quest_choice",
                ("quest", run.Def.Id), ("stage", GameFlow.LastQuestStep.StageId), ("option", optionIndex));
            RefreshQuestPanel();
        }

        private void OnQuestCheck()
        {
            var run = GameFlow.PendingQuest;
            var c = GameFlow.Campaign;
            if (run == null || c == null || !run.IsActive) return;
            var step = run.ResolveCheck(c.Roster.All);
            GameFlow.LastQuestStep = step;
            Telemetry.Event("quest_check",
                ("quest", run.Def.Id), ("stage", step.StageId),
                ("success", step.CheckSuccess),
                ("casualty", step.CasualtyId ?? ""));

            // Летальный провал (US-13.2): гибель протагониста терминальна (айронмен).
            if (step.CasualtyDied)
            {
                foreach (var effect in c.NotifyDeath(step.CasualtyId).Effects)
                    step.Notes.Add($"{NameOfCompanion(c, effect.CompanionId)}: {effect.Note}");
                var victim = c.Roster.Get(step.CasualtyId);
                if (victim != null && victim.IsProtagonist)
                {
                    c.Outcome = CampaignOutcome.Lost;
                    AutoSave.Write(c);
                    ShowQuest(); // терминальный путь ShowQuest уведёт в меню
                    return;
                }
            }
            RefreshQuestPanel();
        }

        /// <summary>Бой квестового этапа — в Battle-сцене; PendingQuest переживает смену сцен.</summary>
        private void StageQuestBattle(QuestStage stage)
        {
            var run = GameFlow.PendingQuest;
            var c = GameFlow.Campaign;
            if (run == null || c == null) return;
            var cs = BuildQuestBattle(c, run.Def.Id, stage.EncounterId);
            if (cs == null)
            {
                // Некому идти (все выбыли/лечатся) — бой без отряда завис бы навечно.
                _root.Q<Label>("quest-stage-text").text = stage.Text + "\n" + UiText.QuestNoSquad;
                return;
            }
            GameFlow.PendingBattle = cs;
            GameFlow.BattleKind = PendingBattleKind.Quest;
            Telemetry.Event("battle_started",
                ("kind", "quest"), ("quest", run.Def.Id), ("encounter", stage.EncounterId));
            SceneManager.LoadScene("Battle");
        }

        /// <summary>
        /// Терминальный этап подтверждён: журнал, сид босса пролога, онбординг,
        /// автосейв, город. Публично — для PlayMode-смоука.
        /// </summary>
        public void ConcludeActiveQuest()
        {
            var run = GameFlow.PendingQuest;
            var c = GameFlow.Campaign;
            if (run == null || c == null || run.IsActive) return;

            c.Quests.Complete(run.Def.Id);
            Telemetry.Event("quest_finished",
                ("quest", run.Def.Id), ("succeeded", run.State == QuestState.Succeeded));
            bool prologue = run.Def.Id == "prologue";
            bool bossBeaten = run.Def.Id == "boss_revenge" && run.State == QuestState.Succeeded;
            string arcId = GameFlow.PendingArcId;
            GameFlow.ClearQuest();

            if (prologue)
            {
                // «Бросил своих»: переговорщик может уйти к злодею — босс акта 1 (US-9.4).
                var defector = Prologue.TrySeedDefector(c);
                if (defector != null)
                    Telemetry.Event("boss_seeded", ("companion", defector.CompanionId));
                _onboarding.NotifyPrologueResolved();
            }

            // Босс повержен: пейоф уже мог примениться в Battle-сцене (до автосейва) —
            // ResolveBossRevenge идемпотентен, здесь он закрывает путь «терминал без боя».
            if (bossBeaten && c.ResolveBossRevenge())
            {
                AddChronicle($"[{c.Base.CurrentDay}] {UiText.BossBeaten}");
                Telemetry.Event("boss_defeated");
            }

            // Глава личной арки сыграна — двигаем арку (даже на провале: иначе
            // прогон навсегда застрял бы в InProgress).
            if (arcId != null)
            {
                foreach (var arcRun in c.Arcs)
                    if (arcRun.Arc.Id == arcId)
                    {
                        arcRun.CompleteChapter();
                        arcRun.Refresh(c.Roster.Get(arcRun.Arc.CompanionId));
                        Telemetry.Event("arc_chapter_done",
                            ("arc", arcId), ("succeeded", run.State == QuestState.Succeeded));
                        break;
                    }
            }
            AdvanceOnboarding(c);
            AutoSave.Write(c);

            if (c.Outcome != CampaignOutcome.Ongoing) { ShowQuest(); return; } // терминальный путь
            ShowCity();
        }

        /// <summary>Состав квестового боя: отряд по сюжету, враги по encounterId.</summary>
        private CombatState BuildQuestBattle(Campaign c, string questId, string encounterId)
        {
            var cs = new CombatState(CombatDemo.BuildArena(), _cfg,
                new SeededRng(Campaign.DeriveSeed(c.Seed, 400 + c.Base.CurrentDay)));

            // Пролог (GDD §17.1): протагонист + оба «спорных» напарника; прочие
            // квесты — доступные бойцы, лидер первым, до размера отряда.
            var squad = new List<Companion>();
            if (questId == "prologue")
            {
                foreach (var id in new[] { "leader", "negotiator", "marksman" })
                {
                    var comp = c.Roster.Get(id);
                    if (comp != null && comp.IsAlive) squad.Add(comp);
                }
            }
            else
            {
                var leader = c.Roster.Get("leader");
                if (leader != null && leader.IsAvailableForDuty) squad.Add(leader);
                foreach (var comp in c.Roster.All)
                {
                    if (squad.Count >= _cfg.SquadSize) break;
                    if (comp.IsAvailableForDuty && !squad.Contains(comp)) squad.Add(comp);
                }
            }

            if (squad.Count == 0) return null; // бой без отряда — мгновенный тупик

            // Симметрично вылазке (TrySend): участники снимаются с позиций/совета —
            // иначе после боя Status (InCamp/Injured) расходится с занятым слотом,
            // а погибший навсегда «держит» пост.
            foreach (var comp in squad)
            {
                if (comp.IsAssigned) c.Base.Unassign(comp.AssignedSlotId);
                comp.Status = CompanionStatus.InSquad;
            }

            var abilities = DefaultContent.AbilityCatalog();
            for (int i = 0; i < squad.Count && i < CombatDemo.SquadSpawns.Length; i++)
                cs.AddUnit(CombatUnit.FromCompanion(squad[i], Armory(squad[i]), _cfg, abilities),
                    CombatDemo.SquadSpawns[i]);

            var enemies = QuestEncounterEnemies(encounterId);
            int spawn = 0;

            // Босс-перебежчик (US-9.4): дерётся своими статами, гиром и приёмами —
            // это тот самый напарник, которого бросили в прологе.
            if (encounterId == DefaultQuests.BossEncounterId && c.Antagonists.Count > 0)
            {
                var record = c.Antagonists[0];
                var traitor = c.Roster.Get(record.CompanionId);
                if (traitor != null)
                {
                    cs.AddUnit(DefectionSystem.BuildBossUnit(traitor, _cfg, abilities), EnemySpawns[spawn++]);
                    Telemetry.Event("boss_encounter", ("companion", record.CompanionId));
                }
            }

            for (int i = 0; i < enemies.Count && spawn < EnemySpawns.Length; i++, spawn++)
                cs.AddUnit(CombatUnit.FromEnemy(enemies[i], $"e{i}"), EnemySpawns[spawn]);

            return cs;
        }

        /// <summary>На сколько штурм может превышать вышедший отряд — по полосе Готовности.</summary>
        private static int FinaleEnemyMargin(Game.Core.Threats.ReadinessBand band)
        {
            switch (band)
            {
                case Game.Core.Threats.ReadinessBand.Fortified: return 1;
                case Game.Core.Threats.ReadinessBand.Braced: return 2;
                default: return 4; // неготовый город встречает полную волну
            }
        }

        /// <summary>Составы врагов квестовых боёв (авторские, US-13.1).</summary>
        private static List<EnemyDefinition> QuestEncounterEnemies(string encounterId)
        {
            switch (encounterId)
            {
                case "outskirts_ambush": // пролог: посильно тройке новичков
                    return new List<EnemyDefinition>
                    { DefaultContent.RaiderBruiser(), DefaultContent.ScavGunner(), DefaultContent.FeralGhoul() };
                case "raiders": // «Пропавший караван»: отступники
                    return new List<EnemyDefinition>
                    { DefaultContent.RaiderBruiser(), DefaultContent.RaiderBruiser(), DefaultContent.ScavGunner() };
                case "horde_vanguard": // акт 2: передовой лагерь орды — жёстче
                    return new List<EnemyDefinition>
                    {
                        DefaultContent.RaiderBruiser(), DefaultContent.ScavGunner(),
                        DefaultContent.ScavGunner(), DefaultContent.FeralGhoul(), DefaultContent.FeralGhoul()
                    };
                case DefaultQuests.BossEncounterId: // свита перебежчика (сам он — отдельным юнитом)
                    return new List<EnemyDefinition>
                    { DefaultContent.RaiderBruiser(), DefaultContent.ScavGunner() };
                default:
                    return new List<EnemyDefinition>
                    { DefaultContent.RaiderBruiser(), DefaultContent.ScavGunner(), DefaultContent.FeralGhoul() };
            }
        }

        // ================= ОТЧЁТ =================
        private void BindReport()
        {
            var back = _root.Q<Button>("report-back-button");
            back.clicked -= OnReportBack;
            back.clicked += OnReportBack;
        }

        private void ShowReport()
        {
            var report = GameFlow.LastReport;
            var lines = _root.Q<ScrollView>("report-lines");
            lines.Clear();

            var campaign = GameFlow.Campaign;
            _root.Q<Label>("report-headline").text =
                campaign != null && campaign.Outcome == CampaignOutcome.Won ? UiText.CampaignWon
                : report.GameOver ? UiText.GameOverIronman
                : campaign != null && campaign.Outcome == CampaignOutcome.Lost ? UiText.CampaignLost
                : report.Outcome == CombatOutcome.Victory ? UiText.ReportVictory : UiText.ReportDefeat;

            void Add(string text)
            {
                var l = new Label(text);
                l.AddToClassList("report-line");
                lines.Add(l);
            }

            foreach (var oc in report.Companions)
            {
                var comp = campaign != null ? campaign.Roster.Get(oc.CompanionId) : null;
                string name = comp != null ? comp.DisplayName : oc.CompanionId;
                if (oc.Died)
                {
                    Add(oc.DiedOnReturn
                        ? $"✝ {name} погиб(ла) по дороге домой — беда пришла в город."
                        : $"✝ {name} погиб(ла) — насовсем.");
                    // Потеря отзывается в ростере (US-9.6): соратники скорбят.
                    if (campaign != null)
                        foreach (var effect in campaign.NotifyDeath(oc.CompanionId).Effects)
                            Add("    " + string.Format(UiText.ChronicleMourn,
                                NameOf(effect.CompanionId), effect.Note));
                }
                else if (oc.Injury != Game.Core.Health.InjuryTier.None)
                    Add($"{name}: ранение {oc.Injury}" + (oc.ScarId != null ? $", вечный шрам ({oc.ScarId})" : ""));
            }
            if (report.GoldBanked > 0)
                Add($"Добыча: {report.GoldBanked} зол, {report.BuildingMaterialBanked} строймат, {report.CraftingMaterialBanked} крафт.");
            foreach (var item in report.LootDropped) Add($"Трофей: {item.DisplayName} ({item.Rarity})");
            string NameOf(string id)
            {
                var comp = campaign != null ? campaign.Roster.Get(id) : null;
                return comp != null ? comp.DisplayName : id;
            }
            foreach (var id in report.LeveledUp) Add($"↑ {NameOf(id)}: новый уровень.");
            foreach (var id in report.RecoveredOnReturn) Add($"{NameOf(id)}: раны затянулись в дороге.");

            // Что случилось в городе, пока отряда не было (US-8.2).
            int day = campaign != null ? campaign.Base.CurrentDay : 0;
            foreach (var incident in report.IncidentsWhileAway)
            {
                string who = incident.ResolvedById != null
                    ? NameOf(incident.ResolvedById) : UiText.IncidentNobody;
                string line = $"Дома: {incident.DisplayName} — " +
                              (incident.Success ? UiText.IncidentResolved : UiText.IncidentFailed) + $" ({who})";
                if (incident.CrisisApplied != Game.Core.Threats.CrisisEffect.None)
                    line += $"\n    ⚠ {UiText.CrisisName(incident.CrisisApplied)}" +
                            (incident.CrisisVictimId != null ? ": " + NameOf(incident.CrisisVictimId) : "");
                Add(line);
                AddChronicle($"[{day}] {line}");
            }
            foreach (var built in report.ConstructionCompletedWhileAway)
            {
                string line = $"{UiText.ChronicleBuilt} {built}";
                Add(line);
                AddChronicle($"[{day}] {line}");
            }
            if (report.CityTierAdvancedTo > 0)
            {
                Add(string.Format(UiText.ChronicleCityGrew, report.CityTierAdvancedTo));
                ReportCityGrowth(report.CityTierAdvancedFrom, report.CityTierAdvancedTo, day);
            }

            Show("panel-report");
        }

        private void OnReportBack()
        {
            GameFlow.LastReport = null;
            var c = GameFlow.Campaign;

            // Кампания окончена (победа финала / пермасмерть айронмена) — в меню,
            // без пересейва: исход терминален (US-16.2).
            if (c == null || c.Outcome != CampaignOutcome.Ongoing)
            {
                Telemetry.Event("campaign_ended",
                    ("outcome", c != null ? c.Outcome.ToString() : "-"));
                Telemetry.End();
                GameFlow.Reset();
                BindMenu();
                SetMenuNote(c != null && c.Outcome == CampaignOutcome.Won
                    ? "Кампания выиграна. Город выстоял." : "Кампания окончена.");
                Show("panel-menu");
                return;
            }

            var stepBefore = _onboarding.Step;
            _onboarding.NotifyExpeditionConcluded(c);
            if (_onboarding.Step != stepBefore)
                Telemetry.Event("onboarding_step", ("step", _onboarding.Step.ToString()));
            Telemetry.Event("report_acknowledged");
            AutoSave.Write(c);
            ShowCity();
        }
    }
}
