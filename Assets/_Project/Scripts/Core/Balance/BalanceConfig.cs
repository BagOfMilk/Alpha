using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Єдина точка налаштування балансу. Усі «магічні числа» зібрані тут,
    /// щоб можна було крутити економіку і прокачку, не чіпаючи логіку.
    /// В Unity це обгортається ScriptableObject (BalanceConfigAsset), але сам
    /// тип — чистий C#, тому тестується і читається без рушія.
    /// </summary>
    [Serializable]
    public sealed class BalanceConfig
    {
        // ---- Прокачка (XP / рівні) ----
        // Поріг досвіду до наступного рівня: XpBase * (level ^ XpExponent).
        public double XpBase = 100.0;
        public double XpExponent = 1.5;
        public int MaxLevel = 20;

        // Скільки очок СКІЛІВ дається за рівень (розподіляється за ростовим
        // профілем архетипу). Атрибути рівень не чіпає — US-2.1: їх підіймає
        // лише крафт аугменту, інакше пізня гра впирається в стелю 1–10.
        public int SkillPointsPerLevel = 3;

        // ---- Робота на базі ----
        // Базовий досвід ролі за один цикл (день) активного призначення.
        public int RoleXpPerCycle = 20;

        // Множник досвіду, якщо напарник добре підходить ролі. Застосовується,
        // коли профільний скіл слоту >= SkillMatchThreshold. Поріг осмислений:
        // половина майстерності на єдиній шкалі 0–10.
        public int SkillMatchThreshold = 5;
        public double WellSuitedXpMultiplier = 1.5;

        // ---- Виробництво ----
        // Глобальний множник усього виробництва бази (для загального підкручування).
        public double GlobalProductionMultiplier = 1.0;

        // Штраф до виробництва, якщо в напарника статус "поранений" і він досі
        // призначений (наприклад, легка рана дозволяє роботу зі штрафом).
        public double InjuredProductionMultiplier = 0.5;

        // ---- Відновлення ----
        // Скільки одиниць «здоров'я відновлення» знімається з пораненого за цикл
        // у лазареті на одиницю схильності Medicine призначеного медика.
        public double HealingPerMedicinePoint = 2.0;
        public double BaseHealingPerCycle = 5.0;

        // ---- Утримання / прогодування ----
        // Скільки їжі споживає один член поселення за цикл.
        public int FoodUpkeepPerCompanion = 1;

        /// <summary>Множник виробітку в день після голодного (Поправка №4).</summary>
        public double HungryProductionMultiplier = 0.5;

        /// <summary>Множник рольового досвіду в день після голодного (Поправка №4).</summary>
        public double HungryRoleXpMultiplier = 0.5;

        // ---- Похідні від атрибутів (GDD Е2.1, числа — Додаток Б) ----
        // Усі плейсхолдери. Рахуються один раз у DerivedStats і далі живуть
        // в агрегаторі як звичайні стати, щоб перки і гір могли їх змінювати.

        public double HpBase = 6.0;
        public double HpPerStrength = 1.0;
        public double ApBase = 8.0;
        public double ApPerAgilityStep = 3.0;
        public double AccuracyPerAgility = 1.0;
        public double DefenseBase = 0.0;
        public double DefensePerAgility = 1.0;
        public double InitiativeBase = 0.0;
        public double InitiativePerAgility = 1.0;
        public double InitiativePerWits = 1.0;
        public double CritBase = 5.0;
        public double CritPerWits = 1.0;
        public double CarryBase = 10.0;
        public double CarryPerStrength = 2.0;
        public double StatusDurationReductionPerWill = 0.25;
        public double MoveApPerTileBase = 1.0;

        /// <summary>Скільки трейтів тримається активними одночасно (US-2.4, ПЛЕЙСХОЛДЕР).</summary>
        public int TraitSlots = 4;

        /// <summary>Межі шкал: атрибути 1–10, скіли 0–10.</summary>
        public int MinAttribute = 1;
        public int MaxAttribute = 10;
        public int MaxSkillLevel = 10;

        // ---- Вилазки (Е6.2, Додаток А) ----
        // Заглушка данжа — єдиний кран матеріалів: місто їх не
        // виробляє взагалі. Тут же живе вісь Поправки №1 «час проти ризику».

        /// <summary>Розмір загону. Більше чотирьох на точку не ходить (US-8.3).</summary>
        public int ExpeditionPartyMax = 4;

        /// <summary>Кожна полоса наслідку над Базовою додає стільки часток бази.</summary>
        public double ExpeditionYieldPerBand = 0.5;

        /// <summary>Кожне відпрацювання точки зрізає здобич на цю частку.</summary>
        public double ExpeditionDepletionStep = 0.25;

        /// <summary>Нижче цього множника виснаження не опускає точку.</summary>
        public double ExpeditionDepletionFloor = 0.25;

        /// <summary>Ранень при силовому підході: провал і звичайний наслідок.</summary>
        public int ExpeditionForcefulWoundsOnWorst = 2;
        public int ExpeditionForcefulWoundsOnBase = 1;

        /// <summary>У скільки очок відновлення обходиться легка рана.</summary>
        public double ExpeditionLightWoundPoints = 20.0;

        /// <summary>Те саме для серйозної. За US-4.2 саме вона лишає шрам.</summary>
        public double ExpeditionSeriousWoundPoints = 60.0;

        // ---- Міський шар (Поправка №3) ----
        // Секції винесені в окремі класи: у кожної свій SO-асет,
        // щоб дизайнер правив їх незалежно і в Play-режимі (US-18.3).

        /// <summary>Прихована шкала «Напруга»: пороги полос, тик, білий список драйверів.</summary>
        public TensionBalance Tension = new TensionBalance();

        /// <summary>Шар сигналів: бюджет уваги і правило «немає німого переходу».</summary>
        public SignalBalance Signals = new SignalBalance();

        /// <summary>Детерміновані перевірки і полоси наслідку.</summary>
        public CheckBalance Checks = new CheckBalance();

        /// <summary>Накопичувачі тиску замість кидка кубика.</summary>
        public PulseBalance Pulse = new PulseBalance();

        /// <summary>Як місто відповідає гравцю: люди, рада, тіри (Поправка №6).</summary>
        public CityBalance City = new CityBalance();

        /// <summary>Тактичний бій (Б1): влучність/урон/overwatch/статуси. Своя секція — R14.</summary>
        public CombatBalance Combat = new CombatBalance();

        /// <summary>Квестовий рушій поза конвеєром дня (R6, пакет B6).</summary>
        public QuestBalance Quest = new QuestBalance();

        /// <summary>Готовність громади до фіналу (R8, пакет B6).</summary>
        public ReadinessBalance Readiness = new ReadinessBalance();

        /// <summary>Push-your-luck данж: коефіцієнти прихованої шкали Threat (Core/Dungeons, R4).</summary>
        public DungeonBalance Dungeon = new DungeonBalance();

        /// <summary>Фракції і нові укази ради (R5, B5).</summary>
        public FactionBalance Faction = new FactionBalance();

        /// <summary>Предмети/крафт: ціна підняття рідкості, заряди іменних ефектів (B3).</summary>
        public ItemBalance Items = new ItemBalance();

        /// <summary>Лояльність напарників, брижі ростера, дефекція (B4/R2).</summary>
        public CompanionSocialBalance CompanionSocial = new CompanionSocialBalance();
    }
}
