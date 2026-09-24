using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Balance;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Pressure;
using Game.Core.Signals;

namespace Game.Core.Base
{
    public enum BuildOrderResult
    {
        Started = 0,
        UnknownBuilding,
        AlreadyBuilt,
        AlreadyInProgress,
        QuestOnly,
        NotEnoughGold,
        NotEnoughMaterials
    }

    public enum CouncilOrderResult
    {
        Queued = 0,
        NoCouncilHall,
        AlreadyQueued,
        OnCooldown,
        NotEnoughGold,
        NotEnoughFood,

        // ---- B5: Фракции + указы рады ----
        /// <summary>Эффект применился сразу (Указ/Дипломатия/Підготовка/Спорядження), а не встал в очередь суток.</summary>
        Applied,
        /// <summary>Цель дипломатии/указу не зареєстрована в FactionRegistry.</summary>
        UnknownFaction,
        /// <summary>Инвестиция просит здание, которого нет.</summary>
        BuildingNotBuilt
    }

    /// <summary>
    /// Городские работы: что построено, что строится, что приказал совет и кто
    /// пришёл извне (Поправка №6).
    ///
    /// Всё, что игрок заказывает, ОПЛАЧИВАЕТСЯ СРАЗУ, а исполняется конвейером
    /// дня — шагом <see cref="CityWorksStep"/>. Причина: изменение Напряжения
    /// или населения в промежутке между сутками не попало бы ни в один отчёт, и
    /// игрок увидел бы сдвиг без причины. Всё, что двигает город, обязано
    /// случиться внутри суток и прозвучать.
    /// </summary>
    public sealed class CityWorks : Loop.IStateBlob
    {
        private sealed class Project
        {
            public string Id;
            public int DaysLeft;
            public int TotalDays;
        }

        private readonly HashSet<string> _built = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Project> _projects = new List<Project>();

        private bool _raidQueued;
        private int _lastRaidDay = int.MinValue / 2;
        private int _settlersQueued;
        private int _lastSettlersDay = int.MinValue / 2;
        private int _arrivalsQueued;

        // ---- B5: Фракции + указы рады (R5, AUDIT П8/G12/G20) ----
        private int _lastDecreeDay = int.MinValue / 2;
        private int _lastDiplomacyDay = int.MinValue / 2;
        private int _lastPrepareThreatDay = int.MinValue / 2;
        private int _investmentGoldPerDay;
        private int _investmentDaysLeft;
        /// <summary>Маркеры готовности для B6/D1 — этот пакет ReadinessTrack не заводит (§1.1).</summary>
        private int _readinessMilestonesQueued;
        private ExpeditionOutfitBuff _pendingOutfitBuff;

        /// <summary>
        /// Ревью-фикс (major): Напруга от Указа копится ЗДЕСЬ, а не через
        /// DayProcessor.QueueExternal. Очередь QueueExternal (_externalTension)
        /// не входит в SettlementSave.Capture/Restore — а Order* и SaveState
        /// оба легальны в фазе Morning (docs/TEST_BUILD.md §4.1), поэтому
        /// «Указ → SaveState → загрузка» тихо съедало бы уплаченный сдвиг
        /// Напруги, притом что золото, Уклад и фракции из того же вызова уже
        /// применились и сохранились. CityWorks сам входит в слепок (см.
        /// CaptureState/RestoreState), поэтому поле переживает сохранение —
        /// применяет его CityWorksStep.Execute тем же приёмом, каким Облава
        /// уже кладёт CouncilRaid прямо в ctx.Tension, а не через очередь.
        /// </summary>
        private int _pendingCouncilEdictTension;

