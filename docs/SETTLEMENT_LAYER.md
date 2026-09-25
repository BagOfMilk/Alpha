# Живий шар поселення — дизайн-специфікація

> **Статус:** робоча специфікація. Правила зафіксовані Поправкою №3
> (`docs/GDD_AMENDMENTS.md`); цей документ тримає **формули, якорі та числа**.
> При конфлікті: поправки > GDD > цей документ > код.
>
> Усі числа — **стартові плейсхолдери**, тюняться у плейтесті.

---

## 1. Навіщо цей шар

Міський цикл — головний луп гри (Поправка №3.1). Завдання шару: зробити так,
щоб поселення **жило само** — події росли з відносин гравця зі світом, а не
з глобального таймера, і щоб усе це читалося **без жодного числа на екрані**.

Три правила, з яких виведено решту:

1. **Детермінізм.** Жодного `System.Random` у ядрі. Несподіванка — з неповноти
   інформації, а не з костей.
2. **Сигнал обов'язковий.** Зміна полоси будь-якої прихованої шкали зобов'язана
   породити спостережуваний сигнал. Немає німого переходу.
3. **Якір, споживач, сигнал.** Шкала без усіх трьох не додається.

---

## 2. Модель даних

Нові namespace у `Game.Core`. **Не посилаються** на `Game.Core.Stats` і
`Game.Core.Economy` — ці типи будуть переписані при перебудові ядра. Зв'язок —
лише через порти.

### 2.1 Порти (розв'язка з умираючим кодом)

```
Game.Core.Checks
    SkillKey            — рядковий ключ навички/атрибута
    SkillKeys           — відомі ключі (persuade, intimidate, trade, medicine, ...)
    ISettlementActor    — Id, IsPresentInSettlement, HeldPositionId,
                          GetCheckValue(SkillKey), GetTraitModifier(SkillKey),
                          HasValueTag(string)
    IRosterView         — PresentActors, Protagonist
```

Рядковий ключ — вже прийнята в проєкті конвенція
(`AssignmentSlotDefinition.PassiveBonusId`). Коли з'явиться нова модель
персонажа (4 атрибути + 10 скілів), мапінг робиться **в одному місці**.

```
Game.Core.Economy2      (тимчасова назва; стане Economy після смерті ResourceType)
    CostBundle          — Gold, Build, Craft, Influence, Days
    ICostSink           — CanAfford, TrySpend, Grant
```

**Дні — теж ціна.** Це механічне тіло осі «час проти ризику» (Поправка №1).

### 2.2 Напруга

```
Game.Core.Pressure
    TensionDriver       — ЗАКРИТИЙ enum (див. §5.2)
    TensionBand         — Calm, Murmur, Ferment, Heat, Fracture
    TensionChange       — Driver, Requested, Applied, From, To, Rejected, SourceId
    TensionState        — internal Value; public Band; DaysInCurrentBand;
                          event BandChanged; internal Apply(...); internal DayLedger
```

`Value` і `DayLedger` — **`internal`**. `Game.Gameplay` не збереться, якщо
спробує їх прочитати.

### 2.3 Пульс світу

```
Game.Core.World
    WorldEventKind      — FactionMove, InternalThreat, NightCrime, PatronInterest,
                          Rumor, Crisis
    PressureTrack       — SourceId, Kind, internal Charge, internal Threshold,
                          LastFiredDay, CooldownDays, ForewarnLevel
    IPressureSource     — Id, Kind, InsistencePerDay(ctx), Threshold,
                          CooldownDays, IsActive(ctx)
    WorldPulse          — Advance(ctx, day, cfg) -> PulseTick
    PulseTick           — FiredSourceIds, Forewarnings
    Forewarning         — SourceId, Level (1..3), DomainTag
```

### 2.4 Поселення і фракції

