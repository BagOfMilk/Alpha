using System;
using System.Collections.Generic;
using Game.Core.Loop;
using Game.Core.Pressure;

namespace Game.Core.Factions
{
    /// <summary>
    /// Єдина точка соц-наслідку вибору (R5): рухає одну або декілька
    /// фракцій і, опційно, кладе заявку в Напругу — але ТІЛЬКИ через
    /// один з уже наявних драйверів списку (інваріант 5). Квести і
    /// загрози читають цей самий тип, тому список драйверів, дозволених для
    /// соціальних наслідків, — єдиний і закритий тут, а не в кожному
    /// місці виклику окремо.
    ///
    /// Тест-охоронець: <c>SocialConsequence_NeverIntroducesNewDriver</c>.
    /// </summary>
    public sealed class SocialConsequence
    {
        private static readonly TensionDriver[] Allowed =
        {
            TensionDriver.QuestChoice,
            TensionDriver.ThreatOutcome,
            TensionDriver.CouncilEdict
        };

        private readonly List<(string FactionId, int Delta)> _factionDeltas = new List<(string, int)>();

        public TensionDriver Driver { get; private set; } = TensionDriver.None;
        public int Amount { get; private set; }

        public SocialConsequence Faction(string factionId, int delta)
        {
            if (!string.IsNullOrEmpty(factionId) && delta != 0)
                _factionDeltas.Add((factionId, delta));
            return this;
        }

        /// <summary>
        /// Заявка в Напругу наявним драйвером. Кидає виняток, якщо драйвер
        /// не входить у список, дозволений соціальним наслідкам, — список
        /// драйверів Напруги закритий (інваріант 5), і це стосується і того,
        /// яким драйверам дозволено приходити саме ЗВІДСИ.
        /// </summary>
        public SocialConsequence Tension(TensionDriver driver, int amount)
        {
            if (!IsAllowed(driver))
                throw new ArgumentException(
                    "SocialConsequence не может завести новый драйвер Напряжения: " + driver, nameof(driver));

            Driver = driver;
            Amount = amount;
            return this;
        }

        public static bool IsAllowed(TensionDriver driver)
        {
            for (int i = 0; i < Allowed.Length; i++)
                if (Allowed[i] == driver) return true;
            return false;
        }

        /// <summary>
        /// Застосовує наслідок. registry/processor null-терпимі — викликач
        /// може цікавитися лише однією зі сторін ефекту.
        /// </summary>
        public void Apply(FactionRegistry registry, DayProcessor processor)
        {
            if (registry != null)
                for (int i = 0; i < _factionDeltas.Count; i++)
                    registry.ApplySocialConsequence(_factionDeltas[i].FactionId, _factionDeltas[i].Delta);

            if (Driver != TensionDriver.None && processor != null)
                processor.QueueExternal(Driver, Amount);
        }
    }
}
