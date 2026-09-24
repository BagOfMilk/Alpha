using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Economy;

namespace Game.Core.Base
{
    /// <summary>
    /// Центральное состояние базы и главный игровой цикл «мирной» фазы.
    /// Связывает ростер, слоты назначений, кошелёк ресурсов и баланс-конфиг,
    /// и продвигает время методом <see cref="AdvanceCycle"/> (один цикл = один
    /// игровой день). Вся логика — чистый C#, без зависимостей от Unity.
    ///
    /// НЕ реализует <c>Game.Core.Loop.IStateBlob</c> сама — намеренно:
    /// охранитель <c>BaseState_HasNoBackdoorToAdvanceTime</c> запрещает этому
    /// типу реализовывать ЛЮБОЙ контракт из Game.Core.Loop, после истории с
    /// портом IDailyCycle, который дал ей публичный RunDay(). Слепок хозяйства
    /// (кошелёк + открытые слоты + уровень/опыт ростера) отдаёт наружу
    /// <see cref="CaptureState"/>/<see cref="RestoreState"/> как обычные
    /// публичные методы; в конвейер их заворачивает <see cref="EconomyBlob"/>
    /// (Foundation/A1) — тем же приёмом, каким RosterAdapter заворачивает Roster.
    /// </summary>
    public sealed class BaseState
    {
        public Roster Roster { get; }
        public ResourceLedger Resources { get; }
        public BalanceConfig Balance { get; }

        public int CurrentCycle { get; private set; }

        /// <summary>
        /// Вчера не поели — сегодня работаем хуже (Поправка №4). Просадка идёт
        /// следующим циклом, а не тем же: прокорм считается последним шагом дня,
        /// когда выработка уже начислена.
        /// </summary>
        public bool WasHungryLastCycle { get; private set; }

        private readonly Dictionary<string, AssignmentSlot> _slotsById = new Dictionary<string, AssignmentSlot>();
        private readonly List<AssignmentSlot> _slots = new List<AssignmentSlot>();

        public IReadOnlyList<AssignmentSlot> Slots => _slots;

        public BaseState(Roster roster, ResourceLedger resources, BalanceConfig balance)
        {
            Roster = roster ?? throw new ArgumentNullException(nameof(roster));
            Resources = resources ?? throw new ArgumentNullException(nameof(resources));
            Balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public AssignmentSlot GetSlot(string slotId)
        {
            return slotId != null && _slotsById.TryGetValue(slotId, out var s) ? s : null;
        }

        public AssignmentSlot AddSlot(AssignmentSlotDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id)) return null;
            if (_slotsById.ContainsKey(definition.Id)) return _slotsById[definition.Id];
            var slot = new AssignmentSlot(definition);
            _slotsById.Add(slot.Id, slot);
            _slots.Add(slot);
            return slot;
        }

        /// <summary>
        /// Открывает закрытый слот за ресурсы. До появления полноценной стройки
        /// (US-7.1, US-7.3) это единственный способ ввести слот в игру — раньше
        /// закрытый слот оставался закрытым навсегда.
        ///
        /// Списание атомарное: если ресурсов не хватает, кошелёк не трогается
        /// вообще, а слот остаётся закрытым.
        /// </summary>
        public UnlockResult TryUnlockSlot(string slotId)
        {
            var slot = GetSlot(slotId);
            if (slot == null) return UnlockResult.SlotNotFound;
            if (slot.Unlocked) return UnlockResult.AlreadyUnlocked;

            var cost = slot.Definition?.UnlockCost;
            if (cost == null || cost.Count == 0) return UnlockResult.NoPriceDefined;
            if (!Resources.TrySpend(cost)) return UnlockResult.CannotAfford;

            slot.Unlocked = true;
            return UnlockResult.Success;
        }

        // ---- Назначения ----

