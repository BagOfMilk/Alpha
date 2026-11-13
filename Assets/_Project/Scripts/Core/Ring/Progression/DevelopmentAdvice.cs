using System;
using System.Collections.Generic;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;

namespace Game.Core.Ring
{
    /// <summary>Що радить «Радимо зараз»: перк, прийом чи атрибут.</summary>
    public enum AdviceKind { Perk = 0, Move = 1, Attr = 2 }

    /// <summary>Одна порада розвитку: що купити, за скільки і чому (ключ рядка; null — звичайний опис перка, прийому чи атрибута).</summary>
    public sealed class DevAdvice
    {
        public AdviceKind Kind;
        /// <summary>id перка чи прийому; для атрибута — null.</summary>
        public string Id;
        public RingAttr Attr;
        public int Cost;
        /// <summary>Ключ пояснення: роль, ґімік, чого бракувало в останньому матчі (c.dev.why.*); null — опис із каталогу.</summary>
        public string WhyKey;
        public int Score;
    }

    /// <summary>
    /// Поради розвитку (P2-2, плейтест 08.11.2026: «Радимо зараз» завжди показувало «Друге дихання» і «Тик у вічі» — і з 3, і
    /// з 39 очками, і героєві, хоча брудний прийом — шлях лиходія). Порада — від ролі (домінантного шляху), ґіміка й того, чого
    /// бракувало в останньому матчі (утримання, больовий, ажіотаж, травма); лише те, що можна купити зараз. Брудних прийомів
    /// героєві й перків чужого шляху не радить. Очок багато — на одну пораду більше (до чотирьох). Порядок: найкраща порада
    /// кожного виду, далі решта за оцінкою. Лише читає стан — нічого не купує.
    /// Два пояснення однаковими не бувають (кадри «Меню», слот 28, рев'ю ПМ 12.11.2026: «Друге дихання» і «Витривалість» обидві
    /// казали «вчися вириватися»): причина з минулого матчу пояснюється за видом поради — атрибут як основа, перк як порятунок саме
    /// в цю мить, прийом як відповідь тим самим (<see cref="WhyFor"/>); якщо ключ однаково повторився (два перки ролі, два прийоми
    /// ґіміка), нижча порада йде з описом із каталогу.
    /// </summary>
    public static class DevelopmentAdvice
    {
        /// <summary>З такою кількістю очок розвитку порад на одну більше.</summary>
        public const int ManyPoints = 6;
        public const int MaxAdvice = 4;

        /// <summary>Ключ «пасує твоїй ролі»: c.dev.why.role.0 (герой) / .1 (лиходій) / .2 (бунтар).</summary>
        public static string RoleKey(RingPath role) { return "c.dev.why.role." + (int)role; }

        /// <summary>Ключ «прийом твоєї ролі» (брудний прийом лиходієві): c.dev.why.role.N.move — не те саме, що в перка ролі.</summary>
        public static string RoleMoveKey(RingPath role) { return RoleKey(role) + ".move"; }

        /// <summary>
        /// Ключ причини з минулого матчу (<c>lost_pin</c>, <c>lost_sub</c>, <c>low_hype</c>) за видом поради: атрибут —
        /// c.dev.why.&lt;причина&gt; (основа на кожен матч), перк — .perk, прийом — .move.
        /// </summary>
        public static string WhyFor(string reason, AdviceKind kind)
        {
            string k = "c.dev.why." + reason;
            return kind == AdviceKind.Perk ? k + ".perk" : kind == AdviceKind.Move ? k + ".move" : k;
        }

