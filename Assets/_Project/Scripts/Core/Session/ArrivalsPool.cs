using Game.Core.Characters.Creation;

namespace Game.Core.Session
{
    /// <summary>
    /// Пул прибульців (Поправка №12.10, рішення власника 29.09.2026: «поки
    /// всі троє присутні завжди — погано»; обрано «Нові попаданці в пул»).
    ///
    /// Хто з чотирьох фахівців (Дід Овсій/Гафія/Гобан-Сайр/Синдбад) прибивсь
    /// до гурту ГГ — детерміновано (інваріант 1, жодного Random) з двох
    /// виборів гравця:
    /// <list type="bullet">
    /// <item>передісторія протагоніста (<see cref="Backgrounds"/>): healer →
    /// Гафія, trader → Синдбад, warrior → Гобан-Сайр;</item>
    /// <item>відповідь Тугарові в сцені відкриття (сама обрана репліка, а не
    /// результат перевірки — <c>OpeningScenes.TugarOfferChoiceId</c>): refuse
    /// → Дід Овсій, bargain → Синдбад, ask_myroslava → Гафія; якщо цей уже
    /// прибив за передісторією — Дід Овсій.</item>
    /// </list>
    /// Прибиваються рівно двоє різних — увесь простір 3×3 передісторій і
    /// відповідей Тугарові дає різних (див. ArrivalsPoolTests, 9 комбінацій).
    /// Хто НЕ прибив — лишається в ростері карткою, але ніде не з'являється
    /// (<see cref="Characters.CompanionStatus.NotArrived"/>).
    /// </summary>
    public static class ArrivalsPool
    {
        public const string KeeperId = "keeper";
        public const string HealerId = "healer";
        public const string GobanId = "goban";
        public const string SindbadId = "sindbad";

        /// <summary>Чотири специфічні id, вичерпний пул — інших "хто прибив" не буває.</summary>
        public static readonly string[] AllSpecialistIds = { KeeperId, HealerId, GobanId, SindbadId };

        /// <summary>Результат: двоє прибульців — рівно двоє РІЗНИХ id з <see cref="AllSpecialistIds"/>.</summary>
        public readonly struct Arrivals
        {
            /// <summary>Прибув за передісторією протагоніста.</summary>
            public readonly string FromBackground;

            /// <summary>Прибув за відповіддю Тугарові.</summary>
            public readonly string FromTugar;

            public Arrivals(string fromBackground, string fromTugar)
            {
                FromBackground = fromBackground;
                FromTugar = fromTugar;
            }

            /// <summary>Чи прибив специфічний фахівець (byId — один із <see cref="AllSpecialistIds"/>).</summary>
            public bool Contains(string specialistId) =>
                !string.IsNullOrEmpty(specialistId) &&
                (specialistId == FromBackground || specialistId == FromTugar);
        }

        /// <summary>
        /// Визначає двох прибульців. <paramref name="backgroundId"/> —
        /// <c>Backgrounds.Warrior/Trader/Healer().Id</c> (невідомий/null —
        /// той самий дефолт, що <c>Backgrounds.All()[0].Id</c> == "warrior",
        /// коли створення протагоніста пропущено — <c>NewGameOptions.
        /// SkipCreation</c>). <paramref name="tugarChoiceId"/> — id варіанту
        /// вибору <c>OpeningScenes.TugarOfferChoiceId</c> ("refuse"/
        /// "bargain"/"ask_myroslava"; невідомий/null — той самий дефолт
        /// "refuse", перший варіант у сцені).
        /// </summary>
        public static Arrivals Determine(string backgroundId, string tugarChoiceId)
        {
            string fromBackground = ArrivalFromBackground(backgroundId);
            string fromTugar = ArrivalFromTugarChoice(tugarChoiceId);

            // Колізія (background уже привів того самого фахівця, якого
            // привела б відповідь Тугарові): відкат на Діда Овсія — background
            // ніколи сам не приводить Діда Овсія (healer/trader/warrior
            // ведуть лише до Гафії/Синдбада/Гобана-Сайра), тож відкат завжди
            // дає другого, РІЗНОГО прибульця.
            if (fromTugar == fromBackground) fromTugar = KeeperId;

            return new Arrivals(fromBackground, fromTugar);
        }

        private static string ArrivalFromBackground(string backgroundId)
        {
            switch (backgroundId)
            {
                case "healer": return HealerId;
                case "trader": return SindbadId;
                case "warrior": return GobanId;
                default: return GobanId; // дефолт Backgrounds.All()[0].Id == "warrior"
            }
        }

        private static string ArrivalFromTugarChoice(string tugarChoiceId)
        {
            switch (tugarChoiceId)
            {
                case "bargain": return SindbadId;
                case "ask_myroslava": return HealerId;
                case "refuse":
                default: return KeeperId; // дефолт — перший варіант сцени
            }
        }
    }
}
