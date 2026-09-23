using System.Collections.Generic;
using Game.Core.Characters.Perks;
using Game.Core.Stats;

namespace Game.Core.Characters.Build
{
    /// <summary>Почему план нельзя подтвердить (или Ok, если можно).</summary>
    public enum BuildPlanStatus
    {
        Ok = 0,
        NotEnoughPoints = 1,
        AboveSkillCeiling = 2,
        PerkUnavailable = 3,

        /// <summary>Подтверждения не было — Commit ничего не сделал.</summary>
        NotConfirmed = 4
    }

    /// <summary>Скил до и после плана.</summary>
    public struct SkillChange
    {
        public SkillType Skill;
        public int From;
        public int To;
    }

    /// <summary>Производная величина до и после плана.</summary>
    public struct StatChange
    {
        public StatKey Key;
        public double From;
        public double To;
    }

    /// <summary>Перк плана и вердикт по нему НА МОМЕНТ плана, а не сейчас.</summary>
    public struct PerkVerdict
    {
        public PerkDefinition Perk;
        public PerkAvailability Verdict;

        public string Id => Perk == null ? null : Perk.Id;
    }

    /// <summary>
    /// Что игрок увидит до подтверждения: эффект на скилы и статы, вердикты по
    /// перкам плана и то, что план ОТКРОЕТ сверх запланированного.
    ///
    /// Ничего не применяет: превью и применение разведены намеренно, иначе
    /// «посмотреть» стоило бы столько же, сколько «сделать» (US-2.3).
    /// </summary>
    public sealed class BuildPreview
    {
        /// <summary>
        /// Текст предупреждения показывается РЯДОМ с кнопкой подтверждения.
        /// Живёт здесь, а не в UI: правило необратимости — часть правил, и
        /// вторая копия текста в другом слое разъехалась бы с этой.
        /// </summary>
        public const string IrreversibleWarning =
            "Распределение необратимо: отменить или перераспределить вложенное нельзя.";

        public BuildPlanStatus Status = BuildPlanStatus.Ok;

        /// <summary>Сколько очков просит план и сколько их есть.</summary>
        public int PointCost;
        public int PointsAvailable;

        public readonly List<SkillChange> Skills = new List<SkillChange>();
        public readonly List<StatChange> Stats = new List<StatChange>();
        public readonly List<PerkVerdict> Perks = new List<PerkVerdict>();

        /// <summary>
        /// Что откроется, если план подтвердить, — из перков, которых игрок НЕ
        /// планировал. Ради этого списка планировщик и существует: очко тратится
        /// осознанно, когда видно не только эффект, но и следующую дверь.
        /// </summary>
        public readonly List<PerkDefinition> Unlocks = new List<PerkDefinition>();

        public bool CanCommit => Status == BuildPlanStatus.Ok;
    }
}
