using System;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Core.Combat;
using Game.Core.Saves;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Телеметрия плейтеста (итерация 17, GDD Приложение В): JSONL append-only —
    /// одно событие = одна строка JSON, крашебезопасно (строка либо записана, либо
    /// нет), тривиально парсится jq/pandas. Файл на сессию кампании в
    /// persistentDataPath/telemetry/. Пишется вручную через StringBuilder —
    /// JsonUtility не дружит со словарями/разнородными полезными нагрузками.
    /// Сбой записи НИКОГДА не роняет игру (плейтест важнее метрик).
    /// </summary>
    public static class Telemetry
    {
        private static string _path;
        private static float _startedAt;

        public static string Dir => Path.Combine(Application.persistentDataPath, "telemetry");

        /// <summary>Активна ли сессия (файл открыт Begin-ом).</summary>
        public static bool Active => _path != null;

        /// <summary>Начинает файл сессии; повторный Begin той же кампании — новый файл (новая сессия игры).</summary>
        public static void Begin(Campaign campaign)
        {
            // Headless-прогоны тестов не мусорят файлами сессий в persistentDataPath.
            if (Application.isBatchMode) { _path = null; return; }
            try
            {
                Directory.CreateDirectory(Dir);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                int seed = campaign != null ? campaign.Seed : 0;
                _path = Path.Combine(Dir, $"session_{stamp}_{(uint)seed}.jsonl");
                _startedAt = Time.realtimeSinceStartup;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Telemetry: не удалось начать сессию — {e.Message}");
                _path = null;
            }
        }

        public static void End()
        {
            _path = null;
        }

        /// <summary>
        /// Пишет событие. Пары (ключ, значение): string/bool/int/long/float/double —
        /// как JSON-типы, всё остальное — строкой. Конверт добавляется сам:
        /// ts (UTC), t (сек от старта сессии), day/seed из кампании.
        /// </summary>
        public static void Event(string name, params (string key, object value)[] payload)
        {
            if (_path == null) return;
            try
            {
                var sb = new StringBuilder(256);
                sb.Append("{\"ev\":\"").Append(Escape(name)).Append('"');
                sb.Append(",\"ts\":\"")
                  .Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append('"');
                sb.Append(",\"t\":").Append(
                    (Time.realtimeSinceStartup - _startedAt).ToString("F1", CultureInfo.InvariantCulture));

                var c = GameFlow.Campaign;
                if (c != null)
                {
                    sb.Append(",\"day\":").Append(c.Base.CurrentDay);
                    sb.Append(",\"seed\":").Append((uint)c.Seed);
                }

                if (payload != null)
                    foreach (var (key, value) in payload)
                    {
                        sb.Append(",\"").Append(Escape(key)).Append("\":");
                        AppendValue(sb, value);
                    }

                sb.Append('}').Append('\n');
                File.AppendAllText(_path, sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Telemetry: событие {name} потеряно — {e.Message}");
            }
        }

        /// <summary>Агрегаты боя для battle_ended: раунды, атаки, hit-rate vs показанный
        /// шанс (R11). Только атаки ГРАВЦА: врагам шанс не телеграфируется — их роллы
        /// размывали бы метрику честности показанного процента.</summary>
        public static (string key, object value)[] CombatStats(CombatState cs, string kind)
        {
            int attacks = 0, hits = 0, grazes = 0, misses = 0, crits = 0, forced = 0;
            long chanceSum = 0;
            int playerDowns = 0, playerDeaths = 0, enemyDeaths = 0;
            foreach (var a in cs.Attacks)
            {
                if (a.AttackerSide != Side.Player) continue;
                if (a.Forced) { forced++; continue; }
                attacks++;
                chanceSum += a.Chance;
                if (a.Outcome == HitOutcome.Hit) hits++;
                else if (a.Outcome == HitOutcome.Graze) grazes++;
                else misses++;
                if (a.Crit) crits++;
            }
            foreach (var u in cs.Units)
            {
                if (u.Side == Side.Player)
                {
                    if (u.LifeState == UnitLifeState.Downed || u.LifeState == UnitLifeState.Stabilized) playerDowns++;
                    if (u.LifeState == UnitLifeState.Dead) playerDeaths++;
                }
                else if (u.LifeState == UnitLifeState.Dead) enemyDeaths++;
            }
            return new (string, object)[]
            {
                ("kind", kind),
                ("outcome", cs.Outcome.ToString()),
                ("rounds", cs.Round),
                ("attacks", attacks),
                ("hits", hits), ("grazes", grazes), ("misses", misses), ("crits", crits),
                ("strikes", forced),
                ("avgShownChance", attacks > 0 ? (int)(chanceSum / attacks) : 0),
                ("playerDowns", playerDowns),
                ("playerDeaths", playerDeaths),
                ("enemyDeaths", enemyDeaths)
            };
        }

        private static void AppendValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case uint u: sb.Append(u.ToString(CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(f.ToString("F2", CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString("F2", CultureInfo.InvariantCulture)); break;
                default: sb.Append('"').Append(Escape(value.ToString())).Append('"'); break;
            }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
