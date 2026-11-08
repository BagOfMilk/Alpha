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

    /// <summary>NPC-борець місцевої федерації (career_roster.json).</summary>
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

        public RingPath Path { get { return Role == CareerRole.Hero ? RingPath.Face : RingPath.Heel; } }
    }

    /// <summary>Усе кар'єрне вмістиме: ворожнеча-спина, прийоми, ґіміки, ростер. Завантажується з JSON; стан не тримає.</summary>
    public sealed class CareerContent
    {
        public const string HammerId = "hammer";

        public FeudScript Feud;
        public MoveCatalog Moves;
        public readonly List<GimmickDef> Gimmicks = new List<GimmickDef>();
        public readonly List<NpcDef> Roster = new List<NpcDef>();
        public string BeltHolderId;
        /// <summary>Закулісні сцени (career_backstage.json). null — файлу не передали: «Закулісся» лишається кидком Харизми.</summary>
        public BackstageContent Backstage;

        /// <summary>Мови сцен, крім ростера: букер, гравець, оповідач, публіка, ведуча місцевого каналу, агент чужої федерації.</summary>
        public static readonly string[] ExtraSpeakers = { "booker", "player", "narrator", "crowd", "host", "scout" };

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
            foreach (var o in JsonRead.ObjList(r, "npcs", "roster"))
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
                    StartMomentum = JsonRead.Int(o, "startMomentum", where, 0)
                };
                var w = new WrestlerStats(id, n.NameKey);
                RingAttr fa;
                if (!FeudVars.TryAttr(JsonRead.Str(o, "finisher", where), out fa)) throw new FormatException(where + ".finisher: невідомий атрибут");
                w.FinisherAttr = fa;
                var at = new int[5];
                ReadAttr(JsonRead.Obj(o, "attr", where), at, where + ".attr");
                for (int i = 0; i < 5; i++) w.Set((RingAttr)i, at[i]);
                n.Stats = w;
                c.Roster.Add(n);
            }
            if (!string.IsNullOrEmpty(backstageJson)) c.Backstage = BackstageContent.Load(backstageJson);
            c.Validate();
            return c;
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
            var hammer = FindNpc(HammerId);
            if (hammer == null) throw new FormatException("у ростері немає Молота — суперника ворожнечі");
            for (int i = 0; i < 5; i++)
                if (hammer.Stats.Get((RingAttr)i) != Feud.Rival.Get((RingAttr)i))
                    throw new FormatException("атрибути Молота в ростері не збігаються зі сценарієм ворожнечі");
            if (Backstage != null) ValidateBackstage();
        }

        /// <summary>Закулісні сцени посилаються лише на наявних NPC й допустимих мовців; ефекти не чіпають план і умови матчу ворожнечі.</summary>
        private void ValidateBackstage()
        {
            foreach (var sc in Backstage.Scenes)
            {
                string where = "закулісна сцена " + sc.Id;
                var effects = new List<FeudEffect>();
                var conds = new List<FeudCondition>(sc.Requires);
                foreach (var n in sc.Promo.Nodes)
                {
                    if (n.Type == PromoNodeType.Line && n.Speaker != null && FindNpc(n.Speaker) == null
                        && Array.IndexOf(ExtraSpeakers, n.Speaker) < 0)
                        throw new FormatException(where + "/" + n.Id + ": невідомий мовець '" + n.Speaker + "'");
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
                }
                foreach (var cd in conds)
                    if (cd.Kind == ConditionKind.Career && cd.Key == "npc_feud" && FindNpc(cd.Target) == null)
                        throw new FormatException(where + ": умова ворожнечі з невідомим NPC '" + cd.Target + "'");
            }
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
    }
}
