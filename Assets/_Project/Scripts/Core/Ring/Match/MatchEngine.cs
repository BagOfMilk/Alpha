using System;
using System.Collections.Generic;
using Game.Core.Randomness;

namespace Game.Core.Ring.Match
{
    /// <summary>
    /// Рушій пошагового матчу (gdd §2). Чистий C#, без сцени: UI і ШІ ходять одним API.
    /// Потік: Perform -> (React) -> PendingRoll -> Commit(докрутка). Кожен виклик повертає нові події журналу.
    /// </summary>
    public sealed class MatchEngine
    {
        private readonly MatchSetup _setup;
        private readonly IDiceRoller _dice;
        private readonly MatchState _s = new MatchState();
        private List<MatchEvent> _new = new List<MatchEvent>();
        private MatchEvent _cur;
        private int _extraDamage;
        private bool _repeatPending;
        private MoveHint _hint = MoveHint.Default;

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
        public MatchSide CurrentActor { get { return _s.CurrentActor; } }
        public int Round { get { return _s.Round; } }
        public int Ap { get { return _s.Ap; } }
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
                ReversalTokens = MatchRules.BaseReversalTokens + Math.Max(0, mods == null ? 0 : mods.ExtraReversalTokens)
            };
        }

        private void Start()
        {
            int p = TwoD6.Roll(_dice, "m:ex:p");
            int r = TwoD6.Roll(_dice, "m:ex:r");
            int pt = p + _s.Player.Stats.Get(RingAttr.Technique);
            int rt = r + _s.Rival.Stats.Get(RingAttr.Technique);
            MatchSide first;
            if (pt != rt) first = pt > rt ? MatchSide.Player : MatchSide.Rival;
            else if (_s.Player.Momentum != _s.Rival.Momentum) first = _s.Player.Momentum > _s.Rival.Momentum ? MatchSide.Player : MatchSide.Rival;
            else first = MatchSide.Player;
            var ev = Ev(MatchEventKind.Exchange, first, MatchActionId.CatchBreath);
            ev.HasRoll = true; ev.Roll = p; ev.Total = pt; ev.Modifier = pt - p;
            _s.FirstActor = first;
            StartTurn(first);
            Phase = MatchPhase.AwaitAction;
            _new = new List<MatchEvent>();
        }

        // ---------- доступні дії ----------

        public IReadOnlyList<ActionOption> GetAvailableActions()
        {
            var list = new List<ActionOption>();
            for (int i = 0; i <= (int)MatchActionId.CatchBreath; i++) list.Add(Describe((MatchActionId)i));
            return list;
        }

        public ActionOption Describe(MatchActionId id) { return Describe(id, 0); }

        /// <summary>Те саме з додатком до кидка від прийому (кар'єра: травма зони, бонус прийому) — шанси показують його.</summary>
        public ActionOption Describe(MatchActionId id, int extraRollMod)
        {
            var o = new ActionOption { Id = id, Cost = MatchRules.Cost(id), IsBig = MatchRules.IsBig(id) };
            if (Phase != MatchPhase.AwaitAction) { o.Reason = UnavailableReason.WrongPhase; return o; }
            var me = _s.Actor; var opp = _s.Other;
            RingAttr attr;
            if (AttrOf(id, me, out attr)) { o.HasAttr = true; o.Attr = attr; }
            o.Reason = Check(id, me, opp);
            o.Available = o.Reason == UnavailableReason.None;
            if (id == MatchActionId.Pin)
            {
                o.HasRoll = true; o.RollIsOpponents = true;
                o.Modifier = KickoutMod(opp, 0);
                o.Chances = TwoD6.ChancesOf36(o.Modifier);
            }
            else if (o.HasAttr)
            {
                o.HasRoll = true;
                o.Modifier = RollModifier(id, me) + extraRollMod;
                o.Chances = TwoD6.ChancesOf36(o.Modifier);
            }
            return o;
        }

        private UnavailableReason Check(MatchActionId id, Fighter me, Fighter opp)
        {
            if (id == MatchActionId.CatchBreath) return UnavailableReason.None;
            bool bothIn = me.Zone != Zone.Outside && opp.Zone != Zone.Outside;
            bool sameSide = (me.Zone == Zone.Outside) == (opp.Zone == Zone.Outside);
            UnavailableReason r = UnavailableReason.None;
            switch (id)
            {
                case MatchActionId.GetUp:
                    if (me.Posture != Posture.Down) r = UnavailableReason.NotLying;
                    break;
                case MatchActionId.HopeSpot:
                    if (me.HopeUsed) r = UnavailableReason.AlreadyUsed;
                    else if (!_setup.HopeSpotEnabled || me.Damage < MatchRules.HopeMinDamage
                             || me.Damage - opp.Damage < MatchRules.HopeGap) r = UnavailableReason.NotFarBehind;
                    else if (!bothIn) r = UnavailableReason.NotInRing;
                    break;
                case MatchActionId.Recover:
                    if (me.Damage <= 0) r = UnavailableReason.NothingToRecover;
                    break;
                case MatchActionId.LeaveRing:
                    if (me.Zone == Zone.Outside) r = UnavailableReason.AlreadyOutside;
                    break;
                case MatchActionId.ReturnToRing:
                    if (me.Zone != Zone.Outside) r = UnavailableReason.NotOutside;
                    break;
                case MatchActionId.Ally:
                    if (me.Side != MatchSide.Player || !_setup.PlayerHasAlly) r = UnavailableReason.NoAlly;
                    else if ((_setup.Stipulations & MatchStipulations.NoAlly) != 0) r = UnavailableReason.AllyForbidden;
                    else if (_s.AllyUsed) r = UnavailableReason.AlreadyUsed;
                    break;
                default:
                    if (!me.IsUpright) { r = UnavailableReason.ActorDown; break; }
                    switch (id)
                    {
                        case MatchActionId.Strike:
                            if (!opp.IsUpright) r = UnavailableReason.TargetNotStanding;
                            else if (!sameSide) r = UnavailableReason.NotSameZone;
                            break;
                        case MatchActionId.Throw:
                        case MatchActionId.IntoRopes:
                        case MatchActionId.IntoCorner:
                            if (!opp.IsUpright) r = UnavailableReason.TargetNotStanding;
                            else if (!bothIn) r = UnavailableReason.NotInRing;
                            break;
                        case MatchActionId.TopRope:
                            if (me.Zone != Zone.Corner) r = UnavailableReason.NotInCorner;
                            else if (opp.Zone == Zone.Outside) r = UnavailableReason.NotInRing;
                            break;
                        case MatchActionId.Taunt:
                            if (!(opp.Posture == Posture.Down || opp.Zone == Zone.Outside)) r = UnavailableReason.TargetNotDownOrOutside;
                            break;
                        case MatchActionId.Submission:
                            if (opp.Posture != Posture.Down) r = UnavailableReason.TargetNotDown;
                            else if (!bothIn) r = UnavailableReason.NotInRing;
                            else if (opp.Damage < MatchRules.PinMinDamage) r = UnavailableReason.TargetNotHurt;
                            break;
                        case MatchActionId.Pin:
                            if (opp.Posture != Posture.Down) r = UnavailableReason.TargetNotDown;
                            else if (!bothIn) r = UnavailableReason.NotInRing;
                            else if (opp.Zone == Zone.Ropes) r = UnavailableReason.InRopes;
                            else if (opp.Damage < MatchRules.PinMinDamage) r = UnavailableReason.TargetNotHurt;
                            break;
                        case MatchActionId.Finisher:
                            if (!bothIn) r = UnavailableReason.NotInRing;
                            else if (me.Momentum < MatchRules.FinisherMomentum) r = UnavailableReason.NeedImpulse;
                            else if (_s.Hype < MatchRules.FinisherHype) r = UnavailableReason.NeedHype;
                            else if (opp.Damage < MatchRules.FinisherTargetDamage) r = UnavailableReason.TargetNotHurt;
                            break;
                        case MatchActionId.Desperate:
                            if (me.DesperateUsed) r = UnavailableReason.AlreadyUsed;
                            else if (me.Momentum > MatchRules.DesperateMaxMomentum) r = UnavailableReason.ImpulseTooHigh;
                            else if (me.Damage < MatchRules.DesperateMinDamage) r = UnavailableReason.NotExhausted;
                            else if (!bothIn) r = UnavailableReason.NotInRing;
                            break;
                    }
                    break;
            }
            if (r == UnavailableReason.None && _s.Ap < MatchRules.Cost(id)) r = UnavailableReason.NotEnoughAp;
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
                case MatchActionId.IntoCorner:
                    attr = RingAttr.Strength; return true;
                case MatchActionId.IntoRopes:
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

        private int Late() { return _s.Round >= MatchRules.LateStageRound ? MatchRules.LateStagePenalty : 0; }

        private int RollModifier(MatchActionId id, Fighter me)
        {
            RingAttr a; AttrOf(id, me, out a);
            int m = me.Stats.Get(a) - Threshold(me);
            if ((id == MatchActionId.Strike || id == MatchActionId.Throw) && me.NextHitBonus) m += 1;
            if (id == MatchActionId.TopRope || id == MatchActionId.Desperate) m += Mods(me.Side).RiskyMoveBonus;
            return m;
        }

        /// <summary>«Вирватися»: Витривалість - вирвані - поріг - штраф прийому - пізня стадія + KickOutBonus.</summary>
        private int KickoutMod(Fighter def, int penalty)
        {
            // «Герой залу»: при ажіотажі 7+ зал тягне героя — +1 до «Вирватися».
            int crowd = Mods(def.Side).HeroOfCrowd && _s.Hype >= MatchRules.HeroOfCrowdHype ? 1 : 0;
            return MatchRules.DefenseBase + def.Stats.Get(RingAttr.Stamina) - def.KickoutsDone - Threshold(def) - penalty - Late() + Mods(def.Side).KickOutBonus + crowd;
        }

        private int EndureMod(Fighter def, int penalty)
        {
            return MatchRules.DefenseBase + def.Stats.Get(RingAttr.Stamina) - Threshold(def) - penalty - Late();
        }

        private int ReverseMod(Fighter def, bool vsFinisher)
        {
            return def.Stats.Get(RingAttr.Technique) - Threshold(def) + (vsFinisher ? MatchRules.ReverseVsFinisherPenalty : 0);
        }

        // ---------- публічні команди ----------

        public IReadOnlyList<MatchEvent> Perform(MatchActionId id) { return Perform(id, null); }

        /// <summary>Дія з підказкою прийому (кар'єра): ключ для повтору, додаток до кидка, ажіотаж за чистий успіх.</summary>
        public IReadOnlyList<MatchEvent> Perform(MatchActionId id, MoveHint hint)
        {
            RequirePhase(MatchPhase.AwaitAction);
            _hint = hint ?? MoveHint.Default;
            var opt = Describe(id, _hint.RollMod);
            if (!opt.Available) throw new InvalidOperationException("Дія " + id + " недоступна: " + opt.Reason);
            BeginCall();
            var me = _s.Actor; var opp = _s.Other;
            if (id == MatchActionId.CatchBreath) { FinishTurn(true); return EndCall(); }

            _s.Ap -= opt.Cost;
            if (id == MatchActionId.Finisher) me.Momentum -= MatchRules.FinisherMomentum;
            if (id == MatchActionId.Desperate) me.DesperateUsed = true;
            if (id == MatchActionId.HopeSpot) me.HopeUsed = true;
            _repeatPending = _s.HasLastAction && _s.LastAction == id && _s.LastMoveKey == _hint.Key;
            _s.HasLastAction = true; _s.LastAction = id; _s.LastMoveKey = _hint.Key;
            _extraDamage = 0;

            switch (id)
            {
                case MatchActionId.Pin:
                case MatchActionId.Recover:
                case MatchActionId.LeaveRing:
                case MatchActionId.ReturnToRing:
                case MatchActionId.Ally:
                    Ev(MatchEventKind.Action, me.Side, id);
                    if (_repeatPending) Hype(-1);
                    _repeatPending = false;
                    ApplyNoRoll(id, me, opp);
                    AfterAction(false);
                    break;
                default:
                    if (MatchRules.IsBig(id)) BeginReaction(id, me, opp);
                    else BeginRoll(id, me);
                    break;
            }
            return EndCall();
        }

        /// <summary>«Перевести подих»: кінець ходу, до 1 AP переходить на наступний.</summary>
        public IReadOnlyList<MatchEvent> EndTurn()
        {
            RequirePhase(MatchPhase.AwaitAction);
            BeginCall();
            FinishTurn(true);
            return EndCall();
        }

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
            if (tier == RollTier.Strong) GoDown(att);
            _repeatPending = false;
            FinishTurn(false);
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
            _hint = MoveHint.Default;
            AfterAction(tier == RollTier.Miss && pr.Action != MatchActionId.HopeSpot);
            return EndCall();
        }

        // ---------- хід нападника ----------

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
            int mod = RollModifier(id, me) + _hint.RollMod;
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
            if (id == MatchActionId.Strike || id == MatchActionId.Throw) me.NextHitBonus = false;
            switch (id)
            {
                case MatchActionId.Strike:
                    if (miss) { Dmg(me, 1); break; }
                    Dmg(opp, (strong ? 2 : 1) + (opp.Zone == Zone.Corner ? 1 : 0) + extra);
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
                    if (strong) Hype(1); else Dmg(me, 1);
                    break;
                case MatchActionId.IntoRopes:
                    if (miss) break;
                    SetZone(opp, Zone.Ropes);
                    if (strong) me.NextHitBonus = true;
                    break;
                case MatchActionId.IntoCorner:
                    if (miss) SetZone(me, Zone.Corner); else SetZone(opp, Zone.Corner);
                    break;
                case MatchActionId.TopRope:
                    SetZone(me, Zone.Center);
                    if (miss) { Dmg(me, 2); GoDown(me); break; }
                    if (strong) { Dmg(opp, 4 + extra); GoDown(opp); Hype(2); }
                    else { Dmg(opp, 2 + extra); Dmg(me, 1); }
                    break;
                case MatchActionId.Taunt:
                    if (strong) Hype(2);
                    else if (partial)
                    {
                        Hype(1);
                        if (opp.Posture == Posture.Down) { opp.Posture = Posture.Standing; Fx(MatchEffectKind.Stand, opp.Side, 1); }
                    }
                    else Dmg(me, 1);
                    break;
                case MatchActionId.Dirty:
                    ApplyDirty(t, me, opp);
                    break;
                case MatchActionId.Submission:
                    if (miss) break;
                    ResolveEndure(me, opp, strong ? MatchRules.SubmissionStrongPenalty : MatchRules.SubmissionPartialPenalty);
                    break;
                case MatchActionId.Finisher:
                    if (miss) { GoDown(me); break; }
                    Dmg(opp, (strong ? 4 : 3) + extra); GoDown(opp);
                    if (opp.Zone != Zone.Ropes)
                        ResolvePin(me, opp, strong ? MatchRules.FinisherStrongPinPenalty : MatchRules.FinisherPartialPinPenalty);
                    break;
                case MatchActionId.Desperate:
                    if (miss) { Dmg(me, 2); GoDown(me); break; }
                    if (strong) { Dmg(opp, 3 + extra); GoDown(opp); }
                    else { Dmg(opp, 2 + extra); Dmg(me, 1); }
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
                    if (partial && _s.Ap > 0) { _s.Ap--; Fx(MatchEffectKind.ApChange, me.Side, -1); }
                    break;
            }
            // «+1 імпульсу за чистий успіх»; відчайдушний дає +3 замість цього
            if (strong) AddMomentum(me, id == MatchActionId.Desperate ? 3 : 1);
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
                case MatchActionId.Recover: Dmg(me, -1); Hype(-1); break;
                case MatchActionId.LeaveRing: SetZone(me, Zone.Outside); break;
                case MatchActionId.ReturnToRing: SetZone(me, Zone.Center); RefreshCount(); break;
                case MatchActionId.Ally:
                    _s.AllyUsed = true; _s.PlayerUsedAlly = true; _s.RefereeDistracted = true;
                    Fx(MatchEffectKind.RefereeDistracted, me.Side, 1);
                    break;
            }
        }

        // ---------- утримання й больовий ----------

        private void ResolvePin(Fighter att, Fighter def, int penalty)
        {
            int roll = TwoD6.Roll(_dice, "m:kick:" + def.Side);
            int mod = KickoutMod(def, penalty); int total = roll + mod; var tier = TwoD6.TierOf(total);
            // «Підкуплений суддя»: раз за матч рахунок зупиняється на «два» замість «трьох».
            string perk = null;
            if (tier == RollTier.Miss && Mods(def.Side).BribedRef && !def.BribeUsed) { tier = RollTier.Partial; def.BribeUsed = true; perk = PerkCatalog.BribedRef; }
            var ev = Ev(MatchEventKind.Kickout, def.Side, MatchActionId.Pin);
            ev.HasRoll = true; ev.Roll = roll; ev.Modifier = mod; ev.Total = total; ev.Tier = tier; ev.Perk = perk;
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
        }

        private void ResolveEndure(Fighter att, Fighter def, int penalty)
        {
            int roll = TwoD6.Roll(_dice, "m:endure:" + def.Side);
            int mod = EndureMod(def, penalty); int total = roll + mod; var tier = TwoD6.TierOf(total);
            var ev = Ev(MatchEventKind.Endure, def.Side, MatchActionId.Submission);
            ev.HasRoll = true; ev.Roll = roll; ev.Modifier = mod; ev.Total = total; ev.Tier = tier;
            if (tier == RollTier.Miss) End(WinnerOf(att.Side), MatchEndKind.Submission);
            // 7-9 у канатах - больовий розірвано; поза канатами тримається (ще 2 AP на новий больовий)
        }

        // ---------- кінець ходу й матчу ----------

        private void AfterAction(bool missed)
        {
            if (IsOver || Phase != MatchPhase.AwaitAction) return;
            if (missed || _s.Ap <= 0) FinishTurn(false);
        }

        private void FinishTurn(bool breath)
        {
            if (IsOver) return;
            var a = _s.Actor;
            Ev(MatchEventKind.TurnEnd, a.Side, MatchActionId.CatchBreath);
            a.CarryAp = breath ? Math.Min(MatchRules.MaxCarryAp, Math.Max(0, _s.Ap)) : 0;
            a.NextHitBonus = false;
            if (a.Posture == Posture.Stunned) a.Posture = Posture.Standing;
            if ((a.Zone == Zone.Ropes || a.Zone == Zone.Corner) && a.ZoneCarried) SetZone(a, Zone.Center);
            if (a.Zone == Zone.Outside)
            {
                _s.OutsideCount += MatchRules.OutsideCountStep;
                Fx(MatchEffectKind.Count, a.Side, MatchRules.OutsideCountStep);
                if (_s.OutsideCount >= MatchRules.OutsideCountLimit)
                {
                    if (_s.Other.Zone == Zone.Outside) End(MatchWinner.Draw, MatchEndKind.DoubleCountOut);
                    else End(WinnerOf(Opp(a.Side)), MatchEndKind.CountOut);
                    return;
                }
            }
            RefreshCount();
            _s.TurnInRound++;
            if (_s.TurnInRound >= 2)
            {
                _s.TurnInRound = 0; _s.Round++; _s.RefereeDistracted = false;
                if (_s.Round > _setup.RoundLimit) { End(MatchWinner.Draw, MatchEndKind.TimeLimit); return; }
            }
            StartTurn(Opp(a.Side));
        }

        private void StartTurn(MatchSide side)
        {
            var f = _s.Get(side);
            _s.CurrentActor = side;
            int ap = MatchRules.ApPerTurn + f.CarryAp;
            f.CarryAp = 0;
            if (f.Posture == Posture.Stunned) ap -= MatchRules.StunApPenalty;
            _s.Ap = Math.Max(0, ap);
            _s.HasLastAction = false;
            f.NextHitBonus = false;
            f.ZoneCarried = f.Zone == Zone.Ropes || f.Zone == Zone.Corner;
            Phase = MatchPhase.AwaitAction;
        }

        private void RefreshCount()
        {
            if (_s.Player.Zone != Zone.Outside && _s.Rival.Zone != Zone.Outside) _s.OutsideCount = 0;
        }

        private void End(MatchWinner w, MatchEndKind k)
        {
            if (IsOver) return;
            Ev(MatchEventKind.MatchEnd, _s.CurrentActor, MatchActionId.CatchBreath);
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
            var e = new MatchEvent { Seq = _s.Log.Count, Round = _s.Round, Kind = kind, Actor = actor, Action = action };
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

        private void SetZone(Fighter f, Zone z)
        {
            if (f.Zone == z) return;
            f.Zone = z; Fx(MatchEffectKind.ZoneChange, f.Side, (int)z);
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
    }
}
