using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Pressure;
using Game.Core.World;

namespace Game.Core.Loop
{
    /// <summary>
    /// Розбір спрацьованих джерел. Наслідок впливає на Напругу сьогодні, але на
    /// ваги таблиці — тільки завтра: зворотного зв'язку всередині одного дня немає.
    /// </summary>
    public sealed class IncidentStep : IDayStep
    {
        public int Order => DayStepOrder.Incidents;

        public void Execute(DayContext ctx)
        {
            if (ctx.Incidents == null || ctx.FiredSourceIds.Count == 0) return;

            // Полоса береться одна на весь крок — та сама, за якою пульс вирішував,
            // розряджати джерело чи ні (HasIncidentFor). Наслідок першого інциденту
            // рухає Напругу одразу, і без знімка друге джерело фази
            // шукало б пул за іншою полосою: могло не знайти нічого і згоріти мовчки.
            var band = ctx.Tension.Band;

            for (int i = 0; i < ctx.FiredSourceIds.Count; i++)
            {
                string sourceId = ctx.FiredSourceIds[i];
                bool crisis = IsCrisisSource(sourceId);

                // Умова «криза оголошена і почута» живе в PressureTrack.IsReady:
                // сюди справа просто не доходить, і заряд не згоряє даремно.

                // Селектор детермінований: день плюс позиція джерела.
                int selector = ctx.Day * 31 + sourceId.Length * 7 + i;

                // Інцидент береться тільки з пулу СВОГО джерела: інакше
                // передвісник називає один домен, а приходить подія з іншого
                // (§5.1, правило «передвісник не бреше»).
                var incident = ctx.Incidents.Pick(
                    band, ctx.Tier, ctx.IsNight, crisis, selector, sourceId);
                if (incident == null) continue;

                // Аудит П10: кожен спрацьований інцидент фази — своє рішення
                // гравця, а не тільки перший. Тут вони лише СКЛАДАЮТЬСЯ в
                // чергу фази — подія, а не готова пропозиція; DayProcessor
                // виймає їх по одному і тримає AwaitsDecision=true, поки
                // черга не спорожніє. Раніше друге і наступні спрацювання
                // фази (вночі бюджет допускає два) тихо резолвилися самі —
                // вибір без наслідку для гравця.
                //
                // Пропозиція (BuildOffer) НЕ будується тут заздалегідь: якби
                // всі пропозиції фази рахувалися одним проходом до першого ходу
                // гравця, друга і далі показували б поріг за станом
                // Fear/Repeats ДО розв'язання попередніх — а застосовувався б поріг
                // ПІСЛЯ (кривавий шлях уже міг заармувати страх). Інваріант 8
                // («показаний поріг дорівнює застосованому») вимагає будувати
                // пропозицію безпосередньо в момент виймання — див.
                // DayProcessor.TryDequeueNextPending.
                if (ctx.RequirePlayerDecision)
                {
                    ctx.PendingQueue.Enqueue(incident);
                    continue;
                }

                // Без точки рішення все розбирається тихим шляхом: кров — це
                // вибір гравця, а не те, що трапляється саме.
                var outcome = IncidentResolver.Resolve(
                    incident, ctx.Roster, ctx.Repeats, ctx.Casualties,
                    ctx.Population, ctx.Tension, ctx.Day, ctx.Balance,
                    IncidentPath.Quiet, ctx.Fear);

                ctx.IncidentOutcomes.Add(outcome);
            }
        }

        /// <summary>
        /// Чи знайдеться джерелу інцидент у цій фазі. Той самий відбір, що в
        /// Execute: Pick повертає null рівно тоді, коли пул порожній. Пульс
        /// питає тут ДО розряду накопичувача (PulseStep): рішення «чи згорить
        /// заряд» приймає той, хто знає таблицю. Без таблиці розбирати
        /// нема кому зовсім — жодне джерело не розряджається.
        /// </summary>
        internal static bool HasIncidentFor(DayContext ctx, string sourceId)
        {
            if (ctx.Incidents == null) return false;
            return ctx.Incidents.Eligible(
                ctx.Tension.Band, ctx.Tier, ctx.IsNight, IsCrisisSource(sourceId), sourceId).Count > 0;
        }

        private static bool IsCrisisSource(string sourceId) => sourceId == "crisis";

        /// <summary>
        /// Пропозиція гравцю: шляхи, виконавці і ПОКАЗАНІ пороги.
        ///
        /// Саме тут нарешті приземляється US-2.6 «поріг показаний заздалегідь»:
        /// раніше передпоказ існував як метод резолвера, але моменту, коли
        /// його можна було б показати, у конвеєрі не було.
        ///
        /// internal, а не private: DayProcessor.TryDequeueNextPending зобов'язаний
        /// викликати її ЛІНИВО, у момент виймання з черги фази (див. коментар
        /// в Execute вище) — інакше показаний поріг міг розійтися із застосованим.
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
            // Запит будується рівно тим самим методом, що і при резолві, — разом
            // з надбавкою за страх громади. Інакше гравець побачив би один поріг,
            // а застосувався б інший.
            var request = IncidentResolver.BuildRequest(incident, path, ctx.Fear, ctx.Day, ctx.Balance);
            var preview = CheckResolver.Preview(request, ctx.Roster, ctx.Repeats, ctx.Day, ctx.Balance);

            options.Add(new DecisionOption(path, request.Skill, preview.EffectiveThreshold,
                request.Approach, preview.BestActorId, preview.HasCandidate, preview.ExpectedBand));
        }
    }
}
