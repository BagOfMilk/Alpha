using System.Collections.Generic;

namespace Game.Core.Signals
{
    /// <summary>
    /// Підсумок дня для гравця. ЄДИНЕ, що читає міський UI: чисел
    /// прихованих шкал тут нема і бути не може.
    /// </summary>
    public sealed class SignalDigest
    {
        public IReadOnlyList<SignalRequest> Requests { get; }
        public MoodboardState Moodboard { get; }

        public SignalDigest(IReadOnlyList<SignalRequest> requests, MoodboardState moodboard)
        {
            Requests = requests ?? new List<SignalRequest>();
            Moodboard = moodboard;
        }
    }

    /// <summary>Приймач сигналів; реалізується в Game.Gameplay (UI, звук, сцена).</summary>
    public interface ISignalSink
    {
        void Publish(SignalDigest digest);
    }
}
