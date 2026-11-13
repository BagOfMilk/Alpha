using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Randomness;
using Game.Core.Ring.Match;

namespace Game.Core.Ring.Career
{
    /// <summary>
    /// Фасад кар'єри альфи (TRPG_DESIGN §3, §6, §11.3). Екрани й тести ходять лише через нього; правил матчу тут немає —
    /// він переставляє етапи, веде тиждень і шоу, а ворожнеча з Молотом лишається в <see cref="RingCampaign"/> (<see cref="Feud"/>).
    ///
    /// Потік: Title → StartNewGame → Create → CreateHero → Week (3 дії: Train / Recover / Promo / Backstage / SideJob; розвиток —
    /// BuyAttr / BuyPerk / LearnMove) → EndWeek → Show: StartShow → (звичайне шоу: Match → FinishMatch → Result) або (велике шоу
    /// ворожнечі: Phase=Feud, далі екрани <see cref="Feud"/>, матч — через <see cref="StartFeudMatch"/> / <see cref="FinishMatch"/>,
    /// кінець — <see cref="FinishBigShow"/>) → ContinueFromShow → наступний тиждень … → тиждень 8 → SeasonEnd (StartTitleMatch /
    /// SkipTitle → StartNextSeason).
    ///
    /// Популярність, довіра букера, ворожнеча з Молотом і очки розвитку живуть у <see cref="Feud"/>.State (одне джерело правди для
    /// промо ворожнечі і кар'єри); решта — у <see cref="State"/>. Весь стан — один непрозорий зліпок «ring3».
    /// </summary>
    public sealed class CareerSession
    {
        public const int CostumeCount = 8;
        public const int MaxNameLength = 24;
        public const string BlobVersion = "ring3";
        /// <summary>Дії, що доступні завжди й не є прийомами каталогу (системні).</summary>
        public static readonly MatchActionId[] UniversalActions =
        {
            MatchActionId.Pin, MatchActionId.Desperate, MatchActionId.HopeSpot, MatchActionId.GetUp,
            MatchActionId.LeaveRing, MatchActionId.ReturnToRing, MatchActionId.Weapon,
            MatchActionId.Recover, MatchActionId.Ally, MatchActionId.CatchBreath
        };

        public CareerContent Content { get; }
        public IDiceRoller Dice { get; }
        public CareerState State { get; private set; }
        /// <summary>Кампанія ворожнечі з Молотом: велике шоу ворожнечі проходить її екранами.</summary>
        public RingCampaign Feud { get; private set; }
        public CareerStage Stage { get; private set; }

        private MatchEngine _engine;

        private CareerSession(CareerContent content, IDiceRoller dice, ulong seed)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Dice = dice ?? throw new ArgumentNullException(nameof(dice));
            State = new CareerState { Seed = seed };
            Feud = RingCampaign.New(content.Feud, dice);
            Stage = CareerStage.Title;
            SyncFederationRules();
        }

        public static CareerSession New(CareerContent content, IDiceRoller dice, ulong seed = 1) { return new CareerSession(content, dice, seed); }

        // ---------- довгі шкали (одне джерело — стан ворожнечі) ----------

        public WrestlerStats Player { get { return Feud.State.Player; } }
        /// <summary>Популярність 0–10 (= Audience ворожнечі).</summary>
        public int Popularity { get { return Feud.State.Audience; } }
        /// <summary>Довіра букера 0–5.</summary>
        public int Trust { get { return Feud.State.Trust; } }
        public int DevPoints { get { return Feud.State.DevPoints; } }
        public int Money { get { return State.Money; } }
        public int Season { get { return State.Season; } }
        public int Week { get { return State.Week; } }
        public bool IsChampion { get { return State.BeltHolder == CareerState.PlayerHolder; } }
        // ---------- федерація (П5; власник 11.11.2026 погодив таблиці як ПЛЕЙСХОЛДЕР) ----------

        /// <summary>Рівень федерації: 1 — місцева … 4 — світова. Рівень зали для «Изучение».</summary>
        public int FederationTier { get { return State.Federation; } }
        public FederationDef Federation { get { return Content.Federation(State.Federation); } }
        /// <summary>Стеля популярності федерації (P1-6): вище цієї федерації не піднятися — це сигнал перейти вище.</summary>
        public int PopularityCeiling { get { return Federation.Ceiling; } }
        /// <summary>Підлога популярності: нижче на кінці сезону контракт не продовжують — рівень нижче.</summary>
        public int PopularityFloor { get { return Federation.Floor; } }
        /// <summary>Проживання й дорога за тиждень у цій федерації (списується наприкінці тижня).</summary>
        public int WeeklyCost { get { return Federation.LivingCost + Federation.TravelCost; } }
        /// <summary>Ціна костюма рівня цієї федерації (разово; ажіотаж виходу — лише від костюма не нижчого за рівень федерації).</summary>
        public int CostumePrice { get { return Federation.CostumeCost; } }

        /// <summary>Чи NPC — чинний чемпіон свого дивізіону своєї федерації (для виходу на ринг і картки).</summary>
        public bool IsBeltHolder(string npcId)
        {
            var n = Content.FindNpc(npcId);
            return n != null && State.Belt(n.Tier, n.Female) == npcId;
        }

        /// <summary>
        /// Суперники, з яких букер складає картку: ростер федерації гравця, дивізіон героя (P2-6: суперник своєї статі). Якщо дивізіону
        /// у федерації немає — увесь ростер.
        /// </summary>
        public List<NpcDef> BookingPool()
        {
            var pool = new List<NpcDef>();
            foreach (var n in Federation.Roster) if (n.Female == State.Female) pool.Add(n);
            if (pool.Count == 0) pool.AddRange(Federation.Roster);
            return pool;
        }

        /// <summary>
        /// Дивізіон героя (P2-6), коли його не знав екран створення (старий сейв, чернетка без <see cref="CharacterDraft.Female"/>):
        /// хост проставляє з тіла. Змінити можна лише до першого кроку кар'єри (сезон 1, тиждень 1, жодної дії) — або один раз, поки
        /// дивізіон невідомий (старий сейв). Пояс героя (зі старого сейву, де пояс був один) переходить у його дивізіон, а пояс
        /// колишнього дивізіону повертається до стартового чемпіона; у новому дивізіоні пояс у героя, бо в старій кар'єрі він його
        /// виграв. Картку тижня букер складає наново, лише поки тиждень не почато.
        /// </summary>
        public void SetFemale(bool female)
        {
            bool changed = State.Female != female;
            bool fresh = State.Season == 1 && State.Week == 1 && State.ActionsDone.Count == 0 && Feud.Records.Count == 0 && State.Last == null;
            if (changed && State.FemaleKnown && !fresh)
                throw new InvalidOperationException("Дивізіон героя змінюють лише до першого тижня");
            if (changed && State.Belt(State.Federation, State.Female) == CareerState.PlayerHolder)
            {
                State.SetBelt(State.Federation, State.Female, Federation.BeltOf(State.Female) ?? "");
                State.SetBelt(State.Federation, female, CareerState.PlayerHolder);
            }
            State.Female = female;
            State.FemaleKnown = true;
            if (changed && Stage == CareerStage.Week && State.ActionsDone.Count == 0 && _bs == null) { BookShow(); OpenAutoScene(); }
        }

        /// <summary>Пояси всіх федерацій і дивізіонів, яких ще немає в стані, — у їхніх стартових чемпіонів.</summary>
        private void InitBelts()
        {
            foreach (var f in Content.Federations)
                for (int d = 0; d < 2; d++)
                {
                    bool female = d == 1;
                    if (!State.Belts.ContainsKey(CareerState.BeltKey(f.Tier, female))) State.SetBelt(f.Tier, female, f.BeltOf(female) ?? "");
                }
        }

        /// <summary>
        /// Перейти у федерацію рівня <paramref name="tier"/> зі статусом входу: популярність і довіра нового букера. Пояс із гравцем не
        /// переїжджає; картку тижня букер складає наново. Сам перехід (пропозиція, контракт) — <see cref="AcceptOffer"/>; тут — механіка.
        /// </summary>
        internal void EnterFederation(int tier, int popularity, int trust)
        {
            tier = Math.Max(1, Math.Min(Content.Federations.Count, tier));
            if (IsChampion) State.BeltHolder = Federation.BeltOf(State.Female) ?? "";
            State.Federation = tier;
            State.ContractExclusive = false; State.ContractEndSeason = 0; State.ContractPenalty = 0;
            SyncFederationRules();
            Feud.State.Audience = popularity;
            Feud.State.Trust = trust;
            State.TierPos = -1;
            State.Grudges.Clear();
            if (Stage == CareerStage.Week && _bs == null) BookShow();
        }

        /// <summary>Правила федерації в стані ворожнечі (стеля популярності й ажіотаж для неї) — не зберігаються, ставляться тут.</summary>
        private void SyncFederationRules()
        {
            Feud.State.AudienceCap = Federation.Ceiling;
            Feud.State.AudienceHypeAt = Federation.PopularityHype;
            Feud.State.LateRole = State.LateRole;
        }

        /// <summary>Обрати пізню роль (§5.3) — на екрані кінця сезону, з першого: одна на раз, змінити можна в кінці кожного сезону.</summary>
        public bool CanChooseLateRole { get { return Stage == CareerStage.SeasonEnd; } }

        /// <summary>Пізня роль (або <see cref="LateRole.None"/> — без неї). false — не кінець сезону.</summary>
        public bool ChooseLateRole(LateRole role)
        {
            if (!CanChooseLateRole) return false;
            State.LateRole = role;
            Feud.State.LateRole = role;
            return true;
        }

        /// <summary>Зірка поза рингом: тренувань цього тижня вже стільки, скільки дозволено (§5.3 «менше часу на тренування»).</summary>
        public bool StarTrainingUsed
        {
            get
            {
                if (State.LateRole != LateRole.OffRingStar) return false;
                int n = 0;
                foreach (var k in State.ActionsDone) if (k == (int)WeekActionKind.Training) n++;
                return n >= LateRoles.StarTrainingsPerWeek;
            }
        }

        /// <summary>Роль зараз — домінантний шлях (поворот герой ↔ лиходій — подія кар'єри ev_turn, §5.3; <see cref="Turn"/>).</summary>
        public RingPath CurrentRole { get { return Feud.State.DominantPath(); } }

        /// <summary>
        /// Найсильніша ворожнеча героя зараз — із суперниками, з яких букер складає картку (федерація й дивізіон героя; Молот — шкалою
        /// ворожнечі, поки її не розв'язано). Умова повороту ролі (§5.3: «потрібна ворожнеча 3+»).
        /// </summary>
        public int MaxFeudLevel
        {
            get
            {
                int m = 0;
                foreach (var n in BookingPool()) m = Math.Max(m, FeudLevel(n.Id));
                return m;
            }
        }

        /// <summary>
        /// Поворот ролі (§5.3, ефект сцени <c>turn</c>): очки героя й лиходія міняються місцями — зал перечитує той самий образ навиворіт,
        /// тож вага образу переходить у нову роль, а куплені перки лишаються. Після повороту нова роль домінує завжди (за рівних очок —
        /// +1 новій). Лише з протилежної ролі: бунтар (без ролі) не повертається, повернутий удруге в ту саму роль — нічого. Ставить і
        /// заявлену роль кар'єри. false — повороту не було.
        /// </summary>
        public bool Turn(RingPath to)
        {
            if (to == RingPath.Rebel) return false;
            var from = to == RingPath.Face ? RingPath.Heel : RingPath.Face;
            if (CurrentRole != from) return false;
            int a = Feud.State.GetPath(from), b = Feud.State.GetPath(to);
            Feud.State.AddPath(to, a - b);
            Feud.State.AddPath(from, b - a);
            while (CurrentRole != to) Feud.State.AddPath(to, 1);
            State.Role = to == RingPath.Face ? CareerRole.Hero : CareerRole.Villain;
            return true;
        }

        /// <summary>Матч, що йде зараз: звичайний (окремий) або матч ворожнечі на великому шоу. null — матчу немає.</summary>
        public MatchEngine Engine { get { return State.Phase == ShowPhase.Feud ? Feud.CurrentMatch : _engine; } }

