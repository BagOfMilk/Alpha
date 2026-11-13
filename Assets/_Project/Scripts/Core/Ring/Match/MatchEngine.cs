using System;
using System.Collections.Generic;
using Game.Core.Randomness;

namespace Game.Core.Ring.Match
{
    /// <summary>
    /// Рушій пошагового матчу — ланцюг ініціативи (рішення власника 08.11.2026, TRPG_DESIGN §4.5). Чистий C#, без сцени: UI і ШІ
    /// ходять одним API. Діє той, у кого ініціатива: 10+ — ініціатива лишається; 7–9 — лишається з ускладненням
    /// (<see cref="ApplyComplication"/>); 6 і менше — переходить до суперника. Старт — Обмін (2d6 + Техніка); реверс 7–9 — знову
    /// Обмін. Гальмо ланцюга — «Сили». Потік дії: Perform -> (React) -> PendingRoll -> Commit(докрутка); кожен виклик повертає нові
    /// події журналу.
    /// </summary>
    public sealed class MatchEngine
    {
        private static readonly MatchActionId[] ActionOrder =
        {
            MatchActionId.Strike, MatchActionId.Throw, MatchActionId.TopRope, MatchActionId.Taunt, MatchActionId.Dirty,
            MatchActionId.Submission, MatchActionId.Pin, MatchActionId.Finisher, MatchActionId.Desperate,
            MatchActionId.LeaveRing, MatchActionId.ReturnToRing, MatchActionId.Weapon, MatchActionId.GetUp,
            MatchActionId.Recover, MatchActionId.Ally, MatchActionId.CatchBreath
        };

        private readonly MatchSetup _setup;
        private readonly IDiceRoller _dice;
        private readonly MatchState _s = new MatchState();
        private readonly HashSet<int> _flourished = new HashSet<int>();
        private List<MatchEvent> _new = new List<MatchEvent>();
        private MatchEvent _cur;
        private int _extraDamage;
        private bool _repeatPending;
        private MoveHint _hint = MoveHint.Default;
        // поточна дія: модифікатор, показаний до кидка (його ж і кидаємо), постановка, бік і місце в ланцюгу
        private bool _inAction;
        private int _actionMod;
        private MoveStaging _staging;
        private MatchActionId _actionId;
        private MatchSide _actionSide;
        private int _chainIndex;
        // пара була за рингом, коли дія почалась (відлік рахує лише такі дії)
        private bool _actionOutside;

        public MatchEngine(MatchSetup setup, IDiceRoller dice)
        {
            if (setup == null) throw new ArgumentNullException("setup");
            if (dice == null) throw new ArgumentNullException("dice");
            _setup = setup;
            _dice = dice;
            _s.Player = NewFighter(MatchSide.Player, setup.Player, setup.PlayerMods, setup.PlayerStartMomentum);
            _s.Rival = NewFighter(MatchSide.Rival, setup.Rival, setup.RivalMods, setup.RivalStartMomentum);
            _s.Hype = Clamp(setup.StartHype, 0, MatchRules.MaxHype);
            Start();
        }

        public MatchState State { get { return _s; } }
        public MatchPhase Phase { get; private set; }
        public PendingRoll PendingRoll { get; private set; }
        public PendingReaction PendingReaction { get; private set; }
        public MatchResult Result { get; private set; }
        public bool IsOver { get { return Phase == MatchPhase.Over; } }
        /// <summary>У кого ініціатива (той і діє).</summary>
        public MatchSide CurrentActor { get { return _s.CurrentActor; } }
        /// <summary>Номер наступної дії матчу.</summary>
        public int Round { get { return _s.Round; } }
        /// <summary>Скільки дій поспіль зробив нинішній власник ініціативи.</summary>
        public int Chain { get { return _s.Chain; } }
        public MatchSetup Setup { get { return _setup; } }
        /// <summary>Зараз хід того, хто може вжити «промінь надії» (для кнопки в UI).</summary>
        public bool HopeSpotAvailable { get { return Phase == MatchPhase.AwaitAction && Describe(MatchActionId.HopeSpot).Available; } }

        public static MatchSide Opp(MatchSide s) { return s == MatchSide.Player ? MatchSide.Rival : MatchSide.Player; }

        // ---------- старт ----------

        private static Fighter NewFighter(MatchSide side, WrestlerStats stats, MatchModifiers mods, int momentum)
        {
            if (stats == null) throw new ArgumentException("У матчі немає борця: " + side);
            return new Fighter
            {
                Side = side,
                Stats = stats,
                Momentum = Clamp(momentum, 0, MatchRules.MaxMomentum),
                Stamina = MatchRules.StartStamina,
                ReversalTokens = MatchRules.BaseReversalTokens + Math.Max(0, mods == null ? 0 : mods.ExtraReversalTokens)
            };
        }

        private void Start()
        {
            var first = RollExchange();
            _s.FirstActor = first;
            _s.CurrentActor = first;
            _s.Chain = 0;
            Phase = MatchPhase.AwaitAction;
            _new = new List<MatchEvent>();
        }

        /// <summary>Обмін (§4.6): обидва 2d6 + Техніка; рівно — у кого більший імпульс, далі — гравець. Повертає переможця.</summary>
        private MatchSide RollExchange()
        {
            int p = TwoD6.Roll(_dice, "m:ex:p");
            int r = TwoD6.Roll(_dice, "m:ex:r");
            int pt = p + _s.Player.Stats.Get(RingAttr.Technique);
            int rt = r + _s.Rival.Stats.Get(RingAttr.Technique);
            MatchSide w;
            if (pt != rt) w = pt > rt ? MatchSide.Player : MatchSide.Rival;
            else if (_s.Player.Momentum != _s.Rival.Momentum) w = _s.Player.Momentum > _s.Rival.Momentum ? MatchSide.Player : MatchSide.Rival;
            else w = MatchSide.Player;
            var ev = Ev(MatchEventKind.Exchange, w, MatchActionId.CatchBreath);
            ev.HasRoll = true; ev.Roll = p; ev.Total = pt; ev.Modifier = pt - p; ev.OtherTotal = rt;
            return w;
        }

        // ---------- доступні дії ----------

