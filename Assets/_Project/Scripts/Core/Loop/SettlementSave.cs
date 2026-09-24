using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Pressure;

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

            // Дробные остатки остальных драйверов (храм, укрепления): без них
            // каждая загрузка незаметно округляла бы их вклад вниз.
            string fractions = p.Tension.OtherFractionsForSave();
            if (!string.IsNullOrEmpty(fractions)) sb.Append(';').Append("tfr=").Append(fractions);

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

            // Городские работы: без них загрузка отменяла бы стройку, за которую
            // уже заплачено, и откат облавы — бесплатная облава за перезапуск.
            if (p.CityState != null)
            {
                string city = p.CityState.CaptureState();
                if (!string.IsNullOrEmpty(city)) sb.Append(';').Append("city=").Append(city);
            }

            // Ростер и партия в поле (Поправка №5.6 п. 4): без них продолжение
            // отличимо от непрерывного — люди стоят не там, а ушедшие дома.
            var roster = p.Roster as IStateBlob;
            if (roster != null)
            {
                string blob = roster.CaptureState();
                if (!string.IsNullOrEmpty(blob)) sb.Append(';').Append("ros=").Append(blob);
            }

            if (p.Party != null)
            {
                string blob = p.Party.CaptureState();
                if (!string.IsNullOrEmpty(blob)) sb.Append(';').Append("party=").Append(blob);
            }

            // Хозяйство игрока (Foundation/A1, закрывает D10): без кошелька,
            // открытых слотов и прожитого уровня загрузка возвращала бы игру
            // без единого заработанного гроша и без единого повышения. Порт, а
            // не тип базы напрямую — Loop не должен знать про Game.Core.Base
            // (охранитель Loop_DoesNotReferenceBase), поэтому фактическая форма
            // блоба собирается на стороне BaseState (Game.Core.Base), который
            // реализует тот же IStateBlob, что и CityState/Roster/Party ниже.
            if (p.Economy != null)
            {
                string blob = p.Economy.CaptureState();
                if (!string.IsNullOrEmpty(blob)) sb.Append(';').Append("eco=").Append(blob);
            }

            // Истощение точек вылазки (R15): без него загрузка возвращала бы
            // каждую точку девственной, и повтор точки после save/load снова
            // давал бы полную добычу бесплатно.
            if (p.Sites != null)
            {
                string blob = p.Sites.CaptureState();
                if (!string.IsNullOrEmpty(blob)) sb.Append(';').Append("sites=").Append(blob);
            }

            // Сюжетные флаги (R3): «видел предложение», «зерно зрады посеяно» —
            // без слепка эти отметки снимались бы каждой загрузкой бесплатно.
            if (p.Flags != null)
            {
                string blob = p.Flags.CaptureState();
                if (!string.IsNullOrEmpty(blob)) sb.Append(';').Append("flags=").Append(blob);
            }

            // Очередь QueueExternal (D1a, шов документирован в SaveState()):
            // без неё заявка Напруги, положенная извне между фазами (напр.
            // квестовым наслідком у Morning до AdvanceDay), терялась бы
            // молча при save/load.
            var extQueue = p.PeekExternalTensionForSave();
            if (extQueue.Count > 0)
            {
                sb.Append(';').Append("extq=");
                for (int i = 0; i < extQueue.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append((int)extQueue[i].Driver).Append('|').Append(extQueue[i].Amount);
                }
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
                else if (key == "city" && p.CityState != null) p.CityState.RestoreState(value);
                else if (key == "tension") RestoreTension(p, value);
                else if (key == "tfr") p.Tension.RestoreOtherFractions(value);
                else if (key == "trk") RestoreTrack(p, value);
                else if (key == "sig") p.SignalMemory.RestoreState(value);
                else if (key == "ros")
                {
                    var roster = p.Roster as IStateBlob;
                    if (roster != null) roster.RestoreState(value);
                }
                else if (key == "party")
                {
                    if (p.Party != null) p.Party.RestoreState(value);
                }
                else if (key == "rep")
                {
                    var repeats = p.Repeats as IStateBlob;
                    if (repeats != null) repeats.RestoreState(value);
                }
                else if (key == "eco" && p.Economy != null) p.Economy.RestoreState(value);
                else if (key == "sites" && p.Sites != null) p.Sites.RestoreState(value);
                else if (key == "flags" && p.Flags != null) p.Flags.RestoreState(value);
                else if (key == "extq") RestoreExternalTensionQueue(p, value);
            }
        }

        private static void RestoreExternalTensionQueue(DayProcessor p, string value)
        {
            var entries = new List<ExternalTensionEntry>();
            if (!string.IsNullOrEmpty(value))
            {
                foreach (var entry in value.Split(','))
                {
                    var f = entry.Split('|');
                    if (f.Length < 2) continue;
                    entries.Add(new ExternalTensionEntry((Pressure.TensionDriver)ParseInt(f[0]), ParseInt(f[1])));
                }
            }
            p.RestoreExternalTensionForSave(entries);
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
