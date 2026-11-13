using System;
using System.Collections.Generic;
using Game.Core.Ring.Match;

namespace Game.Core.Ring.Career
{
    /// <summary>Ґімік борця (TRPG_DESIGN §5.2): архетип, що дає основу атрибутів і набір прийомів. MVP альфи — чотири.</summary>
    public enum Gimmick { Monster = 0, Flyer = 1, Technician = 2, Showman = 3 }

    /// <summary>Роль борця (§5.3): герой чи лиходій. Вона задає початковий шлях ворожнечі; далі шлях міняють вчинки (поворот).</summary>
    public enum CareerRole { Hero = 0, Villain = 1 }

    /// <summary>Зона тіла для травм (§6.3) і зони-цілі прийомів (§5.4).</summary>
    public enum BodyZone { Head = 0, Body = 1, Legs = 2 }

    public enum MoveKind { Basic = 0, Gimmick = 1, Signature = 2, Finisher = 3, Learn = 4 }

    public enum MoveCategory { Strike = 0, Throw = 1, Power = 2, Aerial = 3, Submission = 4, Hardcore = 5, Showy = 6 }

    /// <summary>
    /// Прийом (§5.4): обгортка над дією рушія матчу. Атрибут кидка бере дія (<see cref="MatchEngine.AttrOf"/>); прийом додає категорію,
    /// зону-ціль (вона ж зона тіла, що напружується — травма зони дає −1 до кидка), ажіотаж за чистий успіх (Heat) і бонус до кидка.
    /// </summary>
    public sealed class MoveDef
    {
        public string Id;
        /// <summary>Ключ назви в ui_uk.json.</summary>
        public string NameKey;
        public MatchActionId Action;
        public MoveCategory Category;
        public BodyZone Zone;
        /// <summary>Додатковий ажіотаж за чистий успіх (10+): 0–2.</summary>
        public int Heat;
        public int RollBonus;
        public MoveKind Kind;
        /// <summary>Ґімік-власник (для ґімік-прийомів, коронних і фінішерів); null — загальний.</summary>
        public string GimmickId;
        /// <summary>Ціна вивчення в очках розвитку (лише для Gimmick/Learn).</summary>
        public int Cost;
        /// <summary>
        /// Постановка для показу (рішення власника 08.11.2026): усе в центрі рингу, прийом із канатів чи кута — склейкою кадру.
        /// У JSON — «staging»: center | ropes | corner; без поля — ризикований трюк з кута, решта в центрі.
        /// </summary>
        public MoveStaging Staging;

        public bool IsLearnable { get { return Kind == MoveKind.Gimmick || Kind == MoveKind.Learn; } }

        public MoveHint ToHint(int injuryPenalty)
        {
            return new MoveHint { Key = Id, RollMod = RollBonus - injuryPenalty, HypeOnStrong = Heat, Staging = Staging };
        }
    }

    /// <summary>Каталог прийомів (career_moves.json). Атрибут кидка — за дією, прийом його лише обгортає.</summary>
    public sealed class MoveCatalog
    {
        public readonly List<MoveDef> All = new List<MoveDef>();
        private readonly Dictionary<string, MoveDef> _byId = new Dictionary<string, MoveDef>();

        public MoveDef Find(string id)
        {
            MoveDef m;
            return id != null && _byId.TryGetValue(id, out m) ? m : null;
        }

        public MoveDef Get(string id)
        {
            var m = Find(id);
            if (m == null) throw new ArgumentException("невідомий прийом: " + id);
            return m;
        }

        public static MoveCatalog Load(string json)
        {
            var c = new MoveCatalog();
            var root = MiniJson.ParseObject(json);
            foreach (var o in JsonRead.ObjList(root, "moves", "moves"))
            {
                string id = JsonRead.Str(o, "id", "move");
                string where = "move " + id;
                var m = new MoveDef
                {
                    Id = id,
                    NameKey = JsonRead.Str(o, "name", where),
                    Action = ParseEnum<MatchActionId>(JsonRead.Str(o, "action", where), where + ".action"),
                    Category = ParseEnum<MoveCategory>(JsonRead.Str(o, "category", where), where + ".category"),
                    Zone = ParseEnum<BodyZone>(JsonRead.Str(o, "zone", where), where + ".zone"),
                    Heat = JsonRead.Int(o, "heat", where, 0),
                    RollBonus = JsonRead.Int(o, "rollBonus", where, 0),
                    Kind = ParseEnum<MoveKind>(JsonRead.Str(o, "kind", where), where + ".kind"),
                    GimmickId = JsonRead.Str(o, "gimmick", where, false),
                    Cost = JsonRead.Int(o, "cost", where, 0)
                };
                string staging = JsonRead.Str(o, "staging", where, false);
                m.Staging = staging != null ? ParseEnum<MoveStaging>(staging, where + ".staging")
                          : m.Action == MatchActionId.TopRope ? MoveStaging.Corner : MoveStaging.Center;
                if (c._byId.ContainsKey(id)) throw new FormatException("повторний прийом " + id);
                c._byId[id] = m;
                c.All.Add(m);
            }
            return c;
        }

