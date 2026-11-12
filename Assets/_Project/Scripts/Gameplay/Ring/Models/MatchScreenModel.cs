using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Ring;
using Game.Core.Ring.Match;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>Картка борця (TRPG_DESIGN §4.3): ушкодження з порогами, імпульс, «Сили», стан, жетони.</summary>
    public sealed class FighterCardModel
    {
        public MatchSide Side;
        public string Name;
        public int Damage;
        public int DamageMax;
        public string DamageText;
        /// <summary>Пороги штрафу: «виснажений» і «на межі» (з урахуванням перка «Залізна щелепа»).</summary>
        public int ExhaustedAt;
        public int EdgeAt;
        /// <summary>Поточний штраф до кидків 0/1/2 і його текст (порожньо, якщо штрафу нема).</summary>
        public int Penalty;
        public string PenaltyText;
        public int Momentum;
        public int MomentumMax;
        public string MomentumText;
        public string PostureText;
        /// <summary>«Сили» 0–10 (гальмо ланцюга ініціативи) і їх текст.</summary>
        public int Stamina;
        public int StaminaMax;
        public string StaminaText;
        /// <summary>Ініціатива зараз у цього борця.</summary>
        public bool HasInitiative;
        public int Tokens;
        public string TokensText;
        public int Warnings;
        public string WarningsText;
    }

    /// <summary>Рядок списку дій. Number &gt; 0 — гаряча клавіша (лише для доступних, перші дев'ять).</summary>
    public sealed class MatchActionRow
    {
        public int Number;
        public MatchActionId Id;
        /// <summary>Чия дія: рід суперника в шансах і причинах («Її «Вирватися»», «Суперниця не лежить») — від протилежного борця.</summary>
        public MatchSide Side;
        /// <summary>Прийом кар'єри (ключ каталогу) або null — системна дія рушія.</summary>
        public string MoveId;
        /// <summary>Той самий прийом двічі поспіль: −1 ажіотажу.</summary>
        public bool RepeatWarning;
        public string Name;
        /// <summary>Ціна в «Силах».</summary>
        public int Cost;
        public string CostText;
        /// <summary>«Сил» 0: у шансах уже −1.</summary>
        public bool Winded;
        /// <summary>Атрибут кидка («Сила»); порожньо, якщо кидка нема.</summary>
        public string AttrText = "";
        public bool Available;
        public string ReasonText = "";
        public bool HasRoll;
        /// <summary>Шанси — суперника (для «Утримання»).</summary>
        public bool RollIsOpponents;
        public string ModifierText = "";
        /// <summary>[провал, 7–9, 10+] у відсотках, сума 100 (порожньо, якщо кидка нема).</summary>
        public int[] Percents = new int[0];
        public bool IsBig;
        /// <summary>
        /// Готові тексти трьох шансів у порядку «10+ / 7–9 / ≤6» (порожньо, якщо кидка нема). Для «Утримання» (<see cref="RollIsOpponents"/>)
        /// — з боку суперника: «Вирвався / На два / Три»; для «променя надії» ≤6 не провал.
        /// </summary>
        public string[] ChanceTexts = new string[0];
        /// <summary>Шанси — суперника: колонка «10+» тут погана для гравця («вирвався»), а «≤6» — добра («три»); колір смужки інвертується.</summary>
        public bool ChancesInverted;
        /// <summary>false для «променя надії»: ≤6 не провал.</summary>
        public bool MissIsFailure = true;
        /// <summary>Пояснення до шансів (для «променя надії»), інакше порожньо.</summary>
        public string ChanceNote = "";
        /// <summary>«Сили» до і після дії (з прев'ю рушія) і готовий рядок «Сили після дії: 7 → 5»; порожньо, якщо сили не змінюються.</summary>
        public int StaminaBefore;
        public int StaminaAfter;
        public string StaminaAfterText = "";
        /// <summary>Попередження «Сил немає: −1 до кидка» (<see cref="Winded"/>) і «Той самий прийом двічі» (<see cref="RepeatWarning"/>) текстом.</summary>
        public string WindedText = "";
        public string RepeatText = "";
        /// <summary>Підказка рядка (та сама, що при наведенні мишею); для клавіатури й геймпада.</summary>
        public string HintText = "";
    }

    /// <summary>Що зробить суперник (показ «як свій», до виконання): дія, продаж, реверс чи докрутка.</summary>
    public enum MatchIntentKind { Action = 0, Sell = 1, Reverse = 2, Boost = 3 }

    public sealed class MatchIntentView
    {
        public MatchIntentKind Kind;
        public MatchSide Side;
        public string Text;
        /// <summary>Рядок дії суперника (шанси й ціна — тими самими текстами, що в гравця); лише для <see cref="MatchIntentKind.Action"/>.</summary>
        public MatchActionRow Row;
        public MatchActionId Action;
        /// <summary>Реакція: відсотки реверсу [≤6, 7–9, 10+]. Докрутка: витрата імпульсу й ступінь, що вийде.</summary>
        public int[] ReversePercents = new int[0];
        public int BoostPoints;
        public RollTier BoostTier;
    }

    /// <summary>Рядок журналу для екрана: текст, хто діяв (бік підкладки), чи це найновіший.</summary>
    public sealed class MatchLogLine
    {
        public int Seq;
        public string Text;
        public MatchSide Actor;
        public bool IsLatest;
    }

    /// <summary>Сторона табло: «Сили» з максимумом і часткою, штраф за нуль.</summary>
    public sealed class ScoreboardSide
    {
        public MatchSide Side;
        public string Name;
        public int Stamina;
        public int StaminaMax;
        public float Stamina01;
        public string StaminaText;
        /// <summary>«Сил» 0: −1 до кожного кидка.</summary>
        public bool Winded;
        public string WindedText;
        public bool HasInitiative;
    }

    /// <summary>Табло матчу: ініціатива ×N, «Сили» обох, місце пари й відлік, ажіотаж, номер дії.</summary>
    public sealed class ScoreboardModel
    {
        public bool Over;
        public MatchSide InitiativeSide;
        /// <summary>Номер дії, що ось-ось буде в ланцюгу ініціативи (×N).</summary>
        public int ChainNumber;
        public string InitiativeText;
        public ScoreboardSide Player;
        public ScoreboardSide Rival;
        public bool Outside;
        /// <summary>«На рингу» або «За рингом» — завжди непорожнє.</summary>
        public string PlaceLabel;
        /// <summary>«Відлік за рингом: x/10» або порожньо (на рингу, «без DQ»).</summary>
        public string CountText;
        public string HypeText;
        public string RoundText;
    }

    /// <summary>Реакція захисника: продати прийом чи спробувати реверс (gdd §2.5).</summary>
    public sealed class ReactionView
    {
        public string AttackerName;
        public string ActionName;
        public string PromptText;
        public string SellText;
        public string ReverseText;
        public bool CanReverse;
        public int Tokens;
        public int[] ReversePercents = new int[0];
    }

    /// <summary>Варіант докрутки імпульсом: скільки витратити і який ступінь вийде.</summary>
    public sealed class BoostOptionRow
    {
        public int Number;
        public int Points;
        public RollTier Tier;
        public string Text;
    }

    /// <summary>Чий вхід зараз потрібен.</summary>
    public enum MatchInput { PlayerAction = 0, PlayerReaction = 1, PlayerBoost = 2, Ai = 3, Over = 4 }

    /// <summary>
    /// Модель екрана матчу (TRPG_DESIGN §4): картки борців, ажіотаж, ініціатива, список дій із шансами й причинами, модальні реакція
    /// і докрутка, журнал. Читає MatchEngine через його публічний API; команди — тонкі обгортки. Хід суперника веде ШІ
    /// (<see cref="StepAi"/>), темп (пауза) задає екран.
    /// </summary>
    public sealed class MatchScreenModel
    {
        public const int LogLines = 8;

        private readonly MatchEngine _e;
        private readonly RingText _t;
        private readonly Game.Core.Ring.Career.CareerSession _career;

        /// <summary>
        /// <paramref name="career"/> — матч кар'єри: замість 17 дій рушія гравець обирає прийоми свого набору (TRPG_DESIGN §5.4)
        /// плюс системні дії (утримання, встати, промінь надії…). Без кар'єри — дії рушія, як у зрізі ворожнечі.
        /// </summary>
        public MatchScreenModel(MatchEngine engine, RingText text, Game.Core.Ring.Career.CareerSession career = null)
        {
            _e = engine ?? throw new ArgumentNullException(nameof(engine));
            _t = text ?? throw new ArgumentNullException(nameof(text));
            _career = career;
        }

        public MatchEngine Engine { get { return _e; } }

        // ---------- вхід ----------

        public MatchSide PendingSide
        {
            get
            {
                switch (_e.Phase)
                {
                    case MatchPhase.AwaitReaction: return _e.PendingReaction.Defender;
                    case MatchPhase.AwaitBoost: return _e.PendingRoll.Actor;
                    default: return _e.CurrentActor;
                }
            }
        }

        public MatchInput Input
        {
            get
            {
                if (_e.IsOver) return MatchInput.Over;
                if (PendingSide != MatchSide.Player) return MatchInput.Ai;
                switch (_e.Phase)
                {
                    case MatchPhase.AwaitReaction: return MatchInput.PlayerReaction;
                    case MatchPhase.AwaitBoost: return MatchInput.PlayerBoost;
                    default: return MatchInput.PlayerAction;
                }
            }
        }

        public bool IsOver { get { return _e.IsOver; } }

        /// <summary>Один крок ШІ за суперника (дія, реакція чи фіксація кидка). Лише коли Input == Ai.</summary>
        public void StepAi()
        {
            if (Input != MatchInput.Ai) throw new InvalidOperationException("Зараз не хід ШІ");
            MatchAi.Step(_e);
        }

        // ---------- картки, ажіотаж, раунд ----------

        public FighterCardModel Card(MatchSide side)
        {
            var f = _e.State.Get(side);
            var mods = side == MatchSide.Player ? _e.Setup.PlayerMods : _e.Setup.RivalMods;
            int shift = mods == null ? 0 : mods.DamageThresholdShift;
            int pen = _e.Threshold(f);
            var c = new FighterCardModel
            {
                Side = side,
                Name = _t.Get(f.Stats.NameKey),
                Damage = f.Damage,
                DamageMax = MatchRules.MaxDamage,
                ExhaustedAt = MatchRules.ExhaustedDamage + shift,
                EdgeAt = MatchRules.EdgeDamage + shift,
                Penalty = pen,
                Momentum = f.Momentum,
                MomentumMax = MatchRules.MaxMomentum,
                PostureText = RingLabels.PostureName(_t, f.Posture, side),
                Stamina = f.Stamina,
                StaminaMax = MatchRules.MaxStamina,
                HasInitiative = !_e.IsOver && _e.CurrentActor == side,
                Tokens = f.ReversalTokens,
                Warnings = f.Warnings
            };
            c.DamageText = _t.Format("match.card.damage", f.Damage, MatchRules.MaxDamage);
            // Звідки штраф — у самому рядку (плейтест 08.11.2026, P2-3: «штраф −1/−2» червоним без пояснення).
            c.PenaltyText = pen > 0 ? _t.Format("match.card.penalty", pen, pen >= 2 ? c.EdgeAt : c.ExhaustedAt) : "";
            c.MomentumText = _t.Format("match.card.momentum", f.Momentum, MatchRules.MaxMomentum);
            c.StaminaText = _t.Format("match.card.stamina", f.Stamina, MatchRules.MaxStamina);
            c.TokensText = _t.Format("match.card.tokens", f.ReversalTokens);
            c.WarningsText = f.Warnings > 0 ? _t.Format("match.card.warnings", f.Warnings, MatchRules.WarningsToDq) : "";
            return c;
        }

        public int Hype { get { return _e.State.Hype; } }
        public int HypeMax { get { return MatchRules.MaxHype; } }
        public string HypeText { get { return _t.Format("match.hype", Hype, HypeMax, RingLabels.HypeStageText(_t, Hype)); } }
        /// <summary>Номер дії матчу з ліміту (ланцюг ініціативи рахує кожну дію; раундів немає).</summary>
        public string RoundText { get { return _t.Format("match.round", Math.Min(_e.Round, _e.Setup.RoundLimit), _e.Setup.RoundLimit); } }

        /// <summary>У кого ініціатива і яка це дія ланцюга («Ініціатива: ти ×3»); порожньо після кінця.</summary>
        public string InitiativeText
        {
            get
            {
                if (_e.IsOver) return "";
                if (_e.CurrentActor == MatchSide.Player) return _t.Format("match.initiative.player", _e.Chain + 1);
                return RingLabels.ForSideFormat(_t, "match.initiative.rival", MatchSide.Rival, _e.Chain + 1);
            }
        }

        /// <summary>Пара за рингом (бій за рингом склейкою, рішення власника 08.11.2026).</summary>
        public bool IsOutside { get { return _e.State.Outside; } }
        /// <summary>«За рингом» або порожньо (на рингу).</summary>
        public string PlaceText { get { return IsOutside ? _t.Get("match.place.outside") : ""; } }
        /// <summary>«Відлік за рингом: 6/10»; порожньо на рингу і в матчі «без DQ» (там відліку немає).</summary>
        public string CountText
        {
            get
            {
                if (!IsOutside || (_e.Setup.Stipulations & Game.Core.Ring.MatchStipulations.NoDq) != 0) return "";
                return _t.Format("match.count", _e.State.OutsideCount, MatchRules.OutsideCountLimit);
            }
        }

        public string RefereeText { get { return _t.Get(_e.State.RefereeDistracted ? "match.referee.distracted" : "match.referee.watching"); } }

        public string TurnText
        {
            get
            {
                if (_e.IsOver) return _t.Get("match.turn.over");
                switch (Input)
                {
                    case MatchInput.PlayerReaction: return _t.Get("match.turn.react");
                    case MatchInput.PlayerBoost: return _t.Get("match.turn.boost");
                    case MatchInput.PlayerAction: return _t.Get("match.turn.player");
                    default:
                        return RingLabels.ForSide(_t, _e.Phase == MatchPhase.AwaitReaction ? "match.turn.rival.react" : "match.turn.rival", MatchSide.Rival);
                }
            }
        }

        // ---------- дії ----------

        /// <summary>Усі дії з цінами, шансами й причинами; номери — лише доступним (перші дев'ять).</summary>
        public List<MatchActionRow> Actions()
        {
            var rows = new List<MatchActionRow>();
            if (Input != MatchInput.PlayerAction) return rows;
            int n = 1;
            if (_career != null) return CareerActions();
            foreach (var o in _e.GetAvailableActions())
            {
                var r = Row(o, RingLabels.ActionName(_t, o.Id));
                if (r.Available && n <= 9) r.Number = n++;
                rows.Add(r);
            }
            return rows;
        }

        /// <summary>Прийоми набору (назва — прийом, шанси — дії рушія з травмою й бонусом) і системні дії кар'єри.</summary>
        private List<MatchActionRow> CareerActions()
        {
            var rows = new List<MatchActionRow>();
            int n = 1;
            foreach (var mo in _career.GetMoveOptions())
            {
                var r = Row(mo.Option, _t.Get(mo.Move.NameKey));
                r.MoveId = mo.Move.Id;
                r.RepeatWarning = mo.RepeatWarning;
                if (mo.RepeatWarning) r.RepeatText = _t.Get("match.action.repeat");
                if (r.Available && n <= 9) r.Number = n++;
                rows.Add(r);
            }
            foreach (var o in _career.GetUniversalOptions())
            {
                var r = Row(o, RingLabels.ActionName(_t, o.Id));
                if (r.Available && n <= 9) r.Number = n++;
                rows.Add(r);
            }
            return rows;
        }

        private MatchActionRow Row(ActionOption o, string name) { return Build(o, name, MatchSide.Player); }

        private MatchActionRow Build(ActionOption o, string name, MatchSide side)
        {
            var r = new MatchActionRow
            {
                Id = o.Id,
                Side = side,
                Name = name,
                Cost = o.Cost,
                CostText = o.Cost > 0 ? _t.Format("match.action.cost", o.Cost) : _t.Get("match.action.free"),
                Available = o.Available,
                IsBig = o.IsBig,
                HasRoll = o.HasRoll,
                RollIsOpponents = o.RollIsOpponents,
                Winded = o.Winded
            };
            if (o.HasAttr) r.AttrText = RingLabels.AttrName(_t, o.Attr);
            if (o.HasRoll && o.Chances != null)
            {
                r.Percents = RingChances.Percent(o.Chances);
                r.ModifierText = RingChances.Signed(o.Modifier);
                FillChanceTexts(r);
            }
            if (!o.Available) r.ReasonText = RingLabels.Unavailable(_t, o.Reason, side);
            // «Сили після дії» — з прев'ю рушія (одна правда з Perform); WrongPhase не заважає: сторона може й не ходити.
            var pv = _e.Preview(side, o.Id);
            r.StaminaBefore = pv.StaminaBefore;
            r.StaminaAfter = pv.StaminaAfter;
            if (pv.StaminaAfter != pv.StaminaBefore) r.StaminaAfterText = _t.Format("match.action.stamina_after", pv.StaminaBefore, pv.StaminaAfter);
            if (o.Winded) r.WindedText = _t.Get("match.action.winded");
            r.HintText = HintFor(r);
            return r;
        }

        /// <summary>Готові підписи трьох шансів: «10+ 42%», «7–9 39%», «≤6 19%»; для суперника й «променя надії» — свої.</summary>
        private void FillChanceTexts(MatchActionRow r)
        {
            int p0 = r.Percents[0], p1 = r.Percents[1], p2 = r.Percents[2];
            if (r.RollIsOpponents)
            {
                r.ChancesInverted = true;
                var opp = RingLabels.Other(r.Side);   // «Вирвалася» — у роді того, хто вириватиметься
                r.ChanceTexts = new[]
                {
                    RingLabels.ForSideFormat(_t, "match.chance.opp.strong", opp, p2), _t.Format("match.chance.opp.partial", p1), _t.Format("match.chance.opp.miss", p0)
                };
                return;
            }
            bool hope = r.Id == MatchActionId.HopeSpot;
            if (hope) { r.MissIsFailure = false; r.ChanceNote = _t.Get("match.chance.hope.note"); }
            r.ChanceTexts = new[]
            {
                _t.Format("match.chance.strong", p2), _t.Format("match.chance.partial", p1),
                _t.Format(hope ? "match.chance.hope.miss" : "match.chance.miss", p0)
            };
        }

        /// <summary>
        /// Підказка рядка дії: «Назва · N сил · Атрибут ±mod | шанси» — той самий текст, що при наведенні мишею; клавіатура й геймпад
        /// показують саме його (фокус — <see cref="MatchFocusModel"/>). «Перевести подих» — свій пояснювальний текст.
        /// </summary>
        public string HintFor(MatchActionRow a)
        {
            if (a.Id == MatchActionId.CatchBreath) return _t.Get("match.endturn");
            string s = a.Name + "  ·  " + a.CostText;
            if (a.AttrText.Length > 0) s += "  ·  " + a.AttrText + " " + a.ModifierText;
            if (a.HasRoll && a.Percents.Length == 3)
                s += "   |   " + ChancesShort(_t, a);
            return s;
        }

        /// <summary>«Шанси: 10+ … » або для «Утримання» «Його / Її «Вирватися»: …» — у роді суперника борця, що діє.</summary>
        public static string ChancesShort(RingText t, MatchActionRow a)
        {
            if (a.RollIsOpponents) return RingLabels.ForSideFormat(t, "match.action.chances.opp", RingLabels.Other(a.Side), a.Percents[0], a.Percents[1], a.Percents[2]);
            return t.Format("match.chances.short", a.Percents[0], a.Percents[1], a.Percents[2]);
        }

        /// <summary>Підказка, поки ніщо не в фокусі й не під мишею.</summary>
        public string DefaultHint { get { return _t.Get("match.hint.default"); } }

        /// <summary>Виконати рядок: прийом кар'єри або дію рушія.</summary>
        public bool PerformRow(MatchActionRow row)
        {
            if (row == null) return false;
            if (row.MoveId == null || _career == null) return _career != null ? PerformCareerAction(row.Id) : Perform(row.Id);
            if (Input != MatchInput.PlayerAction || !row.Available) return false;
            _career.PerformMove(row.MoveId);
            return true;
        }

        private bool PerformCareerAction(MatchActionId id)
        {
            if (Input != MatchInput.PlayerAction) return false;
            if (!_e.Describe(id).Available) return false;
            _career.PerformAction(id);
            return true;
        }

        /// <summary>Виконати дію гравця. «Перевести подих» віддає ініціативу.</summary>
        public bool Perform(MatchActionId id)
        {
            if (Input != MatchInput.PlayerAction) return false;
            var o = _e.Describe(id);
            if (!o.Available) return false;
            if (id == MatchActionId.CatchBreath) _e.EndTurn();
            else _e.Perform(id, id == MatchActionId.Finisher ? _e.Setup.FinisherOf(MatchSide.Player) : null);
            return true;
        }

        public bool PerformNumber(int number)
        {
            foreach (var r in Actions()) if (r.Number == number) return PerformRow(r);
            return false;
        }

        // ---------- табло ----------

        /// <summary>Табло: ініціатива ×N, «Сили» обох із максимумом, місце пари («На рингу»/«За рингом») і відлік. Лише читання.</summary>
        public ScoreboardModel Scoreboard()
        {
            return new ScoreboardModel
            {
                Over = _e.IsOver,
                InitiativeSide = _e.CurrentActor,
                ChainNumber = _e.Chain + 1,
                InitiativeText = InitiativeText,
                Player = ScoreSide(MatchSide.Player),
                Rival = ScoreSide(MatchSide.Rival),
                Outside = IsOutside,
                PlaceLabel = _t.Get(IsOutside ? "match.place.outside" : "match.place.ring"),
                CountText = CountText,
                HypeText = HypeText,
                RoundText = RoundText
            };
        }

        private ScoreboardSide ScoreSide(MatchSide side)
        {
            var f = _e.State.Get(side);
            return new ScoreboardSide
            {
                Side = side,
                Name = _t.Get(f.Stats.NameKey),
                Stamina = f.Stamina,
                StaminaMax = MatchRules.MaxStamina,
                Stamina01 = MatchRules.MaxStamina > 0 ? (float)f.Stamina / MatchRules.MaxStamina : 0f,
                StaminaText = _t.Format("match.card.stamina", f.Stamina, MatchRules.MaxStamina),
                Winded = f.Stamina <= 0,
                WindedText = f.Stamina <= 0 ? _t.Get("match.stamina.zero") : "",
                HasInitiative = !_e.IsOver && _e.CurrentActor == side
            };
        }

        // ---------- хід суперника «як свій» ----------

        /// <summary>
        /// Що зробить суперник просто зараз (лише коли <see cref="Input"/> == Ai): його дія з тими самими шансами й ціною, що в
        /// гравця, або реакція чи докрутка. Вибір — той самий ШІ, що виконає <see cref="StepAi"/>; кубика й стану не торкається
        /// (прев'ю + <see cref="MatchAi"/> без побічних ефектів).
        /// </summary>
        public MatchIntentView RivalIntent()
        {
            if (Input != MatchInput.Ai) return null;
            var side = PendingSide;
            string who = _t.Get(_e.State.Get(side).Stats.NameKey);
            switch (_e.Phase)
            {
                case MatchPhase.AwaitReaction:
                {
                    var pr = _e.PendingReaction;
                    var choice = MatchAi.ChooseReaction(_e);
                    var p = RingChances.Percent(pr.ReverseChances);
                    string act = RingLabels.ActionName(_t, pr.Action);
                    if (choice == ReactionChoice.Reverse)
                        return new MatchIntentView
                        {
                            Kind = MatchIntentKind.Reverse, Side = side, Action = pr.Action, ReversePercents = p,
                            Text = _t.Format("match.intent.reverse", who, _e.State.Get(side).ReversalTokens, p[0], p[1], p[2])
                        };
                    return new MatchIntentView
                    {
                        Kind = MatchIntentKind.Sell, Side = side, Action = pr.Action, ReversePercents = p,
                        Text = _t.Format("match.intent.sell", who, act)
                    };
                }
                case MatchPhase.AwaitBoost:
                {
                    var pr = _e.PendingRoll;
                    int pts = MatchAi.ChooseBoost(_e);
                    var tier = pts == 0 ? pr.Tier : (pts == pr.PointsToPartial ? RollTier.Partial : RollTier.Strong);
                    return new MatchIntentView
                    {
                        Kind = MatchIntentKind.Boost, Side = side, Action = pr.Action, BoostPoints = pts, BoostTier = tier,
                        Text = pts == 0
                            ? _t.Format("match.intent.noboost", who, RingLabels.TierName(_t, tier))
                            : _t.Format("match.intent.boost", who, pts, RingLabels.TierName(_t, tier))
                    };
                }
                default:
                {
                    var id = MatchAi.ChooseAction(_e);
                    var pv = _e.Preview(side, id);
                    var row = Build(PreviewToOption(pv), RingLabels.ActionName(_t, id), side);
                    return new MatchIntentView
                    {
                        Kind = MatchIntentKind.Action, Side = side, Action = id, Row = row,
                        Text = _t.Format("match.intent.action", who, row.Name)
                    };
                }
            }
        }

        private static ActionOption PreviewToOption(ActionPreview p)
        {
            return new ActionOption
            {
                Id = p.Id, Cost = p.Cost, IsBig = p.IsBig, HasAttr = p.HasAttr, Attr = p.Attr, Available = p.Available,
                Reason = p.Reason, HasRoll = p.HasRoll, RollIsOpponents = p.RollIsOpponents, Modifier = p.Modifier,
                Winded = p.Winded, Chances = p.Chances36
            };
        }

        // ---------- фокус без миші ----------

        /// <summary>Ідентифікатор рядка дії для фокуса: прийом кар'єри або дія рушія (стабільний між оновленнями рядків).</summary>
        public static string FocusId(MatchActionRow r) { return r.MoveId != null ? "move:" + r.MoveId : "act:" + (int)r.Id; }

        /// <summary>
        /// Кандидати на фокус для поточного входу: дії (підказка — як при наведенні мишею), «продати/реверс» у реакції, варіанти
        /// докрутки. Порожньо, коли вводу від гравця немає.
        /// </summary>
        public List<MatchFocusItem> FocusItems()
        {
            var items = new List<MatchFocusItem>();
            switch (Input)
            {
                case MatchInput.PlayerAction:
                    foreach (var r in Actions())
                        items.Add(new MatchFocusItem { Id = FocusId(r), Label = r.Name, Available = r.Available, Hint = r.HintText, Number = r.Number });
                    break;
                case MatchInput.PlayerReaction:
                {
                    var rv = Reaction();
                    items.Add(new MatchFocusItem { Id = "react:sell", Label = rv.SellText, Available = true, Hint = rv.SellText, Number = 1 });
                    items.Add(new MatchFocusItem { Id = "react:reverse", Label = rv.ReverseText, Available = rv.CanReverse, Hint = rv.ReverseText, Number = 2 });
                    break;
                }
                case MatchInput.PlayerBoost:
                    foreach (var b in BoostOptions())
                        items.Add(new MatchFocusItem { Id = "boost:" + b.Points, Label = b.Text, Available = true, Hint = b.Text, Number = b.Number });
                    break;
            }
            return items;
        }

        /// <summary>Оновити фокус свіжими кандидатами: фокус лишається на тому ж Id (екран перебудовується на кожен крок).</summary>
        public void RefreshFocus(MatchFocusModel focus)
        {
            if (focus == null) throw new ArgumentNullException(nameof(focus));
            focus.DefaultHint = DefaultHint;
            focus.Set(FocusItems());
        }

        // ---------- реакція ----------

        public ReactionView Reaction()
        {
            if (Input != MatchInput.PlayerReaction) return null;
            var pr = _e.PendingReaction;
            var def = _e.State.Get(pr.Defender);
            var p = RingChances.Percent(pr.ReverseChances);
            string att = _t.Get(_e.State.Get(pr.Attacker).Stats.NameKey);
            string act = RingLabels.ActionName(_t, pr.Action);
            return new ReactionView
            {
                AttackerName = att,
                ActionName = act,
                PromptText = _t.Format("match.react.prompt", att, act),
                SellText = _t.Get("match.react.sell"),
                ReverseText = _t.Format("match.react.reverse", def.ReversalTokens, p[0], p[1], p[2]),
                CanReverse = def.ReversalTokens > 0 && def.Posture != Posture.Stunned,
                Tokens = def.ReversalTokens,
                ReversePercents = p
            };
        }

        public bool React(ReactionChoice choice)
        {
            if (Input != MatchInput.PlayerReaction) return false;
            if (choice == ReactionChoice.Reverse && !Reaction().CanReverse) return false;
            _e.React(choice);
            return true;
        }

        // ---------- докрутка ----------

        /// <summary>Кидок гравця вже відомий: варіанти «без докрутки» та «+N імпульсу → ступінь».</summary>
        public List<BoostOptionRow> BoostOptions()
        {
            var rows = new List<BoostOptionRow>();
            if (Input != MatchInput.PlayerBoost) return rows;
            var pr = _e.PendingRoll;
            int n = 1;
            rows.Add(new BoostOptionRow
            {
                Number = n++, Points = 0, Tier = pr.Tier,
                Text = _t.Format("match.boost.none", RingLabels.TierName(_t, pr.Tier))
            });
            if (pr.PointsToPartial > 0)
                rows.Add(new BoostOptionRow
                {
                    Number = n++, Points = pr.PointsToPartial, Tier = RollTier.Partial,
                    Text = _t.Format("match.boost.spend", pr.PointsToPartial, RingLabels.TierName(_t, RollTier.Partial))
                });
            if (pr.PointsToStrong > 0)
                rows.Add(new BoostOptionRow
                {
                    Number = n++, Points = pr.PointsToStrong, Tier = RollTier.Strong,
                    Text = _t.Format("match.boost.spend", pr.PointsToStrong, RingLabels.TierName(_t, RollTier.Strong))
                });
            return rows;
        }

        /// <summary>Заголовок докрутки: дія, кидок, сума, поточний ступінь.</summary>
        public string BoostPrompt
        {
            get
            {
                if (Input != MatchInput.PlayerBoost) return "";
                var pr = _e.PendingRoll;
                return _t.Format("match.boost.prompt", RingLabels.ActionName(_t, pr.Action), pr.Roll,
                    RingChances.Term(pr.Modifier), pr.Total, RingLabels.TierName(_t, pr.Tier), pr.Impulse);
            }
        }

        public bool Commit(int points)
        {
            if (Input != MatchInput.PlayerBoost) return false;
            var pr = _e.PendingRoll;
            if (points != 0 && points != pr.PointsToPartial && points != pr.PointsToStrong) return false;
            _e.Commit(points);
            return true;
        }

        public bool CommitNumber(int number)
        {
            foreach (var r in BoostOptions()) if (r.Number == number) return Commit(r.Points);
            return false;
        }

        // ---------- журнал ----------

        /// <summary>
        /// Останні записи журналу текстом (найновіший — останній), лише до події з номером <paramref name="shownSeq"/>
        /// включно: 3D показує подію пізніше, ніж ядро її порахувало, і рядок не має випереджати тіло (огляд 08.11.2026:
        /// кікаут був у журналі ще до удару). Без 3D — усе.
        /// </summary>
        public List<string> LogTexts(int shownSeq = int.MaxValue)
        {
            var res = new List<string>();
            foreach (var l in LogEntries(shownSeq)) res.Add(l.Text);
            return res;
        }

        /// <summary>
        /// Те саме, що <see cref="LogTexts"/> (те саме відсікання за <paramref name="shownSeq"/>), але рядками з боком діяча й
        /// познакою найновішого — для підкладки на боці того, хто діє.
        /// </summary>
        public List<MatchLogLine> LogEntries(int shownSeq = int.MaxValue)
        {
            var res = new List<MatchLogLine>();
            var log = _e.State.Log;
            int end = log.Count;
            while (end > 0 && log[end - 1].Seq > shownSeq) end--;
            int from = Math.Max(0, end - LogLines);
            for (int i = from; i < end; i++)
                res.Add(new MatchLogLine { Seq = log[i].Seq, Text = EventText(log[i]), Actor = log[i].Actor, IsLatest = i == end - 1 });
            return res;
        }

        public string EventText(MatchEvent ev)
        {
            string name = _t.Get(_e.State.Get(ev.Actor).Stats.NameKey);
            string action = RingLabels.ActionName(_t, ev.Action);
            var sb = new StringBuilder();
            sb.Append(_t.Format("log.round", ev.Round)).Append(' ');
            switch (ev.Kind)
            {
                case MatchEventKind.Exchange: sb.Append(_t.Format("log.exchange", name, ev.Total, ev.OtherTotal)); break;
                case MatchEventKind.Sell: sb.Append(_t.Format("log.sell", name, action)); break;
                case MatchEventKind.Reverse: sb.Append(_t.Format("log.reverse", name, action, RingLabels.TierName(_t, ev.Tier))); break;
                case MatchEventKind.Kickout: sb.Append(_t.Format("log.kickout", name, RingLabels.TierName(_t, ev.Tier))); break;
                case MatchEventKind.Endure: sb.Append(_t.Format("log.endure", name, RingLabels.TierName(_t, ev.Tier))); break;
                case MatchEventKind.Rescue: sb.Append(_t.Format("log.rescue", name)); break;
                case MatchEventKind.TurnEnd: sb.Append(_t.Format("log.turnend", name)); break;
                case MatchEventKind.Flourish: sb.Append(_t.Format("log.flourish", name, action)); break;
                case MatchEventKind.MatchEnd: sb.Append(_t.Get("log.matchend")); break;
                default:
                    if (ev.HasRoll)
                        sb.Append(_t.Format("log.action.roll", name, action, RingLabels.TierName(_t, ev.Tier), ev.Total));
                    else
                        sb.Append(_t.Format("log.action", name, action));
                    break;
            }
            if (ev.Effects.Count > 0)
            {
                var fx = new List<string>();
                foreach (var e in ev.Effects) fx.Add(EffectText(e));
                sb.Append(" — ").Append(string.Join("; ", fx));
            }
            return sb.ToString();
        }

        private string EffectText(MatchEffect e)
        {
            string who = _t.Get(_e.State.Get(e.Who).Stats.NameKey);
            string kind = RingLabels.ForSide(_t, "fx." + (int)e.Kind, e.Who);   // «підвелася» — у роді того, з ким сталося
            switch (e.Kind)
            {
                case MatchEffectKind.Down:
                case MatchEffectKind.Stand:
                case MatchEffectKind.Stunned:
                case MatchEffectKind.Warning:
                case MatchEffectKind.RefereeDistracted:
                    return _t.Format("fx.flag", who, kind);
                default:
                    return _t.Format("fx.delta", who, kind, RingChances.Signed(e.Amount));
            }
        }

        // ---------- підсумок ----------

        public MatchResult Result { get { return _e.Result; } }

        /// <summary>Підсумок матчу рядками: переможець, як закінчився, скільки дій, ажіотаж.</summary>
        public List<string> ResultLines()
        {
            var res = new List<string>();
            var r = _e.Result;
            if (r == null) return res;
            res.Add(RingLabels.WinnerName(_t, r.Winner));
            res.Add(RingLabels.EndKind(_t, r.Kind));
            res.Add(_t.Format("match.result.rounds", r.Rounds));
            res.Add(_t.Format("match.result.hype", r.FinalHype, MatchRules.MaxHype));
            return res;
        }
    }
}
