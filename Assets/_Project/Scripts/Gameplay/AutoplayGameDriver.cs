using System;
using System.Collections.Generic;
using Game.Core.Base;
using Game.Core.Characters.Creation;
using Game.Core.Checks;
using Game.Core.Combat;
using Game.Core.Dungeons;
using Game.Core.Expeditions;
using Game.Core.Loop;
using Game.Core.Quests;
using Game.Core.Scenes;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;
using Game.Gameplay.UI;
using UnityEngine;

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

        /// <summary>
        /// Поправка №7, <c>-autoplay-long</c>: стеля доби вільної гри, на якій
        /// довгий тур зупиняється, навіть якщо великий бунт на площі так і
        /// не настав (ота ж сама причина, що в <c>TestBuildTensionPaceTests</c>:
        /// 30-денні бот-прогони). Трохи вище цільової доби 25 бунту —
        /// запобіжник, а не очікуваний результат.
        /// </summary>
        public const int LongTourDayCap = 32;

        /// <summary>
        /// Бій v2 (docs/COMBAT_V2.md §5, §7.4): скільки реального часу
        /// (<c>Time.realtimeSinceStartup</c>, не ігрового — хід ворога йде
        /// поза контролем цього водія) можна чекати один суцільний хід
        /// ворога, перш ніж вважати його завислим. Урок, заради якого
        /// переписаний увесь бойовий шматок водія: раніше він сам тиснув
        /// "Кінець ходу" за ворога й ніколи не міг застрягнути — тепер хід
        /// ворога веде виключно презентер (<see cref="IBattleHudData"/>), і
        /// водій зобов'язаний уміти зафіксувати, якщо той сам зависне.
        /// </summary>
        private const float EnemyTurnWatchdogSeconds = 60f;

        /// <summary>Загальний запобіжник на будь-яке очікування <c>IsBusy</c> (такт показу) — коротший за ватчдог ходу ворога вище, бо стосується лише одного такту, не всього ходу.</summary>
        private const float BusyWatchdogSeconds = 30f;

        /// <summary>Стеля дій ГРАВЦЯ за бій у режимі <c>-autoplay-battle</c> (докладніше — <see cref="RunBattleOnly"/>).</summary>
        private const int MaxBattleTourPlayerActions = 400;

        private static readonly string[] HubTabSlugs =
        {
            "hub-posts", "hub-buildings", "hub-council", "hub-expedition", "hub-gear",
            "hub-people", "hub-quests", "hub-factions", "hub-readiness", "hub-save", "hub-journal"
        };

        private readonly IAutoplayHost _host;
        private readonly GameShell _shell;
        private readonly bool _useThresholdRule;
        private readonly bool _longTour;

        private int _freePlayStartDay = -1;
        private bool _freePlayMilestoneShown;
        private bool _hubToured;
        private bool _battleShown;
        private bool _dungeonShown;
        private bool _decisionShown;
        private bool _eveningShown;
        private bool _nightShown;
        private bool _delveDeparted;
        private bool _crisisFinaleAttempted;

        // ===================== -autoplay-battle: лише бій, крізь IBattleInput =====================
        // (докладніше — RunBattleOnly нижче). Прапорці "раз за бій" і "раз за
        // тур" одноразових знімків — той самий прийом, що _capturedOnce/
        // _battleShown вище, окремими полями, бо тут стан прив'язаний до
        // фаз ОДНОГО бою (хід ворога/здібність/дозор), а не до тура в цілому.
        private bool _btFirstEnemyTurnDone;
        private bool _btBannerShotTaken;
        private bool _btEnemyActionShotTaken;
        private bool _btHoverEnemyShotTaken;
        private bool _btHoverTileShotTaken;
        private bool _btAfterMoveShotTaken;
        private bool _btAfterAttackShotTaken;
        private bool _btAbilityShotTaken;
        private bool _btOverwatchShotTaken;
        private bool _btAbilityUsedOnce;

        /// <summary>Рев'ю Бою v2: раз за бій — здібність, якій потрібна клітинка («Пастка» чи двофазний «Наказ пересунутися»).</summary>
        private bool _btTileAbilityUsedOnce;
        private bool _btOverwatchUsedOnce;

        /// <summary>Скільки подій <see cref="GameSession.DayLog"/> уже перевірено на потребу знімка (-autoplay-long) — той самий "не повторюй" прийом, що <see cref="_capturedOnce"/> нижче.</summary>
        private int _dayLogScanIndex;

        /// <summary>
        /// Останній перевірений рядок DayLog. GameSession очищає DayLog на межі
        /// фаз (ClearDayLog), а лічильник версії — internal; тож очищення
        /// розпізнаємо так: рядок перед індексом уже не той самий об'єкт.
        /// Без цього після першого ж очищення індекс лишався більшим за новий
        /// журнал, і тур мовчки пропускав зсуви смуг і передвісники.
        /// </summary>
        private GameEvent _lastScannedEvent;

        /// <summary>-autoplay-long: розв'язку великого бунту на площі вже показано знімком — можна завершувати тур, не чекаючи стелі доби.</summary>
        private bool _greatCrisisResolved;

        /// <summary>
        /// Поправка №7.8, п.4: іменовані знімки Choice-кроків/наслідків/
        /// квестових глав — за id, не за лічильником, тож кожен показується
        /// РІВНО раз за тур, скільки б разів диспетчер не заглянув у той самий
        /// стан (кілька вечорів поспіль без нового вибору тощо).
        /// </summary>
        private readonly HashSet<string> _capturedOnce = new HashSet<string>();

        // ===================== -autoplay-journal: журнальний тур =====================
        //
        // Ціль 1 (пряме доручення власника 25.09.2026): "Ти протестив що гру можна
        // почати, створити персонажа, взяти в команду і тд та пройти першу кризу?
        // Усі механіки можна виконати по журналу?" — той самий сценарій, що вже
        // доведений у Core (MechanicsJournalCompletionTests.
        // JournalPlayer_OnePlaythrough_SeesAllFortyFourEntries), тут же — крізь
        // РЕАЛЬНІ екрани (GameShell/*Screen.cs), тими самими командами, якими
        // грає людина: жодного прямого виклику GameSession в обхід TryRun/
        // SceneScreen.Driver*, яких цей файл і так уже не робить у Run() вище.

        /// <summary>Стеля доби журнального туру — вище цільової доби природного бунту (§7.9 CLAUDE.md, ~доба 25) з запасом на повільніший підбір без Стюарда/патруля щоночі.</summary>
        public const int JournalDayCap = 60;
        private const int JournalMaxLoopSteps = 20000;

        private bool _jBattleShown; // перший бій (вузол 1) — кровавий шлях форсовано і ведеться ПОКРОКОВО наївною тактикою; решта — «Автобій».
        private int _jLastMorningDay = int.MinValue;
        private bool _jEquipped;
        private bool _jCrafted;
        private bool _jTrained;
        private bool _jSaved;
        private bool _jCrisisFinaleAttempted;

        /// <summary>Id журнальних записів, уже знятих скріншотом "journal-&lt;id&gt;" — рівно один раз за тур, у момент, коли вони вперше стають Seen.</summary>
        private readonly HashSet<string> _jSeenIds = new HashSet<string>();

        private static readonly string[] JournalBuildPriority =
        {
            DefaultBuildings.Infirmary, DefaultBuildings.Workshop, DefaultBuildings.Tavern,
            DefaultBuildings.Temple, DefaultBuildings.Market, DefaultBuildings.Fortifications
        };

        /// <summary>Скільки з 44 записів <c>GetMechanicsJournal()</c> побачено на кінець туру — <see cref="AutoplayBootstrap"/> читає це для коду виходу 4.</summary>
        public int JournalSeenCount { get; private set; }

        /// <summary>Скільки всього записів у реєстрі журналу (44 на час написання) — рахується динамічно, нічого не хардкодиться.</summary>
        public int JournalTotalCount { get; private set; }

        public AutoplayGameDriver(IAutoplayHost host, GameShell shell, bool useThresholdRule, bool longTour = false)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
            _useThresholdRule = useThresholdRule;
            _longTour = longTour;
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

                // -autoplay-long: знімки зсуву смуги/передвісників кризи/
                // розв'язки бунту — за НОВИМИ рядками DayLog, незалежно від
                // того, у якому стані диспетчер саме зараз (band-change/
                // forewarn можуть прийти конвеєром дня, а не тільки в Decision).
                if (_longTour)
                    foreach (var f in ScanForGreatCrisisSignals()) yield return f;

                // ---- портретна сцена (Поправка №7.8, п.1/4) ----
                if (state == SessionState.Scene)
                {
                    // ЦЕЙ САМИЙ SceneScreen, що малює OnGUI — водій просуває
                    // курсор через нього (не Session.AdvanceScene() напряму),
                    // інакше в екрана з'явився б другий, розсинхронізований
                    // курсор: скріншоти фіксували б застиглий перший кадр,
                    // поки Session тихо пішла далі під капотом (див. коментар
                    // над SceneScreen.DriverAdvance).
                    var scene = _shell.Scene;
                    var step = scene.DriverAdvance(_shell);
                    LogIfMessage();
                    foreach (var f in WaitFrames(FramesShort)) yield return f;

                    if (step != null && step.IsChoice)
                    {
                        string choiceId = step.ChoiceId ?? "choice";
                        if (_capturedOnce.Add("choice-" + choiceId))
                        {
                            _host.Capture("choice-" + choiceId);
                            yield return 0;
                        }

                        scene.DriverChoose(_shell, ChooseSceneOptionIndex(step));
                        LogIfMessage();
                        foreach (var f in WaitFrames(FramesShort)) yield return f;

                        if (scene.IsShowingConsequence)
                        {
                            if (_capturedOnce.Add("consequence-" + choiceId))
                            {
                                _host.Capture("consequence-" + choiceId);
                                yield return 0;
                            }
                            scene.DriverContinueConsequence();
                            foreach (var f in WaitFrames(FramesShort)) yield return f;
                        }
                        continue;
                    }

                    if (step != null && sceneShots < 10 && (!string.IsNullOrEmpty(step.LineKey) || step.IsFinished))
                    {
                        sceneShots++;
                        _host.Capture("scene-line-" + sceneShots);
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
                        else if (day > _freePlayStartDay && !_freePlayMilestoneShown)
                        {
                            _freePlayMilestoneShown = true;

                            foreach (var f in WaitFrames(FramesShort)) yield return f;
                            _host.Capture("freeplay-day" + day);
                            yield return 0;

                            // Поправка №7.8, п.4: журнал механік наостанок —
                            // до цього моменту тур пройшов бій/квести/вилазку/
                            // данж/сцени-вибори/раду, тож рахунок "побачено"
                            // тут найповніший за весь прогін (порівняй із
                            // "hub-journal" дня 1, де побачено майже нічого).
                            // -autoplay-long: журнал наостанок таки ще не
                            // "наостанок" — тур продовжує до бунту/стелі
                            // доби, тож знімок тут лише проміжний.
                            _shell.SetHubTab(10);
                            foreach (var f in WaitFrames(FramesShort)) yield return f;
                            _host.Capture(_longTour ? "mechanics-journal-freeplay-start" : "mechanics-journal-final");
                            yield return 0;
                            _shell.SetHubTab(0);

                            if (!_longTour)
                            {
                                _host.Log("FreePlay доби " + day + " досягнуто (стартувало на добу " + _freePlayStartDay + ") — тур завершено успішно.");
                                yield break;
                            }

                            _host.Log("FreePlay доби " + day + " досягнуто — довгий тур (-autoplay-long) веде далі, руки геть, до бунту на площі або доби " + LongTourDayCap + ".");
                        }

                        // -autoplay-long: завершуємо, щойно розв'язку бунту
                        // вже показано знімком, або впираємось у стелю доби —
                        // те саме "не застрягти назавжди", що MaxLoopSteps
                        // вище, лише для природної кризи, а не диспетчера.
                        if (_longTour && (_greatCrisisResolved || day >= LongTourDayCap))
                        {
                            foreach (var f in WaitFrames(FramesShort)) yield return f;
                            _shell.SetHubTab(10);
                            foreach (var f in WaitFrames(FramesShort)) yield return f;
                            _host.Capture("mechanics-journal-final");
                            yield return 0;
                            _shell.SetHubTab(0);

                            _host.Log("Довгий тур завершено на добу " + day +
                                (_greatCrisisResolved ? " (бунт на площі розв'язано)." : " (досягнуто стелі " + LongTourDayCap + " діб без бунту).") );
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

                        foreach (var f in ExploreTour()) yield return f;
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
                        // Рівно та сама дія, що й кнопка «Почати день» (HubScreen.StartDay),
                        // а не виклик ядра в обхід екрана.
                        Run(() => UI.HubScreen.StartDay(Session));
                        _host.Log("Ранок доби " + day + " підтверджено.");
                    }
                    continue;
                }

                // ---- день (конвеєр) ----
                if (state == SessionState.Day)
                {
                    Run(() => UI.HubScreen.StartDay(Session));
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
                    // -autoplay-long: великий бунт на площі — окремий знімок
                    // РІВНО на своєму рішенні, незалежно від _decisionShown
                    // вище (яке ловить лише ПЕРШЕ рішення за весь тур).
                    else if (_longTour && offer != null && offer.IsCrisis &&
                             _capturedOnce.Add("crisis-riot-decision"))
                    {
                        foreach (var f in WaitFrames(FramesShort)) yield return f;
                        _host.Capture("crisis-riot-decision");
                        yield return 0;
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

                    // Бій v2 (docs/COMBAT_V2.md §7.4): автотур ходить у бій ЛИШЕ
                    // через IBattleInput — той самий шлях, що клік миші/клавіші.
                    // Раніше цей блок за ворога сам тиснув CombatEndTurn (і тому
                    // тур ніколи не бачив завислого ходу ворога, на відміну від
                    // людини) — WaitOutEnemyTurn нижче натомість ЧЕКАЄ, поки
                    // презентер сам його проведе, і падає з діагностикою, якщо
                    // за EnemyTurnWatchdogSeconds нічого не відбулось.
                    var hud = _shell.BattlePresenter as IBattleHudData;
                    if (hud == null)
                        throw new InvalidOperationException(
                            "Автотур: BattlePresenter відсутній або не реалізує IBattleHudData — 3D-презентер бою не знайдено (docs/COMBAT_V2.md §7.4).");

                    if (firstBattle)
                    {
                        int manualSteps = 0;
                        while (manualSteps < MaxManualBattleSteps && Session.State == SessionState.Battle)
                        {
                            foreach (var f in WaitWhileBusy(hud)) yield return f;
                            if (Session.State != SessionState.Battle) break;

                            var view = hud.View;
                            if (view == null || view.Outcome != "Ongoing") break;

                            if (view.IsAiTurn)
                            {
                                // Хід ворога — НЕ рахується у стелю ручних кроків
                                // гравця: презентер веде його сам.
                                foreach (var f in WaitOutEnemyTurn(hud, false)) yield return f;
                                continue;
                            }

                            manualSteps++;
                            PlayOneBattleStep(hud, view);
                            foreach (var f in WaitFrames(FramesShort)) yield return f;
                        }
                        if (Session.State == SessionState.Battle)
                        {
                            _host.Capture("battle-after-turns");
                            yield return 0;
                        }
                    }

                    // Ручні ходи могли вже добити бій (Session.State пішов
                    // далі); RequestAutoResolve() — той самий контракт, що
                    // кнопка «Автобій» (жодного Session.CombatAutoResolve()
                    // напряму — це й був би той самий обхід ядра, який
                    // ховав завислий хід ворога).
                    if (Session.State == SessionState.Battle)
                    {
                        hud.RequestAutoResolve();
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
                    // Фікс-ревью (блокер, знайдено QA): попередня версія
                    // (39445a8) чекала WaitFrames(2) і сподівалась, що OnGUI
                    // (де живе GameShell.RouteOfferedSceneContentIfAvailable)
                    // встигне спрацювати МІЖ цими двома кроками ітератора.
                    // Кадровий каданс OnGUI виявився НЕ детермінованим у
                    // фоновому (без фокуса вікна) прогоні тур-автоплею —
                    // Layout/Repaint-подія інколи не приходить жодного разу
                    // за N кадрів Update(), тож «Нічна розмова»/рада Захара/
                    // Максимів квест мовчки пропускались (QA: той самий build
                    // 0a5cfe0, той самий сід — 0/3 незалежних прогони бачать
                    // цей контент проти 2/2 авторських). Водій тепер кличе ТУ
                    // САМУ маршрутизацію напряму й синхронно — жодного
                    // очікування кадру, результат не залежить від того, чи
                    // взагалі відбувся хоч один OnGUI-прохід.
                    _shell.RouteOfferedSceneContentIfAvailable();
                    if (Session.State != SessionState.Evening) continue; // маршрутизація підхопила сценарний зміст — далі йде він

                    if (!_eveningShown)
                    {
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
                        // Гафіїна й Максимова лінії квесту ділять ОДИН
                        // GameSession._currentQuestOffer (див. той самий фікс
                        // у HubScreen.DrawQuestOffer) — перезапит ЦІЄЇ лінії
                        // просто ПЕРЕД ResolveQuestChoice синхронізує вказівник
                        // назад на неї, бо нижче ми так само запитуємо Максимову.
                        Run(() =>
                        {
                            Session.OfferQuestStage(DefaultQuests.HafiyaId);
                            Session.ResolveQuestChoice(chosen);
                        });
                    }

                    // Максимова квестова глава арки «Не за кров» (Поправка
                    // №7.8, п.4): GameShell.RouteOfferedSceneContentIfAvailable
                    // вище вже зареєстрував визначення в пулі
                    // (BeginArcChapterQuest), коли главу відкрито (гейт
                    // Steady) — тут лише доганяємо тим самим OfferQuestStage,
                    // яким і Гафіїн квест вище.
                    var maksymQuest = Run(() => Session.OfferQuestStage(DefaultQuests.MaksymCh1Id));
                    if (maksymQuest != null && _capturedOnce.Add("arc-quest-choice"))
                    {
                        foreach (var f in WaitFrames(FramesShort)) yield return f;
                        _host.Capture("arc-quest-choice");
                        yield return 0;
                    }
                    if (maksymQuest?.Options != null && maksymQuest.Options.Count > 0)
                    {
                        int idx = 0;
                        for (int i = 0; i < maksymQuest.Options.Count; i++)
                            if (maksymQuest.Options[i].HasCandidate) { idx = i; break; }
                        int chosen = idx;
                        Run(() =>
                        {
                            Session.OfferQuestStage(DefaultQuests.MaksymCh1Id);
                            Session.ResolveQuestChoice(chosen);
                        });
                    }

                    // Той самий захист, що на вході блоку: якщо котрийсь із
                    // yield'ів вище (наприклад, знімок пропозиції квесту) дав
                    // маршрутизації підхопити ЩОЙНО відкриту сцену, стан уже
                    // не Evening — SetPatrol/ConfirmEvening тут були б
                    // Evening-лише командою на чужому стані.
                    if (Session.State != SessionState.Evening) continue;

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

        /// <summary>
        /// -autoplay-journal (ціль 1): один прогін, тими самими командами, що
        /// й кнопки реальних екранів (той самий принцип, що <see cref="Run"/>
        /// вище), доки <c>GameSession.GetMechanicsJournal()</c> не покаже всі
        /// 44 записи побаченими, або тур не впреться в <see cref="JournalDayCap"/>.
        /// Сценарій — той самий, що вже доведений у Core
        /// (MechanicsJournalCompletionTests.JournalPlayer_OnePlaythrough_
        /// SeesAllFortyFourEntries): доби 1–3 ведуться вручну (кровавий вузол
        /// 1 наївною тактикою до Base/Worst без смерті Максима → «Нічна
        /// розмова» доби 3 → «Звинуватити»), з доби 4 — та сама «добра
        /// економіка», що й довгий тур/боти (розстановка, рада, вилазка/данж,
        /// патруль через парність), плюс гір/крафт/тренувальний бій/
        /// збереження при першій нагоді.
        /// </summary>
        public IEnumerator<int> RunJournal()
        {
            // ---------------- Титул -> Нова гра (детермінований поріг) ----------------
            foreach (var f in WaitFrames(FramesMedium)) yield return f;
            _host.Capture("title");
            yield return 0;

            Run(() => Session.NewGame(new NewGameOptions
            {
                HitRule = HitRuleKind.Threshold,
                Seed = 1,
                Roller = _shell.Roller,
                SkipCreation = false
            }));
            _host.Log("Журнальний тур: Нова гра (поріг — перевірки детерміновані, інваріант 8).");

            // ---------------- Створення протагоніста ----------------
            // Учениця знахарки (Healer), НЕ Warrior: наївна тактика бою вузла 1
            // нижче рахує саме на слабшого в бою протагоніста — з бійцем той
            // самий наївний бій вигравається чисто (Good), і полоса Base/Worst
            // (потрібна для betrayal_confrontation/defection/roster_drama) не
            // настає ніколи (те саме емпіричне спостереження, що вже задокумен-
            // товане в MechanicsJournalCompletionTests).
            foreach (var f in WaitFrames(FramesShort)) yield return f;
            _host.Capture("creation-default");
            yield return 0;

            Run(() => Session.SetProtagonistName("Ярина"));
            Run(() => Session.SetProtagonistGender(Gender.Female));
            _shell.ProtagonistGender = Gender.Female;
            Run(() => Session.SetProtagonistBackground(Backgrounds.Healer().Id));

            foreach (var f in WaitFrames(FramesShort)) yield return f;
            _host.Capture("creation-filled");
            yield return 0;

            Run(() => Session.ConfirmCreation());
            _host.Log("Створення підтверджено: Ярина, жіночий рід, передісторія healer.");

            // ---------------- Головний диспетчер станів ----------------
            int guard = 0;
            while (true)
            {
                guard++;
                if (guard > JournalMaxLoopSteps)
                    throw new InvalidOperationException("Журнальний тур: запобіжник maxSteps (" + JournalMaxLoopSteps +
                        ") — застряг у стані " + Session.State + " (можливе зациклення диспетчера).");

                var state = Session.State;

                // Скріншот "journal-<id>" рівно раз на кожен запис, у момент,
                // коли він щойно став Seen — незалежно від того, у якому стані
                // диспетчер саме зараз (сигнали/лічильники приходять конвеєром
                // дня, не лише в Decision/Evening).
                foreach (var f in ScanJournalDeltas()) yield return f;

                bool checkpoint = state == SessionState.Morning || state == SessionState.FreePlay;
                if (checkpoint && AllJournalSeen())
                {
                    _host.Log("Журнал: усі 44 записи побачено на добу " + Session.CurrentView.Day + " — тур завершено успішно.");
                    break;
                }
                if (checkpoint && Session.CurrentView.Day > JournalDayCap)
                {
                    _host.Log("Журнальний тур упирається в стелю доби " + JournalDayCap + " із непобаченими записами.");
                    break;
                }

                // ---- портретна сцена (та сама механіка, що й Run() вище) ----
                if (state == SessionState.Scene)
                {
                    var scene = _shell.Scene;
                    var step = scene.DriverAdvance(_shell);
                    LogIfMessage();
                    foreach (var f in WaitFrames(FramesShort)) yield return f;

                    if (step != null && step.IsChoice)
                    {
                        scene.DriverChoose(_shell, ChooseJournalSceneOptionIndex(step));
                        LogIfMessage();
                        foreach (var f in WaitFrames(FramesShort)) yield return f;

                        if (scene.IsShowingConsequence)
                        {
                            scene.DriverContinueConsequence();
                            foreach (var f in WaitFrames(FramesShort)) yield return f;
                        }
                    }
                    continue;
                }

                // ---- ранок / вільна гра ----
                if (state == SessionState.Morning || state == SessionState.FreePlay)
                {
                    int day = Session.CurrentView.Day;
                    if (_jLastMorningDay != day)
                    {
                        _jLastMorningDay = day;

                        var roster = Session.GetRosterView();
                        var assignments = BotSupport.DefaultAssignments(roster);
                        foreach (var kv in assignments)
                        {
                            string companionId = kv.Key;
                            string slotId = kv.Value;
                            Run(() => Session.Assign(companionId, slotId));
                        }

                        // §CLAUDE.md "Приймання клапана": стройка/рада/крафт
                        // лише з доби 2 — доба 1 сама по собі веде лише до
                        // вузла 1 (нижче), як і в ручному сценарії Core-теста.
                        if (day >= 2)
                        {
                            JournalCouncilRoutine(day);
                            JournalOpportunisticGearAndTraining(day);
                        }
                        if (day >= 4) JournalMaybeDepartExpedition(day);
                    }

                    if (Session.State == SessionState.Morning || Session.State == SessionState.FreePlay)
                        Run(() => UI.HubScreen.StartDay(Session));
                    continue;
                }

                // ---- день (конвеєр) ----
                if (state == SessionState.Day)
                {
                    Run(() => UI.HubScreen.StartDay(Session));
                    continue;
                }

                // ---- точка рішення ----
                if (state == SessionState.Decision)
                {
                    var offer = Session.GetPendingOffer();
                    var path = !_jBattleShown ? BloodyIfPossible(offer) : StewardPath(offer);
                    Run(() => Session.ResolveIncident(path));
                    continue;
                }

                // ---- бій: вузол 1 — ПОКРОКОВО наївною тактикою обох сторін до кінця; решта — «Автобій» ----
                if (state == SessionState.Battle)
                {
                    foreach (var f in WaitFrames(FramesBattleEnter)) yield return f;

                    if (!_jBattleShown)
                    {
                        _jBattleShown = true;
                        foreach (var f in PlayJournalBattleNaively()) yield return f;
                    }
                    else if (Session.State == SessionState.Battle)
                    {
                        // Секундарні бої журнального туру — тим самим
                        // контрактом, що звичайний тур: жодного
                        // Session.CombatAutoResolve() напряму поза
                        // PlayJournalBattleNaively (охоронець
                        // AutoplayDriverGuardTests.AutoplayDriver_CallsCombatCoreOnlyInsideNaiveJournalBattle).
                        var hud = _shell.BattlePresenter as IBattleHudData;
                        if (hud == null)
                            throw new InvalidOperationException(
                                "-autoplay-journal: BattlePresenter відсутній або не реалізує IBattleHudData — 3D-презентер бою не знайдено (docs/COMBAT_V2.md §7.4).");
                        hud.RequestAutoResolve();
                        foreach (var f in WaitFrames(FramesMedium)) yield return f;
                    }

                    foreach (var f in WaitFrames(FramesShort)) yield return f;
                    AcknowledgeBattleIfPending();
                    foreach (var f in WaitFrames(FramesShort)) yield return f;
                    continue;
                }

                // ---- данж ----
                if (state == SessionState.Dungeon)
                {
                    var view = Session.GetDungeonView();
                    if (view == null) { continue; }

                    if (view.CurrentRoom != null)
                    {
                        var room = view.CurrentRoom;
                        if (room.Type == "Combat")
                        {
                            if (room.HasQuietBypass) Run(() => Session.ResolveDungeonRoom(IncidentPath.Quiet));
                            else Run(() => Session.ResolveDungeonRoom(IncidentPath.Bloody));
                        }
                        else if (room.Type == "Event")
                        {
                            Run(() => Session.ResolveDungeonEvent(0));
                        }
                        else
                        {
                            Run(() => Session.ResolveDungeonRoom(IncidentPath.Quiet));
                        }
                    }
                    else if (view.RoomsCleared < 3)
                    {
                        Run(() => Session.PushDeeper());
                    }
                    else
                    {
                        Run(() => Session.ExtractDungeon());
                    }
                    continue;
                }

                // ---- вечір ----
                if (state == SessionState.Evening)
                {
                    // Особисті арки, «Нічна розмова»/тиха перевірка Мирослави
                    // доба 3, рада Захара доба 5 — той самий гачок, що й
                    // реальний GameShell.OnGUI кличе щокадру для людини.
                    _shell.RouteOfferedSceneContentIfAvailable();
                    if (Session.State != SessionState.Evening) continue;

                    JournalAdvanceQuest(DefaultQuests.HafiyaId);
                    JournalAdvanceQuest(DefaultQuests.MaksymCh1Id);

                    if (Session.State != SessionState.Evening) continue;

                    Run(() => Session.SetPatrol(Session.CurrentView.Day % 2 == 0));
                    Run(() => Session.ConfirmEvening());
                    continue;
                }

                // ---- ніч (патруль / криза доби 5 / фінал) ----
                if (state == SessionState.Night)
                {
                    int day = Session.CurrentView.Day;

                    if (day == 5 && !_jCrisisFinaleAttempted)
                    {
                        _jCrisisFinaleAttempted = true;
                        Run(() => Session.ReactToCrisis(CrisisReaction.SpendGold));
                        // Кроваво — навмисно (ціль 1: "фінальний бій" мусить
                        // трапитись, як і в -autoplay/-autoplay-long вище).
                        Run(() => Session.ResolveFinale(IncidentPath.Bloody));
                        continue;
                    }

                    Run(() => Session.AdvanceNight());
                    continue;
                }

                // ---- підсумок доби 5 ----
                if (state == SessionState.Summary)
                {
                    Run(() => Session.AcknowledgeSummary());
                    continue;
                }

                throw new InvalidOperationException("Журнальний тур не знає, як показати стан " + state + " — диспетчер не покриває його.");
            }

            // ---- журнал наостанок: вкладка «Журнал механік» + підсумок у лог ----
            _shell.SetHubTab(10);
            foreach (var f in WaitFrames(FramesShort)) yield return f;
            _host.Capture("journal-final");
            yield return 0;
            _shell.SetHubTab(0);

            var finalJournal = Session.GetMechanicsJournal();
            JournalTotalCount = finalJournal != null ? finalJournal.Count : 0;
            int seenCount = 0;
            var stillUnseen = new List<string>();
            if (finalJournal != null)
                foreach (var e in finalJournal)
                {
                    if (e.Seen) seenCount++;
                    else stillUnseen.Add(e.Id);
                }
            JournalSeenCount = seenCount;

            _host.Log("Журнал механік: побачено " + JournalSeenCount + "/" + JournalTotalCount + ".");
            if (stillUnseen.Count > 0)
                _host.Log("Непобачені: " + string.Join(", ", stillUnseen));
        }

        /// <summary>Нові Seen-записи журналу з останнього виклику — знімок "journal-&lt;id&gt;" рівно раз на запис, незалежно від поточного стану диспетчера.</summary>
        private IEnumerable<int> ScanJournalDeltas()
        {
            var journal = Session.GetMechanicsJournal();
            if (journal == null) yield break;

            foreach (var entry in journal)
            {
                if (!entry.Seen) continue;
                if (!_jSeenIds.Add(entry.Id)) continue;

                foreach (var f in WaitFrames(FramesShort)) yield return f;
                _host.Capture("journal-" + entry.Id);
                yield return 0;
                _host.Log("Журнал: уперше побачено «" + entry.Id + "».");
            }
        }

        private bool AllJournalSeen()
        {
            var journal = Session.GetMechanicsJournal();
            if (journal == null) return false;
            foreach (var e in journal)
                if (!e.Seen) return false;
            return true;
        }

        /// <summary>
        /// Вибір варіанту сцени журнального туру: конфронтація Мирослави доби 3
        /// (<see cref="CompanionScenes.MyroslavaConfrontationChoiceId"/>) свідомо
        /// бере «звинуватити» (перевірка Залякування) — той самий вибір, що
        /// в Core-тесті, єдиний, що доводить defection/roster_drama/
        /// betrayal_confrontation за один прогін. Решта сцен — перший
        /// доступний варіант (той самий "обережний за замовчуванням" норов,
        /// що <see cref="BotSupport.ChooseSceneDefault"/>).
        /// </summary>
        private static int ChooseJournalSceneOptionIndex(SceneStepView step)
        {
            int count = step?.Options != null ? step.Options.Count : 0;
            if (count == 0) return 0;

            if (step.ChoiceId == CompanionScenes.MyroslavaConfrontationChoiceId)
                for (int i = 0; i < step.Options.Count; i++)
                    if (step.Options[i].SkillKey == SkillKeys.Intimidate.Id) return i;

            return BotSupport.ChooseSceneDefault(step);
        }

        /// <summary>
        /// Бій вузла 1 ПОКРОКОВО, наївною тактикою обох сторін ("йди до
        /// найближчого і бий", та сама команда, якою грають усі бот-політики
        /// звичайний бій — не «розумний» автобій): без цього наївного шляху
        /// вузол 1 вигравається чисто (Good), і полоса Base/Worst, потрібна
        /// для defection/betrayal_confrontation/roster_drama, не настає
        /// ніколи (емпірично, MechanicsJournalCompletionTests).
        ///
        /// ЄДИНИЙ дозволений виняток із контракту "автотур ходить у бій лише
        /// через IBattleInput" (docs/COMBAT_V2.md §7.4): цей сценарій навмисно
        /// грає ОБИДВІ сторони наївно (кличе Session.Combat* напряму) і тому
        /// сам вимикає ШІ презентера (<see cref="IBattleInput.EnemyAiEnabled"/>
        /// дорівнює false) на час свого циклу — інакше презентер намагався б
        /// вести той самий ворожий хід одночасно з водієм. Охоронці
        /// AutoplayDriverGuardTests.AutoplayDriver_CallsCombatCoreOnlyInsideNaiveJournalBattle/
        /// AutoplayDriver_SetsEnemyAiEnabledFalseOnlyInsideNaiveJournalBattle
        /// звіряють, що і прямі виклики Session.Combat*, і вимкнення
        /// EnemyAiEnabled лишаються РІВНО в цьому методі.
        /// </summary>
        private IEnumerable<int> PlayJournalBattleNaively()
        {
            var input = _shell.BattlePresenter as IBattleInput;
            if (input != null) input.EnemyAiEnabled = false;
            try
            {
                int guard = 0;
                while (Session.State == SessionState.Battle && guard++ < 500)
                {
                    var view = Session.GetBattleView();
                    var current = BotSupport.FindCurrent(view);
                    var target = current != null ? BotSupport.FindNearestOpposite(view, current) : null;

                    if (target == null)
                    {
                        Run(() => Session.CombatEndTurn());
                    }
                    else
                    {
                        string targetId = target.Id;
                        var attackResult = Run(() => Session.CombatAttack(targetId));
                        if (attackResult != CombatActionResult.Success)
                        {
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
                    }

                    yield return 0; // один кадр на хід — довгий бій не має блокувати кадр Unity.
                }
            }
            finally
            {
                if (input != null) input.EnemyAiEnabled = true;
            }
        }

        // ---- ранок журнального туру: рада / гір / крафт / тренування / збереження ----

        private void JournalCouncilRoutine(int day)
        {
            var city = Session.GetCityView();

            foreach (var id in JournalBuildPriority)
            {
                if (IsBuiltOrBuilding(city, id)) continue;
                string buildingId = id;
                Run(() => Session.OrderBuilding(buildingId));
                break; // одна стройка за ранок, як і в бот-водіях (BotRunner.ApplyCouncilRoutine).
            }

            if (city != null && city.RaidReady) Run(() => Session.OrderRaid());
            if (city != null && city.SettlersReady) Run(() => Session.OrderSettlers());

            if (day % 3 == 0)
            {
                Run(() => Session.OrderPrepareThreat());
                Run(() => Session.OrderOutfitExpedition("outskirts"));
            }
            if (day % 4 == 0) Run(() => Session.OrderDecree("tuhar_boyars"));
            if (day % 5 == 0) Run(() => Session.OrderDiplomacy("tuhar_boyars"));
        }

        private static bool IsBuiltOrBuilding(CityView city, string id)
        {
            if (city?.Built != null)
                foreach (var b in city.Built) if (b.Id == id) return true;
            if (city?.InProgress != null)
                foreach (var b in city.InProgress) if (b.Id == id) return true;
            return false;
        }

        /// <summary>Гір/крафт при першій нагоді (equip/craft журналу), тренувальний бій і збереження — рівно один раз кожне за тур, тими самими командами, що кнопки вкладок «Спорядження»/«Готовність»/«Збереження».</summary>
        private void JournalOpportunisticGearAndTraining(int day)
        {
            if (!_jEquipped || !_jCrafted)
            {
                var stash = Session.GetStash();
                if (stash != null && stash.Count > 0)
                {
                    if (!_jEquipped)
                    {
                        var roster = Session.GetRosterView();
                        foreach (var item in stash)
                        {
                            string instanceId = item.InstanceId;
                            var slot = item.Slot;
                            bool done = false;
                            if (roster?.Companions != null)
                                foreach (var c in roster.Companions)
                                {
                                    string companionId = c.Id;
                                    bool ok = Run(() => Session.Equip(companionId, instanceId, slot));
                                    if (ok) { done = true; break; }
                                }
                            if (done) { _jEquipped = true; break; }
                        }
                    }

                    if (!_jCrafted && IsBuiltOrBuilt(city: Session.GetCityView(), id: DefaultBuildings.Workshop))
                    {
                        foreach (var item in Session.GetStash())
                        {
                            string instanceId = item.InstanceId;
                            var result = Run(() => Session.CraftUpgrade(instanceId));
                            if (result == Game.Core.Items.CraftResult.Success) { _jCrafted = true; break; }
                        }
                    }
                }
            }

            if (!_jTrained)
            {
                _jTrained = true; // ставимо ДО виклику: NewTrainingBattle сама лишає партію в тій самій фазі (SuspendReason.TrainingSkirmish), повторний виклик того самого ранку — без потреби.
                Run(() => Session.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold }));
            }

            if (!_jSaved)
            {
                const int slot = 0;
                string blob = Run(() => Session.SaveState(slot));
                if (blob != null)
                {
                    var view = Session.CurrentView;
                    SaveFileStore.Write(slot, blob, view.TensionBand, view.Day);
                    _jSaved = true;
                }
            }
        }

        private static bool IsBuiltOrBuilt(CityView city, string id)
        {
            if (city?.Built == null) return false;
            foreach (var b in city.Built) if (b.Id == id) return true;
            return false;
        }

        /// <summary>
        /// Захар — єдиний здоровий "не-польовий" (не протагоніст/Максим/
        /// Мирослава) кандидат у полі з доби 4: Мирослава дефектила доба 3
        /// (ExpeditionRunner.Depart сам відмовляє Antagonist), Максим
        /// поранений тим самим кривавим вузлом 1 (відмовляє IsInjured) —
        /// той самий склад, що в MechanicsJournalCompletionTests. Чергуємо
        /// звичайну вилазку (парні доби, дає loot.dropped на поверненні) і
        /// данж-делве (непарні, dungeon_delve/scars/loot іменного предмета) —
        /// без парного дня "craft" фізично недосяжний (дженерик-лут капає
        /// лише зі звичайної вилазки).
        /// </summary>
        private void JournalMaybeDepartExpedition(int day)
        {
            var roster = Session.GetRosterView();
            if (roster?.Companions != null)
                foreach (var c in roster.Companions)
                    if ((c.Id == GameSession.ProtagonistId || c.Id == "zakhar") &&
                        c.Status == Game.Core.Characters.CompanionStatus.OnMission)
                        return; // партія вже в полі — не відправляємо другий відряд.

            // Чергування «через раз», а не за парністю доби: звичайна вилазка
            // триває парну кількість діб, і з парністю загін щоразу повертався
            // в парну добу — до підземелля черга не доходила ніколи (журнальний
            // тур 25.09.2026: 42/44, без dungeon_delve).
            bool delve = _jNextDelve;
            string siteId = delve ? DefaultDungeon.AbandonedCamp : "outskirts";
            var approach = delve ? ExpeditionApproach.Delve : ExpeditionApproach.Quiet;
            var party = new List<string> { GameSession.ProtagonistId, "zakhar" };

            var preview = Run(() => Session.PreviewExpedition(siteId, approach, party));
            if (preview == null) return;
            int days = preview.Days;
            Run(() => Session.DepartExpedition(siteId, approach, party, days));
            _jNextDelve = !delve;
        }

        /// <summary>Наступне підземелля в журнальному турі: чергується з вилазкою після кожного виходу.</summary>
        private bool _jNextDelve = true;

        /// <summary>
        /// Журнальний тур: крок квесту тією самою дією, що й вечірня панель
        /// (NightScreen): на етапі-виборі — перший варіант, доступний кандидату;
        /// на етапі без варіантів (перевірка чи підсумок) — кнопка
        /// «Спробувати»/«Підтвердити», тобто OfferQuestStage + ResolveQuestChoice(0).
        /// Без другого квест зупинявся на перевірці назавжди, і глава арки
        /// Максима не завершувалась (журнальний тур 25.09.2026: без arc_chapter).
        /// </summary>
        private void JournalAdvanceQuest(string questId)
        {
            var offer = Run(() => Session.OfferQuestStage(questId));
            if (offer == null) return;
            int chosen = 0;
            if (offer.Options != null && offer.Options.Count > 0)
            {
                bool any = false;
                for (int i = 0; i < offer.Options.Count; i++)
                    if (offer.Options[i].HasCandidate) { chosen = i; any = true; break; }
                if (!any) return; // жоден варіант недоступний — кнопки неактивні, як і в людини
            }
            Run(() =>
            {
                Session.OfferQuestStage(questId);
                Session.ResolveQuestChoice(chosen);
            });
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

        /// <summary>
        /// Прогулянка селом (власник, 25.09.2026: «бігати як у CRPG») — тими
        /// самими діями, що й людина: «Прогулянка» у шапці, дійти до місця
        /// (запит шляху — той самий, що клік мишею), «E — зайти». Тур падає
        /// винятком (код виходу 2), якщо герой не дійшов або «зайти» відкрило
        /// не ту вкладку.
        /// </summary>
        private IEnumerable<int> ExploreTour()
        {
            _shell.SetExploring(true);
            if (!_shell.Exploring)
                throw new InvalidOperationException("Прогулянка: недоступна в стані " + Session.State + ".");
            foreach (var f in WaitFrames(45)) yield return f; // камера наздоганяє героя
            _host.Capture("explore-start");
            yield return 0;

            string[] targets = { "post:scouting_post", Walk.VillagePlaces.TrainingGroundId, Walk.VillagePlaces.NoticeBoardId };
            foreach (var target in targets)
            {
                _shell.RequestWalkTo(target);
                int frames = 0;
                while ((_shell.NearbyPlace == null || _shell.NearbyPlace.Id != target) && frames < ExploreWalkFrameCap)
                {
                    frames++;
                    yield return 0;
                }
                if (_shell.NearbyPlace == null || _shell.NearbyPlace.Id != target)
                    throw new InvalidOperationException("Прогулянка: герой не дійшов до «" + target + "» за " + ExploreWalkFrameCap +
                                                        " кадрів; " + _shell.WalkDebug + ".");
                foreach (var f in WaitFrames(20)) yield return f;
                _host.Capture("explore-" + target.Replace(':', '-'));
                yield return 0;
                _host.Log("Прогулянка: дійшов до «" + target + "» за " + frames + " кадрів.");
            }

            int expectedTab = _shell.NearbyPlace.HubTab;
            _shell.InteractNearby();
            if (_shell.Exploring || _shell.HubTab != expectedTab)
                throw new InvalidOperationException("Прогулянка: «E — зайти» мало відкрити вкладку " + expectedTab +
                                                    ", а відкрито " + _shell.HubTab + " (прогулянка " + (_shell.Exploring ? "триває" : "закінчилась") + ").");
            foreach (var f in WaitFrames(30)) yield return f; // камера повертається
            _host.Capture("explore-entered");
            yield return 0;
            _shell.SetHubTab(0);
            _host.Log("Прогулянка: три місця, «зайти» відкрило вкладку " + expectedTab + ".");
        }

        private const int ExploreWalkFrameCap = 1800;

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

        /// <summary>
        /// Поправка №7.8, п.4: два тури мають обирати РІЗНЕ, інакше знімки
        /// обох прогонів показують той самий вибір двічі. Автори сцен
        /// послідовно кладуть найризикованіший/найкровавіший варіант ОСТАННІМ
        /// у списку (Захарова рада: "загата"/"assault" — тихий/кровавий;
        /// Мирославина довіра: "довіритись"/…/"відіслати") — тур із форсованим
        /// кровавим шляхом (<see cref="_useThresholdRule"/>) тримається його,
        /// другий лишає перший (той самий "перший варіант" за замовчуванням,
        /// що й <see cref="BotSupport.ChooseSceneDefault"/>).
        /// </summary>
        private int ChooseSceneOptionIndex(SceneStepView step)
        {
            int count = step?.Options != null ? step.Options.Count : 0;
            if (count == 0) return 0;
            return _useThresholdRule ? count - 1 : BotSupport.ChooseSceneDefault(step);
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

        // ===================== -autoplay-long: сигнали великого бунту =====================

        /// <summary>
        /// Поправка №7, <c>-autoplay-long</c>: перечитує НОВІ (ще не бачені)
        /// рядки <see cref="GameSession.DayLog"/> і знімає рівно по одному
        /// скріншоту на кожен зсув смуги Напруги, кожен щабель передвісника
        /// природної кризи (<c>SubjectId=="crisis"</c>) і саму розв'язку
        /// бунту на площі — той самий "не повторюй" прийом, що
        /// <see cref="_capturedOnce"/> для choice/consequence вище.
        /// </summary>
        private IEnumerable<int> ScanForGreatCrisisSignals()
        {
            var log = Session.DayLog;
            if (_dayLogScanIndex > log.Count ||
                (_dayLogScanIndex > 0 && !ReferenceEquals(log[_dayLogScanIndex - 1], _lastScannedEvent)))
                _dayLogScanIndex = 0;

            for (; _dayLogScanIndex < log.Count; _dayLogScanIndex++)
            {
                _lastScannedEvent = log[_dayLogScanIndex];
                string slug = LongTourSlugFor(log[_dayLogScanIndex]);
                if (slug == null) continue;
                if (!_capturedOnce.Add(slug)) continue;

                foreach (var f in WaitFrames(FramesShort)) yield return f;
                _host.Capture(slug);
                yield return 0;

                if (slug == "crisis-riot-outcome") _greatCrisisResolved = true;
            }
        }

        /// <summary>
        /// Ім'я знімка для рядка DayLog, вартого окремого кадру довгого туру,
        /// або null — решта рядків тур не знімає (не захаращуємо Screenshots/
        /// повторами доповідей з постів тощо). Три випадки:
        /// "tension.band.&lt;Band&gt;" (SignalComposer, GameSession.TranslateReport) —
        /// зсув смуги настрою міста; "forewarn.levelN" з <c>subject=="crisis"</c>
        /// в Args (GameSession.TranslateReport кладе SubjectId окремим
        /// аргументом — Key лишається спільним "forewarn.levelN" для всіх
        /// джерел, тож розрізняти джерело треба саме за Args, не за Key) —
        /// щабель передвісника природної кризи (площа), не вулиці/ночі/
        /// Тугара; "incident.crisis_riot.&lt;Band&gt;" (SignalComposer:
        /// TopicId+"."+Band) — сама розв'язка бунту.
        /// </summary>
        private static string LongTourSlugFor(GameEvent evt)
        {
            if (evt?.Key == null) return null;

            if (evt.Key.StartsWith("tension.band.", StringComparison.Ordinal) &&
                !evt.Key.StartsWith("tension.band.label", StringComparison.Ordinal) &&
                !string.Equals(evt.Key, "tension.band.risen", StringComparison.Ordinal))
                return "band-" + evt.Key.Substring("tension.band.".Length).ToLowerInvariant();

            if (evt.Key.StartsWith("forewarn.level", StringComparison.Ordinal) &&
                evt.Args != null && evt.Args.TryGetValue("subject", out var subject) &&
                string.Equals(subject, "crisis", StringComparison.Ordinal))
                return "crisis-forewarn-" + evt.Key.Substring("forewarn.".Length);

            if (evt.Key.StartsWith("incident.crisis_riot.", StringComparison.Ordinal))
                return "crisis-riot-outcome";

            return null;
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

        // ===================== бій: хід гравця (лише IBattleInput, §7.4) =====================

        /// <summary>
        /// Один ручний крок гравця у звичайному турі (<c>-autoplay</c>) —
        /// ЛИШЕ через <see cref="IBattleInput"/>, як клік миші: наведення на
        /// найближчого ворога, атака якщо влучення можливе, інакше крок
        /// назустріч. Хід ворога сюди не потрапляє: головний диспетчер
        /// викликає цей метод лише тоді, коли <c>view.IsAiTurn</c> вже false
        /// (див. виклик у <see cref="Run"/>).
        /// </summary>
        private void PlayOneBattleStep(IBattleHudData hud, BattleView view)
        {
            var current = BotSupport.FindCurrent(view);
            if (current == null) { hud.RequestEndTurn(); return; }

            var target = BotSupport.FindNearestOpposite(view, current);
            if (target == null) { hud.RequestEndTurn(); return; }

            hud.SimulateHoverUnit(target.Id);
            var preview = hud.HoverAttack;
            bool acted;
            string what;
            if (preview != null && preview.Result == "Success")
            {
                acted = hud.ClickUnit(target.Id);
                what = "атака по " + target.Id;
            }
            else
            {
                var step = BotSupport.StepToward(view, current, target.Pos);
                if (!step.HasValue)
                {
                    hud.ClearSimulatedHover();
                    hud.RequestEndTurn();
                    return;
                }
                acted = hud.ClickTile(step.Value.X, step.Value.Y);
                what = "рух на " + step.Value.X + "," + step.Value.Y;
            }
            hud.ClearSimulatedHover();
            CheckRejection(acted, hud.LastRejectionText, what);
        }

        /// <summary>
        /// Ціль (доручення власника 25.09.2026, PART=autoplay, п.1):
        /// <c>-autoplay-battle</c> — окремий дим-тест ЛИШЕ бою, без решти
        /// партії. Тренувальний бій з титулу (та сама команда, що кнопка
        /// «Тренувальний бій», <c>TitleScreen.cs</c>), далі — виключно
        /// <see cref="IBattleInput"/>/<see cref="IBattleHudData"/>: хід
        /// ворога веде презентер сам (docs/COMBAT_V2.md §5), цей метод лише
        /// чекає й діагностує завис (<see cref="EnemyTurnWatchdogSeconds"/>).
        /// </summary>
        public IEnumerator<int> RunBattleOnly()
        {
            foreach (var f in WaitFrames(FramesMedium)) yield return f;

            var options = new TrainingBattleOptions { HitRule = _useThresholdRule ? HitRuleKind.Threshold : HitRuleKind.Percent };
            Run(() => Session.NewTrainingBattle(options));
            _host.Log("Титул: Тренувальний бій (" + options.HitRule + ") — режим -autoplay-battle.");

            foreach (var f in WaitFrames(FramesBattleEnter)) yield return f;

            if (Session.State != SessionState.Battle)
                throw new InvalidOperationException(
                    "-autoplay-battle: Тренувальний бій не перевів сесію у стан Battle (стан " + Session.State + ").");

            var hud = _shell.BattlePresenter as IBattleHudData;
            if (hud == null)
                throw new InvalidOperationException(
                    "-autoplay-battle: BattlePresenter відсутній або не реалізує IBattleHudData — 3D-презентер бою не знайдено (docs/COMBAT_V2.md §7.4).");

            _host.Capture("battle-start");
            yield return 0;

            int playerActions = 0;
            while (Session.State == SessionState.Battle)
            {
                foreach (var f in WaitWhileBusy(hud)) yield return f;
                if (Session.State != SessionState.Battle) break;

                var view = hud.View;
                if (view == null || view.Outcome != "Ongoing") break;

                if (view.IsAiTurn)
                {
                    foreach (var f in WaitOutEnemyTurn(hud, true)) yield return f;
                    continue;
                }

                if (++playerActions > MaxBattleTourPlayerActions)
                    throw new InvalidOperationException(
                        "-autoplay-battle: стеля " + MaxBattleTourPlayerActions + " дій гравця вичерпана — бій не завершується.");

                foreach (var f in PlayOneBattleTourStep(hud, view)) yield return f;
            }

            if (Session.State == SessionState.Battle)
                throw new InvalidOperationException(
                    "-autoplay-battle: цикл вийшов (Outcome != Ongoing), але Session.State усе ще Battle — можлива діра диспетчера.");

            var presenter = _shell.BattlePresenter;
            foreach (var f in WaitFrames(FramesShort)) yield return f;
            if (presenter != null && presenter.ResultPending)
            {
                _host.Capture("battle-result");
                yield return 0;
            }
            AcknowledgeBattleIfPending();

            _host.Log("Бій пройдено (-autoplay-battle), дій гравця: " + playerActions + ".");
        }

        /// <summary>
        /// Один крок гравця в режимі <c>-autoplay-battle</c>: те саме, що
        /// <see cref="PlayOneBattleStep"/>, але додатково пробує РІВНО раз за
        /// бій здібність і дозор (щоб дим-тест торкнувся й цих гілок
        /// IBattleInput), і знімає кожен вид дії рівно раз (§ завдання п.5).
        /// </summary>
        private IEnumerable<int> PlayOneBattleTourStep(IBattleHudData hud, BattleView view)
        {
            var current = BotSupport.FindCurrent(view);
            if (current == null) { hud.RequestEndTurn(); yield break; }

            var target = BotSupport.FindNearestOpposite(view, current);
            if (target == null) { hud.RequestEndTurn(); yield break; }

            string abilityId;
            if (!_btAbilityUsedOnce && TryArmAbility(hud, current, out abilityId))
            {
                _btAbilityUsedOnce = true;
                hud.SimulateHoverUnit(target.Id);
                bool ok = hud.ClickUnit(target.Id);
                hud.ClearSimulatedHover();
                CheckRejection(ok, hud.LastRejectionText, "здібність " + abilityId + " по " + target.Id);
                // Після відмови озброєна дія лишається (гравець пробує іншу ціль) —
                // тур не пробує, тож знімає її, щоб наступний клік був звичайним.
                if (!ok) hud.CancelArmed();
                if (!_btAbilityShotTaken)
                {
                    _btAbilityShotTaken = true;
                    foreach (var f in WaitFrames(FramesShort)) yield return f;
                    _host.Capture("ability");
                    yield return 0;
                }
                yield break;
            }

            if (!_btTileAbilityUsedOnce && TryUseTileAbility(hud, view, current, out string tileWhat))
            {
                _btTileAbilityUsedOnce = true;
                foreach (var f in WaitFrames(FramesShort)) yield return f;
                _host.Capture("ability-tile");
                yield return 0;
                _host.Log("Здібність з клітинкою: " + tileWhat + ".");
                yield break;
            }

            if (!_btOverwatchUsedOnce)
            {
                _btOverwatchUsedOnce = true;
                hud.ArmOverwatchAim();
                bool ok = hud.ClickTile(target.Pos.X, target.Pos.Y);
                CheckRejection(ok, hud.LastRejectionText, "дозор на " + target.Pos.X + "," + target.Pos.Y);
                if (!_btOverwatchShotTaken)
                {
                    _btOverwatchShotTaken = true;
                    foreach (var f in WaitFrames(FramesShort)) yield return f;
                    _host.Capture("overwatch");
                    yield return 0;
                }
                yield break;
            }

            hud.SimulateHoverUnit(target.Id);
            if (!_btHoverEnemyShotTaken)
            {
                _btHoverEnemyShotTaken = true;
                foreach (var f in WaitFrames(FramesShort)) yield return f;
                _host.Capture("hover-enemy");
                yield return 0;
            }

            var attackPreview = hud.HoverAttack;
            if (attackPreview != null && attackPreview.Result == "Success")
            {
                bool ok = hud.ClickUnit(target.Id);
                hud.ClearSimulatedHover();
                CheckRejection(ok, hud.LastRejectionText, "атака по " + target.Id);
                if (!_btAfterAttackShotTaken)
                {
                    _btAfterAttackShotTaken = true;
                    foreach (var f in WaitFrames(FramesShort)) yield return f;
                    _host.Capture("after-attack");
                    yield return 0;
                }
                yield break;
            }
            hud.ClearSimulatedHover();

            var dest = BotSupport.StepToward(view, current, target.Pos);
            if (!dest.HasValue) { hud.RequestEndTurn(); yield break; }

            hud.SimulateHoverTile(dest.Value.X, dest.Value.Y);
            if (!_btHoverTileShotTaken)
            {
                _btHoverTileShotTaken = true;
                foreach (var f in WaitFrames(FramesShort)) yield return f;
                _host.Capture("hover-tile");
                yield return 0;
            }

            bool moved = hud.ClickTile(dest.Value.X, dest.Value.Y);
            hud.ClearSimulatedHover();
            CheckRejection(moved, hud.LastRejectionText, "рух на " + dest.Value.X + "," + dest.Value.Y);
            if (!_btAfterMoveShotTaken)
            {
                _btAfterMoveShotTaken = true;
                foreach (var f in WaitFrames(FramesShort)) yield return f;
                _host.Capture("after-move");
                yield return 0;
            }
        }

        /// <summary>
        /// Здібність, якій потрібна клітинка, — тими самими кліками, що людина:
        /// «Пастка» — один клік по вільній клітинці в межах дальності; «Наказ
        /// пересунутися» — клік по союзнику, тоді по вільній клітинці поруч із ним.
        /// Відмова з причиною — нормальна (мало ОД, поза дальністю); відмова без
        /// причини — виняток (CheckRejection).
        /// </summary>
        private bool TryUseTileAbility(IBattleHudData hud, BattleView view, BattleUnitView current, out string what)
        {
            what = null;
            if (current?.Abilities == null || view?.ReachableTiles == null) return false;
            foreach (var a in current.Abilities)
            {
                if (a == null || !a.NeedsTargetTile || a.CooldownRemaining > 0 || current.Ap < a.ApCost) continue;

                BattleUnitView ally = null;
                if (a.Targeting != "Tile")
                {
                    foreach (var u in view.Units)
                        if (u != null && u.Id != current.Id && u.Side == current.Side && !u.IsDowned) { ally = u; break; }
                    if (ally == null) continue;
                }

                var anchor = ally != null ? ally.Pos : current.Pos;
                GridPosView? tile = null;
                int best = int.MaxValue;
                foreach (var p in view.ReachableTiles)
                {
                    int toAnchor = Math.Max(Math.Abs(p.X - anchor.X), Math.Abs(p.Y - anchor.Y));
                    int toMe = Math.Max(Math.Abs(p.X - current.Pos.X), Math.Abs(p.Y - current.Pos.Y));
                    if (toAnchor == 0 || toMe > a.Range) continue;
                    if (toAnchor < best) { best = toAnchor; tile = p; }
                }
                if (!tile.HasValue) continue;

                hud.ArmAbility(a.Id);
                if (ally != null)
                {
                    bool picked = hud.ClickUnit(ally.Id);
                    CheckRejection(picked, hud.LastRejectionText, "перша фаза " + a.Id + " (союзник " + ally.Id + ")");
                    if (!picked) { hud.CancelArmed(); what = a.Id + ": союзника відхилено — " + hud.LastRejectionText; return true; }
                    if (hud.ArmedTargetUnitId != ally.Id)
                        throw new InvalidOperationException("Автотур: після кліку по союзнику двофазна здібність " + a.Id + " не запам'ятала ціль.");
                }

                hud.SimulateHoverTile(tile.Value.X, tile.Value.Y);
                bool ok = hud.ClickTile(tile.Value.X, tile.Value.Y);
                hud.ClearSimulatedHover();
                CheckRejection(ok, hud.LastRejectionText, a.Id + " на " + tile.Value.X + "," + tile.Value.Y);
                if (!ok) hud.CancelArmed();
                what = a.Id + (ally != null ? " (союзник " + ally.Id + ")" : string.Empty) + " на " + tile.Value.X + "," + tile.Value.Y
                    + (ok ? " — виконано" : " — відмова: " + hud.LastRejectionText);
                return true;
            }
            return false;
        }

        /// <summary>Перша здібність поточного юніта, що не на відкаті (для одноразового покриття гілки "здібність" — § завдання п.1).</summary>
        private static bool TryArmAbility(IBattleInput input, BattleUnitView current, out string abilityId)
        {
            abilityId = null;
            if (current?.Abilities == null) return false;
            foreach (var a in current.Abilities)
            {
                if (a.CooldownRemaining > 0) continue;
                abilityId = a.Id;
                input.ArmAbility(a.Id);
                return true;
            }
            return false;
        }

        /// <summary>
        /// «Відмова дії без тексту — теж виняток» (§ завдання п.1): ClickTile/
        /// ClickUnit, що повернули false БЕЗ <see cref="IBattleInput.LastRejectionText"/>,
        /// означають діру в HUD-поясненні (докладніше — docs/COMBAT_V2.md §1
        /// "Кожна кнопка... пояснює, чому не працює"), а не легальну відмову.
        /// </summary>
        private void CheckRejection(bool actionSucceeded, string rejectionText, string what)
        {
            if (actionSucceeded) return;
            if (string.IsNullOrEmpty(rejectionText))
                throw new InvalidOperationException(
                    "Автотур: дію відхилено без причини (LastRejectionText порожній) — " + what + ".");
            _host.Log("  (відмова: " + what + " — " + rejectionText + ")");
        }

        /// <summary>Чекає, поки такт показу (<see cref="IBattleInput.IsBusy"/>) не відпустить ввід — із запобіжником <see cref="BusyWatchdogSeconds"/>.</summary>
        private IEnumerable<int> WaitWhileBusy(IBattleInput input)
        {
            float start = Time.realtimeSinceStartup;
            while (input.IsBusy)
            {
                if (Time.realtimeSinceStartup - start > BusyWatchdogSeconds)
                    throw new InvalidOperationException(
                        "Автотур: IsBusy не знімається довше " + BusyWatchdogSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " с — презентер завис на такті показу.");
                yield return 0;
            }
        }

        /// <summary>
        /// Чекає, поки триває хід ворога (<c>view.IsAiTurn</c>) — НІКОЛИ не
        /// кличе <c>GameSession.Combat*</c>/<c>CombatAiStepOneAction</c> сам:
        /// це рівно та відповідальність, яку docs/COMBAT_V2.md §5 віддає
        /// презентеру ("Хід ворога (режисер)"). Перший хід ворога за бій —
        /// на звичайній швидкості (банер/дія знімаються знімком, коли
        /// <paramref name="captureArtifacts"/>), далі — <c>FastEnemyTurns=true</c>.
        /// Падає з діагностикою (поточний юніт/IsAiTurn/IsBusy/EnemyAiEnabled),
        /// якщо хід не завершився за <see cref="EnemyTurnWatchdogSeconds"/> —
        /// рівно той завис, що знайшов власник ("зупинилась і воні нічого не
        /// робили").
        /// </summary>
        private IEnumerable<int> WaitOutEnemyTurn(IBattleHudData hud, bool captureArtifacts)
        {
            float start = Time.realtimeSinceStartup;
            hud.FastEnemyTurns = _btFirstEnemyTurnDone;

            while (Session.State == SessionState.Battle && hud.View != null && hud.View.IsAiTurn)
            {
                if (captureArtifacts && !_btBannerShotTaken && hud.Banner != null && !hud.Banner.PlayerSide)
                {
                    _btBannerShotTaken = true;
                    _host.Capture("enemy-turn-banner");
                    yield return 0;
                }

                if (captureArtifacts && !_btEnemyActionShotTaken && hud.IsBusy)
                {
                    _btEnemyActionShotTaken = true;
                    _host.Capture("enemy-action");
                    yield return 0;
                }

                if (Time.realtimeSinceStartup - start > EnemyTurnWatchdogSeconds)
                {
                    var current = BotSupport.FindCurrent(hud.View);
                    throw new InvalidOperationException(
                        "Автотур: хід ворога не завершився за " +
                        EnemyTurnWatchdogSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) +
                        " с реального часу — той самий завис, що знайшов власник (\"наступив хід опонентів гра тупо зупинилась\"). Діагностика: поточний юніт " +
                        (current != null ? current.Id : "(немає)") + ", View.IsAiTurn=" + hud.View.IsAiTurn +
                        ", IsBusy=" + hud.IsBusy + ", EnemyAiEnabled=" + hud.EnemyAiEnabled + ".");
                }

                yield return 0;
            }

            _btFirstEnemyTurnDone = true;
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