        public IReadOnlyList<ActionOption> GetAvailableActions()
        {
            var list = new List<ActionOption>();
            for (int i = 0; i < ActionOrder.Length; i++) list.Add(Describe(ActionOrder[i]));
            return list;
        }

        public ActionOption Describe(MatchActionId id) { return Describe(id, 0); }

        /// <summary>Те саме з додатком до кидка від прийому (кар'єра: травма зони, бонус прийому) — шанси показують його.</summary>
        public ActionOption Describe(MatchActionId id, int extraRollMod)
        {
            if (Phase != MatchPhase.AwaitAction)
                return new ActionOption { Id = id, Cost = MatchRules.Cost(id), IsBig = MatchRules.IsBig(id), Reason = UnavailableReason.WrongPhase };
            var p = Preview(_s.CurrentActor, id, extraRollMod);
            return new ActionOption
            {
                Id = p.Id, Cost = p.Cost, IsBig = p.IsBig, HasAttr = p.HasAttr, Attr = p.Attr, Available = p.Available,
                Reason = p.Reason, HasRoll = p.HasRoll, RollIsOpponents = p.RollIsOpponents, Modifier = p.Modifier,
                Winded = p.Winded, Chances = p.Chances36
            };
        }

        /// <summary>
        /// Чисте прев'ю дії для БУДЬ-ЯКОЇ сторони (без кубика, без мутацій): ті самі перевірки, модифікатор і шанси, що в Perform.
        /// Не чий хід або фаза не «чекаю дію» — Available=false, Reason=WrongPhase, але Modifier і Chances36 пораховані
        /// (показ «хід суперника як свій»).
        /// </summary>
        public ActionPreview Preview(MatchSide side, MatchActionId id, int extraRollMod = 0)
        {
            var me = _s.Get(side); var opp = _s.Get(Opp(side));
            var p = new ActionPreview
            {
                Side = side, Id = id, Cost = MatchRules.Cost(id), IsBig = MatchRules.IsBig(id),
                StaminaBefore = me.Stamina, MissPassesInitiative = id != MatchActionId.HopeSpot
            };
            p.StaminaAfter = id == MatchActionId.CatchBreath
                ? Math.Min(MatchRules.MaxStamina, me.Stamina + MatchRules.CatchBreathStamina)
                : Math.Max(0, me.Stamina - p.Cost);
            RingAttr attr;
            if (AttrOf(id, me, out attr)) { p.HasAttr = true; p.Attr = attr; }
            p.Reason = Check(id, me, opp);
            if (p.Reason == UnavailableReason.None && (Phase != MatchPhase.AwaitAction || side != _s.CurrentActor))
                p.Reason = UnavailableReason.WrongPhase;
            p.Available = p.Reason == UnavailableReason.None;
            if (id == MatchActionId.Pin)
            {
                p.HasRoll = true; p.RollIsOpponents = true;
                p.Modifier = KickoutMod(opp, 0);
                p.Chances36 = TwoD6.ChancesOf36(p.Modifier);
            }
            else if (p.HasAttr)
            {
                p.HasRoll = true;
                p.Winded = IsWinded(id, me);
                p.Modifier = RollModifier(id, me) + extraRollMod;
                p.Chances36 = TwoD6.ChancesOf36(p.Modifier);
            }
            return p;
        }

        /// <summary>Прев'ю всіх дій сторони в порядку списку дій; «промінь надії» — в кінці, якщо правила його дозволяють.</summary>
        public IReadOnlyList<ActionPreview> PreviewAll(MatchSide side)
        {
            var list = new List<ActionPreview>();
            for (int i = 0; i < ActionOrder.Length; i++) list.Add(Preview(side, ActionOrder[i]));
            var hope = Preview(side, MatchActionId.HopeSpot);
            if (hope.Reason == UnavailableReason.None || hope.Reason == UnavailableReason.WrongPhase) list.Add(hope);
            return list;
        }

        private UnavailableReason Check(MatchActionId id, Fighter me, Fighter opp)
        {
            if (id == MatchActionId.CatchBreath) return UnavailableReason.None;
            UnavailableReason r = UnavailableReason.None;
            bool noDq = (_setup.Stipulations & MatchStipulations.NoDq) != 0;
            switch (id)
            {
                case MatchActionId.GetUp:
                    if (me.Posture != Posture.Down) r = UnavailableReason.NotLying;
                    break;
                case MatchActionId.HopeSpot:
                    if (me.HopeUsed) r = UnavailableReason.AlreadyUsed;
                    else if (!_setup.HopeSpotEnabled || me.Damage < MatchRules.HopeMinDamage
                             || me.Damage - opp.Damage < MatchRules.HopeGap) r = UnavailableReason.NotFarBehind;
                    else if (_s.Outside) r = UnavailableReason.NotInRing;
                    break;
                case MatchActionId.Recover:
                    if (me.Damage <= 0) r = UnavailableReason.NothingToRecover;
                    break;
                case MatchActionId.Ally:
                    if (me.Side != MatchSide.Player || !_setup.PlayerHasAlly) r = UnavailableReason.NoAlly;
                    else if ((_setup.Stipulations & MatchStipulations.NoAlly) != 0) r = UnavailableReason.AllyForbidden;
                    else if (_s.AllyUsed) r = UnavailableReason.AlreadyUsed;
                    else if (_s.Outside) r = UnavailableReason.NotInRing;
                    break;
                default:
                    if (!me.IsUpright) { r = UnavailableReason.ActorDown; break; }
                    switch (id)
                    {
                        case MatchActionId.Strike:
                        case MatchActionId.Throw:
                            if (!opp.IsUpright) r = UnavailableReason.TargetNotStanding;
                            break;
                        case MatchActionId.Taunt:
                            if (opp.Posture == Posture.Standing) r = UnavailableReason.TargetNotDownOrStunned;
                            break;
                        case MatchActionId.TopRope:
                            if (_s.Outside) r = UnavailableReason.NotInRing;
                            break;
                        case MatchActionId.Submission:
                            if (_s.Outside) r = UnavailableReason.NotInRing;
                            else if (opp.Posture != Posture.Down) r = UnavailableReason.TargetNotDown;
                            else if (opp.Damage < MatchRules.PinMinDamage) r = UnavailableReason.TargetNotHurt;
                            break;
                        case MatchActionId.Pin:
                            if (_s.Outside) r = UnavailableReason.NotInRing;
                            else if (opp.Posture != Posture.Down) r = UnavailableReason.TargetNotDown;
                            else if (_s.HasLastAction && _s.LastAction == MatchActionId.Pin && me.Side == _s.CurrentActor) r = UnavailableReason.JustPinned;
                            else if (opp.Damage < MatchRules.PinMinDamage) r = UnavailableReason.TargetNotHurt;
                            break;
                        case MatchActionId.LeaveRing:
                            if (_s.Outside) r = UnavailableReason.AlreadyOutside;
                            break;
                        case MatchActionId.ReturnToRing:
                            if (!_s.Outside) r = UnavailableReason.NotOutside;
                            break;
                        case MatchActionId.Weapon:
                            if (!_s.Outside) r = UnavailableReason.NotOutside;
                            else if (!noDq) r = UnavailableReason.NeedNoDq;
                            break;
                        case MatchActionId.Finisher:
                            if (_s.Outside) r = UnavailableReason.NotInRing;
                            else if (me.Momentum < MatchRules.FinisherMomentum) r = UnavailableReason.NeedImpulse;
                            else if (_s.Hype < MatchRules.FinisherHype) r = UnavailableReason.NeedHype;
                            else if (opp.Damage < MatchRules.FinisherTargetDamage) r = UnavailableReason.TargetNotHurt;
                            else if (_s.Round < MatchRules.FinisherMinRound && !_setup.NoFinisherMinimum) r = UnavailableReason.TooEarly;
                            break;
                        case MatchActionId.Desperate:
                            if (me.DesperateUsed) r = UnavailableReason.AlreadyUsed;
                            else if (me.Momentum > MatchRules.DesperateMaxMomentum) r = UnavailableReason.ImpulseTooHigh;
                            else if (me.Damage < MatchRules.DesperateMinDamage) r = UnavailableReason.NotExhausted;
                            break;
                    }
                    break;
            }
            if (r == UnavailableReason.None && MatchRules.NeedsFullStamina(id) && me.Stamina < MatchRules.Cost(id))
                r = UnavailableReason.NotEnoughStamina;
            return r;
        }

