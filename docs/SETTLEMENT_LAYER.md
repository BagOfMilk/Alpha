# Живой слой поселения — дизайн-спецификация

> **Статус:** рабочая спецификация. Правила зафиксированы Поправкой №3
> (`docs/GDD_AMENDMENTS.md`); этот документ держит **формулы, якоря и числа**.
> При конфликте: поправки > GDD > этот документ > код.
>
> Все числа — **стартовые плейсхолдеры**, тюнятся в плейтесте.

---

## 1. Зачем этот слой

Городской цикл — главный луп игры (Поправка №3.1). Задача слоя: сделать так,
чтобы поселение **жило само** — события росли из отношений игрока с миром, а не
из глобального таймера, и чтобы всё это читалось **без единого числа на экране**.

Три правила, из которых выведено остальное:

1. **Детерминизм.** Никакого `System.Random` в ядре. Неожиданность — из неполноты
   информации, а не из костей.
2. **Сигнал обязателен.** Смена полосы любой скрытой шкалы обязана породить
   наблюдаемый сигнал. Нет немого перехода.
3. **Якорь, потребитель, сигнал.** Шкала без всех трёх не добавляется.

---

## 2. Модель данных

Новые namespace в `Game.Core`. **Не ссылаются** на `Game.Core.Stats` и
`Game.Core.Economy` — эти типы будут переписаны при перестройке ядра. Связь —
только через порты.

### 2.1 Порты (развязка с умирающим кодом)

```
Game.Core.Checks
    SkillKey            — строковый ключ навыка/атрибута
    SkillKeys           — известные ключи (persuade, intimidate, trade, medicine, ...)
    ISettlementActor    — Id, IsPresentInSettlement, HeldPositionId,
                          GetCheckValue(SkillKey), GetTraitModifier(SkillKey),
                          HasValueTag(string)
    IRosterView         — PresentActors, Protagonist
```

Строковый ключ — уже принятая в проекте конвенция
(`AssignmentSlotDefinition.PassiveBonusId`). Когда появится новая модель
персонажа (4 атрибута + 10 скилов), маппинг делается **в одном месте**.

```
Game.Core.Economy2      (временное имя; станет Economy после смерти ResourceType)
    CostBundle          — Gold, Build, Craft, Influence, Days
    ICostSink           — CanAfford, TrySpend, Grant
```

**Дни — тоже цена.** Это механическое тело оси «время против риска» (Поправка №1).

### 2.2 Напряжение

```
Game.Core.Pressure
    TensionDriver       — ЗАКРЫТЫЙ enum (см. §5.2)
    TensionBand         — Calm, Murmur, Ferment, Heat, Fracture
    TensionChange       — Driver, Requested, Applied, From, To, Rejected, SourceId
    TensionState        — internal Value; public Band; DaysInCurrentBand;
                          event BandChanged; internal Apply(...); internal DayLedger
```

`Value` и `DayLedger` — **`internal`**. `Game.Gameplay` не соберётся, если
попытается их прочитать.

### 2.3 Пульс мира

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

### 2.4 Поселение и фракции

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

### 2.5 Сигналы

```
Game.Core.Signals
    SignalChannel       — CitizenLine, CompanionLine, StreetPresence, Moodboard,
                          Forewarning, Ambient, MarketShelf, PostReport
    SignalUrgency       — Ambient, Notable, Alarming, Imminent
    SignalRequest       — Channel, TopicId, Urgency, SubjectId, IsDelta, Tags
    SignalDigest        — Requests, Moodboard, Streets, RumorTopicIds
    MoodboardState      — Prosperity (0..4), Decay (0..4), Overlays
    SignalComposer      — Compose(ctx, journal, cfg) -> SignalDigest
    ISignalSink         — Publish(digest)     (реализуется в Game.Gameplay)
```

**Ядро выдаёт `TopicId` и теги, а не строки.** Конкретные реплики живут в
SO-таблицах — писателю не нужен программист.

---

## 3. Формулы и якоря

### 3.1 Напряжение

