using System;
using System.Collections.Generic;
using Game.Core.Randomness;
using Game.Core.Ring;

namespace Game.Gameplay.Ring.Models
{
    public enum PromoViewKind
    {
        /// <summary>Репліка: мовець і текст, «Далі».</summary>
        Line = 0,
        /// <summary>Вибір реплік гравця.</summary>
        Choice = 1,
        /// <summary>Панель кидка до кидка: атрибут, модифікатор, три відсотки.</summary>
        Check = 2,
        /// <summary>Кидок зроблено, треба обрати нагороди.</summary>
        Rewards = 3,
        /// <summary>Кидок без вибору нагород (провал чи нічого обирати): показ результату, «Далі».</summary>
        Result = 4,
        /// <summary>Кінець промо.</summary>
        Finished = 5
    }

    /// <summary>Рядок вибору: номер для клавіші, текст, сірий з причиною, значок шляху.</summary>
    public sealed class PromoChoiceRow
    {
        public int Number;
        public string Id;
        public string Text;
        public bool Locked;
        /// <summary>Причина блокування текстом (порожньо, якщо доступний).</summary>
        public string ReasonText = "";
        public bool HasPath;
        public RingPath Path;
        /// <summary>Значок шляху: Г / Л / Б (порожньо, якщо шляху нема).</summary>
        public string PathMark = "";
    }

    public sealed class PromoRewardRow
    {
        public int Number;
        public string Id;
        public string Text;
        public bool Selected;
    }

    /// <summary>Результат кидка для показу: два кубики, модифікатор, сума, ступінь.</summary>
    public sealed class RollResultView
    {
        public int Die1, Die2, Modifier, Total;
        public RollTier Tier;
        public string TierText;
        public string TotalText;
    }

    /// <summary>
    /// Модель екрана промо (gdd §4): що показати зараз і які команди доступні. Читає PromoRunner, не змінює правил;
    /// рядки — з RingText. Без UnityEngine — тестується в .NET.
    /// </summary>
    public sealed class PromoScreenModel
    {
        private readonly PromoRunner _runner;
        private readonly RingText _t;
        private readonly HashSet<string> _picked = new HashSet<string>();
        private RollResultView _result;

        public PromoScreenModel(PromoRunner runner, RingText text)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _t = text ?? throw new ArgumentNullException(nameof(text));
        }

        public PromoRunner Runner { get { return _runner; } }

        public string TitleText { get { return _t.Get(_runner.Promo.TitleKey); } }
        public string PlaceText { get { return _t.Get(_runner.Promo.PlaceKey); } }
        public int Act { get { return _runner.Promo.Act; } }

        public PromoViewKind Kind
        {
            get
            {
                if (_result != null && !_runner.AwaitingRewards) return PromoViewKind.Result;
                if (_runner.IsFinished) return PromoViewKind.Finished;
                switch (_runner.Current.Type)
                {
                    case PromoNodeType.Line: return PromoViewKind.Line;
                    case PromoNodeType.Choice: return PromoViewKind.Choice;
                    default: return _runner.AwaitingRewards ? PromoViewKind.Rewards : PromoViewKind.Check;
                }
            }
        }

        // ---------- репліка ----------

        /// <summary>Мовець поточної репліки (ім'я); порожньо, якщо вузол не репліка.</summary>
        public string SpeakerText
        {
            get
            {
                var n = _runner.Current;
                if (n.Type != PromoNodeType.Line || string.IsNullOrEmpty(n.Speaker)) return "";
                return _t.Get("speaker." + n.Speaker);
            }
        }

        public bool IsNarrator { get { return _runner.Current.Type == PromoNodeType.Line && _runner.Current.Speaker == "narrator"; } }

        public bool IsPlayerSpeaker { get { return _runner.Current.Type == PromoNodeType.Line && _runner.Current.Speaker == "player"; } }

        public string LineText
        {
            get
            {
                var n = _runner.Current;
                return n.Type == PromoNodeType.Line ? _t.Get(n.TextKey) : "";
            }
        }

        // ---------- вибір ----------

