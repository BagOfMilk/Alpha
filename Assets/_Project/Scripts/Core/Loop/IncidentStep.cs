using Game.Core.Pressure;
using Game.Core.World;

namespace Game.Core.Loop
{
    /// <summary>
    /// Разбор сработавших источников. Исход влияет на Напряжение сегодня, но на
    /// веса таблицы — только завтра: обратной связи внутри одного дня нет.
    /// </summary>
    public sealed class IncidentStep : IDayStep
    {
        public int Order => DayStepOrder.Incidents;

        public void Execute(DayContext ctx)
        {
            if (ctx.Incidents == null || ctx.FiredSourceIds.Count == 0) return;

            for (int i = 0; i < ctx.FiredSourceIds.Count; i++)
            {
                string sourceId = ctx.FiredSourceIds[i];
                bool crisis = sourceId == "crisis";

                // Кризис не имеет права сработать без предвестника 3-й ступени:
                // жёсткое последствие обязано быть объявлено (US-11.2, риск R8).
                if (crisis && ctx.Pulse != null && ctx.Pulse.AnnouncedLevelOf(sourceId) < 3)
                    continue;

                // Селектор детерминирован: день плюс позиция источника.
                int selector = ctx.Day * 31 + sourceId.Length * 7 + i;

                var incident = ctx.Incidents.Pick(ctx.Tension.Band, ctx.Tier, ctx.IsNight, crisis, selector);
                if (incident == null) continue;

                var outcome = IncidentResolver.Resolve(
                    incident, ctx.Roster, ctx.Repeats, ctx.Casualties,
                    ctx.Population, ctx.Tension, ctx.Day, ctx.Balance);

                ctx.IncidentOutcomes.Add(outcome);
            }
        }
    }
}
