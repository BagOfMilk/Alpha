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
            return s;
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
