using System;
using System.Collections.Generic;

namespace Game.Core.Ring
{
    public enum PromoNodeType { Line = 0, Choice = 1, Check = 2, End = 3 }

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
