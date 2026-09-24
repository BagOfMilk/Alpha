using System;
using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Dungeons;
using Game.Core.Loop;
using Game.Core.Quests;
using Game.Core.Session.Views;

namespace Game.Core.Session.Bots
{
    /// <summary>
    /// Водій бота (§4.10 TEST_BUILD.md, §1.1 "боти в ядрі"): ЄДИНИЙ, хто звертається
    /// до <see cref="GameSession"/> напряму — політика (<see cref="IBotPolicy"/>)
    /// лише відповідає на запитання за View-шаром. Той самий код споживають
    /// <c>AllMechanicsCoverageTests</c> (Game.Tests.EditMode), <c>tools/Alpha.Play
    /// -- --auto</c> і <c>tools/Alpha.Sim</c> — три різні викликачі, один водій, як і
    /// вимагає §1.1 ("їх споживають тести Unity, автопрогін і tools/* однаково").
    ///
    /// Не володіє жодним генератором випадковості (інваріант 1) — увесь дайс, якщо
    /// потрібен (HitRuleKind.Percent), викликач передає ЗЗОВНІ через
    /// <see cref="NewGameOptions.Roller"/>, водій лише пересилає його в
    /// <see cref="GameSession"/>.
    /// </summary>
    public static class BotRunner
    {
        /// <summary>
        /// Замір темпу (§6.3, акцептанс R9): рахує команди по класах "ціни" з
        /// таблиці §6.3, а не з DayLog (більшість команд, підрахованих там,
        /// узагалі не лишає одразу-однозначної події — <c>SetPatrol</c>/
        /// <c>ConfirmMorning</c>/<c>AdvanceScene</c> тощо або не логують нічого,
        /// або логують подію, що не рахується 1:1 з викликом команди).
        /// </summary>
        public sealed class TimingTally
        {
            /// <summary>Читання екрану/сигналу: AdvanceScene-крок, підсумок AdvanceDay/AdvanceNight — 4 сек.</summary>
            public int ScreenReads;
            /// <summary>Проста команда: Assign, SetPatrol, ConfirmMorning/Evening — 3 сек.</summary>
            public int SimpleCommands;
            /// <summary>Рішення з читанням порогів: ResolveIncident, ResolveQuestChoice — 12 сек.</summary>
            public int Decisions;
            /// <summary>Хід у бою покроково: CombatMove/Attack/UseAbility/EnterOverwatch/EndTurn — 8 сек.</summary>
            public int CombatTurns;
            /// <summary>Ціла бойова сцена через «Автобій» — 20 сек замість суми ходів.</summary>
            public int AutoBattles;
            /// <summary>Данж-кімната: ResolveDungeonRoom/ResolveDungeonEvent — 15 сек.</summary>
            public int DungeonRooms;
            /// <summary>Збереження/завантаження — 5 сек.</summary>
            public int SaveLoads;

            public double TotalSeconds()
                => ScreenReads * 4 + SimpleCommands * 3 + Decisions * 12 +
                   CombatTurns * 8 + AutoBattles * 20 + DungeonRooms * 15 + SaveLoads * 5;

            public double TotalMinutes() => TotalSeconds() / 60.0;
        }

        /// <summary>
        /// Фікс-ревью пакета D2 (§6.1 рядок 43, major): знімок ОДНОГО виклику
        /// ResolveIncident/ResolveQuestChoice/ResolveFinale — саме ці три
        /// команди §6.1 рядок 43 зобов'язує дати відмінну від "нічого" подію.
        /// <see cref="HadConsequence"/> порівнює DayLog ДО і ПІСЛЯ САМЕ цього
        /// виклику (DayLog чиститься лише на межі AdvanceDay/AdvanceNight, а не
        /// між командами всередині фази, тож before/after навколо одного
        /// виклику ніколи не зачіпає сусідні команди).
        /// </summary>
        public sealed class ChoiceDiagnostic
        {
            public string Kind;
            public int Day;
            public int EventsBefore;
            public int EventsAfter;
            public bool HadConsequence => EventsAfter > EventsBefore;
        }

        public const string DefaultProtagonistName = "Богдан";