        /// <summary>Атрибут кидка дії (для кар'єри: каталог прийомів показує атрибут за дією).</summary>
        public static bool AttrOf(MatchActionId id, Fighter me, out RingAttr attr)
        {
            attr = RingAttr.Strength;
            switch (id)
            {
                case MatchActionId.Strike:
                case MatchActionId.Throw:
                case MatchActionId.LeaveRing:
                case MatchActionId.Weapon:
                    attr = RingAttr.Strength; return true;
                case MatchActionId.Submission:
                    attr = RingAttr.Technique; return true;
                case MatchActionId.TopRope:
                    attr = RingAttr.Agility; return true;
                case MatchActionId.Taunt:
                case MatchActionId.Dirty:
                    attr = RingAttr.Charisma; return true;
                case MatchActionId.Finisher:
                    attr = me.Stats.FinisherAttr; return true;
                case MatchActionId.Desperate:
                case MatchActionId.GetUp:
                case MatchActionId.HopeSpot:
                    attr = RingAttr.Stamina; return true;
            }
            return false;
        }

        // ---------- модифікатори ----------

        private static readonly MatchModifiers NoMods = new MatchModifiers();

        private MatchModifiers Mods(MatchSide s)
        {
            var m = s == MatchSide.Player ? _setup.PlayerMods : _setup.RivalMods;
            return m ?? NoMods;
        }

        /// <summary>Штраф ушкодження 0/1/2 з урахуванням зсуву порогів.</summary>
        public int Threshold(Fighter f)
        {
            int shift = Mods(f.Side).DamageThresholdShift;
            if (f.Damage >= MatchRules.EdgeDamage + shift) return 2;
            if (f.Damage >= MatchRules.ExhaustedDamage + shift) return 1;
            return 0;
        }

        /// <summary>Пізня стадія (P2-1): з <see cref="MatchRules.LateStageRound"/>-ї дії штраф <see cref="MatchRules.LateStagePenalty"/>, далі +1 за кожні <see cref="MatchRules.LateStageStep"/> дій.</summary>
        private int Late()
        {
            if (_s.Round < MatchRules.LateStageRound) return 0;
            return MatchRules.LateStagePenalty + (_s.Round - MatchRules.LateStageRound) / MatchRules.LateStageStep;
        }

        private static bool IsWinded(MatchActionId id, Fighter me) { return me.Stamina <= 0 && id != MatchActionId.HopeSpot; }

        private int RollModifier(MatchActionId id, Fighter me)
        {
            RingAttr a; AttrOf(id, me, out a);
            int m = me.Stats.Get(a) - Threshold(me);
            if (id == MatchActionId.TopRope || id == MatchActionId.Desperate) m += Mods(me.Side).RiskyMoveBonus;
            if (IsWinded(id, me)) m -= MatchRules.WindedPenalty;
            return m;
        }

        /// <summary>«Вирватися»: Витривалість - вирвані - поріг - штраф прийому - пізня стадія + KickOutBonus.</summary>
        private int KickoutMod(Fighter def, int penalty)
        {
            // «Герой залу»: при ажіотажі 7+ зал тягне героя — +1 до «Вирватися».
            int crowd = Mods(def.Side).HeroOfCrowd && _s.Hype >= MatchRules.HeroOfCrowdHype ? 1 : 0;
            int fresh = def.PinsFaced == 0 ? MatchRules.FirstPinBonus : 0;
            return MatchRules.DefenseBase + fresh + def.Stats.Get(RingAttr.Stamina) - def.KickoutsDone - Threshold(def) - penalty - Late() + Mods(def.Side).KickOutBonus + crowd;
        }

        private int EndureMod(Fighter def, int penalty)
        {
            int fresh = def.HoldsFaced == 0 ? MatchRules.FirstHoldBonus : 0;
            return MatchRules.DefenseBase + fresh + def.Stats.Get(RingAttr.Stamina) - Threshold(def) - penalty - Late();
        }

