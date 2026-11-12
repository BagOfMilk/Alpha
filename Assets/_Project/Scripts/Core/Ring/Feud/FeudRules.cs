using System;
using System.Collections.Generic;

namespace Game.Core.Ring
{
    /// <summary>Змінні стану ворожнечі (gdd §3) і їхні межі.</summary>
    public static class FeudVars
    {
        public const string Audience = "audience";
        public const string Heat = "heat";
        public const string Momentum = "momentum";
        public const string Trust = "trust";
        public const string Locker = "locker";

        public static readonly string[] All = { Audience, Heat, Momentum, Trust, Locker };

        public static bool IsKnown(string name) => Array.IndexOf(All, name) >= 0;

        public static int Min(string name) => name == Locker ? -2 : 0;

        public static int Max(string name)
        {
            switch (name)
            {
                case Audience: return 10;
                case Heat: return 4;
                case Momentum: return 5;
                case Trust: return 5;
                case Locker: return 2;
                default: throw new ArgumentException("невідома змінна ворожнечі: " + name);
            }
        }

        public static int Clamp(string name, int v) => Math.Max(Min(name), Math.Min(Max(name), v));

        public static readonly string[] AttrNames = { "strength", "agility", "technique", "stamina", "charisma" };
        public static readonly string[] PathNames = { "face", "heel", "rebel" };

        public static bool TryAttr(string name, out RingAttr a)
        {
            int i = Array.IndexOf(AttrNames, name);
            a = (RingAttr)(i < 0 ? 0 : i);
            return i >= 0;
        }

        public static bool TryPath(string name, out RingPath p)
        {
            int i = Array.IndexOf(PathNames, name);
            p = (RingPath)(i < 0 ? 0 : i);
            return i >= 0;
        }

        public static readonly string[] PlanNames = { "none", "win_clean", "lose_clean", "win_any", "no_clean_loss" };

        public static bool TryPlan(string name, out BookerPlan p)
        {
            int i = Array.IndexOf(PlanNames, name);
            p = (BookerPlan)(i < 0 ? 0 : i);
            return i >= 0;
        }

        /// <summary>Прапор результату матчу: m1_won, m1_lost, m1_draw, m1_dirty, m1_plan_ok, m1_plan_swerved.</summary>
        public static string MatchFlag(string matchId, string suffix) => matchId + "_" + suffix;

        public static string AttrName(RingAttr a) => AttrNames[(int)a];
        public static string PathName(RingPath p) => PathNames[(int)p];
        /// <summary>Прапор закритої гілки (ефект close).</summary>
        public static string ClosedFlag(string branch) => "closed." + branch;
    }

    /// <summary>Імена умов і ефектів кар'єри (закулісні сцени): ті, що читає чи пише не FeudState, а CareerSession.</summary>
    public static class CareerFx
    {
        /// <summary>Умови: money(min), season(min), week(min), parity(min 0/1 — парність тижня), injured, champion, last_won,
        /// last_lost, last_draw, last_swerved, plan_set, title_eligible, actions(min), npc_feud(target, min), tier(min — рівень федерації),
        /// female (героїня — жіночий дивізіон, P2-6).</summary>
        public static readonly string[] Conditions =
        {
            "money", "season", "week", "parity", "injured", "champion", "last_won", "last_lost", "last_draw", "last_swerved",
            "plan_set", "title_eligible", "actions", "npc_feud", "tier", "female"
        };

        /// <summary>Ефекти: money(add), injure(target зона head/body/legs/random, add — ушкодження), npc_feud(target NPC | opponent |
        /// last_opponent, add), hype(add — ажіотаж старту наступного матчу), boost(add — імпульс суперника), plan(target — план
        /// наступного шоу), actions(add — дії тижня), dev(add — очки розвитку).</summary>
        public static readonly string[] Effects = { "money", "injure", "npc_feud", "hype", "boost", "plan", "actions", "dev" };

        public static bool IsCondition(string n) { return Array.IndexOf(Conditions, n) >= 0; }
        public static bool IsEffect(string n) { return Array.IndexOf(Effects, n) >= 0; }
    }