        /// <summary>Кімнат у "Покинутому таборі авангарду" (DefaultDungeon.AbandonedCamp) — жадібна політика штовхає рівно стільки, перш ніж банкувати.</summary>
        private const int AbandonedCampRoomCount = 3;

        /// <summary>Запобіжник від зациклення (реальний баг у водії/політиці мав би впасти тестом, а не повиснути назавжди).</summary>
        private const int MaxSteps = 20000;

        /// <summary>
        /// Поправка №7.7 — корінь дефекту "Майстерня добудовується надто
        /// пізно, щоб боти встигли щось зробити" був не лише у Days=4:
        /// <see cref="ApplyCouncilRoutine"/> замовляє РІВНО одну стройку за
        /// ранок і завжди саме ПЕРШУ незбудовану позицію цього списку — поки
        /// вона не по кишені, черга стоїть, і жодна пізніша позиція (навіть
        /// дешевша) не пробується. Зі старим порядком Майстерня (30 золота,
        /// без будматеріалів — одна з найдешевших) чекала за Тавернею/Храмом/
        /// Ринком/Укріпленнями (разом ~215 золота + 21 будматеріалу), і 15-
        /// денний тестовий прогін не встигав туди дійти навіть після переходу
        /// на один день стройки. Тут вона — друга: одразу за Лазаретом
        /// (лікування — теж дешево і рано), щоб ланцюг "стройка → пост →
        /// крафт" (§6.1 №22) встигав спрацювати до фіналу доби 5.
        ///
        /// Кампанійний порядок (<c>Steward.BuildPriority</c>, 90-денний замір
        /// клапана Поправки №6) цей файл НЕ чіпає — то баланс кампанії, а не
        /// тестової збірки.
        /// </summary>
        private static readonly string[] BuildPriority =
        {
            Game.Core.Base.DefaultBuildings.Infirmary,
            Game.Core.Base.DefaultBuildings.Workshop,
            Game.Core.Base.DefaultBuildings.Tavern,
            Game.Core.Base.DefaultBuildings.Temple,
            Game.Core.Base.DefaultBuildings.Market,
            Game.Core.Base.DefaultBuildings.Fortifications
        };

        /// <summary>Нова сесія (NewGame викликається тут) + прогін на <paramref name="days"/> календарних діб під <paramref name="policy"/>.</summary>
        public static GameSession PlayDays(IBotPolicy policy, int days, NewGameOptions options = null,
            List<GameEvent> fullLog = null, Action<GameEvent> onEvent = null,
            List<PendingOfferView> offerLog = null, List<string> viewKeyLog = null,
            List<RosterView> offerRosterLog = null, TimingTally tally = null,
            Action<SceneStepView> onSceneStep = null, Action<ChoiceDiagnostic> onChoiceApplied = null)
        {
            var session = new GameSession(options?.Roller);
            session.NewGame(options ?? new NewGameOptions());
            Drive(session, policy, days, fullLog, onEvent, offerLog, viewKeyLog, offerRosterLog, tally, onSceneStep, onChoiceApplied);
            return session;
        }