        /// <summary>
        /// Модифікатор «Вирватися» борця <paramref name="defender"/> у цю мить, зі штрафом прийому <paramref name="penalty"/>
        /// (утримання — 0, фінішер — <see cref="MatchRules.FinisherStrongPinPenalty"/> / <see cref="MatchRules.FinisherPartialPinPenalty"/>).
        /// Те саме число, що кидає утримання; читач для показу шансів і сценарію демо (RingDemoScript).
        /// </summary>
        public int KickoutModifier(MatchSide defender, int penalty) { return KickoutMod(_s.Get(defender), penalty); }

        /// <summary>
        /// Модифікатор «Терпіти» (больовий) у цю мить; штраф — <see cref="MatchRules.SubmissionStrongPenalty"/> за 10+ нападника,
        /// <see cref="MatchRules.SubmissionPartialPenalty"/> за 7–9. Те саме число, що кидає больовий.
        /// </summary>
        public int EndureModifier(MatchSide defender, int penalty) { return EndureMod(_s.Get(defender), penalty); }

        private int ReverseMod(Fighter def, bool vsFinisher)
        {
            return def.Stats.Get(RingAttr.Technique) - Threshold(def) + (vsFinisher ? MatchRules.ReverseVsFinisherPenalty : 0);
        }

        // ---------- публічні команди ----------

        public IReadOnlyList<MatchEvent> Perform(MatchActionId id) { return Perform(id, null); }

        /// <summary>Дія з підказкою прийому (кар'єра): ключ для повтору й показу, додаток до кидка, ажіотаж за чистий успіх, постановка.</summary>
        public IReadOnlyList<MatchEvent> Perform(MatchActionId id, MoveHint hint)
        {
            RequirePhase(MatchPhase.AwaitAction);
            var h = hint ?? MoveHint.Default;
            var opt = Describe(id, h.RollMod);
            if (!opt.Available) throw new InvalidOperationException("Дія " + id + " недоступна: " + opt.Reason);
            BeginCall();
            _hint = h;
            var me = _s.Actor; var opp = _s.Other;
            _inAction = true;
            _actionId = id; _actionSide = me.Side;
            _actionMod = opt.Modifier;
            _staging = h.Staging ?? (id == MatchActionId.TopRope ? MoveStaging.Corner : MoveStaging.Center);
            _actionOutside = _s.Outside;
            _s.Chain++;
            _chainIndex = _s.Chain;
            _extraDamage = 0;

            if (id == MatchActionId.CatchBreath)
            {
                Ev(MatchEventKind.Action, me.Side, id);
                SetStamina(me, me.Stamina + MatchRules.CatchBreathStamina);
                PassInitiative();
                EndAction();
                return EndCall();
            }

            me.Stamina = Math.Max(0, me.Stamina - MatchRules.Cost(id));
            if (id == MatchActionId.Finisher) me.Momentum -= MatchRules.FinisherMomentum;
            if (id == MatchActionId.Desperate) me.DesperateUsed = true;
            if (id == MatchActionId.HopeSpot) me.HopeUsed = true;
            if (id == MatchActionId.LeaveRing) me.LeftRing = true;
            _repeatPending = _s.HasLastAction && _s.LastAction == id && _s.LastMoveKey == _hint.Key;
            _s.HasLastAction = true; _s.LastAction = id; _s.LastMoveKey = _hint.Key;

            switch (id)
            {
                case MatchActionId.Pin:
                case MatchActionId.Recover:
                case MatchActionId.Ally:
                case MatchActionId.ReturnToRing:
                    Ev(MatchEventKind.Action, me.Side, id);
                    if (_repeatPending) Hype(-1);
                    _repeatPending = false;
                    ApplyNoRoll(id, me, opp);
                    EndAction();
                    break;
                default:
                    if (MatchRules.IsBig(id)) BeginReaction(id, me, opp);
                    else BeginRoll(id, me);
                    break;
            }
            return EndCall();
        }

        /// <summary>«Перевести подих»: +«Сили», ініціатива переходить до суперника.</summary>
        public IReadOnlyList<MatchEvent> EndTurn() { return Perform(MatchActionId.CatchBreath); }

        public IReadOnlyList<MatchEvent> React(ReactionChoice choice)
        {
            RequirePhase(MatchPhase.AwaitReaction);
            BeginCall();
            var pr = PendingReaction; PendingReaction = null;
            var att = _s.Get(pr.Attacker); var def = _s.Get(pr.Defender);
            if (choice == ReactionChoice.Sell) { Sell(def, pr.Action); BeginRoll(pr.Action, att); return EndCall(); }

            def.ReversalTokens--;
            int roll = TwoD6.Roll(_dice, "m:rev:" + def.Side);
            int mod = pr.ReverseModifier; int total = roll + mod; var tier = TwoD6.TierOf(total);
            var ev = Ev(MatchEventKind.Reverse, def.Side, pr.Action);
            ev.HasRoll = true; ev.Roll = roll; ev.Modifier = mod; ev.Total = total; ev.Tier = tier;
            Fx(MatchEffectKind.Token, def.Side, -1);
            if (tier == RollTier.Miss) { _extraDamage = 1; BeginRoll(pr.Action, att); return EndCall(); }
            _repeatPending = false;
            Phase = MatchPhase.AwaitAction;
            if (tier == RollTier.Strong)
            {
                // прийом твій: нападник лежить, ініціатива — захиснику (§4.7)
                GoDown(att);
                PassInitiative();
            }
            else
            {
                // прийом зірвано: ніхто не має переваги — знову Обмін
                GiveInitiative(RollExchange());
            }
            EndAction();
            return EndCall();
        }

        public IReadOnlyList<MatchEvent> Commit() { return Commit(0); }

