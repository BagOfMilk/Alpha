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

    /// <summary>Підсумок одного зіграного матчу ворожнечі (не реванш): що за матч, який був план, чим скінчилось.</summary>
    public sealed class MatchRecord
    {
        public string MatchId;
        public BookerPlan Plan;
        public FeudPlanOutcome PlanOutcome;
        public MatchResult Result;
    }

    /// <summary>
    /// Фасад кампанії зрізу: одна ворожнеча = три матчі в трьох актах (gdd §1, §5.2). Порядок кроків — у даних (Script.Flow):
    /// промо → матч → розвиток → промо → промо → матч → розвиток → промо → матч → кінцівка → розвиток. Екрани й тести ходять
    /// лише через нього; правил тут немає — він переставляє етапи й зберігає стан.
    /// Потік: Overview → StartPromo → (PromoRunner) → FinishPromo → Overview … → MatchPrep → StartMatch → (MatchEngine) →
    /// FinishMatch → Development → ContinueAfterDevelopment → Overview/MatchPrep … Після розв'язки FinishMatch → Ending →
    /// OpenDevelopment → BuyPerk/BuyAttr → StartRematch → … → FinishMatch → Development (кінець).
    /// </summary>
    public sealed class RingCampaign
    {
        public FeudScript Script { get; }
        public IDiceRoller Dice { get; }
        /// <summary>Стан ворожнечі. Між матчами — з підсумками попередніх (Heat тримається); після розв'язки — з куплене в розвитку.</summary>
        public FeudState State { get; private set; }
        public RingStage Stage { get; private set; }
        /// <summary>Індекс кроку порядку (Script.Flow), який іде або піде наступним; у розвитку після матчу — крок цього матчу.</summary>
        public int StepIndex { get; private set; }
        public PromoRunner CurrentPromoRunner { get; private set; }

        private string _bookerVoice;

        /// <summary>
        /// Голос «booker» у промо ворожнечі (<see cref="PromoRunner.BookerVoice"/>): ставить кар'єра за федерацією, не зберігається.
        /// Діє й на промо, що вже йде (після відновлення сейва кар'єра ставить голос уже після кампанії).
        /// </summary>
        public string BookerVoice
        {
            get { return _bookerVoice; }
            set { _bookerVoice = value; if (CurrentPromoRunner != null) CurrentPromoRunner.BookerVoice = value; }
        }
        public MatchEngine CurrentMatch { get; private set; }
        /// <summary>Йде (або йшов останнім) реванш, а не матч ворожнечі.</summary>
        public bool IsRematch { get; private set; }

        /// <summary>Підсумки зіграних матчів ворожнечі за порядком (без реваншу).</summary>
        public IReadOnlyList<MatchRecord> Records { get { return _records; } }
        private readonly List<MatchRecord> _records = new List<MatchRecord>();

        /// <summary>Підсумок останнього зіграного матчу ворожнечі (null, доки не грали). Після розв'язки — підсумок розв'язки.</summary>
        public MatchResult Result { get { return _records.Count == 0 ? null : _records[_records.Count - 1].Result; } }
        public FeudPlanOutcome PlanOutcome { get { return _records.Count == 0 ? FeudPlanOutcome.Neither : _records[_records.Count - 1].PlanOutcome; } }
        /// <summary>План останнього зіграного матчу (стан після матчу план очищає, тож він живе тут).</summary>
        public BookerPlan LastPlan { get { return _records.Count == 0 ? BookerPlan.None : _records[_records.Count - 1].Plan; } }
        /// <summary>Кінцівка ворожнечі — за розв'язкою (останній матч). Реванш показує свій результат, кінцівку не міняє.</summary>
        public FeudEnding Ending { get; private set; }
        public bool HasEnding { get; private set; }
        /// <summary>Підсумок останнього реваншу (null, доки не грали).</summary>
        public MatchResult RematchResult { get; private set; }

        /// <summary>Стан ворожнечі безпосередньо перед останнім розпочатим матчем (для реваншу розв'язки).</summary>
        private string _preMatchState;

        private RingCampaign(FeudScript script, IDiceRoller dice)
        {
            Script = script ?? throw new ArgumentNullException(nameof(script));
            Dice = dice ?? throw new ArgumentNullException(nameof(dice));
            if (script.Flow.Count == 0) throw new ArgumentException("у сценарії немає порядку кроків");
            State = script.NewState();
            Stage = StageForStep();
        }

        public static RingCampaign New(FeudScript script, IDiceRoller dice) { return new RingCampaign(script, dice); }

        public int PromoCount { get { return Script.Promos.Count; } }
        public int MatchCount { get { return Script.Matches.Count; } }

        /// <summary>Крок, що іде або піде наступним (null — порядок вичерпано).</summary>
        public FlowStep CurrentStep { get { return StepIndex < Script.Flow.Count ? Script.Flow[StepIndex] : null; } }

        /// <summary>Скільки промо стоїть у порядку ДО поточного кроку (= номер наступного/поточного промо, з нуля).</summary>
        public int PromoIndex
        {
            get
            {
                int n = 0;
                for (int i = 0; i < StepIndex && i < Script.Flow.Count; i++) if (!Script.Flow[i].IsMatch) n++;
                return n;
            }
        }

        /// <summary>
        /// Номер матчу з нуля: поточного (матч, розвиток після матчу) або найближчого наступного (між промо). Для UI «матч k з N».
        /// </summary>
        public int CurrentMatchIndex
        {
            get
            {
                int n = 0;
                for (int i = 0; i < StepIndex && i < Script.Flow.Count; i++) if (Script.Flow[i].IsMatch) n++;
                return Math.Min(n, Math.Max(0, MatchCount - 1));
            }
        }

        public MatchDef CurrentMatchDef { get { return Script.Matches[CurrentMatchIndex]; } }

        /// <summary>Акт найближчого кроку (з даних промо чи матчу); 0, якщо не заданий.</summary>
        public int CurrentAct
        {
            get
            {
                var st = CurrentStep;
                if (st == null) return 0;
                return st.IsMatch ? Script.Matches[st.Index].Act : Script.Promos[st.Index].Act;
            }
        }

        /// <summary>
        /// Стан, яким піде найближчий матч: промо-плани й умови плюс значення за замовчуванням з даних. Нічого не міняє — для
        /// екрана ворожнечі перед матчем (план і умови наступного матчу).
        /// </summary>
        public FeudState PreviewNextMatchState()
        {
            var s = State.Clone();
            s.PrepareMatch(CurrentMatchDef);
            return s;
        }

        private RingStage StageForStep()
        {
            var st = CurrentStep;
            return st != null && st.IsMatch ? RingStage.MatchPrep : RingStage.Overview;
        }

        private void Require(RingStage s)
        {
            if (Stage != s) throw new InvalidOperationException("Етап " + Stage + ", а потрібен " + s);
        }

        // ---------- промо ----------

        public PromoRunner StartPromo()
        {
            Require(RingStage.Overview);
            var st = CurrentStep;
            if (st == null || st.IsMatch) throw new InvalidOperationException("Наступний крок — не промо");
            CurrentPromoRunner = new PromoRunner(Script, Script.Promos[st.Index], State) { BookerVoice = _bookerVoice };
            Stage = RingStage.Promo;
            return CurrentPromoRunner;
        }

        /// <summary>Промо дійшло до кінця вузла end: наступний крок порядку — промо чи підготовка до матчу.</summary>
        public void FinishPromo()
        {
            Require(RingStage.Promo);
            if (!CurrentPromoRunner.IsFinished) throw new InvalidOperationException("Промо ще не закінчилось");
            CurrentPromoRunner = null;
            StepIndex++;
            Stage = StageForStep();
        }

        // ---------- матч ----------

        /// <summary>Підготувати стан до матчу з даних, знімок стану ДО матчу (для реваншу), збирання MatchSetup, новий рушій.</summary>
        public MatchEngine StartMatch()
        {
            Require(RingStage.MatchPrep);
            State.PrepareMatch(CurrentMatchDef);
            _preMatchState = State.CaptureState();
            CurrentMatch = new MatchEngine(State.BuildMatchSetup(Script), Dice);
            IsRematch = false;
            Stage = RingStage.Match;
            return CurrentMatch;
        }

        /// <summary>
        /// Матч скінчився: підсумок у стан (прапори результату, популярність, очки розвитку — <see cref="FeudState.CompleteMatch"/>).
        /// Не розв'язка — Development, далі <see cref="ContinueAfterDevelopment"/>. Розв'язка — кінцівка, скидання Heat, Ending.
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
            var def = CurrentMatchDef;
            var plan = State.Plan;
            var outcome = State.CompleteMatch(result, def);
            _records.Add(new MatchRecord { MatchId = def.Id, Plan = plan, PlanOutcome = outcome, Result = result });
            if (def.Decisive)
            {
                Ending = FeudEndings.Resolve(State, result, outcome);
                HasEnding = true;
                State.ResetHeatAfterFeud();
                Stage = RingStage.Ending;
            }
            else Stage = RingStage.Development;
        }

        /// <summary>Можна йти далі після розвитку (матч не розв'язка).</summary>
        public bool CanContinue { get { return Stage == RingStage.Development && !HasEnding && !IsRematch; } }

        /// <summary>«Далі» з екрана розвитку між матчами: наступне промо чи підготовка до наступного матчу.</summary>
        public void ContinueAfterDevelopment()
        {
            if (!CanContinue) throw new InvalidOperationException("Продовжити зараз не можна (етап " + Stage + ")");
            StepIndex++;
            Stage = StageForStep();
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
            if (Stage == RingStage.Ending) Stage = RingStage.Development;
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
            if (Stage == RingStage.Ending) Stage = RingStage.Development;
            return Development.BuyAttr(State, a);
        }

        /// <summary>Реванш — лише розв'язки й лише після кінцівки (режим перевірки; проміжні матчі не повторюються).</summary>
        public bool CanRematch { get { return (Stage == RingStage.Ending || Stage == RingStage.Development) && HasEnding && _preMatchState != null; } }

        /// <summary>
        /// Розв'язка ще раз: стан ДО матчу (шкали, шляхи, план, умови, напарник) + куплені в розвитку перки й
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

        private const string BlobVersion = "ring2";

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

        /// <summary>Записи матчів: «id,план,оцінка,результат» через «;» (кожне поле екрановане).</summary>
        private string RecordsToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _records.Count; i++)
            {
                var r = _records[i];
                if (i > 0) sb.Append(';');
                sb.Append(Esc(r.MatchId)).Append(',').Append((int)r.Plan).Append(',').Append((int)r.PlanOutcome).Append(',')
                  .Append(Esc(ResultToString(r.Result)));
            }
            return sb.ToString();
        }

        private static List<MatchRecord> RecordsFromString(string s)
        {
            var list = new List<MatchRecord>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (var part in s.Split(';'))
            {
                var f = part.Split(',');
                if (f.Length != 4) throw new FormatException("пошкоджений запис матчу у зліпку кампанії");
                list.Add(new MatchRecord
                {
                    MatchId = Unesc(f[0]),
                    Plan = (BookerPlan)int.Parse(f[1], CultureInfo.InvariantCulture),
                    PlanOutcome = (FeudPlanOutcome)int.Parse(f[2], CultureInfo.InvariantCulture),
                    Result = ResultFromString(Unesc(f[3]))
                });
            }
            return list;
        }

        /// <summary>
        /// Зліпок кампанії рядком (версія ring2): етап, крок порядку, вузол, стан ворожнечі, записи матчів, кінцівка, стан кубика.
        /// Старий формат ring1 (один матч) не читається. Матч посеред гри не зберігається: матч ворожнечі віддається як
        /// «підготовка» (стан ворожнечі ще не зачеплений підсумком), реванш — як «розвиток». Збережене під час вибору нагород
        /// промо відновиться на вузлі кидка до кидка: кубик уже пішов далі, тож другий кидок дасть інше число.
        /// </summary>
        public string CaptureState()
        {
            var stage = Stage;
            if (stage == RingStage.Match) stage = IsRematch ? RingStage.Development : RingStage.MatchPrep;
            var sb = new StringBuilder(BlobVersion);
            sb.Append("|stage=").Append((int)stage);
            sb.Append("|step=").Append(StepIndex);
            sb.Append("|node=").Append(Stage == RingStage.Promo && CurrentPromoRunner != null ? Esc(CurrentPromoRunner.Current.Id) : "");
            sb.Append("|state=").Append(Esc(State.CaptureState()));
            sb.Append("|pre=").Append(_preMatchState == null ? "" : Esc(_preMatchState));
            sb.Append("|recs=").Append(Esc(RecordsToString()));
            sb.Append("|end=").Append(HasEnding ? ((int)Ending).ToString(CultureInfo.InvariantCulture) : "");
            sb.Append("|rem=").Append(ResultToString(RematchResult));
            sb.Append("|dice=").Append(Esc(Dice.CaptureState()));
            return sb.ToString();
        }

        /// <summary>Відновити кампанію зі зліпка (у свіжий об'єкт, створений через New з тим самим сценарієм).</summary>
        public void RestoreState(string blob)
        {
            if (string.IsNullOrEmpty(blob)) throw new ArgumentException("порожній зліпок");
            var parts = blob.Split('|');
            if (parts[0] != BlobVersion) throw new FormatException("невідома версія зліпка кампанії: " + parts[0]);
            var kv = new Dictionary<string, string>();
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq < 0) throw new FormatException("пошкоджений зліпок кампанії");
                kv[parts[i].Substring(0, eq)] = parts[i].Substring(eq + 1);
            }

            var stage = (RingStage)int.Parse(kv["stage"], CultureInfo.InvariantCulture);
            int step = int.Parse(kv["step"], CultureInfo.InvariantCulture);
            if (step < 0 || step >= Script.Flow.Count) throw new FormatException("крок " + step + " поза порядком сценарію");
            var state = new FeudState();
            state.RestoreState(Unesc(kv["state"]));
            string node = Unesc(kv["node"]);

            State = state;
            StepIndex = step;
            Stage = stage;
            CurrentMatch = null;
            IsRematch = false;
            CurrentPromoRunner = null;
            if (stage == RingStage.Promo)
            {
                var st = Script.Flow[step];
                if (st.IsMatch) throw new FormatException("етап промо на кроці матчу");
                CurrentPromoRunner = new PromoRunner(Script, Script.Promos[st.Index], State, string.IsNullOrEmpty(node) ? null : node) { BookerVoice = _bookerVoice };
            }
            _preMatchState = kv["pre"].Length == 0 ? null : Unesc(kv["pre"]);
            _records.Clear();
            _records.AddRange(RecordsFromString(Unesc(kv["recs"])));
            HasEnding = kv["end"].Length > 0;
            Ending = HasEnding ? (FeudEnding)int.Parse(kv["end"], CultureInfo.InvariantCulture) : default(FeudEnding);
            RematchResult = ResultFromString(kv["rem"]);
            Dice.RestoreState(Unesc(kv["dice"]));
        }
    }
}
