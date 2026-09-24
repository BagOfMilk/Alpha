using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Gameplay.Combat;

namespace Game.Gameplay
{
    /// <summary>
    /// Реалізація <see cref="IAutoplayDriver"/> для дим-тесту (R19): власна,
    /// ІЗОЛЬОВАНА від інтерактивної сесії <see cref="GameShell"/> копія
    /// <c>GameSession</c>, яку <see cref="BotRunner"/> веде політикою
    /// <see cref="StewardPolicy"/> — той самий водій, яким водять
    /// AllMechanicsCoverageTests і <c>tools/Alpha.Play -- --auto</c> (§1.1
    /// TEST_BUILD.md: "боти в ядрі... їх однаково споживають тести Unity,
    /// автопрогін і tools/*").
    ///
    /// Один крок <see cref="RunAutoplayStep"/> = рівно одна КАЛЕНДАРНА доба
    /// (<see cref="BotRunner.Drive"/> з <c>days: 1</c> завжди повертається у
    /// стан Morning/FreePlay наступної доби) — це й дає "скріншот на кожен
    /// ранок" безкоштовно: <see cref="AutoplayBootstrap"/> знімає кадр після
    /// КОЖНОГО кроку. Доба 5→6 в одному виклику проходить крізь ніч (фінал) і
    /// Summary (AcknowledgeSummary), тож той самий кадр — це й "скріншот
    /// після фіналу/підсумку".
    /// </summary>
    public sealed class AutoplayGameDriver : IAutoplayDriver
    {
        /// <summary>5 сценарних діб + 10 вільної гри (§3.6 TEST_BUILD.md) — той самий обсяг, що й тест покриття механік.</summary>
        private const int TotalDays = 15;

        private readonly IBotPolicy _policy = new StewardPolicy();
        private GameSession _session;
        private string _lastDescription = "";

        public bool Failed { get; private set; }

        public bool RunAutoplayStep(int stepIndex)
        {
            try
            {
                if (_session == null)
                {
                    var roller = new SeededDiceRoller(1);
                    var options = new NewGameOptions
                    {
                        HitRule = HitRuleKind.Threshold,
                        Seed = 1,
                        Roller = roller,
                        SkipCreation = false
                    };
                    _session = new GameSession(roller);
                    _session.NewGame(options);
                    _lastDescription = "Нова гра почата (seed=1, поріг влучання, політика " + _policy.Name + ").";
                    return true;
                }

                int dayBefore = SafeDay();
                var events = new List<GameEvent>();
                BotRunner.Drive(_session, _policy, 1, fullLog: events);
                _lastDescription = DescribeDay(dayBefore, _session.State, events);

                bool reachedEnd = _session.State == SessionState.FreePlay && SafeDay() >= TotalDays;
                return !reachedEnd;
            }
            catch (Exception ex)
            {
                Failed = true;
                _lastDescription = "Виняток на кроці " + stepIndex + ": " + ex.GetType().Name + " — " + ex.Message;
                return false;
            }
        }

        public string DescribeLastStep() => _lastDescription;

        private int SafeDay() => _session != null ? _session.CurrentView.Day : 0;

        private static string DescribeDay(int dayBefore, SessionState endState, List<GameEvent> events)
        {
            var sb = new StringBuilder();
            sb.Append("доба ").Append(dayBefore.ToString(CultureInfo.InvariantCulture))
              .Append(" -> стан ").Append(endState)
              .Append(", подій: ").Append(events.Count.ToString(CultureInfo.InvariantCulture));

            int shown = 0;
            for (int i = 0; i < events.Count && shown < 6; i++)
            {
                if (i > 0) sb.Append(i == 1 ? " [" : ", ");
                sb.Append(events[i].Key);
                shown++;
            }
            if (events.Count > 0) sb.Append(']');

            return sb.ToString();
        }
    }
}