```
Value ∈ [0, 1000]
Band  = Calm | Murmur | Ferment | Heat | Fracture   при порогах 200 / 400 / 600 / 800

Дневной тик:   ΔTier  = TierTickPerDay[Тир] × OrderTickMultiplier[Уклад]
Дренаж:        ΔDrain = Σ(Храм, Укрепления)      — дробный, копится отдельно
Выбор в квесте: ±15 (мелкий) / ±40 (крупный) / ±60 (чудовищный)
Исход угрозы:  −40 (Лучшая полоса) … +80 (Худшая полоса)
Резня:         +10 за «лишний» вырезанный ненасильственный узел, потолок +50 за вылазку
Облава:        −80, КД 10 дней

TierTickPerDay      = { 1.0, 2.0, 4.0, 7.0 }
OrderTickMultiplier = { 1.25, 1.0, 0.85, 0.7 }
```

**ЯКОРЬ:** *хутор без единого действия игрока не доходит до кризиса за всю
кампанию.* При тике 1/день и кампании 150–200 дней пассивный дрейф — около одной
полосы. **Кризис — это всегда твои выборы плюс тир, а не течение времени.**
Это математическая гарантия US-1.3: тридцать дней лечения дают 30–60 очков,
то есть максимум четверть полосы.

### 3.2 Пульс мира (без случайности)

```
Charge += Insistence(ctx)                      // чистая функция от состояния мира
Срабатывание: Charge ≥ Threshold  И  (day − LastFiredDay) ≥ CooldownDays
После:        Charge −= Threshold;  LastFiredDay = day
Потолок:      Charge ≤ Threshold × 2
Предвестники: Level 1 при Charge/Threshold ≥ 0.55
              Level 2 при ≥ 0.80
              Level 3 при ≥ 0.95
Порядок при ничьей: по возрастанию SourceId (стабильно и воспроизводимо)
За день срабатывает не более MaxFiresPerDay (день 1 / ночь 2)
```

**ЯКОРЬ:** *порог 100 → дней до вмешательства ≈ 100 / Настойчивость.*
Настойчивость 10 = «примерно раз в десять дней». Это якорь **для дизайнера**;
игрок вместо числа получает лестницу предвестников.

**ИНВАРИАНТ (плата за детерминизм):** одновременно активных накопителей — **не
менее трёх, с разными ставками**. Один накопитель читается насквозь; три
пересекающихся — уже нет. Ставки обязаны меняться от действий игрока.

**Принцип честности: «что и где — честно, когда — нет.»** Предвестник уровня 2
обязан назвать домен и место, уровень 3 — близость. Точный день не сообщается
никогда, даже косвенно.

### 3.3 Проверки и полосы исхода

```
value      = max по присутствующим ( GetCheckValue(skill) + GetTraitModifier(skill) )
             + контекстный атрибут подхода
             (Запугать → Сила/Воля; Убедить → Смекалка; Торговля → Смекалка)
threshold' = threshold + RepeatPenaltyStep × попыток_в_окне
margin     = value − threshold'

band = margin < 0           → Worst
       margin < GoodMargin  → Base       (GoodMargin = 3)
       margin < BestMargin  → Good       (BestMargin = 7)
       иначе                → Best

Формы подхода:
  Убеждение:   margin ≥ −Cushion (2) → band = max(band, Base); band = min(band, Good)
  Запугивание: band == Worst → добавить эффект страха (−отношение);
               Best доступен при margin ≥ BestMargin − 2
  Торговля:    band не меняется; цена ×(1 − 0.10 × band)

Незанятая релевантная позиция → кандидатов нет → band = Worst, WasUnmanned = true
```

**ЯКОРЬ:** *порог = столько, сколько даёт середняк, вложившийся в этот навык на
текущей стадии; Хорошая полоса = специалист; Лучшая = специалист с профильным
трейтом.* Порог всегда виден до подтверждения; тест гарантирует, что показанное
равно применённому.

### 3.4 Досягаемость

```
Reach = TierReach[Тир] + Σ ЗданияReach + clamp(floor(log2(Pop / 200)), −1, +3)
        + Σ FactionReach(Ступень) − OrderReachPenalty[Уклад]

Доступно(X) ⇔ Reach ≥ X.RequiredReach
```