        /// <summary>
        /// Назначает напарника на слот. Если напарник уже стоит на другом слоте —
        /// он автоматически снимается оттуда (перевод). Слот должен быть пустым.
        /// </summary>
        public AssignmentResult TryAssign(string companionId, string slotId)
        {
            var slot = GetSlot(slotId);
            if (slot == null) return AssignmentResult.SlotNotFound;
            if (!slot.Unlocked) return AssignmentResult.SlotLocked;

            var companion = Roster.Get(companionId);
            if (companion == null) return AssignmentResult.CompanionNotFound;

            // B4-аудит §4.5: Antagonist явно исключён (не «!= Dead») — ушедший в
            // антагонисты не встаёт обратно на пост, даже если формально жив.
            if (companion.IsDead || companion.Status == CompanionStatus.OnMission ||
                companion.Status == CompanionStatus.Antagonist)
                return AssignmentResult.CompanionUnavailable;

            // Пост погибшего свободен, даже если сверка ещё не прошла.
            if (slot.IsOccupied && IsFallen(slot.AssignedCompanionId))
                slot.AssignedCompanionId = null;

            if (slot.IsOccupied && slot.AssignedCompanionId != companionId)
                return AssignmentResult.SlotOccupied;

            // Снять с прежнего слота, если был назначен.
            if (companion.IsAssigned && companion.AssignedSlotId != slotId)
                Unassign(companion.AssignedSlotId);

            slot.AssignedCompanionId = companionId;
            companion.AssignedSlotId = slotId;
            if (companion.Status == CompanionStatus.Idle)
                companion.Status = CompanionStatus.Assigned;
            // G26 (ревью B7): Resting — это "свободен и лечится", а не ярлык,
            // который переживает назначение на пост. Тот, кто держит пост,
            // работает через рану (Injured), а не отдыхает — назначение
            // обязано вернуть статус к Injured, иначе ярлык застревал бы на
            // Resting до полного излечения даже у занятого постом.
            else if (companion.Status == CompanionStatus.Resting)
                companion.Status = CompanionStatus.Injured;

            return AssignmentResult.Success;
        }

        /// <summary>Снимает назначение со слота (если занят).</summary>
        public void Unassign(string slotId)
        {
            var slot = GetSlot(slotId);
            if (slot == null || !slot.IsOccupied) return;

            var companion = Roster.Get(slot.AssignedCompanionId);
            slot.AssignedCompanionId = null;
            if (companion != null)
            {
                companion.AssignedSlotId = null;
                if (companion.Status == CompanionStatus.Assigned)
                    companion.Status = CompanionStatus.Idle;
            }
        }

        /// <summary>
        /// Освобождает посты, которые держат погибшие (или те, кого уже нет в
        /// ростере). Смерть приходит из разных мест — кризис, вылазка, позже бой, —
        /// и не всякое из них знает про базу; поэтому база сверяется сама: перед
        /// каждым циклом, перед расстановкой хозяина и при назначении на такой пост.
        ///
        /// Без этого погибший на посту производил вечно, а пост нельзя было отдать
        /// живому: слот считался занятым (аудит разрывов, G17).
        ///
        /// B4-фикс ревью: та же дыра повторялась для Antagonist — переход в
        /// антагонисты (<see cref="Game.Core.Companions.Defection.Defect"/>)
        /// без явной ссылки на эту базу чистит только сторону напарника
        /// (<c>Companion.AssignedSlotId</c>), а бухгалтерия слота
        /// (<c>AssignmentSlot.AssignedCompanionId</c>) не знает об уходе и
        /// оставалась занятой навсегда — пост нельзя отдать живому, а
        /// дефектор продолжал бы производить и получать опыт с поста
        /// (AdvanceCycle ниже сверяется через тот же ReleaseFallen). IsFallen
        /// явно включает Antagonist — не «!= Dead», как и везде в аудите §4.5.
        /// Возвращает, сколько постов освобождено.
        /// </summary>
        public int ReleaseFallen()
        {
            int freed = 0;
            foreach (var slot in _slots)
            {
                if (!slot.IsOccupied || !IsFallen(slot.AssignedCompanionId)) continue;
                slot.AssignedCompanionId = null;
                freed++;
            }
            return freed;
        }

        private bool IsFallen(string companionId)
        {
            var c = Roster.Get(companionId);
            return c == null || c.IsDead || c.Status == CompanionStatus.Antagonist;
        }

        // ---- слепок хозяйства (Foundation/A1) ----
        //
        // g:<золото>|m:<материалы>|f:<еда>|u:<открытые слоты через ','>|
        // x:<companionId:уровень:опыт через ',' >
        // Без «;» и внутреннего «=» — внешний слепок (SettlementSave) режет по
        // ним на своём уровне. Обычные публичные методы, не реализация
        // IStateBlob — см. комментарий класса; в порт их заворачивает EconomyBlob.

