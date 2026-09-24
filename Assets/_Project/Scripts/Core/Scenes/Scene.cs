using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Quests;

namespace Game.Core.Scenes
{
    /// <summary>Что делает шаг сцены (Поправка №5.8, Choice — Поправка №7.8).</summary>
    public enum SceneStepKind
    {
        /// <summary>План: кто в кадре и как.</summary>
        Shot = 0,

        /// <summary>Реплика: кто говорит и по какому ключу подбирается текст.</summary>
        Line = 1,

        /// <summary>Пауза: держим кадр.</summary>
        Beat = 2,

        /// <summary>Эффект: ключ VFX или звука.</summary>
        Effect = 3,

        /// <summary>Переход: сцена кончилась и передаёт управление.</summary>
        Transition = 4,

        /// <summary>
        /// Выбор реплики (Поправка №7.8): 2-4 варианта, у каждого — свой ключ,
        /// необязательная проверка и наслідок (<see cref="SceneChoiceOption"/>).
        /// Сцена ждёт на этом шаге, пока <c>GameSession.ChooseSceneOption</c>
        /// не разрешит его — см. <see cref="ScenePlayback.IsAwaitingChoice"/>.
        /// </summary>
        Choice = 5
    }

    /// <summary>
    /// Один вариант выбора реплики (Поправка №7.8): реюзает модель наслідку
    /// квеста (<see cref="QuestConsequence"/>) — ОДИН применятель наслідков на
    /// квесты/сцены/главы арок, как того требует владелец («выборы в диалогах
    /// или квестах должны влиять это же тоже механики»). Вариант либо резолвит
    /// проверку через существующий <c>CheckResolver</c> (порог показан
    /// заранее, инвариант 8) — исполнитель протагонист, если не назван
    /// присутствующий напарник (<see cref="PerformerCompanionId"/>) — либо
    /// применяет наслідок без проверки. Ведёт либо на метку внутри сцены
    /// (<see cref="NextLabel"/>), либо завершает сцену переходом
    /// (<see cref="TransitionKey"/>).
    /// </summary>
    public sealed class SceneChoiceOption
    {
        public string Id;
        public string TextKey;

        public bool HasCheck;
        public SkillKey CheckSkill;
        public int Threshold;
        public ApproachForm Approach = ApproachForm.Neutral;

        /// <summary>null — исполнитель протагонист; иначе — конкретный напарник (если присутствует, иначе откат на протагониста).</summary>
        public string PerformerCompanionId;

        /// <summary>Наслідок без проверки (HasCheck=false).</summary>
        public QuestConsequence Consequence;

        /// <summary>Наслідок по полосе исхода (HasCheck=true; индекс = (int)OutcomeBand).</summary>
        public QuestConsequence[] ConsequenceByBand;

        /// <summary>Метка шага сцены, на который переходим (см. <see cref="SceneStep.WithLabel"/>). Пусто — вариант не ветвится внутри сцены.</summary>
        public string NextLabel;

        /// <summary>Ключ перехода, которым вариант сразу завершает сцену. Пусто — вариант ведёт на <see cref="NextLabel"/> (или продолжает линейно).</summary>
        public string TransitionKey;

        public static SceneChoiceOption Simple(string id, string textKey, QuestConsequence consequence,
            string nextLabel = null, string transitionKey = null)
        {
            return new SceneChoiceOption
            {
                Id = id,
                TextKey = textKey,
                HasCheck = false,
                Consequence = consequence ?? QuestConsequence.Empty(),
                NextLabel = nextLabel,
                TransitionKey = transitionKey
            };
        }

        public static SceneChoiceOption WithCheck(string id, string textKey, SkillKey skill, int threshold,
            ApproachForm approach, QuestConsequence[] consequenceByBand,
            string performerCompanionId = null, string nextLabel = null, string transitionKey = null)
        {
            return new SceneChoiceOption
            {
                Id = id,
                TextKey = textKey,
                HasCheck = true,
                CheckSkill = skill,
                Threshold = threshold,
                Approach = approach,
                PerformerCompanionId = performerCompanionId,
                ConsequenceByBand = consequenceByBand ?? new QuestConsequence[4],
                NextLabel = nextLabel,
                TransitionKey = transitionKey
            };
        }
    }

