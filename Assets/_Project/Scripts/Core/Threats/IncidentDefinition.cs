using System;
using Game.Core.Stats;

namespace Game.Core.Threats
{
    /// <summary>Серьёзность инцидента — растёт с полосой Напряжения (US-11.1).</summary>
    public enum IncidentSeverity
    {
        Minor = 0,     // мелкие инциденты (доступны с Calm)
        Organized = 1, // организованная преступность (с Tense)
        Crisis = 2     // кризис-события (с Critical)
    }

    /// <summary>Жёсткий непредотвратимый исход кризиса (US-11.1).</summary>
    public enum CrisisEffect
    {
        None = 0,
        PopulationExodus = 1, // отток населения
        KillCompanion = 2,    // гибель напарника (не протагониста)
        HostileFaction = 3    // открывается враждебная группировка (вход в Эпик 10)
    }

    /// <summary>
    /// Инцидент «Напряжения» (US-11.3): выбирается по весам в допустимой полосе,
    /// резолвится релевантным напарником НА ПОЗИЦИИ (US-8.2: позицию с нужным
    /// скилом никто не держит → базовый худший исход). Исход двигает Напряжение в
    /// обе стороны. Чистые данные — в Unity обернётся ScriptableObject.
    /// </summary>
    [Serializable]
    public sealed class IncidentDefinition
    {
        public string Id;
        public string DisplayName;
        public IncidentSeverity Severity = IncidentSeverity.Minor;
        public int Weight = 1;

        /// <summary>Скил резолва: ищется занятая позиция с этим RelevantSkill (US-8.2).</summary>
        public SkillType Skill = SkillType.None;
        public int Threshold = 2; // детерминированная проверка (US-2.6)

        /// <summary>Дельты Напряжения: успех обычно снижает, провал — поднимает.</summary>
        public double TensionOnSuccess = -3;
        public double TensionOnFailure = 4;

        /// <summary>Для кризисов: жёсткий эффект, срабатывает НЕЗАВИСИМО от проверки.</summary>
        public CrisisEffect Crisis = CrisisEffect.None;

        public IncidentDefinition() { }

        public IncidentDefinition(string id, string displayName, IncidentSeverity severity)
        {
            Id = id;
            DisplayName = displayName;
            Severity = severity;
        }
    }

    /// <summary>Что случилось за тик: для UI/лога (число Напряжения наружу не отдаём).</summary>
    public sealed class IncidentReport
    {
        public string IncidentId;
        public string DisplayName;
        public IncidentSeverity Severity;

        /// <summary>Кто резолвил (null — релевантную позицию никто не держал → худший исход).</summary>
        public string ResolvedById;
        public bool Success;

        public CrisisEffect CrisisApplied = CrisisEffect.None;
        public string CrisisVictimId; // для KillCompanion
    }
}