        internal static T ParseEnum<T>(string s, string where) where T : struct
        {
            T v;
            if (!Enum.TryParse<T>(s, true, out v) || !Enum.IsDefined(typeof(T), v)) throw new FormatException(where + ": невідоме значення '" + s + "'");
            return v;
        }
    }

    public sealed class GimmickDef
    {
        public Gimmick Id;
        public string Key;
        public string NameKey;
        public RingAttr FinisherAttr;
        /// <summary>Основа атрибутів за RingAttr (може бути від'ємною: слабкість ґіміка).</summary>
        public int[] Attr = new int[5];
        public string[] GimmickMoves = new string[0];
        public string SignatureMove;
        public string FinisherMove;
    }

    /// <summary>NPC-борець федерації (career_roster.json): місцевої — у корені, вищих — у «federations».</summary>
    public sealed class NpcDef
    {
        public string Id;
        public string NameKey;
        public string BioKey;
        public Gimmick Gimmick;
        public CareerRole Role;
        /// <summary>Сила в рейтингу федерації: 1 (відкриває картку) … 6 (чемпіон).</summary>
        public int Rank;
        public int StartMomentum;
        public WrestlerStats Stats;
        /// <summary>Жіночий дивізіон (P2-6): суперник за замовчуванням — своєї статі; мішаний матч — подія.</summary>
        public bool Female;
        /// <summary>Федерація NPC: 1 — місцева … 4 — світова.</summary>
        public int Tier = 1;

        public RingPath Path { get { return Role == CareerRole.Hero ? RingPath.Face : RingPath.Heel; } }
    }

    /// <summary>
    /// Федерація (TRPG_DESIGN §6.4, П5; власник 11.11.2026 погодив таблиці як ПЛЕЙСХОЛДЕР): рівень, букер, стеля й підлога
    /// популярності, ажіотаж для +1 популярності, довіра нового букера, економіка рівня (гонорар у відсотках, проживання, дорога,
    /// костюм рівня, аванс контракту), два пояси (чоловічий і жіночий дивізіони) і ростер. Числа — у career_roster.json.
    /// </summary>
    public sealed class FederationDef
    {
        public int Tier;
        public string NameKey;
        /// <summary>Мовець букера в сценах закулісся (speaker.*).</summary>
        public string Booker;
        public int Ceiling;
        public int Floor;
        public int PopularityHype;
        public int StartTrust;
        public int FeePercent;
        public int LivingCost;
        public int TravelCost;
        public int CostumeCost;
        public int Advance;
        public string BeltMale;
        public string BeltFemale;
        public readonly List<NpcDef> Roster = new List<NpcDef>();

        public string BeltOf(bool female) { return female ? BeltFemale : BeltMale; }
    }

    /// <summary>Усе кар'єрне вмістиме: ворожнеча-спина, прийоми, ґіміки, ростер. Завантажується з JSON; стан не тримає.</summary>
    public sealed class CareerContent
    {
        public const string HammerId = "hammer";

        public FeudScript Feud;
        public MoveCatalog Moves;
        public readonly List<GimmickDef> Gimmicks = new List<GimmickDef>();
        /// <summary>Усі NPC усіх федерацій (пошук за id). Ростер федерації — <see cref="FederationDef.Roster"/>.</summary>
        public readonly List<NpcDef> Roster = new List<NpcDef>();
        /// <summary>Чоловічий пояс місцевої федерації на старті.</summary>
        public string BeltHolderId;
        /// <summary>Федерації за рівнем: [0] — місцева … [3] — світова.</summary>
        public readonly List<FederationDef> Federations = new List<FederationDef>();
        public const int MaxTier = 4;