        public string CaptureState()
        {
            var sb = new StringBuilder();
            sb.Append("g:").Append(Resources.Get(ResourceType.Gold).ToString(CultureInfo.InvariantCulture));
            sb.Append("|m:").Append(Resources.Get(ResourceType.Materials).ToString(CultureInfo.InvariantCulture));
            sb.Append("|f:").Append(Resources.Get(ResourceType.Food).ToString(CultureInfo.InvariantCulture));

            var unlocked = new List<string>();
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Unlocked) unlocked.Add(_slots[i].Id);
            unlocked.Sort(StringComparer.Ordinal);
            sb.Append("|u:").Append(string.Join(",", unlocked.ToArray()));

            var xp = new List<string>();
            foreach (var c in Roster.All)
                xp.Add(c.Id + ":" + c.Level.ToString(CultureInfo.InvariantCulture) +
                       ":" + c.Xp.ToString(CultureInfo.InvariantCulture));
            xp.Sort(StringComparer.Ordinal);
            sb.Append("|x:").Append(string.Join(",", xp.ToArray()));

            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            if (string.IsNullOrEmpty(blob)) return;

            foreach (var part in blob.Split('|'))
            {
                if (part.Length < 2 || part[1] != ':') continue;
                string body = part.Substring(2);

                switch (part[0])
                {
                    case 'g':
                        Resources.Add(ResourceType.Gold, ParseInt(body) - Resources.Get(ResourceType.Gold));
                        break;
                    case 'm':
                        Resources.Add(ResourceType.Materials, ParseInt(body) - Resources.Get(ResourceType.Materials));
                        break;
                    case 'f':
                        Resources.Add(ResourceType.Food, ParseInt(body) - Resources.Get(ResourceType.Food));
                        break;
                    case 'u':
                        var unlocked = new HashSet<string>(body.Split(','));
                        for (int i = 0; i < _slots.Count; i++)
                            _slots[i].Unlocked = unlocked.Contains(_slots[i].Id);
                        break;
                    case 'x':
                        if (body.Length == 0) break;
                        foreach (var item in body.Split(','))
                        {
                            var f = item.Split(':');
                            if (f.Length < 3) continue;
                            var c = Roster.Get(f[0]);
                            if (c == null) continue;
                            c.RestoreProgressForSave(ParseInt(f[1]), ParseInt(f[2]));
                        }
                        break;
                }
            }
        }

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        // ---- Продвижение времени ----

        /// <summary>
        /// Один цикл (день): производство со всех занятых слотов, начисление
        /// ролевого опыта, естественное лечение и расход еды поселением.
        ///
        /// INTERNAL намеренно. Единственный легальный вызывающий — ProductionStep
        /// внутри дневного конвейера; Game.Gameplay лежит в другой сборке и
        /// физически не может позвать этот метод в обход. Пока он был публичным,
        /// демка базы крутила его напрямую, и в проекте существовало два дневных
        /// цикла, не знающих друг о друге.
        /// </summary>
        internal CycleReport AdvanceCycle()
        {
            CurrentCycle++;
            var report = new CycleReport { Cycle = CurrentCycle };

            ReleaseFallen();

            foreach (var slot in _slots)
            {
                if (!slot.Unlocked || !slot.IsOccupied) continue;
                var companion = Roster.Get(slot.AssignedCompanionId);
                // B4-фикс ревью: ReleaseFallen() чуть выше уже освобождает
                // слот антагониста (тот же IsFallen), но допуск к производству
                // держим явным списком — «не Dead» не исключает Antagonist
                // по построению enum, а слот, отпущенный этим же тиком, сюда
                // и не попадёт (IsOccupied уже false); явная проверка — тот же
                // стиль защиты, что и в TryAssign/Steward.Staff (§4.5).
                if (companion == null || companion.IsDead || companion.Status == CompanionStatus.OnMission ||
                    companion.Status == CompanionStatus.Antagonist) continue;

                var def = slot.Definition;
                int output = ProductionCalculator.OutputPerCycle(companion, def, Balance);
                if (WasHungryLastCycle) output = (int)Math.Round(output * Balance.HungryProductionMultiplier, MidpointRounding.ToEven);

                switch (def.OutputKind)
                {
                    case SlotOutputKind.Resource:
                        Resources.Add(def.OutputResource, output);
                        report.AddProduced(def.OutputResource, output);
                        break;
                    case SlotOutputKind.Passive:
                        report.AddPassive(def.PassiveBonusId, output);
                        break;
                    case SlotOutputKind.Healing:
                        ApplyHealing(output, report);
                        break;
                    case SlotOutputKind.None:
                        // Позиция без выхода — не ошибка и не заглушка на время
                        // отладки: по GDD у мастерской и лаборатории функция
                        // крафт, а крафта ещё нет. Ролевой опыт при этом идёт:
                        // человек на посту всё равно работает.
                        break;
                }

                int xp = ProductionCalculator.RoleXpPerCycle(companion, def, Balance);
                if (WasHungryLastCycle) xp = (int)Math.Round(xp * Balance.HungryRoleXpMultiplier, MidpointRounding.ToEven);
                var lvl = companion.GainXp(xp, Balance);
                if (lvl.LeveledUp)
                    report.LeveledUp.Add(companion.Id);
            }

            ApplyNaturalHealing(report);
            ApplyFoodUpkeep(report);
            return report;
        }

        /// <summary>
        /// Распределяет лечебные очки по самым тяжело раненным.
        ///
        /// G26: пока лечение в лазарете идёт, а сама рана ещё не закрылась,
        /// свободный (не держащий пост) раненый переходит в Resting — статус
        /// был объявлен, но никогда не присваивался. Тот, кто продолжает
        /// работать через рану (пост держит — <c>InjuredCompanion_ProducesLess</c>),
        /// остаётся Injured: он не «отдыхает», он работает с пенальти.
        /// </summary>
        private void ApplyHealing(int healing, CycleReport report)
        {
            if (healing <= 0) return;
            double pool = healing;
            // Сначала тем, у кого больше InjuryPoints.
            var injured = new List<Companion>();
            foreach (var c in Roster.All)
                if (c.IsInjured) injured.Add(c);
            injured.Sort((a, b) => b.InjuryPoints.CompareTo(a.InjuryPoints));

            foreach (var c in injured)
            {
                if (pool <= 0) break;
                double heal = Math.Min(pool, c.InjuryPoints);
                c.InjuryPoints -= heal;
                pool -= heal;
                if (c.InjuryPoints <= 0)
                {
                    c.InjuryPoints = 0;
                    if (c.Status == CompanionStatus.Injured || c.Status == CompanionStatus.Resting)
                        c.Status = CompanionStatus.Idle;
                    report.Recovered.Add(c.Id);
                }
                else if (heal > 0 && !c.IsAssigned && c.Status == CompanionStatus.Injured)
                {
                    c.Status = CompanionStatus.Resting;
                }
            }
        }

        /// <summary>Базовое естественное восстановление всех раненых за цикл.</summary>
        private void ApplyNaturalHealing(CycleReport report)
        {
            double regen = Balance.BaseHealingPerCycle;
            if (regen <= 0) return;
            foreach (var c in Roster.All)
            {
                if (!c.IsInjured) continue;
                c.InjuryPoints -= regen;
                if (c.InjuryPoints <= 0)
                {
                    c.InjuryPoints = 0;
                    if (c.Status == CompanionStatus.Injured || c.Status == CompanionStatus.Resting)
                        c.Status = CompanionStatus.Idle;
                    if (!report.Recovered.Contains(c.Id))
                        report.Recovered.Add(c.Id);
                }
            }
        }

        private void ApplyFoodUpkeep(CycleReport report)
        {
            // Едят живые: погибший в ростере остаётся (память, рябь), но не ест.
            int living = 0;
            foreach (var c in Roster.All)
                if (!c.IsDead) living++;
            int upkeep = Balance.FoodUpkeepPerCompanion * living;
            if (upkeep <= 0) { WasHungryLastCycle = false; return; }
            if (!Resources.TrySpend(ResourceType.Food, upkeep))
            {
                // Голодный день: остатки съедены подчистую, база просядет следующим
                // циклом, а Напряжение поднимет шаг дня HungerStep (Поправка №4).
                Resources.Add(ResourceType.Food, -Resources.Get(ResourceType.Food));
                report.FoodShortage = true;
                WasHungryLastCycle = true;
            }
            else
            {
                WasHungryLastCycle = false;
            }
        }
    }
}
