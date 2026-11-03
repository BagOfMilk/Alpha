using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Randomness;
using Game.Core.Ring.Match;

namespace Game.Core.Ring
{
    /// <summary>Етапи зрізу «Ворожнеча» (gdd §1): огляд → промо → підготовка → матч → кінцівка → розвиток.</summary>
    public enum RingStage { Overview = 0, Promo = 1, MatchPrep = 2, Match = 3, Ending = 4, Development = 5 }

    /// <summary>
    /// Фасад кампанії зрізу: одна ворожнеча = 4 промо + фінальний матч + кінцівка + розвиток + реванш (gdd §1).
    /// Екрани й тести ходять лише через нього; правил тут немає — він переставляє етапи й зберігає стан.
    /// Потік: Overview → StartPromo → (PromoRunner) → FinishPromo → Overview … → MatchPrep → StartMatch → (MatchEngine) →
    /// FinishMatch → Ending → OpenDevelopment → BuyPerk/BuyAttr → StartRematch → … → FinishMatch → Development.
    /// </summary>
    public sealed class RingCampaign
    {
        public FeudScript Script { get; }
        public IDiceRoller Dice { get; }
        /// <summary>Стан ворожнечі. До фінального матчу — поточний; після нього — з підсумками й куплене в розвитку.</summary>
        public FeudState State { get; private set; }
        public RingStage Stage { get; private set; }
        /// <summary>Індекс промо, яке йде або піде наступним (0…Promos.Count).</summary>
        public int PromoIndex { get; private set; }
        public PromoRunner CurrentPromoRunner { get; private set; }
        public MatchEngine CurrentMatch { get; private set; }
        /// <summary>Йде (або йшов останнім) реванш, а не фінальний матч ворожнечі.</summary>
        public bool IsRematch { get; private set; }

        /// <summary>Підсумок фінального матчу (null, доки не зіграно).</summary>
        public MatchResult Result { get; private set; }
        public FeudPlanOutcome PlanOutcome { get; private set; }
        /// <summary>
        /// Кінцівка ворожнечі — за ПЕРШИМ матчем. Реванш показує свій результат (<see cref="RematchResult"/>), але кінцівку
        /// не міняє: у зрізі одна ворожнеча, реванш лише показує, що розвиток працює (gdd §1).
        /// </summary>
        public FeudEnding Ending { get; private set; }
        public bool HasEnding { get { return Result != null; } }
        /// <summary>Підсумок останнього реваншу (null, доки не грали).</summary>
        public MatchResult RematchResult { get; private set; }

        /// <summary>Стан ворожнечі безпосередньо перед фінальним матчем (для реваншу). null — матч ще не починали.</summary>
        private string _preMatchState;

        private RingCampaign(FeudScript script, IDiceRoller dice)
        {
            Script = script ?? throw new ArgumentNullException(nameof(script));
            Dice = dice ?? throw new ArgumentNullException(nameof(dice));
            State = script.NewState();
            Stage = RingStage.Overview;
        }

        public static RingCampaign New(FeudScript script, IDiceRoller dice) { return new RingCampaign(script, dice); }

        public int PromoCount { get { return Script.Promos.Count; } }

        private void Require(RingStage s)
        {
            if (Stage != s) throw new InvalidOperationException("Етап " + Stage + ", а потрібен " + s);
        }

        // ---------- промо ----------

        public PromoRunner StartPromo()
        {
            Require(RingStage.Overview);
            if (PromoIndex >= PromoCount) throw new InvalidOperationException("Промо закінчились");
            CurrentPromoRunner = new PromoRunner(Script, Script.Promos[PromoIndex], State);
            Stage = RingStage.Promo;
            return CurrentPromoRunner;
        }

        /// <summary>Промо дійшло до кінця вузла end: наступне промо або підготовка до матчу.</summary>
        public void FinishPromo()
        {
            Require(RingStage.Promo);
            if (!CurrentPromoRunner.IsFinished) throw new InvalidOperationException("Промо ще не закінчилось");
            CurrentPromoRunner = null;
            PromoIndex++;
            Stage = PromoIndex < PromoCount ? RingStage.Overview : RingStage.MatchPrep;
        }

        // ---------- матч ----------

