using System;
using Game.Core.Balance;
using Game.Core.Stats;

namespace Game.Core.Characters
{
    public enum CompanionStatus
    {
        Idle = 0,      // в резерве, свободен
        Assigned = 1,  // назначен на позицию базы
        OnMission = 2, // в вылазке (Даж)
        Injured = 3,   // ранен, нужно восстановление
        Resting = 4    // отдыхает/лечится в лазарете
    }

    /// <summary>
    /// Рантайм-экземпляр напарника. Хранит прогресс (уровень/опыт),
    /// накопленные за уровни статы, текущий статус и привязку к слоту базы.
    /// Эффективные статы = стартовые (архетип) + накопленные за уровни.
    /// Экипировка/трейты накладываются поверх отдельно (см. EffectiveStats).
    /// </summary>
    public sealed class Companion
    {
        public string Id { get; }
        public CompanionArchetype Archetype { get; }
        public string DisplayName { get; set; }

        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }

        public CompanionStatus Status { get; set; } = CompanionStatus.Idle;

        /// <summary>Id слота базы, на который назначен (null если не назначен).</summary>
        public string AssignedSlotId { get; internal set; }

        /// <summary>Текущее «здоровье восстановления»: 0 = здоров, >0 = лечится.</summary>
        public double InjuryPoints { get; internal set; }

        /// <summary>Статы, накопленные за повышения уровня (без архетипа/экипировки).</summary>
        private readonly StatBlock _gainedStats = new StatBlock();

        public Companion(string id, CompanionArchetype archetype)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
            DisplayName = archetype.DisplayName;
        }

        /// <summary>Эффективные базовые статы (архетип + рост за уровни).</summary>
        public StatBlock EffectiveStats => Archetype.BaseStats.Plus(_gainedStats);

        public int GetStat(StatType stat) => EffectiveStats.Get(stat);

        public bool IsAssigned => !string.IsNullOrEmpty(AssignedSlotId);
        public bool IsInjured => InjuryPoints > 0.0;

        /// <summary>
        /// Начисляет опыт и применяет повышения уровня, выдавая статы по
        /// ростовому профилю архетипа. Возвращает результат для UI/уведомлений.
        /// </summary>
        public ProgressionMath.LevelUpResult GainXp(int amount, BalanceConfig cfg)
        {
            var result = ProgressionMath.GrantXp(Level, Xp, amount, cfg);
            if (result.LeveledUp)
            {
                int points = result.LevelsGained * cfg.StatPointsPerLevel;
                var allocated = Archetype.Growth.AllocatePoints(points);
                _gainedStats.AddFrom(allocated);
            }
            Level = result.Level;
            Xp = result.RemainderXp;
            return result;
        }
    }
}
