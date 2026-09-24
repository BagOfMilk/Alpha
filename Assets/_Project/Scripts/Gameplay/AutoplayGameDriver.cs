using System;
using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Quests;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;
using Game.Gameplay.UI;

namespace Game.Gameplay
{
    /// <summary>
    /// Фаза F (docs/TEST_BUILD.md, "PHASE F LOOP", ціль 1 "UI-TOUR AUTOPLAY"):
    /// жене РЕАЛЬНИЙ <see cref="GameShell"/> (той самий <c>Session</c>, який
    /// малює <c>OnGUI</c>) крізь усі екрани тестової збірки — на відміну від
    /// попередньої версії цього файлу, яка крутила ІЗОЛЬОВАНУ копію
    /// <c>GameSession</c> поза <c>GameShell</c>, тож екран весь прогін бачив
    /// незмінний Title.
    ///
    /// Команди йдуть через <c>shell.TryRun</c> (як і всі реальні кнопки
    /// екранів) — це і ловить <c>InvalidOperationException</c> без падіння
    /// туру, і годує <c>VillageStageBridge.Feed</c> (3D-хаб оновлює постаті на
    /// постах/стадії будівництва), інакше хаб на скріншотах лишався б
    /// незмінним, хоч сесія і просунулась.
    ///
    /// Один прохід стану-диспетчера (той самий принцип, що й
    /// <c>BotRunner.Drive</c>, з яким цей файл ділить-логіку рішень через
    /// <see cref="BotSupport"/>) — з інʼєкцією скріншотів/hub-туру в потрібних
    /// місцях замість сліпого прогону. Кровавий шлях форсується РІВНО там, де
    /// він гарантовано дає бій (перше рішення — вузол 1; ніч доби 5 — фінал),
    /// щоб "Battle: ... коли бій починається" й "day 5: ... фінальний бій"
    /// (ціль 1) не залежали від того, чи має протагоніст кандидата на кровавий
    /// шлях цього конкретного сида.
    /// </summary>
    public sealed class AutoplayGameDriver
    {
        private const int FramesShort = 2;
        private const int FramesMedium = 4;
        private const int FramesBattleEnter = 6;
        private const int MaxLoopSteps = 6000;
        private const int MaxManualBattleSteps = 3;

        private static readonly string[] HubTabSlugs =
        {
            "hub-posts", "hub-buildings", "hub-council", "hub-expedition", "hub-gear",
            "hub-people", "hub-quests", "hub-factions", "hub-readiness", "hub-save"
        };

        private readonly IAutoplayHost _host;
        private readonly GameShell _shell;
        private readonly bool _useThresholdRule;

        private int _freePlayStartDay = -1;
        private bool _hubToured;
        private bool _battleShown;
        private bool _dungeonShown;
        private bool _decisionShown;
        private bool _eveningShown;
        private bool _nightShown;
        private bool _delveDeparted;
        private bool _crisisFinaleAttempted;

        public AutoplayGameDriver(IAutoplayHost host, GameShell shell, bool useThresholdRule)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
            _useThresholdRule = useThresholdRule;
        }

        private GameSession Session => _shell.Session;

