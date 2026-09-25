using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Balance;
using Game.Core.Randomness;

namespace Game.Core.Combat
{
    /// <summary>Результат бойової дії (патерн AssignmentResult).</summary>
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
        Victory = 1, // усі вороги вибули
        Defeat = 2,  // не лишилось діючих юнітів гравця
        Retreat = 3, // загін відступив за командою (BattleResult.Outcome)
        Draw = 4     // досягнуто запобіжний ліміт раундів — гарантія завершуваності
    }

    /// <summary>
    /// Оркестратор бою: грід + юніти + індивідуальна ініціатива + дії поточного
    /// юніта (рух/атака/стабілізація/overwatch/кінець ходу). Правила: пул AP,
    /// надійний %/поріг (IHitRule — R1), Strike-метр, статуси (DoT на початку ходу,
    /// тривалість наприкінці, Resolve скорочує), overwatch, даун з вікном на порятунок
    /// (стабілізація Медициною), смерть насовсім. Вороги симетричні — діють тим
    /// самим API. Roster НІКОЛИ не чіпає — підсумок читається через BattleResult.From,
    /// рани застосовує D1 (RosterAdapter.Wound, Р5).
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
        /// Внутрішній діагностичний трейс (вільний текст російською, із
        /// сирими іменами enum) — НЕ те, що бачить гравець. <c>internal</c>
        /// навмисно, як числа денного звіту (інваріант 3): Game.Gameplay
        /// фізично не може його прочитати, і показати його гравцю не можна навіть
        /// помилково. Гравцю йде <see cref="Journal"/> — ті самі події
        /// ключами (R7), рядок до рядка.
        /// </summary>
        internal IReadOnlyList<string> Log => _log;

        /// <summary>
        /// Журнал бою для гравця: ключ + аргументи на кожен рядок трейсу
        /// <see cref="Log"/>, у тому самому порядку (обидва пише тільки <see cref="Record"/>).
        /// Джерело BattleView.Log; слова до ключів підставляє Gameplay.
        /// </summary>
        public IReadOnlyList<CombatLogEntry> Journal => _journal;

        /// <summary>Структурована історія атак — телеметрія чесності кидка:
        /// показане гравцю число проти фактичного результату. Не серіалізується.</summary>
        public IReadOnlyList<AttackRecord> Attacks => _attacks;
        public CombatOutcome Outcome { get; private set; } = CombatOutcome.Ongoing;
        public CombatUnit Current => _turns?.Current;
        public int Round => _turns?.Round ?? 0;
        public IReadOnlyList<CombatUnit> TurnOrder => _turns?.Order;

        /// <summary>Яке правило влучання в цьому бою — той самий прапорець, що несе BattleView.IsHitRulePercent.</summary>
        public bool IsHitRulePercent => _hitRule is PercentRule;

        /// <summary>
        /// roller може бути null: ThresholdRule його взагалі не читає (R1 —
        /// повністю детермінований режим не зобов'язаний мати під собою кубик).
        /// PercentRule сам кине ArgumentNullException при першому ж Resolve,
        /// якщо roller не переданий — раніше, ніж помилка розповзеться по логіці бою.
        /// </summary>
        public CombatState(GridMap map, BalanceConfig balance, IHitRule hitRule, IDiceRoller roller)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _hitRule = hitRule ?? throw new ArgumentNullException(nameof(hitRule));
            _roller = roller;
        }

        public CombatUnit GetUnit(string id) => id != null && _byId.TryGetValue(id, out var u) ? u : null;

        // ---- Збірка бою ----
        public void AddUnit(CombatUnit unit, GridPos pos)
        {
            if (unit == null || _byId.ContainsKey(unit.Id)) return;
            if (!Map.IsFree(pos)) throw new InvalidOperationException($"Клітинка {pos} зайнята або непрохідна");
            unit.Pos = pos;
            Map.SetOccupant(pos, unit.Id);
            _units.Add(unit);
            _byId.Add(unit.Id, unit);
        }

        /// <summary>Старт бою: будує чергу ініціативи і починає перший хід.</summary>
        public void Begin()
        {
            _turns = new TurnSystem(_units);
            Record(CombatLogKeys.Started, "=== БОЙ НАЧАЛСЯ (раунд 1) ===");
            if (!BeginTurn(_turns.Current))
                AdvanceUntilActorReady();
        }

        /// <summary>
        /// Відступ: загін розриває бій за командою (не поразка і не перемога).
        /// Доступно, поки бій триває — не прив'язано до чийогось ходу, це рішення
        /// рівня вище юнітів (BattleResult.Outcome = Retreat).
        /// </summary>
        public CombatActionResult Retreat()
        {
            if (Outcome != CombatOutcome.Ongoing) return CombatActionResult.InvalidAction;
            Outcome = CombatOutcome.Retreat;
            Record(CombatLogKeys.Retreat, "=== ОТСТУПЛЕНИЕ: отряд разрывает бой ===");
            return CombatActionResult.Success;
        }

        /// <summary>
        /// Безумовний зовнішній запобіжник (CombatAi.AutoResolve): переводить
        /// Ongoing у Draw незалежно від Round/RoundCap. Потрібен, тому що
        /// гарантія завершуваності автобою не може залежати від того, наскільки
        /// щедрий turnBudget передав викликач — сам CombatState єдиний,
        /// хто може чесно закрити бій без «наполовину зіграного» результату.
        /// На бій, уже завершений будь-чим іншим (Victory/Defeat/Retreat/
        /// власний Draw за RoundCap), не діє.
        /// </summary>
        public CombatActionResult ForceDraw(string reason)
        {
            if (Outcome != CombatOutcome.Ongoing) return CombatActionResult.InvalidAction;
            Outcome = CombatOutcome.Draw;
            // Причина — службовий текст викликача (CombatAi), гравцю не йде.
            Record(CombatLogKeys.DrawForced, $"=== НИЧЬЯ: {reason} ===");
            return CombatActionResult.Success;
        }

        // ---- Дії поточного юніта ----
        /// <summary>Рух у досяжний тайл; ціна за тайл росте під Придушенням.</summary>
        public CombatActionResult Move(GridPos dest)
        {
            var unit = ActiveCurrentOrNull();
            if (unit == null) return CombatActionResult.InvalidAction;

            var reachable = ReachableFor(unit);
            if (!reachable.TryGetValue(dest, out int cost)) return CombatActionResult.NotReachable;

            // Шлях рахуємо ДО списання AP і ДО запису рядка журналу: "path"
            // (§7.3 COMBAT_V2.md) — фактичні клітинки руху, той самий шлях,
            // яким ітеруємось нижче, а не окремий перерахунок, що міг би розійтись.
            var path = Pathfinder.Path(Map, unit.Pos, dest);

            unit.Ap -= cost;
            Record(CombatLogKeys.Move, $"{unit.Profile.DisplayName} перемещается в {dest} (−{cost} AP)",
                "unitId", unit.Id, "ap", I(cost), "x", I(dest.X), "y", I(dest.Y), "path", PathArg(path));

            // Шлях проходиться по клітинках, а не стрибком: дозор противника зобов'язаний
            // бачити сам шлях. Реакція може вкласти того, хто йде — тоді він
            // лишається там, де впав. Пастка — тільки в точці прибуття, тим
            // самим порядком, що у ривка і перестановки (PlaceUnitAt): спочатку
            // постріл із дозору, потім пастка — якщо дійшов на ногах.
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
        /// Overwatch: юніт платить ціну пострілу зброєю заздалегідь і тримає
        /// сектор — конус від своєї клітинки в бік aim — до початку свого
        /// наступного ходу. Вхід у дозор завершує хід: постріл оплачений із того
        /// самого пулу, що й усе інше, тому «спочатку походити, потім
        /// залягти» можна, а отримати постріл двічі — ні.
        ///
        /// Спрацьовує ОДИН раз: на першому переміщенні противника в сектор —
        /// крок ходу, приземлення ривка, перестановка за наказом, — у межах
        /// огляду (дальня зброя) або контакту (ближня). Без подвійного
        /// зиску: резерв не повертається, якщо ніхто не прийшов; постріл не
        /// накопичує Strike-метр і не може бути Strike; точність — зі штрафом
        /// навмання. Проки зброї працюють: це властивості самого пострілу.
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
        /// Атака поточною зброєю. useStrike — витратити повний Strike-метр на
        /// гарантоване влучання. Повне влучання/крит накопичує метр і
        /// тригерить проки зброї (Шред/статус); граза — лише половинний урон.
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

        /// <summary>Один удар зброєю: влучання через IHitRule → урон через DamageResolver → проки → Strike.</summary>
        private void ExecuteAttackRoll(CombatUnit unit, CombatUnit target, WeaponDefinition w,
                                       int accuracyBonus, bool forceHit, bool allowStrikeGain, bool isReaction = false)
        {
            int shown = HitChanceCalculator.Compute(unit, target, Map, Balance, accuracyBonus);
            var outcome = forceHit ? AttackOutcome.Hit : _hitRule.Resolve(unit, target, shown, _roller);
            var dmg = DamageResolver.RollAttackDamage(unit, target, w, outcome, _roller, !IsHitRulePercent, Balance);
            string attackKey = CombatLogKeys.Attack(outcome);

            // §7.3 COMBAT_V2.md: "ap" — ціна цього конкретного удару (навіть
            // якщо AP насправді списала здібність цілою сумою — w лишається
            // зброєю цього пострілу), "cover" — напрямлене укриття цілі, яке
            // РЕАЛЬНО застосувалось до цього рола (None, якщо зброя його
            // ігнорує, — інакше журнал брехав би про причину промаху).
            string apArg = I(w.ApCost);
            string coverArg = (w.IsMelee ? CoverType.None : Map.CoverAgainst(target.Pos, unit.Pos)).ToString();

            switch (outcome)
            {
                case AttackOutcome.Miss:
                    Record(attackKey, $"{unit.Profile.DisplayName} → {target.Profile.DisplayName}: промах ({shown})",
                        "unitId", unit.Id, "targetId", target.Id, "chance", I(shown), "damage", I(0),
                        "ap", apArg, "cover", coverArg);
                    break;

                case AttackOutcome.Graze:
                    Record(attackKey, $"{unit.Profile.DisplayName} → {target.Profile.DisplayName}: граза, {dmg.Amount} урона ({shown})",
                        "unitId", unit.Id, "targetId", target.Id, "chance", I(shown), "damage", I(dmg.Amount),
                        "ap", apArg, "cover", coverArg);
                    ApplyDamage(target, dmg.Amount);
                    break;

                case AttackOutcome.Hit:
                case AttackOutcome.Crit:
                    if (allowStrikeGain) unit.StrikeMeter += Balance.Combat.StrikePerHit;
                    Record(attackKey, $"{unit.Profile.DisplayName} → {target.Profile.DisplayName}: " +
                           $"{(outcome == AttackOutcome.Crit ? "КРИТ, " : "")}{dmg.Amount} урона ({shown})",
                        "unitId", unit.Id, "targetId", target.Id, "chance", I(shown), "damage", I(dmg.Amount),
                        "ap", apArg, "cover", coverArg);

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

        /// <summary>Стабілізація дауну союзника поруч (активка Медицини). Детермінована.</summary>
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

        // ---- Здібності ----
        /// <summary>
        /// Застосування здібності: гейт скіла вирішений при зборці юніта; тут — КД,
        /// AP, ціль/дальність/LOS і превалідація спец-ефектів ДО списання AP.
        /// targetUnitId — ціль-юніт; targetTile — точка (пастка/перестановка).
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

            // Зброя ближнього бою б'є тільки з контакту (симетрія з Attack); «Ривок»
            // враховується — він спершу переставляє юніта впритул (lungeDest).
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
                            // §7.3: здібності, що рухають юніта, теж несуть
                            // "path"/"from" — тут стрибок в один тайл (не шлях
                            // по клітинках, як у Move), тому "path" — один
                            // сегмент (дест.), а "from" — звідки стрибнули.
                            var origin = unit.Pos;
                            Record(CombatLogKeys.Lunge, $"  {unit.Profile.DisplayName} совершает рывок к {target.Profile.DisplayName}",
                                "unitId", unit.Id, "targetId", target.Id,
                                "from", XY(origin), "path", PathArg(new List<GridPos> { lungeDest.Value }));
                            PlaceUnitAt(unit, lungeDest.Value);
                        }
                        break;

                    case AbilityEffectKind.RepositionTarget:
                        if (target != null && target.IsActive && targetTile.HasValue && Map.IsFree(targetTile.Value))
                        {
                            var origin = target.Pos;
                            Record(CombatLogKeys.Repositioned, $"  {target.Profile.DisplayName} перемещается в {targetTile.Value}",
                                "unitId", target.Id, "targetId", unit.Id, "x", I(targetTile.Value.X), "y", I(targetTile.Value.Y),
                                "from", XY(origin), "path", PathArg(new List<GridPos> { targetTile.Value }));
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

        /// <summary>Вільна клітинка впритул до цілі ривка, найближча до атакуючого (детерміновано).</summary>
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
        /// Переміщення одним стрибком (ривок, перестановка): власний дозор губиться —
        /// сектор тримається з конкретної клітинки; чужий дозор бачить приземлення;
        /// пастка спрацьовує останньою і тільки під тим, хто лишився на ногах.
        /// Той самий порядок, що й в останнього кроку звичайного ходу (Move).
        /// </summary>
        private void PlaceUnitAt(CombatUnit unit, GridPos dest)
        {
            BreakOverwatch(unit, CombatLogKeys.OverwatchLostDisplaced, "сбит с позиции");
            StepTo(unit, dest);
            ReactToMovement(unit);
            if (unit.IsActive && Outcome == CombatOutcome.Ongoing) TriggerTrapAt(unit);
        }

        /// <summary>Один крок: тільки клітинка і зайнятість, без пасток і реакцій.</summary>
        private void StepTo(CombatUnit unit, GridPos tile)
        {
            Map.ClearOccupant(unit.Pos);
            unit.Pos = tile;
            Map.SetOccupant(tile, unit.Id);
        }

        // ---- Overwatch ----
        /// <summary>
        /// Реакція дозору на крок mover. Дозорні перебираються в порядку додавання в
        /// бій — детерміновано; кожен стріляє не більше разу, і постріл знімає
        /// його дозор ДО ролу. Якщо того, хто йде, вклали, решта вже не стріляють.
        /// </summary>
        private void ReactToMovement(CombatUnit mover)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                if (!mover.IsActive || Outcome != CombatOutcome.Ongoing) return;

                var watcher = _units[i];
                if (watcher == mover || !watcher.IsActive || watcher.Side == mover.Side) continue;
                if (!OverwatchCovers(watcher, mover.Pos)) continue;

                watcher.Overwatch = null; // одне спрацювання
                Record(CombatLogKeys.OverwatchFired, $"{watcher.Profile.DisplayName} стреляет из дозора по {mover.Profile.DisplayName}!",
                    "unitId", watcher.Id, "targetId", mover.Id);
                ExecuteAttackRoll(watcher, mover, watcher.Weapon,
                    accuracyBonus: -Balance.Combat.OverwatchAccuracyPenalty, forceHit: false, allowStrikeGain: false, isReaction: true);
            }
        }

        /// <summary>
        /// Чи накриває дозор watcher клітинку tile: сектор + огляд (дальня зброя) або
        /// контакт (ближня — «страж біля дверей»). Довідка для UI та ШІ: підсвітити сектор.
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

        /// <summary>Показане число пострілу з дозору по цілі там, де вона стоїть (зі штрафом навмання).</summary>
        public int OverwatchHitChancePreview(CombatUnit watcher, CombatUnit target)
            => HitChanceCalculator.Compute(watcher, target, Map, Balance, -Balance.Combat.OverwatchAccuracyPenalty);

        /// <summary>
        /// Бій v2 (§7.1, GameSession.PreviewOverwatchCone): тайли, які накрив
        /// би дозор unit, ЯКБИ він зараз узяв приціл aim — без фактичного
        /// входу в дозор (ніяких мутацій, AP не чіпається). Та сама геометрія
        /// (<see cref="OverwatchStance.InCone"/>) і той самий критерій огляду
        /// (контакт для мілі, LineOfSight для дальньої), що й
        /// <see cref="OverwatchCovers"/> — намальований конус не може розійтись
        /// із реальним спрацюванням дозору.
        /// </summary>
        public IReadOnlyList<GridPos> PreviewOverwatchCone(CombatUnit unit, GridPos aim)
        {
            var result = new List<GridPos>();
            if (unit?.Weapon == null || !Map.InBounds(aim) || aim == unit.Pos) return result;

            for (int y = 0; y < Map.Height; y++)
            {
                for (int x = 0; x < Map.Width; x++)
                {
                    var tile = new GridPos(x, y);
                    if (tile == unit.Pos) continue;
                    if (!OverwatchStance.InCone(unit.Pos, aim, tile,
                            Balance.Combat.OverwatchConeSlopeNum, Balance.Combat.OverwatchConeSlopeDen))
                        continue;

                    bool visible = unit.Weapon.IsMelee
                        ? GridPos.Chebyshev(unit.Pos, tile) <= unit.Weapon.OptimalRange
                        : LineOfSight.HasLine(Map, unit.Pos, tile);
                    if (visible) result.Add(tile);
                }
            }
            return result;
        }

        /// <summary>key — причина для журналу (CombatLogKeys.OverwatchLost*), why — вона ж для трейсу.</summary>
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

        /// <summary>Кінець ходу: тик тривалостей статусів, перехід до наступного діючого юніта.</summary>
        public CombatActionResult EndTurn()
        {
            if (Outcome != CombatOutcome.Ongoing || Current == null) return CombatActionResult.InvalidAction;
            TickStatusDurations(Current);
            AdvanceUntilActorReady();
            return CombatActionResult.Success;
        }

        // ---- Довідки для UI/AI ----
        /// <summary>Досяжні тайли поточного AP юніта (ціна кроку — своя у юніта, враховує Придушення).</summary>
        public Dictionary<GridPos, int> ReachableFor(CombatUnit unit)
            => Pathfinder.Reachable(Map, unit.Pos, unit.Ap, MoveCostPerTile(unit));

        /// <summary>
        /// Ціна кроку руху для ЦЬОГО юніта — з його власної похідної
        /// MoveApPerTile (аудит G18), а не з єдиної глобальної константи балансу,
        /// як було в архіві: перк/трейт/шрам, що змінює MoveApPerTile, тепер
        /// реально щось змінює в бою.
        /// </summary>
        public int MoveCostPerTile(CombatUnit unit)
        {
            int baseCost = Math.Max(1, unit.Profile.MoveApPerTile);
            return unit.HasStatus(StatusType.Suppressed)
                ? (int)Math.Ceiling(baseCost * Balance.Combat.SuppressionMoveCostMultiplier)
                : baseCost;
        }

        /// <summary>Показане гравцю число. accuracyBonus — бонус зведеної здібності:
        /// прев'ю зобов'язане збігатися з фактичним ролом.</summary>
        public int HitChancePreview(CombatUnit attacker, CombatUnit target, int accuracyBonus = 0)
            => HitChanceCalculator.Compute(attacker, target, Map, Balance, accuracyBonus);

        /// <summary>Показаний гравцю діапазон урону поточної зброї атакуючого по цілі (§DamageResolver.PreviewRange) — той самий принцип, що HitChancePreview вище.</summary>
        public DamagePreviewInfo DamagePreview(CombatUnit attacker, CombatUnit target)
            => attacker?.Weapon != null ? DamageResolver.PreviewRange(attacker, target, attacker.Weapon) : default;

        // ---- Внутрішні правила ----
        private CombatUnit ActiveCurrentOrNull()
        {
            if (Outcome != CombatOutcome.Ongoing) return null;
            var u = Current;
            return u != null && u.IsActive ? u : null;
        }

        /// <summary>Накладання статусу: гарантоване; Resolve (StatusDurationReduction) скорочує тривалість, мін 1. Повтор — освіжає.</summary>
        public void ApplyStatus(CombatUnit target, StatusType type)
        {
            // «Жодного стану» накласти не можна: усі внутрішні виклики вже
            // фільтрують None, а в журналу для нього немає токена (CombatLogKeys.StatusId).
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

        /// <summary>Початок ходу юніта. true — юніт готовий діяти (AP видані).</summary>
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

        /// <summary>Кінець ходу: тривалості −1, статуси, що спливли, знімаються.</summary>
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

                // Гарантія завершуваності (B1): раунд переваливсь за запобіжник —
                // бій примусово нічия, незалежно від того, хто і як тягне час.
                if (Round > Balance.Combat.RoundCap)
                {
                    Outcome = CombatOutcome.Draw;
                    Record(CombatLogKeys.DrawRoundCap, "=== НИЧЬЯ: превышен предел раундов ===");
                    return;
                }

                // Межа раунду — подія для гравця: без неї журнал на
                // два-три раунди читається суцільною стрічкою.
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
        /// ЄДИНИЙ спосіб щось записати про бій: рядок трейсу (<see cref="Log"/>,
        /// internal, для відладки) і запис журналу (<see cref="Journal"/>, ключ R7
        /// для гравця) народжуються разом. Окремого AddLog немає навмисно — рядок
        /// без ключа знову утік би до гравця сирим текстом.
        /// args — пари «ім'я, значення» поспіль.
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
                throw new ArgumentException("Аргументи журналу бою — пари «ім'я, значення»", nameof(pairs));
            var args = new Dictionary<string, string>(pairs.Length / 2, StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) args[pairs[i]] = pairs[i + 1];
            return args;
        }

        private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>"x,y" — той самий формат, що й один сегмент <see cref="PathArg"/>.</summary>
        private static string XY(GridPos p) => I(p.X) + "," + I(p.Y);

        /// <summary>
        /// §7.3 COMBAT_V2.md: "x,y;x,y;…" БЕЗ стартового тайла — той самий
        /// рядок і для багатоклітинного руху (Move), і для одноклітинного
        /// стрибка здібності (Lunge/Reposition, path з одного сегмента).
        /// </summary>
        private static string PathArg(List<GridPos> path)
        {
            if (path == null || path.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(XY(path[i]));
            }
            return sb.ToString();
        }
    }
}