**ЯКОРЬ:** *200 человек (стартовое село) = Досягаемость 0 = только базовое.*
Каждое удвоение населения даёт +1, потолок +3. Игрок видит якорь как прилавок:
на нуле там три позиции, на +2 — десяток и есть лекарь.

### 3.5 Толки

```
СлуховЗаДень = 1 (если есть Таверна/Рынок и это день)
             + min( floor(margin / 2), 2 )
Источник только один за день; ночью — только через патруль.
```

**ЯКОРЬ:** *присутствие даёт один слух; вложенный навык — максимум три.*

### 3.6 Тир города

```
Продвижение при ОДНОВРЕМЕННОМ выполнении:
    ключевые здания построены
  И Население ≥ TierPopulationRequirement[тир]     = { 0, 400, 900, 1800 }
  И не менее N из M фракций на ступени ≥ Знакомый
```

Условие «N из M», а не «конкретная фракция», — механическая гарантия того, что
критический путь не заперт за одной фракцией (US-10.1). Покрыто тестом по контенту.

---

## 4. Порядок шагов дня

`BaseState.AdvanceCycle()` превращается в шаг `Production` упорядоченного
конвейера. Порядок объявлен константами **в одном файле** (`DayStepOrder`),
чтобы связи между системами читались с одного экрана.

| Порядок | Шаг | Что делает |
|---:|---|---|
| 0 | Clock | день, фаза (день/ночь) |
| 100 | Construction | стройки, 5 визуальных стадий |
| 200 | Production | существующий проход по позициям |
| 300 | Population | рост населения, вместимость |
| 400 | Derived | Досягаемость, присутствие на улицах, эффекты Уклада |
| 500 | Tension | **только** фоновый тик и пассивный дренаж |
| 600 | Obligations | сроки, просрочки |
| 700 | Pulse | накопители, предвестники, отбор сработавших |
| 800 | Incidents | выбор инцидента, резолв, исход |
| 900 | Healing | **не трогает Напряжение** — гарантировано тестом |
| 1000 | Signals | композиция сигналов по финальному состоянию |
| 1100 | Report | сборка `DayReport` |

**Почему такой порядок.** Производные считаются ровно один раз и только в шаге
`Derived` — двойной счёт невозможен (US-18.2). Напряжение тикает **до** Пульса,
чтобы сегодняшняя полоса взвешивала сегодняшнюю таблицу инцидентов. Инциденты
резолвятся **после** — их исход влияет на веса завтра, а не сегодня (нет обратной
связи внутри одного дня). Сигналы — последними, чтобы видеть финальное состояние.

**Ночь** — та же цепочка, но: производство и лечение частично отключены,
реплики горожан недоступны (диалоги закрыты), зато срабатываний до двух и
доступен канал `Ambient`.

```
DayReport
    Day, Phase
    CycleReport Production                    // временно, Итерация 1
    internal TensionChanges                   // числа наружу не выходят
    internal PulseSnapshot
    internal PopulationChange
    public Incidents                          // исходы публичны — они произошли
    public Completed, Obligations
    public SignalDigest Signals               // ЕДИНСТВЕННОЕ, что читает UI города
```

---

## 5. Слой сигналов

### 5.1 Правила

1. **Нет немого перехода полосы** — любая смена полосы даёт сигнал `Notable`+.
2. **Бюджет** — не более 4 сигналов в день; отбор по срочности, затем по
   «это изменение», затем по разнообразию каналов.
3. **Минимум один слот на дельту** — каждый день хотя бы один сигнал отвечает на
   «что изменилось со вчера». Прямое лекарство от «нет читаемого состояния».
4. **Предвестник не врёт** — уровень 2 обязан назвать домен, уровень 3 обязан
   появиться минимум за день до кризиса.
5. **Топики, не строки** — ядро выдаёт ключ и теги, реплики живут в контенте.

### 5.2 Напряжение → что видит игрок

