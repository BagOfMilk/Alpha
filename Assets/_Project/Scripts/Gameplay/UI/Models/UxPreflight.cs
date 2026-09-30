using System.Collections.Generic;
using Game.Core.Characters.Build;
using Game.Core.Characters.Creation;
using Game.Core.Session;
using Game.Gameplay.Text;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// М'яка перевірка перед «Почати день» (docs/UX_DESIGN.md §5.3, UX-12):
    /// що гравець, найімовірніше, забув. Порожньо — день починається одразу;
    /// інакше діалог «Почати все одно» / «Повернутися». Лише публічні вигляди,
    /// жодного числа прихованої шкали (інв. 3).
    /// </summary>
    public static class UxPreflight
    {
        public static List<string> Check(GameSession session, Gender g)
        {
            var warnings = new List<string>();
            if (session == null) return warnings;
            if (session.State != SessionState.Morning && session.State != SessionState.FreePlay) return warnings;

            var city = session.GetCityView();
            var roster = session.GetRosterView();
            if (city?.OpenPosts != null)
            {
                var empty = new List<string>();
                foreach (var postId in city.OpenPosts)
                    if (Walk.VillagePlaces.OccupantOf(roster, postId) == null)
                        empty.Add(UkrainianText.Get("post." + postId, g));
                bool someoneFree = false;
                if (roster?.Companions != null)
                    foreach (var c in roster.Companions)
                        if (string.IsNullOrEmpty(c.AssignedSlotId) && Walk.VillagePeople.IsInVillage(c)) { someoneFree = true; break; }
                if (empty.Count > 0 && someoneFree)
                    warnings.Add(UkrainianText.Format("ux.preflight.empty_posts", g, "posts", string.Join(", ", empty)));
            }

            try
            {
                var preview = session.PreviewBuildPlan(GameSession.ProtagonistId, new BuildPlan());
                if (preview != null && preview.PointsAvailable > 0)
                    warnings.Add(UkrainianText.Format("ux.preflight.points", g, "n", preview.PointsAvailable.ToString()));
            }
            catch (System.InvalidOperationException) { }
            return warnings;
        }
    }
}
