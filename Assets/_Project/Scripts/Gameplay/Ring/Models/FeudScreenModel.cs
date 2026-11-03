using System;
using System.Collections.Generic;
using Game.Core.Ring;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>Шкала ворожнечі для показу: назва, значення, максимум.</summary>
    public sealed class FeudMeterRow
    {
        public string Id;
        public string Name;
        public int Value;
        public int Min;
        public int Max;
        public string ValueText;
    }

    public sealed class FeudPathRow
    {
        public RingPath Path;
        public string Name;
        public string Mark;
        public int Points;
        public int Rank;
        public bool Dominant;
        public string RankText;
    }

    /// <summary>
    /// Модель екрана ворожнечі (gdd §3): шкали, шляхи, план букера, умови матчу, напарник, акт і номер наступного промо.
    /// Показує те, що змінили вибори, — щоб гравець бачив наслідок (кожен вибір міняє стан).
    /// </summary>
    public sealed class FeudScreenModel
    {
        private readonly RingCampaign _c;
        private readonly RingText _t;

        public FeudScreenModel(RingCampaign campaign, RingText text)
        {
            _c = campaign ?? throw new ArgumentNullException(nameof(campaign));
            _t = text ?? throw new ArgumentNullException(nameof(text));
        }

        public string TitleText { get { return _t.Get(_c.Script.TitleKey); } }

        public string PlayerName { get { return _t.Get(_c.State.Player.NameKey); } }
        public string RivalName { get { return _t.Get(_c.Script.Rival.NameKey); } }
        public string VersusText { get { return _t.Format("feud.versus", PlayerName, RivalName); } }

        public List<FeudMeterRow> Meters()
        {
            var rows = new List<FeudMeterRow>();
            foreach (var v in FeudVars.All)
            {
                int val = _c.State.GetVar(v);
                rows.Add(new FeudMeterRow
                {
                    Id = v,
                    Name = RingLabels.Var(_t, v),
                    Value = val,
                    Min = FeudVars.Min(v),
                    Max = FeudVars.Max(v),
                    // Шкала, що йде в мінус (повага роздягальні −2…+2), — зі знаком і діапазоном: «0 / 2» читалось як «нуль із двох».
                    ValueText = FeudVars.Min(v) < 0
                        ? _t.Format("feud.meter.signed", RingChances.Signed(val), RingChances.Signed(FeudVars.Min(v)), RingChances.Signed(FeudVars.Max(v)))
                        : _t.Format("feud.meter", val, FeudVars.Max(v))
                });
            }
            return rows;
        }

        public List<FeudPathRow> Paths()
        {
            var rows = new List<FeudPathRow>();
            var dom = _c.State.DominantPath();
            for (int i = 0; i < FeudState.PathCount; i++)
            {
                var p = (RingPath)i;
                int pts = _c.State.GetPath(p);
                int rank = Development.PathRank(pts);
                rows.Add(new FeudPathRow
                {
                    Path = p,
                    Name = RingLabels.PathName(_t, p),
                    Mark = RingLabels.PathMark(_t, p),
                    Points = pts,
                    Rank = rank,
                    Dominant = p == dom,
                    RankText = _t.Format("feud.path", pts, rank, Development.MaxRank)
                });
            }
            return rows;
        }

        // План і умови — найближчого матчу: з промо плюс значення за замовчуванням з даних (gdd §5.2); стан не міняється.
        private FeudState Upcoming { get { return _c.PreviewNextMatchState(); } }

        public string PlanText { get { return RingLabels.PlanName(_t, Upcoming.Plan); } }

        /// <summary>Заголовок картки плану: «План і умови матчу k».</summary>
        public string PlanSectionText { get { return _t.Format("feud.section.matchplan", MatchNumber); } }

        /// <summary>Умови матчу текстом; порожній список — без умов.</summary>
        public List<string> StipulationTexts()
        {
            var res = new List<string>();
            var s = Upcoming.Stipulations;
            if ((s & MatchStipulations.NoDq) != 0) res.Add(_t.Get("stip.no_dq"));
            if ((s & MatchStipulations.NoAlly) != 0) res.Add(_t.Get("stip.no_ally"));
            if (res.Count == 0) res.Add(_t.Get("stip.none"));
            return res;
        }

        public string AllyText { get { return _t.Get(_c.State.HasAlly ? "feud.ally.yes" : "feud.ally.no"); } }

        // ---------- де ми в сюжеті ----------

        /// <summary>Номер найближчого матчу ворожнечі (з одиниці) і скільки їх усього.</summary>
        public int MatchNumber { get { return _c.CurrentMatchIndex + 1; } }
        public int MatchCount { get { return _c.MatchCount; } }
        public int PromoNumber { get { return Math.Min(_c.PromoIndex + 1, _c.PromoCount); } }
        public int PromoCount { get { return _c.PromoCount; } }

        private PromoDef NextPromo
        {
            get
            {
                var st = _c.CurrentStep;
                return st != null && !st.IsMatch ? _c.Script.Promos[st.Index] : null;
            }
        }

        /// <summary>Чи наступний крок — промо (інакше — вихід на матч).</summary>
        public bool HasNextPromo { get { return NextPromo != null; } }

        /// <summary>Чи найближчий матч — розв'язка.</summary>
        public bool NextMatchIsFinal { get { return _c.CurrentMatchDef.Decisive; } }

        public int NextAct { get { return _c.CurrentAct; } }

        /// <summary>«Акт N · матч k з 3»: промо перед матчем або сам матч (розв'язка — з позначкою).</summary>
        public string ProgressText
        {
            get
            {
                if (HasNextPromo) return _t.Format("feud.progress.promo", NextAct, MatchNumber, MatchCount);
                return _t.Format(NextMatchIsFinal ? "feud.progress.final" : "feud.progress.matchn", NextAct, MatchNumber, MatchCount);
            }
        }

        public string NextTitleText { get { return HasNextPromo ? _t.Get(NextPromo.TitleKey) : ""; } }
        public string NextPlaceText { get { return HasNextPromo ? _t.Get(NextPromo.PlaceKey) : ""; } }

        public string ContinueText
        {
            get
            {
                if (HasNextPromo) return _t.Get("feud.continue.promo");
                return NextMatchIsFinal ? _t.Get("feud.continue.final") : _t.Format("feud.continue.matchn", MatchNumber, MatchCount);
            }
        }
    }
}