        public IEnumerator<int> Run()
        {
            // ---------------- Титул -> Нова гра ----------------
            foreach (var f in WaitFrames(FramesMedium)) yield return f;
            _host.Capture("title");
            yield return 0;

            var hitRule = _useThresholdRule ? HitRuleKind.Threshold : HitRuleKind.Percent;
            Run(() => Session.NewGame(new NewGameOptions
            {
                HitRule = hitRule,
                Seed = 1,
                Roller = _shell.Roller,
                SkipCreation = false
            }));
            _host.Log("Титул: Нова гра (" + hitRule + ").");

            // ---------------- Створення протагоніста ----------------
            foreach (var f in WaitFrames(FramesShort)) yield return f;
            _host.Capture("creation-default");
            yield return 0;

            var creationView = Session.GetProtagonistCreationView();
            var backgrounds = creationView?.AvailableBackgrounds;
            string backgroundId = backgrounds != null && backgrounds.Count > 1 ? backgrounds[1]
                : (backgrounds != null && backgrounds.Count > 0 ? backgrounds[0] : null);

            Run(() => Session.SetProtagonistName("Оксана"));
            Run(() => Session.SetProtagonistGender(Gender.Female));
            _shell.ProtagonistGender = Gender.Female;
            if (!string.IsNullOrEmpty(backgroundId))
            {
                string bg = backgroundId;
                Run(() => Session.SetProtagonistBackground(bg));
            }

            foreach (var f in WaitFrames(FramesShort)) yield return f;
            _host.Capture("creation-filled");
            yield return 0;

            Run(() => Session.ConfirmCreation());
            _host.Log("Створення підтверджено: Оксана, жіночий рід, передісторія " + (backgroundId ?? "?") + ".");

            // ---------------- Головний диспетчер станів ----------------
            int sceneShots = 0;
            int guard = 0;

            while (true)
            {
                guard++;
                if (guard > MaxLoopSteps)
                {
                    // Виняток, НЕ yield break: запобіжник — це провал тура
                    // (ознака зациклення десь у диспетчері станів), а не чесне
                    // завершення. AutoplayBootstrap ловить будь-який виняток і
                    // завершує процес кодом 2 — те саме мало б статися й тут,
                    // а не тихий код виходу 0 з незавершеним туром.
                    throw new InvalidOperationException("Запобіжник maxSteps (" + MaxLoopSteps +
                        ") — тур застряг у стані " + Session.State + " (можливе зациклення диспетчера).");
                }

                var state = Session.State;

                // ---- портретна сцена ----
                if (state == SessionState.Scene)
                {
                    var step = Run(() => Session.AdvanceScene());
                    foreach (var f in WaitFrames(FramesShort)) yield return f;
                    if (step != null && sceneShots < 6 && (!string.IsNullOrEmpty(step.LineKey) || step.IsFinished))
                    {
                        sceneShots++;
                        _host.Capture("opening-scene-" + sceneShots);
                        yield return 0;
                    }
                    continue;
                }

                // ---- ранок / вільна гра ----
                if (state == SessionState.Morning || state == SessionState.FreePlay)
                {
                    int day = Session.CurrentView.Day;

                    if (state == SessionState.FreePlay)
                    {
                        if (_freePlayStartDay < 0)
                        {
                            _freePlayStartDay = day;
                        }
                        else if (day > _freePlayStartDay)
                        {
                            foreach (var f in WaitFrames(FramesShort)) yield return f;
                            _host.Capture("freeplay-day" + day);
                            yield return 0;
                            _host.Log("FreePlay доби " + day + " досягнуто (стартувало на добу " + _freePlayStartDay + ") — тур завершено успішно.");
                            yield break;
                        }
                    }

                    if (!_hubToured && day == 1 && state == SessionState.Morning)
                    {
                        _hubToured = true;
                        for (int tab = 0; tab < HubTabSlugs.Length; tab++)
                        {
                            _shell.SetHubTab(tab);
                            foreach (var f in WaitFrames(FramesShort)) yield return f;
                            _host.Capture(HubTabSlugs[tab]);
                            yield return 0;
                        }
                        _shell.SetHubTab(0);
                        _host.Log("Хаб: усі " + HubTabSlugs.Length + " вкладок відвідано й знято.");
                    }

                    // "issue the bot's morning commands" — та сама розстановка
                    // за замовчуванням, що й BotSupport.DefaultAssignments
                    // (Steward), через shell.TryRun, щоб 3D-хаб оновив постаті.
                    var roster = Session.GetRosterView();
                    var assignments = BotSupport.DefaultAssignments(roster);
                    foreach (var kv in assignments)
                    {
                        string companionId = kv.Key;
                        string slotId = kv.Value;
                        Run(() => Session.Assign(companionId, slotId));
                    }

                    MaybeOrderBuilding(day);
                    if (day == 4 && !_delveDeparted) MaybeDepartDelve();

                    if (Session.State == SessionState.Morning || Session.State == SessionState.FreePlay)
                    {
                        Run(() => Session.ConfirmMorning());
                        _host.Log("Ранок доби " + day + " підтверджено.");
                    }
                    continue;
                }

                // ---- день (конвеєр) ----
                if (state == SessionState.Day)
                {
                    Run(() => Session.AdvanceDay());
                    continue;
                }

                // ---- точка рішення ----
                if (state == SessionState.Decision)
                {
                    var offer = Session.GetPendingOffer();
                    if (!_decisionShown)
                    {
                        foreach (var f in WaitFrames(FramesShort)) yield return f;
                        _host.Capture("decision-day" + Session.CurrentView.Day);
                        yield return 0;
                        _decisionShown = true;
                    }

                    var path = !_battleShown ? BloodyIfPossible(offer) : StewardPath(offer);
                    Run(() => Session.ResolveIncident(path));
                    continue;
                }

                // ---- бій (3D-арена + HUD) ----
                if (state == SessionState.Battle)
                {
                    foreach (var f in WaitFrames(FramesBattleEnter)) yield return f;

                    bool firstBattle = !_battleShown;
                    _battleShown = true;
                    _host.Capture(firstBattle ? "battle-start" : "battle-secondary-start");
                    yield return 0;

                    if (firstBattle)
                    {
                        for (int i = 0; i < MaxManualBattleSteps; i++)
                        {
                            var view = Session.GetBattleView();
                            if (view == null || view.Outcome != "Ongoing") break;
                            PlayOneBattleStep(view);
                            foreach (var f in WaitFrames(FramesShort)) yield return f;
                            if (Session.State != SessionState.Battle) break; // ручний хід сам добив бій
                        }
                        if (Session.State == SessionState.Battle)
                        {
                            _host.Capture("battle-after-turns");
                            yield return 0;
                        }
                    }

                    // Ручні ходи могли вже добити бій (Session.State пішов
                    // далі) — CombatAutoResolve() на порожньому бою кидає
                    // InvalidOperationException (RequireBattle), TryRun її
                    // ковтає, але немає сенсу й кликати.
                    if (Session.State == SessionState.Battle)
                    {
                        Run(() => Session.CombatAutoResolve());
                        foreach (var f in WaitFrames(FramesMedium)) yield return f;
                    }

                    // Панель результату (GameShell.DrawStateScreen: показує її,
                    // доки BattlePresenter.ResultPending — незалежно від того,
                    // куди вже пішов Session.State) — чекаємо, доки презентер
                    // сам її підхопить (BattleArenaController.Update →
                    // DetectExternalResolution), тоді знімаємо.
                    foreach (var f in WaitFrames(FramesShort)) yield return f;
                    _host.Capture(firstBattle ? "battle-result" : "battle-secondary-result");
                    yield return 0;
                    AcknowledgeBattleIfPending();
                    foreach (var f in WaitFrames(FramesShort)) yield return f;
                    continue;
                }

                // ---- данж ----
                if (state == SessionState.Dungeon)
                {
                    if (!_dungeonShown)
                    {
                        _dungeonShown = true;
                        int roomGuard = 0;
                        while (Session.State == SessionState.Dungeon && roomGuard++ < 12)
                        {
                            var view = Session.GetDungeonView();
                            if (view == null) break;

                            if (view.CurrentRoom != null)
                            {
                                foreach (var f in WaitFrames(FramesShort)) yield return f;
                                _host.Capture("dungeon-room-" + (view.RoomsCleared + 1));
                                yield return 0;

                                var room = view.CurrentRoom;
                                if (room.Type == "Combat")
                                {
                                    if (room.HasQuietBypass) Run(() => Session.ResolveDungeonRoom(IncidentPath.Quiet));
                                    else Run(() => Session.ResolveDungeonRoom(IncidentPath.Bloody));
                                }
                                else if (room.Type == "Event")
                                {
                                    foreach (var f in WaitFrames(FramesShort)) yield return f;
                                    _host.Capture("dungeon-event");
                                    yield return 0;
                                    Run(() => Session.ResolveDungeonEvent(1));
                                }
                                else
                                {
                                    Run(() => Session.ResolveDungeonRoom(IncidentPath.Quiet));
                                }

                                foreach (var f in WaitFrames(FramesShort)) yield return f;
                                if (Session.State == SessionState.Battle) break; // головний цикл сам розбере бій
                            }
                            else if (view.RoomsCleared < 3)
                            {
                                Run(() => Session.PushDeeper());
                                foreach (var f in WaitFrames(FramesShort)) yield return f;
                            }
                            else
                            {
                                foreach (var f in WaitFrames(FramesShort)) yield return f;
                                _host.Capture("dungeon-extract");
                                yield return 0;
                                Run(() => Session.ExtractDungeon());
                                foreach (var f in WaitFrames(FramesShort)) yield return f;
                                break;
                            }
                        }
                    }
                    else
                    {
                        var view = Session.GetDungeonView();
                        if (view != null && view.CurrentRoom == null) Run(() => Session.ExtractDungeon());
                        else if (view != null) Run(() => Session.ResolveDungeonRoom(IncidentPath.Quiet));
                    }
                    continue;
                }

                // ---- вечір ----
                if (state == SessionState.Evening)
                {
                    if (!_eveningShown)
                    {
                        foreach (var f in WaitFrames(FramesShort)) yield return f;
                        _host.Capture("evening-patrol");
                        yield return 0;
                    }

                    var quest = Run(() => Session.OfferQuestStage(DefaultQuests.HafiyaId));
                    if (!_eveningShown && quest != null)
                    {
                        foreach (var f in WaitFrames(FramesShort)) yield return f;
                        _host.Capture("evening-quest-offer");
                        yield return 0;
                    }
                    _eveningShown = true;

                    if (quest?.Options != null && quest.Options.Count > 0)
                    {
                        int idx = 0;
                        for (int i = 0; i < quest.Options.Count; i++)
                            if (quest.Options[i].HasCandidate) { idx = i; break; }
                        int chosen = idx;
                        Run(() => Session.ResolveQuestChoice(chosen));
                    }

                    Run(() => Session.SetPatrol(Session.CurrentView.Day % 2 == 0));
                    Run(() => Session.ConfirmEvening());
                    continue;
                }

                // ---- ніч (патруль / криза доби 5 / фінал) ----
                if (state == SessionState.Night)
                {
                    int day = Session.CurrentView.Day;

                    if (day == 5 && !_crisisFinaleAttempted)
                    {
                        // Один раз за тур (той самий принцип, що й
                        // _delveDeparted): якщо ReactToCrisis/ResolveFinale
                        // не просунули Session.State з якоїсь причини (вікно
                        // кризи вже закрите, фінал уже розв'язано раніше
                        // тощо), TryRun ковтає виняток мовчки — без цього
                        // прапорця головний цикл повторював би весь блок
                        // щокроку до запобіжника maxSteps (спіймано
                        // тур-автоплеєм: тисячі дублів crisis-day5/
                        // finale-choice).
                        _crisisFinaleAttempted = true;

                        foreach (var f in WaitFrames(FramesShort)) yield return f;
                        _host.Capture("crisis-day5");
                        yield return 0;
                        Run(() => Session.ReactToCrisis(CrisisReaction.SpendGold));

                        foreach (var f in WaitFrames(FramesShort)) yield return f;
                        _host.Capture("finale-choice");
                        yield return 0;
                        // Кроваво — навмисно (ціль 1: "фінальний бій" мусить
                        // трапитись, не залежно від того, чи є тихий кандидат).
                        Run(() => Session.ResolveFinale(IncidentPath.Bloody));
                        continue;
                    }

                    if (!_nightShown)
                    {
                        foreach (var f in WaitFrames(FramesShort)) yield return f;
                        _host.Capture("night");
                        yield return 0;
                        _nightShown = true;
                    }
                    Run(() => Session.AdvanceNight());
                    continue;
                }

                // ---- підсумок доби 5 ----
                if (state == SessionState.Summary)
                {
                    foreach (var f in WaitFrames(FramesMedium)) yield return f;
                    _host.Capture("summary");
                    yield return 0;
                    Run(() => Session.AcknowledgeSummary());
                    continue;
                }

                throw new InvalidOperationException("Тур не знає, як показати стан " + state + " — диспетчер не покриває його.");
            }
        }

