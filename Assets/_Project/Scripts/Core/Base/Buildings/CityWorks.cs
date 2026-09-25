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

        // ---- B5: Фракції + укази ради ----
        /// <summary>Ефект застосувався одразу (Указ/Дипломатія/Підготовка/Спорядження), а не став у чергу доби.</summary>
        Applied,
        /// <summary>Ціль дипломатії/указу не зареєстрована в FactionRegistry.</summary>
        UnknownFaction,
        /// <summary>Інвестиція просить будівлю, якої немає.</summary>
        BuildingNotBuilt
    }

    /// <summary>
    /// Міські роботи: що побудовано, що будується, що наказала рада і хто
    /// прийшов ззовні (Поправка №6).
    ///
    /// Усе, що гравець замовляє, ОПЛАЧУЄТЬСЯ ОДРАЗУ, а виконується конвеєром
    /// дня — кроком <see cref="CityWorksStep"/>. Причина: зміна Напруги
    /// або населення в проміжку між добами не потрапила б у жоден звіт, і
    /// гравець побачив би зсув без причини. Усе, що рухає місто, зобов'язане
    /// статися всередині доби і прозвучати.
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

        /// <summary>
        /// Поправка №7.7: у тестовій збірці кожна будівля будується рівно одну
        /// добу замість <see cref="BuildingDefinition.Days"/>. Це параметр
        /// конструктора, не мутне поле — режим гри не міняється всередині
        /// прогону, тому в <see cref="CaptureState"/>/<see cref="RestoreState"/>
        /// його немає.
        ///
        /// Фікс-ревью (мінор): гарантія «той, хто відновлює сейв, будує
        /// CityWorks з тим самим прапорцем, яким збирав світ» тримається
        /// ЛИШЕ для першого <c>GameSession.NewGame</c> у прогоні.
        /// <c>GameSession.ContinueGame</c> НЕ протягує збережений режим — він
        /// завжди викликає <c>NewGame</c> без явного
        /// <c>NewGameOptions.TestBuildOneDayConstruction</c>, тож
        /// перебудований <c>CityWorks</c> завжди отримує дефолт цього поля
        /// (тестова збірка, true), незалежно від режиму, в якому сейв
        /// насправді був створений. Наразі нешкідливо: жоден продакшн-шлях
        /// не створює сейв кампанії (false) через <c>GameSession</c> —
        /// кампанійний режим досі перевіряється лише прямими викликами
        /// <c>FirstHourWorld.Build</c>/<c>CityWorks</c> (CampaignPacingTests,
        /// SettlementSaveTests, FirstHourWorldTests), у обхід save/continue.
        /// Якщо колись кампанія піде через <c>GameSession</c>-сейви, прапорець
        /// доведеться протягнути окремо (напр. через метадані сейву або
        /// новий параметр <c>ContinueGame</c>) — ця гарантія сама собою не
        /// з'явиться.
        /// </summary>
        private readonly bool _oneDayConstruction;

        /// <summary>
        /// Тест-збірка (Поправка №7.8, п.3): показ ЕФЕКТИВНОГО терміну
        /// будівництва (1 доба замість <c>BuildingDefinition.Days</c>) на
        /// вкладці «Будівлі» потребує знати сам прапорець ІЗ інтерфейсу —
        /// інакше картка мовчки брехала б про термін, показуючи проєктне
        /// число, яке насправді ніколи не діє в цьому режимі (R17: жодного
        /// прихованого/невірного числа).
        /// </summary>
        public bool OneDayConstruction => _oneDayConstruction;

        private bool _raidQueued;
        private int _lastRaidDay = int.MinValue / 2;
        private int _settlersQueued;
        private int _lastSettlersDay = int.MinValue / 2;
        private int _arrivalsQueued;

        // ---- B5: Фракції + укази ради (R5, AUDIT П8/G12/G20) ----
        private int _lastDecreeDay = int.MinValue / 2;
        private int _lastDiplomacyDay = int.MinValue / 2;
        private int _lastPrepareThreatDay = int.MinValue / 2;
        private int _investmentGoldPerDay;
        private int _investmentDaysLeft;
        /// <summary>Маркери готовності для B6/D1 — цей пакет ReadinessTrack не заводить (§1.1).</summary>
        private int _readinessMilestonesQueued;
        private ExpeditionOutfitBuff _pendingOutfitBuff;

        /// <summary>
        /// Ревью-фікс (major): Напруга від Указу накопичується ТУТ, а не через
        /// DayProcessor.QueueExternal. Черга QueueExternal (_externalTension)
        /// не входить у SettlementSave.Capture/Restore — а Order* і SaveState
        /// обидва легальні у фазі Morning (docs/TEST_BUILD.md §4.1), тому
        /// «Указ → SaveState → завантаження» тихо з'їдало б сплачений зсув
        /// Напруги, тоді як золото, Уклад і фракції з того самого виклику вже
        /// застосувалися і збереглися. CityWorks сам входить у зліпок (див.
        /// CaptureState/RestoreState), тому поле переживає збереження —
        /// застосовує його CityWorksStep.Execute тим самим прийомом, яким Облава
        /// вже кладе CouncilRaid прямо в ctx.Tension, а не через чергу.
        /// </summary>
        private int _pendingCouncilEdictTension;

        /// <summary>
        /// Ревью-фікс: Указ/Дипломатія/Підготовка/Спорядження застосовуються ОДРАЗУ
        /// (CouncilOrderResult.Applied), на відміну від Облави/Переселенців/Інвестиції,
        /// які виконує й оголошує CityWorksStep. Без цієї черги «застосувалося
        /// одразу» означало б «застосувалося мовчки» — у лупі не було жодного
        /// Game.Core.Signals.CityEvent на ці п'ять дій (docs/TEST_BUILD.md §2
        /// рядок 15, §7.13). Черга — тим самим прийомом, яким уже зібрані
        /// _pendingOutfitBuff/_readinessMilestonesQueued: накопичується тут, забирається
        /// і звучить у CityWorksStep.Execute в той самий (або найближчий) денний крок.
        /// </summary>
        private readonly List<CityEvent> _pendingCouncilAnnouncements = new List<CityEvent>();

        /// <summary>Ринок поселення — єдина позиція, торговий підхід до якої скидає ціну (AUDIT G12).</summary>
        public const string MarketSlotId = "settlement_market";

        /// <summary>
        /// Разовий бонус наступній вилазці (OrderOutfitExpedition). Дані, а не
        /// тип B7 — R15 тримає саму вилазку в пакеті B7 цілком; тут лише
        /// сид її входу, який D1 колись забере і застосує.
        /// </summary>
        public sealed class ExpeditionOutfitBuff
        {
            public string SiteId;
            public int BonusValue;
        }

        /// <summary>
        /// <paramref name="oneDayConstruction"/> — Поправка №7.7, за
        /// замовчуванням false: прямі викликачі (тести, старий код) і надалі
        /// отримують проєктні строки будівництва з <see cref="DefaultBuildings"/>
        /// без явної згоди на тестову збірку. <see cref="FirstHourWorld.Build"/>
        /// — єдине місце, де він стає true за замовчуванням.
        /// </summary>
        public CityWorks(IEnumerable<string> alreadyBuilt = null, bool oneDayConstruction = false)
        {
            _oneDayConstruction = oneDayConstruction;
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
        /// Видима стадія будівництва 0..5 (US-7.3): 0 — не почата, 1–4 — риштування,
        /// 5 — готова. Стадія — чиста функція від минулих діб, окремого
        /// геймплею на стадіях немає.
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

        // ================= замовлення гравця =================

        /// <summary>
        /// Закласти будівлю: ціна списується одразу, будівництво йде добами.
        ///
        /// <paramref name="today"/>/<paramref name="balance"/> — необов'язкові
        /// (за замовчуванням ціна без знижки, як і раніше): без них AUDIT G12 не
        /// працює, але старі виклики не ламаються. З ними — золото скидається,
        /// якщо ринок поселення зайнятий і відкритий (див. <see cref="TradeDiscount"/>).
        ///
        /// Одна доба на будь-яку будівлю замість проєктних
        /// <see cref="BuildingDefinition.Days"/> — не параметр цього виклику, а
        /// режим самого <see cref="CityWorks"/> (див. <see cref="_oneDayConstruction"/>
        /// і конструктор): симуляційний харнес і <see cref="Steward"/> звуть
        /// цей метод на інстансі, зібраному без нього, і тому будують за
        /// проєктними строками.
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

            // Спочатку перевіряємо обидві ціни, потім списуємо: інакше за нестачі
            // матеріалів золото пішло б, а будівництво не почалося б.
            if (!state.Resources.CanAfford(ResourceType.Gold, goldCost))
                return BuildOrderResult.NotEnoughGold;
            if (!state.Resources.CanAfford(ResourceType.Materials, def.MaterialsCost))
                return BuildOrderResult.NotEnoughMaterials;

            state.Resources.TrySpend(ResourceType.Gold, goldCost);
            state.Resources.TrySpend(ResourceType.Materials, def.MaterialsCost);

            // Поправка №7.7: тестова збірка стирає проєктний строк — замовлення
            // уранці, готово до наступного ранку, незалежно від того, скільки
            // діб просить BuildingDefinition.Days (Храм/Укріплення/Лабораторія
            // просять аж 8–10 — саме вони й доходили до гравця надто пізно).
            int days = _oneDayConstruction ? 1 : Math.Max(1, def.Days);
            _projects.Add(new Project { Id = def.Id, DaysLeft = days, TotalDays = days });
            return BuildOrderResult.Started;
        }

        /// <summary>
        /// Облава: разове зниження Напруги драйвером CouncilRaid. Потребує
        /// Залу ради, коштує золота, має відкат.
        ///
        /// <paramref name="factions"/> необов'язковий (за замовчуванням — як до B5,
        /// без фракцій): силовий метод зачіпає і стосунки — бояри Тугара
        /// задоволені порядком, громаді не подобається нагайка на своїх (ревью-фікс,
        /// див. тест Raid_LowersTension_PaysCosts_ShiftsFactions). Зсув —
        /// разовий, одразу; сама облава як і раніше виконується і звучить
        /// CityWorksStep у свій власний день (TakeRaid).
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
        /// Приймання переселенців — «рішення в місті» зі слів власника. Платиться
        /// їжею: нові роти треба годувати, і це чесна ціна зростання.
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

        /// <summary>Люди, знайдені вилазкою: прийдуть у місто найближчими добами.</summary>
        public void QueueArrivals(int people)
        {
            if (people > 0) _arrivalsQueued += people;
        }

        // ================= B5: укази ради + фракції (R5, AUDIT П8/G12/G20) =================
        //
        // Указ/Дипломатія/Підготовка/Спорядження застосовуються ОДРАЗУ, а не через
        // CityWorksStep: DayProcessor.QueueExternal для того й заведений (коментар
        // на самому методі) — прийняти заявку від зовнішньої системи в будь-який момент
        // між добами, не чекаючи кроку конвеєра. Інвестиція — виняток:
        // вона платить золото РОЗТЯГНУТО по добах, і це якраз робота
        // CityWorksStep (див. TakeInvestmentPayout).

        /// <summary>
        /// Указ: рухає Уклад (DayProcessor.OrderLevel) на крок і віддає одну
        /// фракцію у вигоду ціною іншої (AUDIT П8+G20 — Уклад раніше рухати
        /// було рішуче нічим, а понижувальний драйвер CouncilEdict стояв у
        /// білому списку без жодного виклику). costFactionId необов'язковий: без
        /// нього указ просто піднімає вигідну фракцію, не чіпаючи решту.
        ///
        /// Ревью-фікс: favoredFactionId ЗОБОВ'ЯЗАНИЙ бути зареєстрований у
        /// factions — інакше указ («поміняти одну фракцію на іншу») спише
        /// золото, штовхне Уклад і мовчки не поміняє жодної фракції.
        /// Перевірка — до всіх витрат, тим самим прийомом, що вже стоїть в
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

            // Ревью-фікс: фракції рухаються через SocialConsequence — єдину
            // точку, де список дозволених драйверів реально перевіряється
            // (allow-list інакше був мертвим кодом для цього місця). Напругу
            // SocialConsequence.Apply НЕ віддаємо processor'у (див. коментар на
            // _pendingCouncilEdictTension) — накопичуємо її самі і застосовуємо в
            // CityWorksStep.Execute того самого дня, щоб вона переживала сейв.
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

        /// <summary>Дипломатія: чисте покращення однієї фракції, без Уклада і без Напруги.</summary>
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
        /// Інвестиція: платить золото одразу, а повертає більше — розтягнуто по
        /// добах (TakeInvestmentPayout, крок CityWorksStep). buildingId — не
        /// декорація: інвестувати можна тільки в уже готову будівлю.
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
        /// Підготовка до загрози: накопичує МАРКЕРИ готовності, не саму Готовність —
        /// ReadinessTrack заводить B6, цей пакет від нього не залежить (§1.1). D1
        /// забере накопичене через TakeReadinessMilestones, коли трек з'явиться.
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
        /// Спорядження експедиції: разовий бонус наступній вилазці на майданчик
        /// siteId. Дані, а не тип B7 (R15 тримає вилазку цілком) — сид її
        /// входу, який забере D1 через TakeExpeditionOutfitBuff.
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
        /// AUDIT G12: показаний підходом Торгівлі множник ціни
        /// (CheckOutcome.PriceMultiplier) до цього пакета рахувався і
        /// викидався (CheckResolver.Resolve його обчислює, але ніхто не читав).
        /// Тут він нарешті вирішує ціну будівництва і указів ради — але тільки коли
        /// ринок поселення зайнятий і відкритий: без профільної людини на посту
        /// знижки немає (множник 1.0, той самий шлях, що й раніше).
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

        /// <summary>Розтягнута виплата Інвестиції — викликає CityWorksStep щодоби.</summary>
        internal int TakeInvestmentPayout()
        {
            if (_investmentDaysLeft <= 0) return 0;
            _investmentDaysLeft--;
            return _investmentGoldPerDay;
        }

        /// <summary>
        /// Забирає і обнуляє накопичену Напругу Указу (ревью-фікс, див.
        /// _pendingCouncilEdictTension). Викликається CityWorksStep тим самим
        /// прийомом, яким вона вже забирає TakeRaid/TakeSettlers/...
        /// </summary>
        internal int TakeCouncilEdictTension()
        {
            int v = _pendingCouncilEdictTension;
            _pendingCouncilEdictTension = 0;
            return v;
        }

        /// <summary>Забирає і обнуляє накопичені маркери готовності (для D1/B6).</summary>
        internal int TakeReadinessMilestones()
        {
            int n = _readinessMilestonesQueued;
            _readinessMilestonesQueued = 0;
            return n;
        }

        /// <summary>
        /// Підглядає разовий бонус спорядження БЕЗ його зняття (фікс-ревью):
        /// D1 (DepartExpedition) має вирішити, чи застосовний бонус ДО ЦЬОГО
        /// відправлення (siteId збігається із замовленим), ДО того, як забере
        /// його — інакше TakeExpeditionOutfitBuff() знімав бонус безумовно на
        /// першому ж відправленні, навіть на інший майданчик, і він губився
        /// назавжди без жодного шансу бути застосованим туди, куди замовлений.
        /// </summary>
        internal ExpeditionOutfitBuff PeekExpeditionOutfitBuff() => _pendingOutfitBuff;

        /// <summary>Забирає і обнуляє разовий бонус спорядження (для D1) — тільки коли він уже застосовується (див. PeekExpeditionOutfitBuff).</summary>
        internal ExpeditionOutfitBuff TakeExpeditionOutfitBuff()
        {
            var b = _pendingOutfitBuff;
            _pendingOutfitBuff = null;
            return b;
        }

        /// <summary>
        /// Забирає і обнуляє чергу оголошень про Указ/Дипломатію/Підготовку/
        /// Спорядження — викликається CityWorksStep, тим самим прийомом, яким вона вже
        /// забирає TakeRaid/TakeSettlers/TakeArrivals/TakeInvestmentPayout.
        /// </summary>
        internal List<CityEvent> TakeCouncilAnnouncements()
        {
            if (_pendingCouncilAnnouncements.Count == 0) return null;
            var events = new List<CityEvent>(_pendingCouncilAnnouncements);
            _pendingCouncilAnnouncements.Clear();
            return events;
        }

        // ================= виконання всередині доби =================

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
            // Детермінований порядок повідомлень: за ідентифікатором, а не за
            // тим, у якому порядку їх заклали.
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
        /// Замки постів за будівлями: пост без своєї будівлі закритий, людина на
        /// нього не стане. Пости без будівлі (поля, розвідпост) не чіпаємо.
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

        // ================= зліпок =================

        // Формат без «;» і «=» — зовнішній зліпок ріже по них.
        // b:<id>,<id>|p:<id>:<left>:<total>,...|r:<queued>:<lastDay>|s:<n>|a:<n>
        // B5 (AUDIT П8/G12/G20, адитивно): |d:<lastDecreeDay>|y:<lastDiplomacyDay>
        // |t:<lastPrepareThreatDay>|i:<goldPerDay>:<daysLeft>|m:<milestonesQueued>
        // |o:<siteId>:<bonusValue> (порожньо — бонуса немає)
        // |e:<pendingCouncilEdictTension> (ревью-фікс: Напруга Указу переживає сейв)
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