| Полоса | Улицы и мудборд | Горожанин | Напарник | Инциденты |
|---|---|---|---|---|
| **Спокойно** | лавки открыты дотемна, дети во дворах, бельё на верёвках | «Хорошо, что вы здесь» | бытовой баентер | нет |
| **Ропот** | заколоченный ставень, мусор не убран | у колодца спорят о ценах | «Слышал на рынке — у Кожевников людей увели» | мелкая кража |
| **Брожение** | группы по 3–4 замолкают при твоём приближении; граффити фракций | лавочник просит охрану | «Тебе стоило бы показаться на площади» | организованная преступность |
| **Накал** | дневные ставни, патрули парами, часть NPC исчезла; костры в бочках | отводят глаза | напарники ссорятся между собой | серия, ночью кучнее |
| **Излом** | пустая площадь, тревожный колокол, бойцы фракций при оружии открыто | тишина | «Если ничего не сделаешь — я их не удержу» | кризис в течение 1–3 дней |

### 5.3 Ступень доверия фракции

| Ступень | Улицы | Приветствие | Что открыто |
|---|---|---|---|
| Чужой | их людей не видно | не узнают | ничего |
| Знакомый | одна фигура в форме | кивают | одна услуга, обычные цены |
| Свой | двое-трое у своих зданий | зовут по имени | три услуги, скидка, их квесты |
| Доверенный | заметное присутствие, свой угол на рынке | «наш» | полный список, пропуска, ходатайство за патрона |

### 5.4 Сплочённость фракции

| Полоса | Что видно |
|---|---|
| Крепка | единая форма, дисциплина, говорят от лица фракции |
| Трещит | двое их бойцов спорят на улице; «старик уже не тот»; услуга исполняется хуже обещанного |
| Раскол | своя метка поверх их граффити; драка между своими; **предвестник рождения враждебной группировки** |

### 5.5 Уклад

| Уклад | Прилавок | Ночь | Улица |
|---|---|---|---|
| Вольница | всё, включая запретное | патрулей нет, инциденты злее | шумно, наглые |
| Присмотр | обычное плюс серая зона | редкий обход | нейтрально |
| Порядок | только легальное | патрули, объявления | тише, косятся на патруль |
| Затвор | скудно, «по разрешению» | комендантский час, город вымер | молчат при страже |

**У Уклада нет ни одного невизуального эффекта** — он показывает себя сам.

### 5.6 Предвестники

| Ступень | Формат | Пример |
|---|---|---|
| 1 (≥55 %) | амбиент, без адреса | «Собаки третью ночь брешут» |
| 2 (≥80 %) | назван домен и место | «У склада третий день топчется один и тот же» |
| 3 (≥95 %) | названа близость, не день | «Кожевники что-то готовят. Скоро» |

### 5.7 Мудборд

```
Prosperity = f(Тир, Население/Вместимость, число построенных зданий)   → 0..4
Decay      = f(Полоса Напряжения, просроченные обязательства, Раскол)   → 0..4
Overlays   = теги: «баррикады», «палатки», «граффити:X», «патруль»
```

Два независимых числа, а не одно: **богатый и напряжённый город выглядит иначе,
чем бедный и спокойный.**

### 5.8 Доклад с постов

Раз в сутки напарник на релевантной позиции даёт устную субъективную сводку по
своему домену. **Точность — по той же лестнице полос:** сильный профильный
напарник даёт конкретику, слабый — расплывчатость, никого на позиции — тишина.

Это читаемое состояние без панели, новая ценность позиций и точка приложения
правила «усиливать позицию могут только напарники»: рабочие доклад не дают.

---

## 6. Секции BalanceConfig

Все — `[Serializable]` классы в `Game.Core.Balance`, с зеркальными SO-обёртками
в `Game.Gameplay` по эталону `BalanceConfigAsset`.

