using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Quests;

namespace Game.Core.Scenes
{
    /// <summary>Що робить крок сцени (Поправка №5.8, Choice — Поправка №7.8).</summary>
    public enum SceneStepKind
    {
        /// <summary>План: хто в кадрі і як.</summary>
        Shot = 0,

        /// <summary>Репліка: хто говорить і за яким ключем підбирається текст.</summary>
        Line = 1,

        /// <summary>Пауза: тримаємо кадр.</summary>
        Beat = 2,

        /// <summary>Ефект: ключ VFX або звуку.</summary>
        Effect = 3,

        /// <summary>Перехід: сцена закінчилася і передає керування.</summary>
        Transition = 4,

        /// <summary>
        /// Вибір репліки (Поправка №7.8): 2-4 варіанти, у кожного — свій ключ,
        /// необов'язкова перевірка і наслідок (<see cref="SceneChoiceOption"/>).
        /// Сцена чекає на цьому кроці, поки <c>GameSession.ChooseSceneOption</c>
        /// не вирішить його — див. <see cref="ScenePlayback.IsAwaitingChoice"/>.
        /// </summary>
        Choice = 5
    }

    /// <summary>
    /// Один варіант вибору репліки (Поправка №7.8): реюзає модель наслідку
    /// квесту (<see cref="QuestConsequence"/>) — ОДИН застосувач наслідків на
    /// квести/сцени/глави арок, як того вимагає власник («выборы в диалогах
    /// или квестах должны влиять это же тоже механики»). Варіант або резолвить
    /// перевірку через існуючий <c>CheckResolver</c> (поріг показаний
    /// заздалегідь, інваріант 8) — виконавець протагоніст, якщо не названий
    /// присутній напарник (<see cref="PerformerCompanionId"/>) — або
    /// застосовує наслідок без перевірки. Веде або на мітку всередині сцени
    /// (<see cref="NextLabel"/>), або завершує сцену переходом
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

        /// <summary>null — виконавець протагоніст; інакше — конкретний напарник (якщо присутній, інакше відкат на протагоніста).</summary>
        public string PerformerCompanionId;

        /// <summary>Наслідок без перевірки (HasCheck=false).</summary>
        public QuestConsequence Consequence;

        /// <summary>Наслідок за полосою результату (HasCheck=true; індекс = (int)OutcomeBand).</summary>
        public QuestConsequence[] ConsequenceByBand;

        /// <summary>Мітка кроку сцени, на який переходимо (див. <see cref="SceneStep.WithLabel"/>). Пусто — варіант не розгалужується всередині сцени.</summary>
        public string NextLabel;

        /// <summary>Ключ переходу, яким варіант одразу завершує сцену. Пусто — варіант веде на <see cref="NextLabel"/> (або продовжує лінійно).</summary>
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

    /// <summary>Як знято план.</summary>
    public enum ShotFraming
    {
        None = 0,
        Close = 1,   // крупний: одне обличчя
        Two = 2,     // подвійний: двоє в кадрі
        Empty = 3    // порожній: місце без людей
    }

    /// <summary>
    /// Один крок сценарію. Тексту тут нема — лише ключі: репліки живуть у
    /// таблицях, як і сигнали, тому письменнику не потрібен програміст.
    /// </summary>
    public sealed class SceneStep
    {
        public SceneStepKind Kind;

        /// <summary>Хто в кадрі або хто говорить — id картки персонажа. Для Choice — id самого вибору (журнал/боти).</summary>
        public string ActorId;

        /// <summary>Другий учасник подвійного плану.</summary>
        public string SecondActorId;

        public ShotFraming Framing = ShotFraming.None;

        /// <summary>Ключ репліки, ефекту або переходу. Не текст.</summary>
        public string Key;

        /// <summary>Тривалість паузи в частках секунди — підказка інтерпретатору.</summary>
        public double Seconds;

        /// <summary>Варіанти вибору (Kind == Choice, Поправка №7.8).</summary>
        public List<SceneChoiceOption> Options;

        /// <summary>
        /// Мітка кроку — ціль розгалуження (<see cref="SceneChoiceOption.NextLabel"/>).
        /// Пусто у більшості кроків: мітки потрібні лише там, куди варіанти
        /// вибору справді стрибають.
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

        /// <summary>Вибір репліки (Поправка №7.8): 2-4 варіанти — <see cref="SceneValidator"/> перевіряє межі.</summary>
        public static SceneStep Choice(string id, List<SceneChoiceOption> options)
            => new SceneStep { Kind = SceneStepKind.Choice, ActorId = id, Options = options ?? new List<SceneChoiceOption>() };

        /// <summary>Позначає крок міткою — ціллю розгалуження варіанта вибору.</summary>
        public SceneStep WithLabel(string label)
        {
            Label = label;
            return this;
        }
    }

    /// <summary>
    /// Портретна сцена як ДАНІ (Поправка №5.8): ядро зберігає і перевіряє
    /// сценарій, Game.Gameplay відтворює його інтерпретатором, консоль
    /// друкує текстом.
    ///
    /// Сенс саме в цьому: сцену можна поставити і перевірити без редактора —
    /// порядок кроків, існування ключів, наявність картки у кожного
    /// учасника. Інтерпретатор пишеться один раз.
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

        /// <summary>Усі, хто з'являється в сцені: за ними перевіряються картки.</summary>
        public List<string> Participants()
        {
            var result = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                // Choice-крок несе в ActorId id самого вибору (не персонажа) —
                // учасником сцени його вважати не можна, інакше валідатор шукав
                // би картку для "myroslava_trust_choice".
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
