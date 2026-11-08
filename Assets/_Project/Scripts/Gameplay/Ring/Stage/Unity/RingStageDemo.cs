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
            if (RingStage3D.ReadArg("-ring-entrances") != null) { yield return Entrances(stage, folder); yield break; }

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
