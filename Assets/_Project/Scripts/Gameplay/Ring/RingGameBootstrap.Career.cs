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
                var lines = RingSaveFile.Read(SavePath, l =>
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
                _onTitle = false;
                LastWeekResult = null;
                Rebind();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Ринг] Сейв не відкрився: " + ex.Message);
            }
        }

        public void Save()
        {
            if (_career == null || _career.Stage == CareerStage.Title || _career.Stage == CareerStage.Create) return;
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
            bool week = !_onTitle && _career.Stage == CareerStage.Week && !_career.BackstageOpen;
            if (!week) return false;
            if (_shotGrow && BotGrow()) { _view.Refresh(); grew = true; return true; }
            if (_career.State.ActionsLeft > 0 || _career.State.PromoChoicePending) { _view.OnDigit(1 + _shotDigit++ % 6); return true; }
            return false;
        }
    }
}
