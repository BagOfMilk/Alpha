using System;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Checks;
using Game.Core.Economy;

namespace Game.Core.Story
{
    /// <summary>
    /// Розв'язка вузла 1 «бій на перевалі» (§3.1 TEST_BUILD.md, аудит G7/рядок
    /// 19): раніше <c>OpeningScenes.PassResolution</c> ІСНУВАЛА, але нічого не
    /// викликало метод з даними наслідку — сцена показувала репліку, а склад
    /// ростера й ресурси не змінювались. Тут — САМІ ДАНІ: <see cref="Apply"/>
    /// реально мутує підсумок вузла; ХТО викликає його (після тихого чи
    /// кровавого резолву) — робота D1.
    ///
    /// 4 полоси, за §3.1:
    /// • Найкраща — склад цілий, Максим і Мирослава лишаються.
    /// • Хороша — Максим ранений (кровавий шлях), Мирослава лишається.
    /// • Базова — Мирослава йде за батьком (посіяно <see cref="DefectorSeededFlag"/>),
    ///   склад розграбований.
    /// • Найгірша — Максим ранений (кровавий шлях) + Мирослава йде + склад
    ///   розграбований.
    ///
    /// Рана дається ЛИШЕ на кровавому шляху (<c>wasBloody</c>): тихий резолв
    /// теж дає 4 полоси (Persuade-перевірка), але рана — ціна саме крові
    /// (Поправка №1), а не будь-якого недостатнього результату.
    /// </summary>
    public static class PassVanguardOutcome
    {
        public const string MaksymId = "maksym";
        public const string MyroslavaId = "myroslava";

        /// <summary>
        /// Прапор «зерно зради посіяно» (§3.1): Мирослава ЩЕ не дефекція (те
        /// стане можливим лише коли B4 заведе <c>Defection</c>/`Antagonist`) —
        /// лише позначка на майбутнє й на фінал (<c>Finale.BuildAssault</c>
        /// приймає її id як зрадника параметром, коли D1 вирішить, що зрада
        /// відбулась).
        /// </summary>
        public const string DefectorSeededFlag = "defector_seeded";

        /// <summary>ПЛЕЙСХОЛДЕР: рана Максима (той самий порядок величини, що BloodyPathInjury — CheckBalance).</summary>
        public const double MaksymWoundInjuryPoints = 30.0;

        /// <summary>ПЛЕЙСХОЛДЕРИ: що забирає розграбований склад.</summary>
        public const int PlunderedMaterials = 5;
        public const int PlunderedFood = 8;

        /// <summary>"best"|"good"|"base"|"worst" — той самий формат, що <c>Finale.ResolveKey</c>.</summary>
        public static string ResolveKey(OutcomeBand band, bool wasBloody)
            => ResolveKey(band, wasBloody, maksymDead: false);

        /// <summary>
        /// Фікс-ревью (major, раунд 2, знайдено QA): "good"/"worst" — єдині
        /// полоси, чий канонічний текст стверджує "Максим поранений". Це
        /// АБСТРАКТНИЙ розв'язок вузла (§3.1) — окремо від нього справжній
        /// тактичний бій (CombatAutoResolve) міг того самого Максима вже
        /// вбити по-справжньому (ApplyBattleCasualties → RosterAdapter.Kill,
        /// раніше в тому самому виклику GameSession.FinishBattle). Стан
        /// ростера лишався коректним (WoundReporting не чіпає вже мертвого —
        /// <c>c.IsDead</c> guard), але гравець читав СЛОВА "поранений" одразу
        /// після рядка стрічки подій "Максим Беркут загинув" — пряма
        /// суперечність. <paramref name="maksymDead"/> — це вже зафіксований
        /// (справжній, посмертний) стан ростера на момент виклику, тож
        /// good/worst перемикаються на "_dead"-варіант тексту, а не на нову
        /// класифікацію полоси (полоса й далі описує ЯКІСТЬ бою — склад/
        /// Мирослава — незалежно від Максима).
        /// </summary>
        public static string ResolveKey(OutcomeBand band, bool wasBloody, bool maksymDead)
        {
            // wasBloody не змінює САМ ключ (розв'язки однакові по суті що для
            // тихого, що для кровавого шляху, §3.1) — параметр лишений для
            // симетрії з Apply(...) і про запас, якщо E3 захоче окрему
            // репліку саме для кровавого варіанту одного з чотирьох ключів.
            switch (band)
            {
                case OutcomeBand.Best: return "best";
                case OutcomeBand.Good: return maksymDead ? "good_dead" : "good";
                case OutcomeBand.Base: return "base";
                default: return maksymDead ? "worst_dead" : "worst";
            }
        }

