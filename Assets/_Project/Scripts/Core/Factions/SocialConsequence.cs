using System;
using System.Collections.Generic;
using Game.Core.Loop;
using Game.Core.Pressure;

namespace Game.Core.Factions
{
    /// <summary>
    /// Единая точка соц-последствия выбора (R5): двигает одну или несколько
    /// фракций и, опционально, кладёт заявку в Напряжение — но ТОЛЬКО через
    /// один из уже существующих драйверов списка (инвариант 5). Квесты и
    /// угрозы читают этот же тип, поэтому список драйверов, разрешённых для
    /// социальных последствий, — единый и закрытый здесь, а не в каждом
    /// вызывающем месте отдельно.
    ///
    /// Тест-охранитель: <c>SocialConsequence_NeverIntroducesNewDriver</c>.
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
        /// Заявка в Напряжение существующим драйвером. Выбрасывает, если драйвер
        /// не входит в список, разрешённый социальным последствиям, — список
        /// драйверов Напряжения закрыт (инвариант 5), и это касается и того,
        /// каким драйверам разрешено приходить именно ОТСЮДА.
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
        /// Применяет последствие. registry/processor null-терпимы — вызывающий
        /// может интересоваться только одной из сторон эффекта.
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
