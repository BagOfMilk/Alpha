using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Stage;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Лічильник підстановок таблиці кліпів (критерій М1: «підстановок кліпів 0»; до кліпів «Кліпів» — лічильник). Храповик: число
    /// різних підстановок таблиці лише спадає — нову заглушку не додати мовчки. Перелік — у виводі тесту (звірка з CLIP_NEEDS.md).
    /// </summary>
    public sealed class RingBeatStandInTests
    {
        /// <summary>
        /// Базова лінія 08.11.2026 (після 3-P1-3) — 20; 09.11.2026 (Ф2 кроки 5 і 7, кліпи P1–P2) — 17. Лише спадає: кліп прийшов —
        /// зменшити число тут.
        /// </summary>
        private const int Baseline = 17;

        private static readonly RollTier[] Tiers = { RollTier.Miss, RollTier.Partial, RollTier.Strong };

        /// <summary>Усі плани таблиці: кожен прийом каталогу на трьох ступенях і кожна дія без ключа — на рингу й за ним.</summary>
        private static IEnumerable<KeyValuePair<string, List<StageStep>>> AllPlans()
        {
            var c = CareerKit.Content();
            foreach (var m in c.Moves.All)
                foreach (var t in Tiers)
                {
                    var ev = new MatchEvent { Kind = MatchEventKind.Action, Actor = MatchSide.Player, Action = m.Action, Tier = t, MoveKey = m.Id, Staging = m.Staging };
                    yield return new KeyValuePair<string, List<StageStep>>(m.Id + "/" + t, RingClipTable.Plan(ev));
                }
            foreach (MatchActionId a in Enum.GetValues(typeof(MatchActionId)))
                foreach (var t in Tiers)
                    foreach (var outside in new[] { false, true })
                    {
                        var ev = new MatchEvent { Kind = MatchEventKind.Action, Actor = MatchSide.Player, Action = a, Tier = t, Outside = outside };
                        yield return new KeyValuePair<string, List<StageStep>>(a + "/" + t + (outside ? "/за рингом" : ""), RingClipTable.Plan(ev));
                    }
        }

        [Test]
        public void StandIns_OnlyShrink()
        {
            var notes = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var kv in AllPlans())
                foreach (var st in kv.Value)
                {
                    if (!st.IsStandIn) continue;
                    List<string> where;
                    if (!notes.TryGetValue(st.Note, out where)) notes[st.Note] = where = new List<string>();
                    where.Add(kv.Key);
                }
            foreach (var kv in notes) TestContext.WriteLine("— " + kv.Key + "  [" + string.Join(", ", kv.Value.Take(4)) + (kv.Value.Count > 4 ? ", …" : "") + "]");
            TestContext.WriteLine("різних підстановок: " + notes.Count + " (базова лінія " + Baseline + ")");
            Assert.LessOrEqual(notes.Count, Baseline, "нова підстановка в таблиці кліпів — спершу кліп («Кліпи», CLIP_NEEDS.md)");
            if (notes.Count < Baseline) TestContext.WriteLine("Підстановок поменшало — знизь Baseline до " + notes.Count);
        }

        [Test]
        public void StandIn_IsCountedPerPlan_AndFinishersShowWhichAreMissing()
        {
            Func<string, int> fin = key => RingClipTable.StandIns(RingClipTable.Plan(new MatchEvent
            { Kind = MatchEventKind.Action, Actor = MatchSide.Rival, Action = MatchActionId.Finisher, Tier = RollTier.Strong, MoveKey = key }));
            Assert.AreEqual(1, fin("crusher"), "важкий ларіат замість підписного фінішера — підстановка");
            Assert.AreEqual(1, fin("iron_clamp"), "затиск голови замість больового фінішера — підстановка");
            Assert.AreEqual(0, fin("grand_finale"), "фліп — свій кліп");
            Assert.AreEqual(1, fin(null), "фінішер без ключа — запасний ларіат, підстановка");
            // утримання й рефері — свої кліпи
            Assert.AreEqual(0, RingClipTable.StandIns(RingClipTable.Plan(new MatchEvent { Kind = MatchEventKind.Kickout, Actor = MatchSide.Rival, Tier = RollTier.Partial })));
        }
    }
}
