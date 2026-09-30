using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>
    /// Шаблони арен (Поправка №14.4, крок C3): карта бою кімнати — рядки тексту
    /// в контенті, а не зигзаг генератора. Ключ — <c>DungeonRoomDefinition.ArenaKey</c>;
    /// немає шаблону — <c>GameSession</c> будує поле генератором, як раніше.
    ///
    /// Легенда (рядок 0 — північ, верх карти; стовпець 0 — захід):
    /// <list type="bullet">
    /// <item><c>.</c> — земля;</item>
    /// <item><c>#</c> — стіна: непрохідна, закриває огляд;</item>
    /// <item><c>l</c> — низька перепона (½ укриття сусідам);</item>
    /// <item><c>H</c> — висока перепона (повне укриття сусідам, закриває огляд);</item>
    /// <item><c>b</c> — бочка з порохом; <c>s</c> — копиця сіна;</item>
    /// <item><c>P</c> — місце бійця загону; <c>E</c> — місце ворога; <c>R</c> — куди прийде підкріплення.</item>
    /// </list>
    /// Місця заповнюються в порядку читання (рядок за рядком, зліва направо).
    /// Детерміновано: той самий шаблон — те саме поле.
    /// </summary>
    public static class ArenaTemplates
    {
        /// <summary>Табір розвідників (кімната «scouts_left_behind»): тин і брили біля загону, бочка й сіно посередині.</summary>
        public const string CampYard = "arena_camp_yard_8x8";

        /// <summary>Двір скиту (кімната «hermitage_watch»): стіни келій, колоди, бочка біля входу.</summary>
        public const string HermitageYard = "arena_hermitage_yard_8x8";

        private static readonly Dictionary<string, string[]> Templates = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [CampYard] = new[]
            {
                "..s.....",
                "........",
                ".P......",
                "..b.l...",
                ".P......",
                "......E.",
                ".P.lE...",
                ".......R",
            },
            [HermitageYard] = new[]
            {
                "##....##",
                ".P......",
                "..l.....",
                ".P....E.",
                "........",
                ".P..b...",
                "......s.",
                "##....R#",
            },
        };

        public static IEnumerable<string> Keys => Templates.Keys;

        public static bool TryGet(string key, out string[] rows)
        {
            rows = null;
            return !string.IsNullOrEmpty(key) && Templates.TryGetValue(key, out rows);
        }

        /// <summary>Скільки місць кожного виду в шаблоні — для охоронця контенту.</summary>
        public static void CountSlots(string[] rows, out int players, out int enemies, out int reinforcements)
        {
            players = enemies = reinforcements = 0;
            foreach (var row in rows)
                foreach (char c in row)
                {
                    if (c == 'P') players++;
                    else if (c == 'E') enemies++;
                    else if (c == 'R') reinforcements++;
                }
        }

        /// <summary>
        /// Поле з шаблону: розміри, стіни, об'єкти, місця загону й ворогів, місця
        /// підкріплень. Кидає виняток, якщо місць менше, ніж бійців, — так
        /// поганий контент ламає охоронця, а не бій.
        /// </summary>
        public static BattleSetup Build(string[] rows, IReadOnlyList<string> partyIds, IReadOnlyList<string> enemyIds,
            HitRuleKind hitRule, BattleOpening opening,
            IReadOnlyList<(int round, string enemyId)> reinforcements = null)
        {
            if (rows == null || rows.Length == 0) throw new ArgumentException("Порожній шаблон арени", nameof(rows));
            int height = rows.Length;
            int width = rows[0].Length;

            var setup = new BattleSetup { Width = width, Height = height, HitRule = hitRule, Opening = opening };
            var playerSlots = new List<GridPos>();
            var enemySlots = new List<GridPos>();
            var reinforcementSlots = new List<GridPos>();

            for (int row = 0; row < height; row++)
            {
                if (rows[row].Length != width) throw new ArgumentException($"Рядок {row} шаблону іншої довжини", nameof(rows));
                int y = height - 1 - row;
                for (int x = 0; x < width; x++)
                {
                    var pos = new GridPos(x, y);
                    switch (rows[row][x])
                    {
                        case '.': break;
                        case '#': setup.Walls.Add(new WallPlacement(pos)); break;
                        case 'l': setup.Objects.Add(new MapObjectPlacement(MapObjectKind.LowCover, pos)); break;
                        case 'H': setup.Objects.Add(new MapObjectPlacement(MapObjectKind.HighCover, pos)); break;
                        case 'b': setup.Objects.Add(new MapObjectPlacement(MapObjectKind.PowderKeg, pos)); break;
                        case 's': setup.Objects.Add(new MapObjectPlacement(MapObjectKind.Haystack, pos)); break;
                        case 'P': playerSlots.Add(pos); break;
                        case 'E': enemySlots.Add(pos); break;
                        case 'R': reinforcementSlots.Add(pos); break;
                        default: throw new ArgumentException($"Невідомий символ шаблону «{rows[row][x]}»", nameof(rows));
                    }
                }
            }

            int partyCount = partyIds?.Count ?? 0;
            int enemyCount = enemyIds?.Count ?? 0;
            if (partyCount > playerSlots.Count) throw new InvalidOperationException("У шаблоні замало місць P для загону");
            if (enemyCount > enemySlots.Count) throw new InvalidOperationException("У шаблоні замало місць E для ворогів");

            for (int i = 0; i < partyCount; i++) setup.PlayerUnits.Add(new PlayerSpawn(partyIds[i], playerSlots[i]));
            for (int i = 0; i < enemyCount; i++) setup.EnemyUnits.Add(new EnemySpawn(enemyIds[i], enemySlots[i]));

            if (reinforcements != null)
                for (int i = 0; i < reinforcements.Count; i++)
                {
                    // Без свого місця R — біля останнього місця ворогів (CombatState знайде найближчу вільну).
                    var at = i < reinforcementSlots.Count ? reinforcementSlots[i]
                        : enemySlots.Count > 0 ? enemySlots[enemySlots.Count - 1] : new GridPos(width - 1, 0);
                    setup.Reinforcements.Add(new ReinforcementSpawn(reinforcements[i].round, reinforcements[i].enemyId, at));
                }

            return setup;
        }
    }
}
