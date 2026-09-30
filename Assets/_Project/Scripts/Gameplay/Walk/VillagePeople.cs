using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Session;
using Game.Core.Session.Views;

namespace Game.Gameplay.Walk
{
    /// <summary>Де стоїть людина в селі: на своєму посту чи біля вогнища Віча.</summary>
    public sealed class PersonSpot
    {
        public string CompanionId;
        public float X;
        public float Z;
        /// <summary>Стоїть на посту (біля дверей своєї будівлі); інакше — біля вогнища Віча.</summary>
        public bool AtPost;
        /// <summary>Номер місця біля вогнища (0..), або −1 для поста.</summary>
        public int IdleIndex = -1;
    }

    /// <summary>
    /// Хто з людей стоїть у селі і де (власник, 30.09.2026: «досі неможна …
    /// поговорити з персонажем»). На посту — біля дверей своєї будівлі; без
    /// поста — біля вогнища Віча, доки є місця. Хто поза селом (вилазка),
    /// загинув, пішов, у полоні чи ще не прибився — у селі не стоїть. Одна
    /// функція для сцени (фігури) і для місць розмови — фігура і місце завжди
    /// збігаються. Детермінована: порядок — порядок ростера.
    /// </summary>
    public static class VillagePeople
    {
        /// <summary>Скільки людей уміщається біля вогнища Віча.</summary>
        public const int IdleSpotCount = 4;

        /// <summary>Людина зараз у селі (і з нею можна говорити).</summary>
        public static bool IsInVillage(CompanionSummary c)
        {
            if (c == null || c.Id == GameSession.ProtagonistId) return false;
            switch (c.Status)
            {
                case CompanionStatus.Idle:
                case CompanionStatus.Assigned:
                case CompanionStatus.Injured:
                case CompanionStatus.Resting:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Розставляє людей: <paramref name="postFigures"/> — де стоїть фігура
        /// працівника кожного поста; <paramref name="idleSpots"/> — місця біля
        /// вогнища. Людина на посту без фігури (напр. дослідницький стіл без
        /// ділянки) іде до вогнища.
        /// </summary>
        public static List<PersonSpot> Arrange(RosterView roster,
            IReadOnlyDictionary<string, WalkPoint> postFigures, IReadOnlyList<WalkPoint> idleSpots)
        {
            var spots = new List<PersonSpot>();
            if (roster?.Companions == null) return spots;
            int idle = 0;
            foreach (var c in roster.Companions)
            {
                if (!IsInVillage(c)) continue;
                WalkPoint at;
                if (!string.IsNullOrEmpty(c.AssignedSlotId) && postFigures != null &&
                    postFigures.TryGetValue(c.AssignedSlotId, out at))
                {
                    spots.Add(new PersonSpot { CompanionId = c.Id, X = at.X, Z = at.Z, AtPost = true });
                    continue;
                }
                if (idleSpots == null || idle >= idleSpots.Count || idle >= IdleSpotCount) continue;
                var spot = idleSpots[idle];
                spots.Add(new PersonSpot { CompanionId = c.Id, X = spot.X, Z = spot.Z, IdleIndex = idle });
                idle++;
            }
            return spots;
        }
    }
}
