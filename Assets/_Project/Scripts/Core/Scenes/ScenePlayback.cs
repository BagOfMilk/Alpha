using System;
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
        private readonly Dictionary<string, int> _labels = new Dictionary<string, int>(StringComparer.Ordinal);
        private int _index = -1;

        private string _actor, _second, _speaker, _line, _effect;
        private ShotFraming _framing = ShotFraming.None;

        private IReadOnlyList<SceneChoiceOption> _pendingOptions;

        public ScenePlayback(Scene scene)
        {
            _steps = scene != null ? scene.Steps : new List<SceneStep>();
            for (int i = 0; i < _steps.Count; i++)
                if (!string.IsNullOrEmpty(_steps[i].Label)) _labels[_steps[i].Label] = i;
        }

        /// <summary>Сцена доиграна: дальше показывать нечего.</summary>
        public bool IsFinished { get; private set; }

        /// <summary>Ключ перехода, которым сцена закончилась (null, пока идёт).</summary>
        public string TransitionKey { get; private set; }

        /// <summary>Сколько держать текущий кадр: пауза задаёт, остальное — мгновенно.</summary>
        public double HoldSeconds { get; private set; }

        /// <summary>
        /// Сцена стоит на выборе реплики (Поправка №7.8) и ждёт
        /// <see cref="Choose"/> — <see cref="Next"/> сам не движется, пока это
        /// true (иначе наивный вызывающий, не умеющий выбирать, тихо
        /// пропустил бы выбор, а не завис бы на нём явно).
        /// </summary>
        public bool IsAwaitingChoice { get; private set; }

        /// <summary>Id текущего шага-выбора (§4.10-подобный ключ для ботов/журнала) — пусто, если не ждём выбора.</summary>
        public string ChoiceId { get; private set; }

        /// <summary>Варианты текущего выбора — пусто, если не ждём выбора.</summary>
        public IReadOnlyList<SceneChoiceOption> PendingOptions => _pendingOptions;

        public SceneFrame Current => new SceneFrame(_actor, _second, _framing, _speaker, _line, _effect);

        /// <summary>
        /// Следующий шаг. Возвращает false, когда сцена кончилась.
        ///
        /// Реплика и эффект живут ровно один шаг — это события; план держится,
        /// пока его не сменит другой. Остановившись на выборе, повторные
        /// вызовы возвращают тот же кадр, пока не придёт <see cref="Choose"/>.
        /// </summary>
        public bool Next()
        {
            if (IsAwaitingChoice) return true;

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

                    case SceneStepKind.Choice:
                        ChoiceId = step.ActorId;
                        _pendingOptions = step.Options;
                        IsAwaitingChoice = true;
                        return true;

                    case SceneStepKind.Transition:
                        TransitionKey = step.Key;
                        IsFinished = true;
                        return false;
                }
            }
        }

        /// <summary>
        /// Разрешает текущий выбор (только когда <see cref="IsAwaitingChoice"/>):
        /// вариант с <see cref="SceneChoiceOption.TransitionKey"/> завершает
        /// сцену на месте (как обычный Transition-шаг); вариант с
        /// <see cref="SceneChoiceOption.NextLabel"/> прыгает на метку; вариант
        /// без обоих продолжает сцену линейно со следующего шага. Наслідок и
        /// проверку резолвит вызывающий (GameSession) ДО этого вызова — сама
        /// сцена ни того, ни другого не знает.
        /// </summary>
        public void Choose(int optionIndex)
        {
            if (!IsAwaitingChoice) throw new InvalidOperationException("Сцена не стоит на выборе.");
            var options = _pendingOptions;
            if (options == null || optionIndex < 0 || optionIndex >= options.Count)
                throw new ArgumentOutOfRangeException(nameof(optionIndex));

            var option = options[optionIndex];
            IsAwaitingChoice = false;
            _pendingOptions = null;

            if (!string.IsNullOrEmpty(option.TransitionKey))
            {
                TransitionKey = option.TransitionKey;
                IsFinished = true;
                return;
            }

            if (!string.IsNullOrEmpty(option.NextLabel))
            {
                int target;
                if (_labels.TryGetValue(option.NextLabel, out target))
                {
                    // Next() увеличит индекс перед тем, как прочитать шаг —
                    // ставим на "предыдущий перед целью", а не на саму цель.
                    _index = target - 1;
                }
            }
            // Ни того, ни другого — сцена просто продолжает со следующего
            // шага после Choice (индекс уже на нём).
        }
    }
}