```
Game.Core.Settlement
    DayPhase            — Day, Night
    OrderLevel          — Loose, Watched, Policed, Locked          (Уклад)
    SettlementState     — Tier, internal Population, internal Capacity, Order,
                          internal Reach, Crowd, Buildings, Positions
    BuildingState       — DefinitionId, Level, IsPatronOwned,
                          ConstructionDaysLeft, VisualStage (1..5)
    PositionState       — DefinitionId, BuildingId, OccupantActorId, RelevantSkill
    ReachCalculator     — Compute(...)

Game.Core.Factions
    TrustStep           — Stranger, Acquaintance, Insider, Trusted
    CohesionBand        — Solid, Strained, Splintering
    ContactLoyalty      — Reliable, SelfServing, ForSale
    FactionState        — Id, internal Standing, internal Cohesion, Trust,
                          Cohesion Band, StreetPresence (0..3),
                          ResponseDelayDays, IsHostile, internal RecentRequestDays
    FavorDefinition     — Id, FactionId, RequiredStep, Check, Cost,
                          GrantsEffectId, CreatesObligation
    Obligation          — Id, FactionId, DueDay, Payment,
                          StandingPenaltyOnDefault, InsistenceBonusOnDefault
    ContactState        — Id, DomainTag, Loyalty, InfoLagDays,
                          LeansTowardFactionId, LastQueriedDay, QueriesToday
```

### 2.5 Сигнали

```
Game.Core.Signals
    SignalChannel       — CitizenLine, CompanionLine, StreetPresence, Moodboard,
                          Forewarning, Ambient, MarketShelf, PostReport
    SignalUrgency       — Ambient, Notable, Alarming, Imminent
    SignalRequest       — Channel, TopicId, Urgency, SubjectId, IsDelta, Tags
    SignalDigest        — Requests, Moodboard, Streets, RumorTopicIds
    MoodboardState      — Prosperity (0..4), Decay (0..4), Overlays
    SignalComposer      — Compose(ctx, journal, cfg) -> SignalDigest
    ISignalSink         — Publish(digest)     (реалізується в Game.Gameplay)
```

**Ядро видає `TopicId` і теги, а не рядки.** Конкретні репліки живуть у
SO-таблицях — письменнику не потрібен програміст.

---

## 3. Формули та якорі

### 3.1 Напруга

```
Value ∈ [0, 1000]
Band  = Calm | Murmur | Ferment | Heat | Fracture   при порогах 200 / 400 / 600 / 800

Денний тик:    ΔTier  = TierTickPerDay[Тір] × OrderTickMultiplier[Уклад]
Дренаж:        ΔDrain = Σ(Храм, Укріплення)      — дробовий, накопичується окремо
Вибір у квесті: ±15 (дрібний) / ±40 (великий) / ±60 (жахливий)
Наслідок загрози: −40 (Найкраща полоса) … +80 (Найгірша полоса)
Різня:         +10 за «зайвий» вирізаний ненасильницький вузол, стеля +50 за вилазку
Облава:        −80, КД 10 днів

TierTickPerDay      = { 1.0, 2.0, 4.0, 7.0 }
OrderTickMultiplier = { 1.25, 1.0, 0.85, 0.7 }
```

**ЯКІР:** *хутір без жодної дії гравця не доходить до кризи за всю
кампанію.* При тику 1/добу і кампанії 150–200 днів пасивний дрейф — близько однієї
полоси. **Криза — це завжди твої вибори плюс тір, а не плин часу.**
Це математична гарантія US-1.3: тридцять днів лікування дають 30–60 очок,
тобто максимум чверть полоси.

### 3.2 Пульс світу (без випадковості)

```
Charge += Insistence(ctx)                      // чиста функція від стану світу
Спрацювання: Charge ≥ Threshold  І  (day − LastFiredDay) ≥ CooldownDays
Після:        Charge −= Threshold;  LastFiredDay = day
Стеля:        Charge ≤ Threshold × 2
Предвісники:  Level 1 при Charge/Threshold ≥ 0.55
              Level 2 при ≥ 0.80
              Level 3 при ≥ 0.95
Порядок при нічиї: за зростанням SourceId (стабільно і відтворювано)
За добу спрацьовує не більше MaxFiresPerDay (день 1 / ніч 2)
```