        /// <summary>
        /// Ревью-фикс: Указ/Дипломатия/Подготовка/Снаряжение применяются СРАЗУ
        /// (CouncilOrderResult.Applied), в отличие от Облавы/Переселенцев/Инвестиции,
        /// которых исполняет и объявляет CityWorksStep. Без этой очереди «применилось
        /// сразу» означало «применилось молча» — в лупе не было ни одного
        /// Game.Core.Signals.CityEvent на эти пять действий (docs/TEST_BUILD.md §2
        /// строка 15, §7.13). Очередь — тем же приёмом, каким уже собраны
        /// _pendingOutfitBuff/_readinessMilestonesQueued: копится здесь, забирается
        /// и звучит в CityWorksStep.Execute в тот же (или ближайший) дневной шаг.
        /// </summary>
        private readonly List<CityEvent> _pendingCouncilAnnouncements = new List<CityEvent>();

        /// <summary>Рынок поселения — единственная позиция, торговый подход к которой скидывает цену (AUDIT G12).</summary>
        public const string MarketSlotId = "settlement_market";

        /// <summary>
        /// Разовый бонус следующей вилазке (OrderOutfitExpedition). Данные, а не
        /// тип B7 — R15 держит саму вилазку у пакета B7 целиком; здесь только
        /// сид её входа, который D1 когда-нибудь заберёт и применит.
        /// </summary>
        public sealed class ExpeditionOutfitBuff
        {
            public string SiteId;
            public int BonusValue;
        }

        public CityWorks(IEnumerable<string> alreadyBuilt = null)
        {
            if (alreadyBuilt != null)
                foreach (var id in alreadyBuilt)
                    if (!string.IsNullOrEmpty(id)) _built.Add(id);
        }

        public bool Has(string buildingId)
        {
            return buildingId != null && _built.Contains(buildingId);
        }

        public bool IsBuilding(string buildingId)
        {
            return FindProject(buildingId) != null;
        }

        public IEnumerable<string> Built
        {
            get { return _built; }
        }

        /// <summary>
        /// Видимая стадия постройки 0..5 (US-7.3): 0 — не начата, 1–4 — леса,
        /// 5 — готова. Стадия — чистая функция от прошедших суток, отдельного
        /// геймплея на стадиях нет.
        /// </summary>
        public int StageOf(string buildingId)
        {
            if (Has(buildingId)) return 5;

            var p = FindProject(buildingId);
            if (p == null) return 0;

            int elapsed = p.TotalDays - p.DaysLeft;
            int stage = 1 + (elapsed * 4) / Math.Max(1, p.TotalDays);
            return stage > 4 ? 4 : stage;
        }

        // ================= заказы игрока =================

        /// <summary>
        /// Заложить здание: цена списывается сразу, стройка идёт по суткам.
        ///
        /// <paramref name="today"/>/<paramref name="balance"/> — необязательны
        /// (по умолчанию цена без скидки, как раньше): без них AUDIT G12 не
        /// работает, но старые вызовы не ломаются. С ними — золото скидывается,
        /// если рынок поселения занят и открыт (см. <see cref="TradeDiscount"/>).
        /// </summary>
        public BuildOrderResult Order(string buildingId, BaseState state, int today = 0, BalanceConfig balance = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var def = DefaultBuildings.Get(buildingId);
            if (def == null) return BuildOrderResult.UnknownBuilding;
            if (Has(buildingId)) return BuildOrderResult.AlreadyBuilt;
            if (IsBuilding(buildingId)) return BuildOrderResult.AlreadyInProgress;
            if (def.QuestOnly) return BuildOrderResult.QuestOnly;

            int goldCost = balance != null
                ? DiscountedPrice(def.GoldCost, TradeDiscount(state, balance, today))
                : def.GoldCost;

            // Сначала проверяем обе цены, потом списываем: иначе при нехватке
            // материалов золото ушло бы, а стройка не началась.
            if (!state.Resources.CanAfford(ResourceType.Gold, goldCost))
                return BuildOrderResult.NotEnoughGold;
            if (!state.Resources.CanAfford(ResourceType.Materials, def.MaterialsCost))
                return BuildOrderResult.NotEnoughMaterials;

            state.Resources.TrySpend(ResourceType.Gold, goldCost);
            state.Resources.TrySpend(ResourceType.Materials, def.MaterialsCost);

            _projects.Add(new Project { Id = def.Id, DaysLeft = Math.Max(1, def.Days), TotalDays = Math.Max(1, def.Days) });
            return BuildOrderResult.Started;
        }

