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
            if (RingStage3D.ReadArg("-ring-locotest") != null) { yield return LocoTest(stage, folder); yield break; }

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
            // Камера на рівні мату біля пари (-ring-demo-feet) — перевірка стоп: ковзання, крок, контакт із матом.
            if (RingStage3D.ReadArg("-ring-demo-feet") != null) stage.LookbookFeet(true);
            yield return Shot(folder, "match_idle", 1.0f);

            var P = MatchSide.Player; var R = MatchSide.Rival;
            string ev = RingStage3D.ReadArg("-ring-demo-every");
            float every;
            if (ev != null && float.TryParse(ev, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out every)) _every = Mathf.Clamp(every, 0.04f, 2f);
            dir.Enqueue(Ev(MatchEventKind.Action, P, MatchActionId.Strike, RollTier.Partial));
            yield return Watch(dir, folder, "strike");
            dir.Enqueue(Ev(MatchEventKind.Action, R, MatchActionId.Strike, RollTier.Strong));
            yield return Watch(dir, folder, "bigboot");
            // Кидки в канати й у кут (огляд 08.11.2026, пункт 8): частково — спина в канат і зупинка; чисто — відскок і через
            // ринг у протилежні канати; у кут; промах у кут — реверс. Між ними — повернення в центр (кінець ходу).
            dir.Enqueue(Ev(MatchEventKind.Action, P, MatchActionId.IntoRopes, RollTier.Partial,
                new MatchEffect(MatchEffectKind.ZoneChange, R, (int)Zone.Ropes)));
            yield return Watch(dir, folder, "ropes_partial");
            dir.Enqueue(Ev(MatchEventKind.TurnEnd, R, MatchActionId.CatchBreath, RollTier.Miss, new MatchEffect(MatchEffectKind.ZoneChange, R, (int)Zone.Center)));
            yield return Watch(dir, folder, "back1");
            dir.Enqueue(Ev(MatchEventKind.Action, P, MatchActionId.IntoRopes, RollTier.Strong,
                new MatchEffect(MatchEffectKind.ZoneChange, R, (int)Zone.Ropes)));
            yield return Watch(dir, folder, "ropes_strong");
            dir.Enqueue(Ev(MatchEventKind.TurnEnd, R, MatchActionId.CatchBreath, RollTier.Miss, new MatchEffect(MatchEffectKind.ZoneChange, R, (int)Zone.Center)));
            yield return Watch(dir, folder, "back2");
            dir.Enqueue(Ev(MatchEventKind.Action, P, MatchActionId.IntoCorner, RollTier.Partial,
                new MatchEffect(MatchEffectKind.ZoneChange, R, (int)Zone.Corner)));
            yield return Watch(dir, folder, "corner");
            dir.Enqueue(Ev(MatchEventKind.TurnEnd, R, MatchActionId.CatchBreath, RollTier.Miss, new MatchEffect(MatchEffectKind.ZoneChange, R, (int)Zone.Center)));
            yield return Watch(dir, folder, "back3");
            dir.Enqueue(Ev(MatchEventKind.Action, P, MatchActionId.IntoCorner, RollTier.Miss,
                new MatchEffect(MatchEffectKind.ZoneChange, P, (int)Zone.Corner)));
            yield return Watch(dir, folder, "corner_miss");
            dir.Enqueue(Ev(MatchEventKind.TurnEnd, P, MatchActionId.CatchBreath, RollTier.Miss, new MatchEffect(MatchEffectKind.ZoneChange, P, (int)Zone.Center)));
            yield return Watch(dir, folder, "back4");
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
            dir.Enqueue(Ev(MatchEventKind.Action, P, MatchActionId.Finisher, RollTier.Strong,
                new MatchEffect(MatchEffectKind.Down, R, 1), new MatchEffect(MatchEffectKind.ZoneChange, R, (int)Zone.Center)));
            yield return Watch(dir, folder, "finisher");
            dir.Enqueue(Ev(MatchEventKind.Kickout, R, MatchActionId.Pin, RollTier.Miss));
            yield return Watch(dir, folder, "pin3");
            dir.Enqueue(new MatchEvent { Kind = MatchEventKind.MatchEnd, Actor = P, Action = MatchActionId.CatchBreath });
            yield return Watch(dir, folder, "end");
            yield return Shot(folder, "end_hold", 1.5f);
            Debug.Log("[Ринг] Демо 3D: знято " + _n + " кадрів");
            Debug.Log(stage.PlayedReport());
            HardExit.Now(0);
        }

        /// <summary>
        /// Налагодження шару руху (<c>-ring-demo &lt;тека&gt; -ring-locotest 1</c>): одна постать, по кадру — входи мікшера,
        /// слот і таз; хода → стійка → біг (з перемотуванням петлі) → вхід у канат. Стрибок тазу за кадр позначено «!».
        /// </summary>
        private static IEnumerator LocoTest(RingStage3D stage, string folder)
        {
            var dir = stage.BeginDemoMatch();
            var a = stage.Actor(MatchSide.Player);
            stage.Place(a, new Vector3(-1.2f, RingStage3D.MatY, 0f), 90f, "Ring_Stance_Loop");
            yield return new WaitForSeconds(0.6f);
            System.Func<string, float, IEnumerator> watch = (tag, secs) => WatchLoco(a, tag, secs, folder);
            // Хода режисера: уперед, убік (кружляння), назад — з покадровими стопами.
            foreach (var target in new[] { new Vector4(0.2f, 0f, 0f, 90f), new Vector4(0.2f, 0f, 0.9f, 90f), new Vector4(-0.9f, 0f, 0.7f, 90f) })
            {
                var co = stage.StartCoroutine(dir.DebugWalk(a, new Vector3(target.x, RingStage3D.MatY, target.z), target.w));
                yield return watch(string.Format(System.Globalization.CultureInfo.InvariantCulture, "режисер→({0:0.0},{1:0.0})", target.x, target.z), 3.2f);
            }
            a.PlayAtBlend(stage.Clip("Move_Walk_Fwd_Loop"), 0f, true, a.PosePos, 90f, 0.22f, 2.0f, true);
            yield return watch("хода", 1.6f);
            a.PlayAtBlend(stage.Clip("Ring_Stance_Loop"), 0f, true, a.PosePos, 90f, 0.3f);
            yield return watch("стійка", 0.7f);
            a.PlayAtBlend(stage.Clip("Move_Circle_L_Loop"), 0f, true, a.PosePos, 90f, 0.22f, 2.0f, true);
            yield return watch("кружляння", 1.2f);
            a.PlayAtBlend(stage.Clip("Run_Loop"), 0f, true, a.PosePos, -90f, 0.25f, RingStage3D.PlaySpeed, true);
            yield return watch("біг", 0.15f);
            a.PlayAtBlend(stage.Clip("Rope_Back_L"), 0.3f, false, a.PosePos, -90f, 0.15f, RingStage3D.PlaySpeed, true);
            yield return watch("канат", 1.8f);
            Debug.Log(stage.PlayedReport());
            HardExit.Now(0);
        }

        private static IEnumerator WatchLoco(RingActor a, string tag, float secs, string folder)
        {
            Vector3 last = a.PosePos;
            for (float t = 0f; t < secs; t += Time.deltaTime)
            {
                yield return new WaitForEndOfFrame();
                Vector3 p = a.PosePos;
                float step = new Vector2(p.x - last.x, p.z - last.z).magnitude;
                last = p;
                Vector3 fl, fr;
                float ll = a.FootLift(0, out fl), lr = a.FootLift(1, out fr);
                Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[Ринг] локо {0} {1:0.000}{2} крок {3:0.000} | стопи Л ({5:0.000},{6:0.000}) h{7:0.000} П ({8:0.000},{9:0.000}) h{10:0.000} | {4}",
                    tag, t, step > 0.12f ? " !" : "", step, a.DebugMix(), fl.x, fl.z, ll, fr.x, fr.z, lr));
                if (Mathf.Repeat(t, 0.1f) < Time.deltaTime) Capture(folder, "loco_" + tag);
            }
        }

        /// <summary>Крок кадрів під час дії, с гри (<c>-ring-demo-every</c>; щільно — для перевірки контакту й бігу).</summary>
        private static float _every = 0.55f;

        private static IEnumerator Watch(RingMatchDirector dir, string folder, string tag)
        {
            Debug.Log("[Ринг] демо: сегмент " + tag + " з кадру " + _n);
            yield return null;
            float t = 0f;
            while (dir.IsBusy && t < 25f)
            {
                yield return new WaitForSeconds(_every);
                t += _every;
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