| Секция | Ключевые поля |
|---|---|
| `TensionBalance` | Max=1000; BandThresholds={200,400,600,800}; TierTickPerDay={1,2,4,7}; OrderTickMultiplier={1.25,1,0.85,0.7}; AllowedRaising / AllowedLowering (белый список); RaidDelta=−80; RaidCooldownDays=10; TempleDrainPerDay=−0.5; FortificationDrainPerDay=−0.3; BloodDeltaPerNode=10; BloodCapPerExpedition=50; CrisisGraceDays=3 |
| `PulseBalance` | DefaultThreshold=100; Forewarn1At=0.55; Forewarn2At=0.80; Forewarn3At=0.95; MaxFiresPerDay=1; MaxFiresPerNight=2; ChargeCapMultiplier=2; MinActiveTracks=3 |
| `CheckBalance` | GoodMargin=3; BestMargin=7; PersuadeCushion=2; IntimidateBestBonus=2; TradeBandDiscount=0.10; RepeatPenaltyStep=2; RepeatWindowDays=7 |
| `SettlementBalance` | ReachPopulationAnchor=200; ReachPopCap=3; ReachPopFloor=−1; TierReach={0,1,2,3}; TierPopulationRequirement={0,400,900,1800}; PopulationGrowthPerDay=1.5; TavernGrowthBonus=1.0; OrderRaiseThresholdBase=6; OrderRaiseThresholdStep=2; OrderDecayDaysWithoutSupport=14 |
| `FactionBalance` | TrustStepThresholds={0,30,60,85}; StreetPresenceThresholds={0,25,55,80}; CohesionBandThresholds={35,70}; SplinterInsistenceBonus=6; ResponseDelayByScale={1,3,7}; ObligationDefaultDays=14; ObligationDefaultPenalty=15 |
| `SignalBalance` | MaxSignalsPerDay=4; MinDeltaSlots=1; ForceSignalOnBandChange=true; RumorBase=1; RumorPerMarginStep=2; RumorMaxExtra=2 |

**Закрытый список драйверов Напряжения** (`TensionDriver`):
повышают — `CityTierTick`, `QuestChoice`, `ThreatOutcome`, `PlaystyleBlood`;
понижают — `CouncilRaid`, `CouncilEdict`, `TempleAura`, `Fortifications`,
`EventOutcome`. Всё остальное отклоняется.

---

## 7. Этапы

| Этап | Содержание | Сложность | Что доказывает |
|---|---|---|---|
| **Э0 «Пульс города»** | часы кампании, `TensionState` с 5 полосами, драйверы тир-тик и квест-выбор, `SignalComposer` на 3 канала, правило «нет немого перехода», порты `SkillKey`/`ISettlementActor`/`CostBundle` | M | скрытая шкала читается глазами |
| **Э1 «Инциденты и телеграфия»** | полосы исхода + предпросмотр порога, «незанятая позиция = Худшая», `WorldPulse` (≥3 накопителя), предвестники, 6–8 инцидентов в 2 тира + 1 кризис, ночь и патруль, доклад с постов | L | кризис ощущается заслуженным |
| **Э2 «Стороны»** | 1–2 фракции: отношение → 4 ступени → 3 услуги, присутствие на улицах, отзывчивость, штраф за повторные обращения, толки, слава | M | «где я с ними» читается без числа |
| **Э3 «Режим и досягаемость»** | Уклад (4 состояния) и его визуал, Досягаемость, перенаселение, изнанка/чёрный рынок, тиры 1–3 | L | город становится местом со своим устройством |
| **Э4 [ПОЗЖЕ]** | знакомые и верность, обязательства, раскол → новая враждебная фракция, патроны, совет из 6 действий, Готовность, тир 4 | XL | политика и финал |

**MVP = Э0 + Э1 + одна фракция с тремя услугами из Э2.**

---

## 8. Тесты (EditMode)

**Гарантии ограничений — главная ценность набора:**