        /// <summary>
        /// Веде вже створену сесію (NewGame вже викликаний, будь-який стан від
        /// Creation до FreePlay) крізь <paramref name="days"/> календарних діб
        /// ЗВЕРХУ (Day лишається &lt; поточний+days на виході з <see cref="ContinueGame"/>-
        /// сценаріїв так само, як зі свіжої гри — рахунок відносний до вхідного стану).
        /// <paramref name="offerLog"/> — необов'язковий знімок КОЖНОГО справжнього
        /// <see cref="PendingOfferView"/> (стан Decision) за прогін, для тестів §6.1
        /// рядок 3 ("Antagonist/OnMission ніколи не BestActorId"), яким потрібен сам
        /// офер, а не лише подія з DayLog.
        /// </summary>
        public static void Drive(GameSession session, IBotPolicy policy, int days,
            List<GameEvent> fullLog = null, Action<GameEvent> onEvent = null,
            List<PendingOfferView> offerLog = null, List<string> viewKeyLog = null,
            List<RosterView> offerRosterLog = null, TimingTally tally = null,
            Action<SceneStepView> onSceneStep = null, Action<ChoiceDiagnostic> onChoiceApplied = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            int startDay = session.State == SessionState.Title ? 0 : session.CurrentView.Day;
            int untilDay = startDay + days;

            int lastMorningDoneForDay = int.MinValue;
            bool crisisWindowOpen = false;
            bool finaleResolvedDay5 = false;
            int steps = 0;

            // Скільки записів GameSession.DayLog уже перенесено в fullLog за
            // ПОТОЧНУ фазу — DayLog чиститься лише на ClearDayLog() (початок
            // AdvanceDay/AdvanceNight), а не після кожної команди, тож наївне
            // "додати весь DayLog після кожного кроку" дублювало б кожен запис
            // стільки разів, скільки команд лишилось до кінця фази.
            int collectedInPhase = 0;
            int lastDayLogVersion = session.DayLogVersion;

            while (true)
            {
                var state = session.State;
                if ((state == SessionState.Morning || state == SessionState.FreePlay) &&
                    session.CurrentView.Day >= untilDay)
                    break;

                if (steps++ > MaxSteps)
                    throw new InvalidOperationException(
                        "BotRunner: перевищено запобіжний ліміт кроків (" + MaxSteps +
                        ") для політики " + policy.Name + " — імовірне зациклення водія.");

                switch (state)
                {
                    case SessionState.Title:
                        throw new InvalidOperationException(
                            "BotRunner.Drive очікує сесію ПІСЛЯ NewGame — стан Title усередині прогону не підтримується.");

                    case SessionState.Creation:
                        session.SetProtagonistName(DefaultProtagonistName);
                        session.ConfirmCreation();
                        if (tally != null) tally.SimpleCommands += 2;
                        break;

                    case SessionState.Scene:
                    {
                        var step = session.AdvanceScene();
                        if (tally != null) tally.ScreenReads++;
                        onSceneStep?.Invoke(step);
                        if (viewKeyLog != null && step != null)
                        {
                            if (!string.IsNullOrEmpty(step.LineKey)) viewKeyLog.Add(step.LineKey);
                            if (!string.IsNullOrEmpty(step.EffectKey)) viewKeyLog.Add(step.EffectKey);
                            if (!string.IsNullOrEmpty(step.TransitionKey)) viewKeyLog.Add(step.TransitionKey);
                        }
                        break;
                    }

                    case SessionState.Morning:
                    case SessionState.FreePlay:
                    {
                        int day = session.CurrentView.Day;
                        if (lastMorningDoneForDay != day)
                        {
                            ApplyAssignments(session, policy, tally);
                            ApplyCouncilRoutine(session, day, tally);
                            MaybeDepartExpedition(session, policy, day, tally);
                            lastMorningDoneForDay = day;
                        }
                        // ConfirmMorning лише якщо ранок і досі не підвис у Dungeon
                        // (MaybeDepartExpedition(Delve) міг щойно перевести сюди).
                        if (session.State == SessionState.Morning || session.State == SessionState.FreePlay)
                        {
                            session.ConfirmMorning();
                            if (tally != null) tally.SimpleCommands++;
                        }
                        break;
                    }

                    case SessionState.Day:
                    {
                        session.AdvanceDay();
                        if (tally != null) tally.ScreenReads++;
                        // Форсована криза доби 5 (§3.5) відкривається САМЕ тут
                        // (GameSession.AdvanceDay: day==5 і фаза Idle) — незалежно
                        // від того, чи є в ту саму фазу ще й звичайне рішення.
                        if (session.CurrentView.Day == 5) crisisWindowOpen = true;
                        break;
                    }

                    case SessionState.Decision:
                    {
                        var offer = session.GetPendingOffer();
                        if (offerLog != null)
                        {
                            offerLog.Add(offer);
                            offerRosterLog?.Add(session.GetRosterView());
                        }
                        var path = policy.ChooseIncidentPath(offer);
                        int decisionDay = session.CurrentView.Day;
                        int decisionBefore = session.DayLog.Count;
                        session.ResolveIncident(path);
                        onChoiceApplied?.Invoke(new ChoiceDiagnostic
                        {
                            Kind = "ResolveIncident", Day = decisionDay,
                            EventsBefore = decisionBefore, EventsAfter = session.DayLog.Count
                        });
                        if (tally != null) tally.Decisions++;
                        break;
                    }

                    case SessionState.Evening:
                        MaybeOfferQuest(session, policy, tally, onChoiceApplied);
                        session.SetPatrol(policy.ChoosePatrol(session.CurrentView));
                        session.ConfirmEvening();
                        if (tally != null) tally.SimpleCommands += 2;
                        break;

                    case SessionState.Night:
                    {
                        if (crisisWindowOpen)
                        {
                            session.ReactToCrisis(ChooseCrisisReaction(policy));
                            if (tally != null) tally.Decisions++;
                            crisisWindowOpen = false;
                        }

                        if (session.CurrentView.Day == 5 && !finaleResolvedDay5)
                        {
                            // Позначаємо ДО виклику: кровавий шлях підвішує бій і
                            // повертається в Night вдруге лише ПІСЛЯ його розв'язку
                            // (GameSession.CompleteFinale) — другий візит має вже
                            // йти в AdvanceNight(), інакше "Фінал уже розв'язано".
                            finaleResolvedDay5 = true;
                            var offer = BotSupport.SyntheticOffer("Finale", "finale");
                            var path = policy.ChooseIncidentPath(offer);
                            int finaleDay = session.CurrentView.Day;
                            int finaleBefore = session.DayLog.Count;
                            session.ResolveFinale(path);
                            onChoiceApplied?.Invoke(new ChoiceDiagnostic
                            {
                                Kind = "ResolveFinale", Day = finaleDay,
                                EventsBefore = finaleBefore, EventsAfter = session.DayLog.Count
                            });
                            if (tally != null) tally.Decisions++;
                        }
                        else
                        {
                            session.AdvanceNight();
                            if (tally != null) tally.ScreenReads++;
                        }
                        break;
                    }

                    case SessionState.Dungeon:
                        DoDungeonRoutine(session, policy, tally);
                        break;

                    case SessionState.Battle:
                        DoBattleRoutine(session, policy, tally);
                        break;

                    case SessionState.Summary:
                        session.AcknowledgeSummary();
                        if (tally != null) tally.ScreenReads++;
                        break;

                    default:
                        throw new InvalidOperationException("BotRunner: невідомий стан " + state);
                }

                CollectDelta(session, fullLog, onEvent, ref collectedInPhase, ref lastDayLogVersion);
            }
        }

