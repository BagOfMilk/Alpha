using System.Collections.Generic;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Quests;
using Game.Core.Saves;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Экран боя на UI Toolkit (US-18.4, итерация 15): игрок ВПЕРВЫЕ управляет
    /// отрядом руками, врагов ведёт CombatAi. Дата-управляемо: разметка в
    /// BattleScreen.uxml/.uss, контроллер только читает CombatState и шлёт действия.
    /// Клик по врагу — атака (или взведённая способность), по свободному
    /// достижимому тайлу — движение; наведение на врага телеграфирует шанс и ПОЧЕМУ
    /// (укрытие/дистанция — US-3.3/17.3). Сцена: Alpha → Create Battle Scene.
    /// </summary>
    public sealed class BattleScreenController : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — дефолтный баланс из кода.")]
        public BalanceConfigAsset balanceAsset;
        public int seed = 42;

        /// <summary>Бой (для PlayMode-тестов и внешней оркестрации).</summary>
        public CombatState Combat => _cs;

        private BalanceConfig _cfg;
        private CombatState _cs;

        private VisualElement _grid;
        private VisualElement _initiativeBar;
        private VisualElement _abilityBar;
        private Label _roundLabel, _unitName, _unitStats, _targetInfo, _outcomeLabel;
        private ScrollView _logScroll;
        private Button _strikeButton, _endTurnButton, _continueButton, _exitButton;

        private readonly Dictionary<GridPos, Button> _tiles = new Dictionary<GridPos, Button>();
        private string _armedAbilityId; // взведённая способность (следующий клик по цели)
        private bool _armedStrike;      // потратить Strike-метр на следующую атаку
        private int _shownLogLines;
        private bool _combatStarted;    // Begin() ровно один раз (ре-enable не перезапускает бой)

        private bool _campaignBattle; // бой вылазки из GameFlow (иначе — песочница)

        private void Awake()
        {
            _cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();
            if (GameFlow.HasPendingBattle)
            {
                _cs = GameFlow.PendingBattle; // бой вылазки собрал кампанийный экран
                _campaignBattle = true;
            }
            else
            {
                _cs = BuildSkirmish(_cfg, seed);
            }
        }

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _grid = root.Q<VisualElement>("grid");
            _initiativeBar = root.Q<VisualElement>("initiative-bar");
            _abilityBar = root.Q<VisualElement>("ability-bar");
            _roundLabel = root.Q<Label>("round-label");
            _unitName = root.Q<Label>("unit-name");
            _unitStats = root.Q<Label>("unit-stats");
            _targetInfo = root.Q<Label>("target-info");
            _outcomeLabel = root.Q<Label>("outcome-label");
            _logScroll = root.Q<ScrollView>("log-scroll");
            _strikeButton = root.Q<Button>("strike-button");
            _endTurnButton = root.Q<Button>("end-turn-button");
            _continueButton = root.Q<Button>("continue-button");
            _continueButton.clicked -= OnContinueAfterBattle;
            _continueButton.clicked += OnContinueAfterBattle;

            // Песочница — не тупик: выход в кампанийное меню доступен всегда
            // (в бою кампании выход только через исход + «Продолжить»).
            _exitButton = root.Q<Button>("exit-button");
            if (_exitButton != null)
            {
                _exitButton.clicked -= OnExitSandbox;
                _exitButton.clicked += OnExitSandbox;
                _exitButton.style.display = _campaignBattle ? DisplayStyle.None : DisplayStyle.Flex;
            }

            // Идемпотентные подписки: ре-enable не даёт дублей.
            _strikeButton.clicked -= ToggleStrike;
            _strikeButton.clicked += ToggleStrike;
            _endTurnButton.clicked -= EndPlayerTurn;
            _endTurnButton.clicked += EndPlayerTurn;

            BuildGrid();
            _logScroll.Clear();
            _shownLogLines = 0;
            if (!_combatStarted)
            {
                _combatStarted = true;
                _cs.Begin();
                RunEnemyTurns();
            }
            Refresh();
        }

        private void ToggleStrike()
        {
            _armedStrike = !_armedStrike;
            Refresh();
        }

        /// <summary>Демо-стычка: 4 напарника из бэкграундов против 5 ролей бестиария.</summary>
        private static CombatState BuildSkirmish(BalanceConfig cfg, int seed)
        {
            var cs = new CombatState(CombatDemo.BuildArena(), cfg, new SeededRng(seed));
            var abilities = DefaultContent.AbilityCatalog();

            var squad = new[]
            {
                DefaultContent.Marksman().CreateInstance("marksman", cfg),
                DefaultContent.Brawler().CreateInstance("brawler", cfg),
                DefaultContent.Medic().CreateInstance("medic", cfg),
                DefaultContent.Leader().CreateInstance("leader", cfg)
            };
            var weapons = new[]
            {
                DefaultContent.Rifle(), DefaultContent.Machete(),
                DefaultContent.Pistol(), DefaultContent.Rifle()
            };
            for (int i = 0; i < squad.Length; i++)
            {
                squad[i].RefreshPerks(DefaultContent.PerkCatalog());
                cs.AddUnit(CombatUnit.FromCompanion(squad[i], weapons[i], cfg, abilities),
                    CombatDemo.SquadSpawns[i]);
            }

            cs.AddUnit(CombatUnit.FromEnemy(DefaultContent.RaiderBruiser(), "e_bruiser"), new GridPos(10, 2));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultContent.ScavGunner(), "e_gunner"), new GridPos(11, 3));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultContent.RustDrone(), "e_drone"), new GridPos(10, 4));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultContent.PlagueBearer(), "e_plague"), new GridPos(11, 5));
            cs.AddUnit(CombatUnit.FromEnemy(DefaultContent.FeralGhoul(), "e_ghoul"), new GridPos(10, 6));
            return cs;
        }

        // ---- Постройка грида ----
        private void BuildGrid()
        {
            _grid.Clear();
            _tiles.Clear();
            for (int y = 0; y < _cs.Map.Height; y++)
            {
                var row = new VisualElement();
                row.AddToClassList("grid-row");
                for (int x = 0; x < _cs.Map.Width; x++)
                {
                    var pos = new GridPos(x, y);
                    var tile = new Button { text = "" };
                    tile.AddToClassList("tile");
                    tile.clicked += () => OnTileClicked(pos);
                    tile.RegisterCallback<MouseEnterEvent>(_ => OnTileHovered(pos));
                    row.Add(tile);
                    _tiles[pos] = tile;
                }
                _grid.Add(row);
            }
        }

        // ---- Ввод игрока ----
        private void OnTileClicked(GridPos pos)
        {
            if (_cs.Outcome != CombatOutcome.Ongoing) return;
            var current = _cs.Current;
            if (current == null || current.Side != Side.Player || !current.IsActive) return;

            var occupantId = _cs.Map.OccupantAt(pos);
            var occupant = occupantId != null ? _cs.GetUnit(occupantId) : null;

            if (occupant != null && occupant.Side == Side.Enemy)
            {
                if (_armedAbilityId != null)
                {
                    var r = _cs.UseAbility(_armedAbilityId, occupant.Id);
                    if (r == CombatActionResult.Success) _armedAbilityId = null;
                    else _targetInfo.text = "Способность не применена: " + Reason(r);
                }
                else
                {
                    var r = _cs.Attack(occupant.Id, _armedStrike && current.StrikeMeter >= _cfg.StrikeGuaranteeAt);
                    if (r == CombatActionResult.Success) _armedStrike = false;
                    else _targetInfo.text = "Атака не выполнена: " + Reason(r);
                }
            }
            else if (occupant != null && occupant.Side == Side.Player
                     && occupant.LifeState == UnitLifeState.Downed)
            {
                _cs.Stabilize(occupant.Id); // клик по павшему союзнику — стабилизация
            }
            else if (occupant != null && occupant.Side == Side.Player && _armedAbilityId != null)
            {
                // Перевязка/Перегруппировка/Очнись!: взведённая способность по союзнику
                // (или себе). Core сам валидирует тип цели/дальность/КД.
                var r = _cs.UseAbility(_armedAbilityId, occupant == current ? null : occupant.Id);
                if (r == CombatActionResult.Success) _armedAbilityId = null;
                else _targetInfo.text = "Способность не применена: " + Reason(r);
            }
            else if (occupant == null)
            {
                if (_armedAbilityId != null)
                {
                    var r = _cs.UseAbility(_armedAbilityId, targetTile: pos); // ловушка и т.п.
                    if (r == CombatActionResult.Success) _armedAbilityId = null;
                    else _targetInfo.text = "Способность не применена: " + Reason(r);
                }
                else
                {
                    _cs.Move(pos);
                }
            }

            AfterPlayerAction();
        }

        private void OnTileHovered(GridPos pos)
        {
            var current = _cs.Current;
            var occupantId = _cs.Map.OccupantAt(pos);
            var target = occupantId != null ? _cs.GetUnit(occupantId) : null;

            if (current == null || target == null || target.Side != Side.Enemy || !target.IsActive)
            {
                _targetInfo.text = "наведи на врага — покажу шанс и почему";
                return;
            }

            // Телеграфия честного процента (US-3.3): шанс + ПОЧЕМУ. Взведённая
            // способность меняет точность залпа — превью обязано это учитывать,
            // иначе показанный процент расходится с роллом (и метрикой R11).
            int abilityBonus = 0;
            if (_armedAbilityId != null)
            {
                foreach (var ab in current.Abilities)
                    if (ab.Id == _armedAbilityId)
                    {
                        foreach (var fx in ab.Effects)
                            if (fx.Kind == AbilityEffectKind.WeaponAttack) abilityBonus += fx.AccuracyBonus;
                        break;
                    }
            }
            int chance = _cs.HitChancePreview(current, target, abilityBonus);
            var cover = _cs.Map.CoverAgainst(target.Pos, current.Pos);
            int dist = GridPos.Chebyshev(current.Pos, target.Pos);
            bool coverApplies = current.Weapon != null && !current.Weapon.IsMelee;
            string coverText = !coverApplies ? "ближний бой — укрытие не влияет"
                : cover == CoverType.Full ? "полное укрытие −" + _cfg.CoverFullHitPenalty
                : cover == CoverType.Half ? "полуукрытие −" + _cfg.CoverHalfHitPenalty
                : "без укрытия";
            _targetInfo.text = $"{target.Profile.DisplayName}: {chance}% попадания\n" +
                               $"{coverText} · дистанция {dist}\n" +
                               $"HP {target.Hp}/{target.Profile.MaxHp} · броня {target.EffectiveArmor}";
        }

        /// <summary>Конец хода игрока (кнопка) — дальше очередь ведёт ИИ.</summary>
        public void EndPlayerTurn()
        {
            if (_cs.Outcome != CombatOutcome.Ongoing) return;
            _armedAbilityId = null;
            _armedStrike = false;
            _cs.EndTurn();
            RunEnemyTurns();
            Refresh();
        }

        private void AfterPlayerAction()
        {
            RunEnemyTurns(); // если действие завершило ход/бой — ИИ подхватывает
            Refresh();
        }

        /// <summary>Все ходы врагов подряд — до юнита игрока или исхода боя.</summary>
        private void RunEnemyTurns()
        {
            int guard = 64;
            while (_cs.Outcome == CombatOutcome.Ongoing && guard-- > 0)
            {
                var u = _cs.Current;
                if (u == null) break;
                if (u.Side == Side.Player && u.IsActive) break;
                CombatAi.TakeTurn(_cs); // враги и недееспособные — через ИИ/скип
            }
        }

        // ---- Отрисовка ----
        private void Refresh()
        {
            var current = _cs.Current;
            _roundLabel.text = $"Раунд {_cs.Round}";

            // Инициатива.
            _initiativeBar.Clear();
            if (_cs.TurnOrder != null)
                foreach (var u in _cs.TurnOrder)
                {
                    if (!u.IsActive) continue;
                    var chip = new Label(u.Profile.DisplayName);
                    chip.AddToClassList("initiative-chip");
                    chip.AddToClassList(u.Side == Side.Player ? "initiative-chip--player" : "initiative-chip--enemy");
                    if (u == current) chip.AddToClassList("initiative-chip--current");
                    _initiativeBar.Add(chip);
                }

            // Панель юнита.
            if (current != null)
            {
                _unitName.text = current.Profile.DisplayName + (current.Side == Side.Player ? "" : " (враг)");
                _unitStats.text = $"HP {current.Hp}/{current.Profile.MaxHp} · AP {current.Ap}\n" +
                                  $"Strike {current.StrikeMeter}/{_cfg.StrikeGuaranteeAt}" +
                                  (_armedStrike ? " (ВЗВЕДЁН)" : "") +
                                  $"\nБроня {current.EffectiveArmor} · Точн. {current.Profile.Accuracy}";
            }

            RefreshAbilityBar(current);
            RefreshGrid(current);
            RefreshLog();

            _strikeButton.SetEnabled(current != null && current.Side == Side.Player
                                     && current.StrikeMeter >= _cfg.StrikeGuaranteeAt);
            _strikeButton.EnableInClassList("action-btn--armed", _armedStrike);
            _endTurnButton.SetEnabled(_cs.Outcome == CombatOutcome.Ongoing
                                      && current != null && current.Side == Side.Player);

            if (_cs.Outcome != CombatOutcome.Ongoing)
            {
                _outcomeLabel.text = _cs.Outcome == CombatOutcome.Victory ? "ПОБЕДА" : "ПОРАЖЕНИЕ";
                _outcomeLabel.AddToClassList("outcome-label--visible");
                // Кампанийный бой: «Продолжить» уводит в отчёт возвращения.
                _continueButton.EnableInClassList("continue-button--visible", _campaignBattle);
            }
        }

        /// <summary>Выход из песочницы боя в кампанийное меню (последствий нет).</summary>
        private void OnExitSandbox()
        {
            if (_campaignBattle) return; // кампанийный бой закрывается только исходом
            GameFlow.ClearBattle();
            UnityEngine.SceneManagement.SceneManager.LoadScene("Campaign");
        }

        /// <summary>Гард повторного входа: LoadScene отложен до конца кадра — даблклик
        /// по «Продолжить» иначе применил бы последствия боя дважды.</summary>
        private bool _concluded;

        /// <summary>Бой вылазки/квеста завершён: последствия в ростер → Campaign-сцена.</summary>
        private void OnContinueAfterBattle()
        {
            if (_concluded) return;
            if (!_campaignBattle || _cs.Outcome == CombatOutcome.Ongoing || GameFlow.Campaign == null) return;
            _concluded = true;
            var campaign = GameFlow.Campaign;
            bool victory = _cs.Outcome == CombatOutcome.Victory;

            // Квестовый бой (US-13.1/17.1): последствия в ростер как у вылазки, но без
            // дороги/лута — награду даёт Outcome-этап; шаг квеста играется в Campaign.
            // Ветка выбирается по ТИПУ боя, а не по «висит ли квест»: отложенный
            // квест не должен перехватывать исход боя вылазки (иначе вылазка не
            // завершается никогда, а этап квеста засчитывается чужим боем).
            if (GameFlow.BattleKind == PendingBattleKind.Quest && GameFlow.PendingQuest != null)
            {
                var qReport = QuestCombat.ApplyToRoster(_cs, campaign.Roster, campaign.Cfg,
                    scarPicker: null, inventory: campaign.Base.Inventory);
                if (qReport.ProtagonistDied) campaign.Outcome = CampaignOutcome.Lost;
                GameFlow.LastQuestStep = GameFlow.PendingQuest.ResolveCombat(victory);
                // Терминал достигнут прямо боем (награда уже начислена Finalize):
                // журнал закрывается ДО автосейва — иначе сейв «награда есть, квест
                // Active» позволял дюп через выход из игры (Restore демотирует Active).
                if (!GameFlow.PendingQuest.IsActive)
                {
                    campaign.Quests.Complete(GameFlow.PendingQuest.Def.Id);
                    // Пейоф применяется ДО автосейва: иначе выход из игры на панели
                    // итога терял награду (гир перебежчика) навсегда.
                    if (GameFlow.PendingQuest.Def.Id == "boss_revenge"
                        && GameFlow.PendingQuest.State == Game.Core.Quests.QuestState.Succeeded)
                        campaign.ResolveBossRevenge();
                }
                // Смерти квестового боя отзываются в ростере (US-9.6).
                foreach (var oc in qReport.Companions)
                    if (oc.Died) campaign.NotifyDeath(oc.CompanionId);
                Telemetry.Event("battle_ended", Telemetry.CombatStats(_cs, "quest"));
                AutoSave.Write(campaign); // смерти зафиксированы — перезагрузка их не отменит
                GameFlow.ClearBattle();
                UnityEngine.SceneManagement.SceneManager.LoadScene("Campaign");
                return;
            }

            GameFlow.LastReport = campaign.ConcludeExpedition(_cs);
            Telemetry.Event("battle_ended",
                Telemetry.CombatStats(_cs, GameFlow.PendingFinale ? "finale" : "expedition"));
            // Инциденты дороги домой эмитим ЗДЕСЬ, а не в ShowReport: тот вызывается
            // при каждом пересоздании контроллера и дублировал бы события воронки.
            foreach (var incident in GameFlow.LastReport.IncidentsWhileAway)
                Telemetry.Incident(incident, "travel_back");

            // Бонус золота от «Снаряжения экспедиции» — банкуется только при победе.
            if (victory && GameFlow.PendingBuffGold > 0)
                campaign.Base.Resources.Add(Game.Core.Economy.ResourceType.Gold, GameFlow.PendingBuffGold);

            // ФИНАЛЬНАЯ битва (US-11.4/14.2): исход кампании решает FinalBattle.Resolve
            // (победа = кампания выиграна + айронмен-ачивка; поражение = game over).
            if (GameFlow.PendingFinale && campaign.Outcome == CampaignOutcome.Ongoing)
                Game.Core.Story.FinalBattle.Resolve(campaign, victory);

            AutoSave.Write(campaign);
            GameFlow.ClearBattle();
            UnityEngine.SceneManagement.SceneManager.LoadScene("Campaign");
        }

        private void RefreshAbilityBar(CombatUnit current)
        {
            _abilityBar.Clear();
            if (current == null || current.Side != Side.Player || !current.IsActive) return;

            foreach (var ability in current.Abilities)
            {
                int cd = current.CooldownRemaining(ability.Id);
                var btn = new Button
                {
                    text = $"{ability.DisplayName}\n{ability.ApCost} AP" + (cd > 0 ? $" · КД {cd}" : "")
                };
                btn.AddToClassList("ability-btn");
                if (_armedAbilityId == ability.Id) btn.AddToClassList("ability-btn--armed");
                btn.SetEnabled(cd == 0 && current.Ap >= ability.ApCost);
                string id = ability.Id;
                btn.clicked += () =>
                {
                    _armedAbilityId = _armedAbilityId == id ? null : id; // повторный клик снимает
                    Refresh();
                };
                _abilityBar.Add(btn);
            }
        }

        private void RefreshGrid(CombatUnit current)
        {
            Dictionary<GridPos, int> reachable = null;
            if (current != null && current.Side == Side.Player && current.IsActive)
                reachable = _cs.ReachableFor(current);

            foreach (var kv in _tiles)
            {
                var pos = kv.Key;
                var tile = kv.Value;
                tile.RemoveFromClassList("tile--player");
                tile.RemoveFromClassList("tile--enemy");
                tile.RemoveFromClassList("tile--downed");
                tile.RemoveFromClassList("tile--reachable");
                tile.RemoveFromClassList("tile--current");
                tile.RemoveFromClassList("tile--wall");
                tile.RemoveFromClassList("tile--cover");
                tile.text = "";

                if (!_cs.Map.IsWalkable(pos))
                {
                    tile.AddToClassList("tile--wall");
                    continue;
                }
                if (HasAnyCover(pos)) tile.AddToClassList("tile--cover");

                var id = _cs.Map.OccupantAt(pos);
                var u = id != null ? _cs.GetUnit(id) : null;
                if (u != null)
                {
                    tile.text = $"{Short(u.Profile.DisplayName)}\n{u.Hp}/{u.Profile.MaxHp}";
                    if (u.LifeState == UnitLifeState.Downed) tile.AddToClassList("tile--downed");
                    else tile.AddToClassList(u.Side == Side.Player ? "tile--player" : "tile--enemy");
                    if (u == current) tile.AddToClassList("tile--current");
                }
                else if (reachable != null && reachable.ContainsKey(pos))
                {
                    tile.AddToClassList("tile--reachable");
                }
            }
        }

        private bool HasAnyCover(GridPos pos)
        {
            return _cs.Map.GetCover(pos, Direction.North) != CoverType.None
                || _cs.Map.GetCover(pos, Direction.South) != CoverType.None
                || _cs.Map.GetCover(pos, Direction.East) != CoverType.None
                || _cs.Map.GetCover(pos, Direction.West) != CoverType.None;
        }

        private void RefreshLog()
        {
            for (; _shownLogLines < _cs.Log.Count; _shownLogLines++)
            {
                var line = new Label(_cs.Log[_shownLogLines]);
                line.AddToClassList("log-line");
                _logScroll.Add(line);
            }
            _logScroll.schedule.Execute(() => _logScroll.scrollOffset = new Vector2(0, float.MaxValue));
        }

        private static string Short(string name)
            => string.IsNullOrEmpty(name) ? "?" : name.Length <= 8 ? name : name.Substring(0, 8);

        private static string Reason(CombatActionResult r)
        {
            switch (r)
            {
                case CombatActionResult.NotEnoughAp: return "не хватает AP";
                case CombatActionResult.OutOfRange: return "вне дальности";
                case CombatActionResult.NoLineOfSight: return "нет линии обзора";
                case CombatActionResult.NotReachable: return "не добраться";
                case CombatActionResult.OnCooldown: return "на перезарядке";
                case CombatActionResult.InvalidTarget: return "неверная цель";
                default: return "нельзя сейчас";
            }
        }
    }
}
