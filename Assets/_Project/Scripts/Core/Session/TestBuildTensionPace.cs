using Game.Core.Balance;
using Game.Core.World;

namespace Game.Core.Session
{
    /// <summary>
    /// Поправка №7 (рішення власника 24.09.2026, дослівно: «За 5 днів пожежа,
    /// потім інші механіки. Так для тесту ввімкни що за 15 днів перша смуга
    /// за 20 друга і за 25 криза? Чи це занадто швидко і треба хочаб 30-40
    /// днів?»). Провідний асистент відповів у тому ж обговоренні: 15/20/25 —
    /// правильний темп саме для тестової збірки; між другою смугою і кризою
    /// повинен стояти «Накал» (~доба 22-23) з драбиною передвісників у три
    /// ступені і вікном милосердя — інакше криза вдарила б німою (інваріант 4).
    /// Підсумкова ціль: Ропіт ~д15, Брожіння ~д20, Накал ~д22-23, бунт ~д25 —
    /// для гравця, що не замовляє облав і не будує Храм/Укріплення (§7.7
    /// AllMechanicsCoverageTests лишається на своєму 15-денному темпі, бо
    /// форсована криза доби 5 і фінал від цього прапорця не залежать).
    ///
    /// Важелі — ЛИШЕ з закритого списку драйверів (інваріант 5), тим самим
    /// прийомом, що й <see cref="Base.CityWorks.OneDayConstruction"/>
    /// (<c>NewGameOptions.TestBuildOneDayConstruction</c>): тестові копії
    /// секцій балансу НЕ мутують кампанійні дефолти, а лише підмінюють поле
    /// <see cref="BalanceConfig.Tension"/>/<see cref="BalanceConfig.Pulse"/>
    /// одного, щойно створеного для цієї партії <see cref="BalanceConfig"/> —
    /// CampaignPacingTests/CityWorksTests/tools/Alpha.Sim, які кличуть
    /// <see cref="FirstHourWorld.Build"/> без прапорця (або з ним false),
    /// свого <c>BalanceConfig</c> не бачать зміненим узагалі.
    ///
    /// Підібрано бот-прогонами (25.09.2026, перепідбір): еталон — СИТИЙ
    /// домосід (<see cref="Bots.HomebodyPolicy"/> + suppressCouncilRoutine) —
    /// Ропіт д15, Брожіння д21, Накал д21-23, бунт д25 і д30; той самий
    /// домосід із рутиною ради (Облава/Храм/Укріплення) — жодного бунту за 30
    /// діб; голодні боти з вилазками — бунт д21-23; кривавий — д24; шкідник
    /// (<see cref="Bots.NeglectPolicy"/>) — д19. Перший підбір (24.09) спирався
    /// на Pacifist, який голодує з восьмої доби: темп тримався на голоді, і
    /// ситий водій Unity-туру побачив лише Ропіт близько тридцятої доби.
    /// Числа лишаються ПЛЕЙСХОЛДЕРОМ тестової збірки, не кампанії.
    /// </summary>
    public static class TestBuildTensionPace
    {
        /// <summary>
        /// Множник дренажу Храму/Укріплень: хутір (тир 1) тікає в 10 разів
        /// швидше за кампанійний (1.0 → 10.0), тож і придушення стиснуте тим
        /// самим множником — інакше воно б нічого не важило.
        /// </summary>
        public const double TierTickCompressionFactor = 10.0;

        /// <summary>
        /// Фоновий тик за тиром — єдине джерело пасивного тиску (US-1.3).
        /// Задано явно, а не множником кампанійних {1,2,4,7}: з множником село
        /// (тир 2, у ботів з радою — вже на восьму-дев'яту добу) тікало б
        /// удвічі швидше за хутір, і жодна Облава не встигала б. У тестовій
        /// збірці ріст тиру лише трохи прискорює, темп задає час.
        /// </summary>
        public static readonly double[] TierTickPerDay = { 10.0, 12.0, 14.0, 16.0 };

        /// <summary>
        /// Пороги смуг тестової збірки: Ропіт / Брожіння / Накал / Злам.
        /// Нерівномірні навмисно: після Ропоту частішають інциденти вулиці й
        /// ночі (їхня ставка росте з полосою), тож Брожіння→Накал проходиться
        /// швидше, ніж Спокій→Ропіт.
        /// </summary>
        public static readonly int[] BandThresholds = { 145, 265, 305, 380 };

