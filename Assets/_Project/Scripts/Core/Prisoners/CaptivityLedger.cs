using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Core.Prisoners
{
    /// <summary>
    /// Годинник полону нашої людини (Поправка №14.7, інваріант 6):
    /// ЯКІР — скільки діб бранець у чужих руках;
    /// СПОЖИВАЧ — щодня знімає лояльність (що довше, то більше), аж до наявної
    /// зради (<c>Companions/Defection.cs</c>);
    /// СИГНАЛ — зміна полоси пишеться у стрічку (<c>captivity.band.*</c>).
    /// Число — internal; назовні лише полоса.
    /// </summary>
    public enum CaptivityBand { Holding = 0, Worn = 1, Breaking = 2 }

    /// <summary>Наша людина в полоні: у кого, з якого загону, відколи.</summary>
    public sealed class Captive
    {
        public string CompanionId;
        /// <summary>Іменний ворог, що тримає бранця, — найстарший за рангом у бою.</summary>
        public string CaptorEnemyId;
        /// <summary>Ранг тримача: 0 — рядовий, 1 — міні-бос, 2 — бос. Від нього — викуп і поріг перемовин.</summary>
        public int CaptorRank;
        /// <summary>Загін тримача: з ким битися в рейді. Бранці одного загону визволяються разом.</summary>
        public string GroupId;
        public List<string> EnemyGroup = new List<string>();
        public int DayTaken;

        internal int Days;
    }

    /// <summary>Що сталося з бранцем за добу — ключ для стрічки і сам бранець.</summary>
    public sealed class CaptivityEvent
    {
        /// <summary>"band" — полоса годинника змінилась.</summary>
        public string Kind;
        public Captive Captive;
    }

    /// <summary>Хто втік із поля, а хто лишився в полоні (Поправка №14.7).</summary>
    public sealed class EscapeSplit
    {
        public readonly List<string> Escaped = new List<string>();
        public readonly List<string> Captured = new List<string>();
    }

    /// <summary>Числа полону — ПЛЕЙСХОЛДЕРИ (Поправка №14.11).</summary>
    public sealed class CaptivityBalance
    {
        /// <summary>З якої доби полону полоса «виснажується», з якої — «ламається».</summary>
        public int WornAfterDays = 3;
        public int BreakingAfterDays = 6;

        /// <summary>Зміна лояльності бранця за добу — за полосою (Holding, Worn, Breaking).</summary>
        public int[] LoyaltyPerDay = { -2, -4, -6 };

        /// <summary>Викуп за рангом тримача (рядовий, міні-бос, бос) і знижка за найкращу Торгівлю.</summary>
        public int[] RansomGold = { 30, 60, 120 };
        public int RansomDiscountPerTrade = 3;
        public int RansomMinGold = 10;

        /// <summary>Поріг Переконання для перемовин за рангом тримача — видно до кліку (інваріант 8).</summary>
        public int[] TalkPersuade = { 3, 5, 7 };

        /// <summary>Визволений вдячний громаді, що не кинула.</summary>
        public int RescueLoyaltyBonus = 5;
    }

    /// <summary>
    /// Хто втікає з програного бою (Поправка №14.7; власник, 29.09.2026: «Але десь
    /// половина має втекти», про протагоніста — «ні не може»). Детерміновано
    /// (інваріант 1): жодного кидка — лише порядок падіння, який видно в колесі.
    /// </summary>
    public static class CaptivityRules
    {
        /// <summary>
        /// <paramref name="living"/> — живі бійці загону (мертвих у полон не беруть);
        /// <paramref name="standing"/> — хто ще на ногах; <paramref name="fallOrder"/> —
        /// порядок падіння (перший упав першим).
        /// Поразка: утікає ⌈n/2⌉ — протагоніст завжди, решта місць — тим, хто впав
        /// останніми. Відступ: на ногах виходять, кожен виносить одного впалого
        /// (хто впав останнім — першим), але не менше ⌈n/2⌉, ніж за поразки,
        /// — відступ не буває гіршим за програш до кінця.
        /// </summary>
        public static EscapeSplit Split(IReadOnlyList<string> living, ICollection<string> standing,
                                        IReadOnlyList<string> fallOrder, string protagonistId, bool retreat)
        {
            var split = new EscapeSplit();
            if (living == null || living.Count == 0) return split;

            var order = new List<string>();
            void Put(string id)
            {
                if (!string.IsNullOrEmpty(id) && !order.Contains(id) && Contains(living, id)) order.Add(id);
            }

            Put(protagonistId);
            if (retreat && standing != null)
                foreach (var id in living)
                    if (standing.Contains(id)) Put(id);
            if (fallOrder != null)
                for (int i = fallOrder.Count - 1; i >= 0; i--) Put(fallOrder[i]);
            foreach (var id in living) Put(id);

            int n = order.Count;
            int half = (n + 1) / 2;
            int slots = half;
            if (retreat)
            {
                int onFeet = 0;
                if (standing != null)
                    foreach (var id in order)
                        if (standing.Contains(id)) onFeet++;
                int carried = Math.Min(onFeet, n - onFeet);
                slots = Math.Max(half, onFeet + carried);
            }
            if (Contains(living, protagonistId)) slots = Math.Max(slots, 1);

            for (int i = 0; i < n; i++)
                (i < slots ? split.Escaped : split.Captured).Add(order[i]);
            return split;
        }

        private static bool Contains(IReadOnlyList<string> list, string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], id, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    /// <summary>
    /// Наші люди в полоні (Поправка №14.7): годинник, викуп, перемовини, рейд.
    /// Бранець не на посту, не у вилазці і не серед присутніх — статус
    /// <c>CompanionStatus.Captive</c>; тут — лише у кого він і відколи.
    /// </summary>
    public sealed class CaptivityLedger
    {
        public CaptivityBalance Balance = new CaptivityBalance();

        private readonly List<Captive> _all = new List<Captive>();
        private int _groupSeq;

        public IReadOnlyList<Captive> All => _all;

        public Captive Get(string companionId)
        {
            foreach (var c in _all)
                if (string.Equals(c.CompanionId, companionId, StringComparison.Ordinal)) return c;
            return null;
        }

        public List<Captive> Group(string groupId)
        {
            var list = new List<Captive>();
            foreach (var c in _all)
                if (string.Equals(c.GroupId, groupId, StringComparison.Ordinal)) list.Add(c);
            return list;
        }

        /// <summary>Новий загін тримача — id групи, до якої беруть бранців одного бою.</summary>
        public string NewGroupId(int day)
        {
            _groupSeq++;
            return "g" + I(day) + "x" + I(_groupSeq);
        }

        public Captive Take(string companionId, string captorEnemyId, int captorRank, string groupId,
                            IReadOnlyList<string> enemyGroup, int day)
        {
            var existing = Get(companionId);
            if (existing != null) return existing;
            var c = new Captive
            {
                CompanionId = companionId, CaptorEnemyId = captorEnemyId, CaptorRank = Clamp(captorRank),
                GroupId = groupId, DayTaken = day
            };
            if (enemyGroup != null) c.EnemyGroup.AddRange(enemyGroup);
            _all.Add(c);
            return c;
        }

        public bool Remove(string companionId)
        {
            var c = Get(companionId);
            return c != null && _all.Remove(c);
        }

        public CaptivityBand BandOf(Captive c) =>
            c.Days >= Balance.BreakingAfterDays ? CaptivityBand.Breaking
            : c.Days >= Balance.WornAfterDays ? CaptivityBand.Worn : CaptivityBand.Holding;

        /// <summary>Скільки лояльності бранець втрачає цієї доби — споживач годинника.</summary>
        public int LoyaltyDeltaFor(Captive c)
        {
            var t = Balance.LoyaltyPerDay;
            int i = (int)BandOf(c);
            return t != null && i < t.Length ? t[i] : 0;
        }

        public int RansomFor(Captive c, int bestTrade)
        {
            var t = Balance.RansomGold;
            int baseGold = t != null && c.CaptorRank < t.Length ? t[c.CaptorRank] : Balance.RansomMinGold;
            return Math.Max(Balance.RansomMinGold, baseGold - Math.Max(0, bestTrade) * Balance.RansomDiscountPerTrade);
        }

        public int TalkThreshold(Captive c)
        {
            var t = Balance.TalkPersuade;
            return t != null && c.CaptorRank < t.Length ? t[c.CaptorRank] : int.MaxValue;
        }

        /// <summary>Доба полону: годинник іде; зміна полоси — подія для стрічки (інваріант 4).</summary>
        public List<CaptivityEvent> Tick(int day)
        {
            var events = new List<CaptivityEvent>();
            foreach (var c in _all)
            {
                var before = BandOf(c);
                c.Days = Math.Max(c.Days, day - c.DayTaken);
                if (BandOf(c) != before) events.Add(new CaptivityEvent { Kind = "band", Captive = c });
            }
            return events;
        }

        // ---- зліпок: без ';' і '=' (заголовок сейву) ----

        public string CaptureState()
        {
            var sb = new StringBuilder();
            sb.Append(I(_groupSeq));
            foreach (var c in _all)
            {
                sb.Append('|').Append(c.CompanionId).Append(',').Append(c.CaptorEnemyId ?? string.Empty).Append(',')
                  .Append(I(c.CaptorRank)).Append(',').Append(c.GroupId ?? string.Empty).Append(',')
                  .Append(string.Join("+", c.EnemyGroup)).Append(',').Append(I(c.DayTaken)).Append(',').Append(I(c.Days));
            }
            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            _all.Clear();
            _groupSeq = 0;
            if (string.IsNullOrEmpty(blob)) return;
            var parts = blob.Split('|');
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _groupSeq);
            for (int i = 1; i < parts.Length; i++)
            {
                var f = parts[i].Split(',');
                if (f.Length < 7) continue;
                var c = new Captive
                {
                    CompanionId = f[0], CaptorEnemyId = f[1], CaptorRank = Clamp(ParseInt(f[2])), GroupId = f[3],
                    DayTaken = ParseInt(f[5]), Days = ParseInt(f[6])
                };
                if (!string.IsNullOrEmpty(f[4])) c.EnemyGroup.AddRange(f[4].Split('+'));
                _all.Add(c);
            }
        }

        private static int Clamp(int rank) => rank < 0 ? 0 : (rank > 2 ? 2 : rank);

        private static int ParseInt(string s) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
