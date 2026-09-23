using System.Collections.Generic;

namespace Game.Core.Scenes
{
    /// <summary>Что сейчас на экране: результат проигрывания, а не сам сценарий.</summary>
    public readonly struct SceneFrame
    {
        /// <summary>Кто в кадре (может быть пусто — пустой план).</summary>
        public readonly string ActorId;
        public readonly string SecondActorId;
        public readonly ShotFraming Framing;

        /// <summary>Кто говорит сейчас и ключ реплики (пусто — реплики нет).</summary>
        public readonly string SpeakerId;
        public readonly string LineKey;

        /// <summary>Ключ эффекта, сработавшего на этом шаге.</summary>
        public readonly string EffectKey;

        public SceneFrame(string actorId, string secondActorId, ShotFraming framing,
            string speakerId, string lineKey, string effectKey)
        {
            ActorId = actorId;
            SecondActorId = secondActorId;
            Framing = framing;
            SpeakerId = speakerId;
            LineKey = lineKey;
            EffectKey = effectKey;
        }
    }

    /// <summary>
    /// Проигрывание портретной сцены (Поправка №5.8).
    ///
    /// Живёт в ядре, а не в движке, по той же причине, по какой сцена — данные:
    /// «кто сейчас в кадре и что он говорит» проверяется тестом без редактора.
    /// Движку остаётся нарисовать кадр, консоли — напечатать его.
    ///
    /// План ДЕРЖИТСЯ, пока не сменится: реплика не стирает кадр, иначе после
    /// первой же фразы говорящий исчезал бы с экрана.
    /// </summary>
    public sealed class ScenePlayback
    {
        private readonly IReadOnlyList<SceneStep> _steps;
        private int _index = -1;

        private string _actor, _second, _speaker, _line, _effect;
        private ShotFraming _framing = ShotFraming.None;

        public ScenePlayback(Scene scene)
        {
            _steps = scene != null ? scene.Steps : new List<SceneStep>();
        }

        /// <summary>Сцена доиграна: дальше показывать нечего.</summary>
        public bool IsFinished { get; private set; }

        /// <summary>Ключ перехода, которым сцена закончилась (null, пока идёт).</summary>
        public string TransitionKey { get; private set; }

        /// <summary>Сколько держать текущий кадр: пауза задаёт, остальное — мгновенно.</summary>
        public double HoldSeconds { get; private set; }

        public SceneFrame Current => new SceneFrame(_actor, _second, _framing, _speaker, _line, _effect);

        /// <summary>
        /// Следующий шаг. Возвращает false, когда сцена кончилась.
        ///
        /// Реплика и эффект живут ровно один шаг — это события; план держится,
        /// пока его не сменит другой.
        /// </summary>
        public bool Next()
        {
            _speaker = null;
            _line = null;
            _effect = null;
            HoldSeconds = 0;

            while (true)
            {
                _index++;
                if (_index >= _steps.Count)
                {
                    IsFinished = true;
                    return false;
                }

                var step = _steps[_index];
                switch (step.Kind)
                {
                    case SceneStepKind.Shot:
                        _actor = step.ActorId;
                        _second = step.SecondActorId;
                        _framing = step.Framing;
                        return true;

                    case SceneStepKind.Line:
                        _speaker = step.ActorId;
                        _line = step.Key;
                        return true;

                    case SceneStepKind.Beat:
                        HoldSeconds = step.Seconds;
                        return true;

                    case SceneStepKind.Effect:
                        _effect = step.Key;
                        return true;

                    case SceneStepKind.Transition:
                        TransitionKey = step.Key;
                        IsFinished = true;
                        return false;
                }
            }
        }
    }
}