**ЯКІР:** *поріг 100 → днів до втручання ≈ 100 / Наполегливість.*
Наполегливість 10 = «приблизно раз на десять днів». Це якір **для дизайнера**;
гравець замість числа отримує драбину предвісників.

**ІНВАРІАНТ (плата за детермінізм):** одночасно активних накопичувачів — **не
менше трьох, з різними ставками**. Один накопичувач читається наскрізь; три
перехресні — вже ні. Ставки зобов'язані змінюватися від дій гравця.

**Принцип чесності: «що і де — чесно, коли — ні.»** Предвісник рівня 2
зобов'язаний назвати домен і місце, рівень 3 — близькість. Точний день не повідомляється
ніколи, навіть опосередковано.

### 3.3 Перевірки і полоси наслідку

```
value      = max по присутнім ( GetCheckValue(skill) + GetTraitModifier(skill) )
             + контекстний атрибут підходу
             (Залякати → Сила/Воля; Переконати → Кмітливість; Торгівля → Кмітливість)
threshold' = threshold + RepeatPenaltyStep × спроб_у_вікні
margin     = value − threshold'

band = margin < 0           → Worst
       margin < GoodMargin  → Base       (GoodMargin = 3)
       margin < BestMargin  → Good       (BestMargin = 7)
       інакше                → Best

Форми підходу:
  Переконання:  margin ≥ −Cushion (2) → band = max(band, Base); band = min(band, Good)
  Залякування:  band == Worst → додати ефект страху (−ставлення);
               Best доступний при margin ≥ BestMargin − 2
  Торгівля:    band не змінюється; ціна ×(1 − 0.10 × band)

Незайнятий релевантний пост → кандидатів немає → band = Worst, WasUnmanned = true
```

**ЯКІР:** *поріг = стільки, скільки дає середняк, який вклався в цю навичку на
поточній стадії; Хороша полоса = спеціаліст; Найкраща = спеціаліст із профільним
трейтом.* Поріг завжди видно до підтвердження; тест гарантує, що показане
дорівнює застосованому.

### 3.4 Досяжність

```
Reach = TierReach[Тір] + Σ БудівліReach + clamp(floor(log2(Pop / 200)), −1, +3)
        + Σ FactionReach(Ступінь) − OrderReachPenalty[Уклад]

Доступно(X) ⇔ Reach ≥ X.RequiredReach
```

**ЯКІР:** *200 людей (стартове село) = Досяжність 0 = лише базове.*
Кожне подвоєння населення дає +1, стеля +3. Гравець бачить якір як прилавок:
на нулі там три пости, на +2 — десяток і є лікар.

### 3.5 Чутки

```
ЧутокЗаДень = 1 (якщо є Таверна/Ринок і це день)
             + min( floor(margin / 2), 2 )
Джерело тільки одне за добу; вночі — тільки через патруль.
```

**ЯКІР:** *присутність дає одну чутку; вкладена навичка — максимум три.*

### 3.6 Тір міста

```
Просування при ОДНОЧАСНОМУ виконанні:
    ключові будівлі побудовані
  І Населення ≥ TierPopulationRequirement[тір]     = { 0, 400, 900, 1800 }
  І не менше N з M фракцій на ступені ≥ Знайомий
```

Умова «N з M», а не «конкретна фракція», — механічна гарантія того, що
критичний шлях не запертий за однією фракцією (US-10.1). Покрито контент-тестом.

---

## 4. Порядок кроків дня

`BaseState.AdvanceCycle()` перетворюється на крок `Production` впорядкованого
конвеєра. Порядок оголошено константами **в одному файлі** (`DayStepOrder`),
щоб зв'язки між системами читалися з одного екрана.