        public static List<DevAdvice> For(CareerSession s, int maxRecs)
        {
            var all = new List<DevAdvice>();
            var role = s.CurrentRole;
            var last = s.State.Last != null && s.State.Last.Played ? s.State.Last : null;
            bool lost = last != null && last.Winner == MatchWinner.Rival;
            bool lostPin = lost && last.EndKind == MatchEndKind.Pin;
            bool lostSub = lost && last.EndKind == MatchEndKind.Submission;
            bool lowHype = last != null && last.Hype < CareerRules.HypeBonusAt;
            bool injured = s.State.IsInjured;
            string gimmick = s.Content.GimmickOf(s.State.Gimmick).Key;
            string reason;

            foreach (var p in PerkCatalog.All)
            {
                if (!s.CanBuyPerk(p.Id, out reason)) continue;
                if (p.Path.HasValue && p.Path.Value != role) continue;   // чужий шлях — не радимо
                var a = new DevAdvice { Kind = AdviceKind.Perk, Id = p.Id, Cost = p.Cost, Score = p.Path.HasValue ? 30 + 2 * p.Rank : 18 };
                if (p.Path.HasValue) a.WhyKey = RoleKey(role);
                if ((lostPin || lostSub) && (p.Id == PerkCatalog.SecondWind || p.Id == PerkCatalog.Veteran)) { a.Score += 12; a.WhyKey = WhyFor(lostPin ? "lost_pin" : "lost_sub", a.Kind); }
                if (lowHype && (p.Id == PerkCatalog.CrowdFavorite || p.Id == PerkCatalog.HeroOfCrowd)) { a.Score += 10; a.WhyKey = WhyFor("low_hype", a.Kind); }
                if (injured && p.Id == PerkCatalog.IronJaw) { a.Score += 16; a.WhyKey = "c.dev.why.injured"; }
                all.Add(a);
            }

            foreach (var m in s.Content.Moves.All)
            {
                if (!m.IsLearnable || s.KnowsMove(m.Id)) continue;
                if (m.GimmickId != null && m.GimmickId != gimmick) continue;
                if (!s.CanLearnMove(m.Id, out reason)) continue;
                if (m.Action == MatchActionId.Dirty && role == RingPath.Face) continue;   // герою брудний прийом коштує ажіотажу
                var a = new DevAdvice { Kind = AdviceKind.Move, Id = m.Id, Cost = m.Cost, Score = m.GimmickId != null ? 26 : 14 };
                if (m.GimmickId != null) a.WhyKey = "c.dev.why.gimmick";
                if (m.Action == MatchActionId.Dirty && role == RingPath.Heel) { a.Score += 8; a.WhyKey = RoleMoveKey(role); }
                if (lostSub && m.Action == MatchActionId.Submission) { a.Score += 6; a.WhyKey = WhyFor("lost_sub", a.Kind); }
                all.Add(a);
            }

            for (int i = 0; i < 5; i++)
            {
                var attr = (RingAttr)i;
                if (!s.CanBuyAttr(attr, out reason)) continue;
                var a = new DevAdvice { Kind = AdviceKind.Attr, Attr = attr, Cost = s.AttrCost(attr), Score = 10 + (WrestlerStats.MaxAttr - s.Player.Get(attr)) };
                if ((lostPin || lostSub) && attr == RingAttr.Stamina) { a.Score += 14; a.WhyKey = WhyFor(lostPin ? "lost_pin" : "lost_sub", a.Kind); }
                if (lowHype && attr == RingAttr.Charisma) { a.Score += 12; a.WhyKey = WhyFor("low_hype", a.Kind); }
                all.Add(a);
            }

            int count = Math.Min(MaxAdvice, Math.Max(0, maxRecs) + (s.DevPoints >= ManyPoints ? 1 : 0));
            // стабільне сортування: оцінка, далі порядок каталогу (перки, прийоми, атрибути)
            var order = new List<DevAdvice>(all);
            order.Sort((x, y) =>
            {
                int c = y.Score.CompareTo(x.Score);
                return c != 0 ? c : all.IndexOf(x).CompareTo(all.IndexOf(y));
            });
            var res = new List<DevAdvice>();
            var kinds = new HashSet<AdviceKind>();
            foreach (var a in order)
                if (res.Count < count && kinds.Add(a.Kind)) res.Add(a);
            foreach (var a in order)
                if (res.Count < count && !res.Contains(a)) res.Add(a);
            res.Sort((x, y) =>
            {
                int c = y.Score.CompareTo(x.Score);
                return c != 0 ? c : all.IndexOf(x).CompareTo(all.IndexOf(y));
            });
            // одне пояснення — одна порада: повтор (вища порада вже його сказала) — опис із каталогу; поради — нові об'єкти, стан не чіпаємо
            var said = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in res)
                if (a.WhyKey != null && !said.Add(a.WhyKey)) a.WhyKey = null;
            return res;
        }
    }
}