    /// <summary>Как снят план.</summary>
    public enum ShotFraming
    {
        None = 0,
        Close = 1,   // крупный: одно лицо
        Two = 2,     // двойной: двое в кадре
        Empty = 3    // пустой: место без людей
    }

    /// <summary>
    /// Один шаг сценария. Текста здесь нет — только ключи: реплики живут в
    /// таблицах, как и сигналы, поэтому писателю не нужен программист.
    /// </summary>
    public sealed class SceneStep
    {
        public SceneStepKind Kind;

        /// <summary>Кто в кадре или кто говорит — id карточки персонажа. Для Choice — id самого выбора (журнал/боты).</summary>
        public string ActorId;

        /// <summary>Второй участник двойного плана.</summary>
        public string SecondActorId;

        public ShotFraming Framing = ShotFraming.None;

        /// <summary>Ключ реплики, эффекта или перехода. Не текст.</summary>
        public string Key;

        /// <summary>Длительность паузы в долях секунды — подсказка интерпретатору.</summary>
        public double Seconds;

        /// <summary>Варианты выбора (Kind == Choice, Поправка №7.8).</summary>
        public List<SceneChoiceOption> Options;

        /// <summary>
        /// Метка шага — цель ветвления (<see cref="SceneChoiceOption.NextLabel"/>).
        /// Пусто у большинства шагов: метки нужны только там, куда варианты
        /// выбора действительно прыгают.
        /// </summary>
        public string Label;

        public static SceneStep Shot(string actorId, ShotFraming framing, string secondActorId = null)
            => new SceneStep { Kind = SceneStepKind.Shot, ActorId = actorId, Framing = framing, SecondActorId = secondActorId };

        public static SceneStep Line(string actorId, string key)
            => new SceneStep { Kind = SceneStepKind.Line, ActorId = actorId, Key = key };

        public static SceneStep Beat(double seconds = 1.0)
            => new SceneStep { Kind = SceneStepKind.Beat, Seconds = seconds };

        public static SceneStep Effect(string key)
            => new SceneStep { Kind = SceneStepKind.Effect, Key = key };

        public static SceneStep Transition(string key)
            => new SceneStep { Kind = SceneStepKind.Transition, Key = key };

        /// <summary>Выбор реплики (Поправка №7.8): 2-4 варианта — <see cref="SceneValidator"/> проверяет границы.</summary>
        public static SceneStep Choice(string id, List<SceneChoiceOption> options)
            => new SceneStep { Kind = SceneStepKind.Choice, ActorId = id, Options = options ?? new List<SceneChoiceOption>() };

        /// <summary>Помечает шаг меткой — целью ветвления варианта выбора.</summary>
        public SceneStep WithLabel(string label)
        {
            Label = label;
            return this;
        }
    }

    /// <summary>
    /// Портретная сцена как ДАННЫЕ (Поправка №5.8): ядро хранит и проверяет
    /// сценарий, Game.Gameplay воспроизводит его интерпретатором, консоль
    /// печатает текстом.
    ///
    /// Смысл ровно в этом: сцену можно поставить и проверить без редактора —
    /// порядок шагов, существование ключей, наличие карточки у каждого
    /// участника. Интерпретатор пишется один раз.
    /// </summary>
    public sealed class Scene
    {
        public string Id;
        public string TitleKey;

        public readonly List<SceneStep> Steps = new List<SceneStep>();

        public Scene() { }

        public Scene(string id, string titleKey = null)
        {
            Id = id;
            TitleKey = titleKey;
        }

        public Scene Step(SceneStep step)
        {
            if (step != null) Steps.Add(step);
            return this;
        }

        /// <summary>Все, кто появляется в сцене: по ним проверяются карточки.</summary>
        public List<string> Participants()
        {
            var result = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                // Choice-шаг несёт в ActorId id самого выбора (не персонажа) —
                // участником сцены его считать нельзя, иначе валидатор искал
                // бы карточку для "myroslava_trust_choice".
                if (Steps[i].Kind == SceneStepKind.Choice) continue;
                Add(result, Steps[i].ActorId);
                Add(result, Steps[i].SecondActorId);
            }
            return result;
        }

        private static void Add(List<string> into, string id)
        {
            if (!string.IsNullOrEmpty(id) && !into.Contains(id)) into.Add(id);
        }
    }
}
