using System.Collections.Generic;
using System.Globalization;
using Game.Core.Session.Views;

namespace Game.Gameplay
{
    /// <summary>
    /// Розбір шляху руху з журналу бою (docs/COMBAT_V2.md §7.3): рядок
    /// <c>combat.log.move</c> несе <c>args["path"]</c> як <c>"x,y;x,y;…"</c>
    /// БЕЗ стартового тайла. Чистий C# (жодного типу рушія — <see cref="GridPosView"/>
    /// із Core теж без залежності від UnityEngine), тому лінтиться і
    /// перевіряється headless-тестами; <see cref="BattleArenaController"/>
    /// (такти, §6) лише йде по вже розібраному списку.
    /// </summary>
    public static class BattleTactParser
    {
        /// <summary>Порожній рядок/<c>null</c> — порожній шлях (жоден крок не відтворюється, а не виняток). Токен, що не розбирається як "x,y" — пропускається, а не рве весь шлях.</summary>
        public static IReadOnlyList<GridPosView> ParsePath(string raw)
        {
            var result = new List<GridPosView>();
            if (string.IsNullOrEmpty(raw)) return result;

            foreach (var segment in raw.Split(';'))
            {
                if (string.IsNullOrEmpty(segment)) continue;
                var parts = segment.Split(',');
                if (parts.Length != 2) continue;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)) continue;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) continue;
                result.Add(new GridPosView(x, y));
            }
            return result;
        }

        /// <summary>Той самий шлях назад у рядок "x,y;x,y;…" — головним чином для тестів (round-trip) і діагностики.</summary>
        public static string FormatPath(IReadOnlyList<GridPosView> tiles)
        {
            if (tiles == null || tiles.Count == 0) return string.Empty;
            var parts = new string[tiles.Count];
            for (int i = 0; i < tiles.Count; i++)
                parts[i] = tiles[i].X.ToString(CultureInfo.InvariantCulture) + "," + tiles[i].Y.ToString(CultureInfo.InvariantCulture);
            return string.Join(";", parts);
        }
    }
}
