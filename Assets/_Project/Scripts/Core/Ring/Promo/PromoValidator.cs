using System.Collections.Generic;

namespace Game.Core.Ring
{
    public enum PromoErrorCode
    {
        MissingStart = 0,
        DuplicateNodeId = 1,
        MissingNext = 2,
        Unreachable = 3,
        NoPathToEnd = 4,
        NoFreeOption = 5,
        OptionWithoutEffect = 6,
        PickTooMany = 7,
        MissingText = 8,
        NoEnd = 9,
        EmptyChoice = 10,
        BadFlow = 11
    }

    public sealed class PromoError
    {
        public string PromoId;
        public string NodeId;
        public PromoErrorCode Code;
        public string Message;
        public override string ToString() => PromoId + "/" + NodeId + ": " + Code + " — " + Message;
    }

    /// <summary>Перевірка графів промо (gdd §4.2): next, досяжність, вихід до end, вільний варіант, ефекти, нагороди, рядки.</summary>
    public static class PromoValidator
    {
        public static List<PromoError> Validate(FeudScript script, IDictionary<string, string> strings)
        {
            var errs = new List<PromoError>();
            Text(errs, strings, "feud", "-", script.TitleKey);
            Text(errs, strings, "feud", "-", script.Player.NameKey);
            Text(errs, strings, "feud", "-", script.Rival.NameKey);
            foreach (var p in script.Promos) ValidatePromo(p, strings, errs);
            ValidateFlow(script, errs);
            return errs;
        }

        /// <summary>
        /// Порядок кроків (gdd §5.2): кожне промо й кожен матч рівно один раз; перший крок — промо; останній — єдина розв'язка.
        /// </summary>
        private static void ValidateFlow(FeudScript script, List<PromoError> errs)
        {
            if (script.Flow.Count == 0) { Add(errs, "flow", "-", PromoErrorCode.BadFlow, "порядок кроків порожній"); return; }
            var seen = new HashSet<string>();
            foreach (var st in script.Flow)
                if (!seen.Add(st.Id)) Add(errs, "flow", st.Id, PromoErrorCode.BadFlow, "крок '" + st.Id + "' повторюється");
            foreach (var p in script.Promos)
                if (!seen.Contains(p.Id)) Add(errs, "flow", p.Id, PromoErrorCode.BadFlow, "промо не входить у порядок кроків");
            foreach (var m in script.Matches)
                if (!seen.Contains(m.Id)) Add(errs, "flow", m.Id, PromoErrorCode.BadFlow, "матч не входить у порядок кроків");
            if (script.Flow[0].IsMatch) Add(errs, "flow", script.Flow[0].Id, PromoErrorCode.BadFlow, "перший крок має бути промо");
            var last = script.Flow[script.Flow.Count - 1];
            if (!last.IsMatch) Add(errs, "flow", last.Id, PromoErrorCode.BadFlow, "останній крок має бути матчем-розв'язкою");
            foreach (var m in script.Matches)
            {
                bool isLast = last.IsMatch && last.Id == m.Id;
                if (m.Decisive && !isLast) Add(errs, "flow", m.Id, PromoErrorCode.BadFlow, "розв'язка — лише останній матч");
                if (!m.Decisive && isLast) Add(errs, "flow", m.Id, PromoErrorCode.BadFlow, "останній матч має бути розв'язкою (decisive)");
            }
        }

        private static void Add(List<PromoError> errs, string promo, string node, PromoErrorCode code, string msg)
        {
            errs.Add(new PromoError { PromoId = promo, NodeId = node, Code = code, Message = msg });
        }

        private static void Text(List<PromoError> errs, IDictionary<string, string> strings, string promo, string node, string key)
        {
            if (key == null) return;
            string v;
            if (strings == null || !strings.TryGetValue(key, out v) || string.IsNullOrEmpty(v))
                Add(errs, promo, node, PromoErrorCode.MissingText, "немає рядка '" + key + "'");
        }

