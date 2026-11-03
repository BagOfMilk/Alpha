using System;
using System.Collections.Generic;

namespace Game.Core.Ring
{
    /// <summary>
    /// Типи вузлів. Branch — автоматичний перехід за умовами (без вибору гравця): перший випадок, чиї умови виконані,
    /// інакше next. Раннер проходить його сам, тож у Current його ніколи не видно (так промо після матчу читає результат).
    /// </summary>
    public enum PromoNodeType { Line = 0, Choice = 1, Check = 2, End = 3, Branch = 4 }

    /// <summary>Випадок вузла branch: умови (усі) → вузол.</summary>
    public sealed class PromoBranchCase
    {
        public List<FeudCondition> Requires = new List<FeudCondition>();
        public string Next;
    }

    /// <summary>Варіант вибору (gdd §4.1).</summary>
    public sealed class PromoOption
    {
        public string Id;
        public string TextKey;
        public List<FeudCondition> Requires = new List<FeudCondition>();
        public List<FeudEffect> Effects = new List<FeudEffect>();
        public string Next;
        /// <summary>Не показується, доки умови не виконані.</summary>
        public bool Hidden;
        /// <summary>Позначка шляху біля репліки (лише UI), якщо задана.</summary>
        public RingPath? Path;
    }

    /// <summary>Нагорода кидка промо.</summary>
    public sealed class PromoReward
    {
        public string Id;
        public string TextKey;
        public List<FeudEffect> Effects = new List<FeudEffect>();
    }

    /// <summary>Вузол графа промо: line / choice / check / end.</summary>
    public sealed class PromoNode
    {
        public string Id;
        public PromoNodeType Type;
        // line
        public string Speaker;
        public string TextKey;
        public string Next;
        // choice
        public List<PromoOption> Options = new List<PromoOption>();
        // check
        public RingAttr Attr;
        public int Mod;
        public int Pick10;
        public int Pick7;
        public List<PromoReward> Rewards = new List<PromoReward>();
        public List<FeudEffect> Fail = new List<FeudEffect>();
        public string Next6;
        // branch (Next — за замовчуванням)
        public List<PromoBranchCase> Cases = new List<PromoBranchCase>();

        /// <summary>Усі переходи вузла (для перевірок графа).</summary>
        public IEnumerable<string> Targets()
        {
            switch (Type)
            {
                case PromoNodeType.Line:
                    yield return Next;
                    break;
                case PromoNodeType.Choice:
                    foreach (var o in Options) yield return o.Next;
                    break;
                case PromoNodeType.Check:
                    yield return Next;
                    if (Next6 != null) yield return Next6;
                    break;
                case PromoNodeType.Branch:
                    foreach (var c in Cases) yield return c.Next;
                    yield return Next;
                    break;
            }
        }
    }

    /// <summary>Одне промо — окремий граф зі стартовим вузлом.</summary>
    public sealed class PromoDef
    {
        public string Id;
        public int Act;
        public string TitleKey;
        public string PlaceKey;
        public string Start;
        public List<PromoNode> Nodes = new List<PromoNode>();

        public PromoNode Find(string id)
        {
            foreach (var n in Nodes) if (n.Id == id) return n;
            return null;
        }
    }

    /// <summary>Правило плану матчу за замовчуванням: перше, чиї умови виконані (gdd §5.2). Діє, лише якщо промо не задали план.</summary>
    public sealed class PlanRule
    {
        public List<FeudCondition> Requires = new List<FeudCondition>();
        public BookerPlan Plan;
    }

    /// <summary>
    /// Матч ворожнеччі в даних (gdd §5.2): id (m1, m2, m3 — від нього прапори результату), акт, плани за замовчуванням,
    /// умови за замовчуванням, чи розв'язка (лише останній).
    /// </summary>
    public sealed class MatchDef
    {
        public string Id;
        public int Act;
        public List<PlanRule> Plans = new List<PlanRule>();
        public MatchStipulations Stipulations;
        public bool Decisive;
    }

    /// <summary>Крок порядку ворожнечі: промо або матч (за індексом у Promos/Matches).</summary>
    public sealed class FlowStep
    {
        public bool IsMatch;
        public string Id;
        /// <summary>Індекс у FeudScript.Matches (IsMatch) або Promos.</summary>
        public int Index;
    }

    /// <summary>Початкові значення змінних ворожнечі.</summary>
    public sealed class FeudStart
    {
        public int Audience = 3;
        public int Heat;
        public int Momentum;
        public int Trust = 2;
        public int Locker;
    }

    /// <summary>Усе, що описує ворожнечу в даних: люди, старт, промо (gdd §4.3).</summary>
    public sealed class FeudScript
    {
        public string Id;
        public string TitleKey;
        public WrestlerStats Player;
        public WrestlerStats Rival;
        public int RivalStartMomentum;
        public FeudStart Start = new FeudStart();
        public List<PromoDef> Promos = new List<PromoDef>();

        /// <summary>
        /// Матчі й порядок кроків (gdd §5.2): промо p1 → матч m1 → … Після кожного матчу — розвиток, після розв'язки ще й кінцівка
        /// (вони неявні). Немає matches/flow у даних — старий порядок: усі промо, потім один матч-розв'язка.
        /// </summary>
        public List<MatchDef> Matches = new List<MatchDef>();
        public List<FlowStep> Flow = new List<FlowStep>();

        public MatchDef FindMatch(string id)
        {
            foreach (var m in Matches) if (m.Id == id) return m;
            return null;
        }

        public PromoDef FindPromo(string id)
        {
            foreach (var p in Promos) if (p.Id == id) return p;
            return null;
        }

        /// <summary>Свіжий стан ворожнечі зі стартових значень сценарію.</summary>
        public FeudState NewState()
        {
            var s = new FeudState(Player.Clone());
            s.Audience = Start.Audience;
            s.Heat = Start.Heat;
            s.Momentum = Start.Momentum;
            s.Trust = Start.Trust;
            s.Locker = Start.Locker;
            return s;
        }
    }
}