        /// <summary>Застосувати кидок; boostPoints — витрата імпульсу, що піднімає ступінь (0 — без докрутки).</summary>
        public IReadOnlyList<MatchEvent> Commit(int boostPoints)
        {
            RequirePhase(MatchPhase.AwaitBoost);
            var pr = PendingRoll;
            if (boostPoints < 0) throw new ArgumentException("Докрутка не може бути від'ємною");
            if (boostPoints > 0 && pr.PointsToPartial != boostPoints && pr.PointsToStrong != boostPoints)
                throw new ArgumentException("Докрутка " + boostPoints + " не піднімає ступінь");
            BeginCall();
            var me = _s.Get(pr.Actor); var opp = _s.Get(Opp(pr.Actor));
            me.Momentum -= boostPoints;
            int total = pr.Total + boostPoints;
            var tier = TwoD6.TierOf(total);
            bool improvised = false;
            if (tier == RollTier.Miss && Mods(me.Side).Improvise && !me.ImproviseUsed
                && pr.Action != MatchActionId.Finisher && pr.Action != MatchActionId.HopeSpot)
            {
                tier = RollTier.Partial; me.ImproviseUsed = true; improvised = true;
            }
            PendingRoll = null; Phase = MatchPhase.AwaitAction;
            var ev = Ev(MatchEventKind.Action, me.Side, pr.Action);
            ev.HasRoll = true; ev.Roll = pr.Roll; ev.Modifier = pr.Modifier; ev.Total = total; ev.Tier = tier; ev.BoostSpent = boostPoints;
            ev.Perk = improvised ? PerkCatalog.Improvise : null;
            if (boostPoints > 0) Fx(MatchEffectKind.Momentum, me.Side, -boostPoints);
            if (_repeatPending) { Hype(-1); _repeatPending = false; }
            ApplyRolled(pr.Action, tier, me, opp);
            if (tier == RollTier.Strong && _hint.HypeOnStrong > 0 && !IsOver) Hype(_hint.HypeOnStrong);
            // 6 і менше — ініціатива переходить (§4.5); промінь надії не підводить ніколи
            if (tier == RollTier.Miss && pr.Action != MatchActionId.HopeSpot && _s.CurrentActor == me.Side) PassInitiative();
            EndAction();
            return EndCall();
        }

        /// <summary>
        /// QTE — лише видовище (рішення власника 08.11.2026, §4.12): вдалий натиск під час прийому гравця дає +1 ажіотажу, раз на подію.
        /// Кидків, ступенів, ушкоджень і ініціативи не чіпає — кубика не торкається. Подія — Action гравця з кидком не гірше 7–9;
        /// інше (або повтор) ігнорується. Працює й після кінця матчу (останній прийом): тоді оновлює FinalHype.
        /// </summary>
        public IReadOnlyList<MatchEvent> RegisterFlourish(int eventSeq)
        {
            var empty = new List<MatchEvent>();
            if (eventSeq < 0 || eventSeq >= _s.Log.Count || _flourished.Contains(eventSeq)) return empty;
            var src = _s.Log[eventSeq];
            if (src.Kind != MatchEventKind.Action || !src.HasRoll || src.Tier == RollTier.Miss || src.Actor != MatchSide.Player) return empty;
            _flourished.Add(eventSeq);
            var saveNew = _new; var saveCur = _cur; bool saveIn = _inAction;
            _new = new List<MatchEvent>(); _cur = null; _inAction = false;
            var ev = Ev(MatchEventKind.Flourish, src.Actor, src.Action);
            ev.MoveKey = src.MoveKey; ev.Staging = src.Staging; ev.ChainIndex = src.ChainIndex; ev.Round = src.Round;
            Hype(1);
            if (IsOver && Result != null) Result.FinalHype = _s.Hype;
            var made = _new;
            _new = saveNew; _cur = saveCur; _inAction = saveIn;
            return made;
        }

        // ---------- дія нападника ----------

        private void BeginReaction(MatchActionId id, Fighter att, Fighter def)
        {
            if (def.ReversalTokens <= 0 || def.Posture == Posture.Stunned)
            {
                Sell(def, id);
                BeginRoll(id, att);
                return;
            }
            int mod = ReverseMod(def, id == MatchActionId.Finisher);
            PendingReaction = new PendingReaction
            {
                Attacker = att.Side, Defender = def.Side, Action = id,
                ReverseModifier = mod, ReverseChances = TwoD6.ChancesOf36(mod)
            };
            Phase = MatchPhase.AwaitReaction;
        }

        private void Sell(Fighter def, MatchActionId id)
        {
            Ev(MatchEventKind.Sell, def.Side, id);
            AddMomentum(def, 1);
            if (Mods(def.Side).HeroOfCrowd && _s.Hype >= MatchRules.HeroOfCrowdHype) AddMomentum(def, 1);
            if (RoleOf(def.Side) == RingPath.Face) Hype(1);
            if (def.Side == MatchSide.Player) _s.PlayerSells++;
        }

        private void BeginRoll(MatchActionId id, Fighter me)
        {
            int roll = TwoD6.Roll(_dice, "m:act:" + id + ":" + me.Side);
            int mod = _actionMod;
            int total = roll + mod; var tier = TwoD6.TierOf(total);
            int toPartial = tier == RollTier.Miss ? 7 - total : 0;
            int toStrong = tier != RollTier.Strong ? 10 - total : 0;
            var pr = new PendingRoll
            {
                Actor = me.Side, Action = id, Roll = roll, Modifier = mod, Total = total, Tier = tier, Impulse = me.Momentum
            };
            if (toPartial > 0 && toPartial <= me.Momentum) pr.PointsToPartial = toPartial;
            if (toStrong > 0 && toStrong <= me.Momentum) pr.PointsToStrong = toStrong;
            pr.CanBoost = pr.PointsToPartial > 0 || pr.PointsToStrong > 0;
            PendingRoll = pr; Phase = MatchPhase.AwaitBoost;
        }

