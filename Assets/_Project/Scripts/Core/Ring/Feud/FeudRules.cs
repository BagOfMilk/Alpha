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

    public enum ConditionKind { Flag = 0, NotFlag = 1, Open = 2, Var = 3, Attr = 4, Path = 5 }

    /// <summary>Умова варіанта вибору (gdd §4.1 requires).</summary>
    public sealed class FeudCondition
    {
        public ConditionKind Kind;
        /// <summary>Прапор / гілка / змінна / атрибут / шлях — залежно від Kind.</summary>
        public string Key;
        /// <summary>Мінімум (для Var, Attr, Path).</summary>
        public int Min;

        public static FeudCondition FromJson(Dictionary<string, object> o, string where)
        {
            var c = new FeudCondition();
            if (JsonRead.Has(o, "flag")) { c.Kind = ConditionKind.Flag; c.Key = JsonRead.Str(o, "flag", where); }
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
            switch (Kind)
            {
                case ConditionKind.Flag: return "flag:" + Key;
                case ConditionKind.NotFlag: return "notFlag:" + Key;
                case ConditionKind.Open: return "open:" + Key;
                case ConditionKind.Var: return "var:" + Key + ">=" + Min;
                case ConditionKind.Attr: return "attr:" + Key + ">=" + Min;
                default: return "path:" + Key + ">=" + Min;
            }
        }
    }

    public enum EffectKind { Var = 0, Flag = 1, Close = 2, Path = 3, Stip = 4, Plan = 5, Ally = 6 }

    /// <summary>Ефект варіанта/нагороди/провалу (gdd §4.1).</summary>
    public sealed class FeudEffect
    {
        public EffectKind Kind;
        /// <summary>Змінна / прапор / гілка / шлях / умова матчу / план — залежно від Kind.</summary>
        public string Key;
        /// <summary>Приріст (Var, Path).</summary>
        public int Amount;
        /// <summary>Значення для Ally.</summary>
        public bool Value = true;

        public static FeudEffect FromJson(Dictionary<string, object> o, string where)
        {
            var e = new FeudEffect();
            if (JsonRead.Has(o, "var"))
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