| Тест | Проверяет |
|---|---|
| `Tension_WaitThirtyDays_OnlyTierTickEntries` | 30 дней «ждать» → в журнале нет драйверов кроме тира |
| `Tension_HealingDays_ProduceNoTension` | лечение 10 дней → дельта ровно 10×тик, полоса та же |
| `Tension_Apply_RejectsDriverOutsideWhitelist` | драйвер вне списка → `Applied == 0`, `Rejected == true` |
| `Tension_PassiveCampaign_NeverReachesFracture` | 200 дней без действий на тирах 1–2 → полоса ≤ Брожение |
| `Signals_BandChange_AlwaysEmitsNotable` | все переходы полос дают сигнал `Notable`+ |
| `Signals_NeverExceedBudget_AndAlwaysIncludeDelta` | ≤4 сигнала/день, ≥1 с `IsDelta` |
| `Pulse_ForewarnPrecedesEveryFire` | перед каждым срабатыванием были уровни 1 и 2 |
| `Pulse_AtLeastThreeActiveTracks` | инвариант детерминизма из §3.2 |
| `Crisis_NeverFiresWithoutLevel3Forewarn` | кризис невозможен без предвестника 3 и grace-окна |
| `Content_EveryIncident_HasNonViolentPath` | у каждого инцидента заполнен тихий путь (Поправка №1) |
| `Content_CriticalPath_ReachableWithAllFactionsHostile` | все фракции враждебны → критический путь проходим |
| `Core_HasNoRandom` | рефлексия: ядро не ссылается на `System.Random` |
| `Pulse_SameInputs_IdenticalSchedule` | два прогона по 200 дней → идентичные логи |
| `Architecture_SettlementLayer_DoesNotReferenceLegacyTypes` | новый слой не завязан на умирающие namespace |

**Проверки и полосы:** `Check_UsesBestSkillAmongPresent`,
`Check_UnmannedPosition_YieldsWorstBand`, `Check_Protagonist_IsValidCandidate`,
`Check_PreviewEqualsResolve`, `Check_Persuade_NeverWorstWithinCushion`,
`Check_Persuade_CannotReachBest`, `Check_Intimidate_ShiftsBothTails`,
`Check_RepeatWithinWindow_RaisesThreshold`.

**Поселение и фракции:** `Reach_MonotonicInPopulation`,
`Reach_AnchorIs200Equals0`, `Trust_StepUnlocksExactlyItsFavors`,
`Tier_Advance_RequiresNofMFactions_NotSpecificOne`,
`Order_DecaysWithoutSupport`, `Obligation_Default_RaisesInsistence`.

**Enum-coverage** (ловит «стат, который никто не читает»):
`EveryTensionDriver_HasConfigEntry`, `EveryTensionBand_HasSignalTopicSet`,
`EverySignalChannel_HasComposerBranch`, `EveryTrustStep_HasAtLeastOneFavor`,
`EveryOrderLevel_HasMarketAndNightProfile`.

---

## 9. Риски слоя

| # | Риск | Митигация | Статус |
|---|---|---|---|
| N1 | Сигналы превращаются в шум | бюджет 4/день, приоритеты, дельта-слот, доклад с постов. **Метрика плейтеста: 4 из 5 тестеров называют полосу словами верно** | МОНИТОР |
| N2 | Шкал слишком много | правило «якорь + потребитель + сигнал», enum-coverage тесты | КОНТРОЛИРУЕТСЯ |
| N3 | Детерминизм читается насквозь — игрок вычислит день кризиса | ≥3 накопителя с разными ставками, ставки меняются от действий, пороги и список источников скрыты | **ПРИНЯТ + МОНИТОР** |
| N4 | Порядок шагов дня заводит скрытые связи | `DayStepOrder` в одном файле, производные считаются один раз, тест на порядок | КОНТРОЛИРУЕТСЯ |
| N5 | Контент-взрыв реплик | топики и теги вместо уникальных строк, цепочка фолбэков; MVP = 5 полос × 2 канала × 3 варианта | КОНТРОЛИРУЕТСЯ |
| N6 | Кризис ощущается несправедливым (наследник R8) | 3 предвестника, grace-окно, «что и где честно, когда — нет», толки как покупаемая телеграфия | ПРИНЯТ + МОНИТОР |
| N7 | Перестройка ядра сломает слой | порты `SkillKey`/`ISettlementActor`/`CostBundle` + архитектурный тест | КОНТРОЛИРУЕТСЯ |

**N3 — главный риск этого дизайна** и прямое следствие выбора полного
детерминизма. Валидируется в срезе: если тестер на 20-й день говорит «сейчас
придут Кожевники» и оказывается прав три раза подряд — нужно добавлять
накопители, а не случайность.
