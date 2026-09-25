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
    /// Центральний стан бази і головний ігровий цикл «мирної» фази.
    /// Пов'язує ростер, слоти призначень, гаманець ресурсів і баланс-конфіг,
    /// і просуває час методом <see cref="AdvanceCycle"/> (один цикл = один
    /// ігровий день). Уся логіка — чистий C#, без залежностей від Unity.
    ///
    /// НЕ реалізує <c>Game.Core.Loop.IStateBlob</c> сама — навмисно:
    /// охоронець <c>BaseState_HasNoBackdoorToAdvanceTime</c> забороняє цьому
    /// типу реалізовувати БУДЬ-ЯКИЙ контракт з Game.Core.Loop, після історії з
    /// портом IDailyCycle, який дав їй публічний RunDay(). Зліпок господарства
    /// (гаманець + відкриті слоти + рівень/досвід ростера) віддає назовні
    /// <see cref="CaptureState"/>/<see cref="RestoreState"/> як звичайні
    /// публічні методи; у конвеєр їх загортає <see cref="EconomyBlob"/>
    /// (Foundation/A1) — тим самим прийомом, яким RosterAdapter загортає Roster.
    /// </summary>
    public sealed class BaseState
    {
        public Roster Roster { get; }
        public ResourceLedger Resources { get; }
        public BalanceConfig Balance { get; }

        public int CurrentCycle { get; private set; }

        /// <summary>
        /// Вчора не поїли — сьогодні працюємо гірше (Поправка №4). Просідання йде
        /// наступним циклом, а не тим самим: прокорм рахується останнім кроком дня,
        /// коли вироблення вже нараховане.
        /// </summary>
        public bool WasHungryLastCycle { get; private set; }

        private readonly Dictionary<string, AssignmentSlot> _slotsById = new Dictionary<string, AssignmentSlot>();
        private readonly List<AssignmentSlot> _slots = new List<AssignmentSlot>();

        public IReadOnlyList<AssignmentSlot> Slots => _slots;

        /// <summary>
        /// R11 (seamsForD1 B7): BaseState сам протагоніста не знає, тож ця
        /// властивість — єдиний спосіб для GameSession повідомити
        /// <see cref="AdvanceCycle"/>, кому з ростера не можна автоматично
        /// витрачати очки скілів при підвищенні рівня від постової XP (як і
        /// решта XP-джерел протагоніста — бій/квест/інцидент, які вже банкують
        /// через GameSession.GrantXp). Порожньо за замовчуванням — виклики без
        /// GameSession (тести, Alpha.Sim) поведінку не міняють.
        /// </summary>
        public string ProtagonistId { get; set; }

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
        /// Відкриває закритий слот за ресурси. До появи повноцінної будови
        /// (US-7.1, US-7.3) це єдиний спосіб ввести слот у гру — раніше
        /// закритий слот лишався закритим назавжди.
        ///
        /// Списання атомарне: якщо ресурсів не вистачає, гаманець не чіпається
        /// зовсім, а слот лишається закритим.
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

        // ---- Призначення ----

        /// <summary>
        /// Призначає напарника на слот. Якщо напарник уже стоїть на іншому слоті —
        /// він автоматично знімається звідти (переведення). Слот має бути порожнім.
        /// </summary>
        public AssignmentResult TryAssign(string companionId, string slotId)
        {
            var slot = GetSlot(slotId);
            if (slot == null) return AssignmentResult.SlotNotFound;
            if (!slot.Unlocked) return AssignmentResult.SlotLocked;

            var companion = Roster.Get(companionId);
            if (companion == null) return AssignmentResult.CompanionNotFound;

            // B4-аудит §4.5: Antagonist явно виключений (не «!= Dead») — той, хто пішов в
            // антагоністи, не стає назад на пост, навіть якщо формально живий.
            if (companion.IsDead || companion.Status == CompanionStatus.OnMission ||
                companion.Status == CompanionStatus.Antagonist)
                return AssignmentResult.CompanionUnavailable;

            // Пост загиблого вільний, навіть якщо звірка ще не пройшла.
            if (slot.IsOccupied && IsFallen(slot.AssignedCompanionId))
                slot.AssignedCompanionId = null;

            if (slot.IsOccupied && slot.AssignedCompanionId != companionId)
                return AssignmentResult.SlotOccupied;

            // Зняти з попереднього слота, якщо був призначений.
            if (companion.IsAssigned && companion.AssignedSlotId != slotId)
                Unassign(companion.AssignedSlotId);

            slot.AssignedCompanionId = companionId;
            companion.AssignedSlotId = slotId;
            if (companion.Status == CompanionStatus.Idle)
                companion.Status = CompanionStatus.Assigned;
            // G26 (ревю B7): Resting — це "вільний і лікується", а не ярлик,
            // який переживає призначення на пост. Той, хто тримає пост,
            // працює крізь рану (Injured), а не відпочиває — призначення
            // зобов'язане повернути статус до Injured, інакше ярлик застряг би на
            // Resting до повного одужання навіть у зайнятого постом.
            else if (companion.Status == CompanionStatus.Resting)
                companion.Status = CompanionStatus.Injured;

            return AssignmentResult.Success;
        }

        /// <summary>Знімає призначення зі слота (якщо зайнятий).</summary>
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
        /// Звільняє пости, які тримають загиблі (або ті, кого вже немає в
        /// ростері). Смерть приходить з різних місць — криза, вилазка, згодом бій, —
        /// і не кожне з них знає про базу; тому база звіряється сама: перед
        /// кожним циклом, перед розстановкою господаря і при призначенні на такий пост.
        ///
        /// Без цього загиблий на посту виробляв би вічно, а пост не можна було б віддати
        /// живому: слот вважався зайнятим (аудит розривів, G17).
        ///
        /// B4-фікс ревю: та сама діра повторювалась для Antagonist — перехід в
        /// антагоністи (<see cref="Game.Core.Companions.Defection.Defect"/>)
        /// без явного посилання на цю базу чистить лише сторону напарника
        /// (<c>Companion.AssignedSlotId</c>), а бухгалтерія слота
        /// (<c>AssignmentSlot.AssignedCompanionId</c>) не знає про відхід і
        /// лишалась зайнятою назавжди — пост не можна віддати живому, а
        /// дефектор продовжував би виробляти й отримувати досвід з поста
        /// (AdvanceCycle нижче звіряється через той самий ReleaseFallen). IsFallen
        /// явно включає Antagonist — не «!= Dead», як і всюди в аудиті §4.5.
        /// Повертає, скільки постів звільнено.
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

        /// <summary>
        /// Перезбирає бухгалтерію слотів (<see cref="AssignmentSlot.AssignedCompanionId"/>)
        /// з уже відновленого ростера (<c>Companion.AssignedSlotId</c>).
        ///
        /// Знайдено 25.09.2026 (лід, відтворено): відновлення сейву у
        /// СВІЖИЙ екземпляр <see cref="GameSession"/> розходилось з безперервною
        /// грою — пости давали вироблення/XP не тому, а деякі не давали
        /// нічого зовсім. Причина: «хто на якому посту» зберігається ДВІЧІ — на
        /// стороні напарника (<c>Companion.AssignedSlotId</c>, персистить
        /// <c>RosterAdapter</c>) і на стороні слота (це поле, персистить лише
        /// <see cref="TryAssign"/> під час гри). <see cref="CaptureState"/>/
        /// <see cref="RestoreState"/> цього класу знають тільки про гаманець/
        /// розблоковані слоти/рівні — сторону слота ніхто не пише і не
        /// читає. На тій самій сесії (той самий живий об'єкт BaseState) це не
        /// видно: слоти вже позначені правильно з моменту призначення. На
        /// СВІЖОМУ BaseState (NewGame() перед ApplySave, ContinueGame,
        /// RestoreFromBlob) кожен слот починається порожнім — <see cref="AdvanceCycle"/>
        /// тихо пропускає зайнятий (за напарником) пост, вважаючи його вільним
        /// (аудит: "storehouse_dock" при цьому випадково лишався правильним —
        /// ЄДИНИЙ пост, чиє призначення захардкожене у
        /// <c>FirstHourWorld.Build</c>, а не прийшло з сейву).
        ///
        /// Викликач — <c>GameSession.ApplySave</c>, ОДРАЗУ після
        /// <c>_processor.RestoreState(corePart)</c> (той відновлює і
        /// ростер, і економіку): без цього порядку <c>Companion.AssignedSlotId</c>
        /// ще не встиг оновитися, і перезбирання спрацювало б на старих даних.
        /// </summary>
        internal void RestoreSlotOccupancy()
        {
            for (int i = 0; i < _slots.Count; i++)
                _slots[i].AssignedCompanionId = null;

            foreach (var c in Roster.All)
            {
                if (string.IsNullOrEmpty(c.AssignedSlotId)) continue;
                var slot = GetSlot(c.AssignedSlotId);
                if (slot != null) slot.AssignedCompanionId = c.Id;
            }
        }

        // ---- зліпок господарства (Foundation/A1) ----
        //
        // g:<золото>|m:<матеріали>|f:<їжа>|u:<відкриті слоти через ','>|
        // x:<companionId:рівень:досвід через ',' >|h:<0|1>
        // Без «;» і внутрішнього «=» — зовнішній зліпок (SettlementSave) ріже за
        // ними на своєму рівні. Звичайні публічні методи, не реалізація
        // IStateBlob — див. коментар класу; у порт їх загортає EconomyBlob.

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

            // Знайдено 25.09.2026 (відновлення у свіжу сесію розходилось
            // з безперервною грою): прапорець голоду минулого циклу впливає на
            // вироблення/XP НАСТУПНОГО циклу (Поправка №4, WasHungryLastCycle
            // вище) — без нього завантаження безкарно знімало штраф голоду,
            // накладений прямо перед збереженням.
            sb.Append("|h:").Append(WasHungryLastCycle ? 1 : 0);

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
                    case 'h':
                        WasHungryLastCycle = body == "1";
                        break;
                }
            }
        }

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        // ---- Просування часу ----

        /// <summary>
        /// Один цикл (день): вироблення з усіх зайнятих слотів, нарахування
        /// рольового досвіду, природне лікування і витрата їжі поселенням.
        ///
        /// INTERNAL навмисно. Єдиний легальний викликач — ProductionStep
        /// усередині денного конвеєра; Game.Gameplay лежить в іншій збірці і
        /// фізично не може покликати цей метод в обхід. Поки він був публічним,
        /// демка бази крутила його напряму, і в проєкті існувало два денних
        /// цикли, які не знали один про одного.
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
                // B4-фікс ревю: ReleaseFallen() трохи вище вже звільняє
                // слот антагоніста (той самий IsFallen), але допуск до вироблення
                // тримаємо явним списком — «не Dead» не виключає Antagonist
                // за побудовою enum, а слот, відпущений цим самим тіком, сюди
                // і не потрапить (IsOccupied вже false); явна перевірка — той самий
                // стиль захисту, що і в TryAssign/Steward.Staff (§4.5).
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
                        // Позиція без виходу — не помилка і не заглушка на час
                        // налагодження: за GDD у майстерні й лабораторії функція —
                        // крафт, а крафту ще немає. Рольовий досвід при цьому йде:
                        // людина на посту все одно працює.
                        break;
                }

                int xp = ProductionCalculator.RoleXpPerCycle(companion, def, Balance);
                if (WasHungryLastCycle) xp = (int)Math.Round(xp * Balance.HungryRoleXpMultiplier, MidpointRounding.ToEven);

                // R11: постова XP протагоніста банкується так само, як бойова/
                // квестова/інцидентна (GameSession.GrantXp) — інакше призначення
                // протагоніста на пост тихо обходило б R11 і витрачало його очки
                // автоматично (seamsForD1 B7: "функционально мёртв" без цієї гілки).
                if (!string.IsNullOrEmpty(ProtagonistId) && string.Equals(companion.Id, ProtagonistId, StringComparison.Ordinal))
                {
                    var pres = companion.GainXpNoAutoSpend(xp, Balance);
                    if (pres.LeveledUp)
                    {
                        report.LeveledUp.Add(companion.Id);
                        report.ProtagonistLevelsGained += pres.LevelsGained;
                    }
                }
                else
                {
                    var lvl = companion.GainXp(xp, Balance);
                    if (lvl.LeveledUp)
                        report.LeveledUp.Add(companion.Id);
                }
            }

            ApplyNaturalHealing(report);
            ApplyFoodUpkeep(report);
            return report;
        }

        /// <summary>
        /// Розподіляє лікувальні очки по найважче поранених.
        ///
        /// G26: поки лікування в лазареті йде, а сама рана ще не закрилась,
        /// вільний (не тримає пост) поранений переходить у Resting — статус
        /// був оголошений, але ніколи не присвоювався. Той, хто продовжує
        /// працювати крізь рану (пост тримає — <c>InjuredCompanion_ProducesLess</c>),
        /// лишається Injured: він не «відпочиває», він працює з пенальті.
        /// </summary>
        private void ApplyHealing(int healing, CycleReport report)
        {
            if (healing <= 0) return;
            double pool = healing;
            // Спочатку тим, у кого більше InjuryPoints.
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

        /// <summary>Базове природне відновлення всіх поранених за цикл.</summary>
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
            // Їдять живі: загиблий у ростері лишається (пам'ять, брижі), але не їсть.
            int living = 0;
            foreach (var c in Roster.All)
                if (!c.IsDead) living++;
            int upkeep = Balance.FoodUpkeepPerCompanion * living;
            if (upkeep <= 0) { WasHungryLastCycle = false; return; }
            if (!Resources.TrySpend(ResourceType.Food, upkeep))
            {
                // Голодний день: залишки з'їдені дочиста, база просяде наступним
                // циклом, а Напругу підійме крок дня HungerStep (Поправка №4).
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
