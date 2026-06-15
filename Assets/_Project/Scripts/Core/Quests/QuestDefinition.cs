using System.Collections.Generic;
using Game.Core.Factions;
using Game.Core.Stats;

namespace Game.Core.Quests
{
    /// <summary>Откуда пришёл квест (US-14.3): живой мир, а не один квестодатель.</summary>
    public enum QuestSource
    {
        NpcSettlement = 0,  // NPC поселения
        NpcLocation = 1,    // NPC авторской локации
        RandomEvent = 2,    // случайное событие
        CouncilBoard = 3,   // доска/совет базы
        TensionIncident = 4 // инцидент «Напряжения» (Эпик 11)
    }

    /// <summary>
    /// Авторская многоэтапная миссия (Эпики 13–14): последовательность этапов с
    /// ветвлением по проверкам и выборам; БЕЗ push-your-luck-экстракции (это не данж).
    /// Гейтинг входа — флаг/полоса фракции/скил. В Unity обернётся ScriptableObject.
    /// </summary>
    public sealed class QuestDefinition
    {
        public string Id;
        public string Title;
        public string GiverFlavor;
        public QuestSource Source;

        // -- Гейтинг входа --
        public string RequiresFlag;
        public string RequiresFaction;
        public FactionBand RequiresBand = FactionBand.Neutral;
        public SkillType RequiresSkill = SkillType.None;
        public int RequiresSkillLevel;

        public readonly List<QuestStage> Stages = new List<QuestStage>();
        public int StartIndex;

        public QuestDefinition(string id, string title, QuestSource source)
        {
            Id = id;
            Title = title;
            Source = source;
        }

        public QuestDefinition Flavor(string flavor) { GiverFlavor = flavor; return this; }
        public QuestDefinition Stage(QuestStage stage) { Stages.Add(stage); return this; }
        public QuestDefinition GateFlag(string flag) { RequiresFlag = flag; return this; }
        public QuestDefinition GateFaction(string factionId, FactionBand band) { RequiresFaction = factionId; RequiresBand = band; return this; }
        public QuestDefinition GateSkill(SkillType skill, int level) { RequiresSkill = skill; RequiresSkillLevel = level; return this; }

        public QuestStage StageAt(int index) => index >= 0 && index < Stages.Count ? Stages[index] : null;

        /// <summary>
        /// Структурная честность (US-13.2): у каждого не-терминального этапа все
        /// переходы ведут на существующие этапы — нет тупиков (провал проверки
        /// всегда куда-то ведёт, в т.ч. утилитарный — на альтернативу).
        /// </summary>
        public bool Validate(out string error)
        {
            error = null;
            if (Stages.Count == 0) { error = "нет этапов"; return false; }

            for (int i = 0; i < Stages.Count; i++)
            {
                var s = Stages[i];
                switch (s.Kind)
                {
                    case QuestStageKind.Check:
                        if (!Valid(s.OnSuccess) || !Valid(s.OnFailure))
                        { error = $"проверка «{s.Id}» ведёт в тупик"; return false; }
                        break;
                    case QuestStageKind.Combat:
                        if (!Valid(s.OnWin) || !Valid(s.OnLoss))
                        { error = $"бой «{s.Id}» ведёт в тупик"; return false; }
                        break;
                    case QuestStageKind.Choice:
                        if (s.Options.Count == 0) { error = $"выбор «{s.Id}» без вариантов"; return false; }
                        for (int o = 0; o < s.Options.Count; o++)
                            if (!Valid(s.Options[o].Next)) { error = $"вариант «{s.Options[o].Label}» ведёт в тупик"; return false; }
                        break;
                }
            }
            return true;
        }

        private bool Valid(int index) => index >= 0 && index < Stages.Count;
    }
}
