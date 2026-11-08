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
            Array.Clear(State.SeasonRecord, 0, State.SeasonRecord.Length);
            State.SeasonPopStart = Popularity;
            StartWeek();
            Stage = CareerStage.Week;
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
                opp = State.IsBig
                    ? ShowBooker.ChooseBig(Content, State, Popularity, HammerAvailable, lv)
                    : ShowBooker.ChooseWeekly(Content, State, Popularity, HammerAvailable, lv);
                State.Position = State.IsBig ? CardPosition.Main : CareerRules.PositionFor(Popularity);
                if (!State.IsBig) UpdateTier();
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
                CanRecover = HasSomethingToRecover(), BackstageOpen = _bs != null
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
                CanWithdraw = State.IsInjured && !State.IsBig, RivalSteps = RivalStepsNow(), IsRematch = IsRematch
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
            if (State.IsFeudShow) return CareerRules.HammerBaseSteps + Feud.Records.Count * CareerRules.HammerStepsPerMatch + growth;
            if (State.IsTitle) return CareerRules.TitleSteps + growth;
            // Щотижневі — на знімку росту, знятому на вході в щабель картки: виріс між підвищеннями — виграєш частіше (пік);
            // підвищили — суперники знову твого рівня. Реванш — суперник слабший.
            int steps = CareerRules.RivalSteps(State.Position, State.TierPos < 0 ? growth : State.TierGrowth);
            if (IsRematch) steps -= CareerRules.RematchSteps;
            return Math.Max(0, steps);
        }

        /// <summary>Цього вечора — реванш із тим, хто переміг гравця останнього разу.</summary>
        public bool IsRematch { get { return !State.IsFeudShow && !State.IsTitle && State.Grudges.Contains(State.Opponent); } }

        /// <summary>Сходинка: на новому (вищому) щаблі картки — знімок поточного росту; на нижчому — щабель знижено, знімок той самий.</summary>
        private void UpdateTier()
        {
            int pos = (int)State.Position;
            if (pos > State.TierPos) State.TierGrowth = PlayerGrowth();
            State.TierPos = pos;
        }

        // ---------- сітки витрат (не займають дій тижня) ----------

        public bool CanBuyCostume { get { return Stage == CareerStage.Week && !State.CostumeBought && State.Money >= CareerRules.CostumeCost; } }

        public bool BuyCostume()
        {
            if (!CanBuyCostume) return false;
            State.Money -= CareerRules.CostumeCost; State.CostumeBought = true;
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
        public bool CanGoAway { get { return Stage == CareerStage.Week && !State.AwayShow && !State.IsBig && State.Money >= CareerRules.AwayTravelCost; } }

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

        private BackstageScene PickBackstageScene()
        {
            var all = Content.Backstage.Scenes;
            foreach (var sc in all)
            {
                if (sc.Kind != BackstageKind.Event) continue;
                if (BackstageSeenThisSeason(sc.Id) || State.Season < sc.MinSeason || State.Week < BackstageDueWeek(sc)) continue;
                if (SceneHolds(sc)) return sc;
            }
            foreach (var sc in all) if (sc.Kind == BackstageKind.Booker && SceneHolds(sc)) return sc;
            throw new InvalidOperationException("Немає закулісної сцени для відкриття");
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
                case "week": return State.Week >= c.Min;
                case "parity": return State.Week % 2 == c.Min;
                case "injured": return State.IsInjured;
                case "champion": return IsChampion;
                case "last_won": return State.LastOutcome == 1;
                case "last_lost": return State.LastOutcome == 2;
                case "last_draw": return State.LastOutcome == 3;
                case "last_swerved": return State.LastPlanOutcome == FeudPlanOutcome.Swerved;
                case "plan_set": return State.Plan != BookerPlan.None && !State.IsFeudShow && !State.IsTitle;
                case "title_eligible":
                    Func<string, int> lv = FeudLevel;
                    return ShowBooker.TitleEligible(Content, State, Trust, HammerAvailable, lv);
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
            Done(r);
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
            }
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
            int steps = RivalStepsNow();
            var e = Feud.StartMatch();
            e.Setup.HopeSpotEnabled = true;
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
            r.Fee = ShowBooker.Fee(State.Position, State.IsBig) * (State.AwayShow ? CareerRules.AwayFeePercent : 100) / 100;
            if (res.Winner == MatchWinner.Player) r.FeeWinBonus = CareerRules.WinBonus;
            if (res.FinalHype >= CareerRules.HypeBonusAt) r.FeeHypeBonus = CareerRules.HypeBonus;
            State.Money += r.TotalFee;

            // «Злам плану» (Бунтар): звернутий план букера — зал це любить і букер запам'ятовує.
            if (outcome == FeudPlanOutcome.Swerved && Feud.State.Perks.Contains(PerkCatalog.PlanBreaker))
            {
                Feud.State.Audience = Math.Min(CareerRules.MaxPopularity, Feud.State.Audience + 1);
                Feud.State.DevPoints += 1;
            }
            ApplyBodyDamage(e, r, wasInjured, res);
            ApplyOpponentFeud(res, feudMatch);
            if (State.IsTitle) ApplyTitle(res, r);

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
            // Новий сезон — хедлайнери підтягуються: знімок росту наздоганяє поточний на CatchUp/Den (на мейн-івенті вищого
            // щабля немає, і без цього знімок застигав: третій сезон 81–87 %).
            if (State.TierPos >= 0)
                State.TierGrowth += (PlayerGrowth() - State.TierGrowth) * CareerRules.SeasonCatchUpNum / CareerRules.SeasonCatchUpDen;
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
            Array.Clear(State.SeasonRecord, 0, State.SeasonRecord.Length);
            State.SeasonPopStart = Popularity;
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
            _bs = null;   // незавершена сцена не зберігається: дія тижня не витрачена, сцена відкриється з початку
        }
    }
}
