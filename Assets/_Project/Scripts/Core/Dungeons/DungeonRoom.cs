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

        // -- Event (мини-событие) --
        public string EventId;
        public int EventLootGold;      // напр. «тайник»
        public int EventThreatDelta;   // напр. «обвал/засада» → опаснее дальше

        public DungeonRoom(RoomType type, int depth)
        {
            Type = type;
            Depth = depth;
        }
    }
}