        public FederationDef Federation(int tier)
        {
            int i = Math.Max(1, Math.Min(Federations.Count, tier)) - 1;
            return Federations[i];
        }
        /// <summary>Закулісні сцени (career_backstage.json). null — файлу не передали: «Закулісся» лишається кидком Харизми.</summary>
        public BackstageContent Backstage;

        /// <summary>Мови сцен, крім ростера: букер, гравець, оповідач, публіка, ведуча місцевого каналу, агент чужої федерації.</summary>
        public static readonly string[] ExtraSpeakers = { "booker", "player", "narrator", "crowd", "host", "scout", "booker2", "booker3", "booker4", "manager" };

        public static CareerContent Load(string feudJson, string movesJson, string gimmicksJson, string rosterJson, string backstageJson = null)
        {
            var c = new CareerContent { Feud = FeudScriptLoader.Load(feudJson), Moves = MoveCatalog.Load(movesJson) };
            var g = MiniJson.ParseObject(gimmicksJson);
            foreach (var o in JsonRead.ObjList(g, "gimmicks", "gimmicks"))
            {
                string id = JsonRead.Str(o, "id", "gimmick");
                string where = "gimmick " + id;
                var d = new GimmickDef
                {
                    Id = MoveCatalog.ParseEnum<Gimmick>(id, where),
                    Key = id,
                    NameKey = JsonRead.Str(o, "name", where),
                    SignatureMove = JsonRead.Str(o, "signature", where),
                    FinisherMove = JsonRead.Str(o, "finisherMove", where)
                };
                RingAttr fa;
                if (!FeudVars.TryAttr(JsonRead.Str(o, "finisher", where), out fa)) throw new FormatException(where + ".finisher: невідомий атрибут");
                d.FinisherAttr = fa;
                ReadAttr(JsonRead.Obj(o, "attr", where), d.Attr, where + ".attr");
                var list = o.ContainsKey("moves") ? o["moves"] as List<object> : null;
                if (list == null) throw new FormatException(where + ".moves: очікувався масив");
                d.GimmickMoves = new string[list.Count];
                for (int i = 0; i < list.Count; i++) d.GimmickMoves[i] = (string)list[i];
                c.Gimmicks.Add(d);
            }
            var r = MiniJson.ParseObject(rosterJson);
            c.BeltHolderId = JsonRead.Str(r, "beltHolder", "roster");
            var local = new FederationDef
            {
                Tier = 1, NameKey = "fed.1.name", Booker = "booker", Ceiling = CareerRules.MaxPopularity, Floor = 0, PopularityHype = 7,
                StartTrust = 1, FeePercent = 100, LivingCost = CareerRules.LivingCost, TravelCost = 0, CostumeCost = CareerRules.CostumeCost,
                BeltMale = c.BeltHolderId, BeltFemale = JsonRead.Str(r, "beltFemale", "roster", false)
            };
            foreach (var o in JsonRead.ObjList(r, "npcs", "roster")) local.Roster.Add(ReadNpc(o, 1));
            // «federations» додано 11.11.2026 (П5): без нього — одна місцева федерація з числами CareerRules
            if (JsonRead.Has(r, "federations"))
            {
                foreach (var o in JsonRead.ObjList(r, "federations", "roster"))
                {
                    int tier = JsonRead.Int(o, "tier", "federation");
                    string where = "federation " + tier;
                    var f = tier == 1 ? local : new FederationDef { Tier = tier };
                    f.NameKey = JsonRead.Str(o, "name", where);
                    f.Booker = JsonRead.Str(o, "booker", where);
                    f.Ceiling = JsonRead.Int(o, "ceiling", where);
                    f.Floor = JsonRead.Int(o, "floor", where);
                    f.PopularityHype = JsonRead.Int(o, "popHype", where);
                    f.StartTrust = JsonRead.Int(o, "startTrust", where);
                    f.FeePercent = JsonRead.Int(o, "feePercent", where);
                    f.LivingCost = JsonRead.Int(o, "living", where);
                    f.TravelCost = JsonRead.Int(o, "travel", where);
                    f.CostumeCost = JsonRead.Int(o, "costume", where);
                    f.Advance = JsonRead.Int(o, "advance", where, 0);
                    if (tier != 1)
                    {
                        f.BeltMale = JsonRead.Str(o, "beltHolder", where);
                        f.BeltFemale = JsonRead.Str(o, "beltFemale", where, false);
                        foreach (var no in JsonRead.ObjList(o, "npcs", where)) f.Roster.Add(ReadNpc(no, tier));
                    }
                    if (c.Federations.Count != tier - 1) throw new FormatException(where + ": рівні федерацій мають іти 1, 2, 3…");
                    c.Federations.Add(f);
                }
            }
            else c.Federations.Add(local);
            if (c.Federations.Count == 0 || c.Federations[0] != local) throw new FormatException("федерації: першою має бути місцева (tier 1)");
            foreach (var f in c.Federations) c.Roster.AddRange(f.Roster);
            if (!string.IsNullOrEmpty(backstageJson)) c.Backstage = BackstageContent.Load(backstageJson);
            c.Validate();
            return c;
        }

