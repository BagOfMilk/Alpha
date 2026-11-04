using System.Collections;
using System.Collections.Generic;
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
            public Zone Zone;
            public Posture Posture;
            public int Damage;
        }

        private readonly Mirror[] _m = new Mirror[2];

        public bool IsBusy
        {
            get { return _running || _q.Count > 0 || (_e != null && _e.State.Log.Count > _seen); }
        }

        public void Init(RingStage3D stage) { _s = stage; }

        // ============================ життєвий цикл ============================

        public void Begin(MatchEngine e)
        {
            StopAllCoroutines();
            _running = false;
            _ended = false;
            _q.Clear();
            _e = e;
            _seen = e != null ? e.State.Log.Count : 0;
            if (e != null)
            {
                _m[0] = new Mirror { Zone = e.State.Player.Zone, Posture = e.State.Player.Posture, Damage = e.State.Player.Damage };
                _m[1] = new Mirror { Zone = e.State.Rival.Zone, Posture = e.State.Rival.Posture, Damage = e.State.Rival.Damage };
            }
            else
            {
                _m[0] = new Mirror(); _m[1] = new Mirror();
            }
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
            _running = false;
            _q.Clear();
            _e = null;
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

        // ============================ дзеркало зон і поз ============================

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
                    case MatchEffectKind.ZoneChange: _m[k].Zone = (Zone)fx.Amount; break;
                    case MatchEffectKind.Damage: _m[k].Damage += fx.Amount; break;
                }
            }
            // Оглушений приходить до тями кінцем свого ходу — ефекту нема; перший самостійний хід знімає (рушій: TurnEnd).
            if (ev.Kind == MatchEventKind.TurnEnd && _m[I(ev.Actor)].Posture == Posture.Stunned) _m[I(ev.Actor)].Posture = Posture.Standing;
        }

        private Vector3 Canon(MatchSide s) { return RingStage3D.ZonePos(s, _m[I(s)].Zone); }

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
            Vector3 pp = Canon(MatchSide.Player), pr = Canon(MatchSide.Rival);
            _s.Place(_s.Actor(MatchSide.Player), pp, RingStage3D.YawTo(pp, pr), IdleName(MatchSide.Player) ?? "Ring_Stance_Loop");
            _s.Place(_s.Actor(MatchSide.Rival), pr, RingStage3D.YawTo(pr, pp), IdleName(MatchSide.Rival) ?? "Ring_Stance_Loop");
            _s.Place(_s.Referee, RingStage3D.RefPos, 180f, "Ref_Idle_Loop");
        }

        // ============================ черга ============================

        private IEnumerator Run()
        {
            _running = true;
            while (_q.Count > 0)
            {
                var ev = _q.Dequeue();
                var steps = RingClipTable.Plan(ev);
                bool applied = false;
                // Звук події — на контакті приблизно (0,4 с від початку кроку); кінець матчу — дзвін.
                if (_s.Sound != null)
                {
                    bool pinEnd = ev.Kind == MatchEventKind.MatchEnd && _e != null && _e.IsOver && _e.Result.Kind == MatchEndKind.Pin;
                    _s.Sound.OnEvent(ev, steps.Count > 0 ? 0.4f / RingStage3D.PlaySpeed : 0f, pinEnd);
                    if (ev.Kind == MatchEventKind.MatchEnd) _s.Sound.BellEnd();
                }
                foreach (var st in steps)
                {
                    if (st.Kind == StageStepKind.Walk && !applied) { Apply(ev); applied = true; }
                    yield return RunStep(st, ev);
                }
                if (!applied) Apply(ev);
                // Натовп живе за ажіотажем; кікаут і кінець матчу — вибух залу.
                if (_e != null) _s.CrowdMood(_e.State.Hype);
                if (ev.Kind == MatchEventKind.Kickout || ev.Kind == MatchEventKind.MatchEnd) _s.CrowdPop();
                if (ev.Kind == MatchEventKind.MatchEnd) { _ended = true; break; }
                if (steps.Count > 0 || ev.Kind == MatchEventKind.TurnEnd) yield return Settle();
            }
            _running = false;
            if (!_ended) _s.SetShot(StageCam.Overview);
        }

        // ============================ кроки ============================

        private IEnumerator RunStep(StageStep st, MatchEvent ev)
        {
            _s.SetShot(st.Cam);
            switch (st.Kind)
            {
                case StageStepKind.Pair: yield return RunPair(st); break;
                case StageStepKind.Solo: yield return RunSolo(st); break;
                case StageStepKind.Walk: yield return RunWalk(st); break;
                case StageStepKind.Ref: yield return RunRef(st); break;
                case StageStepKind.End: yield return RunEnd(); break;
            }
        }

        private IEnumerator RunSolo(StageStep st)
        {
            var a = _s.Actor(st.Actor);
            var clip = _s.Clip(st.Clip);
            if (clip == null) yield break;
            a.PlayHere(clip, false, RingStage3D.PlaySpeed);
            float len = clip.length / RingStage3D.PlaySpeed;
            if (st.Once) len = Mathf.Min(len, 1.7f);
            yield return new WaitForSeconds(len);
        }

        private IEnumerator RunRef(StageStep st)
        {
            var r = _s.Referee;
            var clip = _s.Clip(st.RefClip);
            if (clip == null) yield break;
            if (st.RefFacesActor)
                r.PlayAt(clip, false, r.PosePos, RingStage3D.YawTo(r.PosePos, _s.Actor(st.Actor).PosePos), RingStage3D.PlaySpeed);
            else r.PlayHere(clip, false, RingStage3D.PlaySpeed);
            float len = st.Hold > 0f ? st.Hold : clip.length / RingStage3D.PlaySpeed;
            yield return new WaitForSeconds(Mathf.Min(len, clip.length / RingStage3D.PlaySpeed));
        }

        private IEnumerator RunWalk(StageStep st)
        {
            var a = _s.Actor(st.Actor);
            var other = _s.Actor(RingClipTable.Opp(st.Actor));
            var walk = _s.Clip(st.Clip);
            Vector3 target = Canon(st.Actor);
            Vector3 start = a.PosePos;
            Vector3 dir = target - start;
            dir.y = 0f;
            float dist = dir.magnitude;
            float yaw = dist > 0.1f ? RingStage3D.YawTo(start, target) : a.Slot.eulerAngles.y;
            if (walk != null) a.PlayHere(walk, true, 1.25f);
            a.SlideBegin();
            float dur = Mathf.Clamp(dist / 2.2f, 0.5f, 1.8f);
            float y0 = start.y;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float s = Mathf.SmoothStep(0f, 1f, t / dur);
                Vector3 p = Vector3.Lerp(start, target, s);
                a.SlideSet(p, yaw);
                a.SetHeight(Mathf.Lerp(y0, target.y, s));
                yield return null;
            }
            a.SlideSet(target, RingStage3D.YawTo(target, other.PosePos));
            a.SetHeight(target.y);
        }

        /// <summary>
        /// Чи прийом іде в центрі: обидва до прийому в зоні «центр». Зона — стан до прийому, тож «у канати» (Lockup_To_Ropes)
        /// теж стартує з центру; біля канатів, у куті й поза рингом пара лишається там.
        /// </summary>
        private bool InCenter(StageStep st, MatchSide a, MatchSide b)
        {
            return _m[I(a)].Zone == Zone.Center && _m[I(b)].Zone == Zone.Center;
        }

        private IEnumerator RunPair(StageStep st)
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
            pA.y = pB.y = _m[I(sb)].Zone == Zone.Outside ? 0f : RingStage3D.MatY;

            // Прийом по центру рингу, якщо він не вимагає кута, канатів чи місця поза рингом (власник 30.10.2026 і 04.11.2026:
            // «Чому прийому не по центру ринга? завжди по центру»): пара зсувається так, що її середина — у центрі. Раніше
            // якорем був B на своїй точці (0,85 м убік) чи там, де впав, і пара працювала збоку.
            if (InCenter(st, sa, sb))
            {
                Vector3 d = new Vector3(0f, RingStage3D.MatY, 0f) - (pA + pB) * 0.5f;
                d.y = 0f;
                pA += d;
                pB += d;
            }
            // Обидва лишаються всередині рингу: зсув усієї пари до центру.
            else if (_m[I(sb)].Zone != Zone.Outside && _m[I(sa)].Zone != Zone.Outside)
            {
                Vector3 sh = Vector3.zero;
                float lim = RingStage3D.RingHalf - 0.1f;
                if (pA.x > lim) sh.x = lim - pA.x; else if (pA.x < -lim) sh.x = -lim - pA.x;
                if (pA.z > lim) sh.z = lim - pA.z; else if (pA.z < -lim) sh.z = -lim - pA.z;
                pA += sh;
                pB += sh;
            }
            Vector3 mid = (pA + pB) * 0.5f;
            // Рефері збоку від пари (~1,2 м), у бік центру рингу й трохи за борцями: не перекриває ані їх, ані камеру.
            float sideX = mid.x > 0.15f ? -1f : 1f;
            Vector3 pRef = new Vector3(Mathf.Clamp(mid.x + sideX * 1.25f, -1.7f, 1.7f), RingStage3D.MatY, Mathf.Clamp(mid.z + 0.9f, -1.2f, 1.7f));
            float yawRef = RingStage3D.YawTo(pRef, mid);

            var refActor = _s.Referee;
            yield return Slide(new[]
            {
                new SlideItem(A, pA, yawA),
                new SlideItem(B, pB, yawB),
                new SlideItem(refActor, pRef, yawRef)
            });

            // Старт: слот у таз і новий кліп — в тому самому кадрі, обидва разом.
            A.PlayAt(ca, false, pA, yawA, RingStage3D.PlaySpeed);
            B.PlayAt(cb, false, pB, yawB, RingStage3D.PlaySpeed);
            float len = Mathf.Max(ca.length, cb.length) / RingStage3D.PlaySpeed;
            if (!string.IsNullOrEmpty(st.RefClip))
            {
                var rc = _s.Clip(st.RefClip);
                if (rc != null)
                {
                    refActor.PlayHere(rc, false, RingStage3D.PlaySpeed);
                    len = Mathf.Max(len, rc.length / RingStage3D.PlaySpeed);
                }
            }
            yield return new WaitForSeconds(len);
        }

        private IEnumerator RunEnd()
        {
            var win = _e != null && _e.Result != null ? _e.Result.Winner : MatchWinner.Player;
            var ia = _s.Info("Ref_Raise_Hand_A");
            var ca = _s.Clip("Ref_Raise_Hand_A");
            var cb = _s.Clip("Ref_Raise_Hand_B");
            var R = _s.Referee;
            if (win == MatchWinner.Draw || ia == null || ca == null || cb == null)
            {
                var bell = _s.Clip("Ref_Call_Bell");
                if (bell != null) { R.PlayHere(bell, false); yield return new WaitForSeconds(Mathf.Min(2f, bell.length)); }
                yield break;
            }
            MatchSide sw = win == MatchWinner.Player ? MatchSide.Player : MatchSide.Rival;
            var W = _s.Actor(sw);
            var L = _s.Actor(RingClipTable.Opp(sw));
            // Переможець стоїть, рефері зліва від нього піднімає йому руку (обидва лицем до глядачів).
            float yaw = 180f;
            Vector3 pB = new Vector3(sw == MatchSide.Player ? -0.5f : 0.5f, RingStage3D.MatY, -0.2f);
            Vector3 pA = pB - Quaternion.Euler(0f, yaw, 0f) * ia.Offset;
            yield return Slide(new[] { new SlideItem(W, pB, yaw), new SlideItem(R, pA, yaw) });
            if (_m[I(RingClipTable.Opp(sw))].Posture != Posture.Down)
            {
                var tired = _s.Clip("Idle_Tired_Loop");
                if (tired != null) L.PlayHere(tired, true);
            }
            R.PlayAt(ca, false, pA, yaw, RingStage3D.PlaySpeed);
            W.PlayAt(cb, false, pB, yaw, RingStage3D.PlaySpeed);
            yield return new WaitForSeconds(Mathf.Max(ca.length, cb.length) / RingStage3D.PlaySpeed);
        }

        // ============================ повернення в стійку ============================

        private IEnumerator Settle()
        {
            var items = new List<SlideItem>();
            foreach (var side in new[] { MatchSide.Player, MatchSide.Rival })
            {
                var a = _s.Actor(side);
                string idle = IdleName(side);
                if (idle == null) continue; // лежить — тримає останній кадр кліпа
                var clip = _s.Clip(idle);
                if (clip == null) continue;
                if (a.CurrentClip != clip)
                {
                    a.PlayHere(clip, true);
                }
            }
            Vector3 pp = Canon(MatchSide.Player), pr = Canon(MatchSide.Rival);
            if (_m[0].Posture != Posture.Down) items.Add(new SlideItem(_s.Actor(MatchSide.Player), pp, RingStage3D.YawTo(pp, pr)));
            if (_m[1].Posture != Posture.Down) items.Add(new SlideItem(_s.Actor(MatchSide.Rival), pr, RingStage3D.YawTo(pr, pp)));

            // Рефері повертається на місце й у стійку.
            var ridle = _s.Clip("Ref_Idle_Loop");
            var R = _s.Referee;
            if (ridle != null && R.CurrentClip != ridle) R.PlayHere(ridle, true);
            Vector3 fm = (pp + pr) * 0.5f;
            items.Add(new SlideItem(R, RingStage3D.RefPos, RingStage3D.YawTo(RingStage3D.RefPos, fm)));
            yield return Slide(items.ToArray(), 0.45f);
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
