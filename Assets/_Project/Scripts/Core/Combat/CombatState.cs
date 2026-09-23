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
        InvalidAction = 6,
        OnCooldown = 7
    }

    public enum CombatOutcome
    {
        Ongoing = 0,
        Victory = 1, // все враги выбыли
        Defeat = 2   // не осталось действующих юнитов игрока
    }

    /// <summary>
    /// Оркестратор боя: грид + юниты + индивидуальная инициатива + действия текущего
    /// юнита (движение/атака/стабилизация/overwatch/конец хода). Правила Эпиков 3–4:
    /// пул AP, надёжный %, Strike-метр, статусы (DoT в начале хода, длительность в
    /// конце, Воля сокращает), overwatch (US-3.6), даун с окном на спасение
    /// (стабилизация Медициной), смерть насовсем. Враги симметричны — действуют тем же API.
    /// </summary>
    public sealed class CombatState
    {
        public GridMap Map { get; }
        public BalanceConfig Balance { get; }

        private readonly IRng _rng;
        private readonly List<CombatUnit> _units = new List<CombatUnit>();
        private readonly Dictionary<string, CombatUnit> _byId = new Dictionary<string, CombatUnit>();
        private readonly List<string> _log = new List<string>();
        private readonly List<Trap> _traps = new List<Trap>();
        private readonly List<AttackRecord> _attacks = new List<AttackRecord>();
        private TurnSystem _turns;

        public IReadOnlyList<Trap> Traps => _traps;

        public IReadOnlyList<CombatUnit> Units => _units;
        public IReadOnlyList<string> Log => _log;

        /// <summary>Структурированная история атак — телеметрия честности RNG (риск R11 GDD):
        /// показанный игроку шанс против фактического исхода. Не сериализуется.</summary>
        public IReadOnlyList<AttackRecord> Attacks => _attacks;
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

            unit.Ap -= cost;
            AddLog($"{unit.Profile.DisplayName} перемещается в {dest} (−{cost} AP)");

            // Путь проходится по клеткам, а не прыжком: дозор противника обязан
            // видеть сам путь (US-3.6). Реакция может уронить идущего — тогда он
            // остаётся там, где упал. Ловушка, как и раньше, — только в точке
            // прибытия, и тем же порядком, что у рывка и перестановки (PlaceUnitAt):
            // сначала выстрел из дозора, потом ловушка — если дошёл на ногах.
            var path = Pathfinder.Path(Map, unit.Pos, dest);
            for (int i = 0; i < path.Count; i++)
            {
                StepTo(unit, path[i]);
                ReactToMovement(unit);
                if (!unit.IsActive || Outcome != CombatOutcome.Ongoing) return CombatActionResult.Success;
            }
            TriggerTrapAt(unit);
            return CombatActionResult.Success;
        }

        /// <summary>
        /// Overwatch (US-3.6): юнит платит цену выстрела оружием заранее и держит
        /// сектор — конус от своей клетки в сторону aim — до начала своего
        /// следующего хода. Вход в дозор завершает ход: выстрел оплачен из того же
        /// пула, что и всё остальное, поэтому «сначала походить, потом залечь» можно,
        /// а получить выстрел дважды — нет.
        ///
        /// Срабатывает ОДИН раз: на первом перемещении противника в сектор — шаг
        /// хода, приземление рывка, перестановка по приказу (вход или движение
        /// внутри), — в пределах обзора (дальнее оружие) или контакта (ближнее).
        /// Без двойного профита: резерв не возвращается, если никто не пришёл;
        /// выстрел не копит Strike-метр и не может быть Strike; точность — со
        /// штрафом навскидку. Проки оружия (шред, статус на попадании) работают:
        /// это свойства самого выстрела, а не второй профит сверх него.
        /// </summary>
        public CombatActionResult Overwatch(GridPos aim)
        {
            var unit = ActiveCurrentOrNull();
            if (unit == null || unit.Weapon == null) return CombatActionResult.InvalidAction;
            if (!Map.InBounds(aim) || aim == unit.Pos) return CombatActionResult.InvalidTarget;

            int reserve = unit.Weapon.ApCost;
            if (unit.Ap < reserve) return CombatActionResult.NotEnoughAp;

            unit.Ap -= reserve;
            unit.Overwatch = new OverwatchStance(unit.Pos, aim, reserve);
            AddLog($"{unit.Profile.DisplayName} берёт сектор под прицел в сторону {aim} " +
                   $"(резерв −{reserve} AP, до своего следующего хода)");
            EndTurn();
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

            if (useStrike)
            {
                unit.StrikeMeter = 0;
                AddLog($"{unit.Profile.DisplayName} тратит Strike — гарантированный удар!");
                ExecuteAttackRoll(unit, target, w, accuracyBonus: 0, forceHit: true, allowStrikeGain: false);
            }
            else
            {
                ExecuteAttackRoll(unit, target, w, accuracyBonus: 0, forceHit: false, allowStrikeGain: true);
            }
            return CombatActionResult.Success;
        }

        /// <summary>Один удар оружием: ролл → урон → проки → Strike. Общий для атаки и способностей.</summary>
        private void ExecuteAttackRoll(CombatUnit unit, CombatUnit target, WeaponDefinition w,
                                       int accuracyBonus, bool forceHit, bool allowStrikeGain)
        {
            int chance = HitChanceCalculator.Compute(unit, target, Map, Balance, accuracyBonus);
            var outcome = forceHit ? HitOutcome.Hit : HitChanceCalculator.Roll(chance, _rng, Balance);
            int recDamage = 0;
            bool recCrit = false;

            switch (outcome)
            {
                case HitOutcome.Miss:
                    AddLog($"{unit.Profile.DisplayName} → {target.Profile.DisplayName}: промах ({chance}%)");
                    break;

                case HitOutcome.Graze:
                {
                    var dmg = DamageResolver.RollAttackDamage(unit, target, w, graze: true, _rng, Balance);
                    AddLog($"{unit.Profile.DisplayName} → {target.Profile.DisplayName}: граза, {dmg.Amount} урона ({chance}%)");
                    recDamage = dmg.Amount;
                    recCrit = dmg.Crit;
                    ApplyDamage(target, dmg.Amount);
                    break;
                }

                case HitOutcome.Hit:
                {
                    var dmg = DamageResolver.RollAttackDamage(unit, target, w, graze: false, _rng, Balance);
                    if (allowStrikeGain) unit.StrikeMeter += Balance.StrikePerHit;
                    AddLog($"{unit.Profile.DisplayName} → {target.Profile.DisplayName}: " +
                           $"{(dmg.Crit ? "КРИТ, " : "")}{dmg.Amount} урона ({chance}%)");
                    recDamage = dmg.Amount;
                    recCrit = dmg.Crit;

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

            _attacks.Add(new AttackRecord(Round, unit.Side, unit.Id, target.Id,
                chance, outcome, recCrit, recDamage, forceHit));
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

        // ---- Способности (US-3.9/3.11) ----
        /// <summary>
        /// Применение способности: гейт скила решён при сборке юнита; здесь — КД,
        /// AP, цель/дальность/LOS и преваляция спец-эффектов ДО списания AP.
        /// targetUnitId — цель-юнит; targetTile — точка (ловушка/перестановка).
        /// </summary>
        public CombatActionResult UseAbility(string abilityId, string targetUnitId = null, GridPos? targetTile = null)
        {
            var unit = ActiveCurrentOrNull();
            if (unit == null) return CombatActionResult.InvalidAction;

            var ability = unit.FindAbility(abilityId);
            if (ability == null) return CombatActionResult.InvalidAction;

            // Цель по типу таргетинга (как в Attack: сначала цель, потом КД/AP).
            CombatUnit target = null;
            switch (ability.Targeting)
            {
                case AbilityTarget.Self:
                    target = unit;
                    break;
                case AbilityTarget.Ally:
                    target = GetUnit(targetUnitId);
                    if (target == null || target == unit || target.Side != unit.Side || !target.IsActive)
                        return CombatActionResult.InvalidTarget;
                    break;
                case AbilityTarget.AllyOrSelf:
                    target = targetUnitId == null ? unit : GetUnit(targetUnitId);
                    if (target == null || target.Side != unit.Side || !target.IsActive)
                        return CombatActionResult.InvalidTarget;
                    break;
                case AbilityTarget.Enemy:
                    target = GetUnit(targetUnitId);
                    if (target == null || target.Side == unit.Side || !target.IsActive)
                        return CombatActionResult.InvalidTarget;
                    break;
                case AbilityTarget.Tile:
                    if (!targetTile.HasValue || !Map.InBounds(targetTile.Value))
                        return CombatActionResult.InvalidTarget;
                    break;
            }

            if (unit.CooldownRemaining(abilityId) > 0) return CombatActionResult.OnCooldown;
            if (unit.Ap < ability.ApCost) return CombatActionResult.NotEnoughAp;

            // Дальность и LOS (на себя — не проверяются).
            bool selfTarget = ability.Targeting != AbilityTarget.Tile && target == unit;
            if (!selfTarget)
            {
                GridPos aim = ability.Targeting == AbilityTarget.Tile ? targetTile.Value : target.Pos;
                if (GridPos.Chebyshev(unit.Pos, aim) > ability.Range) return CombatActionResult.OutOfRange;
                if (ability.RequiresLineOfSight && !LineOfSight.HasLine(Map, unit.Pos, aim))
                    return CombatActionResult.NoLineOfSight;
            }

            // Преваляция спец-эффектов до списания AP.
            GridPos? lungeDest = null;
            for (int i = 0; i < ability.Effects.Count; i++)
            {
                var fx = ability.Effects[i];
                if (fx.Kind == AbilityEffectKind.LungeToTarget)
                {
                    lungeDest = FindLungeLanding(unit, target);
                    if (!lungeDest.HasValue) return CombatActionResult.NotReachable;
                }
                else if (fx.Kind == AbilityEffectKind.RepositionTarget)
                {
                    if (!targetTile.HasValue || !Map.IsFree(targetTile.Value)
                        || GridPos.Chebyshev(target.Pos, targetTile.Value) > fx.Amount)
                        return CombatActionResult.NotReachable;
                }
                else if (fx.Kind == AbilityEffectKind.PlaceTrap)
                {
                    if (!targetTile.HasValue || !Map.IsFree(targetTile.Value) || TrapAt(targetTile.Value) != null)
                        return CombatActionResult.InvalidTarget;
                }
                else if (fx.Kind == AbilityEffectKind.HackRobot)
                {
                    // Взлом берёт только роботов (US-3.11) — валидируем ДО оплаты AP.
                    if (target == null || target.Profile.Family != EnemyFamily.Robot)
                        return CombatActionResult.InvalidTarget;
                }
            }

            // Мили-оружие бьёт только из контакта (симметрия с Attack); «Рывок»
            // учитывается — он сперва переставляет юнита вплотную (lungeDest).
            if (unit.Weapon != null && unit.Weapon.IsMelee && target != null && target.Side != unit.Side)
            {
                bool hasWeaponAttack = false;
                for (int i = 0; i < ability.Effects.Count; i++)
                    if (ability.Effects[i].Kind == AbilityEffectKind.WeaponAttack) { hasWeaponAttack = true; break; }
                if (hasWeaponAttack)
                {
                    var attackFrom = lungeDest ?? unit.Pos;
                    if (GridPos.Chebyshev(attackFrom, target.Pos) > unit.Weapon.OptimalRange)
                        return CombatActionResult.OutOfRange;
                }
            }

            unit.Ap -= ability.ApCost;
            unit.SetCooldown(ability.Id, ability.CooldownTurns);
            AddLog($"{unit.Profile.DisplayName} применяет «{ability.DisplayName}»");

            foreach (var fx in ability.Effects)
            {
                // Исполнителя может уронить дозор посреди способности (после рывка) —
                // лежащий дальше не бьёт.
                if (Outcome != CombatOutcome.Ongoing || !unit.IsActive) break;
                switch (fx.Kind)
                {
                    case AbilityEffectKind.WeaponAttack:
                        if (unit.Weapon != null && target != null && target.Side != unit.Side && target.IsActive)
                            ExecuteAttackRoll(unit, target, unit.Weapon, fx.AccuracyBonus, forceHit: false, allowStrikeGain: true);
                        break;

                    case AbilityEffectKind.FlatDamage:
                        if (target != null && target.IsActive)
                        {
                            int dmg = DamageResolver.FlatDamage(fx.Amount, fx.Damage, target);
                            AddLog($"  {target.Profile.DisplayName}: −{dmg} HP ({fx.Damage})");
                            ApplyDamage(target, dmg);
                        }
                        break;

                    case AbilityEffectKind.ApplyStatus:
                        if (target != null && target.IsActive && fx.Status != StatusType.None)
                            ApplyStatus(target, fx.Status);
                        break;

                    case AbilityEffectKind.RemoveStatus:
                        if (target != null && fx.Status != StatusType.None)
                        {
                            var s = target.GetStatus(fx.Status);
                            if (s != null)
                            {
                                target.Statuses.Remove(s);
                                AddLog($"  {target.Profile.DisplayName}: снято состояние {fx.Status}");
                            }
                        }
                        break;

                    case AbilityEffectKind.Shred:
                        if (target != null && target.IsActive && fx.Amount > 0)
                        {
                            target.ArmorShred += fx.Amount;
                            AddLog($"  Шред: броня {target.Profile.DisplayName} −{fx.Amount} (тек. {target.EffectiveArmor})");
                        }
                        break;

                    case AbilityEffectKind.Heal:
                        if (target != null && target.IsActive)
                        {
                            int healed = Math.Min(fx.Amount, target.Profile.MaxHp - target.Hp);
                            if (healed > 0)
                            {
                                target.Hp += healed;
                                AddLog($"  {target.Profile.DisplayName}: +{healed} HP ({target.Hp}/{target.Profile.MaxHp})");
                            }
                        }
                        break;

                    case AbilityEffectKind.GrantAp:
                        if (target != null && target.IsActive && fx.Amount > 0)
                        {
                            target.Ap += fx.Amount;
                            AddLog($"  {target.Profile.DisplayName}: +{fx.Amount} AP");
                        }
                        break;

                    case AbilityEffectKind.LungeToTarget:
                        if (lungeDest.HasValue)
                        {
                            AddLog($"  {unit.Profile.DisplayName} совершает рывок к {target.Profile.DisplayName}");
                            PlaceUnitAt(unit, lungeDest.Value); // дозор видит рывок (реакция внутри)
                        }
                        break;

                    case AbilityEffectKind.RepositionTarget:
                        if (target != null && target.IsActive && targetTile.HasValue && Map.IsFree(targetTile.Value))
                        {
                            AddLog($"  {target.Profile.DisplayName} перемещается в {targetTile.Value}");
                            PlaceUnitAt(target, targetTile.Value);
                        }
                        break;

                    case AbilityEffectKind.PlaceTrap:
                        if (targetTile.HasValue && Map.IsFree(targetTile.Value) && TrapAt(targetTile.Value) == null)
                        {
                            _traps.Add(new Trap
                            {
                                Pos = targetTile.Value, OwnerSide = unit.Side, Name = ability.DisplayName,
                                Damage = fx.Amount, DamageType = fx.Damage, StatusOnTrigger = fx.Status
                            });
                            AddLog($"  Ловушка установлена в {targetTile.Value}");
                        }
                        break;

                    case AbilityEffectKind.HackRobot:
                        // Симметрия бестиария (US-3.14): робот переманивается на сторону
                        // взломщика до конца боя (валидация «только робот» — до оплаты AP).
                        if (target != null && target.IsActive && target.Side != unit.Side
                            && target.Profile.Family == EnemyFamily.Robot)
                        {
                            target.Side = unit.Side;
                            BreakOverwatch(target, "перехвачен");
                            AddLog($"  {target.Profile.DisplayName} перехвачен — теперь дерётся за " +
                                   (unit.Side == Side.Player ? "отряд!" : "врага!"));
                            CheckOutcome(); // возможно, активных врагов не осталось
                        }
                        break;
                }
            }
            return CombatActionResult.Success;
        }

        public Trap TrapAt(GridPos pos)
        {
            for (int i = 0; i < _traps.Count; i++)
                if (_traps[i].Pos == pos) return _traps[i];
            return null;
        }

        /// <summary>Свободная клетка вплотную к цели рывка, ближайшая к атакующему (детерминированно).</summary>
        private GridPos? FindLungeLanding(CombatUnit unit, CombatUnit target)
        {
            GridPos? best = null;
            int bestDist = int.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var p = new GridPos(target.Pos.X + dx, target.Pos.Y + dy);
                    if (p == unit.Pos) return unit.Pos; // уже вплотную — рывок на месте
                    if (!Map.IsFree(p)) continue;
                    int d = GridPos.Chebyshev(unit.Pos, p);
                    if (d < bestDist) { bestDist = d; best = p; }
                }
            }
            return best;
        }

        /// <summary>
        /// Перемещение одним прыжком (рывок, перестановка): свой дозор теряется —
        /// сектор держится с конкретной клетки; чужой дозор видит приземление;
        /// ловушка срабатывает последней и только под тем, кто остался на ногах.
        /// Тот же порядок, что у последнего шага обычного хода (Move) — исход не
        /// зависит от того, КАК юнит попал на клетку.
        /// </summary>
        private void PlaceUnitAt(CombatUnit unit, GridPos dest)
        {
            BreakOverwatch(unit, "сбит с позиции");
            StepTo(unit, dest);
            ReactToMovement(unit);
            if (unit.IsActive && Outcome == CombatOutcome.Ongoing) TriggerTrapAt(unit);
        }

        /// <summary>Один шаг: только клетка и занятость, без ловушек и реакций.</summary>
        private void StepTo(CombatUnit unit, GridPos tile)
        {
            Map.ClearOccupant(unit.Pos);
            unit.Pos = tile;
            Map.SetOccupant(tile, unit.Id);
        }

        // ---- Overwatch (US-3.6) ----
        /// <summary>
        /// Реакция дозора на шаг mover. Дозорные перебираются в порядке добавления в
        /// бой — детерминированно; каждый стреляет не больше раза, и выстрел снимает
        /// его дозор ДО ролла. Если идущего уронили, остальные уже не стреляют.
        /// </summary>
        private void ReactToMovement(CombatUnit mover)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                if (!mover.IsActive || Outcome != CombatOutcome.Ongoing) return;

                var watcher = _units[i];
                if (watcher == mover || !watcher.IsActive || watcher.Side == mover.Side) continue;
                if (!OverwatchCovers(watcher, mover.Pos)) continue;

                watcher.Overwatch = null; // одно срабатывание
                AddLog($"{watcher.Profile.DisplayName} стреляет из дозора по {mover.Profile.DisplayName}!");
                ExecuteAttackRoll(watcher, mover, watcher.Weapon,
                    accuracyBonus: -Balance.OverwatchAccuracyPenalty, forceHit: false, allowStrikeGain: false);
            }
        }

        /// <summary>
        /// Накрывает ли дозор watcher клетку tile: сектор + обзор (дальнее оружие) или
        /// контакт (ближнее — «страж у двери»). Справка для UI и ИИ: подсветить сектор.
        /// </summary>
        public bool OverwatchCovers(CombatUnit watcher, GridPos tile)
        {
            var stance = watcher?.Overwatch;
            var w = watcher?.Weapon;
            if (stance == null || w == null) return false;
            if (!stance.Covers(tile, Balance.OverwatchConeSlopeNum, Balance.OverwatchConeSlopeDen)) return false;

            return w.IsMelee
                ? GridPos.Chebyshev(watcher.Pos, tile) <= w.OptimalRange
                : LineOfSight.HasLine(Map, watcher.Pos, tile);
        }

        /// <summary>Показанный шанс выстрела из дозора по цели там, где она стоит (со штрафом навскидку).</summary>
        public int OverwatchHitChancePreview(CombatUnit watcher, CombatUnit target)
            => HitChanceCalculator.Compute(watcher, target, Map, Balance, -Balance.OverwatchAccuracyPenalty);

        private void BreakOverwatch(CombatUnit unit, string why)
        {
            if (unit?.Overwatch == null) return;
            unit.Overwatch = null;
            AddLog($"  {unit.Profile.DisplayName} теряет дозор ({why})");
        }

        private void TriggerTrapAt(CombatUnit unit)
        {
            for (int i = 0; i < _traps.Count; i++)
            {
                var trap = _traps[i];
                if (trap.Pos != unit.Pos || trap.OwnerSide == unit.Side) continue;
                _traps.RemoveAt(i);
                AddLog($"  {unit.Profile.DisplayName} попадает в ловушку «{trap.Name}»!");
                if (trap.StatusOnTrigger != StatusType.None && unit.IsActive)
                    ApplyStatus(unit, trap.StatusOnTrigger);
                int dmg = DamageResolver.FlatDamage(trap.Damage, trap.DamageType, unit);
                if (dmg > 0)
                {
                    AddLog($"  Ловушка: −{dmg} HP");
                    ApplyDamage(unit, dmg);
                }
                return;
            }
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

        /// <summary>Показанный игроку шанс. accuracyBonus — бонус взведённой способности:
        /// превью обязано совпадать с фактическим роллом (телеграфия US-3.3, метрика R11).</summary>
        public int HitChancePreview(CombatUnit attacker, CombatUnit target, int accuracyBonus = 0)
            => HitChanceCalculator.Compute(attacker, target, Map, Balance, accuracyBonus);

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
                case StatusType.Stunned:
                    baseDuration = 1; // снимется в начале хода цели, отняв AP (US-3.7)
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

            // Оглушённый и сбитый с ног сектор не держат (Подавление — держат, со штрафом к точности).
            if (type == StatusType.Stunned) BreakOverwatch(target, "оглушён");
            else if (type == StatusType.KnockedDown) BreakOverwatch(target, "сбит с ног");
        }

        private void ApplyDamage(CombatUnit target, int amount)
        {
            if (amount <= 0 || !target.IsActive) return;
            target.Hp -= amount;
            if (target.Hp > 0) return;

            target.Hp = 0;
            BreakOverwatch(target, "выбыл");
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

            // Дозор держится «до следующего хода юнита» (US-3.6): не сработал — сгорел
            // вместе с резервом; новый ход начинается с полного пула, а не с пула + резерв.
            if (unit.Overwatch != null)
            {
                unit.Overwatch = null;
                AddLog($"{unit.Profile.DisplayName} снимает дозор — никто не вошёл в сектор");
            }

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
                        if (unit.Profile.ProtectedFromDeath)
                        {
                            // Сюжетная защита протагониста (US-4.4): жив, но выбыл из боя.
                            unit.LifeState = UnitLifeState.Stabilized;
                            Map.ClearOccupant(unit.Pos);
                            AddLog($"  {unit.Profile.DisplayName} теряет сознание, но выживает.");
                        }
                        else
                        {
                            Die(unit);
                        }
                        CheckOutcome();
                    }
                    else
                    {
                        AddLog($"{unit.Profile.DisplayName} истекает кровью (окно: {unit.DownWindowRemaining} х.)");
                    }
                    return false;
            }

            unit.Ap = unit.Profile.MaxAp;
            unit.TickCooldowns();

            // Сбит с ног: подняться стоит AP (US-3.7), после чего юнит действует.
            var knocked = unit.GetStatus(StatusType.KnockedDown);
            if (knocked != null)
            {
                unit.Statuses.Remove(knocked);
                unit.Ap = Math.Max(0, unit.Ap - Balance.StandUpApCost);
                AddLog($"{unit.Profile.DisplayName} поднимается на ноги (−{Balance.StandUpApCost} AP)");
            }

            // Оглушение: потеря AP хода — юнит пропускает ход (US-3.7); статус расходуется.
            var stunned = unit.GetStatus(StatusType.Stunned);
            if (stunned != null)
            {
                unit.Statuses.Remove(stunned);
                unit.Ap = 0;
                AddLog($"{unit.Profile.DisplayName} оглушён — пропускает ход");
            }

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
