using System;
using System.Collections.Generic;
using Game.Core.Ring;

namespace Game.Gameplay.Ring.Models
{
    public enum PerkState { Owned = 0, Available = 1, Locked = 2 }

    public sealed class PerkRow
    {
        public string Id;
        public string Name;
        public string Desc;
        public int Cost;
        public string CostText;
        /// <summary>Шлях перка; для загального — не задано (<see cref="IsGeneral"/>).</summary>
        public RingPath Path;
        public bool IsGeneral;
        public string PathText;
        public PerkState State;
        /// <summary>Причина, чому не можна купити (порожньо для доступного й купленого).</summary>
        public string ReasonText = "";
        public int Number;
    }

    public sealed class AttrRow
    {
        public RingAttr Attr;
        public string Name;
        public int Value;
        public string ValueText;
        public bool CanBuy;
        public string ReasonText = "";
        public string BuyText;
    }

    public sealed class DevPathRow
    {
        public RingPath Path;
        public string Name;
        public int Points;
        public int Rank;
        public string RankText;
    }

    /// <summary>
    /// Модель екрана «кінцівка + розвиток» (gdd §5.3, §6): назва кінцівки, підсумок матчу, очки, ранги шляхів, перки за
    /// шляхами з доступністю й причиною, атрибути з кнопкою +1, реванш.
    /// </summary>
    public sealed class DevelopmentScreenModel
    {
        private readonly RingCampaign _c;
        private readonly RingText _t;

        public DevelopmentScreenModel(RingCampaign campaign, RingText text)
        {
            _c = campaign ?? throw new ArgumentNullException(nameof(campaign));
            _t = text ?? throw new ArgumentNullException(nameof(text));
        }

        public string EndingTitle { get { return _c.HasEnding ? RingLabels.EndingName(_t, _c.Ending) : ""; } }
        public string EndingDesc { get { return _c.HasEnding ? RingLabels.EndingDesc(_t, _c.Ending) : ""; } }

        public string PlanText
        {
            get
            {
                return _t.Format("dev.plan", RingLabels.PlanName(_t, _c.State.Plan), RingLabels.PlanOutcome(_t, _c.PlanOutcome));
            }
        }

        /// <summary>Підсумок першого матчу рядками.</summary>
        public List<string> ResultLines() { return Lines(_c.Result); }

        /// <summary>Підсумок реваншу (порожньо, якщо не грали).</summary>
        public List<string> RematchLines() { return Lines(_c.RematchResult); }

        public bool HasRematchResult { get { return _c.RematchResult != null; } }

        private List<string> Lines(MatchResult r)
        {
            var res = new List<string>();
            if (r == null) return res;
            res.Add(RingLabels.WinnerName(_t, r.Winner));
            res.Add(RingLabels.EndKind(_t, r.Kind));
            res.Add(_t.Format("match.result.rounds", r.Rounds));
            res.Add(_t.Format("match.result.hype", r.FinalHype, Game.Core.Ring.Match.MatchRules.MaxHype));
            return res;
        }

        public int Points { get { return _c.State.DevPoints; } }
        public string PointsText { get { return _t.Format("dev.points", Points); } }

        public List<DevPathRow> Paths()
        {
            var rows = new List<DevPathRow>();
            for (int i = 0; i < FeudState.PathCount; i++)
            {
                var p = (RingPath)i;
                int pts = _c.State.GetPath(p);
                int rank = Development.PathRank(pts);
                rows.Add(new DevPathRow
                {
                    Path = p,
                    Name = RingLabels.PathName(_t, p),
                    Points = pts,
                    Rank = rank,
                    RankText = _t.Format("dev.rank", rank, Development.MaxRank)
                });
            }
            return rows;
        }

        /// <summary>Усі перки: спершу за шляхами (Герой, Лиходій, Бунтар), потім загальні; номер для клавіші — за порядком.</summary>
        public List<PerkRow> Perks()
        {
            var rows = new List<PerkRow>();
            int n = 1;
            for (int pass = 0; pass < 2; pass++)
                foreach (var def in PerkCatalog.All)
                {
                    if ((pass == 0) == def.IsGeneral) continue;
                    var row = new PerkRow
                    {
                        Id = def.Id,
                        Name = RingLabels.PerkName(_t, def.Id),
                        Desc = RingLabels.PerkDesc(_t, def.Id),
                        Cost = def.Cost,
                        CostText = _t.Format("dev.cost", def.Cost),
                        IsGeneral = def.IsGeneral,
                        Number = n++
                    };
                    if (def.IsGeneral) row.PathText = _t.Get("dev.perk.general");
                    else
                    {
                        row.Path = def.Path.Value;
                        row.PathText = _t.Format("dev.perk.path", RingLabels.PathName(_t, def.Path.Value), def.Rank);
                    }
                    string reason;
                    if (_c.State.Perks.Contains(def.Id)) row.State = PerkState.Owned;
                    else if (Development.CanBuyPerk(_c.State, def.Id, out reason)) row.State = PerkState.Available;
                    else
                    {
                        row.State = PerkState.Locked;
                        row.ReasonText = _t.Get("dev.reason." + reason);
                    }
                    rows.Add(row);
                }
            return rows;
        }

        public bool BuyPerk(string id) { return _c.BuyPerk(id); }

        public List<AttrRow> Attrs()
        {
            var rows = new List<AttrRow>();
            for (int i = 0; i < 5; i++)
            {
                var a = (RingAttr)i;
                string reason;
                bool can = Development.CanBuyAttr(_c.State, a, out reason);
                int v = _c.State.Player.Get(a);
                rows.Add(new AttrRow
                {
                    Attr = a,
                    Name = RingLabels.AttrName(_t, a),
                    Value = v,
                    ValueText = RingChances.Signed(v),
                    CanBuy = can,
                    ReasonText = can ? "" : _t.Get("dev.reason." + reason),
                    BuyText = _t.Format("dev.attr.buy", Development.AttrCost)
                });
            }
            return rows;
        }

        public bool BuyAttr(RingAttr a) { return _c.BuyAttr(a); }

        public bool CanRematch { get { return _c.CanRematch; } }
    }
}
