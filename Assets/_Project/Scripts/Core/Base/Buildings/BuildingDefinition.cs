namespace Game.Core.Base
{
    /// <summary>
    /// Что здание делает. Эффект ровно один — правило «один эффект — одна
    /// система» (Эпик 2) распространяется и на постройки: здание, которое
    /// одновременно лечит, торгует и успокаивает, невозможно сбалансировать.
    /// </summary>
    public enum BuildingEffect
    {
        /// <summary>Открывает пост — работу для человека.</summary>
        OpensPost = 0,
        /// <summary>Открывает пост и действия совета (облава, приём переселенцев).</summary>
        CouncilActions = 1,
        /// <summary>Каждые сутки приводит людей (Поправка №6.3).</summary>
        TavernArrivals = 2,
        /// <summary>Каждые сутки снижает Напряжение драйвером TempleAura.</summary>
        TempleAura = 3,
        /// <summary>Каждые сутки снижает Напряжение драйвером Fortifications.</summary>
        Fortifications = 4,
        /// <summary>Эффект ждёт системы, которой ещё нет (снаряжение, аугменты).</summary>
        Awaiting = 5
    }

    /// <summary>
    /// Здание из US-7.1 GDD. Цена, срок и эффект — данные: баланс крутится без
    /// перекомпиляции, а новое здание — это строка в каталоге, а не код.
    /// </summary>
    public sealed class BuildingDefinition
    {
        public string Id;
        public string DisplayName;

        /// <summary>Цена золотом — для всех зданий (решение владельца: «усі мають шось коштувати»).</summary>
        public int GoldCost;

        /// <summary>
        /// Строительный компонент. Город его не производит (Э6.2), источник —
        /// только вылазка: поэтому продвинутые здания замыкают петлю
        /// «вылазка → материалы → стройка».
        /// </summary>
        public int MaterialsCost;

        /// <summary>Срок стройки в сутках. Видимых стадий всегда пять (US-7.3).</summary>
        public int Days;

        public BuildingEffect Effect;

        /// <summary>Пост, который здание открывает (может быть пустым).</summary>
        public string OpensSlotId;

        /// <summary>Строится только по квесту (Лаборатория, GDD Э2).</summary>
        public bool QuestOnly;

        /// <summary>Чего ждёт эффект, если он пока не работает. Показывается игроку честно.</summary>
        public string AwaitingNote;
    }
}
