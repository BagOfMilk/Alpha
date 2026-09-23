using System.Collections.Generic;

namespace Game.Core.Scenes
{
    /// <summary>Что делает шаг сцены (Поправка №5.8).</summary>
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
        Transition = 4
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

        /// <summary>Кто в кадре или кто говорит — id карточки персонажа.</summary>
        public string ActorId;

        /// <summary>Второй участник двойного плана.</summary>
        public string SecondActorId;

        public ShotFraming Framing = ShotFraming.None;

        /// <summary>Ключ реплики, эффекта или перехода. Не текст.</summary>
        public string Key;

        /// <summary>Длительность паузы в долях секунды — подсказка интерпретатору.</summary>
        public double Seconds;

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
