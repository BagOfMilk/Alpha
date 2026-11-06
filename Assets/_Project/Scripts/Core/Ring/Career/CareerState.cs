using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Ring.Match;

namespace Game.Core.Ring.Career
{
    /// <summary>Підсумок шоу, як його показує екран «Підсумок» (і що лишається в сейві, поки його не закрито).</summary>
    public sealed class ShowResult
    {
        public bool Played;
        public bool Withdrew;
        public bool IsBig;
        public bool IsTitle;
        public bool IsFeud;
        public string OpponentId = "";
        public CardPosition Position;
        public MatchWinner Winner;
        public MatchEndKind EndKind;
        public int Hype;
        public int Rounds;
        public FeudPlanOutcome PlanOutcome;
        public int Fee;
        public int FeeWinBonus;
        public int FeeHypeBonus;
        public int PopularityBefore;
        public int PopularityAfter;
        public int TrustBefore;
        public int TrustAfter;
        /// <summary>Нова травма: зона (0–2) або −1.</summary>
        public int InjuredZone = -1;
        public int InjuryWeeks;
        public bool TitleWon;
        /// <summary>Очки розвитку, нараховані за матч (різниця DevPoints).</summary>
        public int DevPointsGained;

        public int TotalFee { get { return Fee + FeeWinBonus + FeeHypeBonus; } }

        internal string Capture()
        {
            return string.Join(",", new[]
            {
                B(Played), B(Withdrew), B(IsBig), B(IsTitle), B(IsFeud), Uri.EscapeDataString(OpponentId ?? ""), I((int)Position),
                I((int)Winner), I((int)EndKind), I(Hype), I(Rounds), I((int)PlanOutcome), I(Fee), I(FeeWinBonus), I(FeeHypeBonus),
                I(PopularityBefore), I(PopularityAfter), I(TrustBefore), I(TrustAfter), I(InjuredZone), I(InjuryWeeks), B(TitleWon),
                I(DevPointsGained)
            });
        }

        internal static ShowResult Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split(',');
            if (p.Length != 23) throw new FormatException("пошкоджений підсумок шоу у зліпку кар'єри");
            int i = 0;
            var r = new ShowResult();
            r.Played = Bo(p[i++]); r.Withdrew = Bo(p[i++]); r.IsBig = Bo(p[i++]); r.IsTitle = Bo(p[i++]); r.IsFeud = Bo(p[i++]);
            r.OpponentId = Uri.UnescapeDataString(p[i++]); r.Position = (CardPosition)In(p[i++]);
            r.Winner = (MatchWinner)In(p[i++]); r.EndKind = (MatchEndKind)In(p[i++]); r.Hype = In(p[i++]); r.Rounds = In(p[i++]);
            r.PlanOutcome = (FeudPlanOutcome)In(p[i++]); r.Fee = In(p[i++]); r.FeeWinBonus = In(p[i++]); r.FeeHypeBonus = In(p[i++]);
            r.PopularityBefore = In(p[i++]); r.PopularityAfter = In(p[i++]); r.TrustBefore = In(p[i++]); r.TrustAfter = In(p[i++]);
            r.InjuredZone = In(p[i++]); r.InjuryWeeks = In(p[i++]); r.TitleWon = Bo(p[i++]); r.DevPointsGained = In(p[i++]);
            return r;
        }