        /// <summary>Знімок стану ДО матчу (для реваншу), збирання MatchSetup зі стану, новий рушій.</summary>
        public MatchEngine StartMatch()
        {
            Require(RingStage.MatchPrep);
            _preMatchState = State.CaptureState();
            CurrentMatch = new MatchEngine(State.BuildMatchSetup(Script), Dice);
            IsRematch = false;
            Stage = RingStage.Match;
            return CurrentMatch;
        }

        /// <summary>
        /// Матч скінчився: застосувати підсумок до стану, кінцівка, скидання ажіотажу ворожнечі (gdd §3, §5.3).
        /// Реванш лише записує свій результат і повертає до розвитку.
        /// </summary>
        public void FinishMatch()
        {
            Require(RingStage.Match);
            if (CurrentMatch == null || !CurrentMatch.IsOver) throw new InvalidOperationException("Матч ще триває");
            var result = CurrentMatch.Result;
            CurrentMatch = null;
            if (IsRematch)
            {
                RematchResult = result;
                Stage = RingStage.Development;
                return;
            }
            Result = result;
            PlanOutcome = State.ApplyMatchResult(result);
            Ending = FeudEndings.Resolve(State, result, PlanOutcome);
            State.ResetHeatAfterFeud();
            Stage = RingStage.Ending;
        }

        // ---------- розвиток ----------

        public void OpenDevelopment()
        {
            if (Stage == RingStage.Ending) Stage = RingStage.Development;
            else Require(RingStage.Development);
        }

        public bool CanBuyPerk(string perkId, out string reason)
        {
            reason = "stage";
            if (Stage != RingStage.Ending && Stage != RingStage.Development) return false;
            return Development.CanBuyPerk(State, perkId, out reason);
        }

        public bool BuyPerk(string perkId)
        {
            string reason;
            if (!CanBuyPerk(perkId, out reason)) return false;
            Stage = RingStage.Development;
            return Development.BuyPerk(State, perkId);
        }

        public bool CanBuyAttr(RingAttr a, out string reason)
        {
            reason = "stage";
            if (Stage != RingStage.Ending && Stage != RingStage.Development) return false;
            return Development.CanBuyAttr(State, a, out reason);
        }

        public bool BuyAttr(RingAttr a)
        {
            string reason;
            if (!CanBuyAttr(a, out reason)) return false;
            Stage = RingStage.Development;
            return Development.BuyAttr(State, a);
        }

        public bool CanRematch { get { return (Stage == RingStage.Ending || Stage == RingStage.Development) && _preMatchState != null; } }

        /// <summary>
        /// Той самий матч ще раз: стан ДО матчу (шкали, шляхи, план, умови, напарник) + куплені в розвитку перки й
        /// атрибути. Нове кидання кубиків (кубик продовжує потік). Результат не міняє кінцівку ворожнечі.
        /// </summary>
        public MatchEngine StartRematch()
        {
            if (!CanRematch) throw new InvalidOperationException("Реванш зараз неможливий");
            var s = new FeudState();
            s.RestoreState(_preMatchState);
            foreach (var p in State.Perks) s.Perks.Add(p);
            s.Player = State.Player.Clone();
            CurrentMatch = new MatchEngine(s.BuildMatchSetup(Script), Dice);
            IsRematch = true;
            RematchResult = null;
            Stage = RingStage.Match;
            return CurrentMatch;
        }

        // ---------- сейв ----------

        private static string Esc(string s) { return Uri.EscapeDataString(s ?? ""); }
        private static string Unesc(string s) { return Uri.UnescapeDataString(s); }

        private static string ResultToString(MatchResult r)
        {
            if (r == null) return "";
            return string.Join(",", new[]
            {
                ((int)r.Winner).ToString(CultureInfo.InvariantCulture), ((int)r.Kind).ToString(CultureInfo.InvariantCulture),
                r.FinalHype.ToString(CultureInfo.InvariantCulture), r.Rounds.ToString(CultureInfo.InvariantCulture),
                r.PlayerDirtyMoves.ToString(CultureInfo.InvariantCulture), r.PlayerSells.ToString(CultureInfo.InvariantCulture),
                r.PlayerKickoutsAtTwo.ToString(CultureInfo.InvariantCulture), r.PlayerUsedAlly ? "1" : "0"
            });
        }

