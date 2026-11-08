using System.Collections;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Налагоджувальний показ 3D (<c>-ring-demo &lt;тека&gt;</c>): без кампанії й UI програє промо з мікрофоном і набір
    /// подій матчу (удар, кидок, утримання, ув'язка, фінішер, кінець), знімаючи кадри. Для огляду пар кліпів.
    /// </summary>
    public static class RingStageDemo
    {
        private static int _n;

        private static MatchEvent Ev(MatchEventKind kind, MatchSide actor, MatchActionId action, RollTier tier, params MatchEffect[] fx)
        {
            var e = new MatchEvent { Kind = kind, Actor = actor, Action = action, HasRoll = true, Tier = tier };
            foreach (var f in fx) e.Effects.Add(f);
            return e;
        }

        public static IEnumerator Run(RingStage3D stage, RingMatchDirector dir, string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            Application.runInBackground = true;
            yield return new WaitForSeconds(1.0f);

            // ---- промо ----
            stage.BeginPromo();
            yield return Shot(folder, "promo_two", 1.2f);
            stage.SetPromoSpeaker("player");
            yield return Shot(folder, "promo_player", 1.6f);
            stage.SetPromoSpeaker("rival");
            yield return Shot(folder, "promo_rival", 1.6f);
            stage.SetPromoSpeaker("player");
            stage.SetPromoSpeaker("rival");
            stage.SetPromoSpeaker("player");
            yield return Shot(folder, "promo_player_again", 1.6f);
            stage.SetPromoSpeaker("narrator");
            yield return Shot(folder, "promo_narrator", 1.6f);

            // ---- матч ----
            dir = stage.BeginDemoMatch();
            yield return Shot(folder, "match_idle", 1.0f);

            var P = MatchSide.Player; var R = MatchSide.Rival;
            dir.Enqueue(Ev(MatchEventKind.Action, P, MatchActionId.Strike, RollTier.Partial));
            yield return Watch(dir, folder, "strike");
            dir.Enqueue(Ev(MatchEventKind.Action, R, MatchActionId.Strike, RollTier.Strong));
            yield return Watch(dir, folder, "bigboot");
            dir.Enqueue(Ev(MatchEventKind.Action, P, MatchActionId.Throw, RollTier.Strong,
                new MatchEffect(MatchEffectKind.Damage, R, 3), new MatchEffect(MatchEffectKind.Down, R, 1)));
            yield return Watch(dir, folder, "bodyslam");
            dir.Enqueue(Ev(MatchEventKind.Kickout, R, MatchActionId.Pin, RollTier.Partial));
            yield return Watch(dir, folder, "cover");
            dir.Enqueue(Ev(MatchEventKind.Action, R, MatchActionId.GetUp, RollTier.Strong,
                new MatchEffect(MatchEffectKind.Stand, R, 1)));
            yield return Watch(dir, folder, "getup");
            dir.Enqueue(Ev(MatchEventKind.Action, R, MatchActionId.Taunt, RollTier.Strong));
            yield return Watch(dir, folder, "taunt");
            dir.Enqueue(Ev(MatchEventKind.Action, P, MatchActionId.IntoRopes, RollTier.Strong,
                new MatchEffect(MatchEffectKind.ZoneChange, R, (int)Zone.Ropes)));
            yield return Watch(dir, folder, "ropes");
            dir.Enqueue(Ev(MatchEventKind.Action, P, MatchActionId.Finisher, RollTier.Strong,
                new MatchEffect(MatchEffectKind.Down, R, 1), new MatchEffect(MatchEffectKind.ZoneChange, R, (int)Zone.Center)));
            yield return Watch(dir, folder, "finisher", Dense ? 0.04f : 0.55f);   // щільно — щоб побачити строб (доступність)
            dir.Enqueue(Ev(MatchEventKind.Kickout, R, MatchActionId.Pin, RollTier.Miss));
            yield return Watch(dir, folder, "pin3");
            dir.Enqueue(new MatchEvent { Kind = MatchEventKind.MatchEnd, Actor = P, Action = MatchActionId.CatchBreath });
            yield return Watch(dir, folder, "end");
            yield return Shot(folder, "end_hold", 1.5f);
            Debug.Log("[Ринг] Демо 3D: знято " + _n + " кадрів");
            HardExit.Now(0);
        }

        /// <summary><c>-ring-demo-dense</c>: фінішер знімається кожні 0,04 с (огляд перемикача «Спалахи»).</summary>
        private static bool Dense { get { return System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ring-demo-dense") >= 0; } }

        private static IEnumerator Watch(RingMatchDirector dir, string folder, string tag, float step = 0.55f)
        {
            yield return null;
            float t = 0f;
            while (dir.IsBusy && t < 20f)
            {
                if (step < 0.5f) yield return new WaitForSecondsRealtime(step);   // щільно: у реальному часі, крізь сповільнення
                else yield return new WaitForSeconds(step);
                t += step;
                Capture(folder, tag);
            }
        }

        private static IEnumerator Shot(string folder, string tag, float wait)
        {
            yield return new WaitForSeconds(wait);
            Capture(folder, tag);
            yield return new WaitForSeconds(0.2f);
        }

        private static void Capture(string folder, string tag)
        {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, string.Format("{0:000}_{1}.png", _n++, tag)));
        }
    }
}
