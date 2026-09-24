using System;
using Game.Core.Balance;
using Game.Core.Checks;

namespace Game.Core.Quests
{
    public enum QuestState { Active = 0, Succeeded = 1, Failed = 2 }

    /// <summary>
    /// Що сталося на кроці квесту — для D1/логу. Наслідок — ДАНІ
    /// (<see cref="QuestConsequence"/>), рушій сам нічого не застосовує (R6).
    /// </summary>
    public sealed class QuestStepReport
    {
        public string QuestId;
        public string StageId;
        public QuestStageKind Kind;

        /// <summary>Прийнято чи ні (недопустимий варіант вибору/чужий тип етапу).</summary>
        public bool Accepted = true;

        // ---- Check ----
        public OutcomeBand Band;
        public bool HasCandidate;
        public string ResolvedActorId;

        // ---- Choice ----
        public int ChosenOption = -1;

        public int NextIndex = -1;
        public bool Terminal;
        public bool Succeeded;

        public QuestConsequence Consequence = QuestConsequence.Empty();
    }

    /// <summary>
    /// Проходження квесту — детермінований автомат по етапах (R6).
    ///
    /// Перевірки йдуть через ІСНУЮЧИЙ <see cref="CheckResolver"/> (той самий,
    /// що й інциденти) — 4 полоси з першого рядка, жодного бінарного
    /// успіху/провалу. Вибір застосовує гейт (за прапором) і повертає
    /// наслідок варіанту. Термінальний етап (Outcome) додає свій власний
    /// наслідок (нагороду) до наслідку переходу, який на нього привів.
    ///
    /// Рушій НІЧОГО не застосовує сам: ні Напругу, ні фракції, ні лояльність,
    /// ні прапори — усе це повертається як <see cref="QuestConsequence"/>, і
    /// застосовує його D1 (GameSession), коли з'явиться.
    /// </summary>
    public sealed class QuestRun
    {
        public QuestDefinition Def { get; }
        public int CurrentIndex { get; private set; }
        public QuestState State { get; private set; } = QuestState.Active;

        public QuestRun(QuestDefinition def)
        {
            Def = def ?? throw new ArgumentNullException(nameof(def));
            CurrentIndex = def.StartIndex;
        }

        public QuestStage Current => Def.StageAt(CurrentIndex);
        public bool IsActive => State == QuestState.Active;

        /// <summary>Відновлення прогону з сейву (QuestLog): етап і стан — як були.</summary>
        internal void RestoreTo(int index, QuestState state)
        {
            CurrentIndex = index;
            State = state;
        }

        /// <summary>
        /// Резолв етапу-перевірки. Порог — той самий, що бачив би гравець у
        /// прев'ю (CheckResolver.Preview і Resolve рахують його однаково,
        /// інваріант 8) — окремого прев'ю тут не показано: показ порогу
        /// заздалегідь — робота View-шару (D1), рушій лише резолвить.
        /// </summary>
        public QuestStepReport ResolveCheck(IRosterView roster, IRepeatTracker repeats, int day, BalanceConfig balance)
        {
            var stage = Current;
            var report = NewReport(stage);

            if (stage == null || stage.Kind != QuestStageKind.Check)
            {
                report.Accepted = false;
                return report;
            }

            string topic = string.IsNullOrEmpty(stage.TopicId) ? Def.Id + "." + stage.Id : stage.TopicId;
            var request = new CheckRequest(stage.CheckSkill, stage.Threshold, stage.Approach,
                topic, stage.RequiredPositionId);
            var outcome = CheckResolver.Resolve(request, roster, repeats, day, balance);

            report.Band = outcome.Band;
            report.HasCandidate = !outcome.WasUnmanned;
            report.ResolvedActorId = outcome.ActorId;

            int bandIndex = (int)outcome.Band;
            int next = stage.NextByBand != null && stage.NextByBand.Length > bandIndex ? stage.NextByBand[bandIndex] : -1;
            var consequence = stage.ConsequenceByBand != null && stage.ConsequenceByBand.Length > bandIndex
                ? stage.ConsequenceByBand[bandIndex] : null;

            GoTo(next, report, consequence);
            return report;
        }

        /// <summary>Резолв етапу-вибору. Недоступний (гейт) варіант квест не рухає.</summary>
        public QuestStepReport Choose(int optionIndex, Story.StoryFlags flags = null)
        {
            var stage = Current;
            var report = NewReport(stage);

            if (stage == null || stage.Kind != QuestStageKind.Choice)
            {
                report.Accepted = false;
                return report;
            }
            if (optionIndex < 0 || optionIndex >= stage.Options.Count)
            {
                report.Accepted = false;
                return report;
            }

            var option = stage.Options[optionIndex];
            if (!option.IsAvailable(flags))
            {
                report.Accepted = false;
                return report;
            }

            report.ChosenOption = optionIndex;
            GoTo(option.Next, report, option.Consequence);
            return report;
        }

        private void GoTo(int nextIndex, QuestStepReport report, QuestConsequence transitionConsequence)
        {
            var consequence = transitionConsequence ?? QuestConsequence.Empty();

            if (nextIndex < 0 || nextIndex >= Def.Stages.Count)
            {
                // Недопустимий перехід тримає квест на місці, а не валить стек:
                // контент зобов'язаний пройти QuestDefinition.Validate() ДО того,
                // як потрапить у гру — тут це лише останній рубіж захисту.
                report.Accepted = false;
                report.Consequence = consequence;
                return;
            }

            CurrentIndex = nextIndex;
            report.NextIndex = nextIndex;

            var next = Def.StageAt(nextIndex);
            if (next != null && next.IsTerminal)
            {
                State = next.Success ? QuestState.Succeeded : QuestState.Failed;
                report.Terminal = true;
                report.Succeeded = next.Success;
                consequence = QuestConsequence.Merge(consequence, next.Consequence);
            }

            report.Consequence = consequence;
        }

        private QuestStepReport NewReport(QuestStage stage) => new QuestStepReport
        {
            QuestId = Def.Id,
            StageId = stage != null ? stage.Id : null,
            Kind = stage != null ? stage.Kind : QuestStageKind.Outcome
        };
    }
}
