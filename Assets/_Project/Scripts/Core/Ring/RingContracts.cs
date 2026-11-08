using System;
using Game.Core.Randomness;

namespace Game.Core.Ring
{
    // Спільні типи зрізу «Ворожнеча» (docs/ring/gdd.md). Межа між промо/ворожнечею і матчем: ворожнеча збирає
    // MatchSetup, матч повертає MatchResult. Ні промо, ні матч не знають одне про одного нічого, крім цих типів.

    /// <summary>П'ять атрибутів борця, модифікатор −1…+3 (gdd §2.1).</summary>
    public enum RingAttr { Strength = 0, Agility = 1, Technique = 2, Stamina = 3, Charisma = 4 }

    /// <summary>Шлях ворожнечі (gdd §3): ранги відкривають перки, домінантний шлях — роль у матчі.</summary>
    public enum RingPath { Face = 0, Heel = 1, Rebel = 2 }

    /// <summary>
    /// План букера на матч (gdd §3, §5.2). NoCleanLoss («не програй чисто») — для проміжного матчу: виконано, якщо гравець не
    /// програв утриманням чи здачею; звернути — програти чисто. Значення додаються лише в кінець (сейви).
    /// </summary>
    public enum BookerPlan { None = 0, WinClean = 1, LoseClean = 2, WinAny = 3, NoCleanLoss = 4 }

    /// <summary>Умови матчу з промо (gdd §3).</summary>
    [Flags]
    public enum MatchStipulations { None = 0, NoDq = 1, NoAlly = 2 }

    public enum MatchWinner { Player = 0, Rival = 1, Draw = 2 }

    public enum MatchEndKind { Pin = 0, Submission = 1, CountOut = 2, Disqualification = 3, DoubleCountOut = 4, TimeLimit = 5 }

    /// <summary>Атрибути борця. Значення за індексом RingAttr; межі −1…+3 тримає той, хто міняє (розвиток).</summary>
    public sealed class WrestlerStats
    {
        public const int MinAttr = -1;
        public const int MaxAttr = 3;

        public string Id;
        /// <summary>Ключ імені в рядках (strings_uk.json).</summary>
        public string NameKey;
        public RingAttr FinisherAttr = RingAttr.Strength;
        private readonly int[] _attr = new int[5];

        public WrestlerStats(string id, string nameKey) { Id = id; NameKey = nameKey; }

        public int Get(RingAttr a) => _attr[(int)a];
        public void Set(RingAttr a, int value) => _attr[(int)a] = Math.Max(MinAttr, Math.Min(MaxAttr, value));

        public WrestlerStats Clone()
        {
            var c = new WrestlerStats(Id, NameKey) { FinisherAttr = FinisherAttr };
            for (int i = 0; i < _attr.Length; i++) c._attr[i] = _attr[i];
            return c;
        }
    }

    /// <summary>
    /// Поправки матчу від перків (gdd §6). Нулі — без перків. Матч читає лише ці поля, про перки не знає.
    /// </summary>
    public sealed class MatchModifiers
    {
        /// <summary>«Друге дихання»: + до кидка «Вирватися».</summary>
        public int KickOutBonus;
        /// <summary>«Улюбленець публіки»: + до ажіотажу старту (додає ворожнеча при збиранні MatchSetup).</summary>
        public int StartHypeBonus;
        /// <summary>«Холодна кров»: + до імпульсу старту (додає ворожнеча при збиранні MatchSetup).</summary>
        public int StartMomentumBonus;
        /// <summary>«Очі на потилиці»: брудний прийом на 7–9 без попередження.</summary>
        public bool DirtyNoWarningOnPartial;
        /// <summary>«Без страховки»: + до «зі стійки» і «відчайдушного».</summary>
        public int RiskyMoveBonus;
        /// <summary>«Ветеран»: + жетонів реверсу.</summary>
        public int ExtraReversalTokens;
        /// <summary>«Залізна щелепа»: зсув порогів ушкодження (6/10 → 6+N/10+N).</summary>
        public int DamageThresholdShift;
        /// <summary>«Камбек»: друге вирване утримання за матч дає жетон реверсу.</summary>
        public bool Comeback;
        /// <summary>«Герой залу»: при ажіотажі ≥ 7 «продати» прийом дає ще +1 імпульсу, а «Вирватися» — +1.</summary>
        public bool HeroOfCrowd;
        /// <summary>«Підкуплений суддя»: раз за матч програний «Вирватися» рефері зупиняє на «два».</summary>
        public bool BribedRef;
        /// <summary>«Брудна гра»: раз за матч брудний прийом без попередження й без мінуса ажіотажу.</summary>
        public bool DirtyGame;
        /// <summary>«Імпровізація»: раз за матч провал (не фінішер, не промінь надії) стає «частково».</summary>
        public bool Improvise;
    }