        /// <summary>
        /// Дельта лояльності Мирослави за полосою (§3.1: +15/+5/−20/−35). ДАНІ,
        /// а не дія: <c>Companion.LoyaltyBand</c> (B4) у цьому робочому дереві
        /// ще не існує — D1/B4 застосує це число, коли з'явиться реальне поле.
        /// </summary>
        public static int MyroslavaLoyaltyDelta(OutcomeBand band)
        {
            switch (band)
            {
                case OutcomeBand.Best: return 15;
                case OutcomeBand.Good: return 5;
                case OutcomeBand.Base: return -20;
                default: return -35;
            }
        }

        /// <summary>
        /// Мутує підсумок вузла 1 у ростері/гаманці (§3.1). Викликається ОДИН
        /// РАЗ на резолв вузла (тихий чи кровавий) — повторний виклик тим самим
        /// band ще раз забере зі складу і ще раз ранить, тому викликач
        /// (D1) зобов'язаний зберегти, що вузол уже розв'язаний
        /// (для цього і є прапор <c>pass_vanguard_resolved</c> нижче).
        /// </summary>
        public static void Apply(BaseState state, OutcomeBand band, bool wasBloody, StoryFlags flags)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            bool myroslavaLeaves = band == OutcomeBand.Base || band == OutcomeBand.Worst;

            // Рана — ціна КРОВІ (Поправка №1), не будь-якого недостатнього
            // результату: тихий шлях 4 полосами теж має Базову/Найгіршу, але
            // без бою ранити нікого через "existing wound path" немає підстави.
            if (wasBloody && (band == OutcomeBand.Good || band == OutcomeBand.Worst))
                WoundMaksym(state);

            if (myroslavaLeaves)
            {
                MyroslavaLeaves(state);
                PlunderStorehouse(state);
            }

            if (flags != null)
            {
                flags.Set("pass_vanguard_resolved");
                if (myroslavaLeaves) flags.Set(DefectorSeededFlag);
            }
        }

        /// <summary>«Existing wound path» (R16/G7) — та сама точка, що ранить на кризі (RosterAdapter.Wound).</summary>
        private static void WoundMaksym(BaseState state)
        {
            new RosterAdapter(state.Roster).Wound(MaksymId, MaksymWoundInjuryPoints);
        }

        /// <summary>
        /// «Мирослава йде за батьком»: знята з поста (якщо стояла), статус —
        /// <c>Idle</c> (буквально за §3.1: «статус Idle → залишає ростер
        /// фізично як «пішла»»). ВІДОМИЙ РОЗРИВ, зафіксований свідомо:
        /// <c>RosterAdapter.IsPresentInSettlement</c> сьогодні виключає лише
        /// <c>OnMission</c>/<c>Dead</c>, тому Мирослава й далі рахується
        /// «присутньою» для перевірок/постів, доки D1/B4 не навчить перевірку
        /// присутності читати <see cref="DefectorSeededFlag"/> або доки B4 не
        /// заведе повноцінний статус «пішла» (Antagonist — про зраду, не про
        /// це). Тут — саме те, що доручено пакету B6: зробити крок, який
        /// можна зробити СЬОГОДНІ, не чіпаючи чужий Companion.cs.
        /// </summary>
        private static void MyroslavaLeaves(BaseState state)
        {
            var myroslava = state.Roster.Get(MyroslavaId);
            if (myroslava == null) return;

            if (!string.IsNullOrEmpty(myroslava.AssignedSlotId))
                state.Unassign(myroslava.AssignedSlotId);

            myroslava.Status = CompanionStatus.Idle;
        }

        /// <summary>Склад розграбований (§3.1) — Базова/Найгірша полоса.</summary>
        private static void PlunderStorehouse(BaseState state)
        {
            state.Resources.Add(ResourceType.Materials, -PlunderedMaterials);
            state.Resources.Add(ResourceType.Food, -PlunderedFood);
        }
    }
}