        private static void ValidatePromo(PromoDef p, IDictionary<string, string> strings, List<PromoError> errs)
        {
            Text(errs, strings, p.Id, "-", p.TitleKey);
            Text(errs, strings, p.Id, "-", p.PlaceKey);

            var byId = new Dictionary<string, PromoNode>();
            foreach (var n in p.Nodes)
            {
                if (byId.ContainsKey(n.Id)) Add(errs, p.Id, n.Id, PromoErrorCode.DuplicateNodeId, "повторний id вузла");
                else byId[n.Id] = n;
            }

            if (p.Start == null || !byId.ContainsKey(p.Start))
            {
                Add(errs, p.Id, "-", PromoErrorCode.MissingStart, "стартового вузла '" + p.Start + "' немає");
                return;
            }

            bool hasEnd = false;
            var speakers = new HashSet<string>();
            foreach (var n in p.Nodes)
            {
                if (n.Type == PromoNodeType.End) hasEnd = true;
                foreach (var t in n.Targets())
                    if (t == null || !byId.ContainsKey(t)) Add(errs, p.Id, n.Id, PromoErrorCode.MissingNext, "перехід на неіснуючий вузол '" + t + "'");

                switch (n.Type)
                {
                    case PromoNodeType.Line:
                        if (n.Speaker != null && speakers.Add(n.Speaker)) Text(errs, strings, p.Id, n.Id, "speaker." + n.Speaker);
                        Text(errs, strings, p.Id, n.Id, n.TextKey);
                        break;
                    case PromoNodeType.Choice:
                        if (n.Options.Count == 0) Add(errs, p.Id, n.Id, PromoErrorCode.EmptyChoice, "вибір без варіантів");
                        bool free = false;
                        foreach (var o in n.Options)
                        {
                            if (o.Requires.Count == 0) free = true;
                            if (o.Effects.Count == 0) Add(errs, p.Id, n.Id, PromoErrorCode.OptionWithoutEffect, "варіант '" + o.Id + "' без ефектів");
                            Text(errs, strings, p.Id, n.Id, o.TextKey);
                        }
                        if (!free && n.Options.Count > 0) Add(errs, p.Id, n.Id, PromoErrorCode.NoFreeOption, "жодного варіанта без requires — можливий тупик");
                        break;
                    case PromoNodeType.Check:
                        if (n.Pick10 > n.Rewards.Count) Add(errs, p.Id, n.Id, PromoErrorCode.PickTooMany, "pick10=" + n.Pick10 + " > нагород " + n.Rewards.Count);
                        if (n.Pick7 > n.Rewards.Count) Add(errs, p.Id, n.Id, PromoErrorCode.PickTooMany, "pick7=" + n.Pick7 + " > нагород " + n.Rewards.Count);
                        foreach (var r in n.Rewards) Text(errs, strings, p.Id, n.Id, r.TextKey);
                        break;
                }
            }
            if (!hasEnd) Add(errs, p.Id, "-", PromoErrorCode.NoEnd, "у промо немає вузла end");

            // досяжність зі старту
            var reach = new HashSet<string>();
            var stack = new Stack<string>();
            stack.Push(p.Start);
            reach.Add(p.Start);
            while (stack.Count > 0)
            {
                var n = byId[stack.Pop()];
                foreach (var t in n.Targets())
                    if (t != null && byId.ContainsKey(t) && reach.Add(t)) stack.Push(t);
            }
            foreach (var n in p.Nodes)
                if (!reach.Contains(n.Id)) Add(errs, p.Id, n.Id, PromoErrorCode.Unreachable, "вузол недосяжний зі старту");

            // з кожного вузла досяжний end (зворотний обхід від end)
            var rev = new Dictionary<string, List<string>>();
            foreach (var n in p.Nodes)
                foreach (var t in n.Targets())
                {
                    if (t == null || !byId.ContainsKey(t)) continue;
                    List<string> l;
                    if (!rev.TryGetValue(t, out l)) rev[t] = l = new List<string>();
                    l.Add(n.Id);
                }
            var good = new HashSet<string>();
            var st2 = new Stack<string>();
            foreach (var n in p.Nodes) if (n.Type == PromoNodeType.End && good.Add(n.Id)) st2.Push(n.Id);
            while (st2.Count > 0)
            {
                List<string> preds;
                if (!rev.TryGetValue(st2.Pop(), out preds)) continue;
                foreach (var pr in preds) if (good.Add(pr)) st2.Push(pr);
            }
            foreach (var n in p.Nodes)
                if (!good.Contains(n.Id)) Add(errs, p.Id, n.Id, PromoErrorCode.NoPathToEnd, "з вузла не досяжний end");
        }
    }
}
