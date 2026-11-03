using System;
using System.Collections.Generic;

namespace Game.Core.Ring
{
    /// <summary>Завантажувач ворожнечі з JSON (схема — gdd §4.3). Помилка схеми — FormatException, помилка JSON — MiniJsonException.</summary>
    public static class FeudScriptLoader
    {
        public static FeudScript Load(string json)
        {
            var root = MiniJson.ParseObject(json);
            var s = new FeudScript
            {
                Id = JsonRead.Str(root, "id", "feud"),
                TitleKey = JsonRead.Str(root, "title", "feud", false)
            };
            s.Player = ReadWrestler(JsonRead.Obj(root, "player", "feud"), "player");
            var rv = JsonRead.Obj(root, "rival", "feud");
            s.Rival = ReadWrestler(rv, "rival");
            s.RivalStartMomentum = JsonRead.Int(rv, "startMomentum", "rival", 0);

            var st = JsonRead.Obj(root, "start", "feud", false);
            if (st != null)
            {
                s.Start.Audience = JsonRead.Int(st, "audience", "start", s.Start.Audience);
                s.Start.Heat = JsonRead.Int(st, "heat", "start", s.Start.Heat);
                s.Start.Momentum = JsonRead.Int(st, "momentum", "start", s.Start.Momentum);
                s.Start.Trust = JsonRead.Int(st, "trust", "start", s.Start.Trust);
                s.Start.Locker = JsonRead.Int(st, "locker", "start", s.Start.Locker);
            }

            foreach (var po in JsonRead.ObjList(root, "promos", "feud"))
                s.Promos.Add(ReadPromo(po));
            foreach (var mo in JsonRead.ObjList(root, "matches", "feud"))
                s.Matches.Add(ReadMatch(mo));
            ReadFlow(root, s);
            return s;
        }

        private static MatchDef ReadMatch(Dictionary<string, object> o)
        {
            string id = JsonRead.Str(o, "id", "match");
            string where = "match " + id;
            var m = new MatchDef { Id = id, Act = JsonRead.Int(o, "act", where, 0), Decisive = JsonRead.Bool(o, "decisive", where, false) };
            foreach (var po in JsonRead.ObjList(o, "plan", where))
            {
                var rule = new PlanRule();
                string pn = JsonRead.Str(po, "plan", where + ".plan");
                if (!FeudVars.TryPlan(pn, out rule.Plan)) throw new FormatException(where + ".plan: невідомий план '" + pn + "'");
                foreach (var co in JsonRead.ObjList(po, "requires", where + ".plan")) rule.Requires.Add(FeudCondition.FromJson(co, where + ".plan.requires"));
                m.Plans.Add(rule);
            }
            object sv;
            if (o.TryGetValue("stipulations", out sv) && sv != null)
            {
                var list = sv as List<object>;
                if (list == null) throw new FormatException(where + ".stipulations: очікувався масив");
                foreach (var x in list)
                {
                    string k = x as string;
                    if (k == "no_dq") m.Stipulations |= MatchStipulations.NoDq;
                    else if (k == "no_ally") m.Stipulations |= MatchStipulations.NoAlly;
                    else throw new FormatException(where + ".stipulations: невідома умова '" + k + "'");
                }
            }
            return m;
        }

        /// <summary>
        /// Порядок кроків: "flow": ["p1_challenge", "m1", …] — кожен елемент або id промо, або id матчу. Без flow — старий порядок:
        /// усі промо підряд і один матч-розв'язка (якщо matches не задано, створюється m1).
        /// </summary>
        private static void ReadFlow(Dictionary<string, object> root, FeudScript s)
        {
            object fv;
            if (root.TryGetValue("flow", out fv) && fv != null)
            {
                var list = fv as List<object>;
                if (list == null) throw new FormatException("feud.flow: очікувався масив");
                foreach (var x in list)
                {
                    string id = x as string;
                    if (id == null) throw new FormatException("feud.flow: очікувались рядки");
                    int pi = s.Promos.FindIndex(p => p.Id == id);
                    int mi = s.Matches.FindIndex(m => m.Id == id);
                    if (pi >= 0 && mi >= 0) throw new FormatException("feud.flow: id '" + id + "' і промо, і матч");
                    if (pi < 0 && mi < 0) throw new FormatException("feud.flow: немає ні промо, ні матчу '" + id + "'");
                    s.Flow.Add(new FlowStep { Id = id, IsMatch = mi >= 0, Index = mi >= 0 ? mi : pi });
                }
                return;
            }
            if (s.Matches.Count == 0) s.Matches.Add(new MatchDef { Id = "m1", Decisive = true });
            else if (s.Matches.Count > 1) throw new FormatException("feud: кілька матчів потребують поля flow");
            for (int i = 0; i < s.Promos.Count; i++) s.Flow.Add(new FlowStep { Id = s.Promos[i].Id, Index = i });
            s.Matches[0].Decisive = true;
            s.Flow.Add(new FlowStep { Id = s.Matches[0].Id, IsMatch = true, Index = 0 });
        }

