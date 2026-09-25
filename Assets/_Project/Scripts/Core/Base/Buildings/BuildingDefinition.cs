namespace Game.Core.Base
{
    /// <summary>
    /// Що будівля робить. Ефект рівно один — правило «один ефект — одна
    /// система» (Епік 2) поширюється і на будівлі: будівля, яка
    /// одночасно лікує, торгує і заспокоює, неможлива для балансування.
    /// </summary>
    public enum BuildingEffect
    {
        /// <summary>Відкриває пост — роботу для людини.</summary>
        OpensPost = 0,
        /// <summary>Відкриває пост і дії ради (облава, приймання переселенців).</summary>
        CouncilActions = 1,
        /// <summary>Щодоби приводить людей (Поправка №6.3).</summary>
        TavernArrivals = 2,
        /// <summary>Щодоби знижує Напругу драйвером TempleAura.</summary>
        TempleAura = 3,
        /// <summary>Щодоби знижує Напругу драйвером Fortifications.</summary>
        Fortifications = 4,
        /// <summary>Ефект чекає системи, якої ще немає (спорядження, аугменти).</summary>
        Awaiting = 5
    }

    /// <summary>
    /// Будівля з US-7.1 GDD. Ціна, термін і ефект — дані: баланс крутиться без
    /// перекомпіляції, а нова будівля — це рядок у каталозі, а не код.
    /// </summary>
    public sealed class BuildingDefinition
    {
        public string Id;
        public string DisplayName;

        /// <summary>Ціна золотом — для всіх будівель (рішення власника: «усі мають шось коштувати»).</summary>
        public int GoldCost;

        /// <summary>
        /// Будівельний компонент. Місто його не виробляє (Е6.2), джерело —
        /// тільки вилазка: тому просунуті будівлі замикають петлю
        /// «вилазка → матеріали → будівництво».
        /// </summary>
        public int MaterialsCost;

        /// <summary>Термін будівництва в добах. Видимих стадій завжди п'ять (US-7.3).</summary>
        public int Days;

        public BuildingEffect Effect;

        /// <summary>Пост, який будівля відкриває (може бути порожнім).</summary>
        public string OpensSlotId;

        /// <summary>Будується тільки за квестом (Лабораторія, GDD Е2).</summary>
        public bool QuestOnly;

        /// <summary>Чого чекає ефект, якщо він поки не працює. Показується гравцю чесно.</summary>
        public string AwaitingNote;
    }
}
