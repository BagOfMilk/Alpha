using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Balance;
using Game.Core.Randomness;

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
        Defeat = 2,  // не осталось действующих юнитов игрока
        Retreat = 3, // отряд отступил по команде (BattleResult.Outcome)
        Draw = 4     // достигнут предохранительный предел раундов — гарантия завершаемости
    }

    /// <summary>
    /// Оркестратор боя: грид + юниты + индивидуальная инициатива + действия текущего
    /// юнита (движение/атака/стабилизация/overwatch/конец хода). Правила: пул AP,
    /// надёжный %/порог (IHitRule — R1), Strike-метр, статусы (DoT в начале хода,
    /// длительность в конце, Resolve сокращает), overwatch, даун с окном на спасение
    /// (стабилизация Медициной), смерть насовсем. Враги симметричны — действуют тем
    /// же API. Roster не трогает НИКОГДА — итог читается через BattleResult.From,
    /// раны применяет D1 (RosterAdapter.Wound, Р5).
    /// </summary>
    public sealed class CombatState
    {
        public GridMap Map { get; }
        public BalanceConfig Balance { get; }

        private readonly IHitRule _hitRule;
        private readonly IDiceRoller _roller;
        private readonly List<CombatUnit> _units = new List<CombatUnit>();
        private readonly Dictionary<string, CombatUnit> _byId = new Dictionary<string, CombatUnit>();
        private readonly List<string> _log = new List<string>();
        private readonly List<CombatLogEntry> _journal = new List<CombatLogEntry>();
        private readonly List<Trap> _traps = new List<Trap>();
        private readonly List<AttackRecord> _attacks = new List<AttackRecord>();
        private TurnSystem _turns;

        public IReadOnlyList<Trap> Traps => _traps;
        public IReadOnlyList<CombatUnit> Units => _units;

        /// <summary>
        /// Внутренний диагностический трейс (свободный текст по-русски, с
        /// сырыми именами enum) — НЕ то, что видит игрок. <c>internal</c>
        /// намеренно, как числа дневного отчёта (инвариант 3): Game.Gameplay
        /// физически не может его прочитать, и показать его игроку нельзя даже
        /// по ошибке. Игроку идёт <see cref="Journal"/> — те же события
        /// ключами (R7), строка к строке.
        /// </summary>
        internal IReadOnlyList<string> Log => _log;

        /// <summary>
        /// Журнал боя для игрока: ключ + аргументы на каждую строку трейса
        /// <see cref="Log"/>, в том же порядке (оба пишет только <see cref="Record"/>).
        /// Источник BattleView.Log; слова к ключам подставляет Gameplay.
        /// </summary>
        public IReadOnlyList<CombatLogEntry> Journal => _journal;

        /// <summary>Структурированная история атак — телеметрия честности броска:
        /// показанное игроку число против фактического исхода. Не сериализуется.</summary>
        public IReadOnlyList<AttackRecord> Attacks => _attacks;
        public CombatOutcome Outcome { get; private set; } = CombatOutcome.Ongoing;
        public CombatUnit Current => _turns?.Current;
        public int Round => _turns?.Round ?? 0;
        public IReadOnlyList<CombatUnit> TurnOrder => _turns?.Order;

        /// <summary>Какое правило попадания в этом бою — тот же флаг, что несёт BattleView.IsHitRulePercent.</summary>
        public bool IsHitRulePercent => _hitRule is PercentRule;

        /// <summary>
        /// roller может быть null: ThresholdRule его не читает вовсе (R1 —
        /// полностью детерминированный режим не обязан иметь под собой кубик).
        /// PercentRule сам бросит ArgumentNullException при первом же Resolve,
        /// если roller не передан — раньше, чем ошибка расползётся по логике боя.
        /// </summary>
        public CombatState(GridMap map, BalanceConfig balance, IHitRule hitRule, IDiceRoller roller)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _hitRule = hitRule ?? throw new ArgumentNullException(nameof(hitRule));
            _roller = roller;
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
            Record(CombatLogKeys.Started, "=== БОЙ НАЧАЛСЯ (раунд 1) ===");
            if (!BeginTurn(_turns.Current))
                AdvanceUntilActorReady();
        }

        /// <summary>
        /// Отступление: отряд разрывает бой по команде (не поражение и не победа).
        /// Доступно, пока бой идёт — не привязано к чьему-то ходу, это решение
        /// уровня выше юнитов (BattleResult.Outcome = Retreat).
        /// </summary>
        public CombatActionResult Retreat()
        {
            if (Outcome != CombatOutcome.Ongoing) return CombatActionResult.InvalidAction;
            Outcome = CombatOutcome.Retreat;
            Record(CombatLogKeys.Retreat, "=== ОТСТУПЛЕНИЕ: отряд разрывает бой ===");
            return CombatActionResult.Success;
        }

        /// <summary>
        /// Безусловный внешний предохранитель (CombatAi.AutoResolve): переводит
        /// Ongoing в Draw независимо от Round/RoundCap. Нужен, потому что
        /// гарантия завершаемости автобоя не может зависеть от того, насколько
        /// щедрый turnBudget передал вызывающий — сам CombatState единственный,
        /// кто может честно закрыть бой без «наполовину сыгранного» результата.
        /// На бой, уже завершённый чем угодно другим (Victory/Defeat/Retreat/
        /// собственный Draw по RoundCap), не действует.
        /// </summary>
        public CombatActionResult ForceDraw(string reason)
        {
            if (Outcome != CombatOutcome.Ongoing) return CombatActionResult.InvalidAction;
            Outcome = CombatOutcome.Draw;
            // Причина — служебный текст вызывающего (CombatAi), игроку не идёт.
            Record(CombatLogKeys.DrawForced, $"=== НИЧЬЯ: {reason} ===");
            return CombatActionResult.Success;
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
            Record(CombatLogKeys.Move, $"{unit.Profile.DisplayName} перемещается в {dest} (−{cost} AP)",
                "unitId", unit.Id, "ap", I(cost), "x", I(dest.X), "y", I(dest.Y));

            // Путь проходится по клеткам, а не прыжком: дозор противника обязан
            // видеть сам путь. Реакция может уронить идущего — тогда он
            // остаётся там, где упал. Ловушка — только в точке прибытия, тем
            // же порядком, что у рывка и перестановки (PlaceUnitAt): сначала
            // выстрел из дозора, потом ловушка — если дошёл на ногах.
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
        /// Overwatch: юнит платит цену выстрела оружием заранее и держит
        /// сектор — конус от своей клетки в сторону aim — до начала своего
        /// следующего хода. Вход в дозор завершает ход: выстрел оплачен из того
        /// же пула, что и всё остальное, поэтому «сначала походить, потом
        /// залечь» можно, а получить выстрел дважды — нет.
        ///
        /// Срабатывает ОДИН раз: на первом перемещении противника в сектор —
        /// шаг хода, приземление рывка, перестановка по приказу, — в пределах
        /// обзора (дальнее оружие) или контакта (ближнее). Без двойного
        /// профита: резерв не возвращается, если никто не пришёл; выстрел не
        /// копит Strike-метр и не может быть Strike; точность — со штрафом
        /// навскидку. Проки оружия работают: это свойства самого выстрела.
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
            Record(CombatLogKeys.OverwatchSet,
                $"{unit.Profile.DisplayName} берёт сектор под прицел в сторону {aim} " +
                $"(резерв −{reserve} AP, до своего следующего хода)",
                "unitId", unit.Id, "ap", I(reserve), "x", I(aim.X), "y", I(aim.Y));
            EndTurn();
            return CombatActionResult.Success;
        }

        /// <summary>
        /// Атака текущим оружием. useStrike — потратить полный Strike-метр на
        /// гарантированное попадание. Полное попадание/крит копит метр и
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

            if (useStrike && unit.StrikeMeter < Balance.Combat.StrikeGuaranteeAt) return CombatActionResult.InvalidAction;

            unit.Ap -= w.ApCost;

            if (useStrike)
            {
                unit.StrikeMeter = 0;
                Record(CombatLogKeys.Strike, $"{unit.Profile.DisplayName} тратит Strike — гарантированный удар!",
                    "unitId", unit.Id);
                ExecuteAttackRoll(unit, target, w, accuracyBonus: 0, forceHit: true, allowStrikeGain: false);
            }
            else
            {
                ExecuteAttackRoll(unit, target, w, accuracyBonus: 0, forceHit: false, allowStrikeGain: true);
            }
            return CombatActionResult.Success;
        }

        /// <summary>Один удар оружием: попадание через IHitRule → урон через DamageResolver → проки → Strike.</summary>
        private void ExecuteAttackRoll(CombatUnit unit, CombatUnit target, WeaponDefinition w,
                                       int accuracyBonus, bool forceHit, bool allowStrikeGain, bool isReaction = false)
        {
            int shown = HitChanceCalculator.Compute(unit, target, Map, Balance, accuracyBonus);
            var outcome = forceHit ? AttackOutcome.Hit : _hitRule.Resolve(unit, target, shown, _roller);
            var dmg = DamageResolver.RollAttackDamage(unit, target, w, outcome, _roller, !IsHitRulePercent, Balance);
            string attackKey = CombatLogKeys.Attack(outcome);

            switch (outcome)
            {
                case AttackOutcome.Miss:
                    Record(attackKey, $"{unit.Profile.DisplayName} → {target.Profile.DisplayName}: промах ({shown})",
                        "unitId", unit.Id, "targetId", target.Id, "chance", I(shown), "damage", I(0));
                    break;

                case AttackOutcome.Graze:
                    Record(attackKey, $"{unit.Profile.DisplayName} → {target.Profile.DisplayName}: граза, {dmg.Amount} урона ({shown})",
                        "unitId", unit.Id, "targetId", target.Id, "chance", I(shown), "damage", I(dmg.Amount));
                    ApplyDamage(target, dmg.Amount);
                    break;

                case AttackOutcome.Hit:
                case AttackOutcome.Crit:
                    if (allowStrikeGain) unit.StrikeMeter += Balance.Combat.StrikePerHit;
                    Record(attackKey, $"{unit.Profile.DisplayName} → {target.Profile.DisplayName}: " +
                           $"{(outcome == AttackOutcome.Crit ? "КРИТ, " : "")}{dmg.Amount} урона ({shown})",
                        "unitId", unit.Id, "targetId", target.Id, "chance", I(shown), "damage", I(dmg.Amount));

                    if (w.ShredOnHit > 0)
                    {
                        target.ArmorShred += w.ShredOnHit;
                        RecordShred(target, w.ShredOnHit);
                    }
                    if (w.StatusOnHit != StatusType.None && target.LifeState == UnitLifeState.Active)
                        ApplyStatus(target, w.StatusOnHit);

                    ApplyDamage(target, dmg.Amount);
                    break;
            }

            _attacks.Add(new AttackRecord(Round, unit.Side, unit.Id, target.Id, shown, outcome, dmg.Amount, forceHit, isReaction));
        }

        /// <summary>Стабилизация дауна союзника рядом (активка Медицины). Детерминирована.</summary>
        public CombatActionResult Stabilize(string targetId)
        {
            var unit = ActiveCurrentOrNull();
            if (unit == null) return CombatActionResult.InvalidAction;
            if (unit.Profile.MedicineSkill < 1) return CombatActionResult.InvalidAction;

            var target = GetUnit(targetId);
            if (target == null || target.Side != unit.Side || target.LifeState != UnitLifeState.Downed)
                return CombatActionResult.InvalidTarget;
            if (GridPos.Chebyshev(unit.Pos, target.Pos) > 1) return CombatActionResult.OutOfRange;
            if (unit.Ap < Balance.Combat.StabilizeApCost) return CombatActionResult.NotEnoughAp;

            unit.Ap -= Balance.Combat.StabilizeApCost;
            target.LifeState = UnitLifeState.Stabilized;
            Map.ClearOccupant(target.Pos);
            Record(CombatLogKeys.Stabilize,
                $"{unit.Profile.DisplayName} стабилизирует {target.Profile.DisplayName} — спасён, выбыл из боя",
                "unitId", unit.Id, "targetId", target.Id);
            CheckOutcome();
            return CombatActionResult.Success;
        }

        // ---- Способности ----
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

            bool selfTarget = ability.Targeting != AbilityTarget.Tile && target == unit;
            if (!selfTarget)
            {
                GridPos aim = ability.Targeting == AbilityTarget.Tile ? targetTile.Value : target.Pos;
                if (GridPos.Chebyshev(unit.Pos, aim) > ability.Range) return CombatActionResult.OutOfRange;
                if (ability.RequiresLineOfSight && !LineOfSight.HasLine(Map, unit.Pos, aim))
                    return CombatActionResult.NoLineOfSight;
            }

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
            Record(CombatLogKeys.Ability, $"{unit.Profile.DisplayName} применяет «{ability.DisplayName}»",
                "unitId", unit.Id, "abilityId", ability.Id);

            foreach (var fx in ability.Effects)
            {
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
                            Record(CombatLogKeys.Damage, $"  {target.Profile.DisplayName}: −{dmg} HP ({fx.Damage})",
                                "unitId", target.Id, "damage", I(dmg), "damageType", CombatLogKeys.DamageTypeId(fx.Damage));
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
                                Record(CombatLogKeys.StatusRemoved, $"  {target.Profile.DisplayName}: снято состояние {fx.Status}",
                                    "unitId", target.Id, "status", CombatLogKeys.StatusId(fx.Status));
                            }
                        }
                        break;

                    case AbilityEffectKind.Shred:
                        if (target != null && target.IsActive && fx.Amount > 0)
                        {
                            target.ArmorShred += fx.Amount;
                            RecordShred(target, fx.Amount);
                        }
                        break;

                    case AbilityEffectKind.Heal:
                        if (target != null && target.IsActive)
                        {
                            int healed = Math.Min(fx.Amount, target.Profile.MaxHp - target.Hp);
                            if (healed > 0)
                            {
                                target.Hp += healed;
                                Record(CombatLogKeys.Heal, $"  {target.Profile.DisplayName}: +{healed} HP ({target.Hp}/{target.Profile.MaxHp})",
                                    "unitId", target.Id, "amount", I(healed), "hp", I(target.Hp), "hpMax", I(target.Profile.MaxHp));
                            }
                        }
                        break;

                    case AbilityEffectKind.GrantAp:
                        if (target != null && target.IsActive && fx.Amount > 0)
                        {
                            target.Ap += fx.Amount;
                            Record(CombatLogKeys.ApGranted, $"  {target.Profile.DisplayName}: +{fx.Amount} AP",
                                "unitId", target.Id, "amount", I(fx.Amount));
                        }
                        break;

                    case AbilityEffectKind.LungeToTarget:
                        if (lungeDest.HasValue)
                        {
                            Record(CombatLogKeys.Lunge, $"  {unit.Profile.DisplayName} совершает рывок к {target.Profile.DisplayName}",
                                "unitId", unit.Id, "targetId", target.Id);
                            PlaceUnitAt(unit, lungeDest.Value);
                        }
                        break;

                    case AbilityEffectKind.RepositionTarget:
                        if (target != null && target.IsActive && targetTile.HasValue && Map.IsFree(targetTile.Value))
                        {
                            Record(CombatLogKeys.Repositioned, $"  {target.Profile.DisplayName} перемещается в {targetTile.Value}",
                                "unitId", target.Id, "targetId", unit.Id, "x", I(targetTile.Value.X), "y", I(targetTile.Value.Y));
                            PlaceUnitAt(target, targetTile.Value);
                        }
                        break;

                    case AbilityEffectKind.PlaceTrap:
                        if (targetTile.HasValue && Map.IsFree(targetTile.Value) && TrapAt(targetTile.Value) == null)
                        {
                            _traps.Add(new Trap
                            {
                                Pos = targetTile.Value, OwnerSide = unit.Side, Name = ability.DisplayName,
                                AbilityId = ability.Id,
                                Damage = fx.Amount, DamageType = fx.Damage, StatusOnTrigger = fx.Status
                            });
                            Record(CombatLogKeys.TrapPlaced, $"  Ловушка установлена в {targetTile.Value}",
                                "unitId", unit.Id, "abilityId", ability.Id,
                                "x", I(targetTile.Value.X), "y", I(targetTile.Value.Y));
                        }
                        break;

                    case AbilityEffectKind.HackRobot:
                        if (target != null && target.IsActive && target.Side != unit.Side
                            && target.Profile.Family == EnemyFamily.Robot)
                        {
                            target.Side = unit.Side;
                            BreakOverwatch(target, CombatLogKeys.OverwatchLostHacked, "перехвачен");
                            Record(unit.Side == Side.Player ? CombatLogKeys.HackedToPlayer : CombatLogKeys.HackedToEnemy,
                                $"  {target.Profile.DisplayName} перехвачен — теперь дерётся за " +
                                (unit.Side == Side.Player ? "отряд!" : "врага!"),
                                "unitId", unit.Id, "targetId", target.Id);
                            CheckOutcome();
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
                    if (p == unit.Pos) return unit.Pos;
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
        /// Тот же порядок, что у последнего шага обычного хода (Move).
        /// </summary>
        private void PlaceUnitAt(CombatUnit unit, GridPos dest)
        {
            BreakOverwatch(unit, CombatLogKeys.OverwatchLostDisplaced, "сбит с позиции");
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

        // ---- Overwatch ----
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
                Record(CombatLogKeys.OverwatchFired, $"{watcher.Profile.DisplayName} стреляет из дозора по {mover.Profile.DisplayName}!",
                    "unitId", watcher.Id, "targetId", mover.Id);
                ExecuteAttackRoll(watcher, mover, watcher.Weapon,
                    accuracyBonus: -Balance.Combat.OverwatchAccuracyPenalty, forceHit: false, allowStrikeGain: false, isReaction: true);
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
            if (!stance.Covers(tile, Balance.Combat.OverwatchConeSlopeNum, Balance.Combat.OverwatchConeSlopeDen)) return false;

            return w.IsMelee
                ? GridPos.Chebyshev(watcher.Pos, tile) <= w.OptimalRange
                : LineOfSight.HasLine(Map, watcher.Pos, tile);
        }

        /// <summary>Показанное число выстрела из дозора по цели там, где она стоит (со штрафом навскидку).</summary>
        public int OverwatchHitChancePreview(CombatUnit watcher, CombatUnit target)
            => HitChanceCalculator.Compute(watcher, target, Map, Balance, -Balance.Combat.OverwatchAccuracyPenalty);

        /// <summary>key — причина для журнала (CombatLogKeys.OverwatchLost*), why — она же для трейса.</summary>
        private void BreakOverwatch(CombatUnit unit, string key, string why)
        {
            if (unit?.Overwatch == null) return;
            unit.Overwatch = null;
            Record(key, $"  {unit.Profile.DisplayName} теряет дозор ({why})", "unitId", unit.Id);
        }

        private void TriggerTrapAt(CombatUnit unit)
        {
            for (int i = 0; i < _traps.Count; i++)
            {
                var trap = _traps[i];
                if (trap.Pos != unit.Pos || trap.OwnerSide == unit.Side) continue;
                _traps.RemoveAt(i);
                Record(CombatLogKeys.TrapTriggered, $"  {unit.Profile.DisplayName} попадает в ловушку «{trap.Name}»!",
                    "unitId", unit.Id, "abilityId", trap.AbilityId);
                if (trap.StatusOnTrigger != StatusType.None && unit.IsActive)
                    ApplyStatus(unit, trap.StatusOnTrigger);
                int dmg = DamageResolver.FlatDamage(trap.Damage, trap.DamageType, unit);
                if (dmg > 0)
                {
                    Record(CombatLogKeys.TrapDamage, $"  Ловушка: −{dmg} HP",
                        "unitId", unit.Id, "damage", I(dmg), "damageType", CombatLogKeys.DamageTypeId(trap.DamageType));
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
        /// <summary>Достижимые тайлы текущего AP юнита (цена шага — своя у юнита, учитывает Подавление).</summary>
        public Dictionary<GridPos, int> ReachableFor(CombatUnit unit)
            => Pathfinder.Reachable(Map, unit.Pos, unit.Ap, MoveCostPerTile(unit));

        /// <summary>
        /// Цена шага движения для ЭТОГО юнита — из его собственной производной
        /// MoveApPerTile (аудит G18), а не из одной глобальной константы баланса,
        /// как было в архиве: перк/трейт/шрам, меняющий MoveApPerTile, теперь
        /// реально что-то меняет в бою.
        /// </summary>
        public int MoveCostPerTile(CombatUnit unit)
        {
            int baseCost = Math.Max(1, unit.Profile.MoveApPerTile);
            return unit.HasStatus(StatusType.Suppressed)
                ? (int)Math.Ceiling(baseCost * Balance.Combat.SuppressionMoveCostMultiplier)
                : baseCost;
        }

        /// <summary>Показанное игроку число. accuracyBonus — бонус взведённой способности:
        /// превью обязано совпадать с фактическим роллом.</summary>
        public int HitChancePreview(CombatUnit attacker, CombatUnit target, int accuracyBonus = 0)
            => HitChanceCalculator.Compute(attacker, target, Map, Balance, accuracyBonus);

        // ---- Внутренние правила ----
        private CombatUnit ActiveCurrentOrNull()
        {
            if (Outcome != CombatOutcome.Ongoing) return null;
            var u = Current;
            return u != null && u.IsActive ? u : null;
        }

        /// <summary>Наложение статуса: гарантированное; Resolve (StatusDurationReduction) сокращает длительность, мин 1. Повтор — освежает.</summary>
        public void ApplyStatus(CombatUnit target, StatusType type)
        {
            // «Никакого состояния» наложить нельзя: все внутренние вызовы уже
            // фильтруют None, а у журнала для него нет токена (CombatLogKeys.StatusId).
            if (type == StatusType.None) return;

            int baseDuration;
            int dot = 0;
            var dotType = DamageType.True;
            switch (type)
            {
                case StatusType.Suppressed:
                    baseDuration = 1;
                    break;
                case StatusType.Stunned:
                    baseDuration = 1;
                    break;
                case StatusType.Bleeding:
                    baseDuration = Balance.Combat.DotDurationTurns;
                    dot = Balance.Combat.DotDamagePerTurn;
                    break;
                case StatusType.Burning:
                    baseDuration = Balance.Combat.DotDurationTurns;
                    dot = Balance.Combat.DotDamagePerTurn;
                    dotType = DamageType.Fire;
                    break;
                case StatusType.Poisoned:
                    baseDuration = Balance.Combat.DotDurationTurns;
                    dot = Balance.Combat.DotDamagePerTurn;
                    dotType = DamageType.Toxin;
                    break;
                default:
                    baseDuration = Balance.Combat.DotDurationTurns;
                    break;
            }

            int reduction = Balance.Combat.ResolvePerStatusTurnReduction > 0
                ? target.Profile.Resolve / Balance.Combat.ResolvePerStatusTurnReduction
                : 0;
            int duration = Math.Max(1, baseDuration - reduction);

            var existing = target.GetStatus(type);
            if (existing != null)
                existing.RemainingTurns = Math.Max(existing.RemainingTurns, duration);
            else
                target.Statuses.Add(new StatusInstance(type, duration, dot, dotType));
            Record(CombatLogKeys.StatusApplied, $"  {target.Profile.DisplayName} получает состояние {type} ({duration} х.)",
                "unitId", target.Id, "status", CombatLogKeys.StatusId(type), "turns", I(duration));

            if (type == StatusType.Stunned) BreakOverwatch(target, CombatLogKeys.OverwatchLostStunned, "оглушён");
            else if (type == StatusType.KnockedDown) BreakOverwatch(target, CombatLogKeys.OverwatchLostKnockedDown, "сбит с ног");
        }

        private void ApplyDamage(CombatUnit target, int amount)
        {
            if (amount <= 0 || !target.IsActive) return;
            target.Hp -= amount;
            if (target.Hp > 0) return;

            target.Hp = 0;
            BreakOverwatch(target, CombatLogKeys.OverwatchLostOut, "выбыл");
            if (target.Profile.CanBeDowned)
            {
                target.LifeState = UnitLifeState.Downed;
                target.DownWindowRemaining = Balance.Combat.DownWindowTurns;
                Record(CombatLogKeys.Downed, $"  {target.Profile.DisplayName} ПАДАЕТ! Окно на спасение: {target.DownWindowRemaining} х.",
                    "unitId", target.Id, "turns", I(target.DownWindowRemaining));
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
            Record(CombatLogKeys.Died, $"  {unit.Profile.DisplayName} погибает.", "unitId", unit.Id);
        }

        /// <summary>Начало хода юнита. true — юнит готов действовать (AP выданы).</summary>
        private bool BeginTurn(CombatUnit unit)
        {
            if (unit == null || Outcome != CombatOutcome.Ongoing) return false;

            if (unit.Overwatch != null)
            {
                unit.Overwatch = null;
                Record(CombatLogKeys.OverwatchExpired, $"{unit.Profile.DisplayName} снимает дозор — никто не вошёл в сектор",
                    "unitId", unit.Id);
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
                        Record(CombatLogKeys.WindowExpired, $"{unit.Profile.DisplayName}: окно спасения вышло…",
                            "unitId", unit.Id);
                        if (unit.Profile.ProtectedFromDeath)
                        {
                            unit.LifeState = UnitLifeState.Stabilized;
                            Map.ClearOccupant(unit.Pos);
                            Record(CombatLogKeys.Survived, $"  {unit.Profile.DisplayName} теряет сознание, но выживает.",
                                "unitId", unit.Id);
                        }
                        else
                        {
                            Die(unit);
                        }
                        CheckOutcome();
                    }
                    else
                    {
                        Record(CombatLogKeys.BleedingOut, $"{unit.Profile.DisplayName} истекает кровью (окно: {unit.DownWindowRemaining} х.)",
                            "unitId", unit.Id, "turns", I(unit.DownWindowRemaining));
                    }
                    return false;
            }

            unit.Ap = unit.Profile.MaxAp;
            unit.TickCooldowns();

            var knocked = unit.GetStatus(StatusType.KnockedDown);
            if (knocked != null)
            {
                unit.Statuses.Remove(knocked);
                unit.Ap = Math.Max(0, unit.Ap - Balance.Combat.StandUpApCost);
                Record(CombatLogKeys.StandUp, $"{unit.Profile.DisplayName} поднимается на ноги (−{Balance.Combat.StandUpApCost} AP)",
                    "unitId", unit.Id, "ap", I(Balance.Combat.StandUpApCost));
            }

            var stunned = unit.GetStatus(StatusType.Stunned);
            if (stunned != null)
            {
                unit.Statuses.Remove(stunned);
                unit.Ap = 0;
                Record(CombatLogKeys.StunnedSkip, $"{unit.Profile.DisplayName} оглушён — пропускает ход", "unitId", unit.Id);
            }

            for (int i = 0; i < unit.Statuses.Count && unit.IsActive; i++)
            {
                var s = unit.Statuses[i];
                if (s.DotDamagePerTurn <= 0) continue;
                int dmg = DamageResolver.DotTick(s.DotDamagePerTurn, s.DotType, unit);
                if (dmg > 0)
                {
                    Record(CombatLogKeys.StatusDot, $"{unit.Profile.DisplayName} страдает от {s.Type}: −{dmg} HP",
                        "unitId", unit.Id, "status", CombatLogKeys.StatusId(s.Type), "damage", I(dmg));
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
                    Record(CombatLogKeys.StatusExpired, $"  {unit.Profile.DisplayName}: состояние {s.Type} спадает",
                        "unitId", unit.Id, "status", CombatLogKeys.StatusId(s.Type));
                    unit.Statuses.RemoveAt(i);
                }
            }
        }

        private void AdvanceUntilActorReady()
        {
            int safety = _units.Count * 2 + 2;
            while (safety-- > 0 && Outcome == CombatOutcome.Ongoing)
            {
                int roundBefore = Round;
                var next = _turns.Advance();

                // Гарантия завершаемости (B1): раунд перевалил за предохранитель —
                // бой принудительно ничья, независимо от того, кто и как тянет время.
                if (Round > Balance.Combat.RoundCap)
                {
                    Outcome = CombatOutcome.Draw;
                    Record(CombatLogKeys.DrawRoundCap, "=== НИЧЬЯ: превышен предел раундов ===");
                    return;
                }

                // Граница раунда — событие для игрока: без неё журнал на
                // два-три раунда читается сплошной лентой.
                if (Round != roundBefore)
                    Record(CombatLogKeys.RoundStarted, $"--- раунд {Round} ---", "round", I(Round));

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
                Record(CombatLogKeys.Victory, "=== ПОБЕДА: враги выбыли ===");
            }
            else if (!playerStanding)
            {
                Outcome = CombatOutcome.Defeat;
                Record(CombatLogKeys.Defeat, "=== ПОРАЖЕНИЕ: отряд не может продолжать бой ===");
            }
        }

        private void RecordShred(CombatUnit target, int amount)
            => Record(CombatLogKeys.Shred, $"  Шред: броня {target.Profile.DisplayName} −{amount} (тек. {target.EffectiveArmor})",
                "unitId", target.Id, "amount", I(amount), "armor", I(target.EffectiveArmor));

        /// <summary>
        /// ЕДИНСТВЕННЫЙ способ что-то записать о бое: строка трейса (<see cref="Log"/>,
        /// internal, для отладки) и запись журнала (<see cref="Journal"/>, ключ R7
        /// для игрока) рождаются вместе. Отдельного AddLog нет намеренно — строка
        /// без ключа снова утекла бы к игроку сырым текстом.
        /// args — пары «имя, значение» подряд.
        /// </summary>
        private void Record(string key, string trace, params string[] args)
        {
            _log.Add(trace);
            _journal.Add(new CombatLogEntry(Round, key, ToArgs(args)));
        }

        private static IReadOnlyDictionary<string, string> ToArgs(string[] pairs)
        {
            if (pairs == null || pairs.Length == 0) return null;
            if (pairs.Length % 2 != 0)
                throw new ArgumentException("Аргументы журнала боя — пары «имя, значение»", nameof(pairs));
            var args = new Dictionary<string, string>(pairs.Length / 2, StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) args[pairs[i]] = pairs[i + 1];
            return args;
        }

        private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
