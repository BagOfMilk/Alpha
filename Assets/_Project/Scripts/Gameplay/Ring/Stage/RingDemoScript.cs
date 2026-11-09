using System;
using System.Collections.Generic;
using Game.Core.Randomness;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Кубик демо (Ф2, крок 9): суму кожного кидка 2d6 називає сценарій у мить кидка — за потоком ядра (<c>m:act:</c>,
    /// <c>m:rev:</c>, <c>m:kick:</c>, <c>m:endure:</c>, <c>m:ex:</c>). Ядро кидає як завжди, тож події, ефекти, кінцівки й переможець —
    /// ті, що дає ядро, а не вигадані демо. Випадковості немає: той самий сценарій — той самий журнал.
    /// </summary>
    public sealed class DemoDice : IDiceRoller
    {
        private readonly Func<string, int> _total;
        private string _pairStream;
        private int _second = -1;

        public DemoDice(Func<string, int> total)
        {
            if (total == null) throw new ArgumentNullException("total");
            _total = total;
        }

        /// <summary>Скільки кидків 2d6 зроблено.</summary>
        public int Rolls { get; private set; }

        public double Roll01(string streamId)
        {
            int d;
            // TwoD6.Roll кидає дві кістки одним потоком поспіль: перша — половина суми, друга — решта.
            if (_second >= 0 && streamId == _pairStream) { d = _second; _second = -1; }
            else
            {
                int t = Math.Max(2, Math.Min(12, _total(streamId)));
                d = t / 2; _second = t - d; _pairStream = streamId; Rolls++;
            }
            return (d - 0.5) / 6.0;
        }

        public string CaptureState() { return ""; }
        public void RestoreState(string blob) { }
    }

    /// <summary>Сценарій не зіграється так, як написаний: ініціатива не в того, дія недоступна, ступінь недосяжний.</summary>
    public sealed class DemoScriptException : InvalidOperationException
    {
        public DemoScriptException(string message) : base(message) { }
    }

    /// <summary>
    /// Зіграний сценарій демо. Рушій — у кінці матчу: журнал містить усі події, <see cref="MatchEngine.Result"/> — кінцівку.
    /// Показ повторює журнал від <see cref="StartCount"/> (<c>RingMatchDirector.BeginReplay</c>), так само як живий матч.
    /// </summary>
    public sealed class DemoMatch
    {
        public string Spec;
        public MatchEngine Engine;
        /// <summary>Скільки подій дав сам старт рушія (стартовий обмін): живий матч їх теж не показує.</summary>
        public int StartCount;
        /// <summary>Кроки сценарію текстом — для журналу огляду.</summary>
        public readonly List<string> Steps = new List<string>();

        public IReadOnlyList<MatchEvent> Events { get { return Engine.State.Log; } }
    }

    /// <summary>
    /// Сценарії демо під нове ядро (Ф2, крок 9; ZONES.md «Чого бракує»): кожен прийом каталогу на трьох ступенях, утримання з
    /// вирваним на «раз» / «два» / «три», больовий, фінішер, кінцівки відлік / подвійний відлік / дискваліфікація, «без DQ» з
    /// предметами, вихід за ринг і повернення, обмін на старті, попередження рефері. Сценарій каже лише, хто діє, що й з яким
    /// ступенем; рушій — справжній (<see cref="MatchEngine"/>), кубик — <see cref="DemoDice"/>. Чистий C#: тести
    /// <c>RingDemoScriptTests</c>; прапорці демо в Unity читає сесія сцени («Изучение»).
    /// </summary>
    public static class RingDemoScript
    {
        public const string AllSpec = "all";

        private const MatchSide P = MatchSide.Player;
        private const MatchSide R = MatchSide.Rival;
        private static readonly RollTier[] Tiers = { RollTier.Strong, RollTier.Partial, RollTier.Miss };

        private static readonly string[] Fixed =
        {
            "exchange", "pin 1", "pin 2", "pin 3", "sub", "warn", "outside", "nodq", "end countout", "end double", "end dq"
        };

        /// <summary>Усі сценарії: загальні, <c>move &lt;id&gt;</c> для кожного прийому каталогу, <c>finisher &lt;id&gt;</c> для кожного фінішера.</summary>
        public static List<string> Specs(MoveCatalog moves)
        {
            if (moves == null) throw new ArgumentNullException("moves");
            var list = new List<string>(Fixed);
            foreach (var m in moves.All) list.Add("move " + m.Id);
            foreach (var m in moves.All) if (m.Action == MatchActionId.Finisher) list.Add("finisher " + m.Id);
            return list;
        }

        /// <summary><c>all</c> — усі сценарії по черзі (окремими матчами); інше — сам сценарій.</summary>
        public static List<string> Expand(string spec, MoveCatalog moves)
        {
            if (spec != null && spec.Trim().Equals(AllSpec, StringComparison.OrdinalIgnoreCase)) return Specs(moves);
            return new List<string> { spec };
        }

        /// <summary>Борці демо: однакові, усі ступені досяжні (Сила +2, Спритність, Техніка, Харизма +1, Витривалість 0).</summary>
        public static MatchSetup Setup(bool noDq)
        {
            return new MatchSetup
            {
                Player = Wrestler("demo_player", "w.player.name"),
                Rival = Wrestler("demo_rival", "w.hammer.name"),
                StartHype = 2,
                PlayerRole = RingPath.Face,
                RivalRole = RingPath.Heel,
                Stipulations = noDq ? MatchStipulations.NoDq : MatchStipulations.None,
                // демо довше за матч: ліміт дій не має обривати показ нічиєю
                RoundLimit = 400
            };
        }

        private static WrestlerStats Wrestler(string id, string nameKey)
        {
            var w = new WrestlerStats(id, nameKey) { FinisherAttr = RingAttr.Strength };
            w.Set(RingAttr.Strength, 2);
            w.Set(RingAttr.Agility, 1);
            w.Set(RingAttr.Technique, 1);
            w.Set(RingAttr.Stamina, 0);
            w.Set(RingAttr.Charisma, 1);
            return w;
        }

        /// <summary>Зіграти сценарій до кінця матчу. Невідомий сценарій чи прийом — <see cref="ArgumentException"/>.</summary>
        public static DemoMatch Play(string spec, MoveCatalog moves)
        {
            if (moves == null) throw new ArgumentNullException("moves");
            if (string.IsNullOrWhiteSpace(spec)) throw new ArgumentException("порожній сценарій демо");
            var parts = spec.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string head = parts[0].ToLowerInvariant();
            string arg = parts.Length > 1 ? parts[1] : null;
            string norm = arg != null ? head + " " + arg : head;
            Runner r;
            switch (head)
            {
                case "exchange":
                    r = new Runner(norm, Setup(false), R, moves);
                    Exchange(r);
                    break;
                case "pin":
                    RollTier kick;
                    if (arg == "1") kick = RollTier.Strong;
                    else if (arg == "2") kick = RollTier.Partial;
                    else if (arg == "3") kick = RollTier.Miss;
                    else throw new ArgumentException("pin 1 | 2 | 3 — на який рахунок вирвався, а не «" + arg + "»");
                    r = new Runner(norm, Setup(false), P, moves);
                    PinDemo(r, kick);
                    break;
                case "sub":
                    r = new Runner(norm, Setup(false), P, moves);
                    Sub(r);
                    break;
                case "warn":
                    r = new Runner(norm, Setup(false), R, moves);
                    Warn(r);
                    break;
                case "outside":
                    r = new Runner(norm, Setup(false), P, moves);
                    Outside(r);
                    break;
                case "nodq":
                    r = new Runner(norm, Setup(true), P, moves);
                    NoDq(r);
                    break;
                case "end":
                    if (arg == "countout") { r = new Runner(norm, Setup(false), P, moves); CountOut(r); }
                    else if (arg == "double") { r = new Runner(norm, Setup(false), P, moves); DoubleCountOut(r); }
                    else if (arg == "dq") { r = new Runner(norm, Setup(false), R, moves); Dq(r); }
                    else throw new ArgumentException("end countout | double | dq, а не «" + arg + "»");
                    break;
                case "move":
                    if (moves.Find(arg) == null) throw new ArgumentException("невідомий прийом демо: " + arg);
                    r = new Runner(norm, Setup(false), P, moves);
                    MoveDemo(r, moves.Get(arg));
                    break;
                case "finisher":
                    var fm = moves.Find(arg);
                    if (fm == null || fm.Action != MatchActionId.Finisher) throw new ArgumentException("не фінішер каталогу: " + arg);
                    r = new Runner(norm, Setup(false), P, moves);
                    r.Ready(P);
                    r.Act(P, MatchActionId.Finisher, RollTier.Strong, fm.Id, RollTier.Miss);
                    break;
                case AllSpec:
                    throw new ArgumentException("all — це перелік сценаріїв: RingDemoScript.Expand / Specs");
                default:
                    throw new ArgumentException("невідомий сценарій демо «" + spec + "»: " + string.Join(", ", Fixed)
                                                + ", move <id>, finisher <id>, all");
            }
            if (!r.E.IsOver) throw new DemoScriptException(norm + ": сценарій скінчився, а матч — ні");
            return r.Done();
        }

        // ============================ сценарії ============================

        /// <summary>Обмін на старті бере суперник; реверс 7–9 — знову Обмін (бере гравець); реверс 10+ — нападник лежить.</summary>
        private static void Exchange(Runner r)
        {
            r.Act(R, MatchActionId.Strike, RollTier.Partial, r.Next(MatchActionId.Strike));
            r.Act(R, MatchActionId.Throw, RollTier.Strong, r.Next(MatchActionId.Throw), reverse: RollTier.Partial, exchangeTo: P);
            r.Act(P, MatchActionId.Strike, RollTier.Strong, r.Next(MatchActionId.Strike));
            r.Act(P, MatchActionId.Throw, RollTier.Strong, r.Next(MatchActionId.Throw), reverse: RollTier.Strong);
            r.Finish(P);
        }

        /// <summary>Утримання: вирвався на «раз» (10+), на «два» (7–9) чи «три» (поразка). Перші два — далі переможне утримання.</summary>
        private static void PinDemo(Runner r, RollTier kick)
        {
            r.Hurt(P, MatchRules.PinMinDamage);
            r.Floor(P);
            r.Act(P, MatchActionId.Pin, defense: kick);
            if (kick != RollTier.Miss) r.Finish(P);
        }

        /// <summary>Больовий: тримається (ще хід), вирвався (ініціатива захиснику), здався.</summary>
        private static void Sub(Runner r)
        {
            var subs = r.All(MatchActionId.Submission);
            r.Hurt(P, MatchRules.PinMinDamage);
            r.Floor(P);
            r.Act(P, MatchActionId.Submission, RollTier.Partial, r.Pick(subs, 0), RollTier.Partial);
            r.Act(P, MatchActionId.Submission, RollTier.Strong, r.Pick(subs, 1), RollTier.Strong);
            r.Floor(P);
            r.Act(P, MatchActionId.Submission, RollTier.Strong, r.Pick(subs, 2), RollTier.Miss);
        }

        /// <summary>Попередження рефері: брудний прийом 7–9 суперника; далі матч іде своїм ходом.</summary>
        private static void Warn(Runner r)
        {
            r.Act(R, MatchActionId.Dirty, RollTier.Partial, r.Next(MatchActionId.Dirty));
            r.Act(R, MatchActionId.Strike, RollTier.Miss, r.Next(MatchActionId.Strike));
            r.Finish(P);
        }

        /// <summary>Вихід за ринг на трьох ступенях, бійка за рингом, повернення склейкою. Відлік до десяти не доходить.</summary>
        private static void Outside(Runner r)
        {
            r.Act(P, MatchActionId.LeaveRing, RollTier.Miss);       // не викинув: ініціатива — суперникові
            r.Give(P);
            r.Act(P, MatchActionId.LeaveRing, RollTier.Partial);    // обидва за рингом, стоять
            r.Act(P, MatchActionId.Strike, RollTier.Strong, r.Next(MatchActionId.Strike));   // відлік 3
            r.Act(P, MatchActionId.Throw, RollTier.Strong, r.Next(MatchActionId.Throw));     // 6, суперник на підлозі
            r.Act(P, MatchActionId.ReturnToRing);                    // знову на рингу, відлік скинуто
            r.Act(P, MatchActionId.LeaveRing, RollTier.Strong);     // 10+: суперник лежить за рингом
            r.Act(P, MatchActionId.Taunt, RollTier.Strong, r.Next(MatchActionId.Taunt));     // 3
            r.Act(P, MatchActionId.ReturnToRing);
            r.Finish(P);
        }

        /// <summary>«Без DQ»: предмет за рингом на трьох ступенях; відліку немає.</summary>
        private static void NoDq(Runner r)
        {
            r.Act(P, MatchActionId.LeaveRing, RollTier.Strong);
            r.Act(P, MatchActionId.Weapon, RollTier.Strong);
            r.Act(P, MatchActionId.Taunt, RollTier.Strong, r.Next(MatchActionId.Taunt));
            r.Act(P, MatchActionId.Weapon, RollTier.Partial);
            r.Act(P, MatchActionId.Taunt, RollTier.Strong, r.Next(MatchActionId.Taunt));
            r.Act(P, MatchActionId.Weapon, RollTier.Miss);          // не влучив: ініціатива — суперникові
            r.OppUp(P);
            r.Act(P, MatchActionId.ReturnToRing);
            r.Finish(P);
        }

        /// <summary>Перемога відліком: суперник лежить за рингом на «десять», гравець на ногах.</summary>
        private static void CountOut(Runner r)
        {
            r.Act(P, MatchActionId.LeaveRing, RollTier.Strong);
            r.Act(P, MatchActionId.Taunt, RollTier.Strong, r.Next(MatchActionId.Taunt));     // 3
            r.Act(P, MatchActionId.CatchBreath);                                              // 6, ініціатива суперникові
            r.Act(R, MatchActionId.GetUp, RollTier.Miss);                                     // 9, не встав
            r.Act(P, MatchActionId.Taunt, RollTier.Strong, r.Next(MatchActionId.Taunt));     // 12: відлік
        }

        /// <summary>Подвійний відлік: обидва за рингом на ногах, на «десять» не повернувся ніхто.</summary>
        private static void DoubleCountOut(Runner r)
        {
            r.Act(P, MatchActionId.LeaveRing, RollTier.Partial);
            for (int i = 0; i < 4; i++) r.Act(P, MatchActionId.Strike, RollTier.Partial, r.Next(MatchActionId.Strike));
        }

        /// <summary>Дискваліфікація: третє попередження супернику — перемога гравця.</summary>
        private static void Dq(Runner r)
        {
            for (int i = 0; i < MatchRules.WarningsToDq; i++)
                r.Act(R, MatchActionId.Dirty, RollTier.Partial, r.Next(MatchActionId.Dirty));
        }

        /// <summary>Прийом каталогу на трьох ступенях: чисто, частково, провал; між ними — підготовка, наприкінці — утримання.</summary>
        private static void MoveDemo(Runner r, MoveDef m)
        {
            foreach (var t in Tiers)
            {
                r.Prep(P, m.Action);
                // больовий: на 10+ тримається (ще хід), на 7–9 вирвався; фінішер — вирвався на «два»
                RollTier defense = m.Action == MatchActionId.Submission && t == RollTier.Partial ? RollTier.Strong : RollTier.Partial;
                r.Act(P, m.Action, t, m.Id, defense);
            }
            r.Finish(P);
        }

        // ============================ виконавець ============================

        private sealed class Beat
        {
            public MatchSide Side;
            public MatchActionId Action;
            public RollTier Tier;
            public RollTier Defense;
            public RollTier? Reverse;
        }

        private sealed class Runner
        {
            public readonly MatchEngine E;
            private readonly MoveCatalog _moves;
            private readonly DemoMatch _m;
            private readonly Dictionary<MatchActionId, int> _turn = new Dictionary<MatchActionId, int>();
            private Beat _b;
            private int _actMod, _revMod;
            private MatchSide _exTo;
            private string _what;

            public Runner(string spec, MatchSetup setup, MatchSide first, MoveCatalog moves)
            {
                _moves = moves;
                _m = new DemoMatch { Spec = spec };
                _exTo = first;
                _what = spec + ", обмін на старті";
                E = new MatchEngine(setup, new DemoDice(Total));
                _m.Engine = E;
                _m.StartCount = E.State.Log.Count;
                if (E.CurrentActor != first) throw Fail("обмін на старті взяв " + E.CurrentActor);
            }

            public DemoMatch Done() { return _m; }

            private Fighter F(MatchSide s) { return E.State.Get(s); }

            // ---------- кубик: сума в мить кидка ----------

            private int Total(string stream)
            {
                if (stream.StartsWith("m:ex:", StringComparison.Ordinal))
                    return (stream == "m:ex:p") == (_exTo == MatchSide.Player) ? 12 : 2;
                if (_b == null) return 7;
                var def = MatchEngine.Opp(_b.Side);
                if (stream.StartsWith("m:act:", StringComparison.Ordinal)) return Roll(_b.Tier, _actMod, "кидок нападника");
                if (stream.StartsWith("m:rev:", StringComparison.Ordinal))
                    return Roll(_b.Reverse ?? RollTier.Miss, _revMod, "реверс");
                if (stream.StartsWith("m:kick:", StringComparison.Ordinal))
                {
                    int pen = _b.Action != MatchActionId.Finisher ? 0
                            : _b.Tier == RollTier.Strong ? MatchRules.FinisherStrongPinPenalty : MatchRules.FinisherPartialPinPenalty;
                    return Roll(_b.Defense, E.KickoutModifier(def, pen), "«Вирватися»");
                }
                if (stream.StartsWith("m:endure:", StringComparison.Ordinal))
                {
                    int pen = _b.Tier == RollTier.Strong ? MatchRules.SubmissionStrongPenalty : MatchRules.SubmissionPartialPenalty;
                    return Roll(_b.Defense, E.EndureModifier(def, pen), "«Терпіти»");
                }
                throw Fail("непередбачений кидок «" + stream + "»");
            }

            /// <summary>Сума 2d6, що з модифікатором дає ступінь <paramref name="t"/>: 10+ — 11, 7–9 — 8, провал — 4, у межах 2–12.</summary>
            private int Roll(RollTier t, int mod, string what)
            {
                int lo, hi, pref;
                switch (t)
                {
                    case RollTier.Strong: lo = 10; hi = 99; pref = 11; break;
                    case RollTier.Partial: lo = 7; hi = 9; pref = 8; break;
                    default: lo = -99; hi = 6; pref = 4; break;
                }
                int rlo = Math.Max(2, lo - mod), rhi = Math.Min(12, hi - mod);
                if (rlo > rhi) throw Fail(what + ": ступінь " + t + " недосяжний з модифікатором " + mod);
                return Math.Max(rlo, Math.Min(rhi, pref - mod));
            }

            // ---------- крок ----------

            /// <summary>
            /// Дія <paramref name="side"/> зі ступенем <paramref name="tier"/>. <paramref name="defense"/> — ступінь «Вирватися» /
            /// «Терпіти» (утримання, фінішер, больовий); <paramref name="reverse"/> — захисник реверсує великий прийом цим ступенем
            /// (без нього — продає); <paramref name="exchangeTo"/> — хто бере Обмін після реверсу 7–9 (за замовчуванням захисник).
            /// </summary>
            public void Act(MatchSide side, MatchActionId id, RollTier tier = RollTier.Strong, string move = null,
                            RollTier defense = RollTier.Partial, RollTier? reverse = null, MatchSide? exchangeTo = null)
            {
                _what = _m.Spec + ", крок " + (_m.Steps.Count + 1) + ": " + side + " " + id + (move != null ? " «" + move + "»" : "") + " " + tier;
                if (E.IsOver) throw Fail("матч уже скінчився");
                if (E.Phase != MatchPhase.AwaitAction) throw Fail("фаза " + E.Phase);
                if (E.CurrentActor != side) throw Fail("ініціатива в " + E.CurrentActor);
                MoveHint hint = null;
                if (move != null)
                {
                    var def = _moves.Get(move);
                    if (def.Action != id) throw Fail("прийом «" + move + "» — дія " + def.Action);
                    hint = def.ToHint(0);
                }
                var opt = E.Describe(id, hint != null ? hint.RollMod : 0);
                if (!opt.Available) throw Fail("дія недоступна: " + opt.Reason);
                // ступінь пишемо лише там, де кидає нападник (утримання кидає захисник — «захист» нижче)
                if (!opt.HasRoll || id == MatchActionId.Pin) _what = _what.Substring(0, _what.Length - (" " + tier).Length);
                _b = new Beat { Side = side, Action = id, Tier = tier, Defense = defense, Reverse = reverse };
                _actMod = opt.Modifier;
                _exTo = exchangeTo ?? MatchEngine.Opp(side);
                var made = new List<MatchEvent>(E.Perform(id, hint));
                if (E.Phase == MatchPhase.AwaitReaction)
                {
                    _revMod = E.PendingReaction.ReverseModifier;
                    made.AddRange(E.React(reverse.HasValue ? ReactionChoice.Reverse : ReactionChoice.Sell));
                }
                else if (reverse.HasValue) throw Fail("реверсу не було: у захисника немає жетонів або він приголомшений");
                if (E.Phase == MatchPhase.AwaitBoost) made.AddRange(E.Commit(0));
                _b = null;
                Verify(made, side, id, tier, defense, reverse, opt.HasRoll);
                string tail = reverse.HasValue ? ", реверс " + reverse.Value : "";
                if (id == MatchActionId.Pin || ((id == MatchActionId.Finisher || id == MatchActionId.Submission) && tier != RollTier.Miss && !Reversed(made)))
                    tail += ", захист " + defense;
                _m.Steps.Add(_what + tail);
            }

            private static bool Reversed(List<MatchEvent> made)
            {
                foreach (var ev in made) if (ev.Kind == MatchEventKind.Reverse && ev.Tier != RollTier.Miss) return true;
                return false;
            }

            /// <summary>Ступені в журналі — ті, що написано в сценарії (інакше сценарій розійшовся з ядром).</summary>
            private void Verify(List<MatchEvent> made, MatchSide side, MatchActionId id, RollTier tier, RollTier defense, RollTier? reverse, bool rolled)
            {
                if (reverse.HasValue)
                {
                    var rev = Find(made, MatchEventKind.Reverse);
                    if (rev == null || rev.Tier != reverse.Value) throw Fail("реверс " + (rev != null ? rev.Tier.ToString() : "—") + " замість " + reverse.Value);
                    if (reverse.Value != RollTier.Miss) return;   // прийом зірвано: кидка нападника немає
                }
                if (id == MatchActionId.Pin)
                {
                    var k = Find(made, MatchEventKind.Kickout);
                    if (k == null || k.Tier != defense) throw Fail("утримання: " + (k != null ? k.Tier.ToString() : "—") + " замість " + defense);
                    return;
                }
                if (!rolled) return;
                MatchEvent act = null;
                foreach (var ev in made)
                    if (ev.Kind == MatchEventKind.Action && ev.HasRoll && ev.Actor == side && ev.Action == id) act = ev;
                if (act == null || act.Tier != tier) throw Fail("кидок " + (act != null ? act.Tier.ToString() : "—") + " замість " + tier);
                if (tier == RollTier.Miss) return;
                var kind = id == MatchActionId.Finisher ? MatchEventKind.Kickout : id == MatchActionId.Submission ? MatchEventKind.Endure : (MatchEventKind?)null;
                if (kind == null) return;
                var d = Find(made, kind.Value);
                if (d == null || d.Tier != defense) throw Fail("захист " + (d != null ? d.Tier.ToString() : "—") + " замість " + defense);
            }

            private static MatchEvent Find(List<MatchEvent> made, MatchEventKind k)
            {
                foreach (var ev in made) if (ev.Kind == k) return ev;
                return null;
            }

            private DemoScriptException Fail(string why) { return new DemoScriptException(_what + " — " + why); }

            // ---------- прийоми каталогу ----------

            /// <summary>Прийоми каталогу з дією <paramref name="id"/> у центрі (для підготовки — без склейок).</summary>
            public List<string> All(MatchActionId id)
            {
                var list = new List<string>();
                foreach (var m in _moves.All) if (m.Action == id && m.Staging == MoveStaging.Center) list.Add(m.Id);
                if (list.Count == 0) foreach (var m in _moves.All) if (m.Action == id) list.Add(m.Id);
                return list;
            }

            public string Pick(List<string> list, int i) { return list.Count == 0 ? null : list[i % list.Count]; }

            /// <summary>Наступний прийом дії по колу: поспіль різні, тож повтору (−1 ажіотажу, §5.4) у підготовці немає.</summary>
            public string Next(MatchActionId id)
            {
                int i;
                _turn.TryGetValue(id, out i);
                _turn[id] = i + 1;
                return Pick(All(id), i);
            }

            // ---------- підготовка: ініціатива, пози, «Сили» ----------

            /// <summary>Ініціатива до <paramref name="side"/>: той, хто діє, переводить подих, а лежачий встає 7–9 (ініціатива переходить сама).</summary>
            public void Give(MatchSide side)
            {
                var cur = E.CurrentActor;
                if (cur == side) return;
                if (F(cur).Posture == Posture.Down) Act(cur, MatchActionId.GetUp, RollTier.Partial);
                else Act(cur, MatchActionId.CatchBreath);
            }

            /// <summary><paramref name="side"/> на ногах і з ініціативою.</summary>
            public void Up(MatchSide side)
            {
                if (F(side).IsUpright) { Give(side); return; }
                if (E.CurrentActor != side) Act(E.CurrentActor, MatchActionId.CatchBreath);
                Act(side, MatchActionId.GetUp, RollTier.Strong);   // 10+: встав, ініціатива лишається
            }

            /// <summary>Суперник <paramref name="side"/> на ногах; ініціатива — у <paramref name="side"/>.</summary>
            public void OppUp(MatchSide side)
            {
                var o = MatchEngine.Opp(side);
                if (F(o).IsUpright) { Give(side); return; }
                if (E.CurrentActor == side) Act(side, MatchActionId.CatchBreath);
                Act(o, MatchActionId.GetUp, RollTier.Partial);   // 7–9: встав, ініціатива переходить
            }

            /// <summary>Щонайменше <paramref name="need"/> «Сил» у <paramref name="side"/>; наприкінці ініціатива в нього.</summary>
            public void Stam(MatchSide side, int need)
            {
                for (int guard = 0; F(side).Stamina < need; guard++)
                {
                    if (guard > 8) throw Fail("не набрати «Сил» " + need);
                    if (E.CurrentActor == side) Act(side, MatchActionId.CatchBreath);
                    Give(side);
                }
                Give(side);
            }

            /// <summary>Ушкодження суперника щонайменше <paramref name="dmg"/>: чисті удари стоячому.</summary>
            public void Hurt(MatchSide side, int dmg)
            {
                var o = MatchEngine.Opp(side);
                for (int guard = 0; F(o).Damage < dmg; guard++)
                {
                    if (guard > 12) throw Fail("не набрати ушкоджень " + dmg);
                    OppUp(side);
                    Up(side);
                    Stam(side, MatchRules.Cost(MatchActionId.Strike));
                    Act(side, MatchActionId.Strike, RollTier.Strong, Next(MatchActionId.Strike));
                }
            }

            /// <summary>Суперник лежить, ініціатива — у <paramref name="side"/>: чистий кидок.</summary>
            public void Floor(MatchSide side)
            {
                Give(side);
                if (F(MatchEngine.Opp(side)).Posture == Posture.Down) return;
                Up(side);
                Stam(side, MatchRules.Cost(MatchActionId.Throw));
                Act(side, MatchActionId.Throw, RollTier.Strong, Next(MatchActionId.Throw));
            }

            /// <summary>Фінішер доступний: ушкодження суперника, імпульс, ажіотаж, повна ціна в «Силах».</summary>
            public void Ready(MatchSide side)
            {
                Hurt(side, MatchRules.FinisherTargetDamage);
                for (int guard = 0; F(side).Momentum < MatchRules.FinisherMomentum || E.State.Hype < MatchRules.FinisherHype; guard++)
                {
                    if (guard > 12) throw Fail("фінішер не стає доступним");
                    Up(side);
                    if (F(MatchEngine.Opp(side)).Posture == Posture.Standing)
                    {
                        Stam(side, MatchRules.Cost(MatchActionId.Strike));
                        Act(side, MatchActionId.Strike, RollTier.Strong, Next(MatchActionId.Strike));
                    }
                    else Act(side, MatchActionId.Taunt, RollTier.Strong, Next(MatchActionId.Taunt));
                }
                Up(side);
                Stam(side, MatchRules.Cost(MatchActionId.Finisher));
            }

            /// <summary>Підготувати дію <paramref name="id"/> для <paramref name="side"/>: пози, ушкодження, «Сили», ініціатива.</summary>
            public void Prep(MatchSide side, MatchActionId id)
            {
                switch (id)
                {
                    case MatchActionId.Strike:
                    case MatchActionId.Throw:
                    case MatchActionId.Dirty:
                        OppUp(side); Up(side); Stam(side, MatchRules.Cost(id));
                        break;
                    case MatchActionId.Taunt:
                        Floor(side);
                        break;
                    case MatchActionId.Submission:
                        Hurt(side, MatchRules.PinMinDamage); Floor(side);
                        break;
                    case MatchActionId.Finisher:
                        Ready(side);
                        break;
                    default:
                        Up(side); Stam(side, MatchRules.Cost(id));
                        break;
                }
            }

            /// <summary>Переможне утримання <paramref name="side"/>: на ринг, ушкодження, кидок, «три».</summary>
            public void Finish(MatchSide side)
            {
                if (E.State.Outside) { Up(side); Act(side, MatchActionId.ReturnToRing); }
                Hurt(side, MatchRules.PinMinDamage);
                Floor(side);
                Act(side, MatchActionId.Pin, defense: RollTier.Miss);
            }
        }
    }
}
