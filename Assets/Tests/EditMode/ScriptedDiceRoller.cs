using System.Collections.Generic;
using Game.Core.Randomness;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Тестовий кубик Б1: видає заздалегідь задану чергу [0,1)-значень поза
    /// залежністю від streamId (детермінований сценарій вручну). Коли
    /// черга порожня — повертає 0.5 (передбачувана середина), як архівний
    /// ScriptedRng. Живе в Tests/EditMode (не в Core!) — Core не реалізує
    /// IDiceRoller ніде (R1, ArchitectureGuardTests.Core_NoTypeImplementsIDiceRoller).
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
        public void RestoreState(string blob) { /* тестовий двійник — не серіалізується */ }
    }
}