        /// <summary>
        /// Переносить лише НОВІ (від <paramref name="collectedInPhase"/>) записи DayLog —
        /// не весь список щоразу (див. коментар над полем у Drive).
        ///
        /// РЕГРЕСІЯ (знайдено бот-прогоном Steward, "forewarn.level2" Тугара на
        /// добу 6 губилося з <c>fullLog</c>/<c>onEvent</c>, хоч і БУЛО в
        /// <c>session.DayLog</c>): стара ознака нової фази — "лог коротший за
        /// курсор" (<c>log.Count &lt; collectedInPhase</c>) — мовчки НЕ
        /// спрацьовує, коли ClearDayLog() очистив лог, а нова фаза встигла
        /// дати БІЛЬШЕ записів, ніж курсор мав на кінець попередньої (типово:
        /// коротка Evening/SetPatrol-фаza з парою записів, за нею — щільний
        /// AdvanceDay з десятком). Курсор тоді лишається старим і перші
        /// count-курсор записів свіжої фази йдуть повз читача — гра їх бачить
        /// (LogEvent/DayLog не постраждали), а бот-водій (і CampaignSimulator/
        /// AllMechanicsCoverageTests/tools/Alpha.Play, які на ньому стоять) —
        /// ні. Правильна ознака нової фази — сам факт ClearDayLog(), а не
        /// висновок за розміром: <see cref="GameSession.DayLogVersion"/> росте
        /// рівно там, курсор скидається, коли версія змінилась.
        /// </summary>
        private static void CollectDelta(GameSession session, List<GameEvent> fullLog, Action<GameEvent> onEvent,
            ref int collectedInPhase, ref int lastDayLogVersion)
        {
            int version = session.DayLogVersion;
            if (version != lastDayLogVersion)
            {
                collectedInPhase = 0; // ClearDayLog() — нова фаза
                lastDayLogVersion = version;
            }

            var log = session.DayLog;
            if (fullLog != null || onEvent != null)
                for (int i = collectedInPhase; i < log.Count; i++)
                {
                    var e = log[i];
                    fullLog?.Add(e);
                    onEvent?.Invoke(e);
                }

            collectedInPhase = log.Count;
        }

