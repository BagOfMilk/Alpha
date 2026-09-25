using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Pressure;

namespace Game.Core.Loop
{
    /// <summary>
    /// Об'єкт, що вміє віддати і прийняти свій стан рядком. Потрібен, щоб
    /// зліпок збирався без посилань на конкретні класи: конвеєр не знає, що
    /// саме криється за портом, і знати не повинен.
    /// </summary>
    public interface IStateBlob
    {
        string CaptureState();
        void RestoreState(string blob);
    }

    /// <summary>
    /// Зліпок міського шару.
    ///
    /// АРХІТЕКТУРНИЙ КОМПРОМІС, оголошений явно. У сейв зобов'язане потрапити рівно
    /// те, що збірці Game.Gameplay читати заборонено: значення Напруги,
    /// заряди накопичувачів, почуті ступені. Віддавати назовні типізоване
    /// DTO не можна — це і був би готовий дашборд.
    ///
    /// Тому назовні йде НЕПРОЗОРИЙ РЯДОК. Game.Gameplay передає його в
    /// систему збережень як є; щоб дістати звідти число, треба свідомо
    /// написати парсер — тобто порушити правило навмисно, а не наткнутися на
    /// нього по дорозі. Інваріант 3 звучить як «дашборд неможливо зібрати навіть
    /// помилково», і саме це тут і зберігається.
    ///
    /// Формат версіонований: невідома версія не завантажується мовчки.
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

            // Дробові залишки решти драйверів (храм, укріплення): без них
            // кожне завантаження непомітно округляло б їхній внесок униз.
            string fractions = p.Tension.OtherFractionsForSave();
            if (!string.IsNullOrEmpty(fractions)) sb.Append(';').Append("tfr=").Append(fractions);

            if (p.Population != null) Field(sb, "pop", p.Population.Count);

            // Страх громади: без нього завантаження було б безкоштовним способом зняти
            // ціну кривавого шляху — зберігся, перезавантажився, і ніхто не пам'ятає.
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

            // Міські роботи: без них завантаження скасовувало б будівництво, за яке
            // вже заплачено, і відкат облави — безкоштовна облава за перезапуск.
            if (p.CityState != null)
            {
                string city = p.CityState.CaptureState();
                if (!string.IsNullOrEmpty(city)) sb.Append(';').Append("city=").Append(city);
            }

            // Ростер і партія в полі (Поправка №5.6 п. 4): без них продовження
            // відрізнялося б від безперервного — люди стоять не там, а ті, хто пішов, удома.
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

            // Господарство гравця (Foundation/A1, закриває D10): без гаманця,
            // відкритих слотів і прожитого рівня завантаження повертало б гру
            // без жодного заробленого гроша і без жодного підвищення. Порт, а
            // не тип бази напряму — Loop не повинен знати про Game.Core.Base
            // (охоронець Loop_DoesNotReferenceBase), тому фактична форма
            // блоба збирається на стороні BaseState (Game.Core.Base), який
            // реалізує той самий IStateBlob, що й CityState/Roster/Party нижче.
            if (p.Economy != null)
            {
                string blob = p.Economy.CaptureState();
                if (!string.IsNullOrEmpty(blob)) sb.Append(';').Append("eco=").Append(blob);
            }

            // Виснаження точок вилазки (R15): без нього завантаження повертало б
            // кожну точку незайманою, і повтор точки після save/load знову
            // давав би повну здобич безкоштовно.
            if (p.Sites != null)
            {
                string blob = p.Sites.CaptureState();
                if (!string.IsNullOrEmpty(blob)) sb.Append(';').Append("sites=").Append(blob);
            }

            // Сюжетні прапорці (R3): «бачив пропозицію», «зерно зради посіяне» —
            // без зліпка ці позначки знімалися б кожним завантаженням безкоштовно.
            if (p.Flags != null)
            {
                string blob = p.Flags.CaptureState();
                if (!string.IsNullOrEmpty(blob)) sb.Append(';').Append("flags=").Append(blob);
            }

            // Черга QueueExternal (D1a, шов документований у SaveState()):
            // без неї заявка Напруги, покладена ззовні між фазами (напр.
            // квестовим наслідком у Morning до AdvanceDay), губилася б
            // мовчки при save/load.
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
            if (string.IsNullOrEmpty(blob)) throw new ArgumentException("Порожній зліпок збереження", nameof(blob));

            var parts = blob.Split(';');
            if (parts.Length == 0 || parts[0] != Version)
                throw new InvalidOperationException("Збереження іншої версії гри: " + (parts.Length > 0 ? parts[0] : "?"));

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