        // ===================== виконання команд =====================

        private void Run(Action action)
        {
            _shell.TryRun(action);
            LogIfMessage();
        }

        private T Run<T>(Func<T> action)
        {
            var result = _shell.TryRun(action);
            LogIfMessage();
            return result;
        }

        private void LogIfMessage()
        {
            if (!string.IsNullOrEmpty(_shell.LastMessage))
                _host.Log("  (" + _shell.LastMessage + ")");
        }

        private static IEnumerable<int> WaitFrames(int frames)
        {
            for (int i = 0; i < frames; i++) yield return 0;
        }

        // ===================== рішення =====================

        private static IncidentPath BloodyIfPossible(PendingOfferView offer)
        {
            if (offer?.Options != null)
                foreach (var o in offer.Options)
                    if (o.Path == IncidentPathView.Bloody && o.HasCandidate) return IncidentPath.Bloody;
            return StewardPath(offer);
        }

        private static IncidentPath StewardPath(PendingOfferView offer)
        {
            if (offer?.Options != null)
            {
                foreach (var o in offer.Options)
                    if (o.Path == IncidentPathView.Quiet && o.HasCandidate) return IncidentPath.Quiet;
                foreach (var o in offer.Options)
                    if (o.Path == IncidentPathView.Bloody && o.HasCandidate) return IncidentPath.Bloody;
            }
            return IncidentPath.Quiet;
        }

