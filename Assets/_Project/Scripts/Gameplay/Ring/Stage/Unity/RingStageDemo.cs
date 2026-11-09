using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Налагоджувальний показ 3D (<c>-ring-demo &lt;тека&gt;</c>): без кампанії й UI програє промо з мікрофоном і набір
    /// подій матчу (удар, склейки «канати / кут», вихід за ринг і бій там, повернення, кидок, утримання, фінішер, кінець),
    /// знімаючи кадри. Для огляду пар кліпів. <c>-ring-outside 1</c> — статичний огляд місць поза центром (за рингом, на
    /// канаті, на куті), <c>-ring-venue hall|arena</c> — інший зал.
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

        /// <summary>Подія прийому кар'єри з постановкою (як від MatchEngine.Perform з MoveHint).</summary>
        private static MatchEvent Move(MatchSide actor, MatchActionId action, RollTier tier, string key, MoveStaging staging, params MatchEffect[] fx)
        {
            var e = Ev(MatchEventKind.Action, actor, action, tier, fx);
            e.MoveKey = key;
            e.Staging = staging;
            return e;
        }

        /// <summary>Подія, що почалась за рингом (MatchEvent.Outside — як у ядра).</summary>
        private static MatchEvent Out(MatchEvent e) { e.Outside = true; return e; }

        // ============================ огляд «за рингом» ============================

        /// <summary>
        /// Статичний огляд місць поза центром (<c>-ring-demo &lt;тека&gt; -ring-outside 1 [-ring-venue hall|arena]</c>), поки
        /// режисер не вміє кроків Cut/Place: постаті ставляться напряму <see cref="RingStage3D.PairPos"/> і
        /// <see cref="RingStage3D.PerchPos"/>, кадр — планами <see cref="RingCine.OutsideShot"/> / <see cref="RingCine.PerchShot"/>.
        /// За рингом — стійка, кидок і ларіат пари (кліп пари розкладено навпіл довкола центру смуги) і загальний план; на
        /// канаті й на куті — кліп склейки з першого кадру з перевіркою, де таз відносно каната.
        /// </summary>
        private static IEnumerator OutsideReview(RingStage3D stage, string folder)
        {
            stage.BeginDemoMatch();
            string v = RingStage3D.Venue.Id;
            var o = RingStage3D.Outside;
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] огляд за рингом: {0}; бік {1}, центр ({2:0.00}; {3:0.00}), смуга ±{4:0.0} м, вільна: {5}",
                RingStage3D.Venue, o.Side, o.X, o.Z, o.HalfLength, o.Ok ? "так" : "НІ"));
            RingActor pl = stage.Actor(MatchSide.Player), rv = stage.Actor(MatchSide.Rival), rf = stage.Referee;
            // Рефері — у рингу біля канатів з боку пари, обличчям до неї (рахує відлік).
            Vector3 refAt = RingStage3D.OutsideOut * (RingStage3D.RingHalf - 0.45f) + RingStage3D.OutsideAlong * o.Shift;
            refAt.y = RingStage3D.MatY;

            // 1) Пара в стійці за рингом.
            Vector3 pp = RingStage3D.PairPos(MatchSide.Player, true), rp = RingStage3D.PairPos(MatchSide.Rival, true);
            stage.Place(pl, pp, RingStage3D.PairYaw(MatchSide.Player, true), "Ring_Stance_Loop");
            stage.Place(rv, rp, RingStage3D.PairYaw(MatchSide.Rival, true), "Ring_Stance_Loop");
            stage.Place(rf, refAt, RingStage3D.YawTo(refAt, RingStage3D.OutsideAnchor), "Ref_Idle_Loop");
            Forget(stage);
            yield return OutsideFrame(stage, folder, v + "_outside_stance", 0.8f);
            stage.SetShot(StageCam.Overview);
            yield return Shot(folder, v + "_outside_overview", 0.8f);

            // 2) Кидок і ларіат пари за рингом (кліпи ядра для Throw і Weapon): кадри на третині й двох третинах кліпа.
            foreach (string clip in new[] { "Bodyslam", "Center_Lariat_Heavy" })
            {
                if (!PairOutside(stage, clip)) continue;
                var ca = stage.Clip(clip + "_A");
                float len = ca != null ? ca.length / RingStage3D.PlaySpeed : 2f;
                yield return OutsideFrame(stage, folder, v + "_outside_" + clip + "_a", len * 0.33f);
                yield return new WaitForSeconds(len * 0.3f);
                Capture(folder, v + "_outside_" + clip + "_b");
                yield return new WaitForSeconds(len * 0.45f);
                Capture(folder, v + "_outside_" + clip + "_end");
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] огляд за рингом: {0} — таз A ({1:0.00}; {2:0.00}), B ({3:0.00}; {4:0.00}); у смузі: {5}",
                    clip, pl.PosePos.x, pl.PosePos.z, rv.PosePos.x, rv.PosePos.z, InBand(pl.PosePos) && InBand(rv.PosePos) ? "так" : "НІ"));
            }

            // 3) На канаті й на куті (план 1 склейки): гравець — відскок від свого каната, суперник — вихід зі свого кута.
            stage.Place(pl, RingStage3D.PairPos(MatchSide.Player, false), 90f, "Ring_Stance_Loop");
            stage.Place(rv, RingStage3D.PairPos(MatchSide.Rival, false), -90f, "Ring_Stance_Loop");
            stage.Place(rf, RingStage3D.RefPos, 180f, "Ref_Idle_Loop");
            Forget(stage);
            yield return null;
            foreach (var perch in new[] { new KeyValuePair<MatchSide, MoveStaging>(MatchSide.Player, MoveStaging.Ropes),
                                          new KeyValuePair<MatchSide, MoveStaging>(MatchSide.Rival, MoveStaging.Corner) })
            {
                var a = stage.Actor(perch.Key);
                var victim = stage.Actor(RingClipTable.Opp(perch.Key));
                string clip = RingClipTable.DefaultPerch(perch.Value);
                float yaw;
                Vector3 at = stage.PerchPos(perch.Key, perch.Value, clip, out yaw);
                var c = stage.Clip(clip, stage.IsFemale(a));
                if (c == null) { Debug.LogWarning("[Ринг] огляд: немає кліпа " + clip); continue; }
                a.Slot.position = new Vector3(at.x, at.y, at.z);
                a.SetHeight(at.y);
                a.PlayAt(c, false, at, yaw, RingStage3D.PlaySpeed);
                stage.Watch.Forget(a);
                var pts = new List<Vector3>();
                RingCine.Body(pts, new Vector3(at.x, at.y, at.z), stage.BodyScale(a), true);
                RingCine.Body(pts, victim.PosePos, stage.BodyScale(victim));
                var shot = RingCine.PerchShot(pts, at, victim.PosePos, stage.CamAspect);
                if (shot.Ok) stage.Cine(shot.Pos, shot.Look, shot.Fov, true);
                else { Debug.LogWarning("[Ринг] огляд: план склейки не вмістився — " + clip); stage.SetShot(StageCam.Low); }
                string tag = v + "_perch_" + (perch.Value == MoveStaging.Ropes ? "ropes" : "corner");
                float deep = 0f, len = c.length / RingStage3D.PlaySpeed;
                Vector3 axis = perch.Value == MoveStaging.Ropes ? Quaternion.Euler(0f, yaw, 0f) * Vector3.forward : new Vector3(Mathf.Sign(at.x), 0f, 1f).normalized;
                int shots = 0;
                for (float t = 0f; t < len + 0.3f; t += Time.deltaTime)
                {
                    yield return null;
                    var hp = a.PosePos;
                    deep = Mathf.Max(deep, Mathf.Max(Mathf.Abs(hp.x), Mathf.Abs(hp.z)));
                    if (shots < 4 && t >= shots * len / 3f) { Capture(folder, tag + "_" + shots); shots++; }
                }
                float rope = RingStage3D.Venue.RopeLine;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[Ринг] огляд склейки: {0} ({1}) — старт тазу ({2:0.00}; {3:0.00}; {4:0.00}), курс {5:0}°, hs {6:0.00}; найдальше від центру {7:0.00} м, канат {8:0.000} (з прогином {9:0.00}) — {10}; вісь ({11:0.00}; {12:0.00})",
                    clip, perch.Key, at.x, at.y, at.z, yaw, stage.BodyScale(a), deep, rope, rope + 0.45f,
                    deep <= rope + 0.45f ? "у канатах" : "ЗА КАНАТАМИ", axis.x, axis.z));
                stage.Place(a, RingStage3D.PairPos(perch.Key, false), perch.Key == MatchSide.Player ? 90f : -90f, "Ring_Stance_Loop");
                stage.Watch.Forget(a);
                yield return null;
            }
            Debug.Log("[Ринг] огляд за рингом: знято " + _n + " кадрів");
            Debug.Log(stage.PlayedReport());
            HardExit.Now(0);
        }

        private static void Forget(RingStage3D stage)
        {
            stage.Watch.Forget(stage.Actor(MatchSide.Player));
            stage.Watch.Forget(stage.Actor(MatchSide.Rival));
            stage.Watch.Forget(stage.Referee);
        }

        /// <summary>Чи таз у смузі пари за рингом (з запасом 0,25 м — кліп пари може хитнути тіло за межу).</summary>
        private static bool InBand(Vector3 p)
        {
            float x0, z0, x1, z1;
            RingStage3D.Outside.Rect(RingStage3D.Venue.Plat, 0.25f, out x0, out z0, out x1, out z1);
            return p.x >= x0 && p.x <= x1 && p.z >= z0 && p.z <= z1;
        }

        /// <summary>
        /// Пару за рингом — у кліп пари: A дивиться вздовж боку, зсув B (бічний і вперед) розкладено навпіл довкола центру
        /// смуги, тож обидва в смузі; слоти — на підлозі (мати).
        /// </summary>
        private static bool PairOutside(RingStage3D stage, string clip)
        {
            var ia = stage.Info(clip + "_A");
            var ca = stage.Clip(clip + "_A");
            var cb = stage.Clip(clip + "_B");
            if (ia == null || ca == null || cb == null) { Debug.LogWarning("[Ринг] огляд: немає пари " + clip); return false; }
            RingActor a = stage.Actor(MatchSide.Player), b = stage.Actor(MatchSide.Rival);
            Vector3 along = RingStage3D.OutsideAlong, c = RingStage3D.OutsideAnchor;
            float ya = RingStage3D.YawTo(Vector3.zero, along), yb = ya - ia.Rotation;
            Vector3 off = Quaternion.Euler(0f, ya, 0f) * new Vector3(ia.Offset.x, 0f, ia.Offset.z);
            Vector3 pa = c - off * 0.5f, pb = c + off * 0.5f;
            pa.y = RingStage3D.Venue.GroundAt(pa.x, pa.z);
            pb.y = RingStage3D.Venue.GroundAt(pb.x, pb.z);
            a.Slot.SetPositionAndRotation(pa, Quaternion.Euler(0f, ya, 0f));
            b.Slot.SetPositionAndRotation(pb, Quaternion.Euler(0f, yb, 0f));
            a.SetHeight(pa.y);
            b.SetHeight(pb.y);
            a.Play(ca, false, 0f, RingStage3D.PlaySpeed);
            b.Play(cb, false, 0f, RingStage3D.PlaySpeed);
            Forget(stage);
            return true;
        }

        /// <summary>Кадр «за рингом»: обидва борці, край фартуха біля пари; камера — планом <see cref="RingCine.OutsideShot"/>.</summary>
        private static IEnumerator OutsideFrame(RingStage3D stage, string folder, string tag, float wait)
        {
            yield return null;
            RingActor a = stage.Actor(MatchSide.Player), b = stage.Actor(MatchSide.Rival);
            var pts = new List<Vector3>();
            Vector3 ga = a.PosePos, gb = b.PosePos;
            ga.y = a.Slot.position.y; gb.y = b.Slot.position.y;
            RingCine.Body(pts, ga, stage.BodyScale(a));
            RingCine.Body(pts, gb, stage.BodyScale(b));
            // Край фартуха навпроти пари — у кадрі: видно, що пара за рингом.
            Vector3 apron = RingStage3D.OutsideAnchor - RingStage3D.OutsideOut * RingVenue.PairOut;
            apron.y = RingStage3D.MatY;
            pts.Add(apron);
            var shot = RingCine.OutsideShot(pts, RingStage3D.OutsideAnchor, RingStage3D.OutsideOut, stage.CamAspect);
            if (shot.Ok) stage.Cine(shot.Pos, shot.Look, shot.Fov, true);
            else { Debug.LogWarning("[Ринг] огляд: план «за рингом» не вмістився — " + tag); stage.SetShot(StageCam.Big); }
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] план за рингом {0}: камера ({1:0.00}; {2:0.00}; {3:0.00}) → ({4:0.00}; {5:0.00}; {6:0.00}), кут {7:0}°, {8}",
                tag, shot.Pos.x, shot.Pos.y, shot.Pos.z, shot.Look.x, shot.Look.y, shot.Look.z, shot.Fov, shot.Ok ? "вмістився" : "НЕ вмістився"));
            yield return Shot(folder, tag, wait);
        }

        public static IEnumerator Run(RingStage3D stage, RingMatchDirector dir, string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            Application.runInBackground = true;
            yield return new WaitForSeconds(1.0f);
            if (RingStage3D.ReadArg("-ring-locotest") != null) { yield return LocoTest(stage, folder); yield break; }
            if (RingStage3D.ReadArg("-ring-entrances") != null) { yield return Entrances(stage, folder); yield break; }
            if (RingStage3D.ReadArg("-ring-outside") != null) { yield return OutsideReview(stage, folder); yield break; }

            // ---- промо ----
            bool probe = RingStage3D.ReadArg("-ring-probe") != null;
            // Світло великого шоу (-ring-demo-big) — порівняння з щотижневим на тих самих кадрах.
            if (RingStage3D.ReadArg("-ring-demo-big") != null) stage.SetBigShow(true);
            stage.BeginPromo();
            yield return Shot(folder, "promo_two", 1.2f);
            if (probe) yield return ProbeBright("promo_two");
            stage.SetPromoSpeaker("player");
            yield return Shot(folder, "promo_player", 1.6f);
            if (probe) yield return ProbeBright("promo_player");
            stage.SetPromoSpeaker("rival");
            yield return Shot(folder, "promo_rival", 1.6f);
            if (probe) yield return ProbeBright("promo_rival");
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
            // Склейки «канати / кут» нового ядра (замість зон — постановка прийому кар'єри, MoveStaging): план 1 — нападник на
            // канаті чи в куті (RingStage3D.PerchPos), план 2 — приземлення в центрі. Прийоми — з career_moves.json.
            dir.Enqueue(Move(P, MatchActionId.Strike, RollTier.Strong, "whip", MoveStaging.Ropes,
                new MatchEffect(MatchEffectKind.Damage, R, 2)));
            yield return Watch(dir, folder, "whip_ropes");
            dir.Enqueue(Move(P, MatchActionId.TopRope, RollTier.Strong, "springboard", MoveStaging.Ropes,
                new MatchEffect(MatchEffectKind.Damage, R, 3), new MatchEffect(MatchEffectKind.Down, R, 1)));
            yield return Watch(dir, folder, "springboard_ropes");
            dir.Enqueue(Ev(MatchEventKind.Action, R, MatchActionId.GetUp, RollTier.Strong, new MatchEffect(MatchEffectKind.Stand, R, 1)));
            yield return Watch(dir, folder, "getup_ropes");
            dir.Enqueue(Move(R, MatchActionId.TopRope, RollTier.Miss, "swallow_dive", MoveStaging.Corner,
                new MatchEffect(MatchEffectKind.Down, R, 1)));
            yield return Watch(dir, folder, "swallow_dive_corner_miss");
            dir.Enqueue(Ev(MatchEventKind.Action, R, MatchActionId.GetUp, RollTier.Strong, new MatchEffect(MatchEffectKind.Stand, R, 1)));
            yield return Watch(dir, folder, "getup_corner");
            // За рингом: «Вийти з рингу» (поштовх, склейка — пара на підлозі між фартухом і огорожею), удар, кидок і предмет
            // за рингом (подія з Outside = true, відлік рефері +3), «Повернутися в ринг» (склейка — знову на рингу).
            dir.Enqueue(Ev(MatchEventKind.Action, P, MatchActionId.LeaveRing, RollTier.Strong, new MatchEffect(MatchEffectKind.Place, P, 1)));
            yield return Watch(dir, folder, "leave_ring");
            dir.Enqueue(Out(Ev(MatchEventKind.Action, R, MatchActionId.Strike, RollTier.Partial,
                new MatchEffect(MatchEffectKind.Damage, P, 1), new MatchEffect(MatchEffectKind.Count, R, 3))));
            yield return Watch(dir, folder, "outside_strike");
            dir.Enqueue(Out(Ev(MatchEventKind.Action, P, MatchActionId.Throw, RollTier.Strong,
                new MatchEffect(MatchEffectKind.Damage, R, 3), new MatchEffect(MatchEffectKind.Down, R, 1), new MatchEffect(MatchEffectKind.Count, P, 3))));
            yield return Watch(dir, folder, "outside_throw");
            dir.Enqueue(Out(Ev(MatchEventKind.Action, R, MatchActionId.GetUp, RollTier.Strong,
                new MatchEffect(MatchEffectKind.Stand, R, 1), new MatchEffect(MatchEffectKind.Count, R, 3))));
            yield return Watch(dir, folder, "outside_getup");
            dir.Enqueue(Out(Ev(MatchEventKind.Action, P, MatchActionId.Weapon, RollTier.Strong,
                new MatchEffect(MatchEffectKind.Damage, R, 3), new MatchEffect(MatchEffectKind.Down, R, 1))));
            yield return Watch(dir, folder, "outside_weapon");
            dir.Enqueue(Out(Ev(MatchEventKind.Action, P, MatchActionId.ReturnToRing, RollTier.Strong, new MatchEffect(MatchEffectKind.Place, P, 0))));
            yield return Watch(dir, folder, "return_ring");
            dir.Enqueue(Ev(MatchEventKind.Action, R, MatchActionId.GetUp, RollTier.Strong, new MatchEffect(MatchEffectKind.Stand, R, 1)));
            yield return Watch(dir, folder, "getup_ring");
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
                new MatchEffect(MatchEffectKind.Down, R, 1)));
            yield return Watch(dir, folder, "finisher", Dense ? 0.04f : -1f);   // щільно — щоб побачити строб (доступність)
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

        /// <summary><c>-ring-demo-dense</c>: фінішер знімається кожні 0,04 с (огляд перемикача «Спалахи»).</summary>
        private static bool Dense { get { return RingLaunchArgs.Has("-ring-demo-dense"); } }

        /// <param name="step">Крок кадрів, с; не задано — <c>-ring-demo-every</c> (<see cref="_every"/>).</param>
        private static IEnumerator Watch(RingMatchDirector dir, string folder, string tag, float step = -1f)
        {
            bool realtime = step > 0f && step < 0.5f;   // явний щільний крок (-ring-demo-dense) — у реальному часі, крізь сповільнення
            if (step <= 0f) step = _every;               // -ring-demo-every лишається в часі гри, як було
            Debug.Log("[Ринг] демо: сегмент " + tag + " з кадру " + _n);
            yield return null;
            float t = 0f;
            while (dir.IsBusy && t < 25f)
            {
                if (realtime) yield return new WaitForSecondsRealtime(step);
                else yield return new WaitForSeconds(step);
                t += step;
                Capture(folder, tag);
            }
        }

        private static bool _probeColliders;

        /// <summary>
        /// Зонд білих плям (-ring-probe): кадр екрана → клітинки 24 px, де ≥ 30 % пікселів майже білі (усі канали ≥ 245) →
        /// промінь з камери через центр клітинки → яка сітка, підсітка й матеріал там стоять (тимчасові MeshCollider на всіх
        /// читаних сітках сцени). Плейтест 08.11.2026, P3: білий «!» у промо, джерело якого не знайшли ні текстом арени, ні UI.
        /// </summary>
        private static IEnumerator ProbeBright(string tag)
        {
            if (!_probeColliders)
            {
                _probeColliders = true;
                int added = 0;
                foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                {
                    if (mf.sharedMesh == null || !mf.sharedMesh.isReadable || mf.GetComponent<Collider>() != null) continue;
                    if (mf.GetComponent<MeshRenderer>() == null || !mf.GetComponent<MeshRenderer>().enabled) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    added++;
                }
                Debug.Log("[Ринг] зонд: колайдерів " + added);
                yield return new WaitForFixedUpdate();
            }
            yield return new WaitForEndOfFrame();
            var cam = Camera.main;
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height, cell = 24, found = 0;
            for (int cy = 0; cy + cell <= h; cy += cell)
                for (int cx = 0; cx + cell <= w; cx += cell)
                {
                    int white = 0;
                    for (int y = cy; y < cy + cell; y++)
                        for (int x = cx; x < cx + cell; x++)
                        {
                            var c = px[y * w + x];
                            if (c.r >= 245 && c.g >= 245 && c.b >= 245) white++;
                        }
                    if (white < cell * cell * 3 / 10) continue;
                    // Екран: (0,0) — лівий нижній кут, як і рядки текстури знімка.
                    var ray = cam.ScreenPointToRay(new Vector3((cx + cell * 0.5f) * Screen.width / w, (cy + cell * 0.5f) * Screen.height / h, 0f));
                    RaycastHit hit;
                    string what = "нічого";
                    if (Physics.Raycast(ray, out hit, 80f))
                    {
                        var mr = hit.collider.GetComponent<MeshRenderer>();
                        var mesh = hit.collider is MeshCollider ? ((MeshCollider)hit.collider).sharedMesh : null;
                        string mat = "?";
                        if (mesh != null && mr != null && hit.triangleIndex >= 0)
                            for (int s = 0; s < mesh.subMeshCount; s++)
                            {
                                var sd = mesh.GetSubMesh(s);
                                int t0 = sd.indexStart / 3, t1 = t0 + sd.indexCount / 3;
                                if (hit.triangleIndex >= t0 && hit.triangleIndex < t1) { mat = s < mr.sharedMaterials.Length && mr.sharedMaterials[s] != null ? mr.sharedMaterials[s].name : "null"; break; }
                            }
                        what = hit.collider.name + " / " + mat + string.Format(System.Globalization.CultureInfo.InvariantCulture, " @ ({0:0.00},{1:0.00},{2:0.00}) за {3:0.0} м",
                            hit.point.x, hit.point.y, hit.point.z, hit.distance);
                    }
                    Debug.Log(string.Format("[Ринг] зонд {0}: біла пляма ({1},{2}) {3}% — {4}", tag, cx, h - cy, white * 100 / (cell * cell), what));
                    if (++found > 40) { Object.Destroy(tex); yield break; }
                }
            Debug.Log("[Ринг] зонд " + tag + ": білих клітинок " + found);
            Object.Destroy(tex);
        }

        private static IEnumerator Shot(string folder, string tag, float wait)
        {
            yield return new WaitForSeconds(wait);
            Capture(folder, tag);
            yield return new WaitForSeconds(0.2f);
        }

        // ============================ огляд виходів ============================

        // Тег екрана в огляді — ті самі слова, що в ui_uk.json (gimmick.*, path.*); у грі — ключами перекладу.
        private static readonly string[] UaGimmick = { "Монстр", "Летун", "Технік", "Шоумен" };
        private static readonly string[] UaPath = { "Герой", "Лиходій", "Бунтар" };

        private sealed class EntranceCase
        {
            public string Id, Body, Name, Tier;
            public Game.Core.Ring.Career.Gimmick Gimmick;
            public RingPath Path;
            public int Str, Agi, Tech, Cha, Pop, Heat, Rgb;
            public bool Injured, Champion, Main, Player;
        }

        /// <summary>
        /// Огляд виходів (<c>-ring-demo &lt;тека&gt; -ring-entrances 1</c>; пакет «Відео огляд» 08.11.2026, п. 4; приймання У2 ТЗ
        /// B07). А — екран: три рецепти × три щаблі (монстр/лиходій · літун/герой · шоумен/бунтар; спортзал · клуб · палац),
        /// текстура 1024×512 у PNG на мітях «удар −0,2 · 0 · +0,3 · +2 с»; гравцеві — золото; «Менше руху». Б — повні виходи
        /// в 3D: кожен біт профілю (завіса, хода й зупинка, вхід — канати / перемах / закочування, поза — центр / кут / канати /
        /// на місці, пояс) з кадром щосекунди, на мітях удару й на початку кожного біта; наостанок — повтор (скорочений).
        /// </summary>
        private static IEnumerator Entrances(RingStage3D stage, string folder)
        {
            var show = stage.Show;
            LogBrightSurfaces();
            // ---- А. екран виходу ----
            string[][] recipes =
            {
                new[] { "monster", "villain", "Тарас «Молот» Гребінь", "Монстр · Лиходій" },
                new[] { "flyer", "hero", "Амара «Комета» Окафор", "Летун · Герой" },
                new[] { "showman", "rebel", "Ґанна «Їжачиха» Євтушенко-Підгірна", "Шоумен · Бунтар" },
            };
            foreach (var tier in new[] { "gym", "hall", "arena" })
                foreach (var r in recipes)
                    yield return ScreenMoments(show, folder, new EntranceRecipe { Gimmick = r[0], Role = r[1], Tier = tier, Name = r[2], TagText = r[3] }, "");
            // Гравцеві — золото (B07: $accent і $flash гравця); «Менше руху» — без тремтіння, спалаху й мерехтіння.
            yield return ScreenMoments(show, folder, new EntranceRecipe { Gimmick = "showman", Role = "rebel", Tier = "arena", Name = "Ґанна «Їжачиха» Євтушенко-Підгірна", TagText = "Шоумен · Бунтар", Player = true }, "_player");
            UI.Menus.RingAccessibility.ReduceMotion = true;
            yield return ScreenMoments(show, folder, new EntranceRecipe { Gimmick = "monster", Role = "villain", Tier = "arena", Name = "Тарас «Молот» Гребінь", TagText = "Монстр · Лиходій" }, "_reduce");
            UI.Menus.RingAccessibility.ReduceMotion = false;
            show.ScreenOff();

            // ---- Б. повні виходи ----
            var M = Game.Core.Ring.Career.Gimmick.Monster; var F = Game.Core.Ring.Career.Gimmick.Flyer;
            var T = Game.Core.Ring.Career.Gimmick.Technician; var S = Game.Core.Ring.Career.Gimmick.Showman;
            var cases = new[]
            {
                new EntranceCase { Id = "a_monster_heel", Body = "body_m_nordic", Name = "Тарас «Молот» Гребінь", Gimmick = M, Path = RingPath.Heel, Str = 3, Cha = 2, Heat = 3, Main = true, Tier = "arena", Rgb = 0x5A1A14 },
                new EntranceCase { Id = "b_flyer_face_f", Body = "body_f_west_african", Name = "Амара «Комета» Окафор", Gimmick = F, Path = RingPath.Face, Agi = 3, Cha = 1, Pop = 6, Tier = "hall", Rgb = 0x2F6FD6 },
                new EntranceCase { Id = "c_showman_rebel_player", Body = "body_f_ukrainian", Name = "Ґанна «Їжачиха» Євтушенко-Підгірна", Gimmick = S, Path = RingPath.Rebel, Agi = 2, Cha = 3, Tier = "gym", Rgb = 0xFF7AB8, Player = true },
                new EntranceCase { Id = "d_tech_face", Body = "body_m_latin", Name = "Іван «Ключ» Їжак", Gimmick = T, Path = RingPath.Face, Tech = 3, Tier = "gym", Rgb = 0x2E6B4F },
                new EntranceCase { Id = "e_tech_heel_injured_f", Body = "body_f_east_asian", Name = "Мей «Вдова» Лін", Gimmick = T, Path = RingPath.Heel, Tech = 2, Injured = true, Tier = "gym", Rgb = 0x3A2340 },
                new EntranceCase { Id = "f_showman_heel_champ", Body = "body_m_mediterranean", Name = "Олівер «Барон» Вінслоу", Gimmick = S, Path = RingPath.Heel, Cha = 2, Heat = 2, Champion = true, Tier = "hall", Rgb = 0x6B4A12 },
                new EntranceCase { Id = "g_monster_face", Body = "body_m_ukrainian", Name = "Богдан «Сталевий» Дуб", Gimmick = M, Path = RingPath.Face, Str = 1, Pop = 2, Tier = "gym", Rgb = 0x24466E },
                new EntranceCase { Id = "h_repeat_monster_heel", Body = "body_m_nordic", Name = "Тарас «Молот» Гребінь", Gimmick = M, Path = RingPath.Heel, Str = 3, Cha = 2, Heat = 3, Main = true, Tier = "arena", Rgb = 0x5A1A14 },
            };
            string only = RingStage3D.ReadArg("-ring-entrance-only");
            foreach (var c in cases)
            {
                if (only != null && !c.Id.StartsWith(only)) continue;
                stage.SetWrestlers(c.Body, c.Rgb, "", "body_m_nordic", 0x5A1A14, "", 1f);
                yield return null;
                var input = new EntranceInput
                {
                    Gimmick = c.Gimmick, Path = c.Path, Strength = c.Str, Agility = c.Agi, Technique = c.Tech, Charisma = c.Cha,
                    Injured = c.Injured, Popularity = c.Pop, Heat = c.Heat, Champion = c.Champion, MainEvent = c.Main, Player = c.Player,
                    Female = c.Body.StartsWith("body_f_"),
                };
                var prof = EntranceProfile.Compose(input);
                var rec = new EntranceRecipe
                {
                    Gimmick = EntranceScreenSlots.GimmickKey(c.Gimmick), Role = EntranceScreenSlots.RoleOf(c.Path), Tier = c.Tier,
                    Name = c.Name, Player = c.Player, TagText = UaGimmick[(int)c.Gimmick] + " · " + UaPath[(int)c.Path],
                };
                stage.BeginEntrance(true, rec, prof, c.Rgb);
                yield return null;
                float hitAt = show.HitAt;
                float[] marks = { hitAt - 0.2f, hitAt, hitAt + 0.3f, hitAt + 2f };
                string[] markTags = { "hit-0.2", "hit+0", "hit+0.3", "hit+2" };
                int mi = 0;
                float next = Time.time + 1f;
                string beat = null;
                float beatShot = -1f;
                float guard = Time.time + 120f;
                while (stage.ShowBusy && Time.time < guard)
                {
                    if (show.Beat != beat) { beat = show.Beat; beatShot = Time.time + 0.45f; }
                    if (mi < marks.Length && Time.time >= marks[mi])
                    {
                        Capture(folder, "ent_" + c.Id + "_" + markTags[mi]);
                        mi++;
                    }
                    else if (beatShot > 0f && Time.time >= beatShot)
                    {
                        Capture(folder, "ent_" + c.Id + "_" + beat + "_start");
                        beatShot = -1f;
                    }
                    else if (Time.time >= next)
                    {
                        Capture(folder, "ent_" + c.Id + "_" + beat);
                        next = Time.time + 1f;
                    }
                    yield return null;
                }
                Debug.Log("[Ринг] огляд виходу " + c.Id + ": " + (stage.ShowBusy ? "ЗАВИС (120 с)" : "завершено"));
                yield return new WaitForSeconds(0.3f);
            }
            Debug.Log("[Ринг] огляд виходів: знято " + _n + " кадрів");
            Debug.Log(stage.PlayedReport());
            HardExit.Now(0);
        }

        /// <summary>
        /// Великі світлі поверхні зали (зауваження «Відео огляду»: «яскраво-білий прямокутник» у залі): об'єкт, матеріал,
        /// базовий колір і текстура, межі — щоб знайти, що це, а не вгадувати.
        /// </summary>
        private static void LogBrightSurfaces()
        {
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var b = r.bounds;
                if (b.size.x * b.size.y + b.size.y * b.size.z + b.size.x * b.size.z < 1.5f) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : Color.clear;
                    Texture t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                    Color e = m.HasProperty("_EmissionColor") && m.IsKeywordEnabled("_EMISSION") ? m.GetColor("_EmissionColor") : Color.clear;
                    float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b, emi = e.maxColorComponent;
                    if (lum < 0.6f && emi < 0.5f) continue;
                    Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "[Ринг] світла поверхня: {0} / {1} — база ({2:0.00},{3:0.00},{4:0.00}) текстура {5}, емісія {6:0.00}; центр ({7:0.0},{8:0.0},{9:0.0}), розмір ({10:0.0},{11:0.0},{12:0.0})",
                        r.name, m.name, c.r, c.g, c.b, t != null ? t.name : "—", emi, b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z));
                }
            }
        }

        /// <summary>Ролик екрана виходу — текстура в PNG на мітях «удар −0,2 · 0 · +0,3 · +2 с» (крок часу — вручну, точно).</summary>
        private static IEnumerator ScreenMoments(RingShow show, string folder, EntranceRecipe r, string suffix)
        {
            var scr = show.Screen;
            show.ManualScreen = true;
            if (!scr.Play(r, false, false)) { show.ManualScreen = false; yield break; }
            double hit = scr.HitIn;
            double[] moments = { hit - 0.2, hit, hit + 0.3, hit + 2.0 };
            string[] tags = { "m02", "h00", "p03", "p20" };
            for (int i = 0; i < moments.Length; i++)
            {
                scr.Seek(moments[i]);
                yield return null;
                yield return new WaitForEndOfFrame();
                SaveRt(show.ScreenTexture, System.IO.Path.Combine(folder, string.Format("{0:000}_tron_{1}{2}_{3}.png", _n++, r.PoolName, suffix, tags[i])));
            }
            show.ManualScreen = false;
            Debug.Log("[Ринг] екран виходу " + r.PoolName + suffix + ": " + scr.Describe + ", шарів " + scr.LayerCount + ", удар "
                + hit.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " с");
        }

        private static void SaveRt(RenderTexture rt, string path)
        {
            if (rt == null) return;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        private static void Capture(string folder, string tag)
        {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, string.Format("{0:000}_{1}.png", _n++, tag)));
        }
    }
}