        // ---- ранок: розстановка / рада / вилазка ----

        private static void ApplyAssignments(GameSession session, IBotPolicy policy, TimingTally tally)
        {
            var roster = session.GetRosterView();
            var city = session.GetCityView();
            var plan = policy.ChooseAssignments(roster, city);
            if (plan == null) return;
            foreach (var kv in plan)
            {
                session.Assign(kv.Key, kv.Value);
                if (tally != null) tally.SimpleCommands++;
            }
        }

        /// <summary>
        /// Спільна для всіх політик "добра економіка" (мирить дух Steward.Act із
        /// тим, що жодна політика не знає скілів напарників публічно): одна
        /// стройка за раз по пріоритету, облава й переселенці, коли на це є
        /// підстава з CityView/SessionView, і раз на кілька діб — нові дії ради
        /// (§4.15/§6.1 рядок 15), яких Steward.Act не знав. Невдала спроба
        /// (немає ради, кулдаун, бракує золота) просто нічого не логує —
        /// GameSession сам вирішує, чи застосувати наказ.
        /// </summary>
        private static void ApplyCouncilRoutine(GameSession session, int day, TimingTally tally)
        {
            var city = session.GetCityView();

            foreach (var id in BuildPriority)
            {
                if (IsBuiltOrBuilding(city, id)) continue;
                session.OrderBuilding(id);
                if (tally != null) tally.SimpleCommands++;
                break; // одна стройка за раз, як Steward.Act
            }

            if (city.RaidReady) { session.OrderRaid(); if (tally != null) tally.SimpleCommands++; }
            if (city.SettlersReady) { session.OrderSettlers(); if (tally != null) tally.SimpleCommands++; }

            MaybeCraft(session, city, tally);

            if (day % 3 == 0)
            {
                session.OrderPrepareThreat();
                session.OrderOutfitExpedition("outskirts");
                if (tally != null) tally.SimpleCommands += 2;
            }
            if (day % 4 == 0)
            {
                session.OrderDecree("tuhar_boyars");
                if (tally != null) tally.SimpleCommands++;
            }
            if (day % 5 == 0)
            {
                session.OrderDiplomacy("tuhar_boyars");
                if (tally != null) tally.SimpleCommands++;
            }
        }

        /// <summary>
        /// Спільна "добра економіка" (§6.1 рядок 22, Поправка №7.7 — стройка
        /// відкриває Майстерню швидко в тестовій збірці, але доти жоден бот
        /// не пробував нею скористатись): коли Майстерня вже стоїть і в
        /// сташі є хоч один предмет, пробуємо підняти рідкість — так само,
        /// як зробив би гравець, щойно побачив відкритий пост. Одна спроба
        /// на ранок, як і решта дій цього методу; неуспіх (іменний предмет,
        /// вже Epic, чи не по кишені) тихо пропускається — CraftUpgrade сам
        /// вирішує, чи застосувати команду.
        /// </summary>
        private static void MaybeCraft(GameSession session, CityView city, TimingTally tally)
        {
            bool workshopBuilt = false;
            if (city?.Built != null)
                foreach (var b in city.Built)
                    if (b.Id == Game.Core.Base.DefaultBuildings.Workshop) { workshopBuilt = true; break; }
            if (!workshopBuilt) return;

            var stash = session.GetStash();
            if (stash == null || stash.Count == 0) return;

            foreach (var item in stash)
            {
                var result = session.CraftUpgrade(item.InstanceId);
                if (result == Game.Core.Items.CraftResult.Success)
                {
                    if (tally != null) tally.SimpleCommands++;
                    return; // одна стройка/крафт за ранок — той самий темп, що й у стройки вище
                }
            }
        }