        private static MatchResult ResultFromString(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split(',');
            return new MatchResult
            {
                Winner = (MatchWinner)int.Parse(p[0], CultureInfo.InvariantCulture),
                Kind = (MatchEndKind)int.Parse(p[1], CultureInfo.InvariantCulture),
                FinalHype = int.Parse(p[2], CultureInfo.InvariantCulture),
                Rounds = int.Parse(p[3], CultureInfo.InvariantCulture),
                PlayerDirtyMoves = int.Parse(p[4], CultureInfo.InvariantCulture),
                PlayerSells = int.Parse(p[5], CultureInfo.InvariantCulture),
                PlayerKickoutsAtTwo = int.Parse(p[6], CultureInfo.InvariantCulture),
                PlayerUsedAlly = p[7] == "1"
            };
        }

        /// <summary>
        /// Зліпок кампанії рядком: етап, індекс промо, вузол, стан ворожнечі, стан кубика. Матч посеред гри не зберігається:
        /// фінальний матч віддається як «підготовка» (стан ворожнечі ще не зачеплений), реванш — як «розвиток».
        /// Збережене під час вибору нагород промо відновиться на вузлі кидка до кидка: кубик уже пішов далі, тож другий кидок
        /// дасть інше число (перезавантаженням кидок не перегравати).
        /// </summary>
        public string CaptureState()
        {
            var stage = Stage;
            if (stage == RingStage.Match) stage = IsRematch ? RingStage.Development : RingStage.MatchPrep;
            var sb = new StringBuilder("ring1");
            sb.Append("|stage=").Append((int)stage);
            sb.Append("|promo=").Append(PromoIndex);
            sb.Append("|node=").Append(Stage == RingStage.Promo && CurrentPromoRunner != null ? Esc(CurrentPromoRunner.Current.Id) : "");
            sb.Append("|state=").Append(Esc(State.CaptureState()));
            sb.Append("|pre=").Append(_preMatchState == null ? "" : Esc(_preMatchState));
            sb.Append("|res=").Append(ResultToString(Result));
            sb.Append("|plan=").Append((int)PlanOutcome);
            sb.Append("|end=").Append((int)Ending);
            sb.Append("|rem=").Append(ResultToString(RematchResult));
            sb.Append("|dice=").Append(Esc(Dice.CaptureState()));
            return sb.ToString();
        }

        /// <summary>Відновити кампанію зі зліпка (у свіжий об'єкт, створений через New з тим самим сценарієм).</summary>
        public void RestoreState(string blob)
        {
            if (string.IsNullOrEmpty(blob)) throw new ArgumentException("порожній зліпок");
            var parts = blob.Split('|');
            if (parts[0] != "ring1") throw new FormatException("невідома версія зліпка кампанії");
            var kv = new Dictionary<string, string>();
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq < 0) throw new FormatException("пошкоджений зліпок кампанії");
                kv[parts[i].Substring(0, eq)] = parts[i].Substring(eq + 1);
            }

            var stage = (RingStage)int.Parse(kv["stage"], CultureInfo.InvariantCulture);
            int promo = int.Parse(kv["promo"], CultureInfo.InvariantCulture);
            var state = new FeudState();
            state.RestoreState(Unesc(kv["state"]));
            string node = Unesc(kv["node"]);

            State = state;
            PromoIndex = promo;
            Stage = stage;
            CurrentMatch = null;
            IsRematch = false;
            CurrentPromoRunner = null;
            if (stage == RingStage.Promo)
                CurrentPromoRunner = new PromoRunner(Script, Script.Promos[promo], State, string.IsNullOrEmpty(node) ? null : node);
            _preMatchState = kv["pre"].Length == 0 ? null : Unesc(kv["pre"]);
            Result = ResultFromString(kv["res"]);
            PlanOutcome = (FeudPlanOutcome)int.Parse(kv["plan"], CultureInfo.InvariantCulture);
            Ending = (FeudEnding)int.Parse(kv["end"], CultureInfo.InvariantCulture);
            RematchResult = ResultFromString(kv["rem"]);
            Dice.RestoreState(Unesc(kv["dice"]));
        }
    }
}
