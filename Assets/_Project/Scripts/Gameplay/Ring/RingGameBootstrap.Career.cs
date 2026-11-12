using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.Stage;
using Game.Gameplay.Ring.UI;
using Game.Gameplay.Ring.UI.Menus;
using Game.Gameplay.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring
{
    /// <summary>
    /// Кар'єра в хості гри (зона «Кар'єра», docs/ring/ZONES.md): дані й тексти кар'єри, сейв (новий, продовжити, записати,
    /// перенесення зі старої теки), контекст екранів і вибір екрана за етапом кар'єри, бот розвитку в огляді. Каркас хоста —
    /// RingGameBootstrap.cs; окремий файл, щоб правки кар'єри не перемішувались зі сценою й боєм.
    /// </summary>
    public sealed partial class RingGameBootstrap
    {
        private const string FeudPath = "Ring/feud_hammer";
        private const string StringsPath = "Ring/strings_uk";
        private const string UiStringsPath = "Ring/ui_uk";
        private const string MovesPath = "Ring/career_moves";
        private const string GimmicksPath = "Ring/career_gimmicks";
        private const string RosterPath = "Ring/career_roster";
        private const string BackstagePath = "Ring/career_backstage";
        private const string BackstageTextPath = "Ring/backstage_uk";

        // ---------------- ICareerHost ----------------

        public int HeroBody { get; set; }
        public int HeroHair { get; set; }
        public WeekActionResult LastWeekResult { get; set; }

        private string SavePath
        {
            get
            {
                // Огляди пишуть свій файл: сейв гравця не чіпають (огляд кар'єри — ring_career_bot, огляд меню — ring_menu_bot).
                string file = _shotsDir != null ? "ring_career_bot.txt" : _menuShotsDir != null ? "ring_menu_bot.txt" : "ring_career.txt";
                return System.IO.Path.Combine(Application.persistentDataPath, file);
            }
        }

        public bool HasSave { get { return _shotsDir == null && RingSaveFile.Exists(SavePath); } }

        /// <summary>
        /// Звідки «Продовжити» читає сейв: огляд із <c>-ring-shot-load &lt;сейв&gt;</c> — заданий файл (QA стартує з потрібного місця
        /// кар'єри), інакше — <see cref="SavePath"/>. Пише огляд усе одно у свій ring_career_bot.txt: заданий сейв не переписується.
        /// </summary>
        private string LoadPath
        {
            get
            {
                ReadCareerShotArgs();
                return _shotLoad ?? SavePath;
            }
        }

        public void NewGame()
        {
            _career = CareerSession.New(_content, new SeededDiceRoller(_seed), _seed);
            _seed += 1;
            HeroBody = 0; HeroHair = 0;
            LastWeekResult = null;
            _career.StartNewGame();
            _onTitle = false;
            Rebind();
        }

        public void ContinueGame()
        {
            try
            {
                CareerSession c = null;
                bool fromBackup;
                var lines = RingSaveFile.Read(LoadPath, l =>
                {
                    var t = CareerSession.New(_content, new SeededDiceRoller(_seed), _seed);
                    t.RestoreState(l[0]);
                    c = t;
                }, out fromBackup);
                if (fromBackup) Debug.LogWarning("[Ринг] основний сейв зіпсований — продовжено з запасної копії (.bak)");
                if (lines.Length > 1)
                {
                    var p = lines[1].Split('|');
                    int b, h;
                    if (p.Length > 1 && int.TryParse(p[0], out b) && int.TryParse(p[1], out h)) { HeroBody = Math.Max(0, Math.Min(b, CareerLooks.HeroBodies.Length - 1)); HeroHair = Math.Max(0, Math.Min(h, CareerLooks.HeroHair.Length - 1)); }
                }
                _career = c;
                SyncHeroSex();
                _onTitle = false;
                LastWeekResult = null;
                Rebind();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Ринг] Сейв не відкрився: " + ex.Message);
            }
        }

        /// <summary>
        /// Дивізіон героя (P2-6, П5): поки екран створення не передає стать у <see cref="CharacterDraft.Female"/>, вона береться з тіла
        /// (body_f_* — жінки). Один раз: після CreateHero (екран одразу кличе Save) і для старих сейвів на «Продовжити». Тиждень 1 букер
        /// перескладає з суперниками свого дивізіону.
        /// </summary>
        private void SyncHeroSex()
        {
            if (_career == null || _career.State.FemaleKnown || _career.Stage == CareerStage.Title || _career.Stage == CareerStage.Create) return;
            int b = Math.Max(0, Math.Min(HeroBody, CareerLooks.HeroBodies.Length - 1));
            _career.SetFemale(CareerLooks.IsFemaleBody(CareerLooks.HeroBodies[b]));
        }

        public void Save()
        {
            if (_career == null || _career.Stage == CareerStage.Title || _career.Stage == CareerStage.Create) return;
            SyncHeroSex();
            try
            {
                RingSaveFile.Write(SavePath, new[] { _career.CaptureState(), HeroBody + "|" + HeroHair });
                InvalidateSaveSummary();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Ринг] Сейв не записався: " + ex.Message);
            }
        }

        /// <summary>
        /// Перенесення сейву (ТЗ B00 §3): з назвою застосунку «Ринг» тека persistentDataPath змінилась
        /// (…/DefaultCompany/Ринг замість …/DefaultCompany/Alpha) — при першому запуску копіюємо старий сейв, якщо нового ще немає.
        /// Старий файл не видаляємо; один запис у лог.
        /// </summary>
        private static void MigrateSave()
        {
            try
            {
                string now = Application.persistentDataPath;
                var company = System.IO.Directory.GetParent(now);
                if (company == null || company.Parent == null) return;
                string oldDir = System.IO.Path.Combine(company.Parent.FullName, "DefaultCompany", "Alpha");
                var seps = new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar };
                if (string.Equals(System.IO.Path.GetFullPath(oldDir).TrimEnd(seps), System.IO.Path.GetFullPath(now).TrimEnd(seps),
                        System.StringComparison.OrdinalIgnoreCase)) return;
                string oldFile = System.IO.Path.Combine(oldDir, "ring_career.txt");
                string newFile = System.IO.Path.Combine(now, "ring_career.txt");
                if (System.IO.File.Exists(newFile) || !System.IO.File.Exists(oldFile)) return;
                System.IO.Directory.CreateDirectory(now);
                System.IO.File.Copy(oldFile, newFile);
                Debug.Log("[Ринг] сейв перенесено зі старої теки: " + oldFile + " → " + newFile);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Ринг] сейв не перенесено: " + e.Message);
            }
        }

        private bool LoadData()
        {
            var feud = Resources.Load<TextAsset>(FeudPath);
            var strings = Resources.Load<TextAsset>(StringsPath);
            var ui = Resources.Load<TextAsset>(UiStringsPath);
            var moves = Resources.Load<TextAsset>(MovesPath);
            var gimmicks = Resources.Load<TextAsset>(GimmicksPath);
            var roster = Resources.Load<TextAsset>(RosterPath);
            var backstage = Resources.Load<TextAsset>(BackstagePath);
            var backstageText = Resources.Load<TextAsset>(BackstageTextPath);
            if (feud == null || strings == null || ui == null || moves == null || gimmicks == null || roster == null || backstage == null || backstageText == null)
            {
                ShowError(new List<string> { "Resources/Ring: немає даних кар'єри" });
                return false;
            }
            try
            {
                _text = RingText.FromJson(strings.text, ui.text, backstageText.text);
                _content = CareerContent.Load(feud.text, moves.text, gimmicks.text, roster.text, backstage.text);
            }
            catch (Exception ex)
            {
                ShowError(new List<string> { ex.Message });
                return false;
            }
            var errors = PromoValidator.Validate(_content.Feud, _text.AsDictionary());
            errors.AddRange(PromoValidator.ValidateScenes(_content.Backstage.Promos(), _text.AsDictionary()));
            if (errors.Count > 0)
            {
                var lines = new List<string>();
                foreach (var e in errors) lines.Add(e.ToString());
                ShowError(lines);
                return false;
            }
            return true;
        }

        /// <summary>Нова чи відновлена кар'єра: контекст екранів, ім'я героя в тексті матчу й промо.</summary>
        private void Rebind()
        {
            _ctx = new RingUiContext
            {
                Campaign = _career.Feud, Text = _text, Career = _career, Host = this,
                Restart = NewGame,
                FinishMatch = () => { _career.FinishMatch(); Save(); },
                StartFeudMatch = () => { _career.StartFeudMatch(); },
                AfterDevelopment = () => { _career.FinishBigShow(); Save(); }
            };
            if (!string.IsNullOrEmpty(_career.State.RingName)) _text.Add("w.player.name", _career.State.RingName);
            // -ring-hero-name: ім'я, яке екран створення підставляє в поле (той самий ключ, що й «Новачок» за замовчуванням)
            ReadCareerShotArgs();
            if (_shotHeroName != null) _text.Add("c.create.default_name", _shotHeroName);
            _view = null;
            _viewKey = null;
            _stageKey = null;
        }

        private object ViewKey()
        {
            if (_onTitle) return "title";
            var st = _career.State;
            switch (_career.Stage)
            {
                case CareerStage.Create: return "create";
                case CareerStage.Week:
                    // Сцена закулісся (розмова з букером чи подія поза рингом) — окремий екран діалогу поверх тижня.
                    return _career.BackstageOpen ? (object)_career.CurrentBackstage : "week:" + st.Season + ":" + st.Week;
                case CareerStage.SeasonEnd: return "season:" + st.Season;
                case CareerStage.Show:
                    switch (st.Phase)
                    {
                        case ShowPhase.Card: return "card:" + st.Season + ":" + st.Week + ":" + st.IsTitle;
                        case ShowPhase.Match: return _career.Engine;
                        case ShowPhase.Result: return "result:" + st.Season + ":" + st.Week + ":" + st.IsTitle;
                        case ShowPhase.Feud:
                            var f = _career.Feud;
                            switch (f.Stage)
                            {
                                case RingStage.Promo: return f.CurrentPromoRunner;
                                case RingStage.Match: return f.CurrentMatch;
                                case RingStage.Overview:
                                case RingStage.MatchPrep: return "feud:" + f.Stage + ":" + f.StepIndex;
                                default: return "feudend:" + f.StepIndex;
                            }
                    }
                    break;
            }
            return "title";
        }

        private RingScreenView MakeView()
        {
            if (_onTitle) return new TitleMenuView(_ctx, this);
            switch (_career.Stage)
            {
                case CareerStage.Create: return new CreateScreenView(_ctx);
                case CareerStage.Week:
                    if (_career.BackstageOpen) return new PromoScreenView(BackstageCtx(), _career.CurrentBackstage);
                    return new WeekScreenView(_ctx);
                case CareerStage.SeasonEnd: return new SeasonEndScreenView(_ctx);
                case CareerStage.Show:
                    switch (_career.State.Phase)
                    {
                        case ShowPhase.Card: return new ShowCardScreenView(_ctx, StartShow);
                        case ShowPhase.Match: return new MatchScreenView(_ctx, _career.Engine);
                        case ShowPhase.Result: return new ShowResultScreenView(_ctx);
                        case ShowPhase.Feud:
                            var f = _career.Feud;
                            switch (f.Stage)
                            {
                                case RingStage.Promo: return new PromoScreenView(_ctx, f.CurrentPromoRunner);
                                case RingStage.Match: return new MatchScreenView(_ctx, f.CurrentMatch);
                                case RingStage.Overview:
                                case RingStage.MatchPrep: return new FeudScreenView(_ctx);
                                default: return new EndingScreenView(_ctx);
                            }
                    }
                    break;
            }
            return new TitleMenuView(_ctx, this);
        }

        /// <summary>Контекст екрана сцени закулісся: той самий діалог, що й промо, але кубик і кінець — кар'єри.</summary>
        private RingUiContext BackstageCtx()
        {
            return new RingUiContext
            {
                Campaign = _ctx.Campaign, Text = _ctx.Text, Career = _ctx.Career, Host = this, Restart = _ctx.Restart,
                PromoDice = _career.Dice,
                FinishPromo = () => { LastWeekResult = _career.FinishBackstage(); Save(); }
            };
        }

        private void StartShow()
        {
            Save();
            _career.StartShow();
        }

        private bool _shotGrow;

        /// <summary>Одна покупка розвитку за крок (видно в кадрі): перший доступний прийом, атрибут, перк — порядок CareerBot.</summary>
        private bool BotGrow()
        {
            foreach (var m in _career.Content.Moves.All) if (_career.LearnMove(m.Id)) { ShotNote("розвиток: прийом " + m.Id); return true; }
            foreach (RingAttr a in new[] { RingAttr.Strength, RingAttr.Agility, RingAttr.Technique, RingAttr.Stamina, RingAttr.Charisma })
                if (_career.BuyAttr(a)) { ShotNote("розвиток: атрибут " + a); return true; }
            foreach (var pk in PerkCatalog.All) if (_career.BuyPerk(pk.Id)) { ShotNote("розвиток: перк " + pk.Id); return true; }
            return false;
        }

        /// <summary>
        /// Огляд перків з дією (06.11.2026): герой огляду має «Імпровізацію» і «Підкупленого суддю», щоб виноска з назвою перка
        /// потрапила в кадр. Лише сейв бота (ring_career_bot.txt), гравця не торкається.
        /// </summary>
        private void ReviewPerks()
        {
            if (_career.Stage == CareerStage.Week && _career.Feud != null && !_career.Feud.State.Perks.Contains(PerkCatalog.Improvise))
            { _career.Feud.State.Perks.Add(PerkCatalog.Improvise); _career.Feud.State.Perks.Add(PerkCatalog.BribedRef); }
        }

        /// <summary>
        /// Крок бота огляду на тижні: спершу розвиток (<c>-ring-shot-grow</c>, одна покупка за крок), далі всі дії тижня різними
        /// цифрами; потім каркас тисне «Далі». true — крок зроблено; <paramref name="grew"/> — це була покупка розвитку (тоді черга
        /// «цифра / Далі» не рухається, як і до розподілу).
        /// </summary>
        private bool BotWeekStep(out bool grew)
        {
            grew = false;
            ReadCareerShotArgs();
            BotForceScenes();
            if (BotLoadStep() || BotEndingStep() || BotDeclineTitleStep() || BotOfferStep()) { grew = true; return true; }
            BotForceOpponent();
            bool week = !_onTitle && _career.Stage == CareerStage.Week && !_career.BackstageOpen;
            if (!week) return false;
            if (_shotGrow && BotGrow()) { _view.Refresh(); grew = true; return true; }
            if (_career.State.ActionsLeft > 0 || _career.State.PromoChoicePending) { _view.OnDigit(1 + _shotDigit++ % 6); return true; }
            return false;
        }

        // ---------------- огляд QA: старт із сейву й бот кінцівок (п. 8 П5, 11.11.2026) ----------------

        private bool _shotArgsRead;
        /// <summary><c>-ring-shot-load &lt;сейв&gt;</c>: повний шлях, шлях від теки запуску або ім'я файла в persistentDataPath.</summary>
        private string _shotLoad;
        /// <summary>0 — сейв ще не відкривали, 1 — «Продовжити» запущено, 2 — готово або не вдалося.</summary>
        private int _shotLoadState;
        private bool _shotHasEnding;
        private FeudEnding _shotEnding;
        private bool _shotEndingNoted;
        private MatchEngine _shotGoalNoted;
        private PromoRunner _botRewardsRunner;
        private string _botRewardsNode;
        private readonly HashSet<string> _botRewardsPressed = new HashSet<string>();
        private bool _shotDeclineTitle;
        /// <summary><c>-ring-shot-offer decline</c>: на кінці сезону з ключами — «Лишитися»; accept (за замовчуванням) — як каркас (Enter).</summary>
        private bool _shotOfferDecline;
        /// <summary><c>-ring-hero-name &lt;ім'я&gt;</c>: ім'я героя огляду (обрізане за MaxNameLength, як поле вводу); null — «Новачок».</summary>
        private string _shotHeroName;
        /// <summary><c>-ring-shot-opponent &lt;id&gt;</c>: суперник першого звичайного тижня огляду; null — уже підмінено або не задано.</summary>
        private string _shotOpponent;
        /// <summary><c>-ring-shot-event &lt;id&gt;</c>: сцена, яку відкриє перше «Закулісся» огляду (поки не примушено — не null).</summary>
        private string _shotEvent;
        private bool _shotBackstageAll;
        private Queue<string> _shotBackstageQueue;

        /// <summary>Прапорці огляду кар'єри — лише разом з <c>-ring-shots</c> (сейв і гра гравця не чіпаються).</summary>
        private void ReadCareerShotArgs()
        {
            if (_shotArgsRead) return;
            _shotArgsRead = true;
            if (_shotsDir == null) return;
            string load = ReadArg("-ring-shot-load");
            if (!string.IsNullOrEmpty(load))
            {
                if (System.IO.Path.IsPathRooted(load)) _shotLoad = load;
                else if (System.IO.File.Exists(load)) _shotLoad = System.IO.Path.GetFullPath(load);
                else _shotLoad = System.IO.Path.Combine(Application.persistentDataPath, load);
            }
            _shotDeclineTitle = RingLaunchArgs.Has("-ring-shot-decline-title");
            string offer = ReadArg("-ring-shot-offer");
            if (offer != null)
            {
                if (offer == "decline") _shotOfferDecline = true;
                else if (offer != "accept") Debug.LogWarning("[Ринг] -ring-shot-offer: accept або decline, а не '" + offer + "' — як за замовчуванням (accept)");
            }
            string name = ReadArg("-ring-hero-name");
            if (name != null)
            {
                if (name.Length > CareerSession.MaxNameLength) name = name.Substring(0, CareerSession.MaxNameLength);
                if (name.Trim().Length == 0) Debug.LogWarning("[Ринг] -ring-hero-name: порожнє ім'я — лишається «Новачок»");
                else
                {
                    _shotHeroName = name;
                    ShotNote("ім'я героя: «" + name + "» (" + name.Length + " знаків, межа " + CareerSession.MaxNameLength + ")");
                }
            }
            _shotOpponent = ReadArg("-ring-shot-opponent");
            _shotEvent = ReadArg("-ring-shot-event");
            string bs = ReadArg("-ring-shot-backstage");
            if (bs != null)
            {
                if (bs == "all") _shotBackstageAll = true;
                else Debug.LogWarning("[Ринг] -ring-shot-backstage: відоме значення лише all, а не '" + bs + "'");
            }
            string end = ReadArg("-ring-shot-ending");
            if (end != null)
            {
                if (FeudBot.TryParseEnding(end, out _shotEnding)) _shotHasEnding = true;
                else Debug.LogWarning("[Ринг] -ring-shot-ending: невідома кінцівка '" + end + "' (own_script, outcast, crowd_favorite, hated_heel, honorable_loss, forgotten)");
            }
        }

        /// <summary>
        /// <c>-ring-shot-load</c>: на титулі — «Продовжити» з заданого сейву (тим самим входом, що й кнопка: розріз, завантаження,
        /// екран за етапом). Не відкрився — лог і звичайний огляд з нової кар'єри.
        /// </summary>
        private bool BotLoadStep()
        {
            if (_shotLoad == null || !_onTitle || _shotLoadState == 2) return false;
            if (_shotLoadState == 0)
            {
                if (!RingSaveFile.Exists(_shotLoad))
                {
                    Debug.LogError("[Ринг] -ring-shot-load: сейву немає — " + _shotLoad);
                    ShotNote("сейв не знайдено: " + _shotLoad + " — нова кар'єра");
                    _shotLoadState = 2;
                    return false;
                }
                ShotNote("старт із сейву: " + _shotLoad);
                _shotLoadState = 1;
                StartGame(true);
                return true;
            }
            // «Продовжити» запущено: чекаємо розрізу й завантаження; скінчилось, а ми ще на титулі — сейв не відкрився
            if (_wipe.Busy || _loading != null) return true;
            Debug.LogError("[Ринг] -ring-shot-load: сейв не відкрився — " + _shotLoad);
            ShotNote("сейв не відкрився: " + _shotLoad + " — нова кар'єра");
            _shotLoadState = 2;
            return false;
        }

        /// <summary>
        /// Ціль матчу бота кінцівок (<c>-ring-shot-ending</c>) для ходу за героя (RingGameBootstrap.Match.cs): розв'язка — сама
        /// кінцівка з урахуванням плану, решта матчів — план букера і стиль потрібного шляху (<see cref="FeudBot.GoalFor"/>).
        /// null — огляд без цілі (звичайний бот).
        /// </summary>
        public MatchGoal? ShotMatchGoal
        {
            get
            {
                if (!_shotHasEnding || _onTitle || _career == null || _career.Engine == null) return null;
                var eng = _career.Engine;
                bool decisive = _career.CurrentMatchDecisive;
                var plan = _career.CurrentMatchPlan;
                var g = FeudBot.GoalFor(_shotEnding, decisive, plan);
                if (decisive && _shotGoalNoted != eng && _shotsDir != null)
                {
                    _shotGoalNoted = eng;
                    ShotNote("розв'язка ворожнечі: план " + FeudVars.PlanNames[(int)plan] + ", ціль матчу " + g
                             + (FeudBot.PlanAllows(_shotEnding, plan) ? "" : " — з цим планом кінцівка " + FeudBot.EndingName(_shotEnding) + " недосяжна"));
                }
                return g;
            }
        }

        /// <summary>
        /// Бот кінцівок у промо ворожнечі й сценах закулісся: вибір — цифрою варіанта, який обрав <see cref="FeudBot.ChooseOption"/>;
        /// нагороди кидка — цифрами <see cref="FeudBot.ChooseRewards"/> по одній за крок, далі «Підтвердити». Репліки й сам кидок —
        /// каркас («Далі»). Кінцівка ворожнечі — рядок у screens.txt і лог: ціль і що вийшло.
        /// </summary>
        private bool BotEndingStep()
        {
            if (!_shotHasEnding || _onTitle || _career == null) return false;
            if (!_shotEndingNoted && _career.Feud.HasEnding)
            {
                _shotEndingNoted = true;
                string line = "кінцівка ворожнечі: ціль " + FeudBot.EndingName(_shotEnding) + ", вийшло " + FeudBot.EndingName(_career.Feud.Ending)
                              + (_career.Feud.Ending == _shotEnding ? " — так" : " — ні");
                Debug.Log("[Ринг] Огляд: " + line);
                if (_shotsDir != null) ShotNote(line);
            }
            PromoRunner r = null;
            MatchDef next = null;
            if (_career.Stage == CareerStage.Week && _career.BackstageOpen) r = _career.CurrentBackstage;
            else if (_career.Stage == CareerStage.Show && _career.State.Phase == ShowPhase.Feud && _career.Feud.Stage == RingStage.Promo)
            {
                r = _career.Feud.CurrentPromoRunner;
                next = FeudBot.NextMatch(_career.Feud);
            }
            if (r == null || r.IsFinished || !(_view is PromoScreenView) || !ReferenceEquals(_viewKey, r)) return false;
            if (r.Current.Type == PromoNodeType.Choice)
            {
                string id = FeudBot.ChooseOption(r, _shotEnding, next);
                int n = id == null ? 0 : FeudBot.OptionNumber(r, id);
                if (n <= 0) return false;
                ShotNote("кінцівка " + FeudBot.EndingName(_shotEnding) + ": " + r.Promo.Id + "/" + r.Current.Id + " → " + id);
                _view.OnDigit(n);
                return true;
            }
            if (r.Current.Type == PromoNodeType.Check && r.AwaitingRewards)
            {
                if (_botRewardsRunner != r || _botRewardsNode != r.Current.Id)
                {
                    _botRewardsRunner = r; _botRewardsNode = r.Current.Id; _botRewardsPressed.Clear();
                }
                foreach (var id in FeudBot.ChooseRewards(r, _shotEnding))
                {
                    if (_botRewardsPressed.Contains(id)) continue;
                    _botRewardsPressed.Add(id);
                    _view.OnDigit(FeudBot.RewardNumber(r, id));
                    return true;
                }
                _view.OnConfirm();
                return true;
            }
            return false;
        }

        /// <summary>
        /// <c>-ring-shot-event &lt;id&gt;</c> і <c>-ring-shot-backstage all</c>: перед «Закуліссям» тижня примусити сцену
        /// (<see cref="CareerSession.ForceBackstage"/>) — спершу задану подію, далі всі сцени каталогу по черзі, по одній на кожне
        /// «Закулісся». Кроку бота не займає: дію тижня тисне каркас.
        /// </summary>
        private void BotForceScenes()
        {
            if (_onTitle || _career == null || _career.Stage != CareerStage.Week || _career.BackstageOpen || _career.ForcedBackstageScene != null) return;
            if (_shotEvent != null)
            {
                string id = _shotEvent;
                _shotEvent = null;
                ForceScene(id, "подія примусово");
                return;
            }
            if (!_shotBackstageAll || _career.Content.Backstage == null) return;
            if (_shotBackstageQueue == null)
            {
                _shotBackstageQueue = new Queue<string>();
                foreach (var sc in _career.Content.Backstage.Scenes) _shotBackstageQueue.Enqueue(sc.Id);
            }
            if (_shotBackstageQueue.Count > 0) ForceScene(_shotBackstageQueue.Dequeue(), "закулісся по черзі (" + _shotBackstageQueue.Count + " ще)");
        }

        private void ForceScene(string id, string why)
        {
            try
            {
                _career.ForceBackstage(id);
                ShotNote(why + ": наступне «Закулісся» — " + id);
            }
            catch (ArgumentException)
            {
                Debug.LogWarning("[Ринг] немає закулісної сцени '" + id + "'");
                ShotNote("немає закулісної сцени " + id);
            }
        }

        /// <summary>
        /// <c>-ring-shot-offer decline</c>: на кінці сезону з ключами — «Лишитися» (той самий виклик, що й кнопка). Наступного сезону
        /// пропозиція повториться — і друга відмова поспіль покаже «букер ображений». accept — нічого: «Перейти» тисне каркас.
        /// </summary>
        private bool BotOfferStep()
        {
            if (!_shotOfferDecline || _onTitle || _career == null || _career.Stage != CareerStage.SeasonEnd || !_career.OfferAvailable) return false;
            ShotNote("пропозиція " + _career.OfferFederation.Tier + "-го рівня: «Лишитися» (-ring-shot-offer decline), відмова поспіль № " + (_career.State.OfferDeclines + 1));
            _career.DeclineOffer();
            Save();
            _view.Refresh();
            return true;
        }

        /// <summary>
        /// <c>-ring-shot-opponent &lt;id&gt;</c>: на першому звичайному тижні огляду (до першої дії) — суперник за id
        /// (<see cref="CareerSession.ForceOpponent"/>). Не вийшло (велике шоу, чужий дивізіон, немає такого) — пробує на наступному тижні.
        /// Кроку бота не займає.
        /// </summary>
        private void BotForceOpponent()
        {
            if (_shotOpponent == null || _onTitle || _career == null || _career.Stage != CareerStage.Week || _career.State.ActionsDone.Count > 0) return;
            if (_career.Content.FindNpc(_shotOpponent) == null)
            {
                Debug.LogWarning("[Ринг] -ring-shot-opponent: немає борця '" + _shotOpponent + "'");
                ShotNote("немає борця " + _shotOpponent);
                _shotOpponent = null;
                return;
            }
            if (!_career.ForceOpponent(_shotOpponent)) return;
            ShotNote("суперник тижня " + _career.State.Week + ": " + _shotOpponent + " (-ring-shot-opponent)");
            _shotOpponent = null;
            Save();
            _view.Refresh();
        }

        /// <summary><c>-ring-shot-decline-title</c>: на кінці сезону з титульним шансом — «Відмовитись» (той самий виклик, що й кнопка).</summary>
        private bool BotDeclineTitleStep()
        {
            if (!_shotDeclineTitle || _onTitle || _career == null || _career.Stage != CareerStage.SeasonEnd || !_career.CanStartTitleMatch) return false;
            ShotNote("титульний шанс: відмова (-ring-shot-decline-title)");
            _career.SkipTitle();
            Save();
            _view.Refresh();
            return true;
        }
    }
}
