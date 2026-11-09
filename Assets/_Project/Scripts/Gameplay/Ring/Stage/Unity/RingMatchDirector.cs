using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Режисер матчу: слухає журнал подій <see cref="MatchEngine"/> (<c>State.Log</c>) і програє на 3D-рингу кліпи за
    /// таблицею <see cref="RingClipTable"/>. Рушій рахує миттєво, показ відстає: нові події стають у чергу, кожна
    /// грається до кінця (парні кліпи — синхронно на двох, B ставиться за зсувом з маніфесту), а зони й пози борців
    /// режисер веде сам з ефектів подій. <see cref="IsBusy"/> каже UI зачекати (хід ШІ, бот).
    /// Не лінтується: працює з Animator/кліпами.
    /// </summary>
    public sealed class RingMatchDirector : MonoBehaviour
    {
        private RingStage3D _s;
        private MatchEngine _e;
        private int _seen;
        private readonly Queue<MatchEvent> _q = new Queue<MatchEvent>();
        private bool _running;
        private bool _ended;

        private struct Mirror
        {
            /// <summary>Пара за рингом (склейка місця); на рингу — false.</summary>
            public bool Outside;
            public Posture Posture;
            public int Damage;
        }

        private readonly Mirror[] _m = new Mirror[2];

        /// <summary>
        /// Скільки кроків планів зіграно підстановкою кліпа (<see cref="RingClipTable.StandIns"/>) за матч — критерій М1 «0»;
        /// скидається в <see cref="Begin"/>. Кожна підстановка пишеться в журнал Unity рядком «[Ринг] підстановок кліпів».
        /// </summary>
        public int StandIns { get; private set; }

        /// <summary>Скільки парних кроків стартувало посеред розбігу кліпа (<see cref="RunInStart"/>) — теж тимчасова підстановка до нових кліпів.</summary>
        public int RunInStarts { get; private set; }

        public bool IsBusy
        {
            get { return _running || _q.Count > 0 || (_e != null && _e.State.Log.Count > _seen); }
        }

        public void Init(RingStage3D stage) { _s = stage; }

        // ============================ життєвий цикл ============================

        public void Begin(MatchEngine e)
        {
            StopAllCoroutines();
            _walkCo.Clear();
            _running = false;
            _ended = false;
            _afterCut = false;
            StandIns = 0;
            RunInStarts = 0;
            _q.Clear();
            _e = e;
            _seen = e != null ? e.State.Log.Count : 0;
            if (e != null)
            {
                _m[0] = new Mirror { Outside = e.State.Outside, Posture = e.State.Player.Posture, Damage = e.State.Player.Damage };
                _m[1] = new Mirror { Outside = e.State.Outside, Posture = e.State.Rival.Posture, Damage = e.State.Rival.Damage };
            }
            else
            {
                _m[0] = new Mirror(); _m[1] = new Mirror();
            }
            // Усе, що сталося до відкриття екрана (обмін на старті), уже показане.
            _s.Reveal(e != null ? e.State.Log.Count - 1 : int.MaxValue);
            PlaceCanonical();
        }

        /// <summary>Демо-режим без рушія: режисер лише програє подані події.</summary>
        public void BeginDemo()
        {
            Begin(null);
        }

        public void EndMatch()
        {
            StopAllCoroutines();
            _walkCo.Clear();
            _running = false;
            _q.Clear();
            _e = null;
            _s.Reveal(int.MaxValue);
        }

        public void Enqueue(MatchEvent ev) { _q.Enqueue(ev); }

        private void Update()
        {
            if (_e != null)
            {
                var log = _e.State.Log;
                while (_seen < log.Count) _q.Enqueue(log[_seen++]);
            }
            if (!_running && _q.Count > 0 && !_ended) StartCoroutine(Run());
        }

        // ============================ дзеркало місця пари і поз ============================

        private static int I(MatchSide s) { return s == MatchSide.Player ? 0 : 1; }

        private void Apply(MatchEvent ev)
        {
            for (int i = 0; i < ev.Effects.Count; i++)
            {
                var fx = ev.Effects[i];
                int k = I(fx.Who);
                switch (fx.Kind)
                {
                    case MatchEffectKind.Down: _m[k].Posture = Posture.Down; break;
                    case MatchEffectKind.Stand: _m[k].Posture = Posture.Standing; break;
                    case MatchEffectKind.Stunned: _m[k].Posture = Posture.Stunned; break;
                    case MatchEffectKind.Place: _m[0].Outside = _m[1].Outside = fx.Amount != 0; break;   // Amount 1 — пара за рингом, 0 — на рингу
                    case MatchEffectKind.Damage: _m[k].Damage += fx.Amount; break;
                }
            }
            // Оглушений приходить до тями кінцем свого ходу — ефекту нема; перший самостійний хід знімає (рушій: TurnEnd).
            if (ev.Kind == MatchEventKind.TurnEnd && _m[I(ev.Actor)].Posture == Posture.Stunned) _m[I(ev.Actor)].Posture = Posture.Standing;
        }

        /// <summary>Місце борця пари зараз: на рингу — центр, за рингом — точка пари (одне місце на обох, див. <see cref="RingStage3D.PairPos"/>).</summary>
        private Vector3 Canon(MatchSide s)
        {
            return RingStage3D.PairPos(s, _m[I(s)].Outside);
        }

        private string IdleName(MatchSide s)
        {
            var m = _m[I(s)];
            if (m.Posture == Posture.Down) return null;
            if (m.Posture == Posture.Stunned) return "Idle_Hurt_Head_Loop";
            if (m.Damage >= MatchRules.ExhaustedDamage) return "Idle_Tired_Loop";
            return "Ring_Stance_Loop";
        }

        private void PlaceCanonical()
        {
            bool o = _m[0].Outside;
            _s.Place(_s.Actor(MatchSide.Player), Canon(MatchSide.Player), RingStage3D.PairYaw(MatchSide.Player, o), IdleName(MatchSide.Player) ?? "Ring_Stance_Loop");
            _s.Place(_s.Actor(MatchSide.Rival), Canon(MatchSide.Rival), RingStage3D.PairYaw(MatchSide.Rival, o), IdleName(MatchSide.Rival) ?? "Ring_Stance_Loop");
            if (o)
            {
                // Старт зі стану «пара за рингом» (відновлення, демо): рефері — на своєму місці за рингом, кадр — «за рингом».
                Vector3 rp = OutsideRefPos();
                _s.Place(_s.Referee, rp, RingStage3D.YawTo(rp, RingStage3D.OutsideAnchor), "Ref_Idle_Loop");
                OutsideStillShot();
            }
            else _s.Place(_s.Referee, RingStage3D.RefPos, 180f, "Ref_Idle_Loop");
        }

        /// <summary>
        /// Склейка кадру на план <paramref name="cam"/> — не наїзд. <see cref="RingStage3D.SetShot"/> склеює лише з кіноплану чи
        /// в низький план, тож спершу кіноплан-заглушка зі склейкою, у тому самому кадрі одразу змінений на <paramref name="cam"/>
        /// (камера проміжного плану не бачить). Обхід до публічного <c>RingStage3D.CutTo</c> — запит «Изучение» через ПМ.
        /// </summary>
        private void HardCut(StageCam cam)
        {
            _s.Cine(Vector3.zero, Vector3.forward, 40f, true);
            _s.SetShot(cam);
        }

        // ============================ черга ============================

        // ============================ такт ============================

        private MatchEvent _ev;
        private BeatPlan _plan;
        private bool _beatFired;

        private float ClipLen(string name)
        {
            var c = _s.Info(name + "_A") ?? _s.Info(name);
            if (c != null) return c.Length;
            return 0f;
        }

        private static bool IsFinisherHit(MatchEvent ev)
        {
            return ev.Action == MatchActionId.Finisher && ev.Kind == MatchEventKind.Action && ev.Tier != RollTier.Miss;
        }

        /// <summary>
        /// Крок <paramref name="idx"/> щойно запустив свої кліпи (з миті <paramref name="startAt"/> часу кліпа): якщо такт події
        /// на ньому — відлік до мітки кліпа.
        /// </summary>
        private void ArmBeat(int idx, float startAt = 0f)
        {
            if (idx != _plan.Step || _beatFired || _ev == null) return;
            if (_plan.IsCount) StartCoroutine(CountBeats(_ev, _plan));
            else StartCoroutine(BeatAt(_ev, _plan.ClipTime - startAt));
        }

        private IEnumerator BeatAt(MatchEvent ev, float clipTime)
        {
            float wait = Mathf.Max(0f, clipTime) / RingStage3D.PlaySpeed;
            if (IsFinisherHit(ev))
            {
                // Сповільнення 0,4× накриває саме контакт: стартує за 0,15 с (ігрових) до мітки і тримається після неї.
                float pre = Mathf.Min(wait, 0.15f);
                if (wait - pre > 0f) yield return new WaitForSeconds(wait - pre);
                _s.TimeWarp(0.4f, 1.25f);
                if (pre > 0f) yield return new WaitForSeconds(pre);
            }
            else if (wait > 0f) yield return new WaitForSeconds(wait);
            if (_ev == ev && !_beatFired) BeatNow(ev);
        }

        /// <summary>Утримання: «раз» і «два» — на долоні рефері (мітки count_1/count_2 його кліпа), розв'язка — на kickout чи count_3.</summary>
        private IEnumerator CountBeats(MatchEvent ev, BeatPlan p)
        {
            float ps = RingStage3D.PlaySpeed;
            yield return new WaitForSeconds(Mathf.Max(0f, p.Count1) / ps);
            _s.Beat(ev, 1);
            if (_s.Sound != null) _s.Sound.Slap();
            yield return new WaitForSeconds(Mathf.Max(0f, p.Count2 - p.Count1) / ps);
            _s.Beat(ev, 2);
            if (_s.Sound != null) _s.Sound.Slap();
            yield return new WaitForSeconds(Mathf.Max(0f, p.Final - p.Count2) / ps);
            if (_ev != ev || _beatFired) yield break;
            _beatFired = true;
            _s.Beat(ev, 3);
            _s.CrowdPop();                                                   // зал вибухає на розв'язці відліку, а не в кінці кліпа
            if (ev.Tier == RollTier.Miss) { if (_s.Sound != null) _s.Sound.Slap(); }
            else
            {
                // Вирвався: поп залу, спалах і тремтіння — на самому кікауті.
                if (_s.Fx != null) _s.Fx.Impact(ev.Tier == RollTier.Partial ? 0.9f : 0.6f);
                _s.Shake(0.04f, 0.35f);
            }
            _s.Reveal(ev.Seq);
        }

        /// <summary>
        /// Такт на контакті: виноска на екрані, звук, тремтіння, стоп-кадр, спалах; фінішер — стробоскоп і сильне тремтіння
        /// (сповільнення вже стартувало в <see cref="BeatAt"/>). Рядок журналу відкривається тут же.
        /// </summary>
        private void BeatNow(MatchEvent ev)
        {
            _beatFired = true;
            if (_plan.IsCount) _s.Beat(ev, 3); else _s.Beat(ev, 0);
            _s.Reveal(ev.Seq);
            if (_s.Sound != null && ev.Kind != MatchEventKind.Kickout && ev.Kind != MatchEventKind.MatchEnd) _s.Sound.OnEvent(ev, 0f, false);
            if (_s.Fx != null && ev.Action == MatchActionId.Finisher && (ev.Kind == MatchEventKind.Action || ev.Kind == MatchEventKind.Sell) && ev.Tier != RollTier.Miss)
                _s.Fx.Finisher();
            if (IsFinisherHit(ev))
            {
                _s.Shake(0.09f, 0.7f);
                if (_s.Fx != null) _s.Fx.Impact(1f);
                return;
            }
            if (ev.Kind != MatchEventKind.Action && ev.Kind != MatchEventKind.Reverse) return;
            if (!ev.HasRoll) return;
            switch (ev.Tier)
            {
                case RollTier.Strong:
                    _s.TimeWarp(0.05f, 0.07f);
                    _s.Shake(0.05f, 0.35f);
                    if (_s.Fx != null) _s.Fx.Impact(0.8f);
                    break;
                case RollTier.Partial:
                    _s.Shake(0.02f, 0.2f);
                    if (_s.Fx != null) _s.Fx.Impact(0.3f);
                    break;
            }
        }

        private IEnumerator Run()
        {
            _running = true;
            while (_q.Count > 0)
            {
                var ev = _q.Dequeue();
                var steps = RingClipTable.Plan(ev);
                // Для огляду й фаз-тесту: яка подія зараз на сцені (контекст для рядків «телепорт», «крізь канати»).
                Debug.Log("[Ринг] 3D подія: " + ev.Kind + " " + ev.Action + " " + ev.Tier + " — " + ev.Actor);
                bool applied = false;
                _ev = ev;
                _beatFired = false;
                _plan = RingBeats.For(steps, _s.Events, ClipLen);
                int si = RingClipTable.StandIns(steps);
                if (si > 0)
                {
                    StandIns += si;
                    Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] підстановок кліпів: +{0} (за матч {1}) — {2} {3} {4}",
                        si, StandIns, ev.Kind, ev.Action, ev.MoveKey ?? "—"));
                }
                // Кінець матчу — дзвін (після відліку: подія йде слідом за утриманням). Світло-шоу — у RunEnd, на склейці.
                if (_s.Sound != null && ev.Kind == MatchEventKind.MatchEnd) _s.Sound.BellEnd();
                // Без кроків (кінець ходу, продаж) — такт одразу.
                if (steps.Count == 0) BeatNow(ev);
                for (int i = 0; i < steps.Count; i++)
                {
                    var st = steps[i];
                    if (st.Kind == StageStepKind.Place && !applied) { Apply(ev); applied = true; }   // місце пари міняється склейкою на кроці Place
                    yield return RunStep(st, ev, i);
                }
                // Такт, що не настав (кліпа нема чи крок коротший за мітку), — у кінці кроків: текст не губиться.
                if (!_beatFired) BeatNow(ev);
                _s.Reveal(ev.Seq);
                if (!applied) Apply(ev);
                // Стан ядра = поза в кадрі: «лежить» без падіння — продаж падінням; «стоїть», а лежить — встає.
                if (ev.Kind != MatchEventKind.MatchEnd) yield return MatchPoses(ev);
                // Натовп живе за ажіотажем; кікаут і кінець матчу — вибух залу.
                if (_e != null) _s.CrowdMood(_e.State.Hype);
                if (ev.Kind == MatchEventKind.MatchEnd) _s.CrowdPop();
                if (ev.Kind == MatchEventKind.MatchEnd) { _ended = true; break; }
                _afterCut = false;
                if (steps.Count > 0 || ev.Kind == MatchEventKind.TurnEnd) yield return Settle();
            }
            _running = false;
            // Пара за рингом лишається в кадрі «за рингом»: загальний план дивиться на ринг.
            if (!_ended && !_m[0].Outside) _s.SetShot(StageCam.Overview);
        }

        // ============================ кроки ============================

        private IEnumerator RunStep(StageStep st, MatchEvent ev, int idx)
        {
            // Утримання — низька камера біля мату (рахунок «раз — два»), а не загальний кіноплан.
            // Склейки (Cut, Place) ставлять кадр самі — в ту саму мить, що й стрибок тазу. За рингом кадр веде Cine пари.
            if (st.Kind != StageStepKind.Cut && st.Kind != StageStepKind.Place && !_m[0].Outside)
            {
                var cam = ev.Kind == MatchEventKind.Kickout ? StageCam.Low : st.Cam;
                // Після склейки приземлення (пара чи промах сам) — теж склейкою кадру, навіть якщо план склейки не вмістився
                // і камера лишилась у низькому плані (тоді SetShot не склеює).
                if ((st.Kind == StageStepKind.Pair || st.Kind == StageStepKind.Solo) && _afterCut) HardCut(cam);
                else _s.SetShot(cam);
            }
            switch (st.Kind)
            {
                case StageStepKind.Pair: yield return RunPair(st, ev, idx); break;
                case StageStepKind.Solo: yield return RunSolo(st, idx); break;
                case StageStepKind.Cut: yield return RunCut(st); break;
                case StageStepKind.Place: yield return RunPlace(st, ev); break;
                case StageStepKind.Ref: yield return RunRef(st, idx); break;
                case StageStepKind.End: yield return RunEnd(idx); break;
            }
        }

        // ============================ геометрія рингу ============================

        /// <summary>Лінія каната рингу від центру, м (спортзал 16 футів: 2,138 — <c>CAST_ANIM.md</c>, «Ринг і місця кліпів»).</summary>
        private static float RopeLine { get { return RingStage3D.RingHalf + 0.10f; } }

        /// <summary>Шлях кліпа на тілі з кроком 1/30 с до <paramref name="tEnd"/>: таз (x праворуч, z уперед від старту) і курс.</summary>
        private static void Path(RingActor a, AnimationClip c, float tEnd, out float[] t, out Vector3[] p, out float[] y, bool slotFrame = false)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(tEnd * 30f) + 1);
            t = new float[n];
            for (int i = 0; i < n; i++) t[i] = Mathf.Min(tEnd, i / 30f);
            p = new Vector3[n];
            y = new float[n];
            a.SamplePath(c, t, p, y, slotFrame);
        }


        private static Vector3 Rot(float yaw, Vector3 v) { return Quaternion.Euler(0f, yaw, 0f) * v; }
        private static float YawOf(Vector3 d) { return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }


        /// <summary>Трасування ходи в демо (<c>-ring-demo</c>, <c>-ring-shots</c>).</summary>
        private static readonly bool Trace = RingStage3D.ReadArg("-ring-demo") != null || RingStage3D.ReadArg("-ring-shots") != null;

        /// <summary>
        /// Нападник уже на місці склейки (канат чи кут): наступний парний крок стартує приземленням у центрі й склеюється
        /// з кадром (без ходи по рингу).
        /// </summary>
        private bool _afterCut;

        /// <summary>
        /// Склейка плану 1 (нападник на канаті чи куті): нападник жорстко стає на точку першого кадру кліпа
        /// (<see cref="RingStage3D.PerchPos(MatchSide, MoveStaging, string, out float)"/>) і грає кліп; суперник стоїть у центрі
        /// на місці пари. Камера склеюється в ТОЙ САМИЙ кадр, що й стрибок тазу (<see cref="RingCine.PerchShot"/>, запасний
        /// <see cref="StageCam.Low"/>), фаз-тест забуває обох. Такт сюди не ставиться (кліп склейки — не дія події).
        /// </summary>
        private IEnumerator RunCut(StageStep st)
        {
            var a = _s.Actor(st.Actor);
            var vs = RingClipTable.Opp(st.Actor);
            var v = _s.Actor(vs);
            var c = _s.Clip(st.Clip, _s.IsFemale(a));
            if (a == null || c == null)
            {
                Debug.LogWarning("[Ринг] 3D: немає кліпа склейки " + st.Clip);
                yield break;
            }
            float yaw;
            Vector3 at = _s.PerchPos(st.Actor, st.Perch, st.Clip, out yaw);
            // Суперник стоїть у центрі на своєму місці пари (хто лежить — лишається, де лежить).
            Vector3 vPos = RingStage3D.PairPos(vs, false);
            if (_m[I(vs)].Posture != Posture.Down)
            {
                Vector3 dv = v.PosePos - vPos;
                dv.y = 0f;
                if (dv.magnitude > 0.15f) _s.Place(v, vPos, RingStage3D.PairYaw(vs, false), IdleName(vs) ?? "Ring_Stance_Loop");
            }
            else { vPos = v.PosePos; vPos.y = RingStage3D.MatY; }
            StopWalk(a);
            a.Slot.position = at;
            a.SetHeight(at.y);
            a.PlayAt(c, false, at, yaw, RingStage3D.PlaySpeed);
            _s.Watch.Forget(a);
            _s.Watch.Forget(v);
            var pts = new List<Vector3>();
            RingCine.Body(pts, at, _s.BodyScale(a), true);
            RingCine.Body(pts, vPos, _s.BodyScale(v));
            var shot = RingCine.PerchShot(pts, at, vPos, _s.CamAspect);
            if (shot.Ok) _s.Cine(shot.Pos, shot.Look, shot.Fov, true);
            else
            {
                Debug.LogWarning("[Ринг] 3D: план склейки не вмістився — " + st.Clip);
                _s.SetShot(StageCam.Low);
            }
            _afterCut = true;
            yield return new WaitForSeconds(Mathf.Min(c.length, 1.7f) / RingStage3D.PlaySpeed);
        }

        /// <summary>
        /// Склейка місця (пара за рингом чи знову на рингу): ефекти події (<see cref="MatchEffectKind.Place"/>) уже застосовані
        /// в <see cref="Run"/>, тож обидва жорстко стають на місце пари (хто лежить — тілом, у своїй позі), рефері — на своє;
        /// кадр склеюється в ту саму мить (<see cref="RingCine.OutsideShot"/>, запасний <see cref="StageCam.Big"/>; на ринг —
        /// кадр кроку) і тримається <see cref="StageStep.Hold"/>. Такт сюди не ставиться.
        /// </summary>
        private IEnumerator RunPlace(StageStep st, MatchEvent ev)
        {
            bool o = _m[0].Outside;
            _afterCut = false;
            foreach (var side in new[] { MatchSide.Player, MatchSide.Rival })
            {
                var a = _s.Actor(side);
                StopWalk(a);
                Vector3 pos = RingStage3D.PairPos(side, o);
                if (_m[I(side)].Posture == Posture.Down)
                {
                    // Лежачого переносимо разом із позою кліпа, не міняючи курсу тіла.
                    a.SlideBegin();
                    Set(new SlideItem(a, pos, a.PoseYaw) { StartPos = a.PosePos, StartYaw = a.PoseYaw }, 1f);
                }
                else _s.Place(a, pos, RingStage3D.PairYaw(side, o), IdleName(side) ?? "Ring_Stance_Loop");
            }
            var R = _s.Referee;
            StopWalk(R);
            Vector3 rp = o ? OutsideRefPos() : RefSpot(RingStage3D.PairPos(MatchSide.Player, false), RingStage3D.PairPos(MatchSide.Rival, false));
            Vector3 mid = o ? RingStage3D.OutsideAnchor : new Vector3(0f, RingStage3D.MatY, 0f);
            _s.Place(R, rp, RingStage3D.YawTo(rp, mid), "Ref_Idle_Loop");
            _s.Watch.Forget(_s.Actor(MatchSide.Player));
            _s.Watch.Forget(_s.Actor(MatchSide.Rival));
            _s.Watch.Forget(R);
            if (o) OutsideStillShot();
            else HardCut(st.Cam);
            if (st.Hold > 0f) yield return new WaitForSeconds(st.Hold);
            else yield return null;
        }

        /// <summary>Кадр пари, що стоїть за рингом (обидва на місцях пари й край фартуха), — склейкою; запасний — <see cref="StageCam.Big"/>.</summary>
        private void OutsideStillShot()
        {
            var pts = new List<Vector3>();
            foreach (var side in new[] { MatchSide.Player, MatchSide.Rival })
                RingCine.Body(pts, RingStage3D.PairPos(side, true), _s.BodyScale(_s.Actor(side)));
            AddApron(pts);
            var shot = RingCine.OutsideShot(pts, RingStage3D.OutsideAnchor, RingStage3D.OutsideOut, _s.CamAspect);
            if (shot.Ok) _s.Cine(shot.Pos, shot.Look, shot.Fov, true);
            else { Debug.LogWarning("[Ринг] 3D: план «за рингом» не вмістився"); HardCut(StageCam.Big); }
        }

        /// <summary>Місце рефері за рингом: на боці пари, за її лінією від рингу (між бійцями, ближче до огорожі).</summary>
        private static Vector3 OutsideRefPos()
        {
            Vector3 p = RingStage3D.OutsideAnchor + RingStage3D.OutsideOut * 0.6f;
            p.y = RingStage3D.Venue.GroundAt(p.x, p.z);
            return p;
        }

        /// <summary>Край фартуха навпроти пари — у кадрі «за рингом»: видно, що пара поза рингом.</summary>
        private static void AddApron(List<Vector3> pts)
        {
            Vector3 apron = RingStage3D.OutsideAnchor - RingStage3D.OutsideOut * RingVenue.PairOut;
            apron.y = RingStage3D.MatY;
            pts.Add(apron);
        }

        /// <summary>Кадр парного кроку за рингом: обидва борці (старт і кінець зсуву пари) і край фартуха; кадр плавно доганяє.</summary>
        private void OutsidePairShot(RingActor A, RingActor B, Vector3 pAStart, Vector3 pB, Vector3 pA)
        {
            var pts = new List<Vector3>();
            RingCine.Body(pts, pAStart, _s.BodyScale(A));
            RingCine.Body(pts, pB, _s.BodyScale(B));
            RingCine.Body(pts, pA, _s.BodyScale(A));
            AddApron(pts);
            var shot = RingCine.OutsideShot(pts, RingStage3D.OutsideAnchor, RingStage3D.OutsideOut, _s.CamAspect);
            if (shot.Ok) _s.Cine(shot.Pos, shot.Look, shot.Fov, false);
            else { Debug.LogWarning("[Ринг] 3D: план «за рингом» (пара) не вмістився"); _s.SetShot(StageCam.Big); }
        }

        private IEnumerator RunSolo(StageStep st, int idx)
        {
            var a = _s.Actor(st.Actor);
            var clip = _s.Clip(st.Clip);
            bool fromCut = _afterCut;
            _afterCut = false;
            if (clip == null) yield break;
            if (fromCut)
            {
                // План 2 склейки — промах зі стрибка: нападник «приземляється» в центрі на своєму місці пари (суперник ухилився),
                // жорстко в тому самому кадрі, що й склейка камери (RunStep); фаз-тест його забуває.
                Vector3 p = RingStage3D.PairPos(st.Actor, false);
                StopWalk(a);
                a.Slot.position = p;
                a.SetHeight(p.y);
                a.PlayAt(clip, false, p, RingStage3D.PairYaw(st.Actor, false), RingStage3D.PlaySpeed);
                _s.Watch.Forget(a);
            }
            else a.PlayHere(clip, false, RingStage3D.PlaySpeed);
            ArmBeat(idx);
            float len = clip.length / RingStage3D.PlaySpeed;
            if (st.Once) len = Mathf.Min(len, 1.7f);
            yield return new WaitForSeconds(len);
        }

        private IEnumerator RunRef(StageStep st, int idx)
        {
            var r = _s.Referee;
            var clip = _s.Clip(st.RefClip);
            if (clip == null) yield break;
            if (st.RefFacesActor)
                r.PlayAt(clip, false, r.PosePos, RingStage3D.YawTo(r.PosePos, _s.Actor(st.Actor).PosePos), RingStage3D.PlaySpeed);
            else r.PlayHere(clip, false, RingStage3D.PlaySpeed);
            ArmBeat(idx);
            float len = st.Hold > 0f ? st.Hold : clip.length / RingStage3D.PlaySpeed;
            yield return new WaitForSeconds(Mathf.Min(len, clip.length / RingStage3D.PlaySpeed));
        }



        /// <summary>Найдальший старт розбігу від суперника, м: прийом з довшим розбігом починається посеред бігу.</summary>
        private const float RunInMax = 1.5f;

        /// <summary>
        /// Прийом із розбігом (A стартує далі <see cref="RunInMax"/> від B, як усі <c>Center_*</c>): з якої миті кліпа почати,
        /// щоб A був не далі <see cref="RunInMax"/> (B до контакту стоїть, тож пізній старт його не зачіпає), і де тоді A
        /// відносно старту пари (у системі A, Unity: x праворуч, z уперед). Без розбігу — 0.
        /// </summary>
        private float RunInStart(RingActor A, AnimationClip ca, RingClipInfo ia, out Vector3 relA, out float relYawA)
        {
            relA = Vector3.zero; relYawA = 0f;
            Vector3 off = new Vector3(ia.Offset.x, 0f, ia.Offset.z);
            if (off.magnitude <= RunInMax) return 0f;
            float contact = _s.Events.Time(BaseOf(ia.Name), "contact");
            float tMax = contact > 0f ? contact - 0.3f : ca.length * 0.3f;
            int n = Mathf.Max(2, Mathf.CeilToInt(tMax / (1f / 30f)) + 1);
            var times = new float[n];
            for (int i = 0; i < n; i++) times[i] = Mathf.Min(tMax, i / 30f);
            var pos = new Vector3[n];
            var yaw = new float[n];
            A.SamplePath(ca, times, pos, yaw);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = new Vector3(pos[i].x, 0f, pos[i].z);
                if ((off - p).magnitude <= RunInMax || i == n - 1)
                {
                    relA = p; relYawA = yaw[i];
                    return times[i];
                }
            }
            return 0f;
        }

        /// <summary>
        /// Зсув пари, щоб шлях обох у кліпах (від <paramref name="tStart"/>) лежав у рингу — у межах лінії канатів мінус
        /// 0,25 м. Нуль — якщо вже так або шлях ширший за ринг (тоді зсув не допоможе).
        /// </summary>
        private Vector3 PathInside(RingActor A, AnimationClip ca, RingActor B, AnimationClip cb, Vector3 pB, float yawA, float yawB,
            RingClipInfo ia, float tStart)
        {
            float lim = RopeLine - 0.25f;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            Vector3 off = new Vector3(ia.Offset.x, 0f, ia.Offset.z);
            float[] t; Vector3[] p; float[] y;
            Path(A, ca, ca.length - 0.02f, out t, out p, out y);
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] < tStart) continue;
                Vector3 w = pB + Rot(yawA, new Vector3(p[i].x, 0f, p[i].z) - off);
                minX = Mathf.Min(minX, w.x); maxX = Mathf.Max(maxX, w.x); minZ = Mathf.Min(minZ, w.z); maxZ = Mathf.Max(maxZ, w.z);
            }
            Path(B, cb, cb.length - 0.02f, out t, out p, out y);
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] < tStart) continue;
                Vector3 w = pB + Rot(yawB, new Vector3(p[i].x, 0f, p[i].z));
                minX = Mathf.Min(minX, w.x); maxX = Mathf.Max(maxX, w.x); minZ = Mathf.Min(minZ, w.z); maxZ = Mathf.Max(maxZ, w.z);
            }
            Vector3 sh = Vector3.zero;
            if (maxX - minX <= 2f * lim) { if (maxX > lim) sh.x = lim - maxX; else if (minX < -lim) sh.x = -lim - minX; }
            if (maxZ - minZ <= 2f * lim) { if (maxZ > lim) sh.z = lim - maxZ; else if (minZ < -lim) sh.z = -lim - minZ; }
            return sh;
        }


        private static string BaseOf(string pairClip)
        {
            return pairClip.EndsWith("_A") || pairClip.EndsWith("_B") ? pairClip.Substring(0, pairClip.Length - 2) : pairClip;
        }

        private IEnumerator RunPair(StageStep st, MatchEvent ev, int idx)
        {
            var ia = _s.Info(st.Clip + "_A");
            var ib = _s.Info(st.Clip + "_B");
            var ca = _s.Clip(st.Clip + "_A");
            var cb = _s.Clip(st.Clip + "_B");
            if (ia == null || ca == null || cb == null)
            {
                Debug.LogWarning("[Ринг] 3D: немає парного кліпа " + st.Clip);
                yield break;
            }
            MatchSide sa = st.Actor, sb = RingClipTable.Opp(sa);
            var A = _s.Actor(sa);
            var B = _s.Actor(sb);
            bool bDown = _m[I(sb)].Posture == Posture.Down;
            // Після склейки (нападник на канаті чи куті) чекати нікого: приземлення стартує в тому самому кадрі, що й склейка кадру.
            bool fromCut = _afterCut;
            _afterCut = false;
            // Хто лежить — спершу долежує свій кліп падіння (той ще котить таз): точку пари беремо, коли він уже на місці
            // (сезон 08.11.2026: Cover_B стрибав на 1,3–2,4 м — точку взято, поки B ще ковзав у падінні).
            if (bDown && !fromCut)
            {
                float gd = Time.time + 2.5f;
                while (Time.time < gd && B.CurrentClip != null && !B.Finished && !(_s.Info(B.CurrentClip.name)?.Loop ?? false)) yield return null;
            }
            // Нападник, що ще встає (кліп підйому не дограв), — спершу встає, а вже тоді йде на місце (а не ковзає навстоячки).
            if (!fromCut)
            {
                float ga = Time.time + 2.5f;
                while (Time.time < ga && !Upright(A) && A.CurrentClip != null && !A.Finished && !(_s.Info(A.CurrentClip.name)?.Loop ?? false)) yield return null;
            }
            // Розбіг довший за ринг (Center_*: 2,2–3,5 м, а від центру до каната рингу 16 футів — 2,1 м): кліп стартує посеред
            // бігу, B — у центрі (контакт у центрі, правило власника 30.10.2026), A — за розбігом.
            Vector3 relA = Vector3.zero;
            float relYawA = 0f, tStart = 0f;
            if (!bDown) tStart = RunInStart(A, ca, ia, out relA, out relYawA);
            if (tStart > 0f)
            {
                RunInStarts++;
                if (Trace) Debug.Log("[Ринг] розбіг кліпа " + st.Clip + ": старт з " + tStart.ToString("0.00", CultureInfo.InvariantCulture) + " с (підстановка до нових кліпів)");
            }

            // Стартові пози. B — якір (стоїть на своєму місці або лежить там, де впав), A виходить із зсуву маніфесту.
            Vector3 pB, pA;
            float yawA, yawB;
            if (bDown)
            {
                pB = B.PosePos;
                yawB = B.PoseYaw;
                yawA = yawB + ia.Rotation;
            }
            else
            {
                Vector3 cA = Canon(sa), cB = Canon(sb);
                yawA = RingStage3D.YawTo(cA, cB);
                if ((cB - cA).sqrMagnitude < 0.01f) yawA = sa == MatchSide.Player ? 90f : -90f;
                pB = cB;
                yawB = yawA - ia.Rotation;
            }
            pA = pB - Quaternion.Euler(0f, yawA, 0f) * ia.Offset;
            bool outside = _m[I(sb)].Outside;
            Vector3 hub = outside ? RingStage3D.OutsideAnchor : new Vector3(0f, RingStage3D.MatY, 0f);   // центр пари: центр рингу чи точка за рингом
            pA.y = pB.y = hub.y;

            // Де A насправді стартує: з розбігом — посеред бігу (за кривою тазу кліпа на цьому тілі), інакше — на старті пари.
            float yawAStart = yawA + relYawA;
            Vector3 aStartRel = tStart > 0f ? relA - new Vector3(ia.Offset.x, 0f, ia.Offset.z) : -new Vector3(ia.Offset.x, 0f, ia.Offset.z);
            Vector3 pAStart = pB + Quaternion.Euler(0f, yawA, 0f) * aStartRel;
            pAStart.y = pB.y;

            // Усе в центрі рингу (рішення власника 08.11.2026: «усе в центрі рингу»; раніше — власник 30.10.2026 і 04.11.2026):
            // пара зсувається так, що її середина — у центрі (за рингом — у точці пари). Прийом із розбігом — у центрі B (там
            // контакт), а не середина розбігу. Хто лежить — лишається, де впав: до нього підходить A.
            if (!bDown)
            {
                Vector3 d = hub - (tStart > 0f ? pB : (pAStart + pB) * 0.5f);
                d.y = 0f;
                pA += d;
                pB += d;
                pAStart += d;
            }
            // Увесь шлях обох у кліпі — у рингу: пара зсувається всередину на найбільший вихід за лінію канатів (з запасом
            // 0,25 м). Лежачого це теж стосується, але лише на потрібний мінімум. За рингом смуги канатів немає.
            if (!outside)
            {
                Vector3 sh = PathInside(A, ca, B, cb, pB, yawA, yawB, ia, tStart);
                pA += sh;
                pB += sh;
                pAStart += sh;
            }
            Vector3 mid = (pAStart + pB) * 0.5f;
            // Рефері збоку від пари (~1,2 м), у бік центру рингу й трохи за борцями: не перекриває ані їх, ані камеру. Якщо
            // він уже стоїть так, що не заважає, — лишається (менше перестановок).
            float sideX = mid.x > 0.15f ? -1f : 1f;
            Vector3 pRef = new Vector3(Mathf.Clamp(mid.x + sideX * 1.25f, -1.7f, 1.7f), RingStage3D.MatY, Mathf.Clamp(mid.z + 0.9f, -1.2f, 1.7f));
            if (RefOk(_s.Referee.PosePos, pAStart, pB) && RefOk(_s.Referee.PosePos, pA, pB)) pRef = _s.Referee.PosePos;
            bool finisherShot = IsFinisherHit(ev) && idx == _plan.Step && !outside;
            if (finisherShot)
            {
                // Фінішер знімається збоку від лінії удару: рефері — за парою, з дальнього від камери боку (кадр 073 огляду:
                // стояв поруч і займав пів кадру). Бік камери — з урахуванням того, де рефері зараз (сезон 08.11.2026: після
                // утримання він навколішки біля пари з боку камери й не встигав відійти — великий на передньому плані).
                _finYaw = FinisherYaw(yawA, _s.Referee.PosePos, mid);
                Vector3 camDir = Rot(_finYaw, Vector3.forward);
                pRef = mid + camDir * 1.15f + Rot(_finYaw, Vector3.right) * 0.9f;
                pRef.x = Mathf.Clamp(pRef.x, -1.75f, 1.75f);
                pRef.z = Mathf.Clamp(pRef.z, -1.75f, 1.75f);
                pRef.y = RingStage3D.MatY;
            }
            // За рингом рефері має своє місце — за парою, ближче до огорожі (поза капсулами обох: ≥ 0,85 м від кожного).
            if (outside) pRef = OutsideRefPos();
            float yawRef = RingStage3D.YawTo(pRef, outside ? RingStage3D.OutsideAnchor : mid);

            var refActor = _s.Referee;
            if (fromCut)
            {
                // Склейка: нападник стає в точку старту разом із PlayAtTime нижче, решта — жорстко в цей самий кадр.
                StopWalk(A); StopWalk(B); StopWalk(refActor);
                // Висота слота склейки (верх кута — з підйомом) не лишається на приземленні: PlayAtTime висоти не міняє.
                A.SetHeight(pAStart.y);
                if (!bDown && (B.PosePos - pB).sqrMagnitude > 0.0025f) _s.Place(B, pB, yawB, IdleName(sb) ?? "Ring_Stance_Loop");
                if ((refActor.PosePos - pRef).sqrMagnitude > 0.0025f) _s.Place(refActor, pRef, yawRef, "Ref_Idle_Loop");
                _s.Watch.Forget(A); _s.Watch.Forget(B); _s.Watch.Forget(refActor);
            }
            else
            {
                // Перехід ланцюга — коротким кроком у стійці (0,15–0,25 с), а не ходою; ходять лише ті, кому далеко.
                yield return Transit(new[]
                {
                    new SlideItem(A, pAStart, yawAStart),
                    new SlideItem(B, pB, yawB),
                    new SlideItem(refActor, pRef, yawRef)
                });
            }

            // Фінішер — склейкою в кіноплан до старту кліпа: обидва в кадрі від колін, контакт у кадрі (огляд 07.11.2026:
            // такт приходив, поки камера ще летіла з загального плану).
            if (finisherShot) FinisherShot(A, B, ca, pB, yawA, ia);
            else if (outside) OutsidePairShot(A, B, pAStart, pB, pA);

            // Старт: слот у таз і новий кліп — в тому самому кадрі, обидва разом (з розбігом — посеред бігу).
            A.PlayAtTime(ca, tStart, false, pAStart, yawAStart, RingStage3D.PlaySpeed);
            B.PlayAtTime(cb, tStart, false, pB, yawB, RingStage3D.PlaySpeed);
            float len = (Mathf.Max(ca.length, cb.length) - tStart) / RingStage3D.PlaySpeed;
            if (!string.IsNullOrEmpty(st.RefClip))
            {
                var rc = _s.Clip(st.RefClip);
                if (rc != null)
                {
                    refActor.PlayHere(rc, false, RingStage3D.PlaySpeed);
                    len = Mathf.Max(len, rc.length / RingStage3D.PlaySpeed);
                }
            }
            ArmBeat(idx, tStart);
            yield return new WaitForSeconds(len);
        }

        /// <summary>
        /// Кіноплан фінішера: збоку від лінії удару (з боку залу), трохи згори — над верхнім канатом; голови нижче зони
        /// виноски «ФІНІШЕР!» (Статут FX-05), коліна вище нижньої панелі, обоє в кадрі на миті контакту.
        /// </summary>
        private void FinisherShot(RingActor A, RingActor B, AnimationClip ca, Vector3 pB, float yawA, RingClipInfo ia)
        {
            float contact = _plan.ClipTime;
            var t = new[] { 0f, Mathf.Max(0f, contact) };
            var pos = new Vector3[2];
            var yaw = new float[2];
            A.SamplePath(ca, t, pos, yaw);
            Vector3 aC = pB + Quaternion.Euler(0f, yawA, 0f) * (new Vector3(pos[1].x, 0f, pos[1].z) - new Vector3(ia.Offset.x, 0f, ia.Offset.z));
            aC.y = pB.y;
            float hA = Mathf.Max(1.5f, A.HeadPos.y - A.Slot.position.y + 0.12f);
            float hB = Mathf.Max(1.5f, B.HeadPos.y - B.Slot.position.y + 0.12f);
            var pts = new List<Vector3>
            {
                aC + Vector3.up * 0.5f, aC + Vector3.up * hA,
                pB + Vector3.up * 0.5f, pB + Vector3.up * hB,
            };
            float yawCam = _finYaw;
            // Рефері міг не дійти за пару (встає після відліку, далеко йти): якщо в мить склейки він між камерою й парою —
            // камера з протилежного боку (там він уже позаду), а сам він зупиняється, щоб не перетинати кадр (сезон 08.11.2026:
            // «перед парою на 0,91 м»).
            {
                Vector3 m0 = (aC + pB) * 0.5f, r0 = _s.Referee.PosePos - m0;
                r0.y = 0f;
                if (Vector3.Dot(r0, Rot(yawCam, Vector3.forward)) < -0.3f)
                {
                    yawCam += 180f;
                    StopWalk(_s.Referee);
                }
            }
            // Камера — над верхнім канатом, з нахилом: канат у кадрі проходить нижче колін (кадр 073 огляду: камера на висоті
            // канатів, усі три канати різали тулуби). Ширший нахил — лише якщо пологіший не дає чистого кадру.
            var knees = new[] { aC + Vector3.up * 0.5f, pB + Vector3.up * 0.5f };
            Func<Vector3, bool> ok = p => p.y >= RingStage3D.MatY + 1.35f && p.y <= RingStage3D.MatY + 4.2f && Mathf.Abs(p.x) < 7f && Mathf.Abs(p.z) < 7f
                && RopeClear(p, knees[0]) && RopeClear(p, knees[1]);
            // Голова рефері — теж нижче зони слова «ФІНІШЕР!» (Статут FX-05: слово не накриває обличчя борців і рефері).
            // Перевірка за ТЗ директора 08.11.2026: рефері «за парою» стоїть випростаний, а борці зігнуті, — його голова
            // проєктувалась у верхні 8–22 % кадру під слово (кадри 237, 379, 512 сезону). Спершу кадр, де й він під смугою;
            // якщо такого немає — як раніше, лише борці.
            Vector3 refHead = _s.Referee.HeadPos + Vector3.up * 0.12f;
            var ptsRef = new List<Vector3>(pts) { refHead };
            // Ракурси: обраний бік, далі з відхилом ±25°, далі протилежний бік (біля канатів з боку камери промінь до колін
            // різав канат — кіноплан не вміщався у 1–2 фінішерах сезону, і лишалась звичайна камера; п. 3 пакета — «кожен
            // фінішер»). Для кожного ракурсу — спершу з рефері під смугою слова, потім без нього.
            var shot = new RingCine.Shot();
            float baseYaw = yawCam;
            // Порядок ракурсів: спершу ті, де рефері позаду пари (≥ 0,3 м), далі збоку, і лише потім — перед нею (сезон
            // 08.11.2026: запасний протилежний бік ставив рефері на 0,9 м перед парою — третина кадру).
            Vector3 mR = (aC + pB) * 0.5f, rR = _s.Referee.PosePos - mR;
            rR.y = 0f;
            var all = new[] { 0f, 25f, -25f, 180f, 155f, 205f };
            Func<float, int> tier = dy => { float dp = Vector3.Dot(rR, Rot(baseYaw + dy, Vector3.forward)); return dp >= 0.3f ? 0 : dp >= -0.3f ? 1 : 2; };
            var order = new List<float>();
            for (int tr = 0; tr < 3; tr++) foreach (float d in all) if (tier(d) == tr) order.Add(d);   // стабільно в межах щабля
            foreach (float dyaw in order)
            {
                foreach (var set in new[] { ptsRef, pts })
                {
                    foreach (float pitch in new[] { 18f, 22f, 26f, 30f })
                    {
                        shot = RingCine.FrameAny(set, baseYaw + dyaw, pitch, new[] { 34f, 38f, 42f, 46f }, _s.CamAspect, 0.27f, 0.78f, 0.08f, ok);
                        if (shot.Ok) break;
                    }
                    if (shot.Ok) break;
                }
                if (shot.Ok) { yawCam = baseYaw + dyaw; break; }
            }
            if (shot.Ok) _s.Cine(shot.Pos, shot.Look, shot.Fov, true);
            else Debug.LogWarning("[Ринг] 3D: кіноплан фінішера не вмістився — лишається " + _plan.Clip);
            // Для огляду: де рефері відносно кадру в мить склейки (за парою — добре; перед нею — закриває) і де його голова по
            // висоті кадру (зона слова — верхні 8–22 %).
            if (shot.Ok)
            {
                Vector3 m = (aC + pB) * 0.5f, r = _s.Referee.PosePos - m;
                r.y = 0f;
                float depth = Vector3.Dot(r, Rot(yawCam, Vector3.forward));
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] фінішер: кіноплан, рефері {0} пари на {1:0.00} м, голова рефері на {2:0} % від верху кадру ({3})",
                    depth >= 0f ? "за" : "перед", Mathf.Abs(depth), 100f * FromTop(shot, refHead), _plan.Clip));
            }
        }

        /// <summary>Де точка світу в кадрі кіноплану по висоті: частка від верху (0 — верх, 1 — низ).</summary>
        private static float FromTop(RingCine.Shot s, Vector3 p)
        {
            Vector3 f = (s.Look - s.Pos).normalized, rgt = Vector3.Cross(Vector3.up, f).normalized, up = Vector3.Cross(f, rgt);
            Vector3 v = p - s.Pos;
            float z = Mathf.Max(1e-3f, Vector3.Dot(v, f));
            float ndc = Vector3.Dot(v, up) / (z * Mathf.Tan(s.Fov * 0.5f * Mathf.Deg2Rad));
            return (1f - ndc) * 0.5f;
        }


        /// <summary>Курс кіноплану поточного фінішера (обраний у <see cref="RunPair"/>).</summary>
        private float _finYaw;

        /// <summary>
        /// Курс камери фінішера — перпендикуляр до лінії удару; з двох — той, звідки дивиться зал (камера з боку −Z), але якщо
        /// рефері зараз з того боку від пари (між камерою й парою), — протилежний: там рефері вже за парою.
        /// </summary>
        private static float FinisherYaw(float yawA, Vector3 refPos, Vector3 mid)
        {
            float y1 = yawA + 90f, y2 = yawA - 90f;
            Vector3 r = refPos - mid;
            r.y = 0f;
            Func<float, float> score = y =>
            {
                float behind = Vector3.Dot(r, Rot(y, Vector3.forward));        // > 0 — рефері за парою від камери
                return Mathf.Cos(y * Mathf.Deg2Rad) + (behind < -0.3f ? -2f : behind > 0.3f ? 0.5f : 0f);
            };
            return score(y1) >= score(y2) ? y1 : y2;
        }

        /// <summary>
        /// Промінь від камери до точки не проходить крізь канати: там, де він перетинає площину канатів рингу, він вище
        /// верхнього каната (1,24 м над матом + товщина). Камера всередині рингу — канатів між ними немає.
        /// </summary>
        private static bool RopeClear(Vector3 cam, Vector3 target)
        {
            float line = RopeLine, top = RingStage3D.MatY + 1.24f + 0.07f;
            for (int axis = 0; axis < 2; axis++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    float c = axis == 0 ? cam.x : cam.z, t = axis == 0 ? target.x : target.z;
                    float plane = sgn * line;
                    if ((c - plane) * (t - plane) >= 0f) continue;              // по один бік площини — не перетинає
                    float k = (plane - c) / (t - c);
                    Vector3 hit = Vector3.Lerp(cam, target, k);
                    float other = axis == 0 ? hit.z : hit.x;
                    if (Mathf.Abs(other) > line + 0.1f) continue;                // поза прольотом канатів (за стійкою)
                    if (hit.y < top) return false;
                }
            return true;
        }

        private IEnumerator RunEnd(int idx)
        {
            var win = _e != null && _e.Result != null ? _e.Result.Winner : MatchWinner.Player;
            var ia = _s.Info("Ref_Raise_Hand_A");
            var ca = _s.Clip("Ref_Raise_Hand_A");
            var cb = _s.Clip("Ref_Raise_Hand_B");
            var R = _s.Referee;
            if (win == MatchWinner.Draw || ia == null || ca == null || cb == null)
            {
                var bell = _s.Clip("Ref_Call_Bell");
                if (bell != null) { R.PlayHere(bell, false); ArmBeat(idx); yield return new WaitForSeconds(Mathf.Min(2f, bell.length)); }
                yield break;
            }
            MatchSide sw = win == MatchWinner.Player ? MatchSide.Player : MatchSide.Rival;
            var W = _s.Actor(sw);
            var L = _s.Actor(RingClipTable.Opp(sw));
            // Відлік за рингом (CountOut, ядро MatchEngine.CountOutside): той, хто програв, лежить за рингом, переможець устиг
            // на ринг до «десяти». Ефекту Place ядро тут не дає (пара в ядрі так і лишається за рингом), тож склейкою
            // переможець і рефері — на рингу, той, хто програв, лишається лежати, де лежав, і в кадр перемоги не береться.
            // Нічия (подвійний відлік) — вище: лише дзвін, пара за рингом.
            bool lAway = _m[0].Outside;
            if (lAway)
            {
                StopWalk(W); StopWalk(R);
                _s.Place(W, RingStage3D.PairPos(sw, false), RingStage3D.PairYaw(sw, false), IdleName(sw) ?? "Ring_Stance_Loop");
                _s.Place(R, RingStage3D.RefPos, 180f, "Ref_Idle_Loop");
                _s.Watch.Forget(W);
                _s.Watch.Forget(R);
                HardCut(StageCam.Overview);
            }
            bool lDown = !lAway && _m[I(RingClipTable.Opp(sw))].Posture == Posture.Down;

            // Кадр перемоги (огляд 07.11.2026: голова переможця за кадром, «ПЕРЕМОГА» на тілі, руки складені): камера низько
            // спереду, з краю рингу; переможець стоїть позаду того, хто програв, — над ним; рефері поруч піднімає руку.
            // Бік камери (огляд 08.11.2026, кадр поразки 410: той, хто програв, лежав біля ближніх канатів — камера не
            // вміщалась у ринг і знімала знизу крізь канати): пробуються чотири боки, спершу той, де від того, хто програв,
            // до канатів найбільше рингу (бік залу −Z — з невеликою перевагою); береться перший, де кадр уміщується в ринг.
            // Той, хто програв, за рингом — кадр будується від центру рингу (переможець «за центром», як над тим, хто стоїть).
            Vector3 lp = lAway ? new Vector3(0f, RingStage3D.MatY, 0f) : L.PosePos;
            Vector3 pB = Vector3.zero, pA = Vector3.zero;
            float yaw = 180f;
            var sides = new List<Vector3> { Vector3.back, Vector3.forward, Vector3.left, Vector3.right };
            sides.Sort((a, b) => Room(lp, b).CompareTo(Room(lp, a)));
            for (int k = 0; k < sides.Count; k++)
            {
                VictoryPlace(lp, lDown, ia.Offset, sides[k], out var b, out var a, out var y);
                if (k == 0) { pB = b; pA = a; yaw = y; }
                if (!VictoryFrame(b, a, 1.85f, 1.8f, lDown ? lp : (Vector3?)null, y, true).Ok) continue;
                pB = b; pA = a; yaw = y;
                break;
            }

            // Переможець і рефері йдуть на місце кроками (хода з кермом, не ковзання) і стають лицем до камери.
            yield return Walk(new[] { new SlideItem(W, pB, yaw), new SlideItem(R, pA, yaw) });
            if (!lDown && !lAway)
            {
                // Той, хто програв, не стоїть як на старті: зігнувся від болю (огляд 06.11.2026 — однакова поза на результаті).
                var hurt = _s.Clip("Idle_Hurt_Body_Loop") ?? _s.Clip("Idle_Tired_Loop");
                if (hurt != null) L.PlayHere(hurt, true);
            }
            R.PlayAt(ca, false, pA, yaw, RingStage3D.PlaySpeed);
            W.PlayAt(cb, false, pB, yaw, RingStage3D.PlaySpeed);
            VictoryShot(W, R, L, lDown, yaw, pB, pA);
            // Світло-шоу — на склейці в кадр переможця (огляд 08.11.2026: піро й конфеті стартували ще на низькому плані
            // утримання). Перемога гравця — конфеті, фонтани, промені; поразка — холодна тьмяна зала без свята (огляд
            // 06.11.2026: конфеті на «Поразці» — хибний сигнал).
            if (_s.Fx != null && _e != null && _e.IsOver)
            {
                var p = _s.Actor(MatchSide.Player);
                if (win == MatchWinner.Player) _s.Fx.Victory(p.Hips != null ? p.Hips : p.Slot);
                else _s.Fx.Defeat(p.Hips != null ? p.Hips : p.Slot);
            }
            ArmBeat(idx);
            yield return new WaitForSeconds(Mathf.Max(ca.length, cb.length) / RingStage3D.PlaySpeed);
            // Після підняття руки переможець святкує, а не завмирає зі складеними руками (кадр 102).
            var cheer = _s.Clip("Idle_Hype_Loop");
            if (cheer != null) W.PlayHere(cheer, true);
        }

        /// <summary>
        /// Кіноплан перемоги: камера низько (нахил знизу вгору) з краю рингу спереду переможця; у кадрі переможець увесь із
        /// піднятою рукою, рефері поруч, той, хто програв, лежить на передньому плані нижче. Голови — нижче зони слова
        /// «ПЕРЕМОГА» (верхні 8–22 % кадру, Статут FX-05); панелі внизу на цей момент немає (результат іде після моменту).
        /// </summary>
        private void VictoryShot(RingActor W, RingActor R, RingActor L, bool lDown, float yawW, Vector3 pW, Vector3 pR)
        {
            float hW = Mathf.Max(1.5f, W.HeadPos.y - W.Slot.position.y + 0.12f);
            float hR = Mathf.Max(1.5f, R.HeadPos.y - R.Slot.position.y + 0.12f);
            Vector3? lie = lDown ? L.PosePos : (Vector3?)null;
            var shot = VictoryFrame(pW, pR, hW, hR, lie, yawW, true);
            if (!shot.Ok) shot = VictoryFrame(pW, pR, hW, hR, lie, yawW, false);
            if (shot.Ok) _s.Cine(shot.Pos, shot.Look, shot.Fov, true);
            else Debug.LogWarning("[Ринг] 3D: кіноплан перемоги не вмістився");
        }

        /// <summary>
        /// Розв'язок кадру перемоги: стопи й піднята рука переможця, голова рефері, той, хто програв (якщо лежить). Камера
        /// дивиться на переможця (курс протилежний його курсу), трохи знизу вгору. <paramref name="inside"/> — камера в
        /// межах рингу (основний варіант); інакше — з-за рингу, трохи ширше (запасний).
        /// </summary>
        private RingCine.Shot VictoryFrame(Vector3 pW, Vector3 pR, float hW, float hR, Vector3? lie, float yawW, bool inside)
        {
            // Голови переможця й рефері — одразу під зоною слова «ПЕРЕМОГА» (24 %), піднята рука може зайти вище (до 8 %):
            // пара більша, порожньої зали над нею менше (зауваження «Відео огляду» до сезону s11: пара в нижній третині).
            var pts = new List<Vector3>
            {
                pW, pW + Vector3.up * (hW + 0.45f),                          // стопи й піднята рука
                pR, pR + Vector3.up * hR,
                pW + Vector3.up * hW,                                        // голова переможця
            };
            var tops = new List<float> { 0.27f, 0.08f, 0.27f, 0.24f, 0.24f };
            if (lie.HasValue) { pts.Add(lie.Value + Vector3.up * 0.15f); tops.Add(0.27f); }
            float yawCam = yawW + 180f;
            // Камера в рингу — не ближче 0,5 м до канатів: біля самих канатів верхній канат різав кадр навскіс на передньому
            // плані (той самий огляд, кадри 158–161).
            float edge = RingStage3D.RingHalf - 0.5f;
            var fovs = new[] { 44f, 50f, 56f, 62f, 68f };
            if (inside)
                return RingCine.FrameAny(pts, yawCam, -8f, fovs, _s.CamAspect, 0.27f, 0.95f, 0.06f,
                    p => Mathf.Abs(p.x) <= edge && Mathf.Abs(p.z) <= edge && p.y >= RingStage3D.MatY + 0.3f, 1.0f, 5f, tops);
            return RingCine.FrameAny(pts, yawCam, -4f, fovs, _s.CamAspect, 0.25f, 0.97f, 0.04f,
                p => p.y >= RingStage3D.MatY + 0.3f && Mathf.Abs(p.x) < 7f && Mathf.Abs(p.z) < 7f, 1.0f, 9f, tops);
        }

        /// <summary>Скільки рингу від точки до канатів у напрямку <paramref name="d"/>; бік залу (−Z) — з перевагою 0,3 м.</summary>
        private static float Room(Vector3 p, Vector3 d)
        {
            float along = p.x * d.x + p.z * d.z;
            // Переможцю теж треба місце за тим, хто лежить: якщо 0,95 м за ним уже канати — бік гірший.
            float behind = Mathf.Max(0f, -along + 0.95f - (RingStage3D.RingHalf - 0.75f));
            return RingStage3D.RingHalf - along + (d.z < -0.5f ? 0.3f : 0f) - 2f * behind;
        }

        /// <summary>
        /// Розстановка кадру перемоги для камери з боку <paramref name="d"/> (одиничний вектор від того, хто програв, до
        /// камери): переможець лицем до камери, за тим, хто лежить (на 0,95 м далі від камери, на 0,25 м ближче до осі),
        /// або — якщо той стоїть — за центром рингу; рефері — за зміщенням кліпу підняття руки.
        /// </summary>
        private static void VictoryPlace(Vector3 lp, bool lDown, Vector3 refOffset, Vector3 d, out Vector3 pB, out Vector3 pA, out float yaw)
        {
            float lim = RingStage3D.RingHalf - 0.75f;
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;                     // переможець лицем до камери
            var perp = new Vector3(d.z, 0f, -d.x);
            float lateral = lp.x * perp.x + lp.z * perp.z;
            pB = lDown ? lp - d * 0.95f + perp * (lateral > 0f ? -0.25f : 0.25f)
                       : -d * 0.6f + perp * (-lateral * 0.4f);
            pB = new Vector3(Mathf.Clamp(pB.x, -lim, lim), RingStage3D.MatY, Mathf.Clamp(pB.z, -lim, lim));
            pA = pB - Quaternion.Euler(0f, yaw, 0f) * refOffset;
            float lr = RingStage3D.RingHalf - 0.35f;
            pA = new Vector3(Mathf.Clamp(pA.x, -lr, lr), RingStage3D.MatY, Mathf.Clamp(pA.z, -lr, lr));
        }

        // ============================ поза = стан ядра ============================

        private static readonly string[] Sells = { "Bump_Back_Sell_Back", "Bump_Back_Sell_Head", "Bump_Back_Sell_Arch", "Bump_Back_Sell_Reach" };

        private bool Lying(RingActor a)
        {
            var c = a.CurrentClip;
            if (c == null) return false;
            var info = _s.Info(c.name);
            return info != null && info.EndsLying;
        }

        private bool Standing(RingActor a)
        {
            var c = a.CurrentClip;
            if (c == null) return true;
            var info = _s.Info(c.name);
            return info == null || info.EndsStanding || info.Loop;
        }

        /// <summary>
        /// Розсинхрон «ядро каже — лежить, модель стоїть» (огляд 04.11.2026, кадр фінішера: «Лежить 10/12», а постать
        /// стоїть): кліп прийому скінчився стоячи, а ефект поклав борця. Тоді він продає падіння (кліп продажу; вибитий —
        /// нокаут). Навпаки — ядро підняло, а модель лежить: встає.
        /// </summary>
        private IEnumerator MatchPoses(MatchEvent ev)
        {
            foreach (var side in new[] { MatchSide.Player, MatchSide.Rival })
            {
                var a = _s.Actor(side);
                var m = _m[I(side)];
                AnimationClip clip = null;
                if (m.Posture == Posture.Down && !Lying(a) && Standing(a))
                {
                    string name = m.Damage >= MatchRules.ExhaustedDamage ? "Bump_Back_Sell_KO" : Sells[(ev.Seq + I(side)) % Sells.Length];
                    clip = _s.Clip(name) ?? _s.Clip("Bump_Back");
                }
                else if (m.Posture != Posture.Down && Lying(a)) clip = _s.Clip("GetUp_Back");
                if (clip == null) continue;
                a.PlayHere(clip, false, RingStage3D.PlaySpeed);
                yield return new WaitForSeconds(Mathf.Min(clip.length, 2.2f) / RingStage3D.PlaySpeed);
            }
        }

        // ============================ повернення в стійку ============================

        /// <summary>
        /// Після події — у стійку лицем до суперника. Борці на рингу лишаються там, де закінчили (без зайвих перестановок
        /// ковзанням на кожну подію); за рингом — стають на точки пари; виправляється
        /// лише відстань між ними (не ближче 1,2 м, не далі 2,6 м) і край рингу. Рефері лишається, якщо не стоїть між ними
        /// чи перед ними з боку камери; інакше відходить убік від пари. Усі перестановки — кроками (<see cref="Walk"/>).
        /// </summary>
        private IEnumerator Settle()
        {
            var items = new List<SlideItem>();
            var P = _s.Actor(MatchSide.Player);
            var Rv = _s.Actor(MatchSide.Rival);
            Vector3 pp = SettlePos(MatchSide.Player), pr = SettlePos(MatchSide.Rival);
            bool upP = _m[0].Posture != Posture.Down, upR = _m[1].Posture != Posture.Down;
            if (upP && upR && !_m[0].Outside)
            {
                Vector3 d = pr - pp;
                d.y = 0f;
                float dist = d.magnitude;
                Vector3 l = dist > 0.05f ? d / dist : Vector3.right;
                float want = dist < 1.2f ? 1.3f : dist > 2.6f ? 2.2f : dist;
                Vector3 m = (pp + pr) * 0.5f;
                pp = m - l * (want * 0.5f);
                pr = m + l * (want * 0.5f);
                float lim = RingStage3D.RingHalf - 0.6f;
                Vector3 sh = Vector3.zero;
                foreach (var q in new[] { pp, pr })
                {
                    if (q.x > lim) sh.x = Mathf.Min(sh.x, lim - q.x); else if (q.x < -lim) sh.x = Mathf.Max(sh.x, -lim - q.x);
                    if (q.z > lim) sh.z = Mathf.Min(sh.z, lim - q.z); else if (q.z < -lim) sh.z = Mathf.Max(sh.z, -lim - q.z);
                }
                pp += sh; pr += sh;
                pp.y = pr.y = RingStage3D.MatY;
            }
            // Лицем до суперника.
            if (upP) items.Add(new SlideItem(P, pp, RingStage3D.YawTo(pp, pr)));
            if (upR) items.Add(new SlideItem(Rv, pr, RingStage3D.YawTo(pr, pp)));

            var R = _s.Referee;
            Vector3 fm = (pp + pr) * 0.5f;
            Vector3 rp;
            if (_m[0].Outside) { rp = OutsideRefPos(); fm = RingStage3D.OutsideAnchor; }
            else rp = RefOk(R.PosePos, pp, pr) ? R.PosePos : RefSpot(pp, pr);
            items.Add(new SlideItem(R, rp, RingStage3D.YawTo(rp, fm)));
            yield return Transit(items.ToArray());
        }

        /// <summary>Найдальший перехід борця коротким кроком у стійці, м (за 0,15–0,25 с); далі — звичайна хода (<see cref="Walk"/>).</summary>
        private const float QuickMaxDist = 0.85f;

        /// <summary>
        /// Перехід між діями ланцюга: борці, що стоять і мають зсунутись не далі <see cref="QuickMaxDist"/> з поворотом до
        /// 100°, переходять у стійці ковзанням 0,15–0,25 с (рішення Ф2: переходи ланцюга без ходи 0,7 с); якщо кліп скінчився
        /// не стійкою — спершу ще до 0,12 с кросфейду в стійку (<see cref="Slide"/> чекає кінця переходу: знімок тазу посеред
        /// нього водив таз по колу). Ковзання стійки — підстановка до кліпа короткого переступу (CLIP_NEEDS). Решта — як раніше
        /// (<see cref="Walk"/>: рефері — кроком, лежачий — ковзанням, далекий — ходою). Обидві групи йдуть разом.
        /// </summary>
        private IEnumerator Transit(SlideItem[] items)
        {
            var quick = new List<SlideItem>();
            var rest = new List<SlideItem>();
            for (int i = 0; i < items.Length; i++)
            {
                var a = items[i].Actor;
                Vector3 d = items[i].Pos - a.PosePos;
                d.y = 0f;
                float dist = d.magnitude;
                bool q = a != _s.Referee && Upright(a) && IdleOf(a) != null && dist > 0.06f && dist <= QuickMaxDist
                    && Mathf.Abs(Mathf.DeltaAngle(a.PoseYaw, items[i].Yaw)) <= 100f;
                if (q) quick.Add(items[i]); else rest.Add(items[i]);
            }
            Coroutine w = rest.Count > 0 ? StartCoroutine(Walk(rest.ToArray())) : null;
            Coroutine k = quick.Count > 0 ? StartCoroutine(QuickStep(quick.ToArray())) : null;
            if (w != null) yield return w;
            if (k != null) yield return k;
        }

        private IEnumerator QuickStep(SlideItem[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                var a = items[i].Actor;
                StopWalk(a);
                var idle = IdleOf(a);
                // Кінцева поза минулого кліпа — спершу в стійку на місці (коротко), а вже тоді переходить.
                if (a.CurrentClip != idle) a.PlayAtBlend(idle, 0f, true, a.PosePos, a.PoseYaw, 0.12f);
            }
            yield return Slide(items, 0.15f);
        }

        /// <summary>Де борець стає після події: за рингом — на точці пари, на рингу — там, де стоїть (біля краю — трохи всередину).</summary>
        private Vector3 SettlePos(MatchSide s)
        {
            if (_m[I(s)].Outside) return RingStage3D.PairPos(s, true);
            Vector3 p = _s.Actor(s).PosePos;
            float lim = RingStage3D.RingHalf - 0.6f;
            return new Vector3(Mathf.Clamp(p.x, -lim, lim), RingStage3D.MatY, Mathf.Clamp(p.z, -lim, lim));
        }

        /// <summary>
        /// Рефері тут не заважає: не ближче 0,85 м до лінії між борцями, у рингу (0,35 м від канатів) і не перед ними з боку
        /// камери (камера матчу — з боку залу, −Z).
        /// </summary>
        private static bool RefOk(Vector3 r, Vector3 a, Vector3 b)
        {
            float lim = RingStage3D.RingHalf - 0.35f;
            if (Mathf.Abs(r.x) > lim || Mathf.Abs(r.z) > lim) return false;
            if (r.z < Mathf.Min(a.z, b.z) - 0.2f) return false;
            Vector3 ab = b - a; ab.y = 0f;
            float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(r - a, ab) / ab.sqrMagnitude) : 0f;
            Vector3 c = a + ab * t;
            return new Vector2(r.x - c.x, r.z - c.z).magnitude >= 0.85f;
        }

        /// <summary>Місце рефері біля пари: 1,3 м убік від середини, з дальнього від камери боку (+Z), у межах рингу.</summary>
        private static Vector3 RefSpot(Vector3 a, Vector3 b)
        {
            Vector3 m = (a + b) * 0.5f;
            Vector3 l = b - a; l.y = 0f;
            l = l.sqrMagnitude > 1e-4f ? l.normalized : Vector3.right;
            Vector3 n = new Vector3(-l.z, 0f, l.x);
            if (n.z < 0f) n = -n;
            Vector3 p = m + n * 1.3f;
            float lim = RingStage3D.RingHalf - 0.45f;
            return new Vector3(Mathf.Clamp(p.x, -lim, lim), RingStage3D.MatY, Mathf.Clamp(p.z, -lim, lim));
        }

        // ============================ хода ============================

        /// <summary>Ходовий кліп на цьому тілі: напрям ходу в системі слота (за оберт петлі) і швидкість, м/с при швидкості 1.</summary>
        private struct Gait
        {
            public AnimationClip Clip;
            public Vector3 Dir;
            public float Speed;
        }

        private readonly Dictionary<string, Gait> _gaits = new Dictionary<string, Gait>();

        private Gait GaitOf(RingActor a, string name)
        {
            Gait g;
            string key = a.Name + "|" + name;
            if (_gaits.TryGetValue(key, out g)) return g;
            g = new Gait { Clip = _s.Clip(name) };
            if (g.Clip != null)
            {
                float len = g.Clip.length, e = 1f / 60f;
                var pos = new Vector3[2];
                var yaw = new float[2];
                a.SamplePath(g.Clip, new[] { 0f, len - e }, pos, yaw, true);
                Vector3 d = pos[1] - pos[0];
                d.y = 0f;
                g.Speed = d.magnitude / Mathf.Max(1e-3f, len - e);
                g.Dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] хода: {0} {1} — {2:0.00} м/с, напрям ({3:0.00},{4:0.00}), у маніфесті руху {5:0.00} од./цикл",
                    a.Name, name, g.Speed, g.Dir.x, g.Dir.z, _s.MotionNet(name)));
            }
            _gaits[key] = g;
            return g;
        }

        private readonly Dictionary<RingActor, AnimationClip> _idleOverride = new Dictionary<RingActor, AnimationClip>();

        /// <summary>
        /// Хода поза матчем (вихід борця, ENTRANCE_PROFILE.md біт 4: до центру, до кута, до канатів) — та сама, що в матчі
        /// (кроки, розвороти кліпами, кермо дугою), з заданою стійкою наприкінці: стан минулого матчу (лежить, кут) не діє.
        /// </summary>
        public IEnumerator WalkFree(RingActor a, Vector3 pos, float yaw, AnimationClip idle)
        {
            if (a == null || idle == null) yield break;
            _idleOverride[a] = idle;
            try { yield return Walk(new[] { new SlideItem(a, pos, yaw) }); }
            finally { _idleOverride.Remove(a); }
        }

        /// <summary>Зупинити ходу поза матчем (вихід пропущено).</summary>
        public void StopWalkFree(RingActor a)
        {
            StopWalk(a);
            _idleOverride.Remove(a);
        }

        private AnimationClip IdleOf(RingActor a)
        {
            AnimationClip over;
            if (a != null && _idleOverride.TryGetValue(a, out over)) return over;
            if (a == _s.Referee) return _s.Clip("Ref_Idle_Loop");
            var side = a == _s.Actor(MatchSide.Player) ? MatchSide.Player : MatchSide.Rival;
            string n = IdleName(side);
            return n != null ? _s.Clip(n) : null;
        }

        /// <summary>
        /// Усі переходять на свої місця кроками (правило режисера прев'ю: «хто має перейти на інше місце — переходить
        /// кроками (поворот, крок у стійці, хода), а не ковзає»; пакет «Відео огляд» 08.11.2026, п. 8 — без ковзання стоп).
        /// Таз несе сам ходовий кліп (запечений хід петлі), слот лише повертається навколо таза — кермо дугою; швидкість —
        /// множник кліпа, з кривої таза. Хто лежить — лишається (ковзання — лише для нього, як раніше).
        /// </summary>
        private IEnumerator Walk(SlideItem[] items)
        {
            var done = new bool[items.Length];
            var lying = new List<SlideItem>();
            for (int i = 0; i < items.Length; i++)
            {
                var a = items[i].Actor;
                StopWalk(a);
                if ((Upright(a) || a == _s.Referee) && IdleOf(a) != null) _walkCo[a] = StartCoroutine(WalkOne(items[i], done, i));
                else
                {
                    lying.Add(items[i]);
                    done[i] = true;
                    if (Trace)
                        Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] ковзання (не стоїть): {0} з ({1:0.00},{2:0.00}) у ({3:0.00},{4:0.00}) — {5}, таз {6:0.00} м",
                            a.Name, a.PosePos.x, a.PosePos.z, items[i].Pos.x, items[i].Pos.z, a.CurrentClip != null ? a.CurrentClip.name : "—",
                            a.Hips != null ? a.Hips.position.y - a.Slot.position.y : 0f));
                }
            }
            // Хто лежить чи ще встає — ковзання, як і раніше; його теж дочекатися (сезон 08.11.2026: без очікування прийом
            // стартував, поки ковзання ще рухало постать, — стрибок на 1,2 м у Center_Headbutt_A).
            Coroutine slide = lying.Count > 0 ? StartCoroutine(Slide(lying.ToArray())) : null;
            float guard = Time.time + 9f;
            while (Time.time < guard)
            {
                bool all = true;
                for (int i = 0; i < done.Length; i++) all &= done[i];
                if (all) break;
                yield return null;
            }
            for (int i = 0; i < items.Length; i++) if (!done[i]) StopWalk(items[i].Actor);
            if (slide != null) yield return slide;
        }

        private readonly Dictionary<RingActor, Coroutine> _walkCo = new Dictionary<RingActor, Coroutine>();

        /// <summary>Налагодження (<c>-ring-locotest</c>): хода однієї постаті на місце з курсом — тим самим шляхом, що в матчі.</summary>
        public IEnumerator DebugWalk(RingActor a, Vector3 pos, float yaw)
        {
            yield return Walk(new[] { new SlideItem(a, pos, yaw) });
        }

        /// <summary>Зупинити ходу постаті (перед кліпом прийому: хода, що не встигла, не має перебити його своєю стійкою).</summary>
        private void StopWalk(RingActor a)
        {
            Coroutine c;
            if (a != null && _walkCo.TryGetValue(a, out c))
            {
                if (c != null) StopCoroutine(c);
                _walkCo.Remove(a);
                // Зупинений посеред ходи — у стійку на місці (інакше ходовий кліп крутився б на місці).
                var cur = a.CurrentClip;
                var idle = IdleOf(a);
                if (cur != null && idle != null && cur != idle && (cur.name.Contains("Walk") || cur.name.Contains("Circle")))
                    a.PlayAtBlend(idle, 0f, true, a.PosePos, a.PoseYaw, 0.25f);
            }
        }

        /// <summary>
        /// Стоїть на ногах (таз вище 0,68 висоти тазу цієї постаті у випрямленій позі), а не лежить чи на колінах. Поріг —
        /// від зросту: у нижчого борця таз у стійці 0,63 м, і з порогом 0,7 м його вело ковзання (сезон 08.11.2026).
        /// </summary>
        private static bool Upright(RingActor a)
        {
            return a.Hips != null && a.Hips.position.y - a.Slot.position.y > 0.68f * a.StandHip;
        }

        private IEnumerator WalkOne(SlideItem it, bool[] done, int k)
        {
            var a = it.Actor;
            bool isRef = a == _s.Referee;
            var idle = IdleOf(a);
            if (Trace)
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] хода до: {0} з ({1:0.00},{2:0.00}) курс {3:0} у ({4:0.00},{5:0.00}) курс {6:0} — {7}",
                    a.Name, a.PosePos.x, a.PosePos.z, a.PoseYaw, it.Pos.x, it.Pos.z, it.Yaw, a.CurrentClip != null ? a.CurrentClip.name : "—"));
            // Рефері після відліку на колінах — спершу встає (кліп), а не ковзає навколішки.
            if (isRef && !Upright(a))
            {
                var up = _s.Clip("Ref_Kneel_Up");
                if (up != null)
                {
                    a.PlayAtBlend(up, 0f, false, a.PosePos, a.PoseYaw, 0.2f, RingStage3D.PlaySpeed);
                    float g0 = Time.time + up.length / RingStage3D.PlaySpeed + 0.3f;
                    while (Time.time < g0 && a.CurrentClip == up && !a.Finished) yield return null;
                }
            }
            Vector3 d = it.Pos - a.PosePos;
            d.y = 0f;
            float dist = d.magnitude;
            // Хто спирається на кут і має відійти — спершу виходить із кута кліпом (поза в куті й хода надто різні для
            // переходу: стопи ковзали на 1–1,5 м за фаз-тестом).
            var cc = a.CurrentClip;
            var ci = cc != null ? _s.Info(cc.name) : null;
            if (!isRef && dist >= 0.06f && cc != null && (cc.name == "Move_Corner_Lean_Loop" || (ci != null && ci.EndNode == "corner_lean")))
            {
                var ex = _s.Clip("Move_Corner_Exit");
                if (ex != null)
                {
                    a.PlayAtBlend(ex, 0f, false, a.PosePos, a.PoseYaw, 0.2f, RingStage3D.PlaySpeed);
                    float gx = Time.time + ex.length / RingStage3D.PlaySpeed + 0.3f;
                    while (Time.time < gx && a.CurrentClip == ex && !a.Finished) yield return null;
                    d = it.Pos - a.PosePos;
                    d.y = 0f;
                    dist = d.magnitude;
                }
            }
            if (dist < 0.06f)
            {
                yield return TurnTo(a, it.Yaw, idle, isRef);
                done[k] = true;
                yield break;
            }
            // Борець перед ходою спершу розвертається, якщо стоїть до цілі й до кінцевого курсу зовсім не тим боком.
            if (!isRef && Mathf.Abs(Mathf.DeltaAngle(a.PoseYaw, it.Yaw)) > 100f)
            {
                yield return TurnTo(a, it.Yaw, idle, false);
                d = it.Pos - a.PosePos;
                d.y = 0f;
                dist = d.magnitude;
            }
            // Хода: рефері — своєю (уперед), борець — та з чотирьох (уперед, назад, кружляння вліво/вправо), чий хід ближчий
            // до напрямку цілі відносно того, куди він дивиться, — щоб курс мінявся якнайменше.
            Gait g;
            float yawD = YawOf(d);
            if (isRef) g = GaitOf(a, "Ref_Walk_Loop");
            else
            {
                g = default(Gait);
                float best = float.MaxValue;
                float face = a.PoseYaw;
                foreach (var n in new[] { "Move_Walk_Fwd_Loop", "Move_Walk_Back_Loop", "Move_Circle_L_Loop", "Move_Circle_R_Loop" })
                {
                    var c = GaitOf(a, n);
                    if (c.Clip == null || c.Speed < 0.05f) continue;
                    float slotYaw = yawD - YawOf(c.Dir);
                    float turn = Mathf.Abs(Mathf.DeltaAngle(face, slotYaw)) + 0.5f * Mathf.Abs(Mathf.DeltaAngle(slotYaw, it.Yaw));
                    if (turn < best) { best = turn; g = c; }
                }
            }
            if (g.Clip == null || g.Speed < 0.05f)
            {
                yield return Slide(new[] { it });
                done[k] = true;
                yield break;
            }
            // Цілими циклами: цикл ходи борця в генераторі — від стійки до стійки (ведуча стопа, тоді приставна), тож на межі
            // циклу стопи стоять як у стійці й перехід у неї не ковзає (обрив посеред кроку ковзав стопами на 15 см). Залишок
            // відстані — поправкою, розподіленою на всю ходу (правило режисера прев'ю). Рефері ходить звичайним кроком —
            // йому межа півциклу. Ціль ближче за пів кроку — без ходи (решту розподілить старт наступного кліпа).
            float L = g.Clip.length;
            float unit = g.Speed * L * (isRef ? 0.5f : 1f);
            if (dist < unit * 0.55f)
            {
                yield return TurnTo(a, it.Yaw, idle, isRef);
                done[k] = true;
                yield break;
            }
            int steps = Mathf.Max(1, Mathf.RoundToInt(dist / unit));
            float total = steps * L * (isRef ? 0.5f : 1f);
            // Швидкість: перехід ~0,7 с, але хода не швидша за ×2,6 (борець) / ×1,8 (рефері) і не повільніша за ×1.
            float kMax = isRef ? 1.8f : 2.6f;
            float spd = Mathf.Clamp(dist / (g.Speed * 0.7f), 1f, kMax);
            float yawSlot = yawD - YawOf(g.Dir);
            a.PlayAtBlend(g.Clip, 0f, true, a.PosePos, yawSlot, 0.22f, spd, true);
            float guard = Time.time + total / spd * 1.6f + 1f;
            yield return null;
            while (Time.time < guard && a.CurrentClip == g.Clip)
            {
                // Пройдений час ходи — з обертами петлі (на ×3 огляду кліп за кадр просувається більше ніж на пів циклу, і
                // лічба за різницею часу пропускала кадри: хода тривала довше, а поправка тягла слот назад — ковзання рефері).
                float acc = a.ClipTimeTotal;
                if (acc >= total - spd * Time.deltaTime * 0.5f) break;
                Vector3 r = it.Pos - a.PosePos;
                r.y = 0f;
                // Під час переходу слот ще повертається від старого вирівнювання — напрям ходу беремо задуманий (старий
                // давав «ціль позаду», і хода закінчувалась, не почавшись — демо 08.11.2026, «телепорт 1,84 м»).
                Vector3 dirW = a.Blending ? Quaternion.Euler(0f, yawSlot, 0f) * g.Dir : a.Slot.rotation * g.Dir;
                if (!a.Blending && r.sqrMagnitude > 1e-4f)
                {
                    // Кермо дугою: хід кліпа — на ціль.
                    float err = Vector3.SignedAngle(dirW, r, Vector3.up);
                    float maxTurn = 120f * Time.deltaTime;
                    a.TurnAboutHips(Mathf.Clamp(err, -maxTurn, maxTurn));
                    // Залишок: скільки ще пройде сам кліп до кінця циклів проти того, скільки лишилось до цілі.
                    float remClip = Mathf.Max(0f, total - acc) * g.Speed;
                    float remTime = Mathf.Max(Time.deltaTime, (total - acc) / spd);
                    float need = r.magnitude - remClip;
                    // Поправка — не швидше за 0,3 м/с (за годинником гри, не за кадр): що не встигне — доставить старт
                    // наступного кліпа.
                    float cap = 0.3f * Time.deltaTime;
                    a.Nudge(r.normalized * Mathf.Clamp(need / remTime * Time.deltaTime, -cap, cap));
                }
                yield return null;
            }
            yield return TurnTo(a, it.Yaw, idle, isRef);
            done[k] = true;
        }

        /// <summary>
        /// Розворот на місці до курсу: від 45° — кліпом розвороту (Move_Turn_L90 / R90 / 180, стопи переступають), решта —
        /// переходом у стійку з новим курсом (до ~45°, піввороту стоп). Рефері кліпів розвороту не має — лише переходом.
        /// </summary>
        private IEnumerator TurnTo(RingActor a, float yaw, AnimationClip idle, bool isRef)
        {
            float delta = Mathf.DeltaAngle(a.PoseYaw, yaw);
            if (!isRef && Mathf.Abs(delta) >= 45f)
            {
                // Unity: + — за годинниковою (праворуч); у кліпах «+ ліворуч»: R90 — це +90 тут.
                string n = Mathf.Abs(delta) >= 135f ? "Move_Turn_180" : delta > 0f ? "Move_Turn_R90" : "Move_Turn_L90";
                var c = _s.Clip(n);
                if (c != null)
                {
                    float ps = RingStage3D.PlaySpeed * 1.3f;
                    a.PlayAtBlend(c, 0f, false, a.PosePos, a.PoseYaw, 0.12f, ps);
                    float guard = Time.time + c.length / ps + 0.5f;
                    while (Time.time < guard && a.CurrentClip == c && a.ClipTime < c.length * 0.9f) yield return null;
                }
            }
            if (idle == null) yield break;
            float rest = Mathf.Abs(Mathf.DeltaAngle(a.PoseYaw, yaw));
            if (a.CurrentClip == idle && rest < 4f) yield break;
            float fade = Mathf.Clamp(0.22f + rest / 120f, 0.22f, 0.5f);
            a.PlayAtBlend(idle, 0f, true, a.PosePos, yaw, fade);
            yield return new WaitForSeconds(fade);
        }

        // ============================ ковзання ============================

        public struct SlideItem
        {
            public RingActor Actor;
            public Vector3 Pos;
            public float Yaw;
            public Vector3 StartPos;
            public float StartYaw;

            public SlideItem(RingActor a, Vector3 p, float yaw)
            {
                Actor = a; Pos = p; Yaw = yaw; StartPos = Vector3.zero; StartYaw = 0f;
            }
        }

        /// <summary>Усі переходять у задані пози (таз — у точку, курс — у напрямок) за час, пропорційний відстані.</summary>
        private IEnumerator Slide(SlideItem[] items, float minDur = 0.3f)
        {
            // Спершу — дочекатися кінця переходів: знімок тазу в слоті посеред переходу (таз іще наполовину в старому кліпі,
            // за метри від слота) з поворотом слота водив таз по колу на 1,4 м (сезон 08.11.2026).
            float gb = Time.time + 0.8f;
            while (Time.time < gb)
            {
                bool any = false;
                for (int i = 0; i < items.Length; i++) any |= items[i].Actor.Blending;
                if (!any) break;
                yield return null;
            }
            float maxDist = 0f;
            for (int i = 0; i < items.Length; i++)
            {
                var a = items[i].Actor;
                a.SlideBegin();
                items[i].StartPos = a.PosePos;
                items[i].StartYaw = a.PoseYaw;
                Vector3 d = items[i].Pos - items[i].StartPos;
                d.y = 0f;
                maxDist = Mathf.Max(maxDist, d.magnitude);
            }
            float dur = Mathf.Clamp(maxDist / 3.5f, minDur, 1.2f);
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float s = Mathf.SmoothStep(0f, 1f, t / dur);
                for (int i = 0; i < items.Length; i++) Set(items[i], s);
                yield return null;
            }
            for (int i = 0; i < items.Length; i++) Set(items[i], 1f);
            yield return null;
        }

        private static void Set(SlideItem it, float s)
        {
            Vector3 p = Vector3.Lerp(it.StartPos, it.Pos, s);
            float yaw = Mathf.LerpAngle(it.StartYaw, it.Yaw, s);
            it.Actor.SlideSet(p, yaw);
            it.Actor.SetHeight(Mathf.Lerp(it.StartPos.y, it.Pos.y, s));
        }
    }
}