        private static string B(bool b) { return b ? "1" : "0"; }
        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static bool Bo(string s) { return s == "1"; }
        private static int In(string s) { return int.Parse(s, CultureInfo.InvariantCulture); }
    }

    /// <summary>Підсумок сезону (§6.4, §6.2): титульний шанс, звільнення, куди далі.</summary>
    public sealed class SeasonReport
    {
        public int Season;
        public int Popularity;
        public int Trust;
        public int Money;
        /// <summary>Популярність 0 наприкінці сезону — звільнення (повернення на місцевий рівень; не кінець гри).</summary>
        public bool Fired;
        /// <summary>Титульний шанс заслужено: довіра букера ≥ 4 або ворожнеча 4 — або чемпіон захищає пояс.</summary>
        public bool TitleEligible;
        public bool IsDefense;
        public bool TitleDone;
        public bool TitleWon;
        public bool BeltIsPlayers;

        internal string Capture()
        {
            return string.Join(",", new[]
            {
                Season.ToString(CultureInfo.InvariantCulture), Popularity.ToString(CultureInfo.InvariantCulture),
                Trust.ToString(CultureInfo.InvariantCulture), Money.ToString(CultureInfo.InvariantCulture),
                Fired ? "1" : "0", TitleEligible ? "1" : "0", IsDefense ? "1" : "0", TitleDone ? "1" : "0", TitleWon ? "1" : "0",
                BeltIsPlayers ? "1" : "0"
            });
        }

        internal static SeasonReport Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split(',');
            if (p.Length != 10) throw new FormatException("пошкоджений підсумок сезону у зліпку кар'єри");
            return new SeasonReport
            {
                Season = int.Parse(p[0], CultureInfo.InvariantCulture), Popularity = int.Parse(p[1], CultureInfo.InvariantCulture),
                Trust = int.Parse(p[2], CultureInfo.InvariantCulture), Money = int.Parse(p[3], CultureInfo.InvariantCulture),
                Fired = p[4] == "1", TitleEligible = p[5] == "1", IsDefense = p[6] == "1", TitleDone = p[7] == "1",
                TitleWon = p[8] == "1", BeltIsPlayers = p[9] == "1"
            };
        }
    }

    /// <summary>
    /// Стан кар'єри (TRPG_DESIGN §6.2). Кожна довга шкала має якір, споживача й сигнал:
    ///
    /// <b>Популярність</b> 0–10 (живе в <see cref="FeudState.Audience"/>). Якір: 0 — невідомий, 3 — місцева зірка, 9–10 — ікона.
    /// Споживач: місце в картці → гонорар (<see cref="CareerRules.PositionFor"/>), вибір суперника букером, звільнення при 0 на кінці сезону.
    /// Сигнал: слово місця в картці, реакція залу при виході, підсумок сезону.
    ///
    /// <b>Довіра букера</b> 0–5 (<see cref="FeudState.Trust"/>). Якір: 0 — «хто ти такий», 5 — партнер федерації. Споживач: план букера
    /// на матч (при 0–1 плану немає), титульний шанс (≥ 4). Сигнал: репліка букера в картці, чутки закулісся.
    ///
    /// <b>Ворожнеча</b> 0–4 з кожним суперником (Молот — <see cref="FeudState.Heat"/>, решта — <see cref="NpcFeud"/>). Якір: 0 — перша
    /// зустріч, 4 — головна подія року. Споживач: ажіотаж старту матчу, умова «без дискваліфікації» з рівня 3, вибір суперника
    /// великого шоу, титульний шанс. Сигнал: лінія в картці, промо.
    ///
    /// <b>Гроші</b>. Якір: тиждень життя (<see cref="CareerRules.LivingCost"/>). Споживач: тренер, лікар, проживання; бідність
    /// скорочує наступний тиждень до двох дій. Сигнал: гаманець, рядок підсумку шоу.
    ///
    /// <b>Травми</b>: ушкодження зони 0–3 (споживач — травма; сигнал — картка тіла), травма 1–4 тижні (споживач — штраф −1 до кидків
    /// прийомів із цією зоною; сигнал — позначка на прийомі).
    /// </summary>
    public sealed class CareerState
    {
        public ulong Seed;
        public int Season = 1;
        /// <summary>Тиждень сезону, 1…8.</summary>
        public int Week = 1;
        public int Money;

        public string RingName = "";
        public int Costume;
        public Gimmick Gimmick;
        public CareerRole Role;
        public readonly List<string> Moves = new List<string>();

        public readonly int[] Injury = new int[3];
        public readonly int[] ZoneDamage = new int[3];

        public int ActionsLeft;
        public bool Strained;
        /// <summary>Промо на «7–9» чекає вибору однієї нагороди.</summary>
        public bool PromoChoicePending;
        /// <summary>Бонус ажіотажу до старту найближчого матчу (промо).</summary>
        public int NextHypeBonus;
        /// <summary>Імпульс суперника на старті найближчого матчу (зірване промо).</summary>
        public int RivalBoost;
        public readonly List<int> ActionsDone = new List<int>();
        public readonly int[] ActionTally = new int[5];

        /// <summary>Ворожнеча з NPC (крім Молота, поки ворожнеча триває — його рівень у FeudState.Heat).</summary>
        public readonly SortedDictionary<string, int> NpcFeud = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public const string PlayerHolder = "player";
        public string BeltHolder = "";

        // шоу
        public ShowPhase Phase;
        public string Opponent = "";
        public string LastOpponent = "";
        public BookerPlan Plan;
        public MatchStipulations Stip;
        public CardPosition Position;
        public bool IsBig;
        public bool IsTitle;
        /// <summary>Велике шоу зі ступенем ворожнечі з Молотом (RingCampaign), а не окремим матчем.</summary>
        public bool IsFeudShow;
        public bool ShowPromoDone;
        public int ShowPromoTier = -1;
        public ShowResult Last;
        public SeasonReport Report;

        /// <summary>Закулісні сцени, що вже були: «сезон:id» (подія — раз на сезон).</summary>
        public readonly SortedSet<string> BackstageSeen = new SortedSet<string>(StringComparer.Ordinal);
        /// <summary>Результат останнього шоу (переживає перехід тижня; закулісні розмови його читають): 0 — не грав, 1 — перемога,
        /// 2 — поразка, 3 — нічия, 4 — зійшов травмованим.</summary>
        public int LastOutcome;
        public FeudPlanOutcome LastPlanOutcome;

        /// <summary>
        /// Сходинки сили (власник 06.11.2026: «Піки 70–80%»): щабель картки, на якому знято знімок росту гравця (−1 — ще ні), і сам
        /// знімок. Щотижневі суперники рівняються на знімок, а не на поточний ріст: прокачка між підвищеннями відчувається.
        /// </summary>
        public int TierPos = -1;
        /// <summary>Сітки витрат (06.11.2026): костюм куплено; тренер ростера (−1 — немає) і прогрес його атрибута; страховка на
        /// сезон (номер сезону, 0 — ні); виїзне шоу цього тижня й штраф часу наступного.</summary>
        public bool CostumeBought;
        public int Coach = -1;
        public int CoachProgress;
        public int InsuredSeason;
        public bool AwayShow;
        public bool AwayTired;
        public int TierGrowth;
        /// <summary>Хто переміг гравця останнього разу (реванш — суперник слабший: ти знаєш його слабкість). Перемога знімає образу.</summary>
        public readonly SortedSet<string> Grudges = new SortedSet<string>(StringComparer.Ordinal);

        public int FiredCount;
        public int TitlesWon;
        public bool FeudResolved;
        /// <summary>Очки розвитку на початок поточного матчу (для підсумку).</summary>
        public int DevPointsAtMatchStart;

        public int GetNpcFeud(string id)
        {
            int v;
            return NpcFeud.TryGetValue(id, out v) ? v : 0;
        }

        public void SetNpcFeud(string id, int v) { NpcFeud[id] = Math.Max(0, Math.Min(CareerRules.MaxFeud, v)); }

        public int InjuryPenaltyFor(BodyZone z) { return Injury[(int)z] > 0 ? CareerRules.InjuryRollPenalty : 0; }

        public bool IsInjured { get { return Injury[0] > 0 || Injury[1] > 0 || Injury[2] > 0; } }

        public bool IsBigWeek(int week) { return week % CareerRules.BigShowEvery == 0; }

        // ---------- зліпок ----------

        private static string Esc(string s) { return Uri.EscapeDataString(s ?? ""); }
        private static string Ints(int[] a) { return string.Join(",", Array.ConvertAll(a, x => x.ToString(CultureInfo.InvariantCulture))); }
        private static string Ints(List<int> a) { return Ints(a.ToArray()); }

        internal void Capture(StringBuilder sb)
        {
            Func<bool, string> b = x => x ? "1" : "0";
            sb.Append("|seed=").Append(Seed.ToString(CultureInfo.InvariantCulture));
            sb.Append("|season=").Append(Season).Append("|week=").Append(Week).Append("|money=").Append(Money);
            sb.Append("|name=").Append(Esc(RingName)).Append("|costume=").Append(Costume);
            sb.Append("|gimmick=").Append((int)Gimmick).Append("|role=").Append((int)Role);
            var mv = new List<string>();
            foreach (var m in Moves) mv.Add(Esc(m));
            sb.Append("|moves=").Append(string.Join(",", mv));
            sb.Append("|inj=").Append(Ints(Injury)).Append("|zd=").Append(Ints(ZoneDamage));
            sb.Append("|actions=").Append(ActionsLeft).Append("|strained=").Append(b(Strained)).Append("|promochoice=").Append(b(PromoChoicePending));
            sb.Append("|hypebonus=").Append(NextHypeBonus).Append("|rivalboost=").Append(RivalBoost);
            sb.Append("|done=").Append(Ints(ActionsDone)).Append("|tally=").Append(Ints(ActionTally));
            var nf = new List<string>();
            foreach (var kv in NpcFeud) nf.Add(Esc(kv.Key) + ":" + kv.Value);
            sb.Append("|npcfeud=").Append(string.Join(",", nf));
            sb.Append("|belt=").Append(Esc(BeltHolder));
            sb.Append("|phase=").Append((int)Phase).Append("|opp=").Append(Esc(Opponent)).Append("|lastopp=").Append(Esc(LastOpponent));
            sb.Append("|plan=").Append((int)Plan).Append("|stip=").Append((int)Stip).Append("|pos=").Append((int)Position);
            sb.Append("|big=").Append(b(IsBig)).Append("|title=").Append(b(IsTitle)).Append("|feudshow=").Append(b(IsFeudShow));
            sb.Append("|showpromo=").Append(b(ShowPromoDone)).Append("|showpromotier=").Append(ShowPromoTier);
            sb.Append("|last=").Append(Last == null ? "" : Esc(Last.Capture()));
            sb.Append("|report=").Append(Report == null ? "" : Esc(Report.Capture()));
            sb.Append("|fired=").Append(FiredCount).Append("|titles=").Append(TitlesWon).Append("|resolved=").Append(b(FeudResolved));
            sb.Append("|devstart=").Append(DevPointsAtMatchStart);
            var bs = new List<string>();
            foreach (var k in BackstageSeen) bs.Add(Esc(k));
            sb.Append("|bs=").Append(string.Join(",", bs));
            sb.Append("|lastout=").Append(LastOutcome).Append("|lastplan=").Append((int)LastPlanOutcome);
            sb.Append("|tierpos=").Append(TierPos).Append("|tiergrowth=").Append(TierGrowth);
            sb.Append("|costume2=").Append(b(CostumeBought)).Append("|coach=").Append(Coach).Append("|coachp=").Append(CoachProgress);
            sb.Append("|insured=").Append(InsuredSeason).Append("|away=").Append(b(AwayShow)).Append("|awaytired=").Append(b(AwayTired));
            var gr = new List<string>();
            foreach (var g in Grudges) gr.Add(Esc(g));
            sb.Append("|grudges=").Append(string.Join(",", gr));
        }

        internal void Restore(Dictionary<string, string> kv)
        {
            Func<string, int> I = k => int.Parse(Need(kv, k), CultureInfo.InvariantCulture);
            Func<string, bool> B = k => Need(kv, k) == "1";
            Func<string, string> S = k => Uri.UnescapeDataString(Need(kv, k));
            Seed = ulong.Parse(Need(kv, "seed"), CultureInfo.InvariantCulture);
            Season = I("season"); Week = I("week"); Money = I("money");
            RingName = S("name"); Costume = I("costume"); Gimmick = (Gimmick)I("gimmick"); Role = (CareerRole)I("role");
            Moves.Clear();
            foreach (var m in Need(kv, "moves").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) Moves.Add(Uri.UnescapeDataString(m));
            Fill(Injury, Need(kv, "inj")); Fill(ZoneDamage, Need(kv, "zd"));
            ActionsLeft = I("actions"); Strained = B("strained"); PromoChoicePending = B("promochoice");
            NextHypeBonus = I("hypebonus"); RivalBoost = I("rivalboost");
            ActionsDone.Clear();
            foreach (var d in Need(kv, "done").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) ActionsDone.Add(int.Parse(d, CultureInfo.InvariantCulture));
            Fill(ActionTally, Need(kv, "tally"));
            NpcFeud.Clear();
            foreach (var p in Need(kv, "npcfeud").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int c = p.LastIndexOf(':');
                if (c < 0) throw new FormatException("пошкоджена ворожнеча з NPC у зліпку кар'єри");
                NpcFeud[Uri.UnescapeDataString(p.Substring(0, c))] = int.Parse(p.Substring(c + 1), CultureInfo.InvariantCulture);
            }
            BeltHolder = S("belt");
            Phase = (ShowPhase)I("phase"); Opponent = S("opp"); LastOpponent = S("lastopp");
            Plan = (BookerPlan)I("plan"); Stip = (MatchStipulations)I("stip"); Position = (CardPosition)I("pos");
            IsBig = B("big"); IsTitle = B("title"); IsFeudShow = B("feudshow");
            ShowPromoDone = B("showpromo"); ShowPromoTier = I("showpromotier");
            Last = ShowResult.Parse(S("last")); Report = SeasonReport.Parse(S("report"));
            FiredCount = I("fired"); TitlesWon = I("titles"); FeudResolved = B("resolved");
            DevPointsAtMatchStart = I("devstart");
            // закулісся додано пізніше за перші зліпки ring3: старі зліпки без цих ключів читаються як «сцен ще не було»
            BackstageSeen.Clear();
            string bs;
            if (kv.TryGetValue("bs", out bs))
                foreach (var k in bs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) BackstageSeen.Add(Uri.UnescapeDataString(k));
            string v;
            LastOutcome = kv.TryGetValue("lastout", out v) ? int.Parse(v, CultureInfo.InvariantCulture) : 0;
            LastPlanOutcome = kv.TryGetValue("lastplan", out v) ? (FeudPlanOutcome)int.Parse(v, CultureInfo.InvariantCulture) : FeudPlanOutcome.Neither;
            // сходинки сили додано 06.11.2026: старі зліпки — «знімка ще не було», образ немає
            TierPos = kv.TryGetValue("tierpos", out v) ? int.Parse(v, CultureInfo.InvariantCulture) : -1;
            TierGrowth = kv.TryGetValue("tiergrowth", out v) ? int.Parse(v, CultureInfo.InvariantCulture) : 0;
            // сітки витрат додано 06.11.2026: старі зліпки — нічого не куплено
            CostumeBought = kv.TryGetValue("costume2", out v) && v == "1";
            Coach = kv.TryGetValue("coach", out v) ? int.Parse(v, CultureInfo.InvariantCulture) : -1;
            CoachProgress = kv.TryGetValue("coachp", out v) ? int.Parse(v, CultureInfo.InvariantCulture) : 0;
            InsuredSeason = kv.TryGetValue("insured", out v) ? int.Parse(v, CultureInfo.InvariantCulture) : 0;
            AwayShow = kv.TryGetValue("away", out v) && v == "1";
            AwayTired = kv.TryGetValue("awaytired", out v) && v == "1";
            Grudges.Clear();
            if (kv.TryGetValue("grudges", out v))
                foreach (var g in v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) Grudges.Add(Uri.UnescapeDataString(g));
        }

        private static string Need(Dictionary<string, string> kv, string key)
        {
            string v;
            if (!kv.TryGetValue(key, out v)) throw new FormatException("у зліпку кар'єри немає '" + key + "'");
            return v;
        }

        private static void Fill(int[] into, string csv)
        {
            var p = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != into.Length) throw new FormatException("пошкоджений масив у зліпку кар'єри");
            for (int i = 0; i < p.Length; i++) into[i] = int.Parse(p[i], CultureInfo.InvariantCulture);
        }
    }
}