        private void ApplyRolled(MatchActionId id, RollTier t, Fighter me, Fighter opp)
        {
            bool strong = t == RollTier.Strong, partial = t == RollTier.Partial, miss = t == RollTier.Miss;
            int extra = miss ? 0 : _extraDamage;
            switch (id)
            {
                case MatchActionId.Strike:
                    if (miss) { Dmg(me, 1); break; }
                    Dmg(opp, (strong ? 2 : 1) + extra);
                    if (strong)
                    {
                        Hype(1);
                        if (opp.Posture == Posture.Standing && opp.Damage >= MatchRules.ExhaustedDamage)
                        {
                            opp.Posture = Posture.Stunned; Fx(MatchEffectKind.Stunned, opp.Side, 1);
                        }
                    }
                    break;
                case MatchActionId.Throw:
                    if (miss) { GoDown(me); break; }
                    Dmg(opp, (strong ? 3 : 2) + extra); GoDown(opp);
                    if (strong) Hype(1);
                    break;
                case MatchActionId.TopRope:
                    if (miss) { Dmg(me, 2); GoDown(me); break; }
                    if (strong) { Dmg(opp, 4 + extra); GoDown(opp); Hype(2); }
                    else Dmg(opp, 2 + extra);
                    break;
                case MatchActionId.Taunt:
                    if (strong) Hype(2);
                    else if (partial) Hype(1);
                    else Dmg(me, 1);
                    break;
                case MatchActionId.Dirty:
                    ApplyDirty(t, me, opp);
                    break;
                case MatchActionId.Submission:
                    if (miss) break;   // вирвався: ініціатива переходить за загальним правилом
                    ResolveEndure(me, opp, strong ? MatchRules.SubmissionStrongPenalty : MatchRules.SubmissionPartialPenalty);
                    break;
                case MatchActionId.Finisher:
                    if (miss) { GoDown(me); break; }
                    Dmg(opp, (strong ? 4 : 3) + extra); GoDown(opp);
                    ResolvePin(me, opp, strong ? MatchRules.FinisherStrongPinPenalty : MatchRules.FinisherPartialPinPenalty);
                    break;
                case MatchActionId.Desperate:
                    if (miss) { Dmg(me, 2); GoDown(me); break; }
                    if (strong) { Dmg(opp, 3 + extra); GoDown(opp); }
                    else Dmg(opp, 2 + extra);
                    break;
                case MatchActionId.HopeSpot:
                    // Надія не підводить цілком: навіть невдалий кидок дає «7–9». Матч закінчити не може — жодного утримання.
                    Dmg(opp, (strong ? 4 : 3) + extra); GoDown(opp);
                    Hype(strong ? 3 : 2); AddMomentum(me, strong ? 2 : 1);
                    if (me.Posture == Posture.Down) { me.Posture = Posture.Standing; Fx(MatchEffectKind.Stand, me.Side, 1); }
                    break;
                case MatchActionId.GetUp:
                    if (miss) break;
                    me.Posture = Posture.Standing; Fx(MatchEffectKind.Stand, me.Side, 1);
                    break;
                case MatchActionId.LeaveRing:
                    // викинув суперника за канати й пішов слідом; 10+ — той падає на підлогу. Провал — контратака на рингу.
                    if (miss) { Dmg(me, 1); break; }
                    SetOutside(true, me.Side);
                    Dmg(opp, 1 + extra);
                    if (strong) { GoDown(opp); Hype(1); }
                    break;
                case MatchActionId.Weapon:
                    // стілець чи стіл за рингом (лише «без DQ»): 10+ — суперник лежить, зал вибухає.
                    if (miss) { Dmg(me, 1); break; }
                    if (strong) { Dmg(opp, 3 + extra); GoDown(opp); Hype(2); }
                    else Dmg(opp, 2 + extra);
                    break;
            }
            if (partial && !IsOver) ApplyComplication(id, me, opp);
            // «+1 імпульсу за чистий успіх»; відчайдушний дає +3 замість цього
            if (strong) AddMomentum(me, id == MatchActionId.Desperate ? 3 : 1);
        }

        /// <summary>
        /// Ускладнення 7–9 (§4.6): ініціатива лишається, але дія коштує. В ітерації 1 вибір автоматичний за дією; в ітерації 2 тут
        /// стане меню «обери одне з двох». Брудний (попередження), больовий і фінішер (слабший штраф захиснику) платять у своїх правилах.
        /// </summary>
        private void ApplyComplication(MatchActionId id, Fighter me, Fighter opp)
        {
            switch (id)
            {
                case MatchActionId.Strike:
                case MatchActionId.LeaveRing:
                    if (me.Stamina > 0) { SetStamina(me, me.Stamina - 1); }   // «−1 сила»
                    break;
                case MatchActionId.Throw:
                case MatchActionId.TopRope:
                case MatchActionId.Desperate:
                case MatchActionId.Weapon:
                    Dmg(me, 1);   // влучив, але ушкодив себе
                    break;
                case MatchActionId.Taunt:
                    if (opp.Posture != Posture.Standing) { opp.Posture = Posture.Standing; Fx(MatchEffectKind.Stand, opp.Side, 1); }   // він встає
                    break;
                case MatchActionId.GetUp:
                    if (_s.CurrentActor == me.Side) PassInitiative();   // встав, але ініціатива в нього
                    break;
            }
        }

        private void ApplyDirty(RollTier t, Fighter me, Fighter opp)
        {
            var role = RoleOf(me.Side);
            if (me.Side == MatchSide.Player) _s.PlayerDirtyMoves++;
            if (role == RingPath.Face) Hype(-2);
            bool warned = false;
            if (t != RollTier.Miss) Dmg(opp, 2 + _extraDamage);
            bool sees = !_s.RefereeDistracted;
            // «Брудна гра»: раз за матч рефері дивиться вбік — ні попередження, ні мінуса ажіотажу.
            bool covered = Mods(me.Side).DirtyGame && !me.DirtyCoverUsed && (t == RollTier.Miss || (t == RollTier.Partial && !Mods(me.Side).DirtyNoWarningOnPartial));
            if (covered) { me.DirtyCoverUsed = true; sees = false; }
            if (t == RollTier.Partial && sees && !Mods(me.Side).DirtyNoWarningOnPartial) warned = Warn(me);
            else if (t == RollTier.Miss) { if (sees) warned = Warn(me); if (!covered) Hype(-1); }
            if (role == RingPath.Heel && t != RollTier.Miss && !warned) Hype(1);
        }

        /// <summary>Попередження; true, якщо видане (умова «без дискваліфікації» їх скасовує).</summary>
        private bool Warn(Fighter f)
        {
            if ((_setup.Stipulations & MatchStipulations.NoDq) != 0) return false;
            f.Warnings++; Fx(MatchEffectKind.Warning, f.Side, 1);
            if (f.Warnings >= MatchRules.WarningsToDq) End(WinnerOf(Opp(f.Side)), MatchEndKind.Disqualification);
            return true;
        }

