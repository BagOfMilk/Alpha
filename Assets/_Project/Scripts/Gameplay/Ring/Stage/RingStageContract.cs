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

        /// <summary>Вступ перед меню: пролітка камери по залі, титантрон з назвою гри, вихід героя. Поки грає — IsBusy.</summary>
        void BeginIntro(string title, string subtitle);

        /// <summary>
        /// Вихід борця (гравця чи суперника) перед матчем: титантрон з іменем, хода від завіси, поза в куті.
        /// <paramref name="rgb"/> — колір борця (0xRRGGBB). Поки грає — IsBusy.
        /// </summary>
        void BeginEntrance(bool player, string title, string subtitle, int rgb);

        /// <summary>Пропустити вступ чи вихід (будь-яка клавіша).</summary>
        void SkipShow();

        /// <summary>Грає саме шоу (вступ чи вихід), а не кліпи матчу: тоді UI ховається.</summary>
        bool ShowBusy { get; }

        /// <summary>Лічильник великих моментів світло-шоу (фінішер, перемога) і назва останнього — для кадрів огляду.</summary>
        int FxMoments { get; }

        /// <summary>Лукбук (-ring-lookbook): обидва борці в центрі, камера зблизька по пояс.</summary>
        void LookbookShot();

        /// <summary>Лукбук: камера збоку на рівні мату (ноги й підошви проти полотна) і заміри висоти стоп у лог.</summary>
        void LookbookFeet(bool on);

        /// <summary>Лукбук: гравець грає кліп (без петлі, тримає останній кадр) з Foot IK чи без — порівняння пози.</summary>
        void LookbookClip(string clip, bool footIk);
        string FxLastMoment { get; }

        /// <summary>Закулісся (тиждень): роздягальня, герой і букер.</summary>
        void ShowBackstage();

        /// <summary>Сцена закулісся: null — розмова з букером; ev_* — подія з гостем на місці букера.</summary>
        void SetBackstageScene(string sceneId);

        /// <summary>
        /// Хто на рингу: тіла набору (<c>body_m_latin</c>…), колір екіпіровки 0xRRGGBB, зачіска (ключ частини набору або
        /// порожньо), масштаб суперника (1 — як герой). Та сама зовнішність — без перебудови.
        /// </summary>
        void SetWrestlers(string playerBody, int playerRgb, string playerHair, string rivalBody, int rivalRgb, string rivalHair, float rivalScale);
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