        private static WrestlerStats ReadWrestler(Dictionary<string, object> o, string where)
        {
            var w = new WrestlerStats(JsonRead.Str(o, "id", where), JsonRead.Str(o, "name", where));
            string fin = JsonRead.Str(o, "finisher", where, false);
            if (fin != null)
            {
                RingAttr fa;
                if (!FeudVars.TryAttr(fin, out fa)) throw new FormatException(where + ".finisher: невідомий атрибут '" + fin + "'");
                w.FinisherAttr = fa;
            }
            var attr = JsonRead.Obj(o, "attr", where, false);
            if (attr != null)
            {
                foreach (var kv in attr)
                {
                    RingAttr a;
                    if (!FeudVars.TryAttr(kv.Key, out a)) throw new FormatException(where + ".attr: невідомий атрибут '" + kv.Key + "'");
                    if (!(kv.Value is double)) throw new FormatException(where + ".attr." + kv.Key + ": очікувалось число");
                    w.Set(a, (int)(double)kv.Value);
                }
            }
            return w;
        }

        private static PromoDef ReadPromo(Dictionary<string, object> o)
        {
            string id = JsonRead.Str(o, "id", "promo");
            string where = "promo " + id;
            var p = new PromoDef
            {
                Id = id,
                Act = JsonRead.Int(o, "act", where, 0),
                TitleKey = JsonRead.Str(o, "title", where, false),
                PlaceKey = JsonRead.Str(o, "place", where, false),
                Start = JsonRead.Str(o, "start", where)
            };
            foreach (var no in JsonRead.ObjList(o, "nodes", where)) p.Nodes.Add(ReadNode(no, where));
            return p;
        }

        private static PromoNode ReadNode(Dictionary<string, object> o, string promoWhere)
        {
            string id = JsonRead.Str(o, "id", promoWhere);
            string where = promoWhere + "/" + id;
            string type = JsonRead.Str(o, "type", where);
            var n = new PromoNode { Id = id };
            switch (type)
            {
                case "line":
                    n.Type = PromoNodeType.Line;
                    n.Speaker = JsonRead.Str(o, "speaker", where);
                    n.TextKey = JsonRead.Str(o, "text", where);
                    n.Next = JsonRead.Str(o, "next", where);
                    break;
                case "choice":
                    n.Type = PromoNodeType.Choice;
                    foreach (var oo in JsonRead.ObjList(o, "options", where)) n.Options.Add(ReadOption(oo, where));
                    break;
                case "check":
                    n.Type = PromoNodeType.Check;
                    string an = JsonRead.Str(o, "attr", where);
                    RingAttr a;
                    if (!FeudVars.TryAttr(an, out a)) throw new FormatException(where + ".attr: невідомий атрибут '" + an + "'");
                    n.Attr = a;
                    n.Mod = JsonRead.Int(o, "mod", where, 0);
                    n.Pick10 = JsonRead.Int(o, "pick10", where, 0);
                    n.Pick7 = JsonRead.Int(o, "pick7", where, 0);
                    foreach (var ro in JsonRead.ObjList(o, "rewards", where))
                    {
                        var r = new PromoReward { Id = JsonRead.Str(ro, "id", where), TextKey = JsonRead.Str(ro, "text", where) };
                        r.Effects = ReadEffects(ro, "effects", where + "/reward " + r.Id);
                        n.Rewards.Add(r);
                    }
                    n.Fail = ReadEffects(o, "fail", where);
                    n.Next = JsonRead.Str(o, "next", where);
                    n.Next6 = JsonRead.Str(o, "next6", where, false);
                    break;
                case "branch":
                    n.Type = PromoNodeType.Branch;
                    n.Next = JsonRead.Str(o, "next", where);
                    foreach (var co in JsonRead.ObjList(o, "cases", where))
                    {
                        var bc = new PromoBranchCase { Next = JsonRead.Str(co, "next", where + ".cases") };
                        foreach (var ro in JsonRead.ObjList(co, "requires", where + ".cases")) bc.Requires.Add(FeudCondition.FromJson(ro, where + ".cases.requires"));
                        n.Cases.Add(bc);
                    }
                    break;
                case "end":
                    n.Type = PromoNodeType.End;
                    break;
                default:
                    throw new FormatException(where + ": невідомий тип вузла '" + type + "'");
            }
            return n;
        }

        private static PromoOption ReadOption(Dictionary<string, object> o, string nodeWhere)
        {
            string id = JsonRead.Str(o, "id", nodeWhere);
            string where = nodeWhere + "/option " + id;
            var opt = new PromoOption
            {
                Id = id,
                TextKey = JsonRead.Str(o, "text", where),
                Next = JsonRead.Str(o, "next", where),
                Hidden = JsonRead.Bool(o, "hidden", where, false)
            };
            string path = JsonRead.Str(o, "path", where, false);
            if (path != null)
            {
                RingPath rp;
                if (!FeudVars.TryPath(path, out rp)) throw new FormatException(where + ".path: невідомий шлях '" + path + "'");
                opt.Path = rp;
            }
            foreach (var co in JsonRead.ObjList(o, "requires", where)) opt.Requires.Add(FeudCondition.FromJson(co, where + ".requires"));
            opt.Effects = ReadEffects(o, "effects", where);
            return opt;
        }

        private static List<FeudEffect> ReadEffects(Dictionary<string, object> o, string key, string where)
        {
            var list = new List<FeudEffect>();
            foreach (var eo in JsonRead.ObjList(o, key, where)) list.Add(FeudEffect.FromJson(eo, where + "." + key));
            return list;
        }
    }
}
