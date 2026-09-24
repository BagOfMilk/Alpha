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

            // Полоса берётся одна на весь шаг — та же, по которой пульс решал,
            // разряжать ли источник (HasIncidentFor). Исход первого инцидента
            // двигает Напряжение сразу, и без снимка второй источник фазы
            // искал бы пул по другой полосе: мог не найти ничего и сгореть молча.
            var band = ctx.Tension.Band;

            for (int i = 0; i < ctx.FiredSourceIds.Count; i++)
            {
                string sourceId = ctx.FiredSourceIds[i];
                bool crisis = IsCrisisSource(sourceId);

                // Условие «кризис объявлен и услышан» живёт в PressureTrack.IsReady:
                // сюда дело просто не доходит, и заряд не сгорает впустую.

                // Селектор детерминирован: день плюс позиция источника.
                int selector = ctx.Day * 31 + sourceId.Length * 7 + i;

                // Инцидент берётся только из пула СВОЕГО источника: иначе
                // предвестник называет один домен, а приходит событие из другого
                // (§5.1, правило «предвестник не врёт»).
                var incident = ctx.Incidents.Pick(
                    band, ctx.Tier, ctx.IsNight, crisis, selector, sourceId);
                if (incident == null) continue;

                // Аудит П10: каждый сработавший инцидент фазы — своё решение
                // игрока, а не только первый. Здесь они лишь СКЛАДЫВАЮТСЯ в
                // очередь фазы — событие, а не готовое предложение; DayProcessor
                // вынимает их по одному и держит AwaitsDecision=true, пока
                // очередь не опустеет. Раньше второе и последующие срабатывания
                // фазы (ночью бюджет допускает два) тихо резолвились сами —
                // выбор без последствия для игрока.
                //
                // Предложение (BuildOffer) НЕ строится здесь заранее: если бы
                // все предложения фазы считались одним проходом до первого хода
                // игрока, второе и далее показывали бы порог по состоянию
                // Fear/Repeats ДО разрешения предыдущих — а применялся бы порог
                // ПОСЛЕ (кровавый путь уже мог заармить страх). Инвариант 8
                // («показанный порог равен применённому») требует строить
                // предложение непосредственно в момент выемки — см.
                // DayProcessor.TryDequeueNextPending.
                if (ctx.RequirePlayerDecision)
                {
                    ctx.PendingQueue.Enqueue(incident);
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
        /// Найдётся ли источнику инцидент в этой фазе. Тот же отбор, что в
        /// Execute: Pick возвращает null ровно тогда, когда пул пуст. Пульс
        /// спрашивает здесь ДО разряда накопителя (PulseStep): решение «сгорит
        /// ли заряд» принимает тот, кто знает таблицу. Без таблицы разбирать
        /// некому вовсе — ни один источник не разряжается.
        /// </summary>
        internal static bool HasIncidentFor(DayContext ctx, string sourceId)
        {
            if (ctx.Incidents == null) return false;
            return ctx.Incidents.Eligible(
                ctx.Tension.Band, ctx.Tier, ctx.IsNight, IsCrisisSource(sourceId), sourceId).Count > 0;
        }

        private static bool IsCrisisSource(string sourceId) => sourceId == "crisis";

        /// <summary>
        /// Предложение игроку: пути, исполнители и ПОКАЗАННЫЕ пороги.
        ///
        /// Именно здесь наконец приземляется US-2.6 «порог показан заранее»:
        /// раньше предпоказ существовал как метод резолвера, но момента, когда
        /// его можно было бы показать, в конвейере не было.
        ///
        /// internal, а не private: DayProcessor.TryDequeueNextPending обязан
        /// вызывать её ЛЕНИВО, в момент выемки из очереди фазы (см. комментарий
        /// в Execute выше) — иначе показанный порог мог разойтись с применённым.
        /// </summary>
        internal static PendingDecision BuildOffer(DayContext ctx, IncidentDefinition incident)
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
