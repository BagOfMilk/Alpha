using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Ring;
using Game.Core.Ring.Match;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>Картка борця (gdd §2.1): ушкодження з порогами, імпульс, стан, зона, жетони.</summary>
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
        public string ZoneText;
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
        /// <summary>Прийом кар'єри (ключ каталогу) або null — системна дія рушія.</summary>
        public string MoveId;
        /// <summary>Той самий прийом двічі поспіль: −1 ажіотажу.</summary>
        public bool RepeatWarning;
        public string Name;
        public int Cost;
        public string CostText;
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
    /// Модель екрана матчу (gdd §2): картки борців, ажіотаж, раунд, список дій із шансами й причинами, модальні реакція
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
                PostureText = RingLabels.PostureName(_t, f.Posture),
                ZoneText = RingLabels.ZoneName(_t, f.Zone),
                Tokens = f.ReversalTokens,
                Warnings = f.Warnings
            };
            c.DamageText = _t.Format("match.card.damage", f.Damage, MatchRules.MaxDamage);
            c.PenaltyText = pen > 0 ? _t.Format("match.card.penalty", pen) : "";
            c.MomentumText = _t.Format("match.card.momentum", f.Momentum, MatchRules.MaxMomentum);
            c.TokensText = _t.Format("match.card.tokens", f.ReversalTokens);
            c.WarningsText = f.Warnings > 0 ? _t.Format("match.card.warnings", f.Warnings, MatchRules.WarningsToDq) : "";
            return c;
        }

        public int Hype { get { return _e.State.Hype; } }
        public int HypeMax { get { return MatchRules.MaxHype; } }
        public string HypeText { get { return _t.Format("match.hype", Hype, HypeMax, RingLabels.HypeStageText(_t, Hype)); } }
        public string RoundText { get { return _t.Format("match.round", _e.Round, _e.Setup.RoundLimit); } }
        public string ApText { get { return _t.Format("match.ap", _e.Ap); } }

        /// <summary>Відлік за рингом; порожньо, якщо ніхто не за рингом.</summary>
        public string CountText
        {
            get
            {
                int c = _e.State.OutsideCount;
                return c > 0 ? _t.Format("match.count", c, MatchRules.OutsideCountLimit) : "";
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
                        return _t.Get(_e.Phase == MatchPhase.AwaitReaction ? "match.turn.rival.react" : "match.turn.rival");
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
                var r = new MatchActionRow
                {
                    Id = o.Id,
                    Name = RingLabels.ActionName(_t, o.Id),
                    Cost = o.Cost,
                    CostText = o.Cost > 0 ? _t.Format("match.action.cost", o.Cost) : _t.Get("match.action.free"),
                    Available = o.Available,
                    IsBig = o.IsBig,
                    HasRoll = o.HasRoll,
                    RollIsOpponents = o.RollIsOpponents
                };
                if (o.HasAttr) r.AttrText = RingLabels.AttrName(_t, o.Attr);
                if (o.HasRoll && o.Chances != null)
                {
                    r.Percents = RingChances.Percent(o.Chances);
                    r.ModifierText = RingChances.Signed(o.Modifier);
                }
                if (!o.Available) r.ReasonText = RingLabels.Unavailable(_t, o.Reason);
                else if (n <= 9) r.Number = n++;
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

        private MatchActionRow Row(ActionOption o, string name)
        {
            var r = new MatchActionRow
            {
                Id = o.Id,
                Name = name,
                Cost = o.Cost,
                CostText = o.Cost > 0 ? _t.Format("match.action.cost", o.Cost) : _t.Get("match.action.free"),
                Available = o.Available,
                IsBig = o.IsBig,
                HasRoll = o.HasRoll,
                RollIsOpponents = o.RollIsOpponents
            };
            if (o.HasAttr) r.AttrText = RingLabels.AttrName(_t, o.Attr);
            if (o.HasRoll && o.Chances != null)
            {
                r.Percents = RingChances.Percent(o.Chances);
                r.ModifierText = RingChances.Signed(o.Modifier);
            }
            if (!o.Available) r.ReasonText = RingLabels.Unavailable(_t, o.Reason);
            return r;
        }

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

        /// <summary>Виконати дію гравця. «Перевести подих» — це кінець ходу.</summary>
        public bool Perform(MatchActionId id)
        {
            if (Input != MatchInput.PlayerAction) return false;
            var o = _e.Describe(id);
            if (!o.Available) return false;
            if (id == MatchActionId.CatchBreath) _e.EndTurn(); else _e.Perform(id);
            return true;
        }

        public bool PerformNumber(int number)
        {
            foreach (var r in Actions()) if (r.Number == number) return PerformRow(r);
            return false;
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
            var log = _e.State.Log;
            int end = log.Count;
            while (end > 0 && log[end - 1].Seq > shownSeq) end--;
            int from = Math.Max(0, end - LogLines);
            for (int i = from; i < end; i++) res.Add(EventText(log[i]));
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
                case MatchEventKind.Exchange: sb.Append(_t.Format("log.exchange", name)); break;
                case MatchEventKind.Sell: sb.Append(_t.Format("log.sell", name, action)); break;
                case MatchEventKind.Reverse: sb.Append(_t.Format("log.reverse", name, action, RingLabels.TierName(_t, ev.Tier))); break;
                case MatchEventKind.Kickout: sb.Append(_t.Format("log.kickout", name, RingLabels.TierName(_t, ev.Tier))); break;
                case MatchEventKind.Endure: sb.Append(_t.Format("log.endure", name, RingLabels.TierName(_t, ev.Tier))); break;
                case MatchEventKind.Rescue: sb.Append(_t.Format("log.rescue", name)); break;
                case MatchEventKind.TurnEnd: sb.Append(_t.Format("log.turnend", name)); break;
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
            string kind = _t.Get("fx." + (int)e.Kind);
            switch (e.Kind)
            {
                case MatchEffectKind.ZoneChange:
                    return _t.Format("fx.zone", who, RingLabels.ZoneName(_t, (Zone)e.Amount));
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

        /// <summary>Підсумок матчу рядками: переможець, як закінчився, раунди, ажіотаж.</summary>
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