    public enum ConditionKind { Flag = 0, NotFlag = 1, Open = 2, Var = 3, Attr = 4, Path = 5, Career = 6 }

    /// <summary>Умова варіанта вибору (gdd §4.1 requires).</summary>
    public sealed class FeudCondition
    {
        public ConditionKind Kind;
        /// <summary>Прапор / гілка / змінна / атрибут / шлях — залежно від Kind.</summary>
        public string Key;
        /// <summary>Мінімум (для Var, Attr, Path, Career).</summary>
        public int Min;
        /// <summary>Ціль (для Career: id NPC в умові npc_feud).</summary>
        public string Target;
        /// <summary>Інверсія результату: «not»: true.</summary>
        public bool Negate;

        public static FeudCondition FromJson(Dictionary<string, object> o, string where)
        {
            var c = FromJsonCore(o, where);
            c.Negate = JsonRead.Bool(o, "not", where, false);
            return c;
        }

        private static FeudCondition FromJsonCore(Dictionary<string, object> o, string where)
        {
            var c = new FeudCondition();
            if (JsonRead.Has(o, "career"))
            {
                c.Kind = ConditionKind.Career; c.Key = JsonRead.Str(o, "career", where); c.Min = JsonRead.Int(o, "min", where, 0);
                c.Target = JsonRead.Str(o, "target", where, false);
                if (!CareerFx.IsCondition(c.Key)) throw new FormatException(where + ": невідома умова кар'єри '" + c.Key + "'");
                if (c.Key == "npc_feud" && c.Target == null) throw new FormatException(where + ": npc_feud потребує target");
            }
            else if (JsonRead.Has(o, "flag")) { c.Kind = ConditionKind.Flag; c.Key = JsonRead.Str(o, "flag", where); }
            else if (JsonRead.Has(o, "notFlag")) { c.Kind = ConditionKind.NotFlag; c.Key = JsonRead.Str(o, "notFlag", where); }
            else if (JsonRead.Has(o, "open")) { c.Kind = ConditionKind.Open; c.Key = JsonRead.Str(o, "open", where); }
            else if (JsonRead.Has(o, "var"))
            {
                c.Kind = ConditionKind.Var; c.Key = JsonRead.Str(o, "var", where); c.Min = JsonRead.Int(o, "min", where);
                if (!FeudVars.IsKnown(c.Key)) throw new FormatException(where + ": невідома змінна '" + c.Key + "'");
            }
            else if (JsonRead.Has(o, "attr"))
            {
                c.Kind = ConditionKind.Attr; c.Key = JsonRead.Str(o, "attr", where); c.Min = JsonRead.Int(o, "min", where);
                RingAttr a;
                if (!FeudVars.TryAttr(c.Key, out a)) throw new FormatException(where + ": невідомий атрибут '" + c.Key + "'");
            }
            else if (JsonRead.Has(o, "path"))
            {
                c.Kind = ConditionKind.Path; c.Key = JsonRead.Str(o, "path", where); c.Min = JsonRead.Int(o, "min", where);
                RingPath p;
                if (!FeudVars.TryPath(c.Key, out p)) throw new FormatException(where + ": невідомий шлях '" + c.Key + "'");
            }
            else throw new FormatException(where + ": невідома умова");
            return c;
        }

        /// <summary>Стисла причина для сірого варіанта (UI локалізує за Kind/Key/Min).</summary>
        public override string ToString()
        {
            return (Negate ? "not:" : "") + Describe();
        }

        private string Describe()
        {
            switch (Kind)
            {
                case ConditionKind.Flag: return "flag:" + Key;
                case ConditionKind.NotFlag: return "notFlag:" + Key;
                case ConditionKind.Open: return "open:" + Key;
                case ConditionKind.Var: return "var:" + Key + ">=" + Min;
                case ConditionKind.Attr: return "attr:" + Key + ">=" + Min;
                case ConditionKind.Career: return "career:" + Key + (Target == null ? "" : "@" + Target) + ">=" + Min;
                default: return "path:" + Key + ">=" + Min;
            }
        }
    }

