using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Core.Loop
{
    /// <summary>
    /// Объект, умеющий отдать и принять своё состояние строкой. Нужен, чтобы
    /// слепок собирался без ссылок на конкретные классы: конвейер не знает, что
    /// именно скрывается за портом, и знать не должен.
    /// </summary>
    public interface IStateBlob
    {
        string CaptureState();
        void RestoreState(string blob);
    }

    /// <summary>
    /// Слепок городского слоя.
    ///
    /// АРХИТЕКТУРНЫЙ КОМПРОМИСС, объявленный явно. В сейв обязано попасть ровно
    /// то, что сборке Game.Gameplay читать запрещено: значение Напряжения,
    /// заряды накопителей, услышанные ступени. Отдавать наружу типизированное
    /// DTO нельзя — это и был бы готовый дашборд.
    ///
    /// Поэтому наружу уходит НЕПРОЗРАЧНАЯ СТРОКА. Game.Gameplay передаёт её в
    /// систему сохранений как есть; чтобы достать оттуда число, надо осознанно
    /// написать парсер — то есть нарушить правило намеренно, а не наткнуться на
    /// него по дороге. Инвариант 3 звучит как «дашборд невозможно собрать даже
    /// по ошибке», и именно это здесь и сохраняется.
    ///
    /// Формат версионирован: неизвестная версия не грузится молча.
    /// </summary>
    internal static class SettlementSave
    {
        private const string Version = "alpha1";

        internal static string Capture(DayProcessor p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));

            var sb = new StringBuilder();
            sb.Append(Version);

            Field(sb, "day", p.CurrentDay);
            Field(sb, "tier", p.Tier);
            Field(sb, "order", p.OrderLevel);
            Field(sb, "patrol", p.IsPatrolling ? 1 : 0);

            sb.Append(';').Append("tension=")
              .Append(p.Tension.Value).Append('|')
              .Append(p.Tension.FractionForSave.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(p.Tension.DaysInCurrentBand);

            if (p.Population != null) Field(sb, "pop", p.Population.Count);

            // Страх общины: без него загрузка была бы бесплатным способом снять
            // цену кровавого пути — сохранился, перезагрузился, и никто не помнит.
            if (p.Fear != null && p.Fear.UntilDay >= 0) Field(sb, "fear", p.Fear.UntilDay);

            if (p.Pulse != null)
            {
                foreach (var pair in p.Pulse.Tracks)
                {
                    var t = pair.Value;
                    sb.Append(';').Append("trk=")
                      .Append(pair.Key).Append('|')
                      .Append(t.Charge).Append('|')
                      .Append(t.LastFiredDay).Append('|')
                      .Append(t.EverFiredForSave ? 1 : 0).Append('|')
                      .Append(t.DeliveredLevel).Append('|')
                      .Append(t.DeliveredLevel3Day).Append('|')
                      .Append(t.DeliveredThreeForSave ? 1 : 0);
                }
            }

            string signalBlob = p.SignalMemory.CaptureState();
            if (!string.IsNullOrEmpty(signalBlob)) sb.Append(';').Append("sig=").Append(signalBlob);

            var repeats = p.Repeats as IStateBlob;
            if (repeats != null)
            {
                string blob = repeats.CaptureState();
                if (!string.IsNullOrEmpty(blob)) sb.Append(';').Append("rep=").Append(blob);
            }

            return sb.ToString();
        }

        internal static void Restore(DayProcessor p, string blob)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (string.IsNullOrEmpty(blob)) throw new ArgumentException("Пустой слепок", nameof(blob));

            var parts = blob.Split(';');
            if (parts.Length == 0 || parts[0] != Version)
                throw new InvalidOperationException("Слепок другой версии: " + (parts.Length > 0 ? parts[0] : "?"));

            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;

                string key = parts[i].Substring(0, eq);
                string value = parts[i].Substring(eq + 1);

                if (key == "day") p.RestoreDay(ParseInt(value));
                else if (key == "tier") p.Tier = ParseInt(value);
                else if (key == "order") p.OrderLevel = ParseInt(value);
                else if (key == "patrol") p.IsPatrolling = ParseInt(value) != 0;
                else if (key == "pop" && p.Population != null) p.Population.RestoreForSave(ParseInt(value));
                else if (key == "fear" && p.Fear != null) p.Fear.RestoreForSave(ParseInt(value));
                else if (key == "tension") RestoreTension(p, value);
                else if (key == "trk") RestoreTrack(p, value);
                else if (key == "sig") p.SignalMemory.RestoreState(value);
                else if (key == "rep")
                {
                    var repeats = p.Repeats as IStateBlob;
                    if (repeats != null) repeats.RestoreState(value);
                }
            }
        }

        private static void RestoreTension(DayProcessor p, string value)
        {
            var f = value.Split('|');
            if (f.Length < 3) return;
            p.Tension.RestoreForSave(ParseInt(f[0]), ParseDouble(f[1]), ParseInt(f[2]));
        }

        private static void RestoreTrack(DayProcessor p, string value)
        {
            if (p.Pulse == null) return;

            var f = value.Split('|');
            if (f.Length < 7) return;

            World.PressureTrack track;
            if (!p.Pulse.Tracks.TryGetValue(f[0], out track)) return;

            track.RestoreForSave(ParseInt(f[1]), ParseInt(f[2]), ParseInt(f[3]) != 0,
                ParseInt(f[4]), ParseInt(f[5]), ParseInt(f[6]) != 0);
        }

        private static void Field(StringBuilder sb, string key, int value)
        {
            sb.Append(';').Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static double ParseDouble(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0.0;
        }
    }
}
