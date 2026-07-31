using System.Collections.Generic;
using Game.Core.Combat;
using Game.Core.Items;
using Game.Core.Stats;

namespace Game.Core.Dungeons
{
    /// <summary>Тип комнаты данжа (US-12.3): бой / лут / проверка / мини-событие.</summary>
    public enum RoomType
    {
        Combat = 0,
        Loot = 1,
        Check = 2,
        Event = 3
    }

    /// <summary>
    /// Комната процедурного данжа (Эпик 12). Собирается генератором из «модулей»;
    /// поля заполняются по типу. Бой ведёт вызывающий код (CombatState) — комната
    /// лишь несёт состав врагов; остальное резолвит DungeonRun.
    /// </summary>
    public sealed class DungeonRoom
    {
        public RoomType Type;
        public int Depth;
        public string DisplayName;

        // -- Combat --
        public readonly List<EnemyDefinition> Enemies = new List<EnemyDefinition>();
        public bool Elite; // порог угрозы → подкрепления/элита (US-12.2)

        // -- Лут (Loot и награда за Combat) --
        public int LootGold;
        public int LootBuilding;
        public int LootCrafting;
        public int ItemDrops;          // сколько предметов катать
        public LootTable ItemTable;    // из чего катать

        // -- Check (US-12.3, US-2.6) --
        public SkillType CheckSkill = SkillType.None;
        public int CheckThreshold;
        public int CheckRewardGold;    // успех → в незабанкованное

        // -- Event (мини-событие С ВЫБОРОМ, US-12.3) --
        public string EventId;
        public int EventThreatDelta;   // используется и Check-провалом («дальше опаснее»)
        public readonly List<DungeonEventOption> EventOptions = new List<DungeonEventOption>();

        public DungeonRoom(RoomType type, int depth)
        {
            Type = type;
            Depth = depth;
        }
    }

    /// <summary>
    /// Вариант мини-события (US-12.3): у каждого свой размен риск/награда — золото в
    /// незабанкованное против роста угрозы (шум/время). Резолвит DungeonRun.ResolveEvent.
    /// </summary>
    public sealed class DungeonEventOption
    {
        public string Label;
        public int GoldGain;     // в незабанкованное (банк только на экстракте)
        public int ThreatDelta;  // шум → дальше опаснее

        public DungeonEventOption(string label, int goldGain, int threatDelta)
        {
            Label = label;
            GoldGain = goldGain;
            ThreatDelta = threatDelta;
        }
    }
}
