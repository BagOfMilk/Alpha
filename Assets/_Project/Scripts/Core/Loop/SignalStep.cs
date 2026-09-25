using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Signals;

namespace Game.Core.Loop
{
    /// <summary>
    /// Останній змістовний крок дня: перетворює фінальний стан
    /// на те, що гравець побачить і почує.
    /// </summary>
    public sealed class SignalStep : IDayStep
    {
        public int Order => DayStepOrder.Signals;

        public void Execute(DayContext ctx)
        {
            ctx.Signals = SignalComposer.Compose(
                ctx.Tension.Band,
                ctx.Tension.DayLedger,
                // Тір, що виріс сьогодні, видно сьогодні ж: сигнал «хутір став
                // селом» і вигляд села приходять одним звітом, а не з відставанням.
                System.Math.Max(ctx.Tier, ctx.RaiseTierTo),
                ctx.Balance.Signals,
                ctx.Forewarnings,
                ctx.IncidentOutcomes,
                BuildPostReports(ctx),
                ctx.IsNight,
                ctx.SignalMemory,
                ctx.Day,
                ctx.CityEvents);
        }

        /// <summary>
        /// Доповіді з постів. Вночі місто спить — доповідають тільки вранці.
        /// Точність рахується тим самим резолвером, що і все інше.
        /// </summary>
        private static List<PostReport> BuildPostReports(DayContext ctx)
        {
            if (ctx.IsNight || ctx.PostDomains == null || ctx.PostDomains.Count == 0)
                return null;

            var reports = new List<PostReport>(ctx.PostDomains.Count);
            foreach (var domain in ctx.PostDomains)
            {
                var request = new CheckRequest(
                    domain.Skill,
                    domain.Threshold,
                    ApproachForm.Neutral,
                    "post:" + domain.PositionId,
                    domain.PositionId);

                // Повтори тут не штрафуємо: доповідь — щоденна рутина,
                // а не звернення до фракції.
                var preview = CheckResolver.Preview(request, ctx.Roster, null, ctx.Day, ctx.Balance);

                reports.Add(preview.HasCandidate
                    ? new PostReport(domain.PositionId, domain.DomainTag, preview.BestActorId,
                        preview.ExpectedBand, false)
                    : PostReport.Silent(domain.PositionId, domain.DomainTag));
            }
            return reports;
        }
    }
}
