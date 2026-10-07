using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.World;

namespace Game.Core.Expeditions
{
    /// <summary>Кого і наскільки зачепило.</summary>
    public struct ExpeditionWound
    {
        public string ActorId;
        public WoundTier Tier;
    }

    /// <summary>
    /// Що гравець бачить ДО відправки (US-17.3: пороги показуються заздалегідь).
    /// Рівно ті самі числа, що застосує резолв — рахує їх один і той самий код.
    /// </summary>
    public sealed class ExpeditionPreview
    {
        public string SiteId;
        public ExpeditionApproach Approach;

        public int Threshold;
        public int PartyValue;
        public int Margin;
        public OutcomeBand Band;

        public int Days;
        /// <summary>Будівельний компонент (Поправка №12.5).</summary>
        public int BuildComponent;
        /// <summary>Крафтовий компонент (Поправка №12.5).</summary>
        public int CraftComponent;
        public int Gold;

        /// <summary>Скільки разів точку вже відпрацьовували і в скільки це обійшлося здобичі.</summary>
        public int TimesWorked;
        public double YieldMultiplier;

        /// <summary>Скільки людей повернеться пораненими. Показується заздалегідь — це і є ціна.</summary>
        public int ExpectedWounded;

        /// <summary>Погода доби відходу — вона вже врахована в здобичі (Поправка №21.2).</summary>
        public WeatherKind Weather;

        public bool HasParty => PartyValue > 0 || Threshold <= 0;
    }

    /// <summary>Підсумок вилазки.</summary>
    public sealed class ExpeditionResult
    {
        public string SiteId;
        public ExpeditionApproach Approach;
        public OutcomeBand Band;

        public int Days;
        /// <summary>Будівельний компонент (Поправка №12.5).</summary>
        public int BuildComponent;
        /// <summary>Крафтовий компонент (Поправка №12.5).</summary>
        public int CraftComponent;
        public int Gold;

        /// <summary>Люди, знайдені на точці. Приходять у місто через міські роботи.</summary>
        public int People;

        public List<ExpeditionWound> Wounded = new List<ExpeditionWound>();

        /// <summary>Хто ходив — у тому самому порядку, у якому їх відправили.</summary>
        public List<string> PartyIds = new List<string>();

        // ---- зліпок (R15) ----
        //
        // Результат резолвиться РІВНО ОДИН РАЗ, у момент відправки (§4.11), і
        // мусить пережити збереження/завантаження до самого повернення партії —
        // інакше сейв посеред вилазки міняв би вже вирішений результат. Формат
        // плаский, роздільник полів '|' не перетинається з роздільниками
        // ExpeditionParty ('>' і ',' — там свої сутності), а список ран/id
        // партії всередині поля розділений '~'.

        public string ToBlob()
        {
            var sb = new StringBuilder();
            sb.Append(SiteId ?? string.Empty).Append('|')
              .Append((int)Approach).Append('|')
              .Append((int)Band).Append('|')
              .Append(Days.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(BuildComponent.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(Gold.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(People.ToString(CultureInfo.InvariantCulture)).Append('|');

            for (int i = 0; i < Wounded.Count; i++)
            {
                if (i > 0) sb.Append('~');
                sb.Append(Wounded[i].ActorId).Append(':').Append((int)Wounded[i].Tier);
            }
            sb.Append('|');

            for (int i = 0; i < PartyIds.Count; i++)
            {
                if (i > 0) sb.Append('~');
                sb.Append(PartyIds[i]);
            }

            // Поправка №12.5: крафтовий компонент — ОСТАННІМ полем, щоб
            // зліпок до розділу (9 полів) читався як є: поле 4 у ньому —
            // спільні матеріали, вони йдуть у будівельний, крафтового — нуль.
            sb.Append('|').Append(CraftComponent.ToString(CultureInfo.InvariantCulture));

            return sb.ToString();
        }

        public static ExpeditionResult FromBlob(string blob)
        {
            if (string.IsNullOrEmpty(blob)) return null;

            var f = blob.Split('|');
            if (f.Length < 7) return null;

            var result = new ExpeditionResult
            {
                SiteId = f[0],
                Approach = (ExpeditionApproach)ParseInt(f[1]),
                Band = (OutcomeBand)ParseInt(f[2]),
                Days = ParseInt(f[3]),
                BuildComponent = ParseInt(f[4]),
                Gold = ParseInt(f[5]),
                People = ParseInt(f[6]),
            };

            if (f.Length > 7 && !string.IsNullOrEmpty(f[7]))
                foreach (var w in f[7].Split('~'))
                {
                    var pair = w.Split(':');
                    if (pair.Length != 2) continue;
                    result.Wounded.Add(new ExpeditionWound { ActorId = pair[0], Tier = (WoundTier)ParseInt(pair[1]) });
                }

            if (f.Length > 8 && !string.IsNullOrEmpty(f[8]))
                foreach (var id in f[8].Split('~'))
                    result.PartyIds.Add(id);

            if (f.Length > 9)
                result.CraftComponent = ParseInt(f[9]);

            return result;
        }

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }
}