        private void ApplyNoRoll(MatchActionId id, Fighter me, Fighter opp)
        {
            switch (id)
            {
                case MatchActionId.Pin: ResolvePin(me, opp, 0); break;
                case MatchActionId.Recover: Dmg(me, -1); Hype(-1); PassInitiative(); break;
                case MatchActionId.Ally:
                    _s.AllyUsed = true; _s.PlayerUsedAlly = true; _s.RefereeDistracted = true;
                    Fx(MatchEffectKind.RefereeDistracted, me.Side, 1);
                    break;
                case MatchActionId.ReturnToRing:
                    // закотив суперника в ринг і повернувся сам: відлік скинуто, ініціатива лишається (можна накрити)
                    SetOutside(false, me.Side);
                    break;
            }
        }

        /// <summary>Пара змінила місце (склейка в показі); повернення в ринг скидає відлік.</summary>
        private void SetOutside(bool outside, MatchSide who)
        {
            if (_s.Outside == outside) return;
            _s.Outside = outside;
            _s.OutsideCount = 0;
            Fx(MatchEffectKind.Place, who, outside ? 1 : 0);
        }

        /// <summary>
        /// Відлік за рингом (§4.9): дія, що почалась і скінчилась за рингом, — +3. На 10 програє той, хто лежить (інший «встиг на
        /// дев'ять»); лежать обидва чи стоять обидва — подвійний відлік. У матчі «без DQ» відліку немає.
        /// </summary>
        private void CountOutside()
        {
            if (!_actionOutside || !_s.Outside || IsOver) return;
            if ((_setup.Stipulations & MatchStipulations.NoDq) != 0) return;
            _s.OutsideCount += MatchRules.OutsideCountStep;
            Fx(MatchEffectKind.Count, _actionSide, MatchRules.OutsideCountStep);
            if (_s.OutsideCount < MatchRules.OutsideCountLimit) return;
            bool pDown = _s.Player.Posture == Posture.Down, rDown = _s.Rival.Posture == Posture.Down;
            if (pDown != rDown) End(pDown ? MatchWinner.Rival : MatchWinner.Player, MatchEndKind.CountOut);
            else End(MatchWinner.Draw, MatchEndKind.DoubleCountOut);
        }

        // ---------- утримання й больовий ----------

        /// <summary>
        /// «Вирватися» (§4.6): 10+ — на «раз»; 7–9 — на «два», ажіотаж +2 й імпульс +1. В обох випадках ініціатива переходить до
        /// захисника. 6 і менше — «три», поразка, якщо напарник не розірве рахунок; тоді ініціатива лишається в нападника, і накрити
        /// знову він може лише після іншого прийому. Перше накриття проти борця в матчі — з бонусом свіжого (MatchRules.FirstPinBonus).
        /// </summary>
        private void ResolvePin(Fighter att, Fighter def, int penalty)
        {
            int roll = TwoD6.Roll(_dice, "m:kick:" + def.Side);
            int mod = KickoutMod(def, penalty); int total = roll + mod; var tier = TwoD6.TierOf(total);
            def.PinsFaced++;
            // «Підкуплений суддя»: раз за матч рахунок зупиняється на «два» замість «трьох».
            string perk = null;
            if (tier == RollTier.Miss && Mods(def.Side).BribedRef && !def.BribeUsed) { tier = RollTier.Partial; def.BribeUsed = true; perk = PerkCatalog.BribedRef; }
            var ev = Ev(MatchEventKind.Kickout, def.Side, MatchActionId.Pin);
            ev.HasRoll = true; ev.Roll = roll; ev.Modifier = mod; ev.Total = total; ev.Tier = tier; ev.Perk = perk;
            // фінішер теж закінчується утриманням: «щойно накривав» — для обох шляхів
            _s.HasLastAction = true; _s.LastAction = MatchActionId.Pin;
            if (tier == RollTier.Miss)
            {
                if (def.Side == MatchSide.Player && _setup.PlayerHasAlly && !_s.AllyUsed
                    && (_setup.Stipulations & MatchStipulations.NoAlly) == 0)
                {
                    _s.AllyUsed = true; _s.PlayerUsedAlly = true;
                    Ev(MatchEventKind.Rescue, def.Side, MatchActionId.Ally);
                    return;
                }
                End(WinnerOf(att.Side), MatchEndKind.Pin);
                return;
            }
            def.KickoutsDone++;
            if (Mods(def.Side).Comeback && !def.ComebackGiven && def.KickoutsDone >= MatchRules.ComebackKickouts)
            {
                // Камбек: жетон реверсу й імпульс до порогу фінішера — зал піднімає героя.
                def.ComebackGiven = true; def.ReversalTokens++; Fx(MatchEffectKind.Token, def.Side, 1);
                if (def.Momentum < MatchRules.FinisherMomentum) AddMomentum(def, MatchRules.FinisherMomentum - def.Momentum);
            }
            if (tier == RollTier.Partial)
            {
                Hype(2); AddMomentum(def, 1);
                if (def.Side == MatchSide.Player) _s.PlayerKickoutsAtTwo++;
            }
            // Вирвався — ініціатива його, і на «раз», і на «два» (баланс 08.11.2026, ПЛЕЙСХОЛДЕР): після майже-перемоги — камбек, а не
            // нове накриття тим самим ланцюгом. Замір: з ініціативою в нападника після «два» чистий успіх гравця падав до 24 %, титул — до 30 %.
            if (_s.CurrentActor == att.Side) PassInitiative();
        }

        /// <summary>«Терпіти» (§4.6): 10+ — вирвався, ініціатива захиснику; 7–9 — тримається (ініціатива в нападника); інакше — здався.</summary>
        private void ResolveEndure(Fighter att, Fighter def, int penalty)
        {
            int roll = TwoD6.Roll(_dice, "m:endure:" + def.Side);
            int mod = EndureMod(def, penalty); int total = roll + mod; var tier = TwoD6.TierOf(total);
            def.HoldsFaced++;
            var ev = Ev(MatchEventKind.Endure, def.Side, MatchActionId.Submission);
            ev.HasRoll = true; ev.Roll = roll; ev.Modifier = mod; ev.Total = total; ev.Tier = tier;
            if (tier == RollTier.Miss) End(WinnerOf(att.Side), MatchEndKind.Submission);
            else if (tier == RollTier.Strong && _s.CurrentActor == att.Side) PassInitiative();
        }

