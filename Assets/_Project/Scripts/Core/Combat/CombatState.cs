using System;
using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.Combat
{
    /// <summary>Результат боевого действия (паттерн AssignmentResult).</summary>
    public enum CombatActionResult
    {
        Success = 0,
        NotEnoughAp = 1,
        OutOfRange = 2,
        NoLineOfSight = 3,
        NotReachable = 4,
        InvalidTarget = 5,
        InvalidAction = 6
    }

    public enum CombatOutcome
    {
        Ongoing = 0,
        Victory = 1, // все враги выбыли
        Defeat = 2   // не осталось действующих юнитов игрока
    }

    /// <summary>
    /// Оркестратор боя: грид + юниты + индивидуальная инициатива + действия текущего
    /// юнита (движение/атака/стабилизация/конец хода). Правила Эпиков 3–4: пул AP,
    /// надёжный %, Strike-метр, статусы (DoT в начале хода, длительность в конце,
    /// Воля сокращает), даун с окном на спасение (стабилизация Медициной), смерть
    /// насовсем. Враги симметричны — действуют тем же API.
    /// </summary>
    public sealed class CombatState
    {
        public GridMap Map { get; }
        public BalanceConfig Balance { get; }

        private readonly IRng _rng;
        private readonly List<CombatUnit> _units = new List<CombatUnit>();
        private readonly Dictionary<string, CombatUnit> _byId = new Dictionary<string, CombatUnit>();
        private readonly List<string> _log = new List<string>();
        private TurnSystem _turns;

        public IReadOnlyList<CombatUnit> Units => _units;
        public IReadOnlyList<string> Log => _log;
        public CombatOutcome Outcome { get; private set; } = CombatOutcome.Ongoing;
        public CombatUnit Current => _turns?.Current;
        public int Round => _turns?.Round ?? 0;
        public IReadOnlyList<CombatUnit> TurnOrder => _turns?.Order;

        public CombatState(GridMap map, BalanceConfig balance, IRng rng)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public CombatUnit GetUnit(string id) => id != null && _byId.TryGetValue(id, out var u) ? u : null;

        // ---- Сборка боя ----
        public void AddUnit(CombatUnit unit, GridPos pos)
        {
            if (unit == null || _byId.ContainsKey(unit.Id)) return;
            if (!Map.IsFree(pos)) throw new InvalidOperationException($"Тайл {pos} занят или непроходим");
            unit.Pos = pos;
            Map.SetOccupant(pos, unit.Id);
            _units.Add(unit);
            _byId.Add(unit.Id, unit);
        }

        /// <summary>Старт боя: строит очередь инициативы и начинает первый ход.</summary>
        public void Begin()
        {
            _turns = new TurnSystem(_units);
            AddLog($"=== БОЙ НАЧАЛСЯ (раунд 1) ===");
            if (!BeginTurn(_turns.Current))
                AdvanceUntilActorReady();
        }

        // ---- Действия текущего юнита ----
        /// <summary>Движение в достижимый тайл; цена за тайл растёт под Подавлением.</summary>
        public CombatActionResult Move(GridPos dest)
        {
            var unit = ActiveCurrentOrNull();
            if (unit == null) return CombatActionResult.InvalidAction;

            var reachable = ReachableFor(unit);
            if (!reachable.TryGetValue(dest, out int cost)) return CombatActionResult.NotReachable;

            Map.ClearOccupant(unit.Pos);
            unit.Pos = dest;
            Map.SetOccupant(dest, unit.Id);
            unit.Ap -= cost;
            AddLog($"{unit.Profile.DisplayName} перемещается в {dest} (−{cost} AP)");
            return CombatActionResult.Success;
        }

        /// <summary>
        /// Атака текущим оружием. useStrike — потратить полный Strike-метр на
        /// гарантированное попадание (US-3.3). Полное попадание копит метр и
        /// триггерит проки оружия (Шред/статус); граза — только половинный урон.
        /// </summary>
        public CombatActionResult Attack(string targetId, bool useStrike = false)
        {
            var unit = ActiveCurrentOrNull();
            if (unit == null || unit.Weapon == null) return CombatActionResult.InvalidAction;

            var target = GetUnit(targetId);
            if (target == null || target.Side == unit.Side || target.LifeState != UnitLifeState.Active)
                return CombatActionResult.InvalidTarget;

            var w = unit.Weapon;
            if (unit.Ap < w.ApCost) return CombatActionResult.NotEnoughAp;

            int distance = GridPos.Chebyshev(unit.Pos, target.Pos);
            if (w.IsMelee && distance > w.OptimalRange) return CombatActionResult.OutOfRange;
            if (!w.IsMelee && !LineOfSight.HasLine(Map, unit.Pos, target.Pos)) return CombatActionResult.NoLineOfSight;

            if (useStrike && unit.StrikeMeter < Balance.StrikeGuaranteeAt) return CombatActionResult.InvalidAction;

            unit.Ap -= w.ApCost;

            HitOutcome outcome;
            int chance = HitChanceCalculator.Compute(unit, target, Map, Balance);
            if (useStrike)
            {
                unit.StrikeMeter = 0;
                outcome = HitOutcome.Hit;
                AddLog($"{unit.Profile.DisplayName} тратит Strike — гарантированный удар!");
            }
            else
            {
                outcome = HitChanceCalculator.Roll(chance, _rng, Balance);
            }

            switch (outcome)
            {
                case HitOutcome.Miss:
                    AddLog($"{unit.Profile.DisplayName} → {target.Profile.DisplayName}: промах ({chance}%)");
                    break;

                case HitOutcome.Graze:
                {
                    var dmg = DamageResolver.RollAttackDamage(unit, target, w, graze: true, _rng, Balance);
                    AddLog($"{unit.Profile.DisplayName} → {target.Profile.DisplayName}: граза, {dmg.Amount} урона ({chance}%)");
                    ApplyDamage(target, dmg.Amount);
                    break;
                }

                case HitOutcome.Hit:
                {
                    var dmg = DamageResolver.RollAttackDamage(unit, target, w, graze: false, _rng, Balance);
                    if (!useStrike) unit.StrikeMeter += Balance.StrikePerHit;
                    AddLog($"{unit.Profile.DisplayName} → {target.Profile.DisplayName}: " +
                           $"{(dmg.Crit ? "КРИТ, " : "")}{dmg.Amount} урона ({chance}%)");

                    if (w.ShredOnHit > 0)
                    {
                        target.ArmorShred += w.ShredOnHit;
                        AddLog($"  Шред: броня {target.Profile.DisplayName} −{w.ShredOnHit} (тек. {target.EffectiveArmor})");
                    }
                    if (w.StatusOnHit != StatusType.None && target.LifeState == UnitLifeState.Active)
                        ApplyStatus(target, w.StatusOnHit);

                    ApplyDamage(target, dmg.Amount);
                    break;
                }
            }
            return CombatActionResult.Success;
        }

        /// <summary>Стабилизация дауна союзника рядом (активка Медицины, US-3.11/4.1). Детерминирована.</summary>
        public CombatActionResult Stabilize(string targetId)
        {
            var unit = ActiveCurrentOrNull();
            if (unit == null) return CombatActionResult.InvalidAction;
            if (unit.Profile.MedicineSkill < 1) return CombatActionResult.InvalidAction;

            var target = GetUnit(targetId);
            if (target == null || target.Side != unit.Side || target.LifeState != UnitLifeState.Downed)
                return CombatActionResult.InvalidTarget;
            if (GridPos.Chebyshev(unit.Pos, target.Pos) > 1) return CombatActionResult.OutOfRange;
            if (unit.Ap < Balance.StabilizeApCost) return CombatActionResult.NotEnoughAp;

            unit.Ap -= Balance.StabilizeApCost;
            target.LifeState = UnitLifeState.Stabilized;
            Map.ClearOccupant(target.Pos);
            AddLog($"{unit.Profile.DisplayName} стабилизирует {target.Profile.DisplayName} — спасён, выбыл из боя");
            CheckOutcome();
            return CombatActionResult.Success;
        }

        /// <summary>Конец хода: тик длительностей статусов, переход к следующему действующему юниту.</summary>
        public CombatActionResult EndTurn()
        {
            if (Outcome != CombatOutcome.Ongoing || Current == null) return CombatActionResult.InvalidAction;
            TickStatusDurations(Current);
            AdvanceUntilActorReady();
            return CombatActionResult.Success;
        }

        // ---- Справки для UI/AI ----
        /// <summary>Достижимые тайлы текущего AP юнита (цена шага учитывает Подавление).</summary>
        public Dictionary<GridPos, int> ReachableFor(CombatUnit unit)
            => Pathfinder.Reachable(Map, unit.Pos, unit.Ap, MoveCostPerTile(unit));

        public int MoveCostPerTile(CombatUnit unit)
        {
            int baseCost = Balance.MoveApCostPerTile;
            return unit.HasStatus(StatusType.Suppressed)
                ? (int)Math.Ceiling(baseCost * Balance.SuppressionMoveCostMultiplier)
                : baseCost;
        }

        public int HitChancePreview(CombatUnit attacker, CombatUnit target)
            => HitChanceCalculator.Compute(attacker, target, Map, Balance);

        // ---- Внутренние правила ----
        private CombatUnit ActiveCurrentOrNull()
        {
            if (Outcome != CombatOutcome.Ongoing) return null;
            var u = Current;
            return u != null && u.IsActive ? u : null;
        }

        /// <summary>Наложение статуса: гарантированное; Воля (Resolve) сокращает длительность, мин 1. Повтор — освежает.</summary>
        public void ApplyStatus(CombatUnit target, StatusType type)
        {
            int baseDuration;
            int dot = 0;
            var dotType = DamageType.True;
            switch (type)
            {
                case StatusType.Suppressed:
                    baseDuration = 1; // до конца следующего хода цели
                    break;
                case StatusType.Bleeding:
                    baseDuration = Balance.DotDurationTurns;
                    dot = Balance.DotDamagePerTurn; // True-урон: броню и резисты не трогает
                    break;
                case StatusType.Burning:
                    baseDuration = Balance.DotDurationTurns;
                    dot = Balance.DotDamagePerTurn;
                    dotType = DamageType.Fire;
                    break;
                case StatusType.Poisoned:
                    baseDuration = Balance.DotDurationTurns;
                    dot = Balance.DotDamagePerTurn;
                    dotType = DamageType.Toxin;
                    break;
                default:
                    baseDuration = Balance.DotDurationTurns; // заглушка под итерацию способностей
                    break;
            }

            int reduction = Balance.ResolvePerStatusTurnReduction > 0
                ? target.Profile.Resolve / Balance.ResolvePerStatusTurnReduction
                : 0;
            int duration = Math.Max(1, baseDuration - reduction);

            var existing = target.GetStatus(type);
            if (existing != null)
                existing.RemainingTurns = Math.Max(existing.RemainingTurns, duration);
            else
                target.Statuses.Add(new StatusInstance(type, duration, dot, dotType));
            AddLog($"  {target.Profile.DisplayName} получает состояние {type} ({duration} х.)");
        }

        private void ApplyDamage(CombatUnit target, int amount)
        {
            if (amount <= 0 || !target.IsActive) return;
            target.Hp -= amount;
            if (target.Hp > 0) return;

            target.Hp = 0;
            if (target.Profile.CanBeDowned)
            {
                target.LifeState = UnitLifeState.Downed;
                target.DownWindowRemaining = Balance.DownWindowTurns;
                AddLog($"  {target.Profile.DisplayName} ПАДАЕТ! Окно на спасение: {target.DownWindowRemaining} х.");
            }
            else
            {
                Die(target);
            }
            CheckOutcome();
        }

        private void Die(CombatUnit unit)
        {
            unit.LifeState = UnitLifeState.Dead;
            Map.ClearOccupant(unit.Pos);
            AddLog($"  {unit.Profile.DisplayName} погибает.");
        }

        /// <summary>Начало хода юнита. true — юнит готов действовать (AP выданы).</summary>
        private bool BeginTurn(CombatUnit unit)
        {
            if (unit == null || Outcome != CombatOutcome.Ongoing) return false;

            switch (unit.LifeState)
            {
                case UnitLifeState.Dead:
                case UnitLifeState.Stabilized:
                    return false;

                case UnitLifeState.Downed:
                    unit.DownWindowRemaining--;
                    if (unit.DownWindowRemaining <= 0)
                    {
                        AddLog($"{unit.Profile.DisplayName}: окно спасения вышло…");
                        Die(unit);
                        CheckOutcome();
                    }
                    else
                    {
                        AddLog($"{unit.Profile.DisplayName} истекает кровью (окно: {unit.DownWindowRemaining} х.)");
                    }
                    return false;
            }

            unit.Ap = unit.Profile.MaxAp;

            // DoT тикают в начале хода носителя (Воля уже сократила длительность при наложении).
            for (int i = 0; i < unit.Statuses.Count && unit.IsActive; i++)
            {
                var s = unit.Statuses[i];
                if (s.DotDamagePerTurn <= 0) continue;
                int dmg = DamageResolver.DotTick(s.DotDamagePerTurn, s.DotType, unit);
                if (dmg > 0)
                {
                    AddLog($"{unit.Profile.DisplayName} страдает от {s.Type}: −{dmg} HP");
                    ApplyDamage(unit, dmg);
                }
            }
            return unit.IsActive;
        }

        /// <summary>Конец хода: длительности −1, истёкшие статусы снимаются.</summary>
        private void TickStatusDurations(CombatUnit unit)
        {
            for (int i = unit.Statuses.Count - 1; i >= 0; i--)
            {
                var s = unit.Statuses[i];
                s.RemainingTurns--;
                if (s.RemainingTurns <= 0)
                {
                    AddLog($"  {unit.Profile.DisplayName}: состояние {s.Type} спадает");
                    unit.Statuses.RemoveAt(i);
                }
            }
        }

        private void AdvanceUntilActorReady()
        {
            int safety = _units.Count * 2 + 2;
            while (safety-- > 0 && Outcome == CombatOutcome.Ongoing)
            {
                var next = _turns.Advance();
                if (BeginTurn(next)) return;
            }
        }

        private void CheckOutcome()
        {
            if (Outcome != CombatOutcome.Ongoing) return;

            bool enemyStanding = false, playerStanding = false;
            for (int i = 0; i < _units.Count; i++)
            {
                if (!_units[i].IsActive) continue;
                if (_units[i].Side == Side.Enemy) enemyStanding = true;
                else playerStanding = true;
            }

            if (!enemyStanding)
            {
                Outcome = CombatOutcome.Victory;
                AddLog("=== ПОБЕДА: враги выбыли ===");
            }
            else if (!playerStanding)
            {
                Outcome = CombatOutcome.Defeat;
                AddLog("=== ПОРАЖЕНИЕ: отряд не может продолжать бой ===");
            }
        }

        private void AddLog(string line) => _log.Add(line);
    }
}