        private static NpcDef ReadNpc(Dictionary<string, object> o, int tier)
        {
            string id = JsonRead.Str(o, "id", "npc");
            string where = "npc " + id;
            var n = new NpcDef
            {
                Id = id,
                NameKey = JsonRead.Str(o, "name", where),
                BioKey = JsonRead.Str(o, "bio", where),
                Gimmick = MoveCatalog.ParseEnum<Gimmick>(JsonRead.Str(o, "gimmick", where), where + ".gimmick"),
                Role = MoveCatalog.ParseEnum<CareerRole>(JsonRead.Str(o, "role", where), where + ".role"),
                Rank = JsonRead.Int(o, "rank", where),
                StartMomentum = JsonRead.Int(o, "startMomentum", where, 0),
                Female = JsonRead.Bool(o, "female", where, false),
                Tier = tier
            };
            var w = new WrestlerStats(id, n.NameKey);
            RingAttr fa;
            if (!FeudVars.TryAttr(JsonRead.Str(o, "finisher", where), out fa)) throw new FormatException(where + ".finisher: невідомий атрибут");
            w.FinisherAttr = fa;
            var at = new int[5];
            ReadAttr(JsonRead.Obj(o, "attr", where), at, where + ".attr");
            for (int i = 0; i < 5; i++) w.Set((RingAttr)i, at[i]);
            n.Stats = w;
            return n;
        }

        private static void ReadAttr(Dictionary<string, object> o, int[] into, string where)
        {
            foreach (var kv in o)
            {
                RingAttr a;
                if (!FeudVars.TryAttr(kv.Key, out a)) throw new FormatException(where + ": невідомий атрибут '" + kv.Key + "'");
                if (!(kv.Value is double)) throw new FormatException(where + "." + kv.Key + ": очікувалось число");
                into[(int)a] = (int)(double)kv.Value;
            }
        }

        /// <summary>Перехресні посилання даних: прийоми ґіміків існують, ростер має Молота з атрибутами сценарію ворожнечі, чемпіон — з ростера.</summary>
        private void Validate()
        {
            foreach (var g in Gimmicks)
            {
                var ids = new List<string>(g.GimmickMoves) { g.SignatureMove, g.FinisherMove };
                foreach (var m in ids)
                    if (Moves.Find(m) == null) throw new FormatException("ґімік " + g.Key + ": немає прийому " + m);
            }
            if (Gimmicks.Count == 0 || Roster.Count == 0) throw new FormatException("кар'єра: порожні ґіміки чи ростер");
            if (FindNpc(BeltHolderId) == null) throw new FormatException("чемпіон поза ростером: " + BeltHolderId);
            var npcIds = new HashSet<string>();
            foreach (var n in Roster) if (!npcIds.Add(n.Id)) throw new FormatException("NPC повторюється: " + n.Id);
            int prevCeiling = -1;
            foreach (var f in Federations)
            {
                string where = "федерація " + f.Tier;
                if (f.Roster.Count == 0) throw new FormatException(where + ": порожній ростер");
                if (f.Floor < 0 || f.Floor >= f.Ceiling || f.Ceiling > CareerRules.MaxPopularity || f.Ceiling < prevCeiling)
                    throw new FormatException(where + ": стеля й підлога популярності мають рости з рівнем у межах 0–" + CareerRules.MaxPopularity);
                prevCeiling = f.Ceiling;
                if (f.FeePercent <= 0 || f.LivingCost < 0 || f.TravelCost < 0 || f.CostumeCost <= 0) throw new FormatException(where + ": економіка");
                CheckBelt(f, f.BeltMale, false, where);
                if (f.BeltFemale != null) CheckBelt(f, f.BeltFemale, true, where);
                else foreach (var n in f.Roster) if (n.Female) throw new FormatException(where + ": є жіночий дивізіон, а пояса для нього немає");
                if (Array.IndexOf(ExtraSpeakers, f.Booker) < 0) throw new FormatException(where + ": букер '" + f.Booker + "' — не мовець сцен");
            }
            var hammer = FindNpc(HammerId);
            if (hammer == null) throw new FormatException("у ростері немає Молота — суперника ворожнечі");
            if (hammer.Tier != 1) throw new FormatException("Молот — борець місцевої федерації");
            for (int i = 0; i < 5; i++)
                if (hammer.Stats.Get((RingAttr)i) != Feud.Rival.Get((RingAttr)i))
                    throw new FormatException("атрибути Молота в ростері не збігаються зі сценарієм ворожнечі");
            if (Backstage != null) ValidateBackstage();
        }