| Порядок | Крок | Що робить |
|---:|---|---|
| 0 | Clock | день, фаза (день/ніч) |
| 100 | Construction | будівництва, 5 візуальних стадій |
| 200 | Production | наявний прохід по постах |
| 300 | Population | зростання населення, місткість |
| 400 | Derived | Досяжність, присутність на вулицях, ефекти Укладу |
| 500 | Tension | **тільки** фоновий тик і пасивний дренаж |
| 600 | Obligations | строки, прострочення |
| 700 | Pulse | накопичувачі, предвісники, відбір спрацьованих |
| 800 | Incidents | вибір інциденту, резолв, наслідок |
| 900 | Healing | **не чіпає Напругу** — гарантовано тестом |
| 1000 | Signals | композиція сигналів за фінальним станом |
| 1100 | Report | збірка `DayReport` |

**Чому такий порядок.** Похідні рахуються рівно один раз і тільки в кроці
`Derived` — подвійний рахунок неможливий (US-18.2). Напруга тикає **до** Пульсу,
щоб сьогоднішня полоса зважувала сьогоднішню таблицю інцидентів. Інциденти
резолвляться **після** — їхній наслідок впливає на ваги завтра, а не сьогодні (немає
зворотного зв'язку всередині одного дня). Сигнали — останніми, щоб бачити фінальний стан.

**Ніч** — той самий ланцюжок, але: виробництво і лікування вимкнені **повністю**
(не «частково», як було в цій чернетці), репліки мешканців недоступні
(діалоги закриті), зате спрацювань до двох і доступний канал `Ambient`.

> Уточнено 2026-09-20 при написанні моста. Календарна доба — це дві фази,
> і «частково» довелося б виражати часткою виробітку: неочевидне число там, де
> US-1.5 каже прямо «вночі призначення на пости закриті», а GDD Е1 —
> «патрулювати без лікування». Крок виробництва просто не виконується вночі,
> і це перевіряється тестом `CalendarDay_ProducesExactlyOnce`.

```
DayReport
    Day, Phase
    // CycleReport Production — ЗАСТАРІЛО, так не зроблено.
    // DayReport лежить у Core/Loop і тому не може нести ResourceType:
    // числа виробництва йдуть окремим каналом ProductionStep.LastReport.
    internal TensionChanges                   // числа назовні не виходять
    internal PulseSnapshot
    internal PopulationChange
    public Incidents                          // наслідки публічні — вони відбулися
    public Completed, Obligations
    public SignalDigest Signals               // ЄДИНЕ, що читає UI міста
```

---

## 5. Шар сигналів

### 5.1 Правила

1. **Немає німого переходу полоси** — будь-яка зміна полоси дає сигнал `Notable`+.
2. **Бюджет** — не більше 4 сигналів на день; відбір за терміновістю, потім за
   «це зміна», потім за різноманітністю каналів.
3. **Мінімум один слот на дельту** — кожен день хоча б один сигнал відповідає на
   «що змінилося зі вчора». Прямі ліки від «немає читабельного стану».
4. **Предвісник не бреше** — рівень 2 зобов'язаний назвати домен, рівень 3
   зобов'язаний з'явитися мінімум за день до кризи.
5. **Топіки, не рядки** — ядро видає ключ і теги, репліки живуть у контенті.

### 5.2 Напруга → що бачить гравець

| Полоса | Вулиці й мудборд | Мешканець | Напарник | Інциденти |
|---|---|---|---|---|
| **Спокійно** | крамниці відкриті допізна, діти у дворах, білизна на мотузках | «Добре, що ви тут» | побутовий бантер | немає |
| **Ропіт** | забита дошками віконниця, сміття не прибране | біля криниці сваряться про ціни | «Чув на ринку — у Чинбарів людей забрали» | дрібна крадіжка |
| **Бродіння** | групи по 3–4 замовкають при твоєму наближенні; графіті фракцій | крамар просить охорону | «Тобі варто було б показатися на площі» | організована злочинність |
| **Розпал** | вдень зачинені віконниці, патрулі парами, частина NPC зникла; вогнища в бочках | відводять очі | напарники сваряться між собою | серія, вночі щільніше |
| **Злам** | порожня площа, тривожний дзвін, бійці фракцій відкрито при зброї | тиша | «Якщо нічого не зробиш — я їх не втримаю» | криза протягом 1–3 днів |

### 5.3 Ступінь довіри фракції

| Ступінь | Вулиці | Привітання | Що відкрито |
|---|---|---|---|
| Чужий | їхніх людей не видно | не впізнають | нічого |
| Знайомий | одна постать у формі | кивають | одна послуга, звичайні ціни |
| Свій | двоє-троє біля своїх будівель | звуть на ім'я | три послуги, знижка, їхні квести |
| Довірений | помітна присутність, свій кут на ринку | «наш» | повний список, перепустки, клопотання за патрона |

### 5.4 Згуртованість фракції

| Полоса | Що видно |
|---|---|
| Міцна | єдина форма, дисципліна, говорять від імені фракції |
| Тріщить | двоє їхніх бійців сваряться на вулиці; «старий вже не той»; послуга виконується гірше обіцяного |
| Розкол | своя мітка поверх їхнього графіті; бійка між своїми; **предвісник народження ворожого угруповання** |

### 5.5 Уклад

| Уклад | Прилавок | Ніч | Вулиця |
|---|---|---|---|
| Вольниця | усе, включно із забороненим | патрулів немає, інциденти лютіші | галасливо, нахабні |
| Нагляд | звичайне плюс сіра зона | рідкий обхід | нейтрально |
| Порядок | лише легальне | патрулі, оголошення | тихіше, скошують очі на патруль |
| Засув | скупо, «за дозволом» | комендантська година, місто вимерло | мовчать при варті |

**В Укладу немає жодного невізуального ефекту** — він показує себе сам.

### 5.6 Предвісники

| Ступінь | Формат | Приклад |
|---|---|---|
| 1 (≥55 %) | амбієнт, без адреси | «Собаки третю ніч гавкають» |
| 2 (≥80 %) | названо домен і місце | «Біля складу третій день тиняється той самий» |
| 3 (≥95 %) | названа близькість, не день | «Чинбарі щось готують. Скоро» |

### 5.7 Мудборд

```
Prosperity = f(Тір, Населення/Місткість, число побудованих будівель)   → 0..4
Decay      = f(Полоса Напруги, прострочені зобов'язання, Розкол)   → 0..4
Overlays   = теги: «барикади», «намети», «графіті:X», «патруль»
```

Два незалежних числа, а не одне: **багате і напружене місто виглядає інакше,
ніж бідне і спокійне.**

### 5.8 Доповідь із постів

Раз на добу напарник на релевантному посту дає усне суб'єктивне зведення за
своїм доменом. **Точність — за тією ж драбиною полос:** сильний профільний
напарник дає конкретику, слабкий — розпливчастість, нікого на посту — тиша.

Це читабельний стан без панелі, нова цінність постів і точка застосування
правила «посилювати пост можуть лише напарники»: робітники доповіді не дають.

---

## 6. Секції BalanceConfig

Усі — `[Serializable]` класи в `Game.Core.Balance`, із дзеркальними SO-обгортками
в `Game.Gameplay` за еталоном `BalanceConfigAsset`.

| Секція | Ключові поля |
|---|---|
| `TensionBalance` | Max=1000; BandThresholds={200,400,600,800}; TierTickPerDay={1,2,4,7}; OrderTickMultiplier={1.25,1,0.85,0.7}; AllowedRaising / AllowedLowering (білий список); RaidDelta=−80; RaidCooldownDays=10; TempleDrainPerDay=−0.5; FortificationDrainPerDay=−0.3; BloodDeltaPerNode=10; BloodCapPerExpedition=50; CrisisGraceDays=3 |
| `PulseBalance` | DefaultThreshold=100; Forewarn1At=0.55; Forewarn2At=0.80; Forewarn3At=0.95; MaxFiresPerDay=1; MaxFiresPerNight=2; ChargeCapMultiplier=2; MinActiveTracks=3 |
| `CheckBalance` | GoodMargin=3; BestMargin=7; PersuadeCushion=2; IntimidateBestBonus=2; TradeBandDiscount=0.10; RepeatPenaltyStep=2; RepeatWindowDays=7 |
| `SettlementBalance` | ReachPopulationAnchor=200; ReachPopCap=3; ReachPopFloor=−1; TierReach={0,1,2,3}; TierPopulationRequirement={0,400,900,1800}; PopulationGrowthPerDay=1.5; TavernGrowthBonus=1.0; OrderRaiseThresholdBase=6; OrderRaiseThresholdStep=2; OrderDecayDaysWithoutSupport=14 |
| `FactionBalance` | TrustStepThresholds={0,30,60,85}; StreetPresenceThresholds={0,25,55,80}; CohesionBandThresholds={35,70}; SplinterInsistenceBonus=6; ResponseDelayByScale={1,3,7}; ObligationDefaultDays=14; ObligationDefaultPenalty=15 |
| `SignalBalance` | MaxSignalsPerDay=4; MinDeltaSlots=1; ForceSignalOnBandChange=true; RumorBase=1; RumorPerMarginStep=2; RumorMaxExtra=2 |

**Закритий список драйверів Напруги** (`TensionDriver`):
підвищують — `CityTierTick`, `QuestChoice`, `ThreatOutcome`, `PlaystyleBlood`;
знижують — `CouncilRaid`, `CouncilEdict`, `TempleAura`, `Fortifications`,
`EventOutcome`. Усе інше відхиляється.

---

## 7. Етапи

| Етап | Зміст | Складність | Що доводить |
|---|---|---|---|
| **Е0 «Пульс міста»** | години кампанії, `TensionState` з 5 полосами, драйвери тір-тик і квест-вибір, `SignalComposer` на 3 канали, правило «немає німого переходу», порти `SkillKey`/`ISettlementActor`/`CostBundle` | M | прихована шкала читається очима |
| **Е1 «Інциденти і телеграфія»** | полоси наслідку + попередній перегляд порога, «незайнятий пост = Найгірший», `WorldPulse` (≥3 накопичувача), предвісники, 6–8 інцидентів у 2 тіри + 1 криза, ніч і патруль, доповідь із постів | L | криза відчувається заслуженою |
| **Е2 «Сторони»** | 1–2 фракції: ставлення → 4 ступені → 3 послуги, присутність на вулицях, відгукливість, штраф за повторні звернення, чутки, слава | M | «де я з ними» читається без числа |
| **Е3 «Уклад і Досяжність»** | Уклад (4 стани) і його візуал, Досяжність, перенаселення, тіньовий бік/чорний ринок, тіри 1–3 | L | місто стає місцем зі своїм устроєм |
| **Е4 [ПІЗНІШЕ]** | контакти і вірність, зобов'язання, розкол → нова ворожа фракція, патрони, рада з 6 дій, Готовність, тір 4 | XL | політика і фінал |

**MVP = Е0 + Е1 + одна фракція з трьома послугами з Е2.**

---

## 8. Тести (EditMode)

**Гарантії обмежень — головна цінність набору:**

| Тест | Перевіряє |
|---|---|
| `Tension_WaitThirtyDays_OnlyTierTickEntries` | 30 днів «чекати» → у журналі немає драйверів, крім тіра |
| `Tension_HealingDays_ProduceNoTension` | лікування 10 днів → дельта рівно 10×тик, полоса та сама |
| `Tension_Apply_RejectsDriverOutsideWhitelist` | драйвер поза списком → `Applied == 0`, `Rejected == true` |
| `Tension_PassiveCampaign_NeverReachesFracture` | 200 днів без дій на тірах 1–2 → полоса ≤ Бродіння |
| `Signals_BandChange_AlwaysEmitsNotable` | усі переходи полос дають сигнал `Notable`+ |
| `Signals_NeverExceedBudget_AndAlwaysIncludeDelta` | ≤4 сигнали/день, ≥1 з `IsDelta` |
| `Pulse_ForewarnPrecedesEveryFire` | перед кожним спрацюванням були рівні 1 і 2 |
| `Pulse_AtLeastThreeActiveTracks` | інваріант детермінізму з §3.2 |
| `Crisis_NeverFiresWithoutLevel3Forewarn` | криза неможлива без предвісника 3 і grace-вікна |
| `Content_EveryIncident_HasNonViolentPath` | у кожного інциденту заповнений тихий шлях (Поправка №1) |
| `Content_CriticalPath_ReachableWithAllFactionsHostile` | усі фракції ворожі → критичний шлях прохідний |
| `Core_HasNoRandom` | рефлексія: ядро не посилається на `System.Random` |
| `Pulse_SameInputs_IdenticalSchedule` | два прогони по 200 днів → ідентичні логи |
| `Architecture_SettlementLayer_DoesNotReferenceLegacyTypes` | новий шар не зав'язаний на умираючі namespace |

**Перевірки і полоси:** `Check_UsesBestSkillAmongPresent`,
`Check_UnmannedPosition_YieldsWorstBand`, `Check_Protagonist_IsValidCandidate`,
`Check_PreviewEqualsResolve`, `Check_Persuade_NeverWorstWithinCushion`,
`Check_Persuade_CannotReachBest`, `Check_Intimidate_ShiftsBothTails`,
`Check_RepeatWithinWindow_RaisesThreshold`.

**Поселення і фракції:** `Reach_MonotonicInPopulation`,
`Reach_AnchorIs200Equals0`, `Trust_StepUnlocksExactlyItsFavors`,
`Tier_Advance_RequiresNofMFactions_NotSpecificOne`,
`Order_DecaysWithoutSupport`, `Obligation_Default_RaisesInsistence`.

**Enum-coverage** (ловить «стат, який ніхто не читає»):
`EveryTensionDriver_HasConfigEntry`, `EveryTensionBand_HasSignalTopicSet`,
`EverySignalChannel_HasComposerBranch`, `EveryTrustStep_HasAtLeastOneFavor`,
`EveryOrderLevel_HasMarketAndNightProfile`.

---

## 9. Ризики шару

| # | Ризик | Мітигація | Статус |
|---|---|---|---|
| N1 | Сигнали перетворюються на шум | бюджет 4/день, пріоритети, дельта-слот, доповідь із постів. **Метрика плейтесту: 4 з 5 тестерів називають полосу словами вірно** | МОНІТОР |
| N2 | Шкал забагато | правило «якір + споживач + сигнал», enum-coverage тести | КОНТРОЛЮЄТЬСЯ |
| N3 | Детермінізм читається наскрізь — гравець вирахує день кризи | ≥3 накопичувача з різними ставками, ставки змінюються від дій, пороги і список джерел приховані | **ПРИЙНЯТО + МОНІТОР** |
| N4 | Порядок кроків дня заводить приховані зв'язки | `DayStepOrder` в одному файлі, похідні рахуються один раз, тест на порядок | КОНТРОЛЮЄТЬСЯ |
| N5 | Контент-вибух реплік | топіки і теги замість унікальних рядків, ланцюжок фолбеків; MVP = 5 полос × 2 канали × 3 варіанти | КОНТРОЛЮЄТЬСЯ |
| N6 | Криза відчувається несправедливою (спадкоємець R8) | 3 предвісники, grace-вікно, «що і де чесно, коли — ні», чутки як купована телеграфія | ПРИЙНЯТО + МОНІТОР |
| N7 | Перебудова ядра зламає шар | порти `SkillKey`/`ISettlementActor`/`CostBundle` + архітектурний тест | КОНТРОЛЮЄТЬСЯ |

**N3 — головний ризик цього дизайну** і прямий наслідок вибору повного
детермінізму. Валідується у зрізі: якщо тестер на 20-й день каже «зараз
прийдуть Чинбарі» і виявляється правим три рази поспіль — потрібно додавати
накопичувачі, а не випадковість.