        /// <summary>
        /// Облава: разовое снижение Напряжения драйвером CouncilRaid. Требует
        /// Зал совета, стоит золота, имеет откат.
        ///
        /// <paramref name="factions"/> необязателен (по умолчанию — как до B5,
        /// без фракций): силовой метод задевает и отношения — бояри Тугара
        /// довольны порядком, громаде не нравится нагайка на своих (ревью-фикс,
        /// см. тест Raid_LowersTension_PaysCosts_ShiftsFactions). Сдвиг —
        /// разовый, сразу; сама облава по-прежнему исполняется и звучит
        /// CityWorksStep на её собственный день (TakeRaid).
        /// </summary>
        public CouncilOrderResult OrderRaid(BaseState state, int today, BalanceConfig balance, FactionRegistry factions = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            if (!Has(DefaultBuildings.CouncilHall)) return CouncilOrderResult.NoCouncilHall;
            if (_raidQueued) return CouncilOrderResult.AlreadyQueued;
            if (!RaidReady(today, balance)) return CouncilOrderResult.OnCooldown;

            int raidPrice = DiscountedPrice(balance.City.RaidGoldCost, TradeDiscount(state, balance, today));
            if (!state.Resources.TrySpend(ResourceType.Gold, raidPrice))
                return CouncilOrderResult.NotEnoughGold;

            _raidQueued = true;

            if (factions != null)
                new SocialConsequence()
                    .Faction(DefaultFactions.TuharBoyars, balance.Faction.RaidFactionFavoredDelta)
                    .Faction(DefaultFactions.Community, -balance.Faction.RaidFactionCostDelta)
                    .Apply(factions, null);

            return CouncilOrderResult.Queued;
        }

        public bool RaidReady(int today, BalanceConfig balance)
        {
            return today - _lastRaidDay >= balance.Tension.RaidCooldownDays;
        }

        /// <summary>
        /// Приём переселенцев — «рішення в місті» из слов владельца. Платится
        /// едой: новых ртов надо кормить, и это честная цена роста.
        /// </summary>
        public CouncilOrderResult OrderSettlers(BaseState state, int today, BalanceConfig balance)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            if (!Has(DefaultBuildings.CouncilHall)) return CouncilOrderResult.NoCouncilHall;
            if (_settlersQueued > 0) return CouncilOrderResult.AlreadyQueued;
            if (today - _lastSettlersDay < balance.City.SettlersCooldownDays) return CouncilOrderResult.OnCooldown;
            if (!state.Resources.TrySpend(ResourceType.Food, balance.City.SettlersFoodCost))
                return CouncilOrderResult.NotEnoughFood;

            _settlersQueued = balance.City.SettlersPerOrder;
            return CouncilOrderResult.Queued;
        }

        /// <summary>Люди, найденные вылазкой: придут в город ближайшими сутками.</summary>
        public void QueueArrivals(int people)
        {
            if (people > 0) _arrivalsQueued += people;
        }

        // ================= B5: указы рады + фракции (R5, AUDIT П8/G12/G20) =================
        //
        // Указ/Дипломатия/Підготовка/Спорядження применяются СРАЗУ, а не через
        // CityWorksStep: DayProcessor.QueueExternal для того и заведён (комментарий
        // на самом методе) — принять заявку из внешней системы в любой момент
        // между сутками, не дожидаясь шага конвейера. Инвестиция — исключение:
        // она платит золото РАСТЯНУТО по суткам, и это как раз работа
        // CityWorksStep (см. TakeInvestmentPayout).

