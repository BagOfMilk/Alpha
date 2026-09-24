using System.Collections.Generic;
using Game.Core.Randomness;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Тестовый кубик Б1: выдаёт заранее заданную очередь [0,1)-значений вне
    /// зависимости от streamId (детерминированный сценарий вручную). Когда
    /// очередь пуста — возвращает 0.5 (предсказуемая середина), как архивный
    /// ScriptedRng. Живёт в Tests/EditMode (не в Core!) — Core не реализует
    /// IDiceRoller нигде (R1, ArchitectureGuardTests.Core_NoTypeImplementsIDiceRoller).
    /// </summary>
    internal sealed class ScriptedDiceRoller : IDiceRoller
    {
        private readonly Queue<double> _values;
        public readonly List<string> Streams = new List<string>();

        public ScriptedDiceRoller(params double[] values) => _values = new Queue<double>(values ?? new double[0]);

        public double Roll01(string streamId)
        {
            Streams.Add(streamId);
            return _values.Count > 0 ? _values.Dequeue() : 0.5;
        }

        public string CaptureState() => _values.Count.ToString();
        public void RestoreState(string blob) { /* тестовый двойник — не сериализуется */ }
    }
}
