using System;
using Game.Core.Checks;

namespace Game.Core.Expeditions
{
    /// <summary>
    /// Як відряд бере точку. Це і є вісь балансу Поправки №1 «час проти
    /// ризику»: тихий шлях безпечний, але довгий; силовий швидкий, але платить
    /// пораненнями. Ненасильницький шлях є завжди — він оголошений тут типом,
    /// а не лишений на совість контенту.
    /// </summary>
    public enum ExpeditionApproach
    {
        Quiet = 0,     // прихованість і переговори: довше, але без ран
        Forceful = 1,  // силою: швидше, але поранення майже неминучі

        /// <summary>
        /// Данж (R15/B7-B2): партія заходить усередину і грає кімнатами
        /// (<c>Core/Dungeons</c>), а не одним резолвом на порозі. Диспетчинг
        /// для Delve далі по потоку веде D1 разом з данж-пакетом —
        /// <see cref="Base.ExpeditionRunner.Depart"/> лише спорядує відряд і
        /// НЕ кличе <see cref="ExpeditionResolver.Resolve"/> для цього підходу.
        /// </summary>
        Delve = 2
    }

    /// <summary>
    /// Точка вилазки — заглушка данжа з Додатка А: «один тип, без повного
    /// push-your-luck, але як джерело матеріалів».
    ///
    /// Матеріали за Е6.2 видобуваються лише тут. Місто їх не виробляє
    /// зовсім — тому ця заглушка не прикраса, а єдиний кран.
    /// </summary>
    [Serializable]
    public sealed class ExpeditionSite
    {
        public string Id;
        public string DisplayName;

        /// <summary>Домен для шару сигналів: за ним підбирається репліка.</summary>
        public string DomainTag = "road";

        /// <summary>Скільки днів займає кожен підхід.</summary>
        public int QuietDays = 6;
        public int ForcefulDays = 3;

        /// <summary>Чим беруть точку. Поріг один на обидва підходи — різниться ціна, а не планка.</summary>
        public SkillKey QuietSkill = SkillKeys.Survival;
        public SkillKey ForcefulSkill = SkillKeys.Melee;
        public int Threshold = 5;

        /// <summary>Базовий видобуток на Базовій полосі, до виснаження точки.</summary>
        public int BaseMaterials = 2;
        public int BaseGold = 10;

        /// <summary>
        /// Люди, яких відряд приводить на Хорошій чи Кращій полосі (Поправка
        /// №6.3: «в вилазках можуть бути люди»). Нуль — на цій точці людей немає.
        /// </summary>
        public int PeopleOnGood;

        public ExpeditionSite() { }

        public ExpeditionSite(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public int DaysFor(ExpeditionApproach approach)
        {
            switch (approach)
            {
                case ExpeditionApproach.Quiet: return QuietDays;
                case ExpeditionApproach.Forceful: return ForcefulDays;
                // Delve: данж рахує свої доби сам, за числом кімнат (B2/D1) —
                // це число тут не використовується резолвом (R15 його пропускає).
                default: return ForcefulDays;
            }
        }

        public SkillKey SkillFor(ExpeditionApproach approach)
        {
            switch (approach)
            {
                case ExpeditionApproach.Quiet: return QuietSkill;
                case ExpeditionApproach.Forceful: return ForcefulSkill;
                // Delve: заходять тим самим скілом, що й тихий підхід — далі
                // вирішують кімнати данжу, а не поріг точки.
                default: return QuietSkill;
            }
        }
    }
}
