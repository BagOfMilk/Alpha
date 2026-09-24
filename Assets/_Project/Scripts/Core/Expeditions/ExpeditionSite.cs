using System;
using Game.Core.Checks;

namespace Game.Core.Expeditions
{
    /// <summary>
    /// Как отряд берёт точку. Это и есть ось баланса Поправки №1 «время против
    /// риска»: тихий путь безопасен, но длинный; силовой быстрый, но платит
    /// ранениями. Ненасильственный путь есть всегда — он объявлен здесь типом,
    /// а не оставлен на совесть контента.
    /// </summary>
    public enum ExpeditionApproach
    {
        Quiet = 0,     // скрытность и переговоры: дольше, но без ран
        Forceful = 1,  // силой: быстрее, но ранения почти неизбежны

        /// <summary>
        /// Данж (R15/B7-B2): партия заходит внутрь и играет комнатами
        /// (<c>Core/Dungeons</c>), а не одним резолвом на пороге. Диспетчинг
        /// для Delve дальше по потоку ведёт D1 вместе с данж-пакетом —
        /// <see cref="Base.ExpeditionRunner.Depart"/> лишь снаряжает отряд и
        /// НЕ зовёт <see cref="ExpeditionResolver.Resolve"/> для этого подхода.
        /// </summary>
        Delve = 2
    }

    /// <summary>
    /// Точка вылазки — заглушка данжа из Приложения А: «один тип, без полной
    /// push-your-luck, но как источник материалов».
    ///
    /// Материалы по Э6.2 добываются только здесь. Город их не производит
    /// вовсе — поэтому эта заглушка не украшение, а единственный кран.
    /// </summary>
    [Serializable]
    public sealed class ExpeditionSite
    {
        public string Id;
        public string DisplayName;

        /// <summary>Домен для слоя сигналов: по нему подбирается реплика.</summary>
        public string DomainTag = "road";

        /// <summary>Сколько дней занимает каждый подход.</summary>
        public int QuietDays = 6;
        public int ForcefulDays = 3;

        /// <summary>Чем берут точку. Порог один на оба подхода — разнится цена, а не планка.</summary>
        public SkillKey QuietSkill = SkillKeys.Survival;
        public SkillKey ForcefulSkill = SkillKeys.Melee;
        public int Threshold = 5;

        /// <summary>Базовая добыча на Базовой полосе, до истощения точки.</summary>
        public int BaseMaterials = 2;
        public int BaseGold = 10;

        /// <summary>
        /// Люди, которых отряд приводит на Хорошей или Лучшей полосе (Поправка
        /// №6.3: «в вилазках можуть бути люди»). Ноль — на этой точке людей нет.
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
                // Delve: данж считает свои сутки сам, по числу комнат (B2/D1) —
                // это число здесь не используется резолвом (R15 его пропускает).
                default: return ForcefulDays;
            }
        }

        public SkillKey SkillFor(ExpeditionApproach approach)
        {
            switch (approach)
            {
                case ExpeditionApproach.Quiet: return QuietSkill;
                case ExpeditionApproach.Forceful: return ForcefulSkill;
                // Delve: заходят тем же скилом, что и тихий подход — дальше
                // решают комнаты данжа, а не порог точки.
                default: return QuietSkill;
            }
        }
    }
}
