using System;
using System.Collections.Generic;

namespace Game.Core.Ring
{
    /// <summary>Типізоване читання розібраного JSON (MiniJson). Помилка схеми — FormatException із шляхом.</summary>
    public static class JsonRead
    {
        public static bool Has(Dictionary<string, object> o, string key) => o.ContainsKey(key) && o[key] != null;

        public static string Str(Dictionary<string, object> o, string key, string where, bool required = true)
        {
            object v;
            if (!o.TryGetValue(key, out v) || v == null)
            {
                if (required) throw new FormatException(where + ": немає поля '" + key + "'");
                return null;
            }
            var s = v as string;
            if (s == null) throw new FormatException(where + "." + key + ": очікувався рядок");
            return s;
        }

        public static int Int(Dictionary<string, object> o, string key, string where, int? def = null)
        {
            object v;
            if (!o.TryGetValue(key, out v) || v == null)
            {
                if (def.HasValue) return def.Value;
                throw new FormatException(where + ": немає поля '" + key + "'");
            }
            if (!(v is double)) throw new FormatException(where + "." + key + ": очікувалось число");
            double d = (double)v;
            if (d != Math.Floor(d)) throw new FormatException(where + "." + key + ": очікувалось ціле");
            return (int)d;
        }

        public static bool Bool(Dictionary<string, object> o, string key, string where, bool def)
        {
            object v;
            if (!o.TryGetValue(key, out v) || v == null) return def;
            if (!(v is bool)) throw new FormatException(where + "." + key + ": очікувалось true/false");
            return (bool)v;
        }

        public static Dictionary<string, object> Obj(object v, string where)
        {
            var o = v as Dictionary<string, object>;
            if (o == null) throw new FormatException(where + ": очікувався об'єкт");
            return o;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> o, string key, string where, bool required = true)
        {
            object v;
            if (!o.TryGetValue(key, out v) || v == null)
            {
                if (required) throw new FormatException(where + ": немає поля '" + key + "'");
                return null;
            }
            return Obj(v, where + "." + key);
        }

        /// <summary>Масив об'єктів; відсутнє поле — порожній список.</summary>
        public static List<Dictionary<string, object>> ObjList(Dictionary<string, object> o, string key, string where)
        {
            var res = new List<Dictionary<string, object>>();
            object v;
            if (!o.TryGetValue(key, out v) || v == null) return res;
            var a = v as List<object>;
            if (a == null) throw new FormatException(where + "." + key + ": очікувався масив");
            for (int i = 0; i < a.Count; i++) res.Add(Obj(a[i], where + "." + key + "[" + i + "]"));
            return res;
        }
    }
}
