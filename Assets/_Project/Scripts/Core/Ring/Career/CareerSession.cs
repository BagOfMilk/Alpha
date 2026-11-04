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
            MatchActionId.Pin, MatchActionId.Desperate, MatchActionId.HopeSpot, MatchActionId.GetUp, MatchActionId.LeaveRing,
            MatchActionId.ReturnToRing, MatchActionId.Recover, MatchActionId.Ally, MatchActionId.CatchBreath
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
        /// <summary>Роль зараз — домінантний шлях (поворот герой ↔ лиходій — подія кар'єри).</summary>
        public RingPath CurrentRole { get { return Feud.State.DominantPath(); } }

        /// <summary>Матч, що йде зараз: звичайний (окремий) або матч ворожнечі на великому шоу. null — матчу немає.</summary>
        public MatchEngine Engine { get { return State.Phase == ShowPhase.Feud ? Feud.CurrentMatch : _engine; } }

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
            State.BeltHolder = Content.BeltHolderId;
            State.Season = 1; State.Week = 1;
            StartWeek();
            Stage = CareerStage.Week;
        }

        // ---------- тиждень ----------

        private void StartWeek()
        {
            State.ActionsLeft = State.Strained ? CareerRules.StrainedActions : CareerRules.ActionsPerWeek;
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
                opp = State.IsBig
                    ? ShowBooker.ChooseBig(Content, State, Popularity, HammerAvailable, lv)
                    : ShowBooker.ChooseWeekly(Content, State, Popularity, HammerAvailable, lv);
                State.Position = State.IsBig ? CardPosition.Main : CareerRules.PositionFor(Popularity);
                State.Plan = ShowBooker.PlanFor(opp, Popularity, Trust);
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
                CanRecover = HasSomethingToRecover()
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
                Stipulations = State.Stip, Fee = ShowBooker.Fee(State.Position, State.IsBig),
                WinBonus = CareerRules.WinBonus, HypeBonus = CareerRules.HypeBonus, PromoHypeBonus = State.NextHypeBonus,
                CanWithdraw = State.IsInjured && !State.IsBig
            };
            c.Injuries = InjuryViews();
            return c;
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
            if (r.Ok && withTrainer && State.Money < CareerRules.TrainerCost) { r.Ok = false; r.Reason = "money"; }
            if (!r.Ok) return r;
            int pts = CareerRules.TrainingPoints;
            if (withTrainer)
            {
                State.Money -= CareerRules.TrainerCost; r.MoneyDelta = -CareerRules.TrainerCost;
                pts += CareerRules.TrainerExtraPoints;
            }
            Feud.State.DevPoints += pts; r.DevPointsDelta = pts;
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
        /// Закулісся (§6.1): Харизма 2d6. 10+ — довіра букера +1 і чутка; 7–9 — лише чутка; решта — нічого. Чутка — підказка про
        /// найближче (ключ рядка в ui_uk.json: rumor.*).
        /// </summary>
        public WeekActionResult Backstage()
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

        private string RumorKey()
        {
            Func<string, int> lv = FeudLevel;
            if (State.IsFeudShow) return "rumor.hammer_big";
            if (ShowBooker.TitleEligible(Content, State, Trust, HammerAvailable, lv)) return IsChampion ? "rumor.defense" : "rumor.title_shot";
            if (Trust < 2) return "rumor.booker_cold";
            return State.IsBig ? "rumor.big_show" : "rumor.next_opponent";
        }

        /// <summary>Підробіток (§6.1): гроші, ризик травми, жодного досвіду. Сума 2d6 ≤ 4 — випадкова зона дістає ушкодження 2.</summary>
        public WeekActionResult SideJob()
        {
            var r = Begin(WeekActionKind.SideJob);
            if (!r.Ok) return r;
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
            return true;
        }

        /// <summary>Закінчити планування: решта дій тижня пропадає, починається вечір (шоу).</summary>
        public void EndWeek()
        {
            Require(CareerStage.Week);
            if (State.PromoChoicePending) throw new InvalidOperationException("Спершу оберіть нагороду промо");
            Stage = CareerStage.Show;
            State.Phase = ShowPhase.Card;
            State.Last = null;
        }

        // ---------- розвиток ----------

        private bool CanDevelop { get { return Stage == CareerStage.Week || Stage == CareerStage.SeasonEnd; } }

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
                Week = State.Week, TitlesWon = State.TitlesWon, FiredCount = State.FiredCount, FeudResolved = State.FeudResolved
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
            int hype = 1 + FeudLevel(npc.Id) + mods.StartHypeBonus + State.NextHypeBonus
                       + (State.IsInjured ? CareerRules.InjuredShowHype : 0);
            var setup = new MatchSetup
            {
                Player = Player.Clone(), Rival = npc.Stats.Clone(), PlayerMods = mods,
                PlayerStartMomentum = Math.Max(0, Math.Min(10, mods.StartMomentumBonus)),
                RivalStartMomentum = Math.Max(0, Math.Min(10, npc.StartMomentum + State.RivalBoost)),
                StartHype = Math.Max(0, Math.Min(10, hype)), PlayerHasAlly = false, Stipulations = State.Stip,
                Plan = State.IsTitle ? BookerPlan.None : State.Plan, PlayerRole = Feud.State.DominantPath(), RivalRole = npc.Path,
                HopeSpotEnabled = true
            };
            _engine = new MatchEngine(setup, Dice);
            State.Phase = ShowPhase.Match;
            return _engine;
        }

        /// <summary>Велике шоу ворожнечі: матч Молота з доповненнями кар'єри (промінь надії; ажіотаж і імпульс від промо тижня).</summary>
        public MatchEngine StartFeudMatch()
        {
            RequireShow(ShowPhase.Feud);
            var e = Feud.StartMatch();
            e.Setup.HopeSpotEnabled = true;
            e.State.Hype = Math.Max(0, Math.Min(MatchRules.MaxHype, e.State.Hype + State.NextHypeBonus + (State.IsInjured ? CareerRules.InjuredShowHype : 0)));
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

        /// <summary>Системні дії (утримання, встати, вийти з рингу, відчайдушний, напарник, промінь надії, перевести подих).</summary>
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
            r.Fee = ShowBooker.Fee(State.Position, State.IsBig);
            if (res.Winner == MatchWinner.Player) r.FeeWinBonus = CareerRules.WinBonus;
            if (res.FinalHype >= CareerRules.HypeBonusAt) r.FeeHypeBonus = CareerRules.HypeBonus;
            State.Money += r.TotalFee;

            ApplyBodyDamage(e, r, wasInjured, res);
            ApplyOpponentFeud(res, feudMatch);
            if (State.IsTitle) ApplyTitle(res, r);

            r.PopularityAfter = Popularity; r.TrustAfter = Trust;
            r.DevPointsGained = DevPoints - State.DevPointsAtMatchStart;
            State.LastOpponent = State.Opponent;
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
            if (wasTitle)
            {
                State.Report.TitleDone = true;
                State.Report.TitleWon = State.Last != null && State.Last.TitleWon;
                State.Report.BeltIsPlayers = IsChampion;
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
            }
        }

        private void EndOfWeekBookkeeping()
        {
            if (State.Money >= CareerRules.LivingCost) { State.Money -= CareerRules.LivingCost; State.Strained = false; }
            else { State.Money = 0; State.Strained = true; }
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
                Season = State.Season, Popularity = Popularity, Trust = Trust, Money = State.Money, Fired = Popularity <= 0,
                IsDefense = IsChampion, BeltIsPlayers = IsChampion
            };
            rep.TitleEligible = !rep.Fired && ShowBooker.TitleEligible(Content, State, Trust, HammerAvailable, lv);
            State.Report = rep;
            Stage = CareerStage.SeasonEnd;
        }

        public SeasonReport GetSeasonReport() { Require(CareerStage.SeasonEnd); return State.Report; }

        public bool CanStartTitleMatch { get { return Stage == CareerStage.SeasonEnd && State.Report != null && State.Report.TitleEligible && !State.Report.TitleDone; } }

        /// <summary>Титульний матч сезону (§6.4): шанс — або захист пояса. Іде як велике шоу зі своєю карткою.</summary>
        public void StartTitleMatch()
        {
            if (!CanStartTitleMatch) throw new InvalidOperationException("Титульного матчу зараз немає");
            Func<string, int> lv = FeudLevel;
            var opp = ShowBooker.ChooseTitle(Content, State, HammerAvailable, lv);
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
        }

        public bool CanStartNextSeason
        {
            get { return Stage == CareerStage.SeasonEnd && State.Report != null && (State.Report.Fired || !State.Report.TitleEligible || State.Report.TitleDone); }
        }

        /// <summary>
        /// Новий сезон. Звільнення (популярність 0): повернення на місцевий рівень — популярність і довіра на мінімумі, пояс втрачено,
        /// ворожнечі з NPC скинуто, гроші не нижче мінімуму; кар'єра триває. Між сезонами тіло відпочиває.
        /// </summary>
        public void StartNextSeason()
        {
            if (!CanStartNextSeason) throw new InvalidOperationException("Сезон ще не закрито");
            if (State.Report.Fired)
            {
                Feud.State.Audience = CareerRules.FiredPopularity;
                Feud.State.Trust = CareerRules.FiredTrust;
                State.BeltHolder = Content.BeltHolderId;
                var keys = new List<string>(State.NpcFeud.Keys);
                foreach (var k in keys) State.NpcFeud[k] = 0;
                State.Money = Math.Max(State.Money, CareerRules.LivingCost * 2);
                State.FiredCount++;
            }
            for (int z = 0; z < 3; z++)
            {
                State.ZoneDamage[z] = 0;
                State.Injury[z] = Math.Max(0, State.Injury[z] - 2);
            }
            State.Report = null;
            State.Season++; State.Week = 1;
            State.Strained = false;
            StartWeek();
            Stage = CareerStage.Week;
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
            Stage = stage;
            _engine = null;
        }
    }
}