    public enum EffectKind { Var = 0, Flag = 1, Close = 2, Path = 3, Stip = 4, Plan = 5, Ally = 6, Career = 7 }

    /// <summary>Ефект варіанта/нагороди/провалу (gdd §4.1).</summary>
    public sealed class FeudEffect
    {
        public EffectKind Kind;
        /// <summary>Змінна / прапор / гілка / шлях / умова матчу / план — залежно від Kind.</summary>
        public string Key;
        /// <summary>Приріст (Var, Path, Career).</summary>
        public int Amount;
        /// <summary>Ціль (для Career: зона, id NPC чи назва плану — залежно від ефекту).</summary>
        public string Target;
        /// <summary>Значення для Ally.</summary>
        public bool Value = true;

        public static FeudEffect FromJson(Dictionary<string, object> o, string where)
        {
            var e = new FeudEffect();
            if (JsonRead.Has(o, "career"))
            {
                e.Kind = EffectKind.Career; e.Key = JsonRead.Str(o, "career", where); e.Amount = JsonRead.Int(o, "add", where, 0);
                e.Target = JsonRead.Str(o, "target", where, false);
                if (!CareerFx.IsEffect(e.Key)) throw new FormatException(where + ": невідомий ефект кар'єри '" + e.Key + "'");
                if ((e.Key == "injure" || e.Key == "npc_feud" || e.Key == "plan") && e.Target == null)
                    throw new FormatException(where + ": ефект " + e.Key + " потребує target");
                if (e.Key == "injure" && Array.IndexOf(new[] { "head", "body", "legs", "random" }, e.Target) < 0)
                    throw new FormatException(where + ": невідома зона '" + e.Target + "'");
                BookerPlan cp;
                if (e.Key == "plan" && !FeudVars.TryPlan(e.Target, out cp)) throw new FormatException(where + ": невідомий план '" + e.Target + "'");
                if (e.Key != "plan" && e.Key != "npc_feud" && e.Amount == 0) throw new FormatException(where + ": ефект " + e.Key + " без add");
            }
            else if (JsonRead.Has(o, "var"))
            {
                e.Kind = EffectKind.Var; e.Key = JsonRead.Str(o, "var", where); e.Amount = JsonRead.Int(o, "add", where);
                if (!FeudVars.IsKnown(e.Key)) throw new FormatException(where + ": невідома змінна '" + e.Key + "'");
            }
            else if (JsonRead.Has(o, "flag")) { e.Kind = EffectKind.Flag; e.Key = JsonRead.Str(o, "flag", where); }
            else if (JsonRead.Has(o, "close")) { e.Kind = EffectKind.Close; e.Key = JsonRead.Str(o, "close", where); }
            else if (JsonRead.Has(o, "path"))
            {
                e.Kind = EffectKind.Path; e.Key = JsonRead.Str(o, "path", where); e.Amount = JsonRead.Int(o, "add", where);
                RingPath p;
                if (!FeudVars.TryPath(e.Key, out p)) throw new FormatException(where + ": невідомий шлях '" + e.Key + "'");
            }
            else if (JsonRead.Has(o, "stip"))
            {
                e.Kind = EffectKind.Stip; e.Key = JsonRead.Str(o, "stip", where);
                if (e.Key != "no_dq" && e.Key != "no_ally") throw new FormatException(where + ": невідома умова матчу '" + e.Key + "'");
            }
            else if (JsonRead.Has(o, "plan"))
            {
                e.Kind = EffectKind.Plan; e.Key = JsonRead.Str(o, "plan", where);
                BookerPlan bp;
                if (!FeudVars.TryPlan(e.Key, out bp))
                    throw new FormatException(where + ": невідомий план '" + e.Key + "'");
            }
            else if (o.ContainsKey("ally") && o["ally"] is bool) { e.Kind = EffectKind.Ally; e.Value = (bool)o["ally"]; e.Key = "ally"; }
            else throw new FormatException(where + ": невідомий ефект");
            return e;
        }
    }
}