        private static bool IsBuiltOrBuilding(CityView city, string id)
        {
            if (city?.Built != null)
                foreach (var b in city.Built) if (b.Id == id) return true;
            if (city?.InProgress != null)
                foreach (var b in city.InProgress) if (b.Id == id) return true;
            return false;
        }

        private static void MaybeOfferQuest(GameSession session, IBotPolicy policy, TimingTally tally, Action<ChoiceDiagnostic> onChoiceApplied = null)
        {
            var offer = session.OfferQuestStage(DefaultQuests.HafiyaId);
            if (offer == null) return; // квест ще не готовий до нового кроку АБО вже завершений
            int count = offer.Options != null ? offer.Options.Count : 0;
            int idx = BotSupport.ClampIndex(policy.ChooseQuestOption(offer), count);
            int day = session.CurrentView.Day;
            int before = session.DayLog.Count;
            session.ResolveQuestChoice(idx);
            onChoiceApplied?.Invoke(new ChoiceDiagnostic
            {
                Kind = "ResolveQuestChoice", Day = day,
                EventsBefore = before, EventsAfter = session.DayLog.Count
            });
            if (tally != null) tally.Decisions++;
        }

        private static bool PartyIsAway(GameSession session)
        {
            var roster = session.GetRosterView();
            if (roster?.Companions == null) return false;
            foreach (var c in roster.Companions)
                if (BotSupport.IsReservedForField(c.Id) && c.Status == CompanionStatus.OnMission) return true;
            return false;
        }

        private static void MaybeDepartExpedition(GameSession session, IBotPolicy policy, int day, TimingTally tally)
        {
            // §3.4: відряд формується не раніше доби 4. У момент виклику (Morning
            // ще ДО AdvanceDay) SessionView.Day несе номер ОСТАННЬОЇ завершеної
            // доби — "ранок доби 4" сам іще має Day==3 (доба 1 завершилась із
            // Day==1, ..., доба 3 — з Day==3, і саме тоді настає ранок доби 4).
            if (day < 3) return;
            if (PartyIsAway(session)) return;

            var choice = policy.ChooseExpedition(session.CurrentView);
            if (!choice.HasValue) return;

            var c = choice.Value;
            session.DepartExpedition(c.SiteId, c.Approach, c.CompanionIds, c.Days);
            if (tally != null) tally.Decisions++; // куди/як/кого — рішення, не проста команда
        }

        // ---- данж (§4.10 DelveGreedyPolicy: штовхає глибше, поки не Wipe) ----

        private static void DoDungeonRoutine(GameSession session, IBotPolicy policy, TimingTally tally)
        {
            var dv = session.GetDungeonView();
            var room = dv?.CurrentRoom;
            if (room == null) { session.ExtractDungeon(); if (tally != null) tally.SimpleCommands++; return; }

            if (room.Type == "Event")
            {
                var offer = BotSupport.SyntheticDungeonEventOffer(room);
                int count = offer.Options != null ? offer.Options.Count : 0;
                int idx = BotSupport.ClampIndex(policy.ChooseQuestOption(offer), count);
                session.ResolveDungeonEvent(idx);
                if (tally != null) tally.DungeonRooms++;
            }
            else if (room.Type == "Treasure")
            {
                session.ResolveDungeonRoom(IncidentPath.Quiet); // Cache не має шляху — розв'язується як є
                if (tally != null) tally.DungeonRooms++;
            }
            else
            {
                var offer = BotSupport.SyntheticOffer("Incident", "dungeon." + room.Id);
                var path = policy.ChooseIncidentPath(offer);
                session.ResolveDungeonRoom(path);
                if (tally != null) tally.DungeonRooms++;
            }

            if (session.State != SessionState.Dungeon) return; // підвис у Battle або Wipe -> Morning

            var after = session.GetDungeonView();
            // Фікс-ревью D2 (minor): чи штовхати глибше вирішує сама політика
            // (ChoosePushDeeper), а не type-test "policy is DelveGreedyPolicy" —
            // будь-яка стороння реалізація IBotPolicy тепер теж може обрати
            // "жадібне" делве, не лише зашитий у BotRunner клас.
            bool moreRooms = after != null && after.RoomsCleared < AbandonedCampRoomCount;
            if (moreRooms && policy.ChoosePushDeeper(after)) session.PushDeeper();
            else session.ExtractDungeon();
            if (tally != null) tally.SimpleCommands++;
        }