        // ===================== ранок: рада / вилазка =====================

        private void MaybeOrderBuilding(int day)
        {
            if (day != 2) return; // §3.2 TEST_BUILD.md: рада замовляє Майстерню на добу 2
            Run(() => Session.OrderBuilding(DefaultBuildings.Workshop));
        }

        /// <summary>
        /// §3.4: збори у вилазку-данж «Покинутий табір авангарду» на добу 4 —
        /// трійка з протагоніста/Максима/Мирослави, якщо всі легальні
        /// кандидати. <c>_delveDeparted</c> виставляється ОДРАЗУ (навіть якщо
        /// цей конкретний виклик нічого не відправив) — інакше, якщо
        /// повернення з данжу не одразу зсуває <c>CurrentView.Day</c> за межі
        /// 4, головний цикл кликав би цей метод ЗНОВУ щоранку доби 4, доки не
        /// впреться в запобіжник maxSteps (спіймано тур-автоплеєм).
        /// </summary>
        private void MaybeDepartDelve()
        {
            _delveDeparted = true;
            var roster = Session.GetRosterView();
            var candidateIds = new[] { GameSession.ProtagonistId, "maksym", "myroslava" };
            var party = new List<string>();
            foreach (var id in candidateIds)
            {
                var c = ScreenText.FindCompanion(roster, id);
                if (ScreenText.AssignCandidateLegality(c).Enabled) party.Add(id);
            }
            if (party.Count == 0) return;

            var preview = Run(() => Session.PreviewExpedition("abandoned_camp", ExpeditionApproach.Delve, party));
            if (preview == null) return;
            int days = preview.Days;
            Run(() => Session.DepartExpedition("abandoned_camp", ExpeditionApproach.Delve, party, days));
        }

