using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Core.Ring
{
    /// <summary>Оцінка плану букера після матчу (gdd §3): виконано / звернуто / ні те ні те.</summary>
    public enum FeudPlanOutcome { Neither = 0, Fulfilled = 1, Swerved = 2 }

    /// <summary>
    /// Стан ворожнечі (gdd §3): шкали, шляхи, план, умови матчу, прапори, атрибути гравця, перки, очки розвитку.
    /// Промо пишуть у нього ефектами, матч читає через BuildMatchSetup і повертає MatchResult. Весь стан — у сейві (CaptureState).
    /// </summary>
    public sealed class FeudState
    {
        public const int PathCount = 3;

        private int _audience = 3, _heat, _momentum, _trust = 2, _locker;
        private readonly int[] _path = new int[PathCount];
        private int _devPoints;

        public WrestlerStats Player;
        public BookerPlan Plan = BookerPlan.None;
        public MatchStipulations Stipulations = MatchStipulations.None;
        /// <summary>Напарник у куті гравця (ефект ally).</summary>
        public bool HasAlly;
        public readonly HashSet<string> Flags = new HashSet<string>();
        /// <summary>Куплені перки (id з PerkCatalog).</summary>
        public readonly HashSet<string> Perks = new HashSet<string>();

        public FeudState() : this(new WrestlerStats("player", "w.player.name")) { }

        public FeudState(WrestlerStats player)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
        }

        public int Audience { get { return _audience; } set { _audience = Math.Min(_audienceCap, FeudVars.Clamp(FeudVars.Audience, value)); } }

        private int _audienceCap = int.MaxValue;
        /// <summary>
        /// Стеля популярності: у кар'єрі — стеля федерації (П5: 4 / 6 / 8 / 10). Не зберігається — її ставить власник стану
        /// (CareerSession) після створення й відновлення; нижча стеля одразу зрізає поточне значення.
        /// </summary>
        public int AudienceCap
        {
            get { return _audienceCap; }
            set { _audienceCap = Math.Max(0, value); _audience = Math.Min(_audienceCap, _audience); }
        }

        /// <summary>Ажіотаж фіналу, з якого матч дає популярність (П5: 7 на місцевій і регіональній, 8 вище). Не зберігається.</summary>
        public int AudienceHypeAt = 7;
        public int Heat { get { return _heat; } set { _heat = FeudVars.Clamp(FeudVars.Heat, value); } }
        public int Momentum { get { return _momentum; } set { _momentum = FeudVars.Clamp(FeudVars.Momentum, value); } }
        public int Trust { get { return _trust; } set { _trust = FeudVars.Clamp(FeudVars.Trust, value); } }
        public int Locker { get { return _locker; } set { _locker = FeudVars.Clamp(FeudVars.Locker, value); } }
        /// <summary>Невитрачені очки розвитку (≥ 0).</summary>
        public int DevPoints { get { return _devPoints; } set { _devPoints = Math.Max(0, value); } }

        public int GetVar(string name)
        {
            switch (name)
            {
                case FeudVars.Audience: return _audience;
                case FeudVars.Heat: return _heat;
                case FeudVars.Momentum: return _momentum;
                case FeudVars.Trust: return _trust;
                case FeudVars.Locker: return _locker;
                default: throw new ArgumentException("невідома змінна ворожнечі: " + name);
            }
        }

        public void SetVar(string name, int value)
        {
            switch (name)
            {
                case FeudVars.Audience: Audience = value; break;
                case FeudVars.Heat: Heat = value; break;
                case FeudVars.Momentum: Momentum = value; break;
                case FeudVars.Trust: Trust = value; break;
                case FeudVars.Locker: Locker = value; break;
                default: throw new ArgumentException("невідома змінна ворожнечі: " + name);
            }
        }

        public void AddVar(string name, int delta) { SetVar(name, GetVar(name) + delta); }

        public int GetPath(RingPath p) => _path[(int)p];
        public void AddPath(RingPath p, int delta) { _path[(int)p] = Math.Max(0, _path[(int)p] + delta); }

        /// <summary>Домінантний шлях: найбільше очок; нічия — Герой &gt; Лиходій &gt; Бунтар (gdd §3).</summary>
        public RingPath DominantPath()
        {
            var best = RingPath.Face;
            for (int i = 1; i < PathCount; i++)
                if (_path[i] > _path[(int)best]) best = (RingPath)i;
            return best;
        }

        public bool HasFlag(string f) => Flags.Contains(f);
        public bool IsClosed(string branch) => Flags.Contains(FeudVars.ClosedFlag(branch));

        // ---------- умови й ефекти ----------

        /// <summary>
        /// Зв'язок із кар'єрою для умов і ефектів kind=Career (закулісні сцени). Без них умова кар'єри хибна, а ефект нічого не робить —
        /// тож ворожнеча з Молотом поводиться як раніше. Ставить CareerSession на робочу копію стану на час сцени.
        /// </summary>
        public Func<FeudCondition, bool> CareerProbe;
        public Action<FeudEffect> CareerSink;

        public bool Check(FeudCondition c)
        {
            bool v = CheckRaw(c);
            return c.Negate ? !v : v;
        }

        private bool CheckRaw(FeudCondition c)
        {
            switch (c.Kind)
            {
                case ConditionKind.Flag: return Flags.Contains(c.Key);
                case ConditionKind.NotFlag: return !Flags.Contains(c.Key);
                case ConditionKind.Open: return !IsClosed(c.Key);
                case ConditionKind.Var: return GetVar(c.Key) >= c.Min;
                case ConditionKind.Attr:
                    RingAttr a;
                    return FeudVars.TryAttr(c.Key, out a) && Player.Get(a) >= c.Min;
                case ConditionKind.Path:
                    RingPath p;
                    return FeudVars.TryPath(c.Key, out p) && GetPath(p) >= c.Min;
                case ConditionKind.Career: return CareerProbe != null && CareerProbe(c);
                default: return false;
            }
        }

        /// <summary>Перша невиконана умова зі списку; null, якщо всі виконані.</summary>
        public FeudCondition FirstFailed(IEnumerable<FeudCondition> conds)
        {
            if (conds == null) return null;
            foreach (var c in conds) if (!Check(c)) return c;
            return null;
        }

        public void Apply(FeudEffect e)
        {
            switch (e.Kind)
            {
                case EffectKind.Var: AddVar(e.Key, e.Amount); break;
                case EffectKind.Flag: Flags.Add(e.Key); break;
                case EffectKind.Close: Flags.Add(FeudVars.ClosedFlag(e.Key)); break;
                case EffectKind.Path:
                    RingPath p;
                    if (FeudVars.TryPath(e.Key, out p)) AddPath(p, e.Amount);
                    break;
                case EffectKind.Stip:
                    Stipulations |= e.Key == "no_dq" ? MatchStipulations.NoDq : MatchStipulations.NoAlly;
                    break;
                case EffectKind.Plan:
                    BookerPlan bp;
                    Plan = FeudVars.TryPlan(e.Key, out bp) ? bp : BookerPlan.None;
                    break;
                case EffectKind.Ally: HasAlly = e.Value; break;
                case EffectKind.Career:
                    if (CareerSink != null) CareerSink(e);
                    break;
            }
        }

        /// <summary>
        /// Перенести в цей стан підсумок сцени з робочої копії: шкали, шляхи, прапори, очки розвитку. План, умови матчу, напарник,
        /// перки й борець лишаються як були (сцена не має їх чіпати).
        /// </summary>
        public void AbsorbSceneResult(FeudState work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            Audience = work._audience; Heat = work._heat; Momentum = work._momentum; Trust = work._trust; Locker = work._locker;
            for (int i = 0; i < PathCount; i++) _path[i] = work._path[i];
            DevPoints = work._devPoints;
            Flags.Clear();
            foreach (var f in work.Flags) Flags.Add(f);
        }

        public void Apply(IEnumerable<FeudEffect> effects)
        {
            if (effects == null) return;
            foreach (var e in effects) Apply(e);
        }

        // ---------- матч → ворожнеча ----------

        /// <summary>Оцінка плану за результатом (gdd §3 «Виконання планів»). Стан не змінює.</summary>
        public FeudPlanOutcome EvaluatePlan(MatchResult r)
        {
            bool clean = r.Kind == MatchEndKind.Pin || r.Kind == MatchEndKind.Submission;
            switch (Plan)
            {
                case BookerPlan.WinClean:
                    if (r.Winner == MatchWinner.Player && clean && r.PlayerDirtyMoves == 0) return FeudPlanOutcome.Fulfilled;
                    return r.Winner == MatchWinner.Rival ? FeudPlanOutcome.Swerved : FeudPlanOutcome.Neither;
                case BookerPlan.LoseClean:
                    if (r.Winner == MatchWinner.Rival && clean && r.PlayerKickoutsAtTwo >= 1) return FeudPlanOutcome.Fulfilled;
                    return r.Winner == MatchWinner.Player ? FeudPlanOutcome.Swerved : FeudPlanOutcome.Neither;
                case BookerPlan.NoCleanLoss:
                    // «Не програй чисто»: виконано, якщо не програв утриманням чи здачею; звернути — програти чисто.
                    return r.Winner == MatchWinner.Rival && clean ? FeudPlanOutcome.Swerved : FeudPlanOutcome.Fulfilled;
                case BookerPlan.WinAny:
                    if (r.Winner == MatchWinner.Player) return FeudPlanOutcome.Fulfilled;
                    return r.Winner == MatchWinner.Rival ? FeudPlanOutcome.Swerved : FeudPlanOutcome.Neither;
                default: return FeudPlanOutcome.Neither;
            }
        }

        /// <summary>
        /// Застосувати підсумок матчу до стану (gdd §3 «Матч → ворожнеча»), нарахувати очки розвитку (§6), повернути оцінку плану.
        /// Ажіотаж ≥ 7 і при звернутому плані дає популярність +1 двічі (загальне правило + правило swerve) — так у gdd.
        /// </summary>
        public FeudPlanOutcome ApplyMatchResult(MatchResult r) { return ApplyResult(r, 1); }

        /// <summary>Вага популярності розв'язки: ажіотаж фіналу ворожнечі важить удвічі (gdd §6).</summary>
        public const int DecisiveAudienceWeight = 2;

        /// <summary>
        /// Підсумок матча ворожнечі з трьох (gdd §3, §5.2, §6): умови й план матчу з даних (<see cref="PrepareMatch"/>), результат
        /// у стан (популярність — ×2 для розв'язки), прапори результату m{N}_won / _lost / _draw / _dirty / _plan_ok /
        /// _plan_swerved, і план та умови очищаються — наступний матч дістане свої з промо чи з даних. Heat НЕ скидається
        /// (його скидає лише кінцівка, <see cref="ResetHeatAfterFeud"/>). Повертає оцінку плану цього матчу.
        /// </summary>
        public FeudPlanOutcome CompleteMatch(MatchResult r, MatchDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            PrepareMatch(def);
            var outcome = ApplyResult(r, def.Decisive ? DecisiveAudienceWeight : 1);
            Flags.Add(FeudVars.MatchFlag(def.Id, r.Winner == MatchWinner.Player ? "won" : r.Winner == MatchWinner.Rival ? "lost" : "draw"));
            if (r.Kind == MatchEndKind.Disqualification || r.Kind == MatchEndKind.CountOut || r.Kind == MatchEndKind.DoubleCountOut)
                Flags.Add(FeudVars.MatchFlag(def.Id, "dirty"));
            if (outcome == FeudPlanOutcome.Fulfilled) Flags.Add(FeudVars.MatchFlag(def.Id, "plan_ok"));
            if (outcome == FeudPlanOutcome.Swerved) Flags.Add(FeudVars.MatchFlag(def.Id, "plan_swerved"));
            Plan = BookerPlan.None;
            Stipulations = MatchStipulations.None;
            return outcome;
        }

        /// <summary>
        /// Підготувати стан до матчу з даних (ідемпотентно): умови матчу = з промо + за замовчуванням матчу; план — з промо, а
        /// якщо промо його не задали, то перше правило плану матчу з виконаними умовами.
        /// </summary>
        public void PrepareMatch(MatchDef def)
        {
            if (def == null) return;
            if (Plan == BookerPlan.None)
                foreach (var rule in def.Plans)
                    if (FirstFailed(rule.Requires) == null) { Plan = rule.Plan; break; }
            Stipulations |= def.Stipulations;
        }

        private FeudPlanOutcome ApplyResult(MatchResult r, int audienceWeight)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            var outcome = EvaluatePlan(r);

            if (r.FinalHype >= AudienceHypeAt) Audience += audienceWeight;
            else if (r.FinalHype <= 3) Audience -= audienceWeight;

            if (outcome == FeudPlanOutcome.Fulfilled) Trust += 1;
            else if (outcome == FeudPlanOutcome.Swerved)
            {
                if (r.FinalHype >= 7) { Audience += 1; Flags.Add("swerve_won"); }
                else Trust -= 2;
                AddPath(RingPath.Rebel, 1);
            }

            if (r.PlayerDirtyMoves >= 2) AddPath(RingPath.Heel, 1);
            if (r.PlayerSells >= 2) AddPath(RingPath.Face, 1);

            DevPoints += Development.PointsFor(r, outcome);
            return outcome;
        }

        /// <summary>Після розв'язки ворожнеча скидається до 1 (gdd §3). Викликає той, хто веде потік, лише після останнього матчу.</summary>
        public void ResetHeatAfterFeud() { Heat = 1; }

        // ---------- збирання матчу ----------

        /// <summary>
        /// MatchSetup зі стану (gdd §1, §3, §6): імпульс старту = імпульс ворожнечі + перк; ажіотаж старту = 1 + Heat + перк
        /// (нагороди промо вже вкладені в Heat/Momentum своїми ефектами), 0–10; роль = домінантний шлях.
        /// Напарник — прапор HasAlly (умову «повага ≥ 1» ставить сам граф: варіант з ally вимагає var locker).
        /// </summary>
        public MatchSetup BuildMatchSetup(FeudScript script, LateRole lateRole = LateRole.None)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            var mods = Development.ToModifiers(Perks);
            LateRoles.ApplyTo(mods, lateRole);
            return new MatchSetup
            {
                Player = Player.Clone(),
                Rival = script.Rival.Clone(),
                PlayerMods = mods,
                PlayerStartMomentum = Math.Max(0, Math.Min(10, Momentum + mods.StartMomentumBonus)),
                RivalStartMomentum = Math.Max(0, Math.Min(10, script.RivalStartMomentum)),
                StartHype = Math.Max(0, Math.Min(10, 1 + Heat + mods.StartHypeBonus)),
                PlayerHasAlly = HasAlly,
                Stipulations = Stipulations,
                Plan = Plan,
                PlayerRole = DominantPath()
            };
        }

        // ---------- сейв ----------

        public FeudState Clone()
        {
            var c = new FeudState();
            c.RestoreState(CaptureState());
            return c;
        }

        private static string Esc(string s) => Uri.EscapeDataString(s ?? "");
        private static string Unesc(string s) => Uri.UnescapeDataString(s);

        private static string Join(IEnumerable<string> items)
        {
            var list = new List<string>(items);
            list.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Esc(list[i])); }
            return sb.ToString();
        }

        /// <summary>Зліпок стану рядком (канонічний: однаковий стан — однаковий рядок).</summary>
        public string CaptureState()
        {
            var sb = new StringBuilder("feud1");
            sb.Append("|a=").Append(_audience).Append("|h=").Append(_heat).Append("|m=").Append(_momentum)
              .Append("|t=").Append(_trust).Append("|l=").Append(_locker)
              .Append("|p=").Append(_path[0]).Append(',').Append(_path[1]).Append(',').Append(_path[2])
              .Append("|plan=").Append((int)Plan).Append("|stip=").Append((int)Stipulations)
              .Append("|ally=").Append(HasAlly ? 1 : 0).Append("|pts=").Append(_devPoints)
              .Append("|flags=").Append(Join(Flags)).Append("|perks=").Append(Join(Perks))
              .Append("|pl=").Append(Esc(Player.Id)).Append(',').Append(Esc(Player.NameKey)).Append(',').Append((int)Player.FinisherAttr);
            for (int i = 0; i < 5; i++) sb.Append(i == 0 ? "," : ",").Append(Player.Get((RingAttr)i));
            return sb.ToString();
        }

        /// <summary>Відновити стан зі зліпка (перезаписує все).</summary>
        public void RestoreState(string blob)
        {
            if (string.IsNullOrEmpty(blob)) throw new ArgumentException("порожній зліпок");
            var parts = blob.Split('|');
            if (parts[0] != "feud1") throw new FormatException("невідома версія зліпка ворожнечі");
            var kv = new Dictionary<string, string>();
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq < 0) throw new FormatException("пошкоджений зліпок ворожнечі");
                kv[parts[i].Substring(0, eq)] = parts[i].Substring(eq + 1);
            }
            Audience = I(kv, "a"); Heat = I(kv, "h"); Momentum = I(kv, "m"); Trust = I(kv, "t"); Locker = I(kv, "l");
            var pp = kv["p"].Split(',');
            for (int i = 0; i < PathCount; i++) _path[i] = int.Parse(pp[i]);
            Plan = (BookerPlan)I(kv, "plan");
            Stipulations = (MatchStipulations)I(kv, "stip");
            HasAlly = I(kv, "ally") != 0;
            DevPoints = I(kv, "pts");
            Flags.Clear(); Perks.Clear();
            foreach (var f in Split(kv["flags"])) Flags.Add(f);
            foreach (var f in Split(kv["perks"])) Perks.Add(f);
            var pl = kv["pl"].Split(',');
            var w = new WrestlerStats(Unesc(pl[0]), Unesc(pl[1])) { FinisherAttr = (RingAttr)int.Parse(pl[2]) };
            for (int i = 0; i < 5; i++) w.Set((RingAttr)i, int.Parse(pl[3 + i]));
            Player = w;
        }

        private static int I(Dictionary<string, string> kv, string key)
        {
            string v;
            if (!kv.TryGetValue(key, out v)) throw new FormatException("у зліпку немає '" + key + "'");
            return int.Parse(v);
        }

        private static IEnumerable<string> Split(string s)
        {
            if (string.IsNullOrEmpty(s)) yield break;
            foreach (var x in s.Split(',')) yield return Unesc(x);
        }
    }
}