        // ---------- ініціатива й кінець матчу ----------

        /// <summary>Ініціатива переходить до суперника: подія TurnEnd (Actor — хто втратив), ланцюг з нуля, рефері знову дивиться.</summary>
        private void PassInitiative()
        {
            if (IsOver) return;
            var a = _s.Actor;
            Ev(MatchEventKind.TurnEnd, a.Side, MatchActionId.CatchBreath);
            SwitchTo(Opp(a.Side));
        }

        /// <summary>Після Обміну: переможець бере ініціативу; якщо це той самий бік — ланцюг усе одно починається наново.</summary>
        private void GiveInitiative(MatchSide side)
        {
            if (IsOver) return;
            if (side != _s.CurrentActor) PassInitiative();
            else { _s.Chain = 0; _s.HasLastAction = false; }
        }

        private void SwitchTo(MatchSide side)
        {
            _s.CurrentActor = side;
            _s.Chain = 0;
            _s.HasLastAction = false;
            _s.RefereeDistracted = false;
            _s.InitiativeChanges++;
            var n = _s.Actor;
            // приголомшений приходить до тями, щойно ініціатива в нього
            if (n.Posture == Posture.Stunned) { n.Posture = Posture.Standing; Fx(MatchEffectKind.Stand, n.Side, 1); }
            Phase = MatchPhase.AwaitAction;
        }

        /// <summary>Дію розв'язано: захисник відновлює «Сили» після прийому, лічильник дій росте, ліміт — нічия (§4.11).</summary>
        private void EndAction()
        {
            CountOutside();
            _inAction = false;
            _hint = MoveHint.Default;
            if (IsOver) return;
            if (MatchRules.Cost(_actionId) > 0)
            {
                var def = _s.Get(Opp(_actionSide));
                def.Stamina = Math.Min(MatchRules.MaxStamina, def.Stamina + MatchRules.DefenderStaminaRegen);
            }
            // P2-1: підйом після нокдауну лишається дією (журнал, екран), але не рахується в довжину матчу.
            if (_actionId != MatchActionId.GetUp) _s.Round++;
            if (_s.Round > _setup.RoundLimit) End(MatchWinner.Draw, MatchEndKind.TimeLimit);
        }

        private void End(MatchWinner w, MatchEndKind k)
        {
            if (IsOver) return;
            bool was = _inAction; _inAction = false;
            Ev(MatchEventKind.MatchEnd, _s.CurrentActor, MatchActionId.CatchBreath);
            _inAction = was;
            Phase = MatchPhase.Over; PendingRoll = null; PendingReaction = null;
            Result = new MatchResult
            {
                Winner = w, Kind = k, FinalHype = _s.Hype, Rounds = Math.Min(_s.Round, _setup.RoundLimit),
                PlayerDirtyMoves = _s.PlayerDirtyMoves, PlayerSells = _s.PlayerSells,
                PlayerKickoutsAtTwo = _s.PlayerKickoutsAtTwo, PlayerUsedAlly = _s.PlayerUsedAlly
            };
        }

        private static MatchWinner WinnerOf(MatchSide s) { return s == MatchSide.Player ? MatchWinner.Player : MatchWinner.Rival; }
        private RingPath RoleOf(MatchSide s) { return s == MatchSide.Player ? _setup.PlayerRole : _setup.RivalRole; }

        // ---------- ефекти й журнал ----------

        private void BeginCall() { _new = new List<MatchEvent>(); _cur = null; }
        private IReadOnlyList<MatchEvent> EndCall() { return _new; }

        private void RequirePhase(MatchPhase p)
        {
            if (Phase != p) throw new InvalidOperationException("Очікується фаза " + p + ", зараз " + Phase);
        }

        private MatchEvent Ev(MatchEventKind kind, MatchSide actor, MatchActionId action)
        {
            var e = new MatchEvent { Seq = _s.Log.Count, Round = _s.Round, Kind = kind, Actor = actor, Action = action, Outside = _s.Outside };
            if (_inAction && kind != MatchEventKind.TurnEnd && kind != MatchEventKind.Exchange)
            {
                e.MoveKey = _hint.Key; e.Staging = _staging; e.ChainIndex = _chainIndex; e.Outside = _actionOutside;
            }
            _s.Log.Add(e); _new.Add(e); _cur = e;
            return e;
        }

        private void Fx(MatchEffectKind k, MatchSide who, int amount)
        {
            if (_cur != null) _cur.Effects.Add(new MatchEffect(k, who, amount));
        }

        private void Dmg(Fighter f, int n)
        {
            int v = Clamp(f.Damage + n, 0, MatchRules.MaxDamage);
            int d = v - f.Damage; f.Damage = v;
            if (d != 0) Fx(MatchEffectKind.Damage, f.Side, d);
        }

        private void AddMomentum(Fighter f, int n)
        {
            int v = Clamp(f.Momentum + n, 0, MatchRules.MaxMomentum);
            int d = v - f.Momentum; f.Momentum = v;
            if (d != 0) Fx(MatchEffectKind.Momentum, f.Side, d);
        }

        private void SetStamina(Fighter f, int value)
        {
            int v = Clamp(value, 0, MatchRules.MaxStamina);
            int d = v - f.Stamina; f.Stamina = v;
            if (d != 0) Fx(MatchEffectKind.Stamina, f.Side, d);
        }

        private void Hype(int n)
        {
            int v = Clamp(_s.Hype + n, 0, MatchRules.MaxHype);
            int d = v - _s.Hype; _s.Hype = v;
            if (d != 0) Fx(MatchEffectKind.Hype, MatchSide.Player, d);
        }

        private void GoDown(Fighter f)
        {
            if (f.Posture == Posture.Down) return;
            f.Posture = Posture.Down; Fx(MatchEffectKind.Down, f.Side, 1);
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
    }
}