        /// <summary>Стиснутий поріг накопичувача кризи (кампанійний — 120, Threshold конструктора CrisisPressureSource). Ставки накопичення (5/12 за тик) лишаються кампанійними — тиснути на них не треба, стискає сам поріг.</summary>
        public const int CrisisThreshold = 9;

        /// <summary>Вікно милосердя між почутою третьою ступінню і бунтом — доба замість трьох (кампанійне <see cref="PulseBalance.CrisisGraceDays"/>=3), але НЕ нуль: R2 вимагає &gt;=1, інакше "почута ступінь = та ж доба, що й бунт" читалось би як німа криза.</summary>
        public const int CrisisGraceDays = 1;

        /// <summary>
        /// Відкат кризи — 5 діб замість 30. Не заради частоти бунтів: заряд
        /// накопичувача з порогом 9 відновлюється за дві фази, і драбина
        /// «площі» звучала знову вже наступного ранку після бунту — а з
        /// кампанійним відкатом 30 діб за нею нічого не йшло, передвісник
        /// брехав (SETTLEMENT_LAYER §5.1, правило 4). З відкатом 5 діб
        /// повторна драбина чесно веде до другого бунту, якщо місто не
        /// заспокоїли (облава, храм, укріплення). Знайдено перевіркою
        /// 25.09.2026 (подобовий прогін еталонного бота).
        /// </summary>
        public const int CrisisCooldownDays = 5;

        /// <summary>
        /// Тестова копія <see cref="TensionBalance"/>: пороги смуг замінені, тик
        /// і дренаж Храму/Укріплень масштабовані тим самим <see cref="TierTickCompressionFactor"/>.
        /// Решта полів (вибори квестів, кров, голод, Облава) — БУКВАЛЬНО ті самі
        /// значення кампанійного <paramref name="campaignDefault"/>: власник просив
        /// стиснути темп, а не переважити ці драйвери.
        /// </summary>
        public static TensionBalance BuildTensionBalance(TensionBalance campaignDefault)
        {
            var src = campaignDefault ?? new TensionBalance();
            return new TensionBalance
            {
                Max = src.Max,
                BandThresholds = BandThresholds,
                TierTickPerDay = (double[])TierTickPerDay.Clone(),
                OrderTickMultiplier = src.OrderTickMultiplier,
                AllowedRaising = src.AllowedRaising,
                AllowedLowering = src.AllowedLowering,
                ChoiceMinor = src.ChoiceMinor,
                ChoiceMajor = src.ChoiceMajor,
                ChoiceMonstrous = src.ChoiceMonstrous,
                RaidDelta = src.RaidDelta,
                RaidCooldownDays = src.RaidCooldownDays,
                TempleDrainPerDay = src.TempleDrainPerDay * TierTickCompressionFactor,
                FortificationDrainPerDay = src.FortificationDrainPerDay * TierTickCompressionFactor,
                BloodDeltaPerNode = src.BloodDeltaPerNode,
                BloodCapPerExpedition = src.BloodCapPerExpedition,
                HungerDeltaPerDay = src.HungerDeltaPerDay
            };
        }

        /// <summary>Тестова копія <see cref="PulseBalance"/>: лише вікно милосердя кризи інше — драбина передвісників (0.55/0.80/0.95) і бюджет спрацювань лишаються кампанійними.</summary>
        public static PulseBalance BuildPulseBalance(PulseBalance campaignDefault)
        {
            var src = campaignDefault ?? new PulseBalance();
            return new PulseBalance
            {
                DefaultThreshold = src.DefaultThreshold,
                Forewarn1At = src.Forewarn1At,
                Forewarn2At = src.Forewarn2At,
                Forewarn3At = src.Forewarn3At,
                MaxFiresPerDay = src.MaxFiresPerDay,
                MaxFiresPerNight = src.MaxFiresPerNight,
                ChargeCapMultiplier = src.ChargeCapMultiplier,
                MinActiveTracks = src.MinActiveTracks,
                CrisisGraceDays = CrisisGraceDays,
                CrisisLadderLeadDays = src.CrisisLadderLeadDays
            };
        }

        /// <summary>Джерело кризи зі стиснутим порогом і відкатом — ставки накопичення й Kind/DomainTag лишаються кампанійними (конструктор <see cref="CrisisPressureSource"/>).</summary>
        public static CrisisPressureSource BuildCrisisSource()
        {
            return new CrisisPressureSource(threshold: CrisisThreshold, cooldownDays: CrisisCooldownDays);
        }
    }
}