        private static void CheckBelt(FederationDef f, string id, bool female, string where)
        {
            NpcDef holder = null;
            foreach (var n in f.Roster) if (n.Id == id) holder = n;
            if (holder == null) throw new FormatException(where + ": чемпіон поза ростером федерації: " + id);
            if (holder.Female != female) throw new FormatException(where + ": пояс " + (female ? "жіночого" : "чоловічого") + " дивізіону в борця іншого: " + id);
            if (id == HammerId) throw new FormatException(where + ": Молот — суперник ворожнечі, а не чемпіон");
        }

        /// <summary>
        /// Закулісні сцени посилаються лише на наявних NPC й допустимих мовців; ефекти не чіпають план і умови матчу ворожнечі.
        /// Умова booker — лише на букера якоїсь федерації; у розмові свого букера (умова сцени booker) букери інших федерацій не говорять.
        /// </summary>
        private void ValidateBackstage()
        {
            foreach (var sc in Backstage.Scenes)
            {
                string where = "закулісна сцена " + sc.Id;
                var effects = new List<FeudEffect>();
                var conds = new List<FeudCondition>(sc.Requires);
                string own = SceneBooker(sc);
                var guards = sc.Kind == BackstageKind.Event ? BranchBookerGuards(sc.Promo) : null;
                foreach (var n in sc.Promo.Nodes)
                {
                    // подія буває в будь-якій федерації: букер конкретної федерації говорить лише там, де умова (сцени чи гілки) — його федерація
                    string here;
                    if (guards != null && n.Type == PromoNodeType.Line && IsFederationBooker(n.Speaker)
                        && (guards.TryGetValue(n.Id, out here) ? here : own) != n.Speaker)
                        throw new FormatException(where + "/" + n.Id + ": репліка букера '" + n.Speaker + "' без умови його федерації (booker, tier чи гілка за букером)");
                    if (n.Type == PromoNodeType.Line && n.Speaker != null && FindNpc(n.Speaker) == null
                        && Array.IndexOf(ExtraSpeakers, n.Speaker) < 0)
                        throw new FormatException(where + "/" + n.Id + ": невідомий мовець '" + n.Speaker + "'");
                    if (n.Type == PromoNodeType.Line && own != null && n.Speaker != own && IsFederationBooker(n.Speaker))
                        throw new FormatException(where + "/" + n.Id + ": розмова букера '" + own + "' — репліка чужого букера '" + n.Speaker + "'");
                    foreach (var o in n.Options) { effects.AddRange(o.Effects); conds.AddRange(o.Requires); }
                    foreach (var r in n.Rewards) effects.AddRange(r.Effects);
                    effects.AddRange(n.Fail);
                    foreach (var cs in n.Cases) conds.AddRange(cs.Requires);
                }
                foreach (var e in effects)
                {
                    if (e.Kind == EffectKind.Plan || e.Kind == EffectKind.Stip || e.Kind == EffectKind.Ally)
                        throw new FormatException(where + ": ефекти plan/stip/ally належать ворожнечі; у сценах — career:plan");
                    if (e.Kind == EffectKind.Career && e.Key == "npc_feud" && e.Target != "opponent" && e.Target != "last_opponent" && FindNpc(e.Target) == null)
                        throw new FormatException(where + ": ворожнеча з невідомим NPC '" + e.Target + "'");
                    if (e.Kind == EffectKind.Career && e.Key == "opponent" && (FindNpc(e.Target) == null || e.Target == HammerId))
                        throw new FormatException(where + ": ефект opponent — NPC ростеру (не Молот: у нього своя ворожнеча), а не '" + e.Target + "'");
                }
                foreach (var cd in conds)
                {
                    if (cd.Kind == ConditionKind.Career && cd.Key == "npc_feud" && FindNpc(cd.Target) == null)
                        throw new FormatException(where + ": умова ворожнечі з невідомим NPC '" + cd.Target + "'");
                    if (cd.Kind == ConditionKind.Career && cd.Key == "booker" && !IsFederationBooker(cd.Target))
                        throw new FormatException(where + ": умова booker — '" + cd.Target + "' не букер жодної федерації");
                    if (cd.Kind == ConditionKind.Career && cd.Key == "seen_before" && Backstage.Find(cd.Target) == null)
                        throw new FormatException(where + ": умова seen_before — немає сцени '" + cd.Target + "'");
                }
            }
        }