        /// <summary>
        /// План, яким іде поточний матч: ворожнечі — план кампанії (після підготовки матчу, з планом за замовчуванням), титульний —
        /// без плану, щотижневий — план букера. Для бота кінцівок огляду (<see cref="FeudBot.GoalFor"/>).
        /// </summary>
        public BookerPlan CurrentMatchPlan
        {
            get { return State.Phase == ShowPhase.Feud ? Feud.State.Plan : State.IsTitle ? BookerPlan.None : State.Plan; }
        }

        /// <summary>Поточний матч — розв'язка ворожнечі з Молотом.</summary>
        public bool CurrentMatchDecisive
        {
            get { return State.Phase == ShowPhase.Feud && Feud.Stage == RingStage.Match && Feud.CurrentMatchDef.Decisive; }
        }

        public int FeudLevel(string npcId)
        {
            if (npcId == CareerContent.HammerId && !State.FeudResolved) return Feud.State.Heat;
            return State.GetNpcFeud(npcId);
        }

        private bool HammerAvailable { get { return State.FeudResolved; } }

        private void Require(CareerStage s)
        {
            if (Stage != s) throw new InvalidOperationException("Етап " + Stage + ", а потрібен " + s);
        }

        // ---------- титул і створення ----------

        public void StartNewGame()
        {
            Require(CareerStage.Title);
            Stage = CareerStage.Create;
        }

        public CreationView GetCreationView()
        {
            var v = new CreationView { CostumeCount = CostumeCount, MaxNameLength = MaxNameLength };
            foreach (var g in Content.Gimmicks)
            {
                var gv = new GimmickView { Def = g, BaseAttr = (int[])g.Attr.Clone() };
                foreach (var id in StartMoveIds(g)) gv.StartMoves.Add(Content.Moves.Get(id));
                v.Gimmicks.Add(gv);
            }
            return v;
        }

        /// <summary>Стартовий набір: 6 базових + 2 ґімік + коронний + фінішер (§5.4).</summary>
        private List<string> StartMoveIds(GimmickDef g)
        {
            var ids = new List<string>();
            foreach (var m in Content.Moves.All) if (m.Kind == MoveKind.Basic) ids.Add(m.Id);
            ids.AddRange(g.GimmickMoves);
            ids.Add(g.SignatureMove);
            ids.Add(g.FinisherMove);
            return ids;
        }

        public bool CanCreate(CharacterDraft d, out string reason)
        {
            reason = null;
            if (Stage != CareerStage.Create) { reason = "stage"; return false; }
            if (d == null) { reason = "draft"; return false; }
            string name = (d.RingName ?? "").Trim();
            if (name.Length == 0) { reason = "name"; return false; }
            if (name.Length > MaxNameLength) { reason = "name_long"; return false; }
            if (d.CostumeColor < 0 || d.CostumeColor >= CostumeCount) { reason = "costume"; return false; }
            var g = Content.GimmickOf(d.Gimmick);
            if (g.Attr[(int)d.FreePoint] + 1 > WrestlerStats.MaxAttr) { reason = "attr_max"; return false; }
            return true;
        }

        /// <summary>Створити борця й почати кар'єру з тижня 1 сезону 1.</summary>
        public void CreateHero(CharacterDraft d)
        {
            string reason;
            if (!CanCreate(d, out reason)) throw new InvalidOperationException("Створення неможливе: " + reason);
            var g = Content.GimmickOf(d.Gimmick);
            var stats = new WrestlerStats("player", "w.player.name") { FinisherAttr = g.FinisherAttr };
            for (int i = 0; i < 5; i++) stats.Set((RingAttr)i, g.Attr[i] + (i == (int)d.FreePoint ? 1 : 0));
            Feud.State.Player = stats;
            Feud.State.AddPath(d.Role == CareerRole.Hero ? RingPath.Face : RingPath.Heel, 1);

            State.RingName = d.RingName.Trim();
            State.Costume = d.CostumeColor;
            State.Gimmick = d.Gimmick;
            State.Role = d.Role;
            State.Moves.Clear();
            State.Moves.AddRange(StartMoveIds(g));
            State.Money = CareerRules.StartMoney;
            State.Federation = 1;
            State.Female = d.Female ?? false;
            State.FemaleKnown = d.Female.HasValue;
            State.Belts.Clear();
            InitBelts();
            SyncFederationRules();
            Feud.State.Audience = CareerRules.StartPopularity;
            State.Season = 1; State.Week = 1;
            Array.Clear(State.SeasonRecord, 0, State.SeasonRecord.Length);
            State.SeasonPopStart = Popularity;
            StartWeek();
            Stage = CareerStage.Week;
            OpenAutoScene();
        }

        // ---------- тиждень ----------

        private void StartWeek()
        {
            State.ActionsLeft = State.Strained ? CareerRules.StrainedActions : CareerRules.ActionsPerWeek;
            if (State.AwayTired) { State.ActionsLeft = Math.Max(1, State.ActionsLeft - 1); State.AwayTired = false; }
            State.ActionsDone.Clear();
            State.PromoChoicePending = false;
            State.Phase = ShowPhase.None;
            State.Last = null;
            BookShow();
        }

        /// <summary>Букер складає картку наступного шоу (на початку тижня: гравець бачить суперника й діє під нього).</summary>
        private void BookShow()
        {
            State.IsBig = State.IsBigWeek(State.Week);
            State.IsTitle = false;
            State.IsFeudShow = false;
            State.ShowPromoDone = false; State.ShowPromoTier = -1;
            Func<string, int> lv = FeudLevel;
            NpcDef opp;
            if (State.IsBig && !State.FeudResolved)
            {
                State.IsFeudShow = true;
                opp = Content.FindNpc(CareerContent.HammerId);
                State.Position = CardPosition.Main;
                State.Plan = BookerPlan.None;
                State.Stip = MatchStipulations.None;
            }
            else
            {
                var pool = BookingPool();
                opp = State.IsBig
                    ? ShowBooker.ChooseBig(pool, Federation, State, Popularity, HammerAvailable, lv)
                    : ShowBooker.ChooseWeekly(pool, Federation, State, Popularity, HammerAvailable, lv);
                State.Position = State.IsBig ? CardPosition.Main : CareerRules.PositionFor(Popularity, PopularityCeiling);
                if (!State.IsBig) UpdateTier();
                State.Plan = ShowBooker.PlanFor(opp, Popularity, Trust, PopularityFloor, PopularityCeiling);
                State.Stip = ShowBooker.StipulationsFor(lv(opp.Id));
            }
            State.Opponent = opp.Id;
        }

        public WeekView GetWeekView()
        {
            var v = new WeekView
            {
                Season = State.Season, Week = State.Week, WeeksInSeason = CareerRules.WeeksInSeason(State.Season),
                ActionsLeft = State.ActionsLeft, ActionsTotal = State.Strained ? CareerRules.StrainedActions : CareerRules.ActionsPerWeek,
                Strained = State.Strained, PromoChoicePending = State.PromoChoicePending, Money = State.Money, Popularity = Popularity,
                Trust = Trust, DevPoints = DevPoints, IsBigShowWeek = State.IsBigWeek(State.Week), Card = GetShowCard(),
                CanRecover = HasSomethingToRecover(), BackstageOpen = _bs != null, StarTrainingUsed = StarTrainingUsed
            };
            foreach (var k in State.ActionsDone) v.ActionsDone.Add((WeekActionKind)k);
            v.Injuries = InjuryViews();
            return v;
        }

        public ShowCardView GetShowCard()
        {
            var opp = Content.FindNpc(State.Opponent);
            var c = new ShowCardView
            {
                IsBig = State.IsBig, IsTitle = State.IsTitle, IsFeud = State.IsFeudShow, Opponent = opp,
                OpponentFeudLevel = opp == null ? 0 : FeudLevel(opp.Id), Position = State.Position, Plan = State.Plan,
                Stipulations = State.Stip, Fee = ContractFee(),
                WinBonus = CareerRules.WinBonus, HypeBonus = CareerRules.HypeBonus, PromoHypeBonus = State.NextHypeBonus,
                CanWithdraw = State.IsInjured && !State.IsBig, RivalSteps = RivalStepsNow(), IsRematch = IsRematch, IsOutgrown = !IsRematch && IsOutgrown, IsFarewell = State.Farewell
            };
            c.Injuries = InjuryViews();
            return c;
        }

        /// <summary>
        /// Ріст гравця від створення: куплені атрибути понад базу ґіміка (+1 вільний бал), перки (кожні два — одиниця) і вивчені
        /// загальні прийоми; у щаблі — опукло (<see cref="CareerRules.RivalGrowthNum"/>).
        /// </summary>
        public int PlayerGrowth()
        {
            var g = Content.GimmickOf(State.Gimmick);
            int bought = 0;
            for (int i = 0; i < 5; i++) bought += Math.Max(0, Player.Get((RingAttr)i) - g.Attr[i]);
            int learned = 0;
            foreach (var id in State.Moves) { var m = Content.Moves.Find(id); if (m != null && m.Kind == MoveKind.Learn) learned++; }
            // Перки — за ціною (вінець за 3 очки сильніший за перк за 1): кожні RivalPerksPerStep очок ціни — одиниця.
            int perkCost = 0;
            foreach (var id in Feud.State.Perks) { var pd = PerkCatalog.Find(id); perkCost += pd != null ? pd.Cost : 1; }
            int units = Math.Max(0, bought - 1) + perkCost / CareerRules.RivalPerksPerStep + learned;
            // Опукло: перші одиниці росту (вільний бал, перший перк) майже не роблять гравця сильнішим — суперник ще не росте;
            // далі — щабель на кожні ~1,6 одиниці (замір 06.11.2026: лінійно — другий сезон 40–45 %, третій 47–60 %).
            return Math.Max(0, (CareerRules.RivalGrowthNum * units - CareerRules.RivalGrowthOffset) / CareerRules.RivalGrowthDen);
        }

        private WrestlerStats Strengthened(WrestlerStats npc, MatchModifiers rivalMods)
        {
            var s = npc.Clone();
            ShowBooker.Strengthen(s, RivalStepsNow(), rivalMods);
            return s;
        }

        /// <summary>Скільки щаблів сили отримає суперник цього вечора (картка вечора показує заздалегідь).</summary>
        public int RivalStepsNow()
        {
            int growth = PlayerGrowth();
            // Кульмінації — на поточному рості: ворожнеча й титул лишаються важкими.
            if (State.IsFeudShow) return CareerRules.HammerSteps(Feud.Records.Count) + CareerRules.FeudTierSteps * (State.Federation - 1) + growth;
            if (State.IsTitle) return CareerRules.TitleSteps(State.Federation) + growth;
            // Щотижневі — на знімку росту, знятому на вході в щабель картки: виріс між підвищеннями — виграєш частіше; підвищили —
            // суперники знову твого рівня. Піки (CareerRules, «Піки сили»): реванш, а без нього — «переріс щабель картки».
            int steps = CareerRules.RivalSteps(State.Position, State.TierPos < 0 ? growth : State.TierGrowth, State.Federation);
            if (IsRematch) steps -= CareerRules.RematchSteps(State.Federation);
            else if (IsOutgrown) steps -= CareerRules.PeakSteps(State.Federation);
            return Math.Max(CareerRules.RivalStepsFloor, steps);
        }

        /// <summary>Цього вечора — реванш із тим, хто переміг гравця останнього разу.</summary>
        public bool IsRematch { get { return !State.IsFeudShow && !State.IsTitle && State.Grudges.Contains(State.Opponent); } }

        /// <summary>
        /// Гравець переріс свій щабель картки: після знімка щабля виріс щонайменше на <see cref="CareerRules.PeakLag"/> щаблі, а
        /// підвищення ще не було. Лише щотижневе шоу (велике, титул і ворожнеча — кульмінації, лишаються важкими).
        /// </summary>
        public bool IsOutgrown
        {
            get
            {
                return !State.IsFeudShow && !State.IsTitle && !State.IsBig && State.TierPos >= 0
                       && PlayerGrowth() - State.TierGrowth >= CareerRules.PeakLag;
            }
        }