        /// <summary>
        /// Указ: двигает Уклад (DayProcessor.OrderLevel) на шаг и отдаёт одну
        /// фракцию в выгоду ценой другой (AUDIT П8+G20 — Уклад раньше двигать
        /// было решительно нечем, а понижающий драйвер CouncilEdict стоял в
        /// белом списке без единого вызова). costFactionId необязателен: без
        /// него указ просто поднимает выгодную фракцию, не трогая остальные.
        ///
        /// Ревью-фикс: favoredFactionId ОБЯЗАН быть зарегистрирован в
        /// factions — иначе указ («поменять одну фракцию на другую») спишет
        /// золото, толкнёт Уклад и молча не поменяет ни одной фракции.
        /// Проверка — до всех трат, тем же приёмом, что уже стоит в
        /// OrderDiplomacy.
        /// </summary>
        public CouncilOrderResult OrderDecree(BaseState state, Loop.DayProcessor processor, FactionRegistry factions,
            string favoredFactionId, string costFactionId, int today, BalanceConfig balance)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (processor == null) throw new ArgumentNullException(nameof(processor));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            if (!Has(DefaultBuildings.CouncilHall)) return CouncilOrderResult.NoCouncilHall;
            if (factions == null || factions.Get(favoredFactionId) == null) return CouncilOrderResult.UnknownFaction;
            if (!string.IsNullOrEmpty(costFactionId) && factions.Get(costFactionId) == null)
                return CouncilOrderResult.UnknownFaction;
            if (today - _lastDecreeDay < balance.Faction.DecreeCooldownDays) return CouncilOrderResult.OnCooldown;

            int price = DiscountedPrice(balance.Faction.DecreeGoldCost, TradeDiscount(state, balance, today));
            if (!state.Resources.TrySpend(ResourceType.Gold, price))
                return CouncilOrderResult.NotEnoughGold;

            _lastDecreeDay = today;

            processor.OrderLevel = ClampOrderLevel(processor.OrderLevel + balance.Faction.DecreeOrderLevelStep);

            // Ревью-фикс: факции двигаются через SocialConsequence — единственную
            // точку, где список разрешённых драйверов реально проверяется
            // (allow-list иначе был мёртвым кодом для этого места). Напругу
            // SocialConsequence.Apply НЕ отдаём processor'у (см. комментарий на
            // _pendingCouncilEdictTension) — копим её сами и применяем в
            // CityWorksStep.Execute тем же днём, чтобы она переживала сейв.
            string costTarget = !string.IsNullOrEmpty(costFactionId) && costFactionId != favoredFactionId
                ? costFactionId
                : null;
            var socialConsequence = new SocialConsequence()
                .Faction(favoredFactionId, balance.Faction.DecreeFactionDelta)
                .Faction(costTarget, -balance.Faction.DecreeFactionDelta)
                .Tension(TensionDriver.CouncilEdict, -Math.Abs(balance.Faction.DecreeTensionDelta));
            socialConsequence.Apply(factions, null);
            _pendingCouncilEdictTension += socialConsequence.Amount;

            _pendingCouncilAnnouncements.Add(new CityEvent("council.decree.ordered", SignalUrgency.Notable,
                "favored:" + favoredFactionId, "cost:" + (costTarget ?? string.Empty)));