        public List<PromoChoiceRow> Choices()
        {
            var rows = new List<PromoChoiceRow>();
            if (Kind != PromoViewKind.Choice) return rows;
            int i = 1;
            foreach (var v in _runner.AvailableOptions())
            {
                var row = new PromoChoiceRow
                {
                    Number = i++,
                    Id = v.Option.Id,
                    Text = _t.Get(v.Option.TextKey),
                    Locked = v.Locked
                };
                if (v.Locked) row.ReasonText = ReasonText(_t, v.Reason);
                if (v.Option.Path.HasValue)
                {
                    row.HasPath = true;
                    row.Path = v.Option.Path.Value;
                    row.PathMark = RingLabels.PathMark(_t, row.Path);
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>Причина блокування варіанта текстом: перша невиконана умова (інваріант: поріг видно заздалегідь).</summary>
        public static string ReasonText(RingText t, FeudCondition c)
        {
            if (c == null) return "";
            switch (c.Kind)
            {
                case ConditionKind.Attr:
                    RingAttr a;
                    string an = FeudVars.TryAttr(c.Key, out a) ? RingLabels.AttrName(t, a) : c.Key;
                    return t.Format("reason.attr", an, c.Min);
                case ConditionKind.Var:
                    return t.Format("reason.var", RingLabels.Var(t, c.Key), c.Min);
                case ConditionKind.Path:
                    RingPath p;
                    string pn = FeudVars.TryPath(c.Key, out p) ? RingLabels.PathName(t, p) : c.Key;
                    return t.Format("reason.path", pn, c.Min);
                case ConditionKind.Flag: return Specific(t, "reason.flag.", c.Key, "reason.flag");
                case ConditionKind.NotFlag: return Specific(t, "reason.notflag.", c.Key, "reason.notflag");
                case ConditionKind.Open: return Specific(t, "reason.open.", c.Key, "reason.open");
                default: return t.Get("reason.unknown");
            }
        }

        // Для прапорів і гілок — окрема причина «людською», якщо є; інакше загальна.
        private static string Specific(RingText t, string prefix, string key, string generic)
        {
            return t.Has(prefix + key) ? t.Get(prefix + key) : t.Get(generic);
        }

        /// <summary>Вибрати варіант за id; false — недоступний чи невідомий.</summary>
        public bool Choose(string optionId)
        {
            if (Kind != PromoViewKind.Choice) return false;
            foreach (var v in _runner.AvailableOptions())
                if (v.Option.Id == optionId)
                {
                    if (v.Locked) return false;
                    _runner.Choose(optionId);
                    return true;
                }
            return false;
        }

        /// <summary>Вибрати варіант за номером 1…N (клавіша); false — сірий чи немає такого.</summary>
        public bool ChooseNumber(int number)
        {
            foreach (var r in Choices()) if (r.Number == number) return Choose(r.Id);
            return false;
        }

        // ---------- репліка → далі ----------

        public bool CanAdvance { get { return Kind == PromoViewKind.Line; } }

        public void Advance()
        {
            if (Kind != PromoViewKind.Line) return;
            _runner.Advance();
        }

        // ---------- кидок ----------

        public string CheckAttrText { get { return _runner.Current.Type == PromoNodeType.Check ? RingLabels.AttrName(_t, _runner.Current.Attr) : ""; } }

        public int CheckModifier { get { return Kind == PromoViewKind.Check ? _runner.CheckModifier() : 0; } }

        public string CheckModifierText { get { return RingChances.Signed(CheckModifier); } }

        /// <summary>Три відсотки до кидка [провал, 7–9, 10+], сума 100.</summary>
        public int[] ChancePercents()
        {
            return Kind == PromoViewKind.Check ? RingChances.Percent(_runner.PreviewChances()) : new int[3];
        }

        public bool CanRoll { get { return Kind == PromoViewKind.Check; } }

        /// <summary>Кинути. Результат лишається в <see cref="Result"/>, поки гравець не підтвердить (провал) чи не обере нагороди.</summary>
        public RollResultView Roll(IDiceRoller dice)
        {
            if (Kind != PromoViewKind.Check) throw new InvalidOperationException("Кидати нічого");
            int mod = _runner.CheckModifier();
            var tier = _runner.RollCheck(dice);
            _result = new RollResultView
            {
                Die1 = _runner.LastDie1,
                Die2 = _runner.LastDie2,
                Modifier = mod,
                Total = _runner.LastTotal,
                Tier = tier,
                TierText = RingLabels.TierName(_t, tier),
                TotalText = _t.Format("promo.roll.total", _runner.LastDie1, _runner.LastDie2, RingChances.Term(mod), _runner.LastTotal)
            };
            _picked.Clear();
            return _result;
        }

        public RollResultView Result { get { return _result; } }

        /// <summary>«Далі» після показу результату без вибору нагород.</summary>
        public void AcknowledgeResult()
        {
            if (Kind == PromoViewKind.Result) _result = null;
        }

        // ---------- нагороди ----------

        public int RewardsToPick { get { return Kind == PromoViewKind.Rewards ? _runner.RewardsToPick : 0; } }

        public int RewardsPicked { get { return _picked.Count; } }

        public List<PromoRewardRow> Rewards()
        {
            var rows = new List<PromoRewardRow>();
            if (Kind != PromoViewKind.Rewards) return rows;
            int i = 1;
            foreach (var r in _runner.Current.Rewards)
                rows.Add(new PromoRewardRow { Number = i++, Id = r.Id, Text = _t.Get(r.TextKey), Selected = _picked.Contains(r.Id) });
            return rows;
        }

        /// <summary>Поставити/зняти позначку нагороди; зайву понад ліміт не ставить.</summary>
        public bool ToggleReward(string id)
        {
            if (Kind != PromoViewKind.Rewards) return false;
            if (_picked.Contains(id)) { _picked.Remove(id); return true; }
            if (_picked.Count >= _runner.RewardsToPick) return false;
            bool known = false;
            foreach (var r in _runner.Current.Rewards) if (r.Id == id) known = true;
            if (!known) return false;
            _picked.Add(id);
            return true;
        }

        public bool ToggleRewardNumber(int number)
        {
            foreach (var r in Rewards()) if (r.Number == number) return ToggleReward(r.Id);
            return false;
        }

        public bool CanConfirmRewards { get { return Kind == PromoViewKind.Rewards && _picked.Count == _runner.RewardsToPick; } }

        public void ConfirmRewards()
        {
            if (!CanConfirmRewards) throw new InvalidOperationException("Оберіть рівно " + RewardsToPick + " нагород");
            var ids = new List<string>(_picked);
            _runner.ChooseRewards(ids);
            _picked.Clear();
            _result = null;
        }

        public bool IsFinished { get { return Kind == PromoViewKind.Finished; } }
    }
}
