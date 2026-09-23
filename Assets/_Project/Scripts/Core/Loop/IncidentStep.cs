using System.Collections.Generic;
using Game.Core.Checks;
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

                // Аудит П10: каждый сработавший инцидент фазы — своё решение
                // игрока, а не только первый. Здесь все они лишь СКЛАДЫВАЮТСЯ в
                // очередь фазы; DayProcessor вынимает их по одному в Pending и
                // держит AwaitsDecision=true, пока очередь не опустеет. Раньше
                // второе и последующие срабатывания фазы (ночью бюджет допускает
                // два) тихо резолвились сами — выбор без последствия для игрока.
                if (ctx.RequirePlayerDecision)
                {
                    var offer = BuildOffer(ctx, incident);
                    ctx.PendingQueue.Enqueue(new PendingItem(offer, incident));
                    continue;
                }

                // Без точки решения всё разбирается тихим путём: кровь — это
                // выбор игрока, а не то, что случается само.
                var outcome = IncidentResolver.Resolve(
                    incident, ctx.Roster, ctx.Repeats, ctx.Casualties,
                    ctx.Population, ctx.Tension, ctx.Day, ctx.Balance,
                    IncidentPath.Quiet, ctx.Fear);

                ctx.IncidentOutcomes.Add(outcome);
            }
        }

        /// <summary>
        /// Предложение игроку: пути, исполнители и ПОКАЗАННЫЕ пороги.
        ///
        /// Именно здесь наконец приземляется US-2.6 «порог показан заранее»:
        /// раньше предпоказ существовал как метод резолвера, но момента, когда
        /// его можно было бы показать, в конвейере не было.
        /// </summary>
        private static PendingDecision BuildOffer(DayContext ctx, IncidentDefinition incident)
        {
            var options = new List<DecisionOption>();
            AddOption(options, ctx, incident, IncidentPath.Quiet);
            if (incident.HasBloodyPath)
                AddOption(options, ctx, incident, IncidentPath.Bloody);

            return new PendingDecision(incident.Id, incident.TopicId, incident.DomainTag,
                incident.RelevantPositionId, incident.IsCrisis, options);
        }

        private static void AddOption(List<DecisionOption> options, DayContext ctx,
            IncidentDefinition incident, IncidentPath path)
        {
            // Запрос строится ровно тем же методом, что и при резолве, — вместе
            // с надбавкой за страх общины. Иначе игрок увидел бы один порог,
            // а применился бы другой.
            var request = IncidentResolver.BuildRequest(incident, path, ctx.Fear, ctx.Day, ctx.Balance);
            var preview = CheckResolver.Preview(request, ctx.Roster, ctx.Repeats, ctx.Day, ctx.Balance);

            options.Add(new DecisionOption(path, request.Skill, preview.EffectiveThreshold,
                request.Approach, preview.BestActorId, preview.HasCandidate, preview.ExpectedBand));
        }
    }
}