            return CouncilOrderResult.Applied;
        }

        /// <summary>Дипломатия: чистое улучшение одной фракции, без Уклада и без Напруги.</summary>
        public CouncilOrderResult OrderDiplomacy(BaseState state, FactionRegistry factions, string factionId,
            int today, BalanceConfig balance)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            if (!Has(DefaultBuildings.CouncilHall)) return CouncilOrderResult.NoCouncilHall;
            if (factions == null || factions.Get(factionId) == null) return CouncilOrderResult.UnknownFaction;
            if (today - _lastDiplomacyDay < balance.Faction.DiplomacyCooldownDays) return CouncilOrderResult.OnCooldown;

            int price = DiscountedPrice(balance.Faction.DiplomacyGoldCost, TradeDiscount(state, balance, today));
            if (!state.Resources.TrySpend(ResourceType.Gold, price))
                return CouncilOrderResult.NotEnoughGold;

            _lastDiplomacyDay = today;
            factions.ApplySocialConsequence(factionId, balance.Faction.DiplomacyFactionDelta);

            _pendingCouncilAnnouncements.Add(new CityEvent("council.diplomacy.ordered", SignalUrgency.Notable,
                "faction:" + factionId));

            return CouncilOrderResult.Applied;
        }

        /// <summary>
        /// Инвестиция: платит золото сразу, а возвращает больше — растянуто по
        /// суткам (TakeInvestmentPayout, шаг CityWorksStep). buildingId — не
        /// декорация: инвестировать можно только в уже готовое здание.
        /// </summary>
        public CouncilOrderResult OrderInvestment(BaseState state, string buildingId, int today, BalanceConfig balance)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            if (!Has(DefaultBuildings.CouncilHall)) return CouncilOrderResult.NoCouncilHall;
            if (!string.IsNullOrEmpty(buildingId) && !Has(buildingId)) return CouncilOrderResult.BuildingNotBuilt;
            if (_investmentDaysLeft > 0) return CouncilOrderResult.AlreadyQueued;

            int price = DiscountedPrice(balance.Faction.InvestmentGoldCost, TradeDiscount(state, balance, today));
            if (!state.Resources.TrySpend(ResourceType.Gold, price))
                return CouncilOrderResult.NotEnoughGold;

            _investmentGoldPerDay = balance.Faction.InvestmentGoldPerDay;
            _investmentDaysLeft = balance.Faction.InvestmentDays;
            return CouncilOrderResult.Queued;
        }

        /// <summary>
        /// Подготовка к угрозе: копит МАРКЕРЫ готовности, не саму Готовность —
        /// ReadinessTrack заводит B6, этот пакет от него не зависит (§1.1). D1
        /// заберёт накопленное через TakeReadinessMilestones, когда трек появится.
        /// </summary>
        public CouncilOrderResult OrderPrepareThreat(BaseState state, int today, BalanceConfig balance)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            if (!Has(DefaultBuildings.CouncilHall)) return CouncilOrderResult.NoCouncilHall;
            if (today - _lastPrepareThreatDay < balance.Faction.PrepareThreatCooldownDays) return CouncilOrderResult.OnCooldown;

            int price = DiscountedPrice(balance.Faction.PrepareThreatGoldCost, TradeDiscount(state, balance, today));
            if (!state.Resources.TrySpend(ResourceType.Gold, price))
                return CouncilOrderResult.NotEnoughGold;

            _lastPrepareThreatDay = today;
            _readinessMilestonesQueued++;

            _pendingCouncilAnnouncements.Add(new CityEvent("council.prepare_threat.ordered", SignalUrgency.Notable));

            return CouncilOrderResult.Applied;
        }

        /// <summary>
        /// Снаряжение экспедиции: разовый бонус следующей вилазке на площадку
        /// siteId. Данные, а не тип B7 (R15 держит вилазку целиком) — сид её
        /// входа, который заберёт D1 через TakeExpeditionOutfitBuff.
        /// </summary>
        public CouncilOrderResult OrderOutfitExpedition(BaseState state, string siteId, int today, BalanceConfig balance)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            if (!Has(DefaultBuildings.CouncilHall)) return CouncilOrderResult.NoCouncilHall;
            if (_pendingOutfitBuff != null) return CouncilOrderResult.AlreadyQueued;

            int price = DiscountedPrice(balance.Faction.OutfitExpeditionGoldCost, TradeDiscount(state, balance, today));
            if (!state.Resources.TrySpend(ResourceType.Gold, price))
                return CouncilOrderResult.NotEnoughGold;

            _pendingOutfitBuff = new ExpeditionOutfitBuff { SiteId = siteId, BonusValue = balance.Faction.OutfitExpeditionBonusValue };

            _pendingCouncilAnnouncements.Add(new CityEvent("council.outfit_expedition.ordered", SignalUrgency.Notable,
                "site:" + (siteId ?? string.Empty)));

            return CouncilOrderResult.Applied;
        }

        /// <summary>
        /// AUDIT G12: показанный подходом Торговли множитель цены
        /// (CheckOutcome.PriceMultiplier) до этого пакета считался и
        /// выбрасывался (CheckResolver.Resolve его вычисляет, но никто не читал).
        /// Здесь он наконец решает цену стройки и указов рады — но только когда
        /// рынок поселения занят и открыт: без профильного человека на посту
        /// скидки нет (множитель 1.0, тот же путь, что и раньше).
        /// </summary>
        public static double TradeDiscount(BaseState state, BalanceConfig balance, int today)
        {
            if (state == null || balance == null) return 1.0;

            var slot = state.GetSlot(MarketSlotId);
            if (slot == null || !slot.Unlocked || !slot.IsOccupied) return 1.0;

            var roster = new RosterAdapter(state.Roster, null, balance);
            var request = new CheckRequest(SkillKeys.Trade, balance.Faction.TradeDiscountThreshold,
                ApproachForm.Trade, topicId: "council.trade_discount", requiredPositionId: MarketSlotId);
            var outcome = CheckResolver.Resolve(request, roster, null, today, balance);
            return outcome.PriceMultiplier;
        }

        private static int DiscountedPrice(int basePrice, double multiplier)
        {
            if (basePrice <= 0) return 0;
            int price = (int)Math.Round(basePrice * multiplier, MidpointRounding.AwayFromZero);
            return price < 0 ? 0 : price;
        }

        private static int ClampOrderLevel(int level)
        {
            if (level < 1) return 1;
            return level > 4 ? 4 : level;
        }

        /// <summary>Растянутая выплата Инвестиции — вызывает CityWorksStep каждые сутки.</summary>
        internal int TakeInvestmentPayout()
        {
            if (_investmentDaysLeft <= 0) return 0;
            _investmentDaysLeft--;
            return _investmentGoldPerDay;
        }

        /// <summary>
        /// Забирает и обнуляет накопленную Напругу Указа (ревью-фикс, см.
        /// _pendingCouncilEdictTension). Вызывается CityWorksStep тем же
        /// приёмом, каким она уже забирает TakeRaid/TakeSettlers/...
        /// </summary>
        internal int TakeCouncilEdictTension()
        {
            int v = _pendingCouncilEdictTension;
            _pendingCouncilEdictTension = 0;
            return v;
        }

        /// <summary>Забирает и обнуляет накопленные маркеры готовности (для D1/B6).</summary>
        internal int TakeReadinessMilestones()
        {
            int n = _readinessMilestonesQueued;
            _readinessMilestonesQueued = 0;
            return n;
        }

        /// <summary>
        /// Подсматривает разовый бонус снаряжения БЕЗ его снятия (фикс-ревью):
        /// D1 (DepartExpedition) должен решить, применим ли бонус К ЭТОМУ
        /// отправлению (siteId совпадает с заказанным), ДО того, как заберёт
        /// его — иначе TakeExpeditionOutfitBuff() снимал бонус безусловно на
        /// первом же отправлении, даже на другую площадку, и он терялся
        /// навсегда без единого шанса быть применённым туда, куда заказан.
        /// </summary>
        internal ExpeditionOutfitBuff PeekExpeditionOutfitBuff() => _pendingOutfitBuff;

        /// <summary>Забирает и обнуляет разовый бонус снаряжения (для D1) — только когда он уже применяется (см. PeekExpeditionOutfitBuff).</summary>
        internal ExpeditionOutfitBuff TakeExpeditionOutfitBuff()
        {
            var b = _pendingOutfitBuff;
            _pendingOutfitBuff = null;
            return b;
        }

        /// <summary>
        /// Забирает и обнуляет очередь объявлений об Указе/Дипломатии/Підготовці/
        /// Спорядженні — вызывается CityWorksStep, тем же приёмом, каким она уже
        /// забирает TakeRaid/TakeSettlers/TakeArrivals/TakeInvestmentPayout.
        /// </summary>
        internal List<CityEvent> TakeCouncilAnnouncements()
        {
            if (_pendingCouncilAnnouncements.Count == 0) return null;
            var events = new List<CityEvent>(_pendingCouncilAnnouncements);
            _pendingCouncilAnnouncements.Clear();
            return events;
        }

        // ================= исполнение внутри суток =================

        internal List<string> AdvanceConstruction()
        {
            var done = new List<string>();
            for (int i = _projects.Count - 1; i >= 0; i--)
            {
                var p = _projects[i];
                p.DaysLeft--;
                if (p.DaysLeft > 0) continue;

                _built.Add(p.Id);
                done.Add(p.Id);
                _projects.RemoveAt(i);
            }
            // Детерминированный порядок сообщений: по идентификатору, а не по
            // тому, в каком порядке их заложили.
            done.Sort(StringComparer.Ordinal);
            return done;
        }

        internal bool TakeRaid(int today)
        {
            if (!_raidQueued) return false;
            _raidQueued = false;
            _lastRaidDay = today;
            return true;
        }

        internal int TakeSettlers(int today)
        {
            int n = _settlersQueued;
            _settlersQueued = 0;
            if (n > 0) _lastSettlersDay = today;
            return n;
        }

        internal int TakeArrivals()
        {
            int n = _arrivalsQueued;
            _arrivalsQueued = 0;
            return n;
        }

        /// <summary>
        /// Замки постов по зданиям: пост без своего здания закрыт, человек на
        /// него не встанет. Посты без здания (поля, разведпост) не трогаем.
        /// </summary>
        public void ApplyToSlots(BaseState state)
        {
            if (state == null) return;

            foreach (var def in DefaultBuildings.All())
            {
                if (string.IsNullOrEmpty(def.OpensSlotId)) continue;
                var slot = state.GetSlot(def.OpensSlotId);
                if (slot != null) slot.Unlocked = Has(def.Id);
            }
        }

        private Project FindProject(string id)
        {
            for (int i = 0; i < _projects.Count; i++)
                if (_projects[i].Id == id) return _projects[i];
            return null;
        }

        // ================= слепок =================

        // Формат без «;» и «=» — внешний слепок режет по ним.
        // b:<id>,<id>|p:<id>:<left>:<total>,...|r:<queued>:<lastDay>|s:<n>|a:<n>
        // B5 (AUDIT П8/G12/G20, аддитивно): |d:<lastDecreeDay>|y:<lastDiplomacyDay>
        // |t:<lastPrepareThreatDay>|i:<goldPerDay>:<daysLeft>|m:<milestonesQueued>
        // |o:<siteId>:<bonusValue> (пусто — бонуса нет)
        // |e:<pendingCouncilEdictTension> (ревью-фикс: Напруга Указа переживает сейв)
        public string CaptureState()
        {
            var sb = new StringBuilder();

            var built = new List<string>(_built);
            built.Sort(StringComparer.Ordinal);
            sb.Append("b:").Append(string.Join(",", built.ToArray()));

            sb.Append("|p:");
            for (int i = 0; i < _projects.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var p = _projects[i];
                sb.Append(p.Id).Append(':').Append(p.DaysLeft.ToString(CultureInfo.InvariantCulture))
                  .Append(':').Append(p.TotalDays.ToString(CultureInfo.InvariantCulture));
            }

            sb.Append("|r:").Append(_raidQueued ? 1 : 0).Append(':')
              .Append(_lastRaidDay.ToString(CultureInfo.InvariantCulture));
            sb.Append("|s:").Append(_settlersQueued.ToString(CultureInfo.InvariantCulture))
              .Append(':').Append(_lastSettlersDay.ToString(CultureInfo.InvariantCulture));
            sb.Append("|a:").Append(_arrivalsQueued.ToString(CultureInfo.InvariantCulture));

            sb.Append("|d:").Append(_lastDecreeDay.ToString(CultureInfo.InvariantCulture));
            sb.Append("|y:").Append(_lastDiplomacyDay.ToString(CultureInfo.InvariantCulture));
            sb.Append("|t:").Append(_lastPrepareThreatDay.ToString(CultureInfo.InvariantCulture));
            sb.Append("|i:").Append(_investmentGoldPerDay.ToString(CultureInfo.InvariantCulture))
              .Append(':').Append(_investmentDaysLeft.ToString(CultureInfo.InvariantCulture));
            sb.Append("|m:").Append(_readinessMilestonesQueued.ToString(CultureInfo.InvariantCulture));
            sb.Append("|e:").Append(_pendingCouncilEdictTension.ToString(CultureInfo.InvariantCulture));
            sb.Append("|o:");
            if (_pendingOutfitBuff != null)
                sb.Append(_pendingOutfitBuff.SiteId ?? string.Empty).Append(':')
                  .Append(_pendingOutfitBuff.BonusValue.ToString(CultureInfo.InvariantCulture));

            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            _built.Clear();
            _projects.Clear();
            _raidQueued = false;
            _lastRaidDay = int.MinValue / 2;
            _settlersQueued = 0;
            _lastSettlersDay = int.MinValue / 2;
            _arrivalsQueued = 0;

            _lastDecreeDay = int.MinValue / 2;
            _lastDiplomacyDay = int.MinValue / 2;
            _lastPrepareThreatDay = int.MinValue / 2;
            _investmentGoldPerDay = 0;
            _investmentDaysLeft = 0;
            _readinessMilestonesQueued = 0;
            _pendingOutfitBuff = null;
            _pendingCouncilEdictTension = 0;

            if (string.IsNullOrEmpty(blob)) return;

            foreach (var part in blob.Split('|'))
            {
                if (part.Length < 2 || part[1] != ':') continue;
                string body = part.Substring(2);

                switch (part[0])
                {
                    case 'b':
                        foreach (var id in body.Split(','))
                            if (id.Length > 0) _built.Add(id);
                        break;
                    case 'p':
                        foreach (var item in body.Split(','))
                        {
                            var f = item.Split(':');
                            if (f.Length < 3) continue;
                            _projects.Add(new Project { Id = f[0], DaysLeft = ParseInt(f[1]), TotalDays = ParseInt(f[2]) });
                        }
                        break;
                    case 'r':
                        var r = body.Split(':');
                        if (r.Length >= 2)
                        {
                            _raidQueued = ParseInt(r[0]) != 0;
                            _lastRaidDay = ParseInt(r[1]);
                        }
                        break;
                    case 's':
                        var sp = body.Split(':');
                        _settlersQueued = ParseInt(sp[0]);
                        if (sp.Length > 1) _lastSettlersDay = ParseInt(sp[1]);
                        break;
                    case 'a': _arrivalsQueued = ParseInt(body); break;
                    case 'd': _lastDecreeDay = ParseInt(body); break;
                    case 'y': _lastDiplomacyDay = ParseInt(body); break;
                    case 't': _lastPrepareThreatDay = ParseInt(body); break;
                    case 'i':
                        var ip = body.Split(':');
                        _investmentGoldPerDay = ParseInt(ip[0]);
                        if (ip.Length > 1) _investmentDaysLeft = ParseInt(ip[1]);
                        break;
                    case 'm': _readinessMilestonesQueued = ParseInt(body); break;
                    case 'e': _pendingCouncilEdictTension = ParseInt(body); break;
                    case 'o':
                        if (body.Length == 0) break;
                        var op = body.Split(':');
                        _pendingOutfitBuff = new ExpeditionOutfitBuff
                        {
                            SiteId = op[0],
                            BonusValue = op.Length > 1 ? ParseInt(op[1]) : 0
                        };
                        break;
                }
            }
        }

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }
}
