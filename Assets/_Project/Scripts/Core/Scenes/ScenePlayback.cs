using System;
using System.Collections.Generic;

namespace Game.Core.Scenes
{
    /// <summary>Що зараз на екрані: результат програвання, а не сам сценарій.</summary>
    public readonly struct SceneFrame
    {
        /// <summary>Хто в кадрі (може бути пусто — порожній план).</summary>
        public readonly string ActorId;
        public readonly string SecondActorId;
        public readonly ShotFraming Framing;

        /// <summary>Хто говорить зараз і ключ репліки (пусто — репліки нема).</summary>
        public readonly string SpeakerId;
        public readonly string LineKey;

        /// <summary>Ключ ефекту, що спрацював на цьому кроці.</summary>
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
    /// Програвання портретної сцени (Поправка №5.8).
    ///
    /// Живе в ядрі, а не в рушії, з тієї самої причини, з якої сцена — дані:
    /// «хто зараз у кадрі і що він говорить» перевіряється тестом без редактора.
    /// Рушію лишається намалювати кадр, консолі — надрукувати його.
    ///
    /// План ТРИМАЄТЬСЯ, поки не зміниться: репліка не стирає кадр, інакше після
    /// першої ж фрази той, хто говорить, зникав би з екрана.
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

        /// <summary>Сцена дограна: далі показувати нема чого.</summary>
        public bool IsFinished { get; private set; }

        /// <summary>Ключ переходу, яким сцена закінчилася (null, поки йде).</summary>
        public string TransitionKey { get; private set; }

        /// <summary>Скільки тримати поточний кадр: пауза задає, решта — миттєво.</summary>
        public double HoldSeconds { get; private set; }

        /// <summary>
        /// Сцена стоїть на виборі репліки (Поправка №7.8) і чекає
        /// <see cref="Choose"/> — <see cref="Next"/> сам не рухається, поки це
        /// true (інакше наївний викликач, що не вміє вибирати, тихо
        /// пропустив би вибір, а не завис би на ньому явно).
        /// </summary>
        public bool IsAwaitingChoice { get; private set; }

        /// <summary>Id поточного кроку-вибору (§4.10-подібний ключ для ботів/журналу) — пусто, якщо не чекаємо вибору.</summary>
        public string ChoiceId { get; private set; }

        /// <summary>Варіанти поточного вибору — пусто, якщо не чекаємо вибору.</summary>
        public IReadOnlyList<SceneChoiceOption> PendingOptions => _pendingOptions;

        public SceneFrame Current => new SceneFrame(_actor, _second, _framing, _speaker, _line, _effect);

        /// <summary>
        /// Наступний крок. Повертає false, коли сцена закінчилася.
        ///
        /// Репліка і ефект живуть рівно один крок — це події; план тримається,
        /// поки його не змінить інший. Зупинившись на виборі, повторні
        /// виклики повертають той самий кадр, поки не прийде <see cref="Choose"/>.
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
        /// Вирішує поточний вибір (лише коли <see cref="IsAwaitingChoice"/>):
        /// варіант з <see cref="SceneChoiceOption.TransitionKey"/> завершує
        /// сцену на місці (як звичайний Transition-крок); варіант з
        /// <see cref="SceneChoiceOption.NextLabel"/> стрибає на мітку; варіант
        /// без обох продовжує сцену лінійно з наступного кроку. Наслідок і
        /// перевірку резолвить викликач (GameSession) ДО цього виклику — сама
        /// сцена ні того, ні іншого не знає.
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
                    // Next() збільшить індекс перед тим, як прочитати крок —
                    // ставимо на "попередній перед ціллю", а не на саму ціль.
                    _index = target - 1;
                }
            }
            // Ні того, ні іншого — сцена просто продовжує з наступного
            // кроку після Choice (індекс уже на ньому).
        }
    }
}
