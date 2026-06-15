using System.Collections.Generic;
using Game.Core.Combat;
using Game.Core.Items;

namespace Game.Core.Dungeons
{
    /// <summary>
    /// Сид-настройка данжа: пул врагов (бестиарий из DefaultContent) + лут-таблица
    /// (DefaultItems). В новом неймспейсе, чтобы не трогать общий DefaultContent.cs.
    /// </summary>
    public static class DefaultDungeon
    {
        public static DungeonGenerator NewGenerator()
        {
            var enemies = new List<EnemyDefinition>
            {
                Game.Core.DefaultContent.ScavGunner(),
                Game.Core.DefaultContent.FeralGhoul(),
                Game.Core.DefaultContent.RustDrone(),
                Game.Core.DefaultContent.RaiderBruiser()
            };
            return new DungeonGenerator(enemies, DefaultItems.DropTable());
        }

        public static DungeonRun NewRun(int seed) => new DungeonRun(NewGenerator(), new SeededRng(seed));
    }
}