        /// <summary>
        /// Букер федерації, де сцена буває: з умови booker; інакше — якщо умови tier лишають одну федерацію, її букер; інакше null.
        /// </summary>
        public string SceneBooker(BackstageScene sc)
        {
            int lo = 1, hi = Federations.Count;
            foreach (var cd in sc.Requires)
            {
                if (cd.Kind != ConditionKind.Career) continue;
                if (cd.Key == "booker" && !cd.Negate) return cd.Target;
                if (cd.Key == "tier") { if (cd.Negate) hi = Math.Min(hi, cd.Min - 1); else lo = Math.Max(lo, cd.Min); }
            }
            return lo == hi ? Federation(lo).Booker : null;
        }

        /// <summary>
        /// Вузли, до яких ведуть лише випадки гілок з умовою «букер федерації = X» (усі — з тим самим X): вузол → X. Старт, перехід
        /// рядка, варіант вибору, кидок і запасна гілка (next) — переходи без букера.
        /// </summary>
        internal static Dictionary<string, string> BranchBookerGuards(PromoDef p)
        {
            var into = new Dictionary<string, List<string>>();
            Action<string, string> edge = (to, guard) =>
            {
                if (to == null) return;
                List<string> l;
                if (!into.TryGetValue(to, out l)) into[to] = l = new List<string>();
                l.Add(guard);
            };
            edge(p.Start, null);
            foreach (var n in p.Nodes)
            {
                if (n.Type != PromoNodeType.Branch) { foreach (var t in n.Targets()) edge(t, null); continue; }
                foreach (var cs in n.Cases)
                {
                    string g = null;
                    foreach (var cd in cs.Requires) if (cd.Kind == ConditionKind.Career && cd.Key == "booker" && !cd.Negate) { g = cd.Target; break; }
                    edge(cs.Next, g);
                }
                edge(n.Next, null);
            }
            var guards = new Dictionary<string, string>();
            foreach (var kv in into)
            {
                bool same = kv.Value[0] != null;
                foreach (var x in kv.Value) if (x != kv.Value[0]) same = false;
                if (same) guards[kv.Key] = kv.Value[0];
            }
            return guards;
        }

        /// <summary>Мовець — букер якоїсь федерації.</summary>
        public bool IsFederationBooker(string speaker)
        {
            foreach (var f in Federations) if (f.Booker == speaker) return true;
            return false;
        }

        public NpcDef FindNpc(string id)
        {
            for (int i = 0; i < Roster.Count; i++) if (Roster[i].Id == id) return Roster[i];
            return null;
        }

        public GimmickDef GimmickOf(Gimmick g)
        {
            for (int i = 0; i < Gimmicks.Count; i++) if (Gimmicks[i].Id == g) return Gimmicks[i];
            throw new ArgumentException("немає ґіміка " + g);
        }

        /// <summary>
        /// Фінішер ґіміка для показу (<see cref="MatchSetup.RivalFinisher"/>): ключ прийому й постановка, без бонусу до кидка й
        /// ажіотажу — ШІ суперника й загальна кнопка грають фінішер своїм кліпом, а правила матчу ті самі.
        /// </summary>
        public MoveHint FinisherShow(Gimmick g)
        {
            var m = Moves.Get(GimmickOf(g).FinisherMove);
            return new MoveHint { Key = m.Id, Staging = m.Staging };
        }
    }
}
