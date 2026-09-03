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

                // Условие «кризис объявлен и услышан» живёт в PressureTrack.IsReady:
                // сюда дело просто не доходит, и заряд не сгорает впустую.

                // Селектор детерминирован: день плюс позиция источника.
                int selector = ctx.Day * 31 + sourceId.Length * 7 + i;

                // Инцидент берётся только из пула СВОЕГО источника: иначе
                // предвестник называет один домен, а приходит событие из другого
                // (§5.1, правило «предвестник не врёт»).
                var incident = ctx.Incidents.Pick(
                    ctx.Tension.Band, ctx.Tier, ctx.IsNight, crisis, selector, sourceId);
                if (incident == null) continue;

                var outcome = IncidentResolver.Resolve(
                    incident, ctx.Roster, ctx.Repeats, ctx.Casualties,
                    ctx.Population, ctx.Tension, ctx.Day, ctx.Balance);

                ctx.IncidentOutcomes.Add(outcome);
            }
        }
    }
}
