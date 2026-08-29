using System.Collections.Generic;

namespace Game.Core.Signals
{
    /// <summary>
    /// Итог дня для игрока. ЕДИНСТВЕННОЕ, что читает городской UI: чисел
    /// скрытых шкал здесь нет и быть не может.
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

    /// <summary>Приёмник сигналов; реализуется в Game.Gameplay (UI, звук, сцена).</summary>
    public interface ISignalSink
    {
        void Publish(SignalDigest digest);
    }
}
