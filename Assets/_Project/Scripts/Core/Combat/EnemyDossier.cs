using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Core.Combat
{
    /// <summary>
    /// Що громада знає про ворога (Поправка №14.6, інваріант 6):
    /// ЯКІР — наскільки відкрита картка ворога;
    /// СПОЖИВАЧ — бойовий HUD: на контакті лише роль і здоров'я, решта «?», прев'ю
    /// шкоди з «?»; вивчений — прийоми, опори, умова здачі;
    /// СИГНАЛ — «досьє поповнено» у стрічці (<c>dossier.studied</c>).
    /// Непередбачуваність — з неповноти інформації (статут MECH-02), а не з кубика.
    /// </summary>
    public enum DossierLevel { Unknown = 0, Contact = 1, Studied = 2 }

    /// <summary>
    /// Книга досьє: рівень знання на тип ворога (<see cref="EnemyDefinition.Id"/>).
    /// Знання не спадає. Зліпок — без ';' і '=' (заголовок сейву).
    /// </summary>
    public sealed class EnemyDossierBook
    {
        private readonly Dictionary<string, DossierLevel> _levels = new Dictionary<string, DossierLevel>(StringComparer.Ordinal);

        public DossierLevel LevelOf(string enemyDefinitionId) =>
            !string.IsNullOrEmpty(enemyDefinitionId) && _levels.TryGetValue(enemyDefinitionId, out var l) ? l : DossierLevel.Unknown;

        /// <summary>Підняти знання до <paramref name="to"/>; true — саме цим викликом стало більше.</summary>
        public bool Raise(string enemyDefinitionId, DossierLevel to)
        {
            if (string.IsNullOrEmpty(enemyDefinitionId) || LevelOf(enemyDefinitionId) >= to) return false;
            _levels[enemyDefinitionId] = to;
            return true;
        }

        public string CaptureState()
        {
            var keys = new List<string>(_levels.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var k in keys)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(k).Append(':').Append(((int)_levels[k]).ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            _levels.Clear();
            if (string.IsNullOrEmpty(blob)) return;
            foreach (var entry in blob.Split(','))
            {
                int colon = entry.LastIndexOf(':');
                if (colon <= 0) continue;
                if (int.TryParse(entry.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    _levels[entry.Substring(0, colon)] = (DossierLevel)Math.Max(0, Math.Min(2, v));
            }
        }
    }
}
