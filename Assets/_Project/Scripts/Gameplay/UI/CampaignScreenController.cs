using System.Collections.Generic;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Council;
using Game.Core.Economy;
using Game.Core.Expeditions;
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
        private readonly HashSet<string> _squadPicks = new HashSet<string>();

        private VisualElement _root;
        private readonly Dictionary<string, VisualElement> _panels = new Dictionary<string, VisualElement>();

        private void Awake()
        {
            _cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();
            _worldMap = DefaultWorld.NewMap();
        }

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            foreach (var name in new[] { "panel-menu", "panel-create", "panel-city", "panel-map", "panel-report" })
                _panels[name] = _root.Q<VisualElement>(name);

            BindMenu();
            BindCreate();
            BindCity();
            BindMap();
            BindReport();

            // Контроллер пересоздаётся при каждом возврате из Battle-сцены:
            // онбординг фаст-форвардится из персистентных флагов, а не с пролога.
            if (GameFlow.Campaign != null)
                while (_onboarding.TryAdvance(GameFlow.Campaign)) { }

            if (GameFlow.LastReport != null) ShowReport();
            else if (GameFlow.Campaign != null) ShowCity();
            else Show("panel-menu");
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
            AttachCouncilIfMissing();
            _onboarding = new OnboardingFlow();
            while (_onboarding.TryAdvance(GameFlow.Campaign)) { } // фаст-форвард из флагов
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
                var attr = a;
                var row = new VisualElement();
                row.AddToClassList("stat-row");
                row.Add(new Label($"{attr}: {_builder.Attribute(attr)}"));
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
                row.Add(new Label($"{skill}: {_builder.Skill(skill)}"));
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
            if (_builder == null) RebuildBuilder(); // прямой вызов без меню (тесты)
            var name = _root.Q<TextField>("leader-name").value;
            if (!string.IsNullOrEmpty(name)) _builder.DisplayName = name;
            GameFlow.Campaign = Campaign.NewGame(_cfg, _builder.Build("leader"));
            AttachCouncilIfMissing();
            _onboarding = new OnboardingFlow();
            // Пролог (US-17.1) в UI-подаче — итерация 17 (плейтест-контент); шаг закрываем.
            GameFlow.Campaign.Flags.Add(Game.Core.Quests.DefaultQuests.PrologueDoneFlag);
            _onboarding.TryAdvance(GameFlow.Campaign);
            _onboarding.AcknowledgeIntro(GameFlow.Campaign);
            AutoSave.Write(GameFlow.Campaign);
            ShowCity();
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
            c.AdvanceDays(1);
            _onboarding.TryAdvance(c);
            AutoSave.Write(c);
            RefreshCity();
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
                var card = new Label(DescribeCompanion(comp));
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
                    _onboarding.TryAdvance(c);
                    RefreshCity();
                });
                slotList.Add(dd);
            }

            // Стройка (US-7.1): спец-здания с ценой; построенные/идущие — пометкой.
            var buildList = _root.Q<VisualElement>("build-list");
            buildList.Clear();
            foreach (BaseSectionType section in System.Enum.GetValues(typeof(BaseSectionType)))
            {
                if (b.IsBuilt(section)) continue;
                bool inProgress = false;
                foreach (var con in b.ConstructionQueue)
                    if (con.Section == section) { inProgress = true; break; }

                var blueprint = DefaultContent.Blueprint(section, _cfg);
                var text = $"{blueprint.DisplayName}: {blueprint.GoldCost} зол" +
                           (blueprint.BuildingMaterialCost > 0 ? $" + {blueprint.BuildingMaterialCost} мат" : "") +
                           (inProgress ? UiText.InProgressMark : "");
                var sec = section;
                var btn = new Button(() => { b.StartConstruction(DefaultContent.Blueprint(sec, _cfg)); RefreshCity(); })
                { text = text };
                btn.SetEnabled(!inProgress);
                buildList.Add(btn);
            }

            // Совет (US-8.4).
            var councilList = _root.Q<VisualElement>("council-list");
            councilList.Clear();
            if (c.Council != null)
                foreach (var action in c.Council.Actions)
                {
                    int cd = c.Council.CooldownRemaining(action.Id);
                    var btn = new Button { text = $"{action.DisplayName} ({action.GoldCost} зол/{action.InfluenceCost} вл)" +
                                                  (cd > 0 ? $" · КД {cd}" : "") };
                    string id = action.Id;
                    btn.SetEnabled(cd == 0);
                    btn.clicked += () => { c.Council.Execute(id, Game.Core.Factions.DefaultFactions.Garrison); RefreshCity(); };
                    councilList.Add(btn);
                }
        }

        private static string DescribeCompanion(Companion comp)
        {
            string state = comp.Status.ToString();
            if (comp.IsInjured) state += $", лечится {comp.RecoveryDaysRemaining:0.#} дн.";
            return $"{comp.DisplayName}{(comp.IsProtagonist ? " ★" : "")} — {state}\n" +
                   $"Лояльность: {comp.LoyaltyBand} · Ур. {comp.Level}" +
                   (comp.Scars.Scars.Count > 0 ? $" · шрамы: {comp.Scars.Scars.Count}" : "");
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

            var locked = new List<string>();
            foreach (var node in _worldMap.Locked(c.Base.CityTier, c.Flags))
                locked.Add(node.Plan.DisplayName);
            _root.Q<Label>("locked-nodes").text =
                locked.Count > 0 ? UiText.LockedNodes + " " + string.Join(", ", locked) : "";

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

            var exp = c.LaunchExpedition(_pickedNode.Plan);
            var send = exp.TrySend(new List<string>(_squadPicks));
            if (send != ExpeditionSendResult.Success)
            {
                c.CancelExpedition(); // сбор не удался — вылазка не началась, повтор возможен
                _root.Q<Label>("map-note").text = "Отряд не собран: " + send;
                return;
            }

            c.DepartExpedition();
            // В айронмене НЕ чекпойнтим середину вылазки: загрузка возвращала бы
            // отряд домой «бесплатно» — выход из проигрышного боя. Последний
            // автосейв = город до выхода; бой после исхода сейвится сразу.
            if (!c.Ironman) AutoSave.Write(c);

            // Бой вылазки — в Battle-сцене через GameFlow.
            var cs = BuildExpeditionBattle(c, exp, _pickedNode);
            GameFlow.PendingBattle = cs;
            GameFlow.PendingExpedition = exp;
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

            // Отряд: бонус точности (баф совета + Укрепления финала) — до входа в бой.
            if (accuracyBonus != 0)
                foreach (var u in units) u.Profile.Accuracy += accuracyBonus;
            for (int i = 0; i < units.Count && i < CombatDemo.SquadSpawns.Length; i++)
                cs.AddUnit(units[i], CombatDemo.SquadSpawns[i]);

            var spawns = new[]
            {
                new GridPos(10, 2), new GridPos(11, 3), new GridPos(10, 4),
                new GridPos(11, 5), new GridPos(10, 6), new GridPos(11, 1),
                new GridPos(10, 0), new GridPos(11, 7)
            };
            for (int i = 0; i < enemies.Count && i < spawns.Length; i++)
                cs.AddUnit(CombatUnit.FromEnemy(enemies[i], $"e{i}"), spawns[i]);

            return cs;
        }

        private static WeaponDefinition Armory(Companion c)
        {
            switch (c.Id)
            {
                case "brawler": return DefaultContent.Machete();
                case "medic": return DefaultContent.Pistol();
                default: return DefaultContent.Rifle();
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
                if (oc.Died) Add($"✝ {name} погиб(ла) — насовсем.");
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
                GameFlow.Reset();
                BindMenu();
                SetMenuNote(c != null && c.Outcome == CampaignOutcome.Won
                    ? "Кампания выиграна. Город выстоял." : "Кампания окончена.");
                Show("panel-menu");
                return;
            }

            _onboarding.NotifyExpeditionConcluded(c);
            AutoSave.Write(c);
            ShowCity();
        }
    }
}
