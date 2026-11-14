using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Налагоджувальний показ 3D (<c>-ring-demo &lt;тека&gt;</c>): без кампанії й UI програє промо з мікрофоном і матчі
    /// сценаріїв справжнього ядра (<see cref="RingDemoScript"/>: рушій грає, кубик називає сценарій, режисер повторює журнал),
    /// знімаючи кадри. Сценарії — прапорцями (<see cref="Specs"/>), без них — склейки «канати / кут», вихід за ринг і
    /// повернення, утримання, фінішер. <c>-ring-demo-sex m|f</c> — пара чоловіків чи жінок. <c>-ring-outside 1</c> —
    /// статичний огляд місць поза центром (за рингом, на канаті, на куті), <c>-ring-venue hall|arena</c> — інший зал.
    /// </summary>
    public static class RingStageDemo
    {
        private static int _n;

        // ============================ сценарії демо ============================

        /// <summary>
        /// Без прапорців сценаріїв: склейки «канати» й «кут», вихід за ринг і повернення, «без DQ» з предметами (стільці й реквізит
        /// за рингом), утримання на «два», фінішер з кута.
        /// </summary>
        private static readonly string[] DefaultSpecs = { "move whip", "move springboard", "move swallow_dive", "outside", "nodq", "pin 2", "finisher meteor" };

        /// <summary>
        /// Сценарії демо з прапорців (<see cref="RingDemoScript"/>): <c>-ring-demo-all</c> — усі по черзі; інакше — кожен заданий:
        /// <c>-ring-demo-exchange</c>, <c>-ring-demo-move id[+id…]</c>, <c>-ring-demo-pin 1|2|3[+…]</c>, <c>-ring-demo-sub</c>,
        /// <c>-ring-demo-dirty</c> (попередження рефері), <c>-ring-demo-outside</c>, <c>-ring-demo-nodq</c>,
        /// <c>-ring-demo-finisher id[+id…]</c>, <c>-ring-demo-end countout|double|dq[+…]</c>. Кілька значень — через «+» (запускач
        /// огляду ділить аргументи на пробілах і комах). Без жодного — <see cref="DefaultSpecs"/> (ті, чиї прийоми є в каталозі).
        /// </summary>
        private static readonly string[] DemoFlags = { "-ring-demo-exchange", "-ring-demo-move", "-ring-demo-pin", "-ring-demo-sub",
            "-ring-demo-dirty", "-ring-demo-outside", "-ring-demo-nodq", "-ring-demo-finisher", "-ring-demo-end" };

        private static List<string> Specs(MoveCatalog moves)
        {
            if (RingLaunchArgs.Has("-ring-demo-all")) return RingDemoScript.Expand(RingDemoScript.AllSpec, moves);
            var list = new List<string>();
            if (RingLaunchArgs.Has("-ring-demo-exchange")) list.Add("exchange");
            Many(list, "move", Val("-ring-demo-move"));
            Many(list, "pin", Val("-ring-demo-pin"));
            if (RingLaunchArgs.Has("-ring-demo-sub")) list.Add("sub");
            if (RingLaunchArgs.Has("-ring-demo-dirty")) list.Add("warn");
            if (RingLaunchArgs.Has("-ring-demo-outside")) list.Add("outside");
            if (RingLaunchArgs.Has("-ring-demo-nodq")) list.Add("nodq");
            Many(list, "finisher", Val("-ring-demo-finisher"));
            Many(list, "end", Val("-ring-demo-end"));
            if (list.Count > 0) return list;
            foreach (string flag in DemoFlags)
                if (RingLaunchArgs.Has(flag))
                {
                    Debug.LogWarning("[Ринг] демо: прапорці сценаріїв є, а жодного сценарію з них не вийшло — грається типовий набір");
                    break;
                }
            foreach (string spec in DefaultSpecs)
            {
                string[] w = spec.Split(' ');
                bool needsMove = w.Length > 1 && (w[0] == "move" || w[0] == "finisher");
                if (!needsMove || moves.Find(w[1]) != null) list.Add(spec);
                else Debug.LogWarning("[Ринг] демо: прийому " + w[1] + " немає в каталозі — сценарій «" + spec + "» пропущено");
            }
            return list;
        }

        /// <summary>
        /// Значення прапорця сценарію (малими літерами — id прийомів і кінців у ядрі малі). Прапорець останнім у рядку чи перед
        /// іншим прапорцем — без значення: попередження, сценарію немає (а не «pin -ring-demo-sub»).
        /// </summary>
        private static string Val(string flag)
        {
            if (!RingLaunchArgs.Has(flag)) return null;
            // Прапорець без урахування регістру, як у RingLaunchArgs.Has (Value шукає точно).
            string[] args = RingLaunchArgs.All();
            string v = null;
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase)) { v = args[i + 1]; break; }
            if (string.IsNullOrEmpty(v) || v.StartsWith("-"))
            {
                Debug.LogWarning("[Ринг] демо: у " + flag + " немає значення — прапорець пропущено");
                return null;
            }
            return v.ToLowerInvariant();
        }

        private static void Many(List<string> list, string head, string arg)
        {
            if (string.IsNullOrEmpty(arg)) return;
            foreach (string a in arg.Split('+'))
                if (a.Trim().Length > 0) list.Add(head + " " + a.Trim());
        }

        /// <summary>Тег кадрів сценарію: «move whip» → «move_whip».</summary>
        private static string Tag(string spec) { return spec.Trim().Replace(' ', '_'); }

        /// <summary>
        /// <c>-ring-demo-sex m|f</c>: пара демо — чоловіки чи жінки (тіла набору; кліпи жіночого набору бере сцена за тілом).
        /// Гравець — тіло українця/українки в першому кольорі костюма, суперник — образ NPC кар'єри (Молот / Вдова).
        /// </summary>
        private static void ApplySex(RingStage3D stage)
        {
            string sex = RingStage3D.ReadArg("-ring-demo-sex");
            if (sex == null) return;
            bool f = sex.Trim().ToLowerInvariant().StartsWith("f");
            var rival = CareerLooks.ForNpc(f ? "vdova" : "hammer");
            string body = f ? "body_f_ukrainian" : "body_m_ukrainian";
            stage.SetWrestlers(body, CareerLooks.CostumeRgb(0), f ? "hair_ponytail01" : "hair_short01", rival.Body, rival.Rgb, rival.Hair, rival.Scale);
            Debug.Log("[Ринг] демо: пара — " + (f ? "жінки" : "чоловіки") + " (гравець " + body + ", суперник " + rival.Body + ")");
        }

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
            _outYaw = null;
            // Рефері — на своєму місці, поки пара за рингом (RingStage3D.OutsideRefPos — його має взяти й режисер замість свого
            // OutsideRefPos, запит п. 4а): на фартуху навпроти пари, обличчям до неї, рахує відлік (Ref_Count_Outside_Loop).
            Vector3 refAt = RingStage3D.OutsideRefPos;
            var venue = RingStage3D.Venue;
            float refR = Mathf.Max(Mathf.Abs(refAt.x), Mathf.Abs(refAt.z));
            bool onApron = refR > venue.RopeLine && refR + RingVenue.RefBody <= venue.RingSightHalf && Mathf.Abs(refAt.y - RingStage3D.MatY) < 1e-3f;
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] огляд за рингом: рефері ({0:0.00}; {1:0.00}; {2:0.00}), до гравця {3:0.00} м, до суперника {4:0.00} м, від лінії пари {5:0.00} м, на фартуху за канатами: {6}",
                refAt.x, refAt.y, refAt.z, Flat(refAt, RingStage3D.PairPos(MatchSide.Player, true)), Flat(refAt, RingStage3D.PairPos(MatchSide.Rival, true)),
                o.Dist - refR, onApron ? "так" : "НІ"));

            // 1) Пара в стійці за рингом.
            Vector3 pp = RingStage3D.PairPos(MatchSide.Player, true), rp = RingStage3D.PairPos(MatchSide.Rival, true);
            stage.Place(pl, pp, RingStage3D.PairYaw(MatchSide.Player, true), "Ring_Stance_Loop");
            stage.Place(rv, rp, RingStage3D.PairYaw(MatchSide.Rival, true), "Ring_Stance_Loop");
            stage.Place(rf, refAt, RingStage3D.YawTo(refAt, RingStage3D.OutsideAnchor), "Ref_Count_Outside_Loop");
            Forget(stage);
            _trackRef = true;
            stage.StartCoroutine(TrackRef(stage, refAt));
            yield return OutsideFrame(stage, folder, v + "_outside_stance", 0.8f);
            stage.SetShot(StageCam.Overview);
            yield return Shot(folder, v + "_outside_overview", 0.8f);

            // 2) Кидок і ларіат пари за рингом (кліпи ядра для Throw і Weapon): кадри на третині й двох третинах кліпа.
            foreach (string clip in new[] { "Bodyslam", "Center_Lariat_Heavy" })
            {
                AnimationClip ca, cb;
                if (!PairOutside(stage, clip, out ca, out cb)) continue;
                float len = ca.length / RingStage3D.PlaySpeed;
                yield return OutsideFrame(stage, folder, v + "_outside_" + clip + "_a", len * 0.33f, ca, cb);
                yield return new WaitForSeconds(len * 0.3f);
                Capture(folder, v + "_outside_" + clip + "_b");
                yield return new WaitForSeconds(len * 0.45f);
                Capture(folder, v + "_outside_" + clip + "_end");
                // Знімок пишеться наприкінці кадру: без паузи в ньому вже наступна розстановка (наступний кліп пари чи ринг), і
                // здавалося, що рефері зсувається з фартуха між _b і _end (огляди 8c/8e; стеження 8f — стоїть на місці).
                yield return new WaitForSeconds(0.2f);
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] огляд за рингом: {0} — таз A ({1:0.00}; {2:0.00}), B ({3:0.00}; {4:0.00}); у смузі: {5}",
                    clip, pl.PosePos.x, pl.PosePos.z, rv.PosePos.x, rv.PosePos.z, InBand(pl.PosePos) && InBand(rv.PosePos) ? "так" : "НІ"));
            }

            _trackRef = false;
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

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

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
        private static bool PairOutside(RingStage3D stage, string clip, out AnimationClip ca, out AnimationClip cb)
        {
            RingActor a = stage.Actor(MatchSide.Player), b = stage.Actor(MatchSide.Rival);
            // Стать — за постаттю (жіночі кліпи й рядок жіночого маніфесту, якщо є; як у режисера).
            bool fa = stage.IsFemale(a), fb = stage.IsFemale(b);
            var ia = stage.Info(clip + "_A", fa);
            ca = stage.Clip(clip + "_A", fa);
            cb = stage.Clip(clip + "_B", fb);
            if (ia == null || ca == null || cb == null) { Debug.LogWarning("[Ринг] огляд: немає пари " + clip); return false; }
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

        /// <summary>Курс попереднього кадру огляду «за рингом» — новий план тримається того самого боку, як у режисера.</summary>
        private static float? _outYaw;

        /// <summary>
        /// Кадр «за рингом»: обидва борці, край фартуха біля пари; камера — планом <see cref="RingCine.OutsideShot"/>. Якщо пара
        /// грає кліпи <paramref name="ca"/> / <paramref name="cb"/> — у кадр ідуть і тіла на третині, двох третинах і в кінці
        /// кліпа (ларіат зсуває пару на ~2 м: огляд 8e/8f — на _b і _end пара виходила з кадру).
        /// </summary>
        private static IEnumerator OutsideFrame(RingStage3D stage, string folder, string tag, float wait,
            AnimationClip ca = null, AnimationClip cb = null)
        {
            yield return null;
            RingActor a = stage.Actor(MatchSide.Player), b = stage.Actor(MatchSide.Rival);
            var pts = new List<Vector3>();
            Vector3 ga = a.PosePos, gb = b.PosePos;
            ga.y = a.Slot.position.y; gb.y = b.Slot.position.y;
            RingCine.Body(pts, ga, stage.BodyScale(a));
            RingCine.Body(pts, gb, stage.BodyScale(b));
            ClipBodies(stage, pts, a, ca);
            ClipBodies(stage, pts, b, cb);
            // Край фартуха навпроти пари — у кадрі: видно, що пара за рингом.
            Vector3 apron = RingStage3D.OutsideAnchor - RingStage3D.OutsideOut * RingVenue.PairOut;
            apron.y = RingStage3D.MatY;
            pts.Add(apron);
            var shot = RingCine.OutsideShot(pts, RingStage3D.OutsideAnchor, RingStage3D.OutsideOut, stage.CamAspect, preferYaw: _outYaw);
            if (shot.Ok) { stage.Cine(shot.Pos, shot.Look, shot.Fov, true); _outYaw = RingCine.YawOf(shot); }
            else { Debug.LogWarning("[Ринг] огляд: план «за рингом» не вмістився — " + tag); stage.SetShot(StageCam.Big); }
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] план за рингом {0}: камера ({1:0.00}; {2:0.00}; {3:0.00}) → ({4:0.00}; {5:0.00}; {6:0.00}), кут {7:0}°, {8}",
                tag, shot.Pos.x, shot.Pos.y, shot.Pos.z, shot.Look.x, shot.Look.y, shot.Look.z, shot.Fov,
                !shot.Ok ? "НЕ вмістився" : shot.Compromise != null ? "вмістився з поступкою: " + shot.Compromise : "вмістився"));
            yield return Shot(folder, tag, wait);
        }

        /// <summary>Тіла постаті <paramref name="a"/> на третині, двох третинах і в кінці кліпа <paramref name="c"/> (таз зі
        /// слота кліпа, на висоті слота) — у кадр «за рингом».</summary>
        private static void ClipBodies(RingStage3D stage, List<Vector3> pts, RingActor a, AnimationClip c)
        {
            if (c == null) return;
            float scale = stage.BodyScale(a);
            for (int k = 1; k <= 3; k++)
            {
                Vector3 h;
                float hy;
                a.SampleLocal(c, Mathf.Min(c.length * k / 3f, c.length - 0.02f), out h, out hy);
                Vector3 w = a.Slot.position + a.Slot.rotation * new Vector3(h.x, 0f, h.z);
                w.y = a.Slot.position.y;
                RingCine.Body(pts, w, scale);
            }
        }

        public static IEnumerator Run(RingStage3D stage, RingMatchDirector dir, string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            Application.runInBackground = true;
            yield return new WaitForSeconds(1.0f);
            if (RingStage3D.ReadArg("-ring-locotest") != null) { yield return LocoTest(stage, folder); yield break; }
            if (RingStage3D.ReadArg("-ring-entrances") != null) { yield return Entrances(stage, folder); yield break; }
            ApplySex(stage);
            if (RingStage3D.ReadArg("-ring-outside") != null) { yield return OutsideReview(stage, folder); yield break; }
            if (RingStage3D.ReadArg("-ring-faces") != null) { yield return FacesReview(stage, folder); yield break; }
            if (RingStage3D.ReadArg("-ring-cast") != null) { yield return CastReview(stage, folder); yield break; }

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

            // ---- матч: сценарії справжнього ядра (RingDemoScript) ----
            string ev = RingStage3D.ReadArg("-ring-demo-every");
            float every;
            if (ev != null && float.TryParse(ev, NumberStyles.Float, CultureInfo.InvariantCulture, out every)) _every = Mathf.Clamp(every, 0.04f, 2f);
            // Камера на рівні мату біля пари (-ring-demo-feet) — перевірка стоп: ковзання, крок, контакт із матом.
            bool feet = RingStage3D.ReadArg("-ring-demo-feet") != null;
            // Без каталогу чи з битим каталогом демо не висить до тайм-ауту огляду, а виходить кодом 2 (виняток).
            MoveCatalog moves = null;
            List<string> specs = null;
            try
            {
                var movesAsset = Resources.Load<TextAsset>("Ring/career_moves");
                if (movesAsset != null)
                {
                    moves = MoveCatalog.Load(movesAsset.text);
                    specs = Specs(moves);
                }
            }
            catch (System.Exception x) { Debug.LogError("[Ринг] демо: " + x.GetType().Name + ": " + x.Message); }
            if (moves == null || specs == null)
            {
                Debug.LogError("[Ринг] демо: каталог прийомів Ring/career_moves не прочитано — демо зупинено");
                HardExit.Now(2);
                yield break;
            }
            Debug.Log("[Ринг] демо: сценаріїв " + specs.Count + " — " + string.Join(" | ", specs.ToArray()));
            int shown = 0, failed = 0, standIns = 0, runIns = 0, offCenter = 0;
            foreach (string spec in specs)
            {
                DemoMatch d = null;
                try { d = RingDemoScript.Play(spec, moves); }
                catch (System.Exception x)
                {
                    // Невідомий сценарій чи прийом (ArgumentException) або сценарій, що розійшовся з ядром (DemoScriptException).
                    failed++;
                    Debug.LogWarning("[Ринг] демо: сценарій «" + spec + "» не зіграно — " + x.GetType().Name + ": " + x.Message);
                }
                if (d == null) continue;
                int count = d.Events.Count - d.StartCount;
                var res = d.Engine.Result;
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] демо: сценарій «{0}» — подій {1} (стартових {2}), кінець {3}, переможець {4}, ажіотаж {5}\n{6}",
                    spec, count, d.StartCount, res != null ? res.Kind.ToString() : "—", res != null ? res.Winner.ToString() : "—",
                    res != null ? res.FinalHype : 0, string.Join("\n", d.Steps.ToArray())));
                string tag = Tag(spec);
                try
                {
                    dir = stage.BeginDemoMatch();
                    if (feet) stage.LookbookFeet(true);
                }
                catch (System.Exception x)
                {
                    failed++;
                    Debug.LogWarning("[Ринг] демо: сценарій «" + spec + "» не почато — " + x.GetType().Name + ": " + x.Message);
                    continue;
                }
                // Кадр старту — пара на місцях до першої події (журнал режисер бере з першого ж кадру після BeginReplay).
                yield return Shot(folder, tag + "_start", 1.0f);
                try { dir.BeginReplay(d.Engine, d.StartCount); }
                catch (System.Exception x)
                {
                    failed++;
                    Debug.LogWarning("[Ринг] демо: сценарій «" + spec + "» не почато — " + x.GetType().Name + ": " + x.Message);
                    continue;
                }
                // Щільно (-ring-demo-dense) — фінішер кожні 0,04 с: строб (огляд перемикача «Спалахи»). Стеля — за довжиною журналу.
                yield return Watch(dir, folder, tag, Dense && spec.StartsWith("finisher") ? 0.04f : -1f, 40f + 10f * count);
                yield return Shot(folder, tag + "_end", 1.5f);
                Debug.Log(stage.DirectorReport() + " — «" + spec + "»");
                shown++;
                standIns += dir.StandIns; runIns += dir.RunInStarts; offCenter += dir.OffCenter;
            }
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[Ринг] демо: показано сценаріїв {0} з {1} (не зіграно {2}); за всі — підстановок кліпів {3}, стартів із розбігу {4}, таз поза центром {5}",
                shown, specs.Count, failed, standIns, runIns, offCenter));
            Debug.Log("[Ринг] Демо 3D: знято " + _n + " кадрів");
            // Лічильники режисера — за всі сценарії рядком вище (PlayedReport дав би лише останній матч).
            Debug.Log(stage.PlayedReport(false));
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
        /// <param name="cap">Стеля сегмента, с гри: режисер, що не звільнився за неї, — попередження в журналі.</param>
        private static IEnumerator Watch(RingMatchDirector dir, string folder, string tag, float step = -1f, float cap = 25f)
        {
            bool realtime = step > 0f && step < 0.5f;   // явний щільний крок (-ring-demo-dense) — у реальному часі, крізь сповільнення
            if (step <= 0f) step = _every;               // -ring-demo-every лишається в часі гри, як було
            Debug.Log("[Ринг] демо: сегмент " + tag + " з кадру " + _n);
            yield return null;
            float t = 0f;
            while (dir.IsBusy && t < cap)
            {
                if (realtime) yield return new WaitForSecondsRealtime(step);
                else yield return new WaitForSeconds(step);
                t += step;
                Capture(folder, tag);
            }
            if (dir.IsBusy) Debug.LogWarning(string.Format(CultureInfo.InvariantCulture, "[Ринг] демо: сегмент {0} не скінчився за {1:0} с", tag, cap));
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

        // ============================ крупні плани облич ============================

        /// <summary>
        /// Крупні плани облич (<c>-ring-demo &lt;тека&gt; -ring-faces 1 [-ring-demo-sex m|f] [-ring-venue hall|arena]</c>): огляд
        /// «щелепа як у скелета» (власник, 09.11.2026). Обидва борці по черзі — гравець (<c>pl</c>), потім суперник (<c>rv</c>;
        /// у кадрі власника 8e лежав суперник): стоячи, на спині (кінець Bump_Back), на животі (кінець Bump_Front),
        /// притиснутий (Cover_Hold_Loop_B) і на руках іншого в боді-слемі (Bodyslam_B на 0,35 / 0,5 / 0,62 кліпа; пара
        /// заморожена, як у кліпі); наостанок рефері стоячи. Види — від обличчя (<see cref="Faces"/>); світло — залу, як у
        /// матчі. У журнал — розгин шиї кожної пози і для кожного виду — відстань камери й що загороджувало.
        /// </summary>
        private static IEnumerator FacesReview(RingStage3D stage, string folder)
        {
            stage.BeginDemoMatch();
            RingActor pl = stage.Actor(MatchSide.Player), rv = stage.Actor(MatchSide.Rival), rf = stage.Referee;
            Vector3 c = new Vector3(0f, RingStage3D.MatY, 0f);
            // Решта — у кутах, щоб не тінили обличчя й не лізли в кадр.
            Vector3 otherAway = new Vector3(1.9f, RingStage3D.MatY, -1.9f), rfAway = new Vector3(-1.9f, RingStage3D.MatY, -1.9f);
            foreach (var who in new[] { pl, rv })
            {
                RingActor other = who == pl ? rv : pl;
                string tag = RingStage3D.Venue.Id + "_" + (who == pl ? "pl" : "rv") + "_" + (stage.IsFemale(who) ? "f" : "m");
                stage.Place(other, otherAway, -45f, "Ring_Stance_Loop");
                stage.Place(rf, rfAway, 45f, "Ref_Idle_Loop");

                stage.Place(who, c, 180f, "Ring_Stance_Loop");
                Forget(stage);
                yield return Faces(stage, folder, tag + "_stand", who);

                foreach (var pose in new[] { new[] { "Bump_Back", "back" }, new[] { "Bump_Front", "belly" } })
                {
                    stage.Place(who, c, 180f, "Ring_Stance_Loop");
                    Freeze(stage, who, pose[0], 1f, c, 180f);
                    Forget(stage);
                    yield return Faces(stage, folder, tag + "_" + pose[1], who);
                }

                stage.Place(who, c, 180f, "Cover_Hold_Loop_B");
                Forget(stage);
                yield return Faces(stage, folder, tag + "_pinned", who);

                // Боді-слем: інший кидає (A), цей у повітрі (B) — зсув і поворот пари з маніфесту статі того, хто кидає.
                var ia = stage.Info("Bodyslam_A", stage.IsFemale(other));
                if (ia != null)
                {
                    float ya = 90f, yb = ya - ia.Rotation;
                    Vector3 off = Quaternion.Euler(0f, ya, 0f) * new Vector3(ia.Offset.x, 0f, ia.Offset.z);
                    Vector3 pa = c - off * 0.5f, pb = c + off * 0.5f;
                    foreach (float frac in new[] { 0.35f, 0.5f, 0.62f })
                    {
                        stage.Place(other, pa, ya, "Ring_Stance_Loop");
                        stage.Place(who, pb, yb, "Ring_Stance_Loop");
                        Freeze(stage, other, "Bodyslam_A", frac, pa, ya);
                        Freeze(stage, who, "Bodyslam_B", frac, pb, yb);
                        Forget(stage);
                        yield return Faces(stage, folder, tag + string.Format(CultureInfo.InvariantCulture, "_bodyslam{0:00}", frac * 100f), who);
                    }
                }
                else Debug.LogWarning("[Ринг] обличчя: немає рядка Bodyslam_A у маніфесті");
            }

            stage.Place(rv, otherAway, -45f, "Ring_Stance_Loop");
            stage.Place(pl, new Vector3(1.9f, RingStage3D.MatY, 1.9f), -135f, "Ring_Stance_Loop");
            stage.Place(rf, c, 180f, "Ref_Idle_Loop");
            Forget(stage);
            yield return Faces(stage, folder, RingStage3D.Venue.Id + "_ref_stand", rf);
            Debug.Log("[Ринг] обличчя: огляд скінчено");
            HardExit.Now(0);
        }

        /// <summary>Кліп постаті, заморожений на частці <paramref name="frac"/> довжини (швидкість 0), таз — у точці з курсом.</summary>
        private static void Freeze(RingStage3D stage, RingActor a, string clip, float frac, Vector3 pos, float yaw)
        {
            var c = stage.Clip(clip, stage.IsFemale(a));
            if (c == null) { Debug.LogWarning("[Ринг] обличчя: немає кліпа " + clip); return; }
            a.PlayAtTime(c, c.length * Mathf.Clamp01(frac), false, pos, yaw, 0f);
        }

        /// <summary>Види огляду облич: анфас, 3/4 ліворуч і праворуч (±40° довкола осі шиї), знизу на підборіддя (35° від
        /// анфасу до грудей) і «як камера гри» (згори під 40° з боку обличчя).</summary>
        private static readonly string[] FaceViewNames = { "front", "l40", "r40", "chin", "game" };
        private static readonly float[] FaceDistances = { 0.8f, 0.6f, 0.45f, 0.35f };
        private static readonly HumanBodyBones[] FaceBlockers =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        };

        /// <summary>
        /// Крупні плани обличчя постаті — види від самого обличчя, а не за сторонами світу (огляд 11.11.2026: види «s»/«w»
        /// лежачих поз ставили камеру в тіло, і зріз шиї оболонки голови видавався за ваду). Вісь обличчя: «угору» — шия → голова,
        /// «праворуч» — ліве плече → праве, «вперед» — їхній добуток. Ціль — 9 см від кістки голови вздовж шиї і 5 см уперед
        /// (середина обличчя). Камера — 0,8 м, кут 24°; якщо між нею й обличчям тіло (кістки трьох постатей кулями 9–15 см,
        /// крім голови й шиї цієї постаті) чи мат — ближче (0,6 / 0,45 / 0,35 м); не вийшло — кадр на 0,8 м з позначкою в журналі.
        /// У журнал — кут між шиєю (шия → голова) і грудьми (верх грудей → шия): 0° — голова на продовженні тулуба.
        /// </summary>
        private static IEnumerator Faces(RingStage3D stage, string folder, string tag, RingActor a, bool showCam = false)
        {
            yield return null;
            yield return null;
            Transform head = a.Head, neck = a.GetBone(HumanBodyBones.Neck), chest = a.GetBone(HumanBodyBones.UpperChest);
            if (chest == null) chest = a.GetBone(HumanBodyBones.Chest);
            Transform la = a.GetBone(HumanBodyBones.LeftUpperArm), ra = a.GetBone(HumanBodyBones.RightUpperArm);
            Vector3 h = head != null ? head.position : a.HeadPos;
            Vector3 up = head != null && neck != null ? (head.position - neck.position).normalized : Vector3.up;
            Vector3 right = Vector3.ProjectOnPlane(la != null && ra != null ? ra.position - la.position : a.Slot.right, up);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.ProjectOnPlane(a.Slot.right, up);
            right.Normalize();
            Vector3 fwd = Vector3.Cross(right, up).normalized;
            Vector3 target = h + up * 0.09f + fwd * 0.05f;
            float bend = head != null && neck != null && chest != null ? Vector3.Angle(neck.position - chest.position, head.position - neck.position) : -1f;
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] обличчя {0}: голова ({1:0.00}; {2:0.00}; {3:0.00}), шия до грудей {4:0}°, обличчя дивиться ({5:0.00}; {6:0.00}; {7:0.00}), кліп {8}",
                tag, h.x, h.y, h.z, bend, fwd.x, fwd.y, fwd.z, a.CurrentClip != null ? a.CurrentClip.name : "—"));
            // «Як камера гри»: з боку обличчя згори під 40°; обличчя дивиться вгору чи вниз — з боку тулуба (видно від підборіддя).
            Vector3 flat = Vector3.ProjectOnPlane(fwd, Vector3.up);
            if (flat.sqrMagnitude < 0.09f) flat = -Vector3.ProjectOnPlane(up, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();
            var dirs = new[]
            {
                fwd,
                Quaternion.AngleAxis(40f, up) * fwd,
                Quaternion.AngleAxis(-40f, up) * fwd,
                (fwd * Mathf.Cos(35f * Mathf.Deg2Rad) - up * Mathf.Sin(35f * Mathf.Deg2Rad)).normalized,
                (flat * Mathf.Cos(40f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(40f * Mathf.Deg2Rad)).normalized,
            };
            for (int i = 0; i < dirs.Length; i++)
            {
                string why = null;
                float dist = FaceDistances[0];
                foreach (float d in FaceDistances)
                {
                    why = FaceBlocked(stage, a, target, target + dirs[i] * d);
                    if (why == null) { dist = d; break; }
                }
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] обличчя {0} {1}: камера {2:0.00} м{3}",
                    tag, FaceViewNames[i], dist, why != null ? " — ЗАГОРОДЖЕНО: " + why : ""));
                // У роздягальні камеру веде показ (RingShow), а не кіноплан рингу.
                if (showCam) stage.Show.ReviewLook(target + dirs[i] * dist, target, 24f);
                else stage.Cine(target + dirs[i] * dist, target, 24f, true);
                yield return Shot(folder, "face_" + tag + "_" + FaceViewNames[i] + (why != null ? "_blocked" : ""), 0.25f);
            }
        }

        /// <summary>Що загороджує обличчя з камери <paramref name="cam"/>: мат під камерою чи на лінії, або кістка постаті ближче
        /// за її радіус до лінії камера → обличчя (без останніх 12 см біля обличчя; голова й шия самої постаті не рахуються).
        /// <c>null</c> — вид чистий.</summary>
        private static string FaceBlocked(RingStage3D stage, RingActor subject, Vector3 target, Vector3 cam)
        {
            float mat = RingStage3D.MatY + 0.04f;
            if (cam.y < mat) return "мат";
            Vector3 seg = target - cam;
            float len = seg.magnitude;
            if (len < 0.2f) return null;
            Vector3 dir = seg / len;
            float span = len - 0.12f;
            foreach (var who in new[] { stage.Actor(MatchSide.Player), stage.Actor(MatchSide.Rival), stage.Referee })
            {
                if (who == null) continue;
                foreach (var b in FaceBlockers)
                {
                    if (who == subject && (b == HumanBodyBones.Head || b == HumanBodyBones.Neck)) continue;
                    Transform t = who.GetBone(b);
                    if (t == null) continue;
                    float r = b == HumanBodyBones.Hips || b == HumanBodyBones.Spine || b == HumanBodyBones.Chest || b == HumanBodyBones.UpperChest ? 0.15f
                        : b == HumanBodyBones.Head ? 0.12f : 0.09f;
                    float along = Mathf.Clamp(Vector3.Dot(t.position - cam, dir), 0f, span);
                    if ((cam + dir * along - t.position).sqrMagnitude < r * r) return who.Name + " " + b;
                }
            }
            return null;
        }

        private static IEnumerator Shot(string folder, string tag, float wait)
        {
            yield return new WaitForSeconds(wait);
            Capture(folder, tag);
            yield return new WaitForSeconds(0.2f);
        }

        // ============================ огляд людей кар'єри поза рингом ============================

        /// <summary>
        /// Люди кар'єри поза рингом (<c>-ring-demo &lt;тека&gt; -ring-cast 1 [-ring-venue hall|arena]</c>; рішення власника
        /// 12.11.2026, № 10 і № 8; приймання У2). Закулісся: кожен букер федерації (Ліна, Агата, Девід, Хіроко) — загальний план,
        /// букер говорить, герой відповідає, крупні плани обличчя; події з гостем (менеджерка, скаут, ветеран; зрада — гість
        /// поруч із букером). Ринг великого шоу з найнятою менеджеркою в кутку героя: загальний план, ближче, кіноплани на неї,
        /// обличчя, вихід героя (найближче тіло героя до неї — у журнал), пара за рингом і герой на своєму куті; наостанок —
        /// щотижневе шоу й велике без найму: її немає.
        /// </summary>
        private static IEnumerator CastReview(RingStage3D stage, string folder)
        {
            string v = RingStage3D.Venue.Id;
            var show = stage.Show;

            // ---- закулісся: букери федерацій ----
            foreach (var p in BackstageCast.AllBookers)
            {
                stage.SetCareerCast(p.Speaker, false);
                stage.ShowBackstage();
                stage.SetBackstageScene(null);
                yield return Shot(folder, v + "_bs_" + p.Speaker + "_wide", 1.0f);
                stage.SetPromoSpeaker(p.Speaker);
                yield return Shot(folder, v + "_bs_" + p.Speaker + "_talk", 0.8f);
                stage.SetPromoSpeaker("player");
                yield return Shot(folder, v + "_bs_" + p.Speaker + "_hero", 0.8f);
                Debug.Log("[Ринг] огляд людей: букер " + p.Speaker + " у роздягальні — " + (show.Booker != null ? show.BookerId + ", " + p.Body + ", " + p.Hair : "НЕМАЄ"));
                if (show.Booker != null) yield return Faces(stage, folder, v + "_" + p.Speaker, show.Booker, true);
            }

            // ---- закулісся: події з гостем ----
            stage.SetCareerCast(BackstageCast.DefaultBooker, false);
            foreach (var ev in new[] { "ev_manager", "ev_scout", "ev_veteran", "ev_betrayal" })
            {
                stage.ShowBackstage();
                stage.SetBackstageScene(ev);
                string guest = BackstageCast.GuestSpeaker(ev);
                yield return Shot(folder, v + "_" + ev + "_wide", 1.0f);
                stage.SetPromoSpeaker(guest);
                yield return Shot(folder, v + "_" + ev + "_" + guest, 0.8f);
                if (BackstageCast.GuestWithBooker(ev))
                {
                    stage.SetPromoSpeaker(BackstageCast.DefaultBooker);
                    yield return Shot(folder, v + "_" + ev + "_booker", 0.8f);
                }
                stage.SetPromoSpeaker("player");
                yield return Shot(folder, v + "_" + ev + "_hero", 0.8f);
                Debug.Log(string.Format("[Ринг] огляд людей: {0} — гість {1} {2}, букер {3}", ev, guest, show.Guest != null ? "є" : "НЕМАЄ",
                    show.Booker != null && show.Booker.Model.activeSelf ? "у кадрі" : "схований"));
                if (ev == "ev_manager" && show.Guest != null) yield return Faces(stage, folder, v + "_ev_manager", show.Guest, true);
                stage.SetBackstageScene(null);
            }

            // ---- ринг великого шоу: менеджерка в кутку героя ----
            stage.SetCareerCast(BackstageCast.DefaultBooker, true);
            stage.SetBigShow(true);
            stage.ShowIdle();
            yield return Shot(folder, v + "_ring_big_overview", 1.5f);
            Vector3 at;
            bool spot = stage.ManagerSpot(out at);
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] огляд людей: менеджерка в кутку — {0}, місце ({1:0.00}; {2:0.00}; {3:0.00}), видно: {4}",
                spot ? "є місце" : "МІСЦЯ НЕМАЄ", at.x, at.y, at.z, stage.ManagerShown ? "так" : "НІ"));
            if (spot && stage.Manager != null)
            {
                var m = stage.Manager;
                stage.SetShot(StageCam.Close);
                yield return Shot(folder, v + "_ring_big_close", 1.2f);
                stage.SetShot(StageCam.Big);
                yield return Shot(folder, v + "_ring_big_big", 1.2f);
                // Вона на весь зріст: з боку рингу (за нею зала) і з боку зали (за нею ринг).
                Vector3 toRing = -at;
                toRing.y = 0f;
                toRing.Normalize();
                Vector3 mid = at + Vector3.up * 0.95f;
                stage.Cine(mid + toRing * 3.2f + Vector3.up * 0.6f, mid, 38f, true);
                yield return Shot(folder, v + "_ring_manager_fromring", 0.8f);
                stage.Cine(mid - toRing * 3.2f + Vector3.up * 0.9f, mid, 38f, true);
                yield return Shot(folder, v + "_ring_manager_fromhall", 0.8f);
                yield return Faces(stage, folder, v + "_manager", m);

                // Вихід героя: найближче, куди тіло героя підходить до неї (таз), — у журнал.
                stage.BeginEntrance(true, null, null, CareerLooks.CostumeRgb(0));
                var hero = stage.Actor(MatchSide.Player);
                float near = float.MaxValue, next = 0f;
                for (float t = 0f; t < 40f && stage.ShowBusy; t += Time.deltaTime)
                {
                    yield return null;
                    near = Mathf.Min(near, Flat(hero.PosePos, at));
                    if (t >= next) { Capture(folder, v + "_ring_entrance_" + Mathf.RoundToInt(t)); next += 2f; }
                }
                stage.SkipShow();
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] огляд людей: вихід героя — найближче до менеджерки {0:0.00} м (тіло — {1:0.00} м)",
                    near, RingVenue.CornerBody));

                // Пара за рингом і герой на своєму куті — менеджерка не в смузі пари й не під кутом.
                stage.BeginDemoMatch();
                RingActor pl = stage.Actor(MatchSide.Player), rv = stage.Actor(MatchSide.Rival), rf = stage.Referee;
                stage.Place(pl, RingStage3D.PairPos(MatchSide.Player, true), RingStage3D.PairYaw(MatchSide.Player, true), "Ring_Stance_Loop");
                stage.Place(rv, RingStage3D.PairPos(MatchSide.Rival, true), RingStage3D.PairYaw(MatchSide.Rival, true), "Ring_Stance_Loop");
                stage.Place(rf, RingStage3D.OutsideRefPos, RingStage3D.YawTo(RingStage3D.OutsideRefPos, RingStage3D.OutsideAnchor), "Ref_Count_Outside_Loop");
                Forget(stage);
                _outYaw = null;
                yield return OutsideFrame(stage, folder, v + "_ring_outside_stance", 0.8f);
                stage.SetShot(StageCam.Overview);
                yield return Shot(folder, v + "_ring_outside_overview", 0.8f);
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] огляд людей: пара за рингом (бік {0}) — до менеджерки гравець {1:0.00} м, суперник {2:0.00} м",
                    RingStage3D.Outside.Side, Flat(pl.PosePos, at), Flat(rv.PosePos, at)));

                stage.Place(rv, RingStage3D.PairPos(MatchSide.Rival, false), -90f, "Ring_Stance_Loop");
                stage.Place(rf, RingStage3D.RefPos, 180f, "Ref_Idle_Loop");
                string clip = RingClipTable.DefaultPerch(MoveStaging.Corner);
                float yaw;
                Vector3 p0 = stage.PerchPos(MatchSide.Player, MoveStaging.Corner, clip, out yaw);
                var c = stage.Clip(clip, stage.IsFemale(pl));
                if (c != null)
                {
                    pl.Slot.position = p0;
                    pl.SetHeight(p0.y);
                    pl.PlayAt(c, false, p0, yaw, RingStage3D.PlaySpeed);
                    Forget(stage);
                    stage.SetShot(StageCam.Big);
                    float len = c.length / RingStage3D.PlaySpeed, nearC = float.MaxValue;
                    int shots = 0;
                    for (float t = 0f; t < len + 0.3f; t += Time.deltaTime)
                    {
                        yield return null;
                        nearC = Mathf.Min(nearC, Flat(pl.PosePos, at));
                        if (shots < 3 && t >= shots * len / 2f) { Capture(folder, v + "_ring_corner_" + clip + "_" + shots); shots++; }
                    }
                    Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] огляд людей: герой на своєму куті ({0}) — до менеджерки найближче {1:0.00} м", clip, nearC));
                }
                else Debug.LogWarning("[Ринг] огляд людей: немає кліпа " + clip);
            }

            // ---- щотижневе шоу й велике без найму: менеджерки немає ----
            stage.SetBigShow(false);
            stage.ShowIdle();
            yield return Shot(folder, v + "_ring_weekly_none", 1.2f);
            bool weekly = stage.ManagerShown;
            stage.SetBigShow(true);
            stage.SetCareerCast(BackstageCast.DefaultBooker, false);
            stage.ShowIdle();
            yield return Shot(folder, v + "_ring_big_nohire", 1.2f);
            Debug.Log(string.Format("[Ринг] огляд людей: менеджерка на щотижневому — {0}, на великому без найму — {1}",
                weekly ? "Є (ВАДА)" : "немає", stage.ManagerShown ? "Є (ВАДА)" : "немає"));
            Debug.Log("[Ринг] огляд людей: знято " + _n + " кадрів");
            HardExit.Now(0);
        }

        // ============================ огляд виходів ============================

        // Тег екрана в огляді — тими самими ключами й тим самим тегом у роді борця, що в грі (EntranceScreenSlots.Tag). Раніше тут
        // був масив чотирьох слів: на ґіміках 5–8 — IndexOutOfRange (рев'ю ПМ 12.11.2026).
        private static Game.Gameplay.Ring.Models.RingText _demoText;

        private static Game.Gameplay.Ring.Models.RingText DemoText()
        {
            if (_demoText != null) return _demoText;
            var strings = Resources.Load<TextAsset>("Ring/strings_uk");
            var ui = Resources.Load<TextAsset>("Ring/ui_uk");
            _demoText = Game.Gameplay.Ring.Models.RingText.FromJson(strings != null ? strings.text : "{}", ui != null ? ui.text : "{}");
            return _demoText;
        }

        private sealed class EntranceCase
        {
            /// <summary>NPC ростеру: тіло, колір, зачіска, зріст — з CareerLooks, ім'я — ключем тексту (ґіміки 5–8 «Кар'єри»).</summary>
            public string Npc;
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
                // Ґіміки 5–8 — наявним сімейством виходу (EntranceProfile.Family), назва в тезі своя й у роді борця.
                new EntranceCase { Id = "i_veteran_heel", Npc = "kharakternyk", Gimmick = Game.Core.Ring.Career.Gimmick.Veteran, Path = RingPath.Heel, Tech = 2, Cha = 2, Heat = 2, Tier = "hall" },
                new EntranceCase { Id = "j_hardcore_face", Npc = "buivol", Gimmick = Game.Core.Ring.Career.Gimmick.Hardcore, Path = RingPath.Face, Str = 2, Pop = 4, Tier = "hall" },
                new EntranceCase { Id = "k_rookie_face_f", Npc = "blyskavka", Gimmick = Game.Core.Ring.Career.Gimmick.Rookie, Path = RingPath.Face, Agi = 1, Pop = 2, Tier = "gym" },
                new EntranceCase { Id = "l_strongman_face_f_player", Body = "body_f_nordic", Gimmick = Game.Core.Ring.Career.Gimmick.Strongman, Path = RingPath.Face, Str = 3, Cha = 1, Pop = 5, Main = true, Tier = "arena", Rgb = CareerLooks.CostumeRgb(3), Player = true, Name = "Ґанна «Їжачиха» Євтушенко-Підгірна" },
                new EntranceCase { Id = "m_strongman_heel", Npc = "atlant", Gimmick = Game.Core.Ring.Career.Gimmick.Strongman, Path = RingPath.Heel, Str = 3, Heat = 2, Main = true, Tier = "arena" },
            };
            var text = DemoText();
            string only = RingStage3D.ReadArg("-ring-entrance-only");
            foreach (var c in cases)
            {
                if (only != null && !c.Id.StartsWith(only)) continue;
                string hair = "";
                if (c.Npc != null)
                {
                    var look = CareerLooks.ForNpc(c.Npc);
                    c.Body = look.Body; c.Rgb = look.Rgb; hair = look.Hair; c.Name = text.Get("w." + c.Npc + ".name");
                }
                stage.SetWrestlers(c.Body, c.Rgb, hair, "body_m_nordic", 0x5A1A14, "", 1f);
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
                    Name = c.Name, Player = c.Player,
                    TagText = EntranceScreenSlots.Tag(text, EntranceScreenSlots.GimmickTextKey(c.Gimmick), c.Path, c.Body.StartsWith("body_f_")),
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

        private static bool _trackRef;

        /// <summary>
        /// Журнал рефері на фартуху, поки пара за рингом: таз і слот, коли він крокує понад 3 см за кадр, міняє кліп чи стоїть
        /// далі 0,1 м від свого місця (раз на 0,5 с). Огляд 8e (09.11.2026): наприкінці ларіата пари рефері зсувався з місця
        /// (спортзал — до канатів, клубна зала й арена — поза кадр), хоч петля рахунку руху не має.
        /// </summary>
        private static IEnumerator TrackRef(RingStage3D stage, Vector3 spot)
        {
            var rf = stage.Referee;
            Vector3 last = rf.PosePos;
            string lastClip = rf.CurrentClip != null ? rf.CurrentClip.name : "—";
            float t = 0f, logged = -1f;
            while (_trackRef && t < 60f)
            {
                yield return null;
                t += Time.deltaTime;
                Vector3 p = rf.PosePos;
                string clip = rf.CurrentClip != null ? rf.CurrentClip.name : "—";
                float step = Flat(p, last), off = Flat(p, spot);
                if (step > 0.03f || clip != lastClip || (off > 0.1f && t - logged > 0.5f))
                {
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "[Ринг] огляд: рефері t={0:0.00} с — таз ({1:0.00}; {2:0.00}), слот ({3:0.00}; {4:0.00}), крок {5:0.000} м, від місця {6:0.00} м, кліп {7}",
                        t, p.x, p.z, rf.Slot.position.x, rf.Slot.position.z, step, off, clip));
                    logged = t;
                }
                last = p;
                lastClip = clip;
            }
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] огляд: рефері — стеження скінчено на t={0:0.00} с, від місця {1:0.00} м",
                t, Flat(rf.PosePos, spot)));
        }

        private static void Capture(string folder, string tag)
        {
            if (_trackRef)
            {
                // Рядок до кадру — щоб звірити кадр з журналом рефері.
                Debug.Log("[Ринг] огляд: кадр " + tag);
            }
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, string.Format("{0:000}_{1}.png", _n++, tag)));
        }
    }
}