        // ===================== бій: хід гравця =====================

        private void PlayOneBattleStep(BattleView view)
        {
            var current = BotSupport.FindCurrent(view);
            if (current == null) { Run(() => Session.CombatEndTurn()); return; }

            if (!string.Equals(current.Side, "Player", StringComparison.Ordinal))
            {
                Run(() => Session.CombatEndTurn());
                return;
            }

            var target = BotSupport.FindNearestOpposite(view, current);
            if (target == null) { Run(() => Session.CombatEndTurn()); return; }

            if (BotSupport.Chebyshev(current.Pos, target.Pos) <= 1)
            {
                string targetId = target.Id;
                Run(() => Session.CombatAttack(targetId));
                return;
            }

            var step = BotSupport.StepToward(view, current, target.Pos);
            if (step.HasValue)
            {
                var dest = new GridPos(step.Value.X, step.Value.Y);
                Run(() => Session.CombatMove(dest));
            }
            else
            {
                Run(() => Session.CombatEndTurn());
            }
        }

        private void AcknowledgeBattleIfPending()
        {
            var presenter = _shell.BattlePresenter;
            if (presenter == null || !presenter.IsActive) return;

            if (presenter.ResultPending)
                presenter.AcknowledgeResult();
            else if (Session.State != SessionState.Battle)
                // Запобіжник: ResultPending з якоїсь причини не встиг
                // виставитись (не той шлях завершення бою), а бою вже немає —
                // краще явний Exit(), ніж арена, що лишається у фоні до кінця
                // тура (той самий "привид арени" за Hub-скріном, що вже
                // спіймав тур-автоплей).
                presenter.Exit();
        }
    }
}
