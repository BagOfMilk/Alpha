using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Живе очікування в рингу (власник 14.11.2026: «добавь более живие айдл анимации когда просто стояние происходит, что
    /// бы чуть живее выглядело»). Поки актор стоїть у базовій петлі (<c>Ring_Stance_Loop</c> / <c>Ref_Idle_Loop</c>), на межі
    /// петлі час від часу грає одну вставку-жест: струснути руки, озирнутися на зал, підстрибнути, витерти лоб тощо
    /// (<c>tools/blender/ring_anims_idle.py</c>). Що і коли — вирішує чиста модель <see cref="RingIdleLifePlan"/>, тут лише
    /// застосування до постаті.
    /// <para>Шов невидимий за побудовою: вставка — це стійка з накладеним жестом, рівно на один період петлі, з вузла стійки
    /// у вузол стійки, тож вставка в мить t збігається зі стійкою в мить t поза огинаючою жесту. Тому вставка стартує з тієї
    /// самої фази, що й петля, а повернення — з нуля петлі, і обидва — коротким кросфейдом. Кліп, що не петляє, завмирає за
    /// 0,02 с до свого кінця (<c>RingActor.Tick</c>), тож кожна вставка зсуває фазу петлі актора на ≤ 0,04 с — ролі й так
    /// рознесені за фазою (<see cref="RingIdlePhase"/>), і цього не видно.</para>
    /// <para>Режисер — головний: якщо він сам грає інший кліп, вставка просто зникає (стан скидається). Якщо вікно до біту
    /// стиснулося чи режисер заборонив (кіно фінішера, камерний план, лежить, больовий, за рингом), вставка повертається в
    /// петлю тієї самої фази, не дочекавшись кінця.</para>
    /// Не <c>MonoBehaviour</c>: кличе власник сцени після <c>RingActor.Tick</c> у своєму <c>LateUpdate</c>.
    /// </summary>
    public sealed class RingIdleLife
    {
        /// <summary>Вимикач для лукбука й порівняння «з / без» (за замовчуванням увімкнено).</summary>
        public static bool Enabled = true;

        /// <summary>Кросфейд входу у вставку й повернення в петлю, с.</summary>
        public const float Fade = 0.08f;

        /// <summary>Кросфейд дострокового повернення (вікно стиснулося чи заборонено), с.</summary>
        public const float AbortFade = 0.15f;

        private sealed class State
        {
            public AnimationClip Base;          // базова петля, у якій актор зараз стоїть (null — стоїть не в ній)
            public AnimationClip Insert;        // вставка, що грає зараз (null — ні)
            public int Loop;                    // меж петлі за весь матч — монотонно (хеш вибору не повторюється між стояннями)
            public int LastInsertLoop = -100;
            public string LastClip;
            public int SeenLoops;               // обертів поточного відтворення бази вже враховано
            public float PrevTotal;
        }

        private readonly Func<RingActor, string, AnimationClip> _clip;
        private readonly Dictionary<RingActor, State> _st = new Dictionary<RingActor, State>();
        private readonly HashSet<string> _missing = new HashSet<string>();

        /// <summary>Скільки вставок зіграно за запуск (для огляду й QA).</summary>
        public int Inserts { get; private set; }

        /// <param name="clip">Пошук кліпа для постаті з урахуванням статі тіла, напр. <c>(a, n) =&gt; stage.Clip(n, stage.IsFemale(a))</c>.</param>
        public RingIdleLife(Func<RingActor, string, AnimationClip> clip)
        {
            _clip = clip;
        }

        /// <summary>Ім'я вставки, яку актор грає зараз, або null.</summary>
        public string Playing(RingActor a)
        {
            State s;
            return a != null && _st.TryGetValue(a, out s) && s.Insert != null ? s.Insert.name : null;
        }

        /// <summary>Забути актора (постать знищено чи замінено).</summary>
        public void Forget(RingActor a)
        {
            if (a != null) _st.Remove(a);
        }

        /// <summary>
        /// Кадр живого очікування для одного актора; кликати після <c>RingActor.Tick</c>.
        /// </summary>
        /// <param name="actorKey">Стала на матч: гравець 0, суперник 1, рефері 2 (зерно хешу — суперники не синхронні).</param>
        /// <param name="gimmick">Індекс ґіміка кар'єри (<c>Gimmick</c>) або −1 — ваги жестів.</param>
        /// <param name="window">Секунд реального часу до наступного біту режисера для цього актора;
        /// <see cref="float.PositiveInfinity"/> — чекає гравця.</param>
        /// <param name="allowed">Режисер дозволяє (false — кіно, камерний план, лежить, больовий, за рингом).</param>
        /// <param name="speed">Швидкість, з якою сцена грає базову петлю ЦЬОГО актора — темп сцени × <c>RingIdlePhase.Speed</c>
        /// його ролі (вставка грає з тією самою; <c>RingActor</c> свого темпу назовні не дає, тож його передає власник сцени —
        /// інше число дасть вставку в іншому темпі, ніж петля).</param>
        public void Tick(RingActor a, int actorKey, IdleLifeRole role, int gimmick, float window, bool allowed, float speed = 1f)
        {
            if (a == null) return;
            State s;
            if (!_st.TryGetValue(a, out s)) { s = new State(); _st[a] = s; }
            var cur = a.CurrentClip;
            bool ok = allowed && Enabled;
            float sp = speed > 0.01f ? speed : 1f;
            float win = window * sp;   // вікно в секундах часу кліпа

            if (s.Insert != null)
            {
                if (cur != s.Insert || s.Base == null) { s.Insert = null; s.Base = null; return; }   // режисер грає своє
                if (a.Finished)
                {
                    a.PlayAtBlend(s.Base, 0f, true, a.PosePos, a.PoseYaw, Fade, sp);
                    Enter(s, a);
                    s.Insert = null;
                    return;
                }
                float left = s.Insert.length - a.ClipTime;
                if (!ok || !(win >= left))
                {
                    a.PlayAtBlend(s.Base, Wrap(a.ClipTime, s.Base.length), true, a.PosePos, a.PoseYaw, AbortFade, sp);
                    Enter(s, a);
                    s.Insert = null;
                }
                return;
            }

            if (cur == null || cur.name != RingIdleLifePlan.LoopClip(role)) { s.Base = null; return; }
            float total = a.ClipTimeTotal;
            if (s.Base != cur || total < s.PrevTotal - 0.05f)
            {
                // Нове стояння (чи режисер перезапустив петлю): перша вставка — не раніше другої межі, без «жесту з порога»
                // (на першій межі проміжок 1 — менший за найменший дозволений 2; роль із зсувом фази доходить до неї за пів петлі).
                s.Base = cur;
                Enter(s, a);
                s.LastInsertLoop = s.Loop;
                return;
            }
            s.PrevTotal = total;
            int loops = (int)Math.Floor(total / Math.Max(0.01f, cur.length));
            if (loops <= s.SeenLoops) return;
            s.SeenLoops = loops;
            s.Loop++;
            string pick = RingIdleLifePlan.Pick(actorKey, role, gimmick, s.Loop, s.LastInsertLoop, s.LastClip, win, ok);
            if (pick == null) return;
            var clip = Find(a, pick);
            if (clip == null) return;
            // Модель рахує вікно за номінальною довжиною петлі; тут — за справжньою довжиною кліпа вставки, щоб вона не
            // почалась і не обірвалась наступним кадром (смик), якщо FBX колись розійдеться з номіналом.
            if (!(win >= clip.length - a.ClipTime + RingIdleLifePlan.Margin * sp)) return;
            a.PlayAtBlend(clip, a.ClipTime, false, a.PosePos, a.PoseYaw, Fade, sp);
            s.Insert = clip;
            s.LastInsertLoop = s.Loop;
            s.LastClip = pick;
            Inserts++;
        }

        private static void Enter(State s, RingActor a)
        {
            var c = a.CurrentClip;
            s.Base = c;
            float total = a.ClipTimeTotal;
            s.SeenLoops = c != null ? (int)Math.Floor(total / Math.Max(0.01f, c.length)) : 0;
            s.PrevTotal = total;
        }

        /// <summary>Час у межах довжини петлі (0 ≤ результат &lt; length).</summary>
        private static float Wrap(float t, float length)
        {
            if (!(length > 0f)) return 0f;
            float r = t - (float)Math.Floor(t / length) * length;
            return r < 0f ? 0f : r >= length ? 0f : r;
        }

        private AnimationClip Find(RingActor a, string name)
        {
            if (_missing.Contains(name) || _clip == null) return null;
            var c = _clip(a, name);
            if (c == null)
            {
                _missing.Add(name);
                Debug.LogWarning("[Ринг] живе очікування: немає кліпа " + name + " — вставку пропущено (FBX ще без ring_anims_idle?)");
            }
            return c;
        }
    }
}