        /// <summary>Сходинка: на новому (вищому) щаблі картки — знімок поточного росту; на нижчому — щабель знижено, знімок той самий.</summary>
        private void UpdateTier()
        {
            int pos = (int)State.Position;
            if (pos > State.TierPos || pos == (int)CardPosition.Main) State.TierGrowth = PlayerGrowth();
            State.TierPos = pos;
        }

        // ---------- сітки витрат (не займають дій тижня) ----------

        /// <summary>Костюм рівня федерації (П5): разово; старий костюм нижчого рівня ажіотажу виходу вже не дає.</summary>
        public bool CanBuyCostume { get { return Stage == CareerStage.Week && !State.CostumeBought && State.Money >= CostumePrice; } }

        public bool BuyCostume()
        {
            if (!CanBuyCostume) return false;
            State.Money -= CostumePrice; State.CostumeTier = State.Federation;
            return true;
        }

        /// <summary>Найняти тренера ростера (0..2) або звільнити (−1). Плата — щотижня в кінці тижня; перший тиждень — одразу.</summary>
        public bool HireCoach(int coach)
        {
            if (Stage != CareerStage.Week) return false;
            if (coach < 0) { State.Coach = -1; return true; }
            if (coach >= CareerRules.Coaches.Length || coach == State.Coach || State.Money < CareerRules.CoachWeeklyFee) return false;
            State.Money -= CareerRules.CoachWeeklyFee;
            State.Coach = coach; State.CoachProgress = 0;
            return true;
        }

        public bool CanBuyInsurance { get { return Stage == CareerStage.Week && State.InsuredSeason != State.Season && State.Money >= CareerRules.InsuranceCost; } }

        public bool BuyInsurance()
        {
            if (!CanBuyInsurance) return false;
            State.Money -= CareerRules.InsuranceCost; State.InsuredSeason = State.Season;
            return true;
        }

        /// <summary>Клініка: травма зони знімається повністю (лікар другого рівня). Не займає дії тижня.</summary>
        public bool CanClinic(BodyZone z) { return Stage == CareerStage.Week && State.Money >= CareerRules.ClinicCost && (State.Injury[(int)z] > 0 || State.ZoneDamage[(int)z] > 0); }

        public bool Clinic(BodyZone z)
        {
            if (!CanClinic(z)) return false;
            State.Money -= CareerRules.ClinicCost;
            State.Injury[(int)z] = 0; State.ZoneDamage[(int)z] = 0;
            return true;
        }

        /// <summary>Виїзне шоу цього тижня (не велике й не титульне): дорога зараз, гонорар ×2, наступного тижня на дію менше.</summary>
        public bool CanGoAway
        {
            get { return Stage == CareerStage.Week && !State.AwayShow && !State.IsBig && !ExclusiveActive && State.Money >= CareerRules.AwayTravelCost; }
        }

        public bool GoAway()
        {
            if (!CanGoAway) return false;
            State.Money -= CareerRules.AwayTravelCost; State.AwayShow = true;
            return true;
        }

        private List<InjuryView> InjuryViews()
        {
            var l = new List<InjuryView>();
            for (int z = 0; z < 3; z++) l.Add(new InjuryView { Zone = (BodyZone)z, ZoneDamage = State.ZoneDamage[z], WeeksLeft = State.Injury[z] });
            return l;
        }

        private bool HasSomethingToRecover()
        {
            for (int z = 0; z < 3; z++) if (State.Injury[z] > 0 || State.ZoneDamage[z] > 0) return true;
            return false;
        }

        private WeekActionResult Begin(WeekActionKind kind)
        {
            var r = new WeekActionResult { Kind = kind };
            if (Stage != CareerStage.Week) { r.Reason = "stage"; return r; }
            if (_bs != null) { r.Reason = "scene_open"; return r; }
            if (State.PromoChoicePending) { r.Reason = "choice_pending"; return r; }
            if (State.ActionsLeft <= 0) { r.Reason = "no_actions"; return r; }
            r.Ok = true;
            return r;
        }

        private WeekActionResult Done(WeekActionResult r)
        {
            if (!r.Ok) return r;
            State.ActionsLeft--;
            State.ActionsDone.Add((int)r.Kind);
            State.ActionTally[(int)r.Kind]++;
            return r;
        }

        private void Roll(WeekActionResult r, RingAttr attr, BodyZone zone, string stream)
        {
            r.HasRoll = true;
            r.Roll = TwoD6.Roll(Dice, stream);
            r.Modifier = Player.Get(attr) - State.InjuryPenaltyFor(zone);
            r.Total = r.Roll + r.Modifier;
            r.Tier = TwoD6.TierOf(r.Total);
        }

        /// <summary>Тренування (§6.1): +1 очко розвитку; з тренером — +1 понад те за гроші.</summary>
        public WeekActionResult Train(bool withTrainer)
        {
            var r = Begin(WeekActionKind.Training);
            if (r.Ok && StarTrainingUsed) { r.Ok = false; r.Reason = "star_training"; }
            if (r.Ok && withTrainer && State.Money < CareerRules.TrainerCost) { r.Ok = false; r.Reason = "money"; }
            if (!r.Ok) return r;
            int pts = CareerRules.TrainingPoints;
            if (withTrainer)
            {
                State.Money -= CareerRules.TrainerCost; r.MoneyDelta = -CareerRules.TrainerCost;
                pts += CareerRules.TrainerExtraPoints;
            }
            Feud.State.DevPoints += pts; r.DevPointsDelta = pts;
            if (State.Coach >= 0)
            {
                var a = CareerRules.Coaches[State.Coach];
                if (++State.CoachProgress >= CareerRules.CoachProgressStep && Player.Get(a) < WrestlerStats.MaxAttr)
                {
                    State.CoachProgress = 0;
                    Player.Set(a, Player.Get(a) + 1);
                    r.CoachRaised = true;
                }
            }
            return Done(r);
        }

        /// <summary>Відновлення (§6.1): ушкодження зони −2, травма −1 тиждень (з лікарем −2, за гроші).</summary>
        public WeekActionResult Recover(BodyZone zone, bool withDoctor)
        {
            var r = Begin(WeekActionKind.Recovery);
            int z = (int)zone;
            if (r.Ok && State.Injury[z] <= 0 && State.ZoneDamage[z] <= 0) { r.Ok = false; r.Reason = "nothing"; }
            if (r.Ok && withDoctor && State.Money < CareerRules.DoctorCost) { r.Ok = false; r.Reason = "money"; }
            if (!r.Ok) return r;
            if (withDoctor) { State.Money -= CareerRules.DoctorCost; r.MoneyDelta = -CareerRules.DoctorCost; }
            State.ZoneDamage[z] = Math.Max(0, State.ZoneDamage[z] - CareerRules.RecoverZoneHeal);
            if (State.Injury[z] > 0) State.Injury[z] = Math.Max(0, State.Injury[z] - (withDoctor ? 2 : 1));
            return Done(r);
        }

        /// <summary>
        /// Промо (§6.1): Харизма 2d6. 10+ — ворожнеча з суперником тижня +1 і ажіотаж старту +2; 7–9 — одне з двох
        /// (<see cref="ChoosePromoReward"/>); 6 і менше — «зірвав промо»: ажіотаж −1, суперник стартує з імпульсом +1.
        /// </summary>
        public WeekActionResult Promo()
        {
            var r = Begin(WeekActionKind.Promo);
            if (!r.Ok) return r;
            Roll(r, RingAttr.Charisma, BodyZone.Head, "career:promo");
            if (State.LateRole == LateRole.OffRingStar && r.Tier != RollTier.Miss)
            {
                // Зірка поза рингом: промо — ще й заробіток
                int pay = r.Tier == RollTier.Strong ? LateRoles.StarPromoMoneyStrong : LateRoles.StarPromoMoneyPartial;
                State.Money += pay; r.MoneyDelta += pay;
            }
            switch (r.Tier)
            {
                case RollTier.Strong:
                    r.FeudDelta = RaiseFeud(State.Opponent); r.HypeBonusDelta = CareerRules.PromoStrongHype;
                    State.NextHypeBonus += CareerRules.PromoStrongHype;
                    break;
                case RollTier.Partial:
                    State.PromoChoicePending = true; r.NeedsChoice = true;
                    break;
                default:
                    r.HypeBonusDelta = -1; State.NextHypeBonus -= 1;
                    State.RivalBoost += 1; r.RivalBoosted = true;
                    break;
            }
            return Done(r);
        }

        /// <summary>Нагорода промо на «7–9»: або ворожнеча +1, або ажіотаж старту +2.</summary>
        public WeekActionResult ChoosePromoReward(WeekPromoReward reward)
        {
            var r = new WeekActionResult { Kind = WeekActionKind.Promo };
            if (Stage != CareerStage.Week || !State.PromoChoicePending) { r.Reason = "stage"; return r; }
            State.PromoChoicePending = false;
            if (reward == WeekPromoReward.Feud) r.FeudDelta = RaiseFeud(State.Opponent);
            else { State.NextHypeBonus += CareerRules.PromoStrongHype; r.HypeBonusDelta = CareerRules.PromoStrongHype; }
            r.Ok = true;
            return r;
        }

        private int RaiseFeud(string npcId)
        {
            int before = FeudLevel(npcId);
            if (npcId == CareerContent.HammerId && !State.FeudResolved) Feud.State.Heat += 1;
            else State.SetNpcFeud(npcId, before + 1);
            return FeudLevel(npcId) - before;
        }

        /// <summary>
        /// Закулісся (§6.1, §7): відкриває сцену — розмову з букером або подію поза рингом (<see cref="CurrentBackstage"/>, граф у
        /// форматі промо; кидки — <see cref="BackstageRollCheck"/>). Вибір сцени детермінований і залежить від стану: подія, що
        /// «дозріла» цього тижня й чиї умови виконані, — раз на сезон; інакше розмова з букером. Дію тижня витрачає
        /// <see cref="FinishBackstage"/>, до того інші дії й кінець тижня недоступні (Reason «scene_open»). Незавершена сцена не
        /// зберігається: після відновлення з сейва дія не витрачена, а сцена відкривається з початку.
        /// Без career_backstage.json у вмісті діє стара дія: Харизма 2d6, 10+ — довіра +1 і чутка, 7–9 — чутка.
        /// </summary>
        public WeekActionResult Backstage()
        {
            if (Content.Backstage == null) return BackstageDice();
            var r = Begin(WeekActionKind.Backstage);
            if (!r.Ok) return r;
            var scene = PickBackstageScene();
            OpenBackstage(scene);
            r.NeedsScene = true; r.SceneId = scene.Id; r.SceneKind = scene.Kind;
            return r;
        }

        private WeekActionResult BackstageDice()
        {
            var r = Begin(WeekActionKind.Backstage);
            if (!r.Ok) return r;
            Roll(r, RingAttr.Charisma, BodyZone.Head, "career:backstage");
            if (r.Tier == RollTier.Strong)
            {
                int before = Trust;
                Feud.State.Trust += 1;
                r.TrustDelta = Trust - before;
            }
            if (r.Tier != RollTier.Miss) r.RumorKey = RumorKey();
            return Done(r);
        }

        // ---------- закулісні сцени ----------

        /// <summary>Сцена, що йде: робоча копія стану ворожнеччі (сцена не чіпає справжній стан до завершення) і відкладені ефекти кар'єри.</summary>
        private sealed class BackstageRun
        {
            public BackstageScene Scene;
            public PromoRunner Runner;
            public FeudState Work;
            public readonly List<FeudEffect> Pending = new List<FeudEffect>();
            public int Money;
            public int Actions;
            /// <summary>Подія відкрилась сама (<see cref="BackstageScene.Auto"/>): дії тижня не витрачає.</summary>
            public bool Auto;
        }

        private BackstageRun _bs;

        /// <summary>Іде закулісна сцена.</summary>
        public bool BackstageOpen { get { return _bs != null; } }

