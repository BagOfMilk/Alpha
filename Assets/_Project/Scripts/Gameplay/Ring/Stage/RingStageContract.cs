using Game.Core.Ring.Match;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Що екрани UI знають про 3D-ринг (ітерація 1): без типів рушія, щоб екрани й моделі лишались у лінті.
    /// Реалізує <c>RingStage3D</c> (сцена); без 3D (тести, лінт) <see cref="RingStageHub.Current"/> == null,
    /// і екрани працюють як у 2D-зрізі.
    /// </summary>
    public interface IRingStage
    {
        /// <summary>Режисер ще грає кліпи (ходу суперника і наступним крокам бота треба зачекати).</summary>
        bool IsBusy { get; }

        /// <summary>Матч почався: режисер слухає журнал подій рушія й програє кліпи.</summary>
        void BeginMatch(MatchEngine engine);

        /// <summary>Екран матчу закрито.</summary>
        void EndMatch();

        /// <summary>Промо: двоє на рингу обличчям одне до одного; камера діалогу.</summary>
        void BeginPromo();

        /// <summary>
        /// Хто говорить у вузлі промо (player, rival, booker, narrator...); null — не репліка (вибір, кидок):
        /// камера лишається як була.
        /// </summary>
        void SetPromoSpeaker(string speakerId);

        /// <summary>Усі інші екрани (ворожнеча, розвиток, кінцівка): ринг стоїть, камера — загальний план.</summary>
        void ShowIdle();
    }

    /// <summary>Єдина точка доступу до 3D-сцени з екранів. Пуста, якщо сцена без <c>RingStage3D</c>.</summary>
    public static class RingStageHub
    {
        public static IRingStage Current;

        /// <summary>Сцена збирає ринг із режисером; UI тоді стає напівпрозорим.</summary>
        public static bool Has3D { get { return Current != null; } }

        /// <summary>Запущено налагоджувальний показ <c>-ring-demo &lt;тека&gt;</c>: UI сховано, 3D програє сам.</summary>
        public static bool DemoRequested
        {
            get
            {
                string[] args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++) if (args[i] == "-ring-demo") return true;
                return false;
            }
        }

        /// <summary>Режисер чекає кліпів (false, якщо 3D немає).</summary>
        public static bool Busy { get { return Current != null && Current.IsBusy; } }
    }
}