    /// <summary>Усе, що матч отримує від ворожнечі (gdd §1, §3).</summary>
    public sealed class MatchSetup
    {
        public WrestlerStats Player;
        public WrestlerStats Rival;
        public MatchModifiers PlayerMods = new MatchModifiers();
        public MatchModifiers RivalMods = new MatchModifiers();
        /// <summary>Імпульс старту, уже з бонусами (0–10).</summary>
        public int PlayerStartMomentum;
        public int RivalStartMomentum;
        /// <summary>Ажіотаж старту, уже з бонусами (0–10).</summary>
        public int StartHype;
        /// <summary>Напарник у куті гравця (прапор ворожнечі; умова NoAlly його вимикає — це перевіряє матч).</summary>
        public bool PlayerHasAlly;
        public MatchStipulations Stipulations;
        public BookerPlan Plan;
        /// <summary>Роль гравця в матчі — домінантний шлях (Face — герой, Heel — лиходій, Rebel — без ролі).</summary>
        public RingPath PlayerRole;
        /// <summary>Суперник зрізу — лиходій.</summary>
        public RingPath RivalRole = RingPath.Heel;
        /// <summary>Ліміт дій матчу (ланцюг ініціативи, TRPG_DESIGN §4.11): далі нічия. ПЛЕЙСХОЛДЕР.</summary>
        public int RoundLimit = Match.MatchRules.ActionLimit;
        /// <summary>Кар'єра: слабший бік, що сильно відстає, раз за матч отримує «промінь надії» (ПЛЕЙСХОЛДЕР, TRPG_DESIGN §4). Зріз ворожнечі лишає вимкненим.</summary>
        public bool HopeSpotEnabled;
    }

    /// <summary>Підсумок матчу — усе, що ворожнеча читає назад (gdd §3 «Матч → ворожнеча»).</summary>
    public sealed class MatchResult
    {
        public MatchWinner Winner;
        public MatchEndKind Kind;
        public int FinalHype;
        public int Rounds;
        public int PlayerDirtyMoves;
        public int PlayerSells;
        /// <summary>Скільки разів гравець вирвався з утримання на «два».</summary>
        public int PlayerKickoutsAtTwo;
        public bool PlayerUsedAlly;
    }

    /// <summary>
    /// Кидок 2d6 і три ступені (рішення власника, TRPG_DESIGN §4.1). Кубики — через наявний порт IDiceRoller
    /// (реалізація SeededDiceRoller живе в Gameplay, у ядрі реалізацій немає — охоронець ArchitectureGuardTests).
    /// </summary>
    public enum RollTier { Miss = 0, Partial = 1, Strong = 2 }

    public static class TwoD6
    {
        public static int Die(IDiceRoller dice, string streamId)
        {
            int d = 1 + (int)(dice.Roll01(streamId) * 6.0);
            return d > 6 ? 6 : d;
        }

        public static int Roll(IDiceRoller dice, string streamId) => Die(dice, streamId) + Die(dice, streamId);

        public static RollTier TierOf(int total) => total >= 10 ? RollTier.Strong : total >= 7 ? RollTier.Partial : RollTier.Miss;

        /// <summary>Шанси трьох ступенів для модифікатора (у тридцять шостих): [провал, 7–9, 10+]. Показуються до кидка.</summary>
        public static int[] ChancesOf36(int modifier)
        {
            var c = new int[3];
            for (int a = 1; a <= 6; a++)
                for (int b = 1; b <= 6; b++)
                    c[(int)TierOf(a + b + modifier)]++;
            return c;
        }
    }
}
