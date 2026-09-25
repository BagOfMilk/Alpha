using System.Collections.Generic;
using Game.Core.Characters.Perks;
using Game.Core.Stats;

namespace Game.Core.Characters.Build
{
    /// <summary>Чому план не можна підтвердити (або Ok, якщо можна).</summary>
    public enum BuildPlanStatus
    {
        Ok = 0,
        NotEnoughPoints = 1,
        AboveSkillCeiling = 2,
        PerkUnavailable = 3,

        /// <summary>Підтвердження не було — Commit нічого не зробив.</summary>
        NotConfirmed = 4
    }

    /// <summary>Скіл до і після плану.</summary>
    public struct SkillChange
    {
        public SkillType Skill;
        public int From;
        public int To;
    }

    /// <summary>Похідна величина до і після плану.</summary>
    public struct StatChange
    {
        public StatKey Key;
        public double From;
        public double To;
    }

    /// <summary>Перк плану і вердикт щодо нього НА МОМЕНТ плану, а не зараз.</summary>
    public struct PerkVerdict
    {
        public PerkDefinition Perk;
        public PerkAvailability Verdict;

        public string Id => Perk == null ? null : Perk.Id;
    }

    /// <summary>
    /// Що гравець побачить до підтвердження: ефект на скіли й стати, вердикти по
    /// перках плану і те, що план ВІДКРИЄ понад заплановане.
    ///
    /// Нічого не застосовує: превью і застосування розведені навмисно, інакше
    /// «подивитися» коштувало б стільки ж, скільки «зробити» (US-2.3).
    /// </summary>
    public sealed class BuildPreview
    {
        /// <summary>
        /// Текст попередження показується ПОРУЧ із кнопкою підтвердження.
        /// Живе тут, а не в UI: правило незворотності — частина правил, і
        /// друга копія тексту в іншому шарі розійшлася б із цією.
        /// </summary>
        public const string IrreversibleWarning =
            "Распределение необратимо: отменить или перераспределить вложенное нельзя.";

        public BuildPlanStatus Status = BuildPlanStatus.Ok;

        /// <summary>Скільки очок просить план і скільки їх є.</summary>
        public int PointCost;
        public int PointsAvailable;

        public readonly List<SkillChange> Skills = new List<SkillChange>();
        public readonly List<StatChange> Stats = new List<StatChange>();
        public readonly List<PerkVerdict> Perks = new List<PerkVerdict>();

        /// <summary>
        /// Що відкриється, якщо план підтвердити, — з перків, яких гравець НЕ
        /// планував. Заради цього списку планувальник і існує: очко витрачається
        /// усвідомлено, коли видно не лише ефект, а й наступні двері.
        /// </summary>
        public readonly List<PerkDefinition> Unlocks = new List<PerkDefinition>();

        public bool CanCommit => Status == BuildPlanStatus.Ok;
    }
}