        /// <summary>Курсор по графу відкритої сцени (репліки, вибори, кидки); null — сцени немає.</summary>
        public PromoRunner CurrentBackstage { get { return _bs == null ? null : _bs.Runner; } }

        /// <summary>Опис відкритої сцени (вид, id); null — сцени немає.</summary>
        public BackstageScene CurrentBackstageScene { get { return _bs == null ? null : _bs.Scene; } }

        /// <summary>Кидок вузла check відкритої сцени через кубик кар'єри (той самий потік, що й решта кидків).</summary>
        public RollTier BackstageRollCheck()
        {
            if (_bs == null) throw new InvalidOperationException("Закулісної сцени немає");
            return _bs.Runner.RollCheck(Dice);
        }

        private static string SeenKey(int season, string sceneId) { return season.ToString(CultureInfo.InvariantCulture) + ":" + sceneId; }

        /// <summary>Подія була цього сезону.</summary>
        public bool BackstageSeenThisSeason(string sceneId) { return State.BackstageSeen.Contains(SeenKey(State.Season, sceneId)); }

        /// <summary>Подія була хоч раз за кар'єру (для сцен «раз на кар'єру», <see cref="BackstageScene.Once"/>).</summary>
        public bool BackstageSeenEver(string sceneId)
        {
            string tail = ":" + sceneId;
            foreach (var k in State.BackstageSeen) if (k.EndsWith(tail, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Подія була в одному з минулих сезонів (умова сцени seen_before, §7: «повернення ветерана» — через сезон після травми).</summary>
        public bool BackstageSeenBefore(string sceneId)
        {
            string tail = ":" + sceneId;
            foreach (var k in State.BackstageSeen)
            {
                if (!k.EndsWith(tail, StringComparison.Ordinal)) continue;
                int season;
                if (int.TryParse(k.Substring(0, k.Length - tail.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out season) && season < State.Season)
                    return true;
            }
            return false;
        }

        /// <summary>Тиждень сезону, з якого подія доступна (детермінований розкид за зерном кар'єри; не пізніше останнього тижня).</summary>
        public int BackstageDueWeek(BackstageScene sc)
        {
            int idx = Content.Backstage.Scenes.IndexOf(sc);
            int spread = sc.Spread > 1 ? ShowBooker.Mix(State.Seed, State.Season * 10, 40 + idx) % sc.Spread : 0;
            return Math.Min(CareerRules.WeeksInSeason(State.Season), sc.FromWeek + spread);
        }

        private bool SceneHolds(BackstageScene sc)
        {
            foreach (var c in sc.Requires)
            {
                bool ok = c.Kind == ConditionKind.Career ? ProbeCareer(c, 0, 0) != c.Negate : Feud.State.Check(c);
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// Сцена, яку відкриє наступне «Закулісся» замість звичайного вибору (огляд QA: <c>-ring-shot-event</c>,
        /// <c>-ring-shot-backstage all</c>) — без умов, тижня й «раз за сезон». Одноразова: скидається, щойно сцену відкрито.
        /// У сейв не входить.
        /// </summary>
        public string ForcedBackstageScene { get; private set; }

        /// <summary>Примусити сцену наступного «Закулісся» (null — скасувати). Невідомий id — виняток.</summary>
        public void ForceBackstage(string sceneId)
        {
            if (sceneId != null && (Content.Backstage == null || Content.Backstage.Find(sceneId) == null))
                throw new ArgumentException("немає закулісної сцени '" + sceneId + "'");
            ForcedBackstageScene = sceneId;
        }

        /// <summary>
        /// Огляд QA (<c>-ring-shot-opponent</c>, рев'ю ПМ 12.11.2026 — найдовше ім'я на табло): суперник цього тижня замість обраного
        /// букером. Лише на початку звичайного тижня (жодної дії, сцени, не велике шоу й не титул) і лише з пулу букера (дивізіон
        /// героя; Молот — лише після розв'язки ворожнечі). План і умови — ті, що склав би букер для цього суперника. false — підмінити
        /// не можна, картка не змінюється. Сама картка йде в сейв як звичайна.
        /// </summary>
        public bool ForceOpponent(string npcId)
        {
            if (Stage != CareerStage.Week || State.ActionsDone.Count > 0 || _bs != null || State.IsBig || State.IsTitle || State.IsFeudShow) return false;
            NpcDef npc = null;
            foreach (var n in BookingPool()) if (n.Id == npcId) npc = n;
            if (npc == null || (npc.Id == CareerContent.HammerId && !HammerAvailable)) return false;
            Func<string, int> lv = FeudLevel;
            State.Opponent = npc.Id;
            State.Plan = ShowBooker.PlanFor(npc, Popularity, Trust, PopularityFloor, PopularityCeiling);
            State.Stip = ShowBooker.StipulationsFor(lv(npc.Id));
            return true;
        }

        private BackstageScene PickBackstageScene()
        {
            var all = Content.Backstage.Scenes;
            if (ForcedBackstageScene != null)
            {
                var forced = Content.Backstage.Find(ForcedBackstageScene);
                ForcedBackstageScene = null;
                if (forced != null) return forced;
            }
            foreach (var sc in all)
            {
                if (sc.Kind != BackstageKind.Event || sc.Auto != BackstageAuto.None) continue;
                if (BackstageSeenThisSeason(sc.Id) || State.Season < sc.MinSeason || State.Week < BackstageDueWeek(sc)) continue;
                if (sc.Once && BackstageSeenEver(sc.Id)) continue;
                if (SceneHolds(sc)) return sc;
            }
            // розмова з букером: спершу та, чиї умови виконано (свій букер федерації); запасна без умов — коли жодна не підходить
            BackstageScene fallback = null;
            foreach (var sc in all)
            {
                if (sc.Kind != BackstageKind.Booker || !SceneHolds(sc)) continue;
                if (sc.Requires.Count > 0) return sc;
                if (fallback == null) fallback = sc;
            }
            if (fallback != null) return fallback;
            throw new InvalidOperationException("Немає закулісної сцени для відкриття");
        }

        /// <summary>Іде подія, що відкрилась сама (подія мішаного матчу): після неї дій тижня стільки ж, скільки було.</summary>
        public bool AutoSceneOpen { get { return _bs != null && _bs.Auto; } }

        /// <summary>
        /// Подія, що відкривається сама (<see cref="BackstageAuto.FeudFirst"/>, P2-6, ПМ 12.11.2026: «Молот — єдиний мішаний суперник
        /// кар'єри, перший матч ворожнечі відкриває подія мішаного матчу»): на початку тижня першого шоу ворожнечі (жодної дії,
        /// сцени, матчу ворожнечі ще не було), раз на кар'єру й лише за її умов (героїня — <c>{"career":"female"}</c>). Відкривається
        /// на початку тижня, після відновлення з сейва — знову з початку.
        /// </summary>
        private void OpenAutoScene()
        {
            if (Content.Backstage == null || _bs != null || Stage != CareerStage.Week || State.ActionsDone.Count > 0) return;
            if (!State.IsFeudShow || State.FeudResolved || Feud.Records.Count > 0) return;
            foreach (var sc in Content.Backstage.Scenes)
            {
                if (sc.Auto != BackstageAuto.FeudFirst || BackstageSeenEver(sc.Id) || !SceneHolds(sc)) continue;
                OpenBackstage(sc);
                _bs.Auto = true;
                return;
            }
        }

        private void OpenBackstage(BackstageScene sc)
        {
            var run = new BackstageRun { Scene = sc };
            var work = new FeudState();
            work.RestoreState(Feud.State.CaptureState());
            work.CareerProbe = c => ProbeCareer(c, run.Money, run.Actions);
            work.CareerSink = e =>
            {
                run.Pending.Add(e);
                if (e.Key == "money") run.Money += e.Amount;
                else if (e.Key == "actions") run.Actions += e.Amount;
            };
            run.Work = work;
            run.Runner = new PromoRunner(Content.Feud, sc.Promo, work);
            _bs = run;
        }

        /// <summary>Умови кар'єри для сцен (без урахування «not»: його застосовує FeudState). Гроші й дії враховують уже обіцяне сценою.</summary>
        private bool ProbeCareer(FeudCondition c, int pendingMoney, int pendingActions)
        {
            switch (c.Key)
            {
                case "money": return State.Money + pendingMoney >= c.Min;
                case "season": return State.Season >= c.Min;
                case "tier": return State.Federation >= c.Min;
                case "week": return State.Week >= c.Min;
                case "parity": return State.Week % 2 == c.Min;
                case "injured": return State.IsInjured;
                case "female": return State.Female;
                case "manager": return State.Manager;
                case "booker": return Federation.Booker == c.Target;
                case "role":
                    RingPath rp;
                    return FeudVars.TryPath(c.Target, out rp) && CurrentRole == rp;
                case "max_feud": return MaxFeudLevel >= c.Min;
                case "seen_before": return BackstageSeenBefore(c.Target);
                case "champion": return IsChampion;
                case "last_won": return State.LastOutcome == 1;
                case "last_lost": return State.LastOutcome == 2;
                case "last_draw": return State.LastOutcome == 3;
                case "last_swerved": return State.LastPlanOutcome == FeudPlanOutcome.Swerved;
                case "plan_set": return State.Plan != BookerPlan.None && !State.IsFeudShow && !State.IsTitle;
                case "title_eligible":
                    Func<string, int> lv = FeudLevel;
                    return ShowBooker.TitleEligible(BookingPool(), State, Trust, HammerAvailable, lv);
                case "actions": return State.ActionsLeft + pendingActions >= c.Min;
                case "npc_feud": return FeudLevel(c.Target) >= c.Min;
                default: return false;
            }
        }

        /// <summary>
        /// Закінчити сцену: зміни шкал і прапорів із робочої копії переходять у стан, відкладені ефекти кар'єри (гроші, травма зони,
        /// ворожнеча з NPC, ажіотаж, план наступного шоу, дії тижня, очки розвитку) застосовуються, дія тижня витрачається рівно одна
        /// (плюс дії, які сцена забрала сама, — «витратити день»). Повертає дельти для екрана. Сцена мусить бути дійденою до кінця.
        /// </summary>
        public WeekActionResult FinishBackstage()
        {
            if (_bs == null) throw new InvalidOperationException("Закулісної сцени немає");
            if (!_bs.Runner.IsFinished) throw new InvalidOperationException("Сцену ще не закінчено");
            Require(CareerStage.Week);
            var run = _bs;
            var r = new WeekActionResult { Kind = WeekActionKind.Backstage, Ok = true, SceneId = run.Scene.Id, SceneKind = run.Scene.Kind };
            int pop0 = Popularity, trust0 = Trust, locker0 = Feud.State.Locker, dev0 = DevPoints;
            Feud.State.AbsorbSceneResult(run.Work);
            int extraActions = 0;
            foreach (var e in run.Pending) ApplyCareerEffect(e, r, ref extraActions);
            r.PopularityDelta = Popularity - pop0; r.TrustDelta = Trust - trust0; r.LockerDelta = Feud.State.Locker - locker0;
            r.DevPointsDelta = DevPoints - dev0;
            State.BackstageSeen.Add(SeenKey(State.Season, run.Scene.Id));
            _bs = null;
            if (run.Scene.Kind == BackstageKind.Booker) r.RumorKey = RumorKey();
            if (!run.Auto) Done(r);
            if (extraActions < 0)
            {
                int spend = Math.Min(State.ActionsLeft, -extraActions);
                State.ActionsLeft -= spend;
                for (int i = 0; i < spend; i++) State.ActionsDone.Add((int)WeekActionKind.Backstage);
                r.ActionsDelta = -spend;
            }
            else if (extraActions > 0)
            {
                int gain = Math.Min(extraActions, CareerRules.ActionsPerWeek - State.ActionsLeft);
                State.ActionsLeft += Math.Max(0, gain);
                r.ActionsDelta = Math.Max(0, gain);
            }
            return r;
        }

        private void ApplyCareerEffect(FeudEffect e, WeekActionResult r, ref int extraActions)
        {
            switch (e.Key)
            {
                case "money":
                    int m0 = State.Money;
                    State.Money = Math.Max(0, State.Money + e.Amount);
                    r.MoneyDelta += State.Money - m0;
                    break;
                case "injure":
                    int zone = e.Target == "head" ? 0 : e.Target == "body" ? 1 : e.Target == "legs" ? 2
                        : ShowBooker.Mix(State.Seed, State.Season * 100 + State.Week, 17) % 3;
                    if (AddZoneDamage(zone, e.Amount) && r.InjuredZone < 0) r.InjuredZone = zone;
                    break;
                case "npc_feud":
                    string id = e.Target == "opponent" ? State.Opponent : e.Target == "last_opponent" ? State.LastOpponent : e.Target;
                    if (string.IsNullOrEmpty(id) || Content.FindNpc(id) == null) break;
                    int before = FeudLevel(id);
                    if (id == CareerContent.HammerId && !State.FeudResolved) Feud.State.Heat += e.Amount;
                    else State.SetNpcFeud(id, before + e.Amount);
                    r.FeudDelta += FeudLevel(id) - before;
                    break;
                case "hype":
                    State.NextHypeBonus += e.Amount; r.HypeBonusDelta += e.Amount;
                    break;
                case "boost":
                    State.RivalBoost = Math.Max(0, State.RivalBoost + e.Amount);
                    break;
                case "plan":
                    BookerPlan p;
                    if (!State.IsFeudShow && !State.IsTitle && FeudVars.TryPlan(e.Target, out p)) { State.Plan = p; r.PlanChanged = true; }
                    break;
                case "actions":
                    extraActions += e.Amount;
                    break;
                case "dev":
                    Feud.State.DevPoints += e.Amount;
                    break;
                case "manager":
                    State.Manager = e.Amount > 0;
                    break;
                case "turn":
                    RingPath to;
                    if (FeudVars.TryPath(e.Target, out to) && Turn(to)) r.TurnedTo = to;
                    break;
            }
        }

        private string RumorKey()
        {
            Func<string, int> lv = FeudLevel;
            if (State.IsFeudShow) return "rumor.hammer_big";
            if (ShowBooker.TitleEligible(BookingPool(), State, Trust, HammerAvailable, lv)) return IsChampion ? "rumor.defense" : "rumor.title_shot";
            if (Trust < 2) return "rumor.booker_cold";
            return State.IsBig ? "rumor.big_show" : "rumor.next_opponent";
        }

        /// <summary>Підробіток (§6.1): гроші, ризик травми, жодного досвіду. Сума 2d6 ≤ 4 — випадкова зона дістає ушкодження 2.</summary>
        public WeekActionResult SideJob()
        {
            var r = Begin(WeekActionKind.SideJob);
            if (!r.Ok) return r;
            if (ExclusiveActive) { r.Ok = false; r.Reason = "exclusive"; return r; }
            int gain = CareerRules.SideJobBase + 5 * Math.Max(0, Player.Get(RingAttr.Stamina));
            State.Money += gain; r.MoneyDelta = gain;
            r.HasRoll = true;
            r.Roll = TwoD6.Roll(Dice, "career:sidejob");
            r.Total = r.Roll; r.Tier = TwoD6.TierOf(r.Total);
            if (r.Roll <= CareerRules.SideJobInjuryAtMost)
            {
                int zone = ShowBooker.Mix(State.Seed, State.Season * 100 + State.Week, 7) % 3;
                if (AddZoneDamage(zone, CareerRules.SideJobZoneDamage)) r.InjuredZone = zone;
            }
            Feud.State.DevPoints += CareerRules.SideJobPoints;
            return Done(r);
        }

        /// <summary>Нарахувати ушкодження зони; на порозі — травма 1–4 тижні. true, якщо виникла нова травма.</summary>
        private bool AddZoneDamage(int zone, int amount)
        {
            if (amount <= 0 || State.Injury[zone] > 0) return false;
            State.ZoneDamage[zone] += amount;
            if (State.ZoneDamage[zone] < CareerRules.InjuryAtZoneDamage) return false;
            State.ZoneDamage[zone] = 0;
            int span = CareerRules.MaxInjuryWeeks - CareerRules.MinInjuryWeeks + 1;
            State.Injury[zone] = CareerRules.MinInjuryWeeks + ShowBooker.Mix(State.Seed, State.Season * 100 + State.Week, 11 + zone) % span;
            if (State.InsuredSeason == State.Season) State.Injury[zone] = Math.Max(1, State.Injury[zone] - 1);
            return true;
        }

        /// <summary>Закінчити планування: решта дій тижня пропадає, починається вечір (шоу).</summary>
        public void EndWeek()
        {
            Require(CareerStage.Week);
            if (_bs != null) throw new InvalidOperationException("Спершу закінчіть закулісну сцену");
            if (State.PromoChoicePending) throw new InvalidOperationException("Спершу оберіть нагороду промо");
            Stage = CareerStage.Show;
            State.Phase = ShowPhase.Card;
            State.Last = null;
        }

        // ---------- розвиток ----------

        // Розвиток — на тижні, на підсумку шоу (одразу після матчу, 06.11.2026) і наприкінці сезону.
        private bool CanDevelop { get { return (Stage == CareerStage.Week && _bs == null) || Stage == CareerStage.SeasonEnd || (Stage == CareerStage.Show && State.Phase == ShowPhase.Result); } }

        public bool CanBuyAttr(RingAttr a, out string reason)
        {
            if (!CanDevelop) { reason = "stage"; return false; }
            return Development.CanBuyAttr(Feud.State, a, out reason);
        }

        public bool BuyAttr(RingAttr a)
        {
            string reason;
            return CanBuyAttr(a, out reason) && Development.BuyAttr(Feud.State, a);
        }

        public bool CanBuyPerk(string perkId, out string reason)
        {
            if (!CanDevelop) { reason = "stage"; return false; }
            return Development.CanBuyPerk(Feud.State, perkId, out reason);
        }

        public bool BuyPerk(string perkId)
        {
            string reason;
            return CanBuyPerk(perkId, out reason) && Development.BuyPerk(Feud.State, perkId);
        }

        public bool KnowsMove(string id) { return State.Moves.Contains(id); }

        /// <summary>Вивчити новий прийом (§5.5): ґімік-прийоми й загальні, не коронні й не фінішери.</summary>
        public bool CanLearnMove(string moveId, out string reason)
        {
            reason = null;
            if (!CanDevelop) { reason = "stage"; return false; }
            var m = Content.Moves.Find(moveId);
            if (m == null) { reason = "unknown"; return false; }
            if (!m.IsLearnable) { reason = "not_learnable"; return false; }
            if (KnowsMove(moveId)) { reason = "owned"; return false; }
            if (DevPoints < m.Cost) { reason = "points"; return false; }
            return true;
        }

        public bool LearnMove(string moveId)
        {
            string reason;
            if (!CanLearnMove(moveId, out reason)) return false;
            Feud.State.DevPoints -= Content.Moves.Get(moveId).Cost;
            State.Moves.Add(moveId);
            return true;
        }

        public CareerView GetCareerView()
        {
            var v = new CareerView
            {
                RingName = State.RingName, Costume = State.Costume, Gimmick = State.Gimmick, Role = CurrentRole, Money = State.Money,
                Popularity = Popularity, Trust = Trust, DevPoints = DevPoints, IsChampion = IsChampion, Season = State.Season,
                Week = State.Week, TitlesWon = State.TitlesWon, FiredCount = State.FiredCount, FeudResolved = State.FeudResolved,
                LateRole = State.LateRole
            };
            for (int i = 0; i < 5; i++) v.Attr[i] = Player.Get((RingAttr)i);
            foreach (var id in State.Moves) v.Moves.Add(Content.Moves.Get(id));
            v.Perks.AddRange(Feud.State.Perks);
            v.Perks.Sort(StringComparer.Ordinal);
            v.Injuries = InjuryViews();
            return v;
        }

        // ---------- шоу ----------

        private void RequireShow(ShowPhase p)
        {
            Require(CareerStage.Show);
            if (State.Phase != p) throw new InvalidOperationException("Фаза шоу " + State.Phase + ", а потрібна " + p);
        }

        /// <summary>
        /// Почати вечір. Звичайне шоу — лише матч, без промо (власник 04.11.2026: «промо тільки в фьюді, інши не треба») —
        /// повертає рушій (Phase=Match).
        /// Велике шоу ворожнечі: Phase=Feud, повертає null — далі екрани <see cref="Feud"/> (промо, підготовка), матч — через
        /// <see cref="StartFeudMatch"/>.
        /// </summary>
        public MatchEngine StartShow()
        {
            RequireShow(ShowPhase.Card);
            State.DevPointsAtMatchStart = DevPoints;
            State.Last = null;
            if (State.IsFeudShow)
            {
                State.Phase = ShowPhase.Feud;
                return null;
            }
            var npc = Content.FindNpc(State.Opponent);
            var mods = Development.ToModifiers(Feud.State.Perks);
            LateRoles.ApplyTo(mods, State.LateRole);
            var rivalMods = new MatchModifiers();
            int hype = 1 + FeudLevel(npc.Id) + mods.StartHypeBonus + State.NextHypeBonus + (State.CostumeBought ? CareerRules.CostumeHype : 0)
                       + (State.IsInjured ? CareerRules.InjuredShowHype : 0);
            var setup = new MatchSetup
            {
                Player = Player.Clone(), Rival = Strengthened(npc.Stats, rivalMods), PlayerMods = mods, RivalMods = rivalMods,
                PlayerStartMomentum = Math.Max(0, Math.Min(10, mods.StartMomentumBonus)),
                RivalStartMomentum = Math.Max(0, Math.Min(10, npc.StartMomentum + State.RivalBoost + rivalMods.StartMomentumBonus)),
                StartHype = Math.Max(0, Math.Min(10, hype)), PlayerHasAlly = false, Stipulations = State.Stip,
                Plan = State.IsTitle ? BookerPlan.None : State.Plan, PlayerRole = Feud.State.DominantPath(), RivalRole = npc.Path,
                HopeSpotEnabled = true,
                // фінішер для показу — прийом ґіміка (3-P1-3): ключ і постановка, без бонусів до кидка
                PlayerFinisher = Content.FinisherShow(State.Gimmick), RivalFinisher = Content.FinisherShow(npc.Gimmick)
            };
            _engine = new MatchEngine(setup, Dice);
            State.Phase = ShowPhase.Match;
            return _engine;
        }

        /// <summary>Велике шоу ворожнечі: матч Молота з доповненнями кар'єри (промінь надії; ажіотаж і імпульс від промо тижня).</summary>
        public MatchEngine StartFeudMatch()
        {
            RequireShow(ShowPhase.Feud);
            int steps = RivalStepsNow();
            var e = Feud.StartMatch();
            e.Setup.HopeSpotEnabled = true;
            e.Setup.PlayerFinisher = Content.FinisherShow(State.Gimmick);
            e.Setup.RivalFinisher = Content.FinisherShow(Content.FindNpc(CareerContent.HammerId).Gimmick);
            // Молот росте від матчу до матчу ворожнечі (сила за місцем у картці: велике шоу — мейн-івент).
            var hammer = e.State.Rival.Stats.Clone();
            var over = new MatchModifiers();
            ShowBooker.Strengthen(hammer, steps, over);
            e.State.Rival.Stats = hammer;
            // Понад межу атрибутів — витримка: рушій уже створено, тож жетони додаються одразу, решта — через модифікатори.
            var rm = e.Setup.RivalMods ?? (e.Setup.RivalMods = new MatchModifiers());
            rm.KickOutBonus += over.KickOutBonus;
            rm.DamageThresholdShift += over.DamageThresholdShift;
            e.State.Rival.ReversalTokens += over.ExtraReversalTokens;
            e.State.Rival.Momentum = Math.Min(MatchRules.MaxMomentum, e.State.Rival.Momentum + over.StartMomentumBonus);
            // костюм рівня федерації — і на великому шоу ворожнечі (П5: раніше лише в StartShow)
            e.State.Hype = Math.Max(0, Math.Min(MatchRules.MaxHype, e.State.Hype + State.NextHypeBonus + (State.IsInjured ? CareerRules.InjuredShowHype : 0)
                + (State.CostumeBought ? CareerRules.CostumeHype : 0)));
            e.State.Rival.Momentum = Math.Max(0, Math.Min(MatchRules.MaxMomentum, e.State.Rival.Momentum + State.RivalBoost));
            State.DevPointsAtMatchStart = DevPoints;
            return e;
        }

        /// <summary>Зійти з вечора травмованим (лише не велике шоу): без гонорару, довіра букера −1, травми лікуються швидше.</summary>
        public void WithdrawFromShow()
        {
            RequireShow(ShowPhase.Card);
            if (!State.IsInjured || State.IsBig) throw new InvalidOperationException("Зійти з шоу можна лише травмованим і не з великого");
            int trustBefore = Trust, popBefore = Popularity;
            Feud.State.Trust -= 1;
            for (int z = 0; z < 3; z++) if (State.Injury[z] > 0) State.Injury[z] = Math.Max(0, State.Injury[z] - 1);
            State.Last = new ShowResult
            {
                Withdrew = true, OpponentId = State.Opponent, Position = State.Position, PopularityBefore = popBefore,
                PopularityAfter = Popularity, TrustBefore = trustBefore, TrustAfter = Trust
            };
            State.NextHypeBonus = 0; State.RivalBoost = 0;
            State.LastOutcome = 4; State.LastPlanOutcome = FeudPlanOutcome.Neither;
            State.Phase = ShowPhase.Result;
        }

        // ---------- матч: прийоми ----------

        /// <summary>Штраф до кидка прийому від травми його зони.</summary>
        public int InjuryPenalty(MoveDef m) { return State.InjuryPenaltyFor(m.Zone); }

        /// <summary>Прийоми гравця для поточної дії: шанси — з травмою зони і бонусом прийому; повтор того самого прийому позначено.</summary>
        public List<MoveOption> GetMoveOptions()
        {
            var e = Engine;
            if (e == null) throw new InvalidOperationException("Матчу немає");
            var l = new List<MoveOption>();
            foreach (var id in State.Moves)
            {
                var m = Content.Moves.Get(id);
                var hint = m.ToHint(InjuryPenalty(m));
                var opt = e.Describe(m.Action, hint.RollMod);
                // таблиця прийомів — гравця: поки ходить суперник, вона недоступна
                if (e.Phase == MatchPhase.AwaitAction && e.CurrentActor != MatchSide.Player) { opt.Available = false; opt.Reason = UnavailableReason.WrongPhase; }
                bool repeat = e.State.HasLastAction && e.State.LastAction == m.Action && e.State.LastMoveKey == m.Id
                              && e.CurrentActor == MatchSide.Player;
                l.Add(new MoveOption { Move = m, Option = opt, InjuryPenalty = InjuryPenalty(m), RepeatWarning = repeat });
            }
            return l;
        }

        /// <summary>Системні дії (утримання, встати, відчайдушний, напарник, промінь надії, відновитися, перевести подих).</summary>
        public List<ActionOption> GetUniversalOptions()
        {
            var e = Engine;
            if (e == null) throw new InvalidOperationException("Матчу немає");
            var l = new List<ActionOption>();
            foreach (var a in UniversalActions) l.Add(e.Describe(a));
            return l;
        }

        public IReadOnlyList<MatchEvent> PerformMove(string moveId)
        {
            var e = Engine;
            if (e == null) throw new InvalidOperationException("Матчу немає");
            if (!KnowsMove(moveId)) throw new InvalidOperationException("Прийом не вивчено: " + moveId);
            var m = Content.Moves.Get(moveId);
            return e.Perform(m.Action, m.ToHint(InjuryPenalty(m)));
        }

        public IReadOnlyList<MatchEvent> PerformAction(MatchActionId id)
        {
            var e = Engine;
            if (e == null) throw new InvalidOperationException("Матчу немає");
            if (id == MatchActionId.CatchBreath) return e.EndTurn();
            return e.Perform(id);
        }

        // ---------- матч: підсумок ----------

        /// <summary>
        /// Матч закінчився (звичайний чи ворожнечі): підсумок у шкали — популярність, довіра, очки розвитку (через стан ворожнечі),
        /// гонорар, травми від ударів суперника, ворожнеча з NPC, пояс (титульний матч). Звичайне шоу → Phase=Result; матч ворожнечі
        /// лишає Phase=Feud: далі екрани розвитку/кінцівки, потім <see cref="FinishBigShow"/>.
        /// </summary>
        public ShowResult FinishMatch()
        {
            Require(CareerStage.Show);
            if (State.Phase != ShowPhase.Match && State.Phase != ShowPhase.Feud) throw new InvalidOperationException("Матчу немає (фаза " + State.Phase + ")");
            var e = Engine;
            if (e == null || !e.IsOver) throw new InvalidOperationException("Матч ще триває");
            var res = e.Result;
            bool feudMatch = State.Phase == ShowPhase.Feud;
            int popBefore = Popularity, trustBefore = Trust;
            bool wasInjured = State.IsInjured;

            FeudPlanOutcome outcome;
            if (feudMatch)
            {
                Feud.FinishMatch();
                outcome = Feud.PlanOutcome;
            }
            else
            {
                Feud.State.Plan = State.IsTitle ? BookerPlan.None : State.Plan;
                outcome = Feud.State.ApplyMatchResult(res);
                Feud.State.Plan = BookerPlan.None;
            }

            var r = new ShowResult
            {
                Played = true, IsBig = State.IsBig, IsTitle = State.IsTitle, IsFeud = feudMatch, OpponentId = State.Opponent,
                Position = State.Position, Winner = res.Winner, EndKind = res.Kind, Hype = res.FinalHype, Rounds = res.Rounds,
                PlanOutcome = outcome, PopularityBefore = popBefore, TrustBefore = trustBefore
            };
            r.Fee = ContractFee() * (State.AwayShow ? CareerRules.AwayFeePercent : 100) / 100;
            if (res.Winner == MatchWinner.Player) r.FeeWinBonus = CareerRules.WinBonus;
            if (res.FinalHype >= CareerRules.HypeBonusAt) r.FeeHypeBonus = CareerRules.HypeBonus;
            if (State.Manager)
            {
                // менеджер бере 15 % усього гонорару (база + бонуси; з бонусу 5 окремо вийшло б 0) — знято з бази, тож рядок
                // «гонорар (база + бонуси)» сходиться. Приз за титул — не гонорар, його менеджер не ділить
                r.ManagerCut = CareerRules.ManagerCut(r.TotalFee);
                r.Fee -= r.ManagerCut;
            }
            State.Money += r.TotalFee;

            // P1-6: поразка чи нічия дає популярність лише з ажіотажем ≥ 9 і раз за сезон. Приріст від поразки в стані ворожнечі —
            // лише правило ажіотажу (≥ 7) і «звернув і зал за тебе»: якщо їх не можна — популярність лишається, як до матчу.
            if (res.Winner != MatchWinner.Player && Popularity > popBefore)
            {
                if (res.FinalHype < CareerRules.LossPopularityHype) { Feud.State.Audience = popBefore; r.LossPopularityBlocked = 1; }
                else if (State.LossPopularitySeason == State.Season) { Feud.State.Audience = popBefore; r.LossPopularityBlocked = 2; }
                else State.LossPopularitySeason = State.Season;
            }

            // «Злам плану» (Бунтар): звернутий план букера — зал це любить і букер запам'ятовує.
            if (outcome == FeudPlanOutcome.Swerved && Feud.State.Perks.Contains(PerkCatalog.PlanBreaker))
            {
                Feud.State.Audience = Math.Min(CareerRules.MaxPopularity, Feud.State.Audience + 1);
                Feud.State.DevPoints += 1;
            }
            ApplyBodyDamage(e, r, wasInjured, res);
            ApplyOpponentFeud(res, feudMatch);
            if (State.IsTitle) ApplyTitle(res, r);
            if (State.Farewell) State.FarewellResult = outcome == FeudPlanOutcome.Fulfilled ? 1 : 2;

            r.PopularityAfter = Popularity; r.TrustAfter = Trust;
            r.DevPointsGained = DevPoints - State.DevPointsAtMatchStart;
            State.LastOpponent = State.Opponent;
            State.LastOutcome = res.Winner == MatchWinner.Player ? 1 : res.Winner == MatchWinner.Rival ? 2 : 3;
            State.SeasonRecord[State.LastOutcome - 1]++;
            // Реванш одноразовий: перевага «знаю його слабкість» — на один матч; програв і реванш — нової образи немає.
            bool rematch = State.Grudges.Remove(State.Opponent);
            if (res.Winner == MatchWinner.Rival && !rematch && !State.IsTitle) State.Grudges.Add(State.Opponent);
            State.LastPlanOutcome = outcome;
            State.NextHypeBonus = 0; State.RivalBoost = 0;
            State.Last = r;
            if (!feudMatch) { _engine = null; State.Phase = ShowPhase.Result; }
            return r;
        }

        private static BodyZone ZoneOfRivalAction(MatchActionId a)
        {
            switch (a)
            {
                case MatchActionId.Strike:
                case MatchActionId.Dirty:
                    return BodyZone.Head;
                case MatchActionId.Submission:
                case MatchActionId.Desperate:
                    return BodyZone.Legs;
                default:
                    return BodyZone.Body;
            }
        }

        /// <summary>
        /// Ушкодження зон тіла з удару суперника (§6.3): очки ударів по зоні / <see cref="CareerRules.ZonePointsDivisor"/> додаються до
        /// ушкодження зони; поріг 3 — травма. Виступ травмованим і ушкодження ≥ 6 затягують наявні травми на тиждень.
        /// </summary>
        private void ApplyBodyDamage(MatchEngine e, ShowResult r, bool wasInjured, MatchResult res)
        {
            var pts = new int[3];
            foreach (var ev in e.State.Log)
            {
                if (ev.Actor != MatchSide.Rival) continue;
                foreach (var fx in ev.Effects)
                    if (fx.Kind == MatchEffectKind.Damage && fx.Who == MatchSide.Player && fx.Amount > 0)
                        pts[(int)ZoneOfRivalAction(ev.Action)] += fx.Amount;
            }
            if (wasInjured && e.State.Player.Damage >= CareerRules.InjuryWorsenDamage)
                for (int z = 0; z < 3; z++)
                    if (State.Injury[z] > 0) State.Injury[z] = Math.Min(CareerRules.MaxInjuryWeeks, State.Injury[z] + 1);
            for (int z = 0; z < 3; z++)
            {
                int d = pts[z] / CareerRules.ZonePointsDivisor;
                if (d > 0 && AddZoneDamage(z, d) && r.InjuredZone < 0) { r.InjuredZone = z; r.InjuryWeeks = State.Injury[z]; }
            }
        }

        /// <summary>Ворожнеча з NPC: ажіотаж фіналу ≥ 6 піднімає рівень; головна подія (рівень 4) розв'язується і повертає до 2.</summary>
        private void ApplyOpponentFeud(MatchResult res, bool feudMatch)
        {
            if (feudMatch) return;   // Молот — через FeudState
            string id = State.Opponent;
            int level = State.GetNpcFeud(id);
            if (State.IsBig && level >= CareerRules.MaxFeud) State.SetNpcFeud(id, CareerRules.FeudLevelAfterBlowoff);
            else if (res.FinalHype >= CareerRules.FeudRiseHype) State.SetNpcFeud(id, level + 1);
        }

        private void ApplyTitle(MatchResult res, ShowResult r)
        {
            if (State.BeltHolder == CareerState.PlayerHolder)
            {
                if (res.Winner == MatchWinner.Rival) State.BeltHolder = State.Opponent;
                return;
            }
            if (res.Winner != MatchWinner.Player) return;
            State.BeltHolder = CareerState.PlayerHolder;
            State.TitlesWon++;
            r.TitleWon = true;
            Feud.State.Audience += 1;
            State.Money += CareerRules.TitleWinBonus;
        }

        /// <summary>
        /// Велике шоу ворожнечі закінчено (матч зіграно, розвиток чи кінцівку показано): рух ворожнечі далі й перехід до підсумку шоу.
        /// Після розв'язки ворожнеча з Молотом зникає з великих шоу — далі їх суперники з ростера.
        /// </summary>
        public void FinishBigShow()
        {
            RequireShow(ShowPhase.Feud);
            if (State.Last == null || !State.Last.IsFeud) throw new InvalidOperationException("Матч ворожнечі ще не зіграно");
            var st = Feud.Stage;
            if (st != RingStage.Development && st != RingStage.Ending) throw new InvalidOperationException("Ворожнеча на етапі " + st);
            if (Feud.HasEnding) { State.FeudResolved = true; State.SetNpcFeud(CareerContent.HammerId, Feud.State.Heat); }
            else Feud.ContinueAfterDevelopment();
            State.Phase = ShowPhase.Result;
        }

        public ShowResult GetShowResult() { return State.Last; }

        // ---------- кінець тижня і сезон ----------

        /// <summary>Закрити підсумок шоу: проживання, загоєння, наступний тиждень або кінець сезону (чи титульного матчу).</summary>
        public void ContinueFromShow()
        {
            RequireShow(ShowPhase.Result);
            bool wasTitle = State.IsTitle;
            State.Phase = ShowPhase.None;
            if (State.Farewell)
            {
                // прощальний матч чемпіона: виконав «віддай пояс» — пояс у наступника, «пішов красиво»; ні — пояс вакантний, двері зачинено
                State.Farewell = false;
                State.IsBig = false;
                bool ok = State.FarewellResult == 1;
                State.BeltHolder = ok ? State.Opponent : "";
                if (!ok) State.ClosedTiers.Add(State.Federation);
                Transfer(ok ? CareerRules.FarewellTrust : CareerRules.TransferTrust);
                Stage = CareerStage.SeasonEnd;
                return;
            }
            if (wasTitle)
            {
                State.Report.TitleDone = true;
                State.Report.TitleWon = State.Last != null && State.Last.TitleWon;
                State.Report.BeltIsPlayers = IsChampion;
                RefreshSeasonReport();
                State.IsTitle = false;
                State.IsBig = false;
                Stage = CareerStage.SeasonEnd;
                return;
            }
            EndOfWeekBookkeeping();
            if (State.Week >= CareerRules.WeeksInSeason(State.Season)) EnterSeasonEnd();
            else
            {
                State.Week++;
                StartWeek();
                Stage = CareerStage.Week;
                OpenAutoScene();
            }
        }

        private void EndOfWeekBookkeeping()
        {
            if (State.Money >= WeeklyCost) { State.Money -= WeeklyCost; State.Strained = false; }
            else { State.Money = 0; State.Strained = true; }
            // Тренер ростера: плата щотижня; не вистачило — йде.
            if (State.Coach >= 0)
            {
                if (State.Money >= CareerRules.CoachWeeklyFee) State.Money -= CareerRules.CoachWeeklyFee;
                else State.Coach = -1;
            }
            if (State.AwayShow) { State.AwayShow = false; State.AwayTired = true; }
            for (int z = 0; z < 3; z++)
            {
                State.ZoneDamage[z] = Math.Max(0, State.ZoneDamage[z] - CareerRules.WeeklyZoneHeal);
                if (State.Injury[z] > 0) State.Injury[z]--;
            }
        }

        private void EnterSeasonEnd()
        {
            Func<string, int> lv = FeudLevel;
            var rep = new SeasonReport
            {
                Season = State.Season, Popularity = Popularity, Trust = Trust, Money = State.Money, Fired = CareerRules.IsFired(Popularity, Federation),
                IsDefense = IsChampion, BeltIsPlayers = IsChampion
            };
            rep.TitleEligible = !rep.Fired && ShowBooker.TitleEligible(BookingPool(), State, Trust, HammerAvailable, lv);
            State.Report = rep;
            Stage = CareerStage.SeasonEnd;
        }

        public SeasonReport GetSeasonReport() { Require(CareerStage.SeasonEnd); return State.Report; }

        /// <summary>
        /// Підсумок сезону остаточний: титульний матч зіграно, від нього відмовились або його не було (3-P2-6, плейтест 08.11.2026:
        /// титульний матч дописувався в уже показаний підсумок, і кінець сезону показувався двічі). Поки титульний шанс відкритий,
        /// екран кінця сезону показує лише його, підсумок — після.
        /// </summary>
        public bool SeasonSummaryFinal { get { return Stage == CareerStage.SeasonEnd && !CanStartTitleMatch; } }

        /// <summary>
        /// Шкали підсумку — після титульного матчу чи відмови: популярність, довіра, гроші й звільнення такими, якими сезон
        /// закрився (знімок кінця тижня 4 їх не знав: гонорар титулу, ажіотаж, довіра за відмову чемпіона).
        /// </summary>
        private void RefreshSeasonReport()
        {
            var rep = State.Report;
            if (rep == null) return;
            rep.Popularity = Popularity; rep.Trust = Trust; rep.Money = State.Money;
            rep.Fired = CareerRules.IsFired(Popularity, Federation);
            rep.BeltIsPlayers = IsChampion;
        }

        public bool CanStartTitleMatch { get { return Stage == CareerStage.SeasonEnd && State.Report != null && State.Report.TitleEligible && !State.Report.TitleDone; } }

        /// <summary>Титульний матч сезону (§6.4): шанс — або захист пояса. Іде як велике шоу зі своєю карткою.</summary>
        public void StartTitleMatch()
        {
            if (!CanStartTitleMatch) throw new InvalidOperationException("Титульного матчу зараз немає");
            Func<string, int> lv = FeudLevel;
            var opp = ShowBooker.ChooseTitle(BookingPool(), State, HammerAvailable, lv);
            State.Opponent = opp.Id;
            State.IsBig = true; State.IsTitle = true; State.IsFeudShow = false;
            State.Position = CardPosition.Main;
            State.Plan = BookerPlan.None;
            State.Stip = ShowBooker.StipulationsFor(lv(opp.Id));
            State.ShowPromoDone = false; State.ShowPromoTier = -1;
            Stage = CareerStage.Show;
            State.Phase = ShowPhase.Card;
            State.Last = null;
        }

        /// <summary>Відмовитися від титульного шансу (чемпіон, що ухиляється від захисту, втрачає довіру букера).</summary>
        public void SkipTitle()
        {
            Require(CareerStage.SeasonEnd);
            if (State.Report.TitleDone) return;
            if (State.Report.TitleEligible && IsChampion) Feud.State.Trust -= 1;
            State.Report.TitleDone = true;
            RefreshSeasonReport();
        }

        public bool CanStartNextSeason
        {
            get
            {
                return Stage == CareerStage.SeasonEnd && State.Report != null && !State.Farewell
                    && (State.Report.Fired || !State.Report.TitleEligible || State.Report.TitleDone);
            }
        }

        // ---------- перехід між федераціями: А2 «два ключі» (власник 11.11.2026) ----------

        /// <summary>
        /// Ключі пропозиції вищої федерації на кінці сезону (для екрана: «Глядачі N/потрібно», «Букер M/потрібно», «+ пояс»). null — не
        /// кінець сезону. Перевіряються після титульного матчу чи відмови від нього (<see cref="SeasonSummaryFinal"/>).
        /// </summary>
        public OfferKeys GetOfferKeys()
        {
            if (Stage != CareerStage.SeasonEnd || State.Report == null) return null;
            return CareerRules.OfferReady(Popularity, Trust, IsChampion, Federation, Content.Federations.Count);
        }

        /// <summary>Пропозицію можна прийняти чи відхилити зараз: підсумок остаточний, не звільнений, рішення ще немає, обидва ключі.</summary>
        public bool OfferAvailable
        {
            get
            {
                if (!SeasonSummaryFinal || State.Report.Fired || State.OfferDecision != 0 || State.Farewell) return false;
                var k = GetOfferKeys();
                return k != null && k.Ready && !ExitLocked;
            }
        }

        /// <summary>Федерація, куди кличуть (null — пропозиції немає).</summary>
        public FederationDef OfferFederation { get { return OfferAvailable ? Content.Federation(State.Federation + 1) : null; } }

        /// <summary>
        /// Прийняти пропозицію. Не-чемпіон переходить одразу (довіра в новій 1). Чемпіон спершу грає прощальний матч у старій
        /// федерації — план букера «віддай пояс» наступнику (<see cref="ContinueFromShow"/> завершує перехід). Пояс не переїжджає.
        /// Умови підписано тут: аванс (з надбавкою менеджерки, якщо вона працює зараз) і пастка. Звільнити менеджерку до
        /// прощального матчу можна, але підписаний аванс від цього не меншає — торгувалася вона до підпису.
        /// </summary>
        public void AcceptOffer()
        {
            if (!OfferAvailable) throw new InvalidOperationException("Пропозиції зараз немає");
            var to = OfferFederation;
            // вийти з чинної пастки раніше строку — штраф (§6.4); умови нового паперу фіксуються зараз, діють із переходом
            int penalty = ExitPenaltyDue;
            State.Money -= penalty;
            State.PendingExclusive = OfferIsExclusive;
            State.PendingAdvance = CareerRules.Advance(to, State.PendingExclusive, State.Manager);
            State.OfferDecision = 1;
            State.OfferDeclines = 0;
            if (!IsChampion)
            {
                Transfer(CareerRules.TransferTrust);
                return;
            }
            Func<string, int> lv = FeudLevel;
            var heir = ShowBooker.ChooseTitle(BookingPool(), State, HammerAvailable, lv);
            State.Farewell = true;
            State.FarewellResult = 0;
            State.Opponent = heir.Id;
            State.IsBig = true; State.IsTitle = false; State.IsFeudShow = false;
            State.Position = CardPosition.Main;
            State.Plan = BookerPlan.LoseClean;
            State.Stip = MatchStipulations.None;
            State.ShowPromoDone = false; State.ShowPromoTier = -1;
            Stage = CareerStage.Show;
            State.Phase = ShowPhase.Card;
            State.Last = null;
        }

        /// <summary>Відмовитися: лишаєшся; пропозиція повториться наступного сезону, поки ключі виконані. Друга відмова поспіль — довіра −1.</summary>
        public void DeclineOffer()
        {
            if (!OfferAvailable) throw new InvalidOperationException("Пропозиції зараз немає");
            State.OfferDecision = 2;
            State.OfferDeclines++;
            if (State.OfferDeclines >= CareerRules.DeclinesToOffend) Feud.State.Trust -= 1;
            RefreshSeasonReport();
        }

        /// <summary>Перехід на рівень вище зі статусом входу (у М1 — один: стеля нової − 2) і довірою нового букера.</summary>
        private void Transfer(int trust)
        {
            var to = Content.Federation(State.Federation + 1);
            EnterFederation(to.Tier, CareerRules.EntryPopularity(to), trust);
            State.Money += State.PendingAdvance;
            if (State.PendingExclusive)
            {
                // строк — сезони нової федерації: наступний і ще один; вийти на кінці останнього — уже не рано
                State.ContractExclusive = true;
                State.ContractEndSeason = State.Season + CareerRules.ExclusiveSeasons;
                State.ContractPenalty = CareerRules.ExitPenalty(to);
            }
            State.PendingExclusive = false;
            State.PendingAdvance = 0;
            RefreshSeasonReport();
        }

        // ---------- контракт (§6.4): аванс, пастка «виключне право», менеджер ----------

        /// <summary>Чинна пастка «виключне право»: гонорар ×0,7, без виїзних шоу й підробітку — до кінця строку.</summary>
        public bool ExclusiveActive { get { return State.ContractExclusive && State.Season <= State.ContractEndSeason; } }

        /// <summary>Гонорар шоу за місцем у картці й рівнем федерації — і за контрактом (пастка ×0,7). Без виїзду й менеджера.</summary>
        private int ContractFee()
        {
            return ShowBooker.Fee(State.Position, State.IsBig, Federation.FeePercent) * (ExclusiveActive ? CareerRules.ExclusiveFeePercent : 100) / 100;
        }

        /// <summary>Штраф, якщо піти з федерації зараз: чинна пастка й строк ще не скінчився (0 — вільний).</summary>
        public int ExitPenaltyDue { get { return ExclusiveActive && State.Season < State.ContractEndSeason ? State.ContractPenalty : 0; } }

        /// <summary>Пастка тримає: піти раніше строку можна, лише заплативши штраф, — а грошей не вистачає.</summary>
        public bool ExitLocked { get { return ExitPenaltyDue > State.Money; } }

        /// <summary>Огляд (QA, <c>-ring-shot-contract</c>): пропозиція — пастка (true) чи чесна (false); null — як вирішить зерно. Не в сейві.</summary>
        public bool? ForcedOfferExclusive { get; set; }

        /// <summary>
        /// Пропозиція цього кінця сезону — пастка «виключне право»? Детерміновано від зерна, сезону й рівня (повторна пропозиція
        /// наступного сезону — новий папір). Екран цього не бачить: лише <see cref="GetOfferContract"/>.
        /// </summary>
        internal bool OfferIsExclusive
        {
            get
            {
                if (ForcedOfferExclusive.HasValue) return ForcedOfferExclusive.Value;
                var k = GetOfferKeys();
                int tier = k == null ? State.Federation + 1 : k.ToTier;
                return ShowBooker.Mix(State.Seed, State.Season * 10 + tier, 29) % 100 < CareerRules.ExclusiveOfferPercent;
            }
        }

        /// <summary>Контракт пропозиції для екрана (null — ключів немає чи рішення вже ухвалено). Рахується й тоді, коли пастка тримає.</summary>
        public ContractView GetOfferContract()
        {
            var k = GetOfferKeys();
            if (k == null || !k.Ready || !SeasonSummaryFinal || State.Report.Fired || State.OfferDecision != 0 || State.Farewell) return null;
            var to = Content.Federation(k.ToTier);
            bool ex = OfferIsExclusive;
            var sight = State.Manager ? ContractSight.Seen : (ContractSight)Math.Max(0, Math.Min(3, State.OfferRead));
            var v = new ContractView
            {
                ToTier = to.Tier, Advance = CareerRules.Advance(to, ex, State.Manager), HasManager = State.Manager, Sight = sight,
                ExclusiveSeen = sight == ContractSight.Seen && ex, HintSomething = sight == ContractSight.Hint && ex,
                CanRead = !State.Manager && State.OfferRead == 0, ReadModifier = Player.Get(CareerRules.ContractReadAttr),
                ExitPenalty = ExitPenaltyDue, ExitLocked = ExitLocked
            };
            if (v.ExclusiveSeen)
            {
                v.ExclusiveFeePercent = CareerRules.ExclusiveFeePercent;
                v.ExclusiveSeasons = CareerRules.ExclusiveSeasons;
                v.ExclusivePenalty = CareerRules.ExitPenalty(to);
            }
            return v;
        }

        /// <summary>
        /// «Прочитати дрібний шрифт» (§6.4: «Пункт видно кидком Техніки або з менеджером»): 2d6 + Техніка, раз на пропозицію, дій
        /// тижня не бере. 10+ — пункт видно; 7–9 — видно, чи щось є; 6− — нічого не знайдено. З менеджером читати не треба.
        /// </summary>
        public ContractReadResult ReadOfferContract()
        {
            var r = new ContractReadResult();
            var c = GetOfferContract();
            if (c == null) { r.Reason = "no_offer"; return r; }
            if (State.Manager) { r.Reason = "manager"; return r; }
            if (State.OfferRead != 0) { r.Reason = "already"; return r; }
            r.Roll = TwoD6.Roll(Dice, "career:contract");
            r.Modifier = Player.Get(CareerRules.ContractReadAttr);
            r.Total = r.Roll + r.Modifier;
            r.Tier = TwoD6.TierOf(r.Total);
            r.Sight = r.Tier == RollTier.Strong ? ContractSight.Seen : r.Tier == RollTier.Partial ? ContractSight.Hint : ContractSight.Missed;
            State.OfferRead = (int)r.Sight;
            r.Ok = true;
            return r;
        }

        /// <summary>Менеджер найнятий (§6.4): 15 % кожного гонорару; пастку бачить без кидка; аванс переходу +25 %.</summary>
        public bool HasManager { get { return State.Manager; } }

        /// <summary>Прапор кар'єри «менеджерку звільнено»: сцена ev_manager читає його й вітається інакше (знову найняти можна).</summary>
        public const string ManagerFiredFlag = "manager_fired";

        /// <summary>
        /// Звільнити менеджера — будь-коли, без штрафу (наймає сцена закулісся ev_manager). Наступного сезону Марта приходить знову
        /// і пам'ятає звільнення (гілка сцени за прапором <see cref="ManagerFiredFlag"/>); відмова в сцені закриває її назавжди.
        /// </summary>
        public bool FireManager()
        {
            if (!State.Manager || Stage == CareerStage.Title || Stage == CareerStage.Create) return false;
            State.Manager = false;
            Feud.State.Flags.Add(ManagerFiredFlag);
            return true;
        }

        /// <summary>
        /// Куди підеш, якщо цього сезону контракт не продовжать (популярність нижче підлоги): найвища відкрита федерація нижче поточної.
        /// На місцевій — вона сама (там звільнення — повернення на мінімум).
        /// </summary>
        public FederationDef DemotionFederation { get { return Content.Federation(State.Federation > 1 ? DemotionTier() : 1); } }

        /// <summary>Федерація, з якої прийшов цього кінця сезону (після переходу; інакше null) — для рядка «пояс лишився в …».</summary>
        public FederationDef MovedFrom { get { return State.OfferDecision == 1 && State.Federation > 1 ? Content.Federation(State.Federation - 1) : null; } }

        /// <summary>Найвища відкрита федерація нижче поточної (куди не закрито повернення); місцева приймає завжди.</summary>
        private int DemotionTier()
        {
            for (int t = State.Federation - 1; t > 1; t--) if (!State.ClosedTiers.Contains(t)) return t;
            return 1;
        }

        /// <summary>
        /// Новий сезон. Звільнення (популярність 0): повернення на місцевий рівень — популярність і довіра на мінімумі, пояс втрачено,
        /// ворожнечі з NPC скинуто, гроші не нижче мінімуму; кар'єра триває. Між сезонами тіло відпочиває.
        /// </summary>
        public void StartNextSeason()
        {
            if (!CanStartNextSeason) throw new InvalidOperationException("Сезон ще не закрито");
            // Новий сезон — хедлайнери підтягуються: знімок росту наздоганяє поточний на CatchUp/Den (на мейн-івенті вищого
            // щабля немає, і без цього знімок застигав: третій сезон 81–87 %).
            if (State.TierPos >= 0)
                State.TierGrowth += (PlayerGrowth() - State.TierGrowth) * CareerRules.SeasonCatchUpNum / CareerRules.SeasonCatchUpDen;
            // відмови поспіль (рішення ПМ, 11.11.2026): серію обриває будь-який сезон без явної відмови — мовчазний пропуск, сезон без
            // пропозиції, перехід; «відмова → пропуск → відмова» не штрафує
            if (State.OfferDecision != 2) State.OfferDeclines = 0;
            State.OfferDecision = 0;
            State.FarewellResult = 0;
            State.OfferRead = 0;
            if (State.Report.Fired && State.Federation > 1)
            {
                // нижче підлоги — контракт не продовжують: рівень нижче (власник 11.11.2026), кар'єра триває
                EnterFederation(DemotionTier(), Popularity, CareerRules.FiredTrust);
                State.Money = Math.Max(State.Money, WeeklyCost * 2);
                State.FiredCount++;
            }
            else if (State.Report.Fired)
            {
                Feud.State.Audience = CareerRules.FiredPopularity;
                Feud.State.Trust = CareerRules.FiredTrust;
                State.BeltHolder = Federation.BeltOf(State.Female) ?? "";
                var keys = new List<string>(State.NpcFeud.Keys);
                foreach (var k in keys) State.NpcFeud[k] = 0;
                State.Money = Math.Max(State.Money, WeeklyCost * 2);
                State.FiredCount++;
            }
            for (int z = 0; z < 3; z++)
            {
                State.ZoneDamage[z] = 0;
                State.Injury[z] = Math.Max(0, State.Injury[z] - 2);
            }
            State.Report = null;
            Array.Clear(State.SeasonRecord, 0, State.SeasonRecord.Length);
            State.SeasonPopStart = Popularity;
            State.Season++; State.Week = 1;
            if (State.ContractExclusive && State.Season > State.ContractEndSeason)
            {
                // строк пастки скінчився — контракт продовжується як чесний
                State.ContractExclusive = false; State.ContractEndSeason = 0; State.ContractPenalty = 0;
            }
            State.Strained = false;
            StartWeek();
            Stage = CareerStage.Week;
            OpenAutoScene();
        }

        // ---------- сейв ----------

        /// <summary>
        /// Зліпок кар'єри (ring3): етап, стан кар'єри, зліпок кампанії ворожнечі (ring2), стан кубика. Матч посеред шоу не
        /// зберігається — відновиться як «картка вечора» (як і матч ворожнечі в кампанії); кубик ішов далі, тож інший кидок.
        /// </summary>
        public string CaptureState()
        {
            var phase = State.Phase; var sp = State.ShowPromoDone; var spt = State.ShowPromoTier;
            if (phase == ShowPhase.Match) { State.Phase = ShowPhase.Card; State.ShowPromoDone = false; State.ShowPromoTier = -1; }
            try
            {
                var sb = new StringBuilder(BlobVersion);
                sb.Append("|stage=").Append((int)Stage);
                State.Capture(sb);
                sb.Append("|feud=").Append(Uri.EscapeDataString(Feud.CaptureState()));
                sb.Append("|dice=").Append(Uri.EscapeDataString(Dice.CaptureState()));
                return sb.ToString();
            }
            finally { State.Phase = phase; State.ShowPromoDone = sp; State.ShowPromoTier = spt; }
        }

        /// <summary>Відновити кар'єру зі зліпка (у свіжий об'єкт, створений через New з тим самим вмістом).</summary>
        public void RestoreState(string blob)
        {
            if (string.IsNullOrEmpty(blob)) throw new ArgumentException("порожній зліпок");
            var parts = blob.Split('|');
            if (parts[0] != BlobVersion) throw new FormatException("невідома версія зліпка кар'єри: " + parts[0]);
            var kv = new Dictionary<string, string>();
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq < 0) throw new FormatException("пошкоджений зліпок кар'єри");
                kv[parts[i].Substring(0, eq)] = parts[i].Substring(eq + 1);
            }
            var st = new CareerState();
            st.Restore(kv);
            var stage = (CareerStage)int.Parse(kv["stage"], CultureInfo.InvariantCulture);
            Feud.RestoreState(Uri.UnescapeDataString(kv["feud"]));
            Dice.RestoreState(Uri.UnescapeDataString(kv["dice"]));
            State = st;
            // пояс зі зліпка до 11.11.2026 — у дивізіон його власника; бракує поясів — стартові чемпіони
            if (st.OldBelt != null)
            {
                var holder = Content.FindNpc(st.OldBelt);
                if (holder != null) st.SetBelt(holder.Tier, holder.Female, holder.Id);
                else if (st.OldBelt == CareerState.PlayerHolder) st.SetBelt(st.Federation, st.Female, CareerState.PlayerHolder);
                st.OldBelt = null;
            }
            // герой уже є (не титул і не створення) — бракує поясів (старий сейв без «belts»; пояс порожній чи невідомий) — стартові
            // чемпіони; до створення героя поясів ще немає
            if (stage != CareerStage.Title && stage != CareerStage.Create) InitBelts();
            SyncFederationRules();
            Stage = stage;
            _engine = null;
            _bs = null;   // незавершена сцена не зберігається: дія тижня не витрачена, сцена відкриється з початку
            if (Stage == CareerStage.Week) OpenAutoScene();   // подія, що відкривається сама, — знову з початку
        }
    }
}