        // ---- бій (§4.10: «Автобій» або покроково) ----

        private static void DoBattleRoutine(GameSession session, IBotPolicy policy, TimingTally tally)
        {
            var view = session.GetBattleView();
            if (view == null) return;

            if (policy.ChooseAutoResolve(view))
            {
                session.CombatAutoResolve();
                if (tally != null) tally.AutoBattles++;
                return;
            }

            var action = policy.ChooseCombatAction(view) ?? new CombatAction(CombatIntent.EndTurn);
            ExecuteCombatAction(session, view, action);
            if (tally != null) tally.CombatTurns++;
        }

        /// <summary>
        /// Перекладає намір політики в конкретні Combat*-команди GameSession.
        /// "Поточний" юніт береться напряму з <see cref="BattleView.CurrentUnitId"/>
        /// через <see cref="BotSupport.FindCurrent"/> — керує тим, хто ходить,
        /// незалежно від сторони: у покроковому режимі водій веде обидві сторони,
        /// так само як існуючі GameSessionTests керують ворожим юнітом напряму
        /// (CombatMove на training_scout_1) — тактичні команди GameSession
        /// навмисно side-агностичні.
        ///
        /// Фікс-ревью пакета D2 (доважок до блокера): "Overwatch, поки далеко"
        /// сам по собі — глухий кут, якщо його симетрично виконують ОБИДВІ
        /// сторони (як у PacifistPolicy — тактика не залежить від Side). Ніхто
        /// ніколи не наближається, тож ніхто не перетинає чужий сектор, і
        /// combat.overwatch.triggered НІКОЛИ не спрацьовує — бій тягнеться до
        /// RoundCap-нічиєї. <c>view.Round &lt;= 1</c> обмежує "візьми на приціл"
        /// ПЕРШИМ раундом: із другого раунду й далі водій штовхає юніта вперед
        /// (атака впритул або StepToward+Move), тож дистанція реально
        /// скорочується — і watch-стійки, взяті в раунді 1, отримують шанс
        /// СПРАЦЮВАТИ на чужому русі, а не просто оновлюватись нескінченно.
        /// </summary>
        private static void ExecuteCombatAction(GameSession session, BattleView view, CombatAction action)
        {
            var current = BotSupport.FindCurrent(view);
            if (current == null) { session.CombatEndTurn(); return; }

            var target = BotSupport.FindNearestOpposite(view, current);
            if (target == null || action.Intent == CombatIntent.EndTurn) { session.CombatEndTurn(); return; }

            int dist = BotSupport.Chebyshev(current.Pos, target.Pos);

            if (action.Intent == CombatIntent.Overwatch && dist > 1 && !current.IsOverwatching && view.Round <= 1)
            {
                var aim = new GridPos(target.Pos.X, target.Pos.Y);
                if (session.CombatEnterOverwatch(aim) == CombatActionResult.Success) return;
            }
            else
            {
                if (session.CombatAttack(target.Id) == CombatActionResult.Success) return;
            }

            var step = BotSupport.StepToward(view, current, target.Pos);
            if (step.HasValue)
            {
                var dest = new GridPos(step.Value.X, step.Value.Y);
                if (session.CombatMove(dest) == CombatActionResult.Success) return;
            }

            session.CombatEndTurn();
        }

        private static CrisisReaction ChooseCrisisReaction(IBotPolicy policy)
        {
            var offer = BotSupport.SyntheticOffer("Crisis", "crisis.test", isCrisis: true);
            var path = policy.ChooseIncidentPath(offer);
            return path == IncidentPath.Bloody ? CrisisReaction.Ignore : CrisisReaction.SpendGold;
        }
    }
}
