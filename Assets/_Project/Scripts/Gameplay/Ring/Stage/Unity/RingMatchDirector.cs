using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Ring;
using Game.Core.Ring.Match;
using Game.Gameplay.Ring.Models;
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
        // Ажіотаж на мить показаної події: рушій рахує наперед, а натовп має жити тим, що на екрані.
        private int _hype;

        // ============================ QTE (Ф3: «лише видовище») ============================

        private readonly MatchQteWindow _qte = new MatchQteWindow();
        // Повтор журналу (BeginReplay) і демо без рушія: RegisterFlourish там мутував би чужий рушій — вікна немає.
        private bool _noQte;
        // Огляд (-ring-shots): бот сам тисне QTE посередині вікна — детермінований Hit для кадрів. Звичайна гра — лише людина.
        private static readonly bool AutoQte = RingStage3D.ReadArg("-ring-shots") != null;
        private float _qteOpenT, _qteCloseT;

        /// <summary>QTE-вікно відкрите (для майбутнього вигляду: кільце чи смуга; тут нічого не малюється).</summary>
        public bool QteOpen { get { return _qte.IsOpen; } }

        /// <summary>Скільки вікна лишилось: 1 — щойно відкрилось, 0 — закрите.</summary>
        public float QteRemaining01 { get { return _qte.Remaining01(Time.time); } }

        /// <summary>Вікно відкрилось (мить qte_open, вже зі звуком Prompt).</summary>
        public event Action QteOpened;

        /// <summary>Вікно закрилось: true — натиск влучив (RegisterFlourish викликано), false — пропуск. Скасування (кінець матчу) не повідомляє.</summary>
        public event Action<bool> QteClosed;

        /// <summary>
        /// Натиск гравця (клавіша Confirm чи кнопка геймпада): true — натиск з'їдено вікном (хост не має передавати його далі),
        /// false — вікна немає чи натиск до відкриття. Влучний натиск — <see cref="MatchEngine.RegisterFlourish"/> (+1 ажіотажу,
        /// подія Flourish сама піде в чергу показу й озвучення); кидків це не торкається.
        /// </summary>
        public bool QtePress()
        {
            if (_e == null || _noQte || !_qte.IsActive) return false;
            int seq;
            bool hit = _qte.Press(Time.time, out seq);
            DeliverQte(_qte.Tick(Time.time));
            if (!hit) return false;
            _e.RegisterFlourish(seq);
            var h = QteClosed;
            if (h != null) h(true);
            return true;
        }

        private void DeliverQte(QteTick tk)
        {
            if ((tk & QteTick.Opened) != 0)
            {
                if (_s.Sound != null) _s.Sound.Qte(RingQte.Prompt);
                var o = QteOpened;
                if (o != null) o();
            }
            if ((tk & QteTick.Missed) != 0)
            {
                if (_s.Sound != null) _s.Sound.Qte(RingQte.Miss);
                var c = QteClosed;
                if (c != null) c(false);
            }
        }

        private void UpdateQte()
        {
            if (!_qte.IsActive) return;
            float t = Time.time;
            var tk = _qte.Tick(t);
            DeliverQte(tk);
            if (AutoQte && _qte.IsOpen && t >= (_qteOpenT + _qteCloseT) * 0.5f) QtePress();
        }

        /// <summary>
        /// Завести вікно QTE на мітках кліпа кроку <c>qte_open</c>/<c>qte_close</c> (як <see cref="ArmFollowUp"/>: час кліпа ділиться на
        /// <see cref="RingStage3D.PlaySpeed"/>). Умови: QTE ввімкнено (читається зараз, не кешується), є рушій, подія — прийом гравця, що
        /// дає видовище (<see cref="MatchQteSource.SeqFor"/>), кліп має обидві мітки, а крок стартує не пізніше відкриття. Без міток —
        /// нічого: вікно з'явиться, щойно «Кліпи» дадуть мітку.
        /// </summary>
        private void ArmQte(MatchEvent ev, string clip, float startAt)
        {
            if (_e == null || _noQte || ev == null || clip == null) return;
            int seq = MatchQteSource.SeqFor(_e.State.Log, ev, clip);
            if (seq < 0) return;
            string mb = RingBeats.MarkBase(clip);
            float open = _s.Events.Time(mb, "qte_open");
            float close = _s.Events.Time(mb, "qte_close");
            if (open < 0f || close <= open || open < startAt) return;
            float now = Time.time;
            _qteOpenT = now + (open - startAt) / RingStage3D.PlaySpeed;
            _qteCloseT = now + (close - startAt) / RingStage3D.PlaySpeed;
            _qte.Open(seq, _qteOpenT, _qteCloseT);
        }

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
            _qte.Cancel();
            _noQte = false;
            _walkCo.Clear();
            _running = false;
            _ended = false;
            _afterCut = false;
            StandIns = 0;
            RunInStarts = 0;
            OffCenter = 0;
            _pinSound = null;
            _held = null;
            _lastMove = null;
            _chainClip[0] = _chainClip[1] = null;
            _chainInfo = null;
            _countT0 = -1f;
            _q.Clear();
            _e = e;
            _seen = e != null ? e.State.Log.Count : 0;
            _hype = e != null ? e.State.Hype : 0;
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

        /// <summary>
        /// Повтор зіграного рушія (демо, <see cref="RingDemoScript"/>): режисер програє журнал від події <paramref name="fromSeq"/>
        /// (<see cref="DemoMatch.StartCount"/> — після стартового обміну, як живий матч), а кінець бере з
        /// <see cref="MatchEngine.Result"/> — переможець той, кого назвало ядро. Ажіотаж, пози, ушкодження й місце пари
        /// (ринг чи за рингом) — ті, що лишили події до <paramref name="fromSeq"/>: дзеркало пройде їх так само, як у живому
        /// матчі, тож повтор із середини журналу стартує з тієї ж розстановки (рецензія 204ba0c).
        /// </summary>
        public void BeginReplay(MatchEngine e, int fromSeq)
        {
            if (e == null) throw new ArgumentNullException("e");
            Begin(null);
            _e = e;
            _noQte = true;   // повтор не чіпає рушія: RegisterFlourish там мутував би журнал, який уже зіграно
            var log = e.State.Log;
            _seen = Mathf.Clamp(fromSeq, 0, log.Count);
            _hype = Mathf.Clamp(e.Setup.StartHype, 0, MatchRules.MaxHype);
            for (int i = 0; i < _seen; i++)
            {
                _hype = NextHype(_hype, log[i]);
                Apply(log[i]);
            }
            if (_seen > 0) PlaceCanonical();
            _s.Reveal(_seen - 1);
            _s.CrowdMood(_hype);
            if (_s.Sound != null && _seen <= 1) _s.Sound.BellStart();
        }

        private static int NextHype(int hype, MatchEvent ev)
        {
            for (int i = 0; i < ev.Effects.Count; i++)
                if (ev.Effects[i].Kind == MatchEffectKind.Hype) hype += ev.Effects[i].Amount;
            return Mathf.Clamp(hype, 0, MatchRules.MaxHype);
        }

        public void EndMatch()
        {
            StopAllCoroutines();
            _qte.Cancel();   // без звуку «пропуск»: матч закрито, вікно просто гасне
            _walkCo.Clear();
            _running = false;
            _q.Clear();
            _e = null;
            _s.Reveal(int.MaxValue);
        }

        public void Enqueue(MatchEvent ev) { _q.Enqueue(ev); }

        private void Update()
        {
            UpdateQte();
            if (_e != null)
            {
                var log = _e.State.Log;
                while (_seen < log.Count) _q.Enqueue(log[_seen++]);
            }
            if (!_running && _q.Count > 0 && !_ended) StartCoroutine(Run());
        }

        // ============================ охоронець «у центрі» ============================

        /// <summary>
        /// Таз борця на рингу — не далі цього від центру рингу по горизонталі, м (план М1, Ф2 крок 8; правило власника
        /// 30.10.2026 «усе в центрі», рішення 08.11.2026 — бігу по рингу немає). Поза правилом — склейка на канаті чи куті
        /// (план 1), склейка місця пари, кадр перемоги і пара за рингом. Лежачий і знесений кліпом рахуються: це вада кліпа
        /// (таз ≤ 0,5 м від старту — CLIP_NEEDS.md), а не режисера; власні розстановки режисера — у <see cref="SettleRadius"/>.
        /// </summary>
        public const float CenterLimit = 1.5f;

        /// <summary>Скільки разів за матч таз борця вийшов за <see cref="CenterLimit"/> (раз на борця за подію) — критерій М1 «0».</summary>
        public int OffCenter { get; private set; }

        private bool _guardOff;
        private int _offCenterSeen;   // біти борців, уже позначених у поточній події: 1 — гравець, 2 — суперник

        private void LateUpdate()
        {
            if (!_running || _ended || _guardOff || _m[0].Outside || _s == null) return;
            for (int k = 0; k < 2; k++)
            {
                int bit = 1 << k;
                if ((_offCenterSeen & bit) != 0) continue;
                var side = k == 0 ? MatchSide.Player : MatchSide.Rival;
                var a = _s.Actor(side);
                if (a == null) continue;
                Vector3 p = a.PosePos;
                float d = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                if (d <= CenterLimit) continue;
                _offCenterSeen |= bit;
                OffCenter++;
                var c = a.CurrentClip;
                Debug.LogWarning(string.Format(CultureInfo.InvariantCulture, "[Ринг] таз поза центром: {0} {1:0.00} м > {2:0.0} (кліп {3}; подія {4} {5}; за матч {6})",
                    side, d, CenterLimit, c != null ? c.name : "—", _ev != null ? _ev.Kind.ToString() : "—", _ev != null ? _ev.Action.ToString() : "—", OffCenter));
            }
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
                _s.Place(_s.Referee, rp, RingStage3D.YawTo(rp, RingStage3D.OutsideAnchor), RefIdle(true));
                OutsideStillShot();
            }
            else _s.Place(_s.Referee, RingStage3D.RefPos, 180f, "Ref_Idle_Loop");
        }

        /// <summary>Склейка кадру на план <paramref name="cam"/> — не наїзд (<see cref="RingStage3D.CutTo"/>, «Изучение» e0d50c2).</summary>
        private void HardCut(StageCam cam)
        {
            _outHave = false;   // наступний кадр «за рингом» — новим курсом і склейкою
            _s.CutTo(cam);
        }

        // ============================ черга ============================

        // ============================ такт ============================

        private MatchEvent _ev;
        private BeatPlan _plan;
        private bool _beatFired;

        /// <summary>Подія утримання (Pin), чий звук чекає контакту накриття в наступній події Kickout.</summary>
        private MatchEvent _pinSound;

        /// <summary>Звук утримання, що чекає: на контакті накриття (<paramref name="delay"/>, с показу) або одразу.</summary>
        private void FlushPinSound(float delay)
        {
            if (_pinSound == null) return;
            if (_s.Sound != null) _s.Sound.OnEvent(_pinSound, delay, false);
            _pinSound = null;
        }

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
        private void ArmBeat(int idx, float startAt = 0f, StageStep st = null)
        {
            if (idx != _plan.Step || _beatFired || _ev == null) return;
            if (_plan.IsCount)
            {
                // Відлік крока утримання запускає сам рефері, коли став на коліно й почав рахувати (RefCount).
                if (st != null && st.Count) return;
                StartCoroutine(CountBeats(_ev, _plan));
            }
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

        /// <summary>
        /// Утримання: «раз» і «два» — на долоні рефері (мітки count_1/count_2 його кліпа), розв'язка — на kickout чи count_3.
        /// 10+ вирвався на «раз» — «два» немає (Count2 &lt; 0); третій ляпас — лише справжнє «три».
        /// </summary>
        private IEnumerator CountBeats(MatchEvent ev, BeatPlan p)
        {
            float ps = RingStage3D.PlaySpeed;
            yield return new WaitForSeconds(Mathf.Max(0f, p.Count1) / ps);
            _s.Beat(ev, 1);
            if (_s.Sound != null) _s.Sound.Slap();
            float last = p.Count1;
            if (p.Count2 >= 0f)
            {
                yield return new WaitForSeconds(Mathf.Max(0f, p.Count2 - p.Count1) / ps);
                _s.Beat(ev, 2);
                if (_s.Sound != null) _s.Sound.Slap();
                last = p.Count2;
            }
            yield return new WaitForSeconds(Mathf.Max(0f, p.Final - last) / ps);
            if (_ev != ev || _beatFired) yield break;
            _beatFired = true;
            _s.Beat(ev, 3);
            // Зал вибухає на розв'язці відліку, а не в кінці кліпа — і на рахунку, розірваному напарником: Rescue буває лише
            // слідом за ним (MatchEngine.ResolvePin), тож вибух (фанати + звук) — тут, на самому розриві. Другий Pop події Rescue
            // (RingSoundMap Rescue → Pop) — питання зоні звуку (знахідка 5а, 09.11.2026), не прибирати вибух звідси.
            _s.CrowdPop();
            if (ev.Tier == RollTier.Miss) { if (p.Three && _s.Sound != null) _s.Sound.Slap(); }
            else if (ev.Perk != PerkCatalog.BribedRef)
            {
                // Вирвався: поп залу, спалах і тремтіння — на самому кікауті. Підкуп — рефері зупинився на «два», плеча вгору
                // немає (кліпа кікауту немає): лише поп залу, без удару.
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
            if (_s.Sound != null && ev.Kind != MatchEventKind.Kickout && ev.Kind != MatchEventKind.MatchEnd)
            {
                // Утримання (дія Pin) кроків не має: накриття показує наступна подія — Kickout, кліп Cover. Звук накриття чекає
                // контакту Cover (звірка зі звуком, п. 2), а не звучить до кліпа.
                if (ev.Action == MatchActionId.Pin && _plan.Step < 0) _pinSound = ev;
                else _s.Sound.OnEvent(ev, 0f, false);
            }
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

        // ============================ ланцюг кліпів і поза, що тримається ============================

        /// <summary>
        /// Кліп, що кожен борець грав останнім кроком пари (чи стрибком у просторі пари): ланцюг (<see cref="StageStep.Chain"/>)
        /// продовжує слот лише того, хто досі в ньому.
        /// </summary>
        private readonly AnimationClip[] _chainClip = new AnimationClip[2];
        /// <summary>Маніфест останньої пари ланцюга: наступна пара з іншим зсувом (стрибок → утримання) переставляє A на різницю.</summary>
        private RingClipInfo _chainInfo;
        /// <summary>Поза, яку пара тримає між подіями (<see cref="RingClipTable.HeldByNode"/>: накриття, больовий), і хто тримає.</summary>
        private string _held;
        private MatchSide _heldBy;
        /// <summary>Остання показана дія з кидком (крім самого утримання): чи накриття — кульмінація.</summary>
        private MatchEvent _lastMove;
        /// <summary>Остання зіграна пара події (база й нападник): з її кінцевого вузла видно, чи пара лишилась у позі.</summary>
        private string _lastPairBase;
        private MatchSide _lastPairActor;
        /// <summary>Кроки поточної події: крок утримання дивиться на наступний (мітка кікауту борців).</summary>
        private List<StageStep> _steps;
        /// <summary>Суперник упав на плані 1 склейки (<see cref="StageStep.DropVictim"/>): у цій події лежить, хоч ядро ще не застосоване.</summary>
        private readonly bool[] _dropped = new bool[2];
        /// <summary>Мить старту кліпа відліку рефері (Time.time), −1 — ще не рахує; і сам кліп (обраний ступенем ядра).</summary>
        private float _countT0 = -1f;
        private string _countClip;
        /// <summary>Рефері від плечей захисника на відліку, м (ПЛЕЙСХОЛДЕР до кадрів: долоня рефері лягає на мат біля плечей).</summary>
        private const float CountReach = 0.62f;
        /// <summary>Склейка з мітки: план 1 починається за стільки до неї, с часу кліпа, і тримається стільки після <see cref="StageStep.To"/>.</summary>
        private const float CutLead = 0.12f, CutTail = 0.10f;

        /// <summary>Ядро чи склейка кладуть борця в цій події.</summary>
        private bool IsDown(MatchSide s) { return _m[I(s)].Posture == Posture.Down || _dropped[I(s)]; }

        /// <summary>Кінцевий вузол поточного кліпа постаті (маніфест), null — невідомо.</summary>
        private string NodeOf(RingActor a)
        {
            var c = a != null ? a.CurrentClip : null;
            var info = c != null ? _s.Info(c.name) : null;
            return info != null ? info.EndNode : null;
        }

        /// <summary>
        /// Кроки події з контекстом показу (<see cref="PlanContext"/>). Пара в позі, яку подія не продовжує, — спершу відпускає
        /// (<see cref="RingClipTable.Release"/>); подія без кроків (QTE, обмін) позу не перериває — хіба що кінець ходу чи матчу.
        /// </summary>
        private List<StageStep> PlanEvent(MatchEvent ev)
        {
            MatchSide b = RingClipTable.Opp(ev.Actor);
            var A = _s.Actor(ev.Actor);
            var B = _s.Actor(b);
            var ctx = new PlanContext
            {
                Held = _held,
                HeldBy = _heldBy,
                LastMove = _lastMove,
                Next = _q.Count > 0 ? _q.Peek() : null,
                VictimDown = _m[I(b)].Posture == Posture.Down || (B != null && Lying(B)),
                ActorNode = NodeOf(A),
                VictimNode = NodeOf(B)
            };
            List<StageStep> steps;
            if (_held != null && !RingClipTable.Continues(ev, _held, _heldBy))
            {
                var free = ctx;
                free.Held = null;
                steps = RingClipTable.Plan(ev, free);
                if (steps.Count > 0 || ev.Kind == MatchEventKind.TurnEnd || ev.Kind == MatchEventKind.MatchEnd)
                {
                    steps.InsertRange(0, RingClipTable.Release(_held, _heldBy));
                    _held = null;
                }
            }
            else steps = RingClipTable.Plan(ev, ctx);
            if (ev.Kind == MatchEventKind.Action && ev.HasRoll && ev.Action != MatchActionId.Pin) _lastMove = ev;
            return steps;
        }

        private IEnumerator Run()
        {
            _running = true;
            while (_q.Count > 0)
            {
                var ev = _q.Dequeue();
                var steps = PlanEvent(ev);
                // Для огляду й фаз-тесту: яка подія зараз на сцені (контекст для рядків «телепорт», «крізь канати»).
                Debug.Log("[Ринг] 3D подія: " + ev.Kind + " " + ev.Action + " " + ev.Tier + " — " + ev.Actor);
                bool applied = false;
                _ev = ev;
                _beatFired = false;
                _offCenterSeen = 0;
                _guardOff = false;
                _steps = steps;
                _lastPairBase = null;
                _dropped[0] = _dropped[1] = false;
                _countT0 = -1f;
                _countClip = null;
                _plan = RingBeats.For(steps, _s.Events, ClipLen);
                int si = RingClipTable.StandIns(steps);
                if (si > 0)
                {
                    StandIns += si;
                    Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] підстановок кліпів: +{0} (за матч {1}) — {2} {3} {4}",
                        si, StandIns, ev.Kind, ev.Action, ev.MoveKey ?? "—"));
                }
                // Звук утримання не губиться: якщо слідом не кікаут (накриття не показане) — одразу, до звуку цієї події.
                if (ev.Kind != MatchEventKind.Kickout) FlushPinSound(0f);
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
                if (ev.Kind == MatchEventKind.Kickout) FlushPinSound(0f);   // кліпа накриття не було — звук утримання наприкінці
                _s.Reveal(ev.Seq);
                if (!applied) Apply(ev);
                // Пара лишилась у позі (накриття після стрибка, больовий до «терпіти»): наступна подія її продовжить або відпустить.
                if (_lastPairBase != null)
                {
                    var li = _s.Info(_lastPairBase + "_A");
                    _held = RingClipTable.HeldByNode(li != null ? li.EndNode : null);
                    _heldBy = _lastPairActor;
                }
                bool held = _held != null;
                // Стан ядра = поза в кадрі: «лежить» без падіння — продаж падінням; «стоїть», а лежить — встає. Пара в позі — не чіпати.
                if (ev.Kind != MatchEventKind.MatchEnd && !held) yield return MatchPoses(ev);
                // Натовп живе за ажіотажем показаної події (не за рушієм, що рахує наперед); кікаут і кінець матчу — вибух залу.
                _hype = NextHype(_hype, ev);
                _s.CrowdMood(_hype);
                if (ev.Kind == MatchEventKind.MatchEnd) _s.CrowdPop();
                if (ev.Kind == MatchEventKind.MatchEnd) { _ended = true; break; }
                _afterCut = false;
                if (!held && (steps.Count > 0 || ev.Kind == MatchEventKind.TurnEnd)) yield return Settle();
            }
            _running = false;
            // Пара за рингом лишається в кадрі «за рингом»: загальний план дивиться на ринг.
            if (!_ended && !_m[0].Outside) _s.SetShot(StageCam.Overview);
        }

        // ============================ кроки ============================

        private IEnumerator RunStep(StageStep st, MatchEvent ev, int idx)
        {
            // Поза охоронцем центру: план «на канаті / в куті» (Cut), склейка місця пари (Place: лежачий ковзає з-за рингу) і кадр
            // перемоги (End: розстановка під камеру).
            _guardOff = st.Kind == StageStepKind.Cut || st.Kind == StageStepKind.Place || st.Kind == StageStepKind.End;
            // Утримання — низька камера біля мату (рахунок «раз — два»), а не загальний кіноплан.
            // Склейки (Cut, Place) ставлять кадр самі — в ту саму мить, що й стрибок тазу. За рингом кадр веде Cine пари.
            // Ланцюг (накриття → утримання → кікаут, больовий, приземлення) — у тому самому кадрі: камера міняється лише на
            // першому кроці події.
            if (st.Kind != StageStepKind.Cut && st.Kind != StageStepKind.Place && !_m[0].Outside && !(st.Chain && idx > 0))
            {
                var cam = ev.Kind == MatchEventKind.Kickout ? StageCam.Low : st.Cam;
                // Після склейки приземлення (пара чи промах сам) — теж склейкою кадру, навіть якщо план склейки не вмістився
                // і камера лишилась у низькому плані (тоді SetShot не склеює). Пара, що заходить склейкою чи за коренями, — так само.
                if ((st.Kind == StageStepKind.Pair || st.Kind == StageStepKind.Solo) && (_afterCut || st.CutIn || st.ByRoots)) HardCut(cam);
                else _s.SetShot(cam);
            }
            // За рингом кадр веде Cine пари (парний крок — сам, OutsidePairShot); одинак, що заходить склейкою (підйом із живота),
            // — склейкою кадру «за рингом», а не переворотом у кадрі, що пливе.
            else if (_m[0].Outside && st.Kind == StageStepKind.Solo && (st.CutIn || st.ByRoots)) OutsideNowShot();
            switch (st.Kind)
            {
                case StageStepKind.Pair: yield return RunPair(st, ev, idx); break;
                case StageStepKind.Solo: yield return RunSolo(st, idx); break;
                case StageStepKind.Cut: yield return RunCut(st, idx); break;
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

        /// <summary>Таз кліпа на миті <paramref name="t"/> у світі, якщо корінь кліпа (слот) — у <paramref name="slot"/> з курсом <paramref name="slotYaw"/>.</summary>
        private static Vector3 SlotPoint(RingActor a, AnimationClip c, float t, Vector3 slot, float slotYaw)
        {
            Vector3 h;
            float hy;
            a.SampleLocal(c, t, out h, out hy);
            Vector3 p = slot + Rot(slotYaw, new Vector3(h.x, 0f, h.z));
            p.y = slot.y;
            return p;
        }

        /// <summary>
        /// Кліп з миті <paramref name="t"/> у заданому слоті (корінь кліпа — у <paramref name="slot"/> з курсом
        /// <paramref name="slotYaw"/>): ланцюг кліпів одного запису йде далі з того самого кореня, без перестановки за тазом.
        /// </summary>
        private static void PlayInSlot(RingActor a, AnimationClip c, float t, Vector3 slot, float slotYaw, bool loop)
        {
            Vector3 h;
            float hy;
            a.SampleLocal(c, t, out h, out hy);
            Vector3 p = slot + Rot(slotYaw, new Vector3(h.x, 0f, h.z));
            a.PlayAtTime(c, t, loop, p, slotYaw + hy, RingStage3D.PlaySpeed);
        }


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
        private IEnumerator RunCut(StageStep st, int idx)
        {
            var a = _s.Actor(st.Actor);
            var vs = RingClipTable.Opp(st.Actor);
            var v = _s.Actor(vs);
            string name = st.Clip;
            var c = a != null ? _s.Clip(name, _s.IsFemale(a)) : null;
            if (a != null && c == null)
            {
                // Кліпа плану немає в наборі сцени — найближчий план «на канаті/куті» (не тиша й не стрибок).
                name = RingClipTable.DefaultPerch(st.Perch);
                c = _s.Clip(name, _s.IsFemale(a));
                Debug.LogWarning("[Ринг] 3D: немає кліпа склейки " + st.Clip + " — грається " + name);
            }
            if (a == null || c == null)
            {
                Debug.LogWarning("[Ринг] 3D: немає кліпа склейки " + st.Clip);
                yield break;
            }
            bool own = name == st.Clip;   // мітки From/To/Impact — лише свого кліпа
            float t0 = 0f, hit = -1f;
            if (own && st.From != null)
            {
                hit = RingBeats.MarkTime(_s.Events, name, st.From);
                if (hit > 0f) t0 = Mathf.Max(0f, hit - CutLead);
            }
            // Суперник стоїть у центрі на своєму місці пари (хто лежить — лишається, де лежить); падає тут, якщо план просить.
            Vector3 vPos = RingStage3D.PairPos(vs, false);
            if (!IsDown(vs))
            {
                Vector3 dv = v.PosePos - vPos;
                dv.y = 0f;
                if (dv.magnitude > 0.15f) _s.Place(v, vPos, RingStage3D.PairYaw(vs, false), IdleName(vs) ?? "Ring_Stance_Loop");
                var bump = st.DropVictim ? _s.Clip("Bump_Back") : null;
                if (bump != null)
                {
                    StopWalk(v);
                    v.PlayAt(bump, false, vPos, RingStage3D.PairYaw(vs, false), RingStage3D.PlaySpeed);
                    _dropped[I(vs)] = true;
                }
            }
            else
            {
                vPos = v.PosePos;
                vPos.y = RingStage3D.MatY;
                // Лежить обличчям донизу, а приземлення — на спину: кліпа перевороту немає, тож під склейкою кадру тіло стає в
                // кінцеву позу падіння на спину на тому самому місці (рецензія Ф2 кроків 5 і 7).
                var back = st.DropVictim && NodeOf(v) == StageStep.LyingFront ? _s.Clip("Bump_Back") : null;
                if (back != null)
                {
                    StopWalk(v);
                    v.PlayAtTime(back, Mathf.Max(0f, back.length - 1f / 60f), false, vPos, v.PoseYaw, RingStage3D.PlaySpeed);
                }
            }

            // Слот кліпа: стрибок із каната — корінь на лінії каната обличчям у ринг; удар об кут з мітки — таз на миті удару
            // на точці «на куті», хід тазу перед ударом — у кут; решта — таз першого кадру на точці склейки (PerchPos).
            Vector3 slot, at;
            float slotYaw;
            bool corner = st.Perch == MoveStaging.Corner;
            if (own && name == "Springboard_Perch")
            {
                float sx = st.Actor == MatchSide.Player ? -1f : 1f;
                slot = new Vector3(sx * RingStage3D.Venue.RopeLine, RingStage3D.MatY, 0f);
                slotYaw = RingStage3D.YawTo(slot, new Vector3(0f, RingStage3D.MatY, 0f));
                at = SlotPoint(a, c, t0, slot, slotYaw);
            }
            else if (corner && hit > 0f)
            {
                float pyaw;
                Vector3 perch = _s.PerchPos(st.Actor, MoveStaging.Corner, "Move_Corner_Exit", out pyaw);
                Vector3 hHit, hPre;
                float hyHit, hyPre;
                a.SampleLocal(c, hit, out hHit, out hyHit);
                a.SampleLocal(c, Mathf.Max(0f, hit - 0.3f), out hPre, out hyPre);
                Vector3 d = hHit - hPre;
                d.y = 0f;
                slotYaw = d.sqrMagnitude > 1e-4f ? pyaw + 180f - YawOf(d) : pyaw - hyHit;   // pyaw — обличчям до центру, кут — навпаки
                slot = perch - Rot(slotYaw, new Vector3(hHit.x, 0f, hHit.z));
                slot.y = RingStage3D.MatY;
                at = perch;
            }
            else
            {
                float yaw;
                Vector3 p0 = _s.PerchPos(st.Actor, st.Perch, name, out yaw);
                Vector3 h0;
                float hy0;
                a.SampleLocal(c, 0f, out h0, out hy0);
                slotYaw = yaw - hy0;
                slot = p0 - Rot(slotYaw, new Vector3(h0.x, 0f, h0.z));
                slot.y = p0.y;
                at = t0 > 0f ? SlotPoint(a, c, t0, slot, slotYaw) : p0;
            }
            at.y = slot.y;
            StopWalk(a);
            a.Slot.position = slot;
            a.SetHeight(slot.y);
            PlayInSlot(a, c, t0, slot, slotYaw, false);
            _s.Watch.Forget(a);
            _s.Watch.Forget(v);
            float hold = Mathf.Min(c.length - t0, 1.7f);
            if (own && st.To != null)
            {
                float te = RingBeats.MarkTime(_s.Events, name, st.To);
                if (te > t0) hold = Mathf.Min(te - t0 + CutTail, c.length - t0);
            }
            hold = Mathf.Max(hold, 0.3f);
            if (st.Impact && own) ArmBeat(idx, t0, st);
            if (_s.Sound != null)
            {
                // Звук склейки (звірка зі звуком, п. 4; EVENT_SOUND.md): канат — RopeRebound на мітці rope_hit кліпа (спина в
                // канаті); кут — CornerHit у мить, коли нападник спирається на стійку: мітка corner_hit / rope_lean, а кліп кута
                // без мітки (Move_Corner_Exit) стартує вже спертим — одразу. Мітка поза показаним відрізком — теж одразу.
                float t = RingBeats.MarkTime(_s.Events, name, corner ? "corner_hit" : "rope_hit");
                if (t < 0f && corner) t = _s.Events.Time(name, "rope_lean");
                float delay = t >= t0 && t - t0 < hold ? (t - t0) / RingStage3D.PlaySpeed : 0f;
                _s.Sound.Play(corner ? RingCue.CornerHit : RingCue.RopeRebound, 0.85f, delay);
            }
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
            yield return new WaitForSeconds(hold / RingStage3D.PlaySpeed);
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
            _s.Place(R, rp, RingStage3D.YawTo(rp, mid), RefIdle(o));
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
            _outHave = false;
            if (!OutsideCam(pts, true)) { Debug.LogWarning("[Ринг] 3D: план «за рингом» не вмістився"); HardCut(StageCam.Big); }
        }

        /// <summary>
        /// Кадр «за рингом» там, де пара стоїть чи лежить зараз, — склейкою: поза міняється під склейкою (крок <see cref="StageStep.CutIn"/>,
        /// підйом із живота), а не на очах у кадрі, що пливе.
        /// </summary>
        private void OutsideNowShot()
        {
            var pts = new List<Vector3>();
            foreach (var side in new[] { MatchSide.Player, MatchSide.Rival })
            {
                Vector3 g = _s.Actor(side).PosePos;
                g.y = RingStage3D.OutsideAnchor.y;
                RingCine.Body(pts, g, _s.BodyScale(_s.Actor(side)));
            }
            AddApron(pts);
            if (!OutsideCam(pts, true)) { Debug.LogWarning("[Ринг] 3D: план «за рингом» (склейка) не вмістився"); HardCut(StageCam.Big); }
        }

        // План «за рингом» тримається (огляд 8e «Изучение», 09.11.2026: камера пари перескакувала між кутами на кожному кроці й
        // пливла крізь натовп — gym і hall 015, arena 136 у 8b). Наступний кадр спершу пробує той самий курс і нахил; новий курс —
        // лише якщо старий не вміщає пару, і тоді склейкою, якщо камера переходить за кут (різниця курсу більша за OutsideCutYaw).
        // Курс (бік, звідки дивиться камера) тримається довше за нахил і кут огляду: спершу той самий нахил, далі будь-який
        // нахил плану «за рингом», кут огляду — до найширшого. Новий курс — лише якщо старий не вміщає пару ніяк; хід прийому
        // в кадрі важливіший за відсутність склейки (рецензія ПМ 09.11.2026: склейка — на початку кроку, між прийомами).
        private bool _outHave;
        private float _outYaw, _outPitch, _outFov;
        private const float OutsideCutYaw = 25f;
        // Ті самі межі, що в RingCine.OutsideShot (e0d50c2): нахил до 40°, кут огляду до 64° — запасні.
        private const float OutsideMaxFov = 64f;
        private static readonly float[] OutsideKeepPitch = { 14f, 20f, 26f, 32f, 40f };

        /// <summary>
        /// Кадр «за рингом» з гістерезисом: <paramref name="pts"/> — у кадрі, камера — у коридорі довкола рингу
        /// (<see cref="RingVenue.OutsideCameraOk"/>). <paramref name="cut"/> — склейкою (інакше камера доганяє плавно, а
        /// перехід за кут — однаково склейкою). Нічого не ставить і повертає false, якщо пара не вміщається.
        /// Той самий курс береться, лише якщо кадр чистий за правилами <see cref="RingCine.OutsideShot"/> («Изучение», e0d50c2):
        /// кожне тіло видно не крізь ринг і над огорожею, кутова стійка не на передньому плані, рефері на фартуху не обрізаний.
        /// Інакше — новий курс від <see cref="RingCine.OutsideShot"/> (той сам пише в журнал, якщо кадр із поступкою).
        /// </summary>
        private bool OutsideCam(List<Vector3> pts, bool cut)
        {
            var venue = RingStage3D.Venue;
            Vector3 c = RingStage3D.OutsideAnchor;
            Func<Vector3, bool> ok = p =>
            {
                if (!venue.OutsideCameraOk(p.x, p.y, p.z, c.x, c.z)) return false;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector3 q = pts[i];
                    if (Mathf.Max(Mathf.Abs(q.x), Mathf.Abs(q.z)) <= venue.RingSightHalf) continue;
                    if (!venue.SeesOutside(p.x, p.y, p.z, q.x, q.y, q.z)) return false;
                }
                return true;
            };
            var shot = default(RingCine.Shot);
            if (_outHave)
            {
                var fovs = new[] { _outFov, Mathf.Min(_outFov + 6f, OutsideMaxFov), Mathf.Min(_outFov + 12f, OutsideMaxFov), OutsideMaxFov };
                for (int i = -1; i < OutsideKeepPitch.Length && !shot.Ok; i++)
                {
                    float pt = i < 0 ? _outPitch : OutsideKeepPitch[i];
                    foreach (float fov in fovs)
                    {
                        var s = RingCine.Frame(pts, _outYaw, pt, fov, _s.CamAspect, 0.2f, 0.86f, 0.08f, ok, 1.2f, 7f);
                        s.Fov = fov;
                        if (s.Ok && OutsideClean(s, c)) { shot = s; break; }
                    }
                }
            }
            if (!shot.Ok) shot = RingCine.OutsideShot(pts, c, RingStage3D.OutsideOut, _s.CamAspect);
            if (!shot.Ok) return false;
            Vector3 f = shot.Look - shot.Pos;
            float yaw = YawOf(f);
            float pitch = Mathf.Asin(Mathf.Clamp(-f.y / Mathf.Max(1e-4f, f.magnitude), -1f, 1f)) * Mathf.Rad2Deg;
            bool corner = _outHave && Mathf.Abs(Mathf.DeltaAngle(yaw, _outYaw)) > OutsideCutYaw;
            _s.Cine(shot.Pos, shot.Look, shot.Fov, cut || !_outHave || corner);
            _outHave = true;
            _outYaw = yaw;
            _outPitch = pitch;
            _outFov = shot.Fov;
            return true;
        }

        /// <summary>
        /// М'які вимоги кадру «за рингом» — як у <see cref="RingCine.OutsideShot"/>: кутова стійка не ближче за пару в полі зору
        /// (<see cref="RingVenue.PostInView"/>) і рефері на фартуху або весь у кадрі, або поза ним (<see cref="RingVenue.FigureCut"/>).
        /// </summary>
        private bool OutsideClean(RingCine.Shot s, Vector3 pairCenter)
        {
            var venue = RingStage3D.Venue;
            float aspect = _s.CamAspect;
            Vector3 f = s.Look - s.Pos;
            float halfH = Mathf.Atan(Mathf.Tan(0.5f * s.Fov * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
            float dist = new Vector2(pairCenter.x - s.Pos.x, pairCenter.z - s.Pos.z).magnitude;
            if (venue.PostInView(s.Pos.x, s.Pos.z, f.x, f.z, halfH, dist)) return false;
            Vector3 rf = RingStage3D.OutsideRefPos;
            return !RingVenue.FigureCut(s.Pos.x, s.Pos.y, s.Pos.z, s.Look.x, s.Look.y, s.Look.z, s.Fov, aspect,
                rf.x, rf.y, rf.z, RingVenue.RefHeight, RingVenue.RefBody);
        }

        /// <summary>
        /// Тіло на ході кліпа — третина, дві третини, кінець від <paramref name="tFrom"/>: таз зі шляху кліпа на цьому тілі
        /// (<see cref="Path"/>, система першої миті кліпа з курсом <paramref name="yaw0"/>), перенесений від
        /// <paramref name="start"/> — де таз на <paramref name="tFrom"/>. Прийом із зсувом пари (ларіат — 2,2 м) не виходить із
        /// кадру «за рингом» (огляд 8e).
        /// </summary>
        private void PathBodies(List<Vector3> into, RingActor a, AnimationClip c, Vector3 start, float yaw0, float tFrom)
        {
            if (c == null) return;
            float[] t; Vector3[] p; float[] y;
            Path(a, c, c.length - 0.02f, out t, out p, out y);
            int k = 0;
            while (k < t.Length - 1 && t[k] < tFrom) k++;
            float scale = _s.BodyScale(a);
            for (int s = 1; s <= 3; s++)
            {
                int i = k + Mathf.RoundToInt((t.Length - 1 - k) * s / 3f);
                Vector3 w = start + Rot(yaw0, new Vector3(p[i].x - p[k].x, 0f, p[i].z - p[k].z));
                w.y = start.y;
                RingCine.Body(into, w, scale);
            }
        }

        /// <summary>
        /// Місце рефері, поки пара за рингом: на фартуху навпроти пари, за канатами (<see cref="RingStage3D.OutsideRefPos"/>,
        /// «Изучение» e0d50c2). Раніше режисер ставив його на підлогу за 0,6 м від пари — тіло з боді-слему проходило крізь
        /// рефері (hall 018, власник 09.11.2026: «Ці колізії типу ок?» — ні).
        /// </summary>
        private static Vector3 OutsideRefPos() { return RingStage3D.OutsideRefPos; }

        /// <summary>
        /// Спокій рефері: за рингом — рахує відлік з фартуха (<c>Ref_Count_Outside_Loop</c>, як у огляді «Изучение»), на рингу —
        /// <c>Ref_Idle_Loop</c>.
        /// </summary>
        private static string RefIdle(bool outside) { return outside ? "Ref_Count_Outside_Loop" : "Ref_Idle_Loop"; }

        /// <summary>Край фартуха навпроти пари — у кадрі «за рингом»: видно, що пара поза рингом.</summary>
        private static void AddApron(List<Vector3> pts)
        {
            Vector3 apron = RingStage3D.OutsideAnchor - RingStage3D.OutsideOut * RingVenue.PairOut;
            apron.y = RingStage3D.MatY;
            pts.Add(apron);
        }

        /// <summary>
        /// Кадр парного кроку за рингом: обидва борці (старт і кінець зсуву пари), увесь хід кліпів (<paramref name="path"/>) і
        /// край фартуха; кадр тримає курс (<see cref="OutsideCam"/>). Якщо весь хід не вміщається — старт і контакт.
        /// <paramref name="cut"/> — пара стала на місця склейкою (за коренями, <see cref="StageStep.CutIn"/>): і кадр склейкою.
        /// </summary>
        private void OutsidePairShot(RingActor A, RingActor B, Vector3 pAStart, Vector3 pB, Vector3 pA, List<Vector3> path, bool cut)
        {
            var pts = new List<Vector3>();
            RingCine.Body(pts, pAStart, _s.BodyScale(A));
            RingCine.Body(pts, pB, _s.BodyScale(B));
            RingCine.Body(pts, pA, _s.BodyScale(A));
            AddApron(pts);
            if (path != null && path.Count > 0)
            {
                var all = new List<Vector3>(pts);
                all.AddRange(path);
                if (OutsideCam(all, cut)) return;
            }
            if (OutsideCam(pts, cut)) return;
            Debug.LogWarning("[Ринг] 3D: план «за рингом» (пара) не вмістився");
            _outHave = false;
            if (cut) HardCut(StageCam.Big); else _s.SetShot(StageCam.Big);
        }

        private IEnumerator RunSolo(StageStep st, int idx)
        {
            if (st.AnchorPair != null) { yield return RunAnchored(st, idx); yield break; }
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
            else
            {
                a.PlayHere(clip, false, RingStage3D.PlaySpeed);
                if (st.CutIn) _s.Watch.Forget(a);   // поза міняється під склейкою кадру (RunStep): не телепорт
            }
            ArmBeat(idx);
            if (st.Actor == _ev?.Actor) ArmFollowUp(_ev, st.Clip, 0f);
            ArmQte(_ev, st.Clip, 0f);
            float len = clip.length / RingStage3D.PlaySpeed;
            if (st.Once) len = Mathf.Min(len, 1.7f);
            yield return new WaitForSeconds(len);
        }

        private IEnumerator RunRef(StageStep st, int idx)
        {
            var r = _s.Referee;
            var clip = _s.Clip(st.RefClip);
            if (clip == null) yield break;
            // Рефері навколішки після відліку (напарник розірвав рахунок) — спершу встає.
            var up = !Upright(r) ? _s.Clip("Ref_Kneel_Up") : null;
            if (up != null)
            {
                r.PlayHere(up, false, RingStage3D.PlaySpeed);
                yield return new WaitForSeconds(up.length / RingStage3D.PlaySpeed);
            }
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


        /// <summary>
        /// Зсув пари — з жіночого маніфесту (<see cref="RingStage3D.Info(string, bool)"/>, «Изучение» e0d50c2), лише коли обоє
        /// борців — жінки: жіночий набір знято на двох жіночих тілах. Мішана пара (рідкісна — суперник своєї статі за
        /// замовчуванням, P2-6) — чоловічий маніфест, як досі; рух жіночого набору (f_motion) поки не читаємо.
        /// </summary>
        private bool PairFemale(MatchSide a)
        {
            return _s.IsFemale(_s.Actor(a)) && _s.IsFemale(_s.Actor(RingClipTable.Opp(a)));
        }

        private static string BaseOf(string pairClip)
        {
            return pairClip.EndsWith("_A") || pairClip.EndsWith("_B") ? pairClip.Substring(0, pairClip.Length - 2) : pairClip;
        }

        private IEnumerator RunPair(StageStep st, MatchEvent ev, int idx)
        {
            bool fem = PairFemale(st.Actor);
            var ia = _s.Info(st.Clip + "_A", fem);
            var ib = _s.Info(st.Clip + "_B", fem);
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
            // Ланцюг одного запису (накриття → утримання → кікаут, больовий, приземлення): далі з того самого кореня.
            bool liveA = _chainClip[I(sa)] != null && A.CurrentClip == _chainClip[I(sa)];
            bool liveB = _chainClip[I(sb)] != null && B.CurrentClip == _chainClip[I(sb)];
            if (st.Chain && (liveA || liveB)) { yield return RunChain(st, ev, idx, ia, ib, ca, cb, liveA, liveB); yield break; }
            if (st.ByRoots) { yield return RunPairRoots(st, ev, idx, ia, ib, ca, cb); yield break; }
            bool bDown = IsDown(sb);
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
            // Утримання: рефері йде до плечей захисника й стає на коліно сам (RefCount) — паралельно з підходом нападника.
            if (st.Count) StartCoroutine(RefCount(ev, B, pA, st.RefClip));
            bool hard = fromCut || st.CutIn;
            if (hard)
            {
                // Склейка: нападник стає в точку старту разом із PlayAtTime нижче, решта — жорстко в цей самий кадр.
                StopWalk(A); StopWalk(B);
                if (!st.Count) StopWalk(refActor);
                // Висота слота склейки (верх кута — з підйомом) не лишається на приземленні: PlayAtTime висоти не міняє.
                A.SetHeight(pAStart.y);
                if (!bDown && (B.PosePos - pB).sqrMagnitude > 0.0025f) _s.Place(B, pB, yawB, IdleName(sb) ?? "Ring_Stance_Loop");
                if (!st.Count && (refActor.PosePos - pRef).sqrMagnitude > 0.0025f) _s.Place(refActor, pRef, yawRef, RefIdle(outside));
                _s.Watch.Forget(A); _s.Watch.Forget(B); _s.Watch.Forget(refActor);
            }
            else
            {
                // Перехід ланцюга — коротким кроком у стійці (0,15–0,25 с), а не ходою; ходять лише ті, кому далеко.
                var moves = new List<SlideItem> { new SlideItem(A, pAStart, yawAStart), new SlideItem(B, pB, yawB) };
                if (!st.Count) moves.Add(new SlideItem(refActor, pRef, yawRef));
                yield return Transit(moves.ToArray());
            }

            // Фінішер — склейкою в кіноплан до старту кліпа: обидва в кадрі від колін, контакт у кадрі (огляд 07.11.2026:
            // такт приходив, поки камера ще летіла з загального плану).
            if (finisherShot) FinisherShot(A, B, ca, pB, yawA, ia);
            else if (outside)
            {
                var path = new List<Vector3>();
                PathBodies(path, A, ca, pAStart, yawA, tStart);
                PathBodies(path, B, cb, pB, yawB, tStart);
                OutsidePairShot(A, B, pAStart, pB, pA, path, hard);
            }

            // Старт: слот у таз і новий кліп — в тому самому кадрі, обидва разом (з розбігом — посеред бігу).
            A.PlayAtTime(ca, tStart, ia.Loop, pAStart, yawAStart, RingStage3D.PlaySpeed);
            B.PlayAtTime(cb, tStart, ib != null && ib.Loop, pB, yawB, RingStage3D.PlaySpeed);
            yield return PairTail(st, ev, idx, ca, cb, ia, tStart);
        }

        /// <summary>
        /// Спільний кінець парного кроку: ланцюг запам'ятовує кліпи, рефері грає свій кліп (крім відліку — його веде
        /// <see cref="RefCount"/>), такт, звук накриття на контакті, і скільки крок триває: до мітки відліку
        /// (<see cref="StageStep.UntilCount"/>), до мітки свого кліпа (<see cref="StageStep.StopAt"/>), петля —
        /// <see cref="StageStep.Dur"/>, інакше кліп цілком.
        /// </summary>
        private IEnumerator PairTail(StageStep st, MatchEvent ev, int idx, AnimationClip ca, AnimationClip cb, RingClipInfo ia, float tStart)
        {
            MatchSide sa = st.Actor, sb = RingClipTable.Opp(sa);
            _chainClip[I(sa)] = ca;
            _chainClip[I(sb)] = cb;
            _chainInfo = ia;
            _lastPairBase = st.Clip;
            _lastPairActor = sa;
            if (!st.Count && !string.IsNullOrEmpty(st.RefClip))
            {
                var rc = _s.Clip(st.RefClip);
                var ri = _s.Info(st.RefClip);
                if (rc != null) _s.Referee.PlayHere(rc, ri != null && ri.Loop, RingStage3D.PlaySpeed);
            }
            ArmBeat(idx, tStart, st);
            if (ev != null && sa == ev.Actor) ArmFollowUp(ev, st.Clip, tStart);
            ArmTaps(ev, st.Clip, tStart);
            ArmQte(ev, st.Clip, tStart);
            if (_pinSound != null && ev != null && ev.Kind == MatchEventKind.Kickout)
            {
                // Звук накриття — на контакті Cover (груди на груди: мітка contact або виміряна мить кліпа).
                float tc;
                BeatSource srcC;
                bool has = RingBeats.ImpactMark(st.Clip, _s.Events, out tc, out srcC) && tc >= tStart;
                FlushPinSound(has ? (tc - tStart) / RingStage3D.PlaySpeed : 0f);
            }
            if (st.UntilCount != null)
            {
                // Утримання триває, поки рефері не дорахує до потрібної мітки: кікаут борців — рівно на мітці kickout рефері.
                var next = _steps != null && idx + 1 < _steps.Count ? _steps[idx + 1] : null;
                float guard = Time.time + 14f;
                while (Time.time < guard)
                {
                    if (_countT0 >= 0f)
                    {
                        float until = RingClipTable.CountUntil(st, next, _countClip, _s.Events);
                        if (until < 0f || Time.time >= _countT0 + until / RingStage3D.PlaySpeed) break;
                    }
                    yield return null;
                }
                yield break;
            }
            float len = Mathf.Max(ca.length, cb.length) - tStart;
            if (st.StopAt != null)
            {
                float m = _s.Events.Time(st.Clip, st.StopAt);
                if (m > tStart) len = m - tStart;
            }
            else if (st.Dur > 0f) len = st.Dur;
            yield return new WaitForSeconds(Mathf.Max(0f, len) / RingStage3D.PlaySpeed);
        }

        /// <summary>
        /// Парний крок ланцюга: хто досі в кліпі попереднього кроку — лишається в його корені (той самий запис, вузол на
        /// стику однаковий); другого ставить зсув маніфесту від нього. Обидва в ланцюгу, а зсув пари інший (стрибок із
        /// каната → утримання) — A переставляється на різницю зсувів, B лишається. Переходів, ходи й зміни кадру немає.
        /// </summary>
        private IEnumerator RunChain(StageStep st, MatchEvent ev, int idx, RingClipInfo ia, RingClipInfo ib, AnimationClip ca, AnimationClip cb,
            bool liveA, bool liveB)
        {
            MatchSide sa = st.Actor, sb = RingClipTable.Opp(sa);
            var A = _s.Actor(sa);
            var B = _s.Actor(sb);
            _afterCut = false;
            Vector3 slotA = A.Slot.position, slotB = B.Slot.position;
            float rotA = A.Slot.eulerAngles.y, rotB = B.Slot.eulerAngles.y;
            Vector3 off = new Vector3(ia.Offset.x, 0f, ia.Offset.z);
            if (liveA && liveB)
            {
                if (_chainInfo != null)
                {
                    Vector3 d = new Vector3(_chainInfo.Offset.x, 0f, _chainInfo.Offset.z) - off;
                    if (d.sqrMagnitude > 1e-6f) slotA += Rot(rotA, d);
                }
            }
            else if (liveA)
            {
                rotB = rotA - ia.Rotation;
                slotB = slotA + Rot(rotA, off);
                slotB.y = B.Slot.position.y;
            }
            else
            {
                rotA = rotB + ia.Rotation;
                slotA = slotB - Rot(rotA, off);
                slotA.y = A.Slot.position.y;
            }
            StopWalk(A);
            StopWalk(B);
            PlayInSlot(A, ca, 0f, slotA, rotA, ia.Loop);
            PlayInSlot(B, cb, 0f, slotB, rotB, ib != null && ib.Loop);
            if (Trace)
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Ринг] ланцюг: {0} (A {1}, B {2})", st.Clip, liveA ? "у слоті" : "від B", liveB ? "у слоті" : "від A"));
            if (st.Count) StartCoroutine(RefCount(ev, B, A.PosePos, st.RefClip));
            yield return PairTail(st, ev, idx, ca, cb, ia, 0f);
        }

        /// <summary>
        /// Пара за коренями кліпів (<see cref="StageStep.ByRoots"/>: зустрічний після каната, таран, стрибок із каната, вхід
        /// больового на лікоть): корінь B — на місці B (лежачого — за тазом, з його позою), корінь A — від кореня B за зсувом
        /// маніфесту; пара — у центрі й у межах канатів. Завжди жорстко, склейкою кадру (<see cref="RunStep"/>).
        /// </summary>
        private IEnumerator RunPairRoots(StageStep st, MatchEvent ev, int idx, RingClipInfo ia, RingClipInfo ib, AnimationClip ca, AnimationClip cb)
        {
            MatchSide sa = st.Actor, sb = RingClipTable.Opp(sa);
            var A = _s.Actor(sa);
            var B = _s.Actor(sb);
            bool bDown = IsDown(sb);
            _afterCut = false;
            if (bDown)
            {
                float gd = Time.time + 1f;
                while (Time.time < gd && B.CurrentClip != null && !B.Finished && !(_s.Info(B.CurrentClip.name)?.Loop ?? false)) yield return null;
            }
            bool outside = _m[I(sb)].Outside;
            Vector3 hub = outside ? RingStage3D.OutsideAnchor : new Vector3(0f, RingStage3D.MatY, 0f);
            Vector3 slotA, slotB;
            float rotA, rotB;
            if (bDown) RootPlaceDown(B, cb, ia, out slotA, out rotA, out slotB, out rotB);
            else
            {
                Vector3 cA = Canon(sa), cB = Canon(sb);
                rotA = RingStage3D.YawTo(cA, cB);
                if ((cB - cA).sqrMagnitude < 0.01f) rotA = sa == MatchSide.Player ? 90f : -90f;
                rotB = rotA - ia.Rotation;
                slotB = cB;
                slotA = cB - Rot(rotA, new Vector3(ia.Offset.x, 0f, ia.Offset.z));
                Vector3 d = hub - (slotA + slotB) * 0.5f;
                d.y = 0f;
                slotA += d;
                slotB += d;
            }
            slotA.y = slotB.y = hub.y;
            if (!outside)
            {
                Vector3 sh = PathInsideSlots(A, ca, slotA, rotA, B, cb, slotB, rotB);
                slotA += sh;
                slotB += sh;
            }
            float tc;
            BeatSource src;
            if (!RingBeats.ImpactMark(st.Clip, _s.Events, out tc, out src)) tc = ca.length * 0.4f;
            Vector3 pA0 = SlotPoint(A, ca, 0f, slotA, rotA), pB0 = SlotPoint(B, cb, 0f, slotB, rotB);
            Vector3 pAc = SlotPoint(A, ca, tc, slotA, rotA), pBc = SlotPoint(B, cb, tc, slotB, rotB);
            Vector3 mid = (pAc + pBc) * 0.5f;
            var R = _s.Referee;
            Vector3 pRef = R.PosePos;
            if (outside) pRef = OutsideRefPos();
            else if (!(RefOk(pRef, pA0, pB0) && RefOk(pRef, pAc, pBc))) pRef = RefSpot(pAc, pBc);
            bool finisherShot = IsFinisherHit(ev) && idx == _plan.Step && !outside;
            if (finisherShot)
            {
                _finYaw = FinisherYaw(rotA, R.PosePos, mid);
                pRef = mid + Rot(_finYaw, Vector3.forward) * 1.15f + Rot(_finYaw, Vector3.right) * 0.9f;
                pRef.x = Mathf.Clamp(pRef.x, -1.75f, 1.75f);
                pRef.z = Mathf.Clamp(pRef.z, -1.75f, 1.75f);
                pRef.y = RingStage3D.MatY;
            }
            StopWalk(A);
            StopWalk(B);
            A.SetHeight(slotA.y);
            if (!st.Count)
            {
                StopWalk(R);
                if ((R.PosePos - pRef).sqrMagnitude > 0.0025f) _s.Place(R, pRef, RingStage3D.YawTo(pRef, outside ? RingStage3D.OutsideAnchor : mid), RefIdle(outside));
            }
            _s.Watch.Forget(A); _s.Watch.Forget(B); _s.Watch.Forget(R);
            if (finisherShot) FinisherFrame(A, B, pAc, pBc);
            else if (outside)
            {
                // Пара за коренями стає на місця жорстко — і кадр «за рингом» склейкою; у кадрі — увесь хід обох.
                var path = new List<Vector3>();
                foreach (float f in new[] { 0.33f, 0.66f, 1f })
                {
                    RingCine.Body(path, SlotPoint(A, ca, Mathf.Min(ca.length * f, ca.length - 0.02f), slotA, rotA), _s.BodyScale(A));
                    RingCine.Body(path, SlotPoint(B, cb, Mathf.Min(cb.length * f, cb.length - 0.02f), slotB, rotB), _s.BodyScale(B));
                }
                OutsidePairShot(A, B, pA0, pB0, pAc, path, true);
            }
            PlayInSlot(A, ca, 0f, slotA, rotA, ia.Loop);
            PlayInSlot(B, cb, 0f, slotB, rotB, ib != null && ib.Loop);
            if (st.Count) StartCoroutine(RefCount(ev, B, pAc, st.RefClip));
            yield return PairTail(st, ev, idx, ca, cb, ia, 0f);
        }

        /// <summary>
        /// Корені пари, якщо B лежить: корінь B — так, щоб таз першого кадру кліпа B був там, де B лежить зараз, з тим самим
        /// курсом пози; корінь A — від кореня B за зсувом і поворотом маніфесту (зсув — B відносно A в просторі A).
        /// </summary>
        private static void RootPlaceDown(RingActor B, AnimationClip cb, RingClipInfo ia, out Vector3 slotA, out float rotA, out Vector3 slotB, out float rotB)
        {
            Vector3 hb;
            float hyb;
            B.SampleLocal(cb, 0f, out hb, out hyb);
            rotB = B.PoseYaw - hyb;
            slotB = B.PosePos - Rot(rotB, new Vector3(hb.x, 0f, hb.z));
            slotB.y = RingStage3D.MatY;
            rotA = rotB + ia.Rotation;
            slotA = slotB - Rot(rotA, new Vector3(ia.Offset.x, 0f, ia.Offset.z));
            slotA.y = RingStage3D.MatY;
        }

        /// <summary>Те саме, що <see cref="PathInside"/>, для пари за коренями: увесь шлях тазів обох — у межах канатів мінус 0,25 м.</summary>
        private Vector3 PathInsideSlots(RingActor A, AnimationClip ca, Vector3 slotA, float rotA, RingActor B, AnimationClip cb, Vector3 slotB, float rotB)
        {
            float lim = RopeLine - 0.25f;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            float[] t; Vector3[] p; float[] y;
            for (int k = 0; k < 2; k++)
            {
                var who = k == 0 ? A : B;
                var c = k == 0 ? ca : cb;
                Vector3 slot = k == 0 ? slotA : slotB;
                float rot = k == 0 ? rotA : rotB;
                Path(who, c, c.length - 0.02f, out t, out p, out y, true);
                for (int i = 0; i < t.Length; i++)
                {
                    Vector3 w = slot + Rot(rot, new Vector3(p[i].x, 0f, p[i].z));
                    minX = Mathf.Min(minX, w.x); maxX = Mathf.Max(maxX, w.x); minZ = Mathf.Min(minZ, w.z); maxZ = Mathf.Max(maxZ, w.z);
                }
            }
            Vector3 sh = Vector3.zero;
            if (maxX - minX <= 2f * lim) { if (maxX > lim) sh.x = lim - maxX; else if (minX < -lim) sh.x = -lim - minX; }
            if (maxZ - minZ <= 2f * lim) { if (maxZ > lim) sh.z = lim - maxZ; else if (minZ < -lim) sh.z = -lim - minZ; }
            return sh;
        }

        /// <summary>
        /// Стрибок у просторі пари приземлення (<see cref="StageStep.AnchorPair"/>: «Метеор», «Ластівка»): корені пари
        /// приземлення — від лежачого B, стрибок грається в корені A цієї пари (кінцева поза стрибка — перша поза
        /// приземлення), рефері — убік, якщо заважає. Фінішер — кіноплан приземлення одразу, до відриву. Наступний крок —
        /// саме приземлення ланцюгом (<see cref="RunChain"/>: A у слоті, B — від A за зсувом, тобто там, де лежить).
        /// </summary>
        private IEnumerator RunAnchored(StageStep st, int idx)
        {
            _afterCut = false;
            MatchSide sa = st.Actor, sb = RingClipTable.Opp(sa);
            var A = _s.Actor(sa);
            var B = _s.Actor(sb);
            var ia = _s.Info(st.AnchorPair + "_A", PairFemale(sa));
            var caL = _s.Clip(st.AnchorPair + "_A");
            var cbL = _s.Clip(st.AnchorPair + "_B");
            var air = _s.Clip(st.Clip);
            if (A == null || B == null || ia == null || caL == null || cbL == null || air == null)
            {
                Debug.LogWarning("[Ринг] 3D: немає кліпа стрибка " + st.Clip + " / " + st.AnchorPair);
                yield break;
            }
            // B долежує падіння з плану 1 (коротко: камера вже склеєна).
            float gd = Time.time + 0.5f;
            while (Time.time < gd && B.CurrentClip != null && !B.Finished && !(_s.Info(B.CurrentClip.name)?.Loop ?? false)) yield return null;
            Vector3 slotA, slotB;
            float rotA, rotB;
            RootPlaceDown(B, cbL, ia, out slotA, out rotA, out slotB, out rotB);
            float tc;
            BeatSource src;
            if (!RingBeats.ImpactMark(st.AnchorPair, _s.Events, out tc, out src)) tc = 0.4f;
            Vector3 pAc = SlotPoint(A, caL, tc, slotA, rotA), pB = B.PosePos;
            pB.y = RingStage3D.MatY;
            var R = _s.Referee;
            StopWalk(A);
            StopWalk(B);
            A.SetHeight(slotA.y);
            Vector3 mid = (pAc + pB) * 0.5f;
            bool finisherShot = IsFinisherHit(_ev) && _plan.Step == idx + 1 && !_m[0].Outside;
            Vector3 pRef = R.PosePos;
            if (finisherShot)
            {
                _finYaw = FinisherYaw(rotA, R.PosePos, mid);
                pRef = mid + Rot(_finYaw, Vector3.forward) * 1.15f + Rot(_finYaw, Vector3.right) * 0.9f;
                pRef.x = Mathf.Clamp(pRef.x, -1.75f, 1.75f);
                pRef.z = Mathf.Clamp(pRef.z, -1.75f, 1.75f);
                pRef.y = RingStage3D.MatY;
            }
            else if (!RefOk(pRef, pAc, pB)) pRef = RefSpot(pAc, pB);
            if ((R.PosePos - pRef).sqrMagnitude > 0.0025f)
            {
                StopWalk(R);
                _s.Place(R, pRef, RingStage3D.YawTo(pRef, mid), "Ref_Idle_Loop");
            }
            _s.Watch.Forget(A); _s.Watch.Forget(B); _s.Watch.Forget(R);
            if (finisherShot) FinisherFrame(A, B, pAc, pB);
            PlayInSlot(A, air, 0f, slotA, rotA, false);
            // Лише A — у ланцюгу: B стане від A за зсувом приземлення, а не лишиться в корені кліпа падіння.
            _chainClip[I(sa)] = air;
            _chainClip[I(sb)] = null;
            _chainInfo = ia;
            ArmBeat(idx, 0f, st);
            yield return new WaitForSeconds(air.length / RingStage3D.PlaySpeed);
        }

        /// <summary>
        /// Відлік утримання (<see cref="StageStep.Count"/>): рефері йде до плечей захисника — з боку, протилежного нападникові,
        /// на <see cref="CountReach"/> від них, — стає на коліно (<c>Ref_Kneel_Down</c>) і рахує кліпом, обраним за ступенем
        /// ядра. Звідси ж стартує відлік такту (<see cref="CountBeats"/>), і крок утримання чекає саме його (<see cref="_countT0"/>).
        /// </summary>
        private IEnumerator RefCount(MatchEvent ev, RingActor B, Vector3 pA, string countClip)
        {
            var R = _s.Referee;
            _countClip = countClip;
            var cc = _s.Clip(countClip);
            if (R == null || B == null || cc == null)
            {
                _countT0 = Time.time;
                yield break;
            }
            Vector3 hip = B.PosePos, head = B.HeadPos;
            hip.y = head.y = RingStage3D.MatY;
            Vector3 h = head - hip;
            if (h.sqrMagnitude < 0.01f) h = Rot(B.PoseYaw, Vector3.forward);
            h.Normalize();
            Vector3 sh = head - h * 0.2f;
            Vector3 n = new Vector3(-h.z, 0f, h.x);
            Vector3 toA = pA - sh;
            toA.y = 0f;
            if (Vector3.Dot(toA, n) > 0f) n = -n;
            Vector3 spot = sh + n * CountReach;
            float lim = RingStage3D.RingHalf - 0.3f;
            spot = new Vector3(Mathf.Clamp(spot.x, -lim, lim), RingStage3D.MatY, Mathf.Clamp(spot.z, -lim, lim));
            float yaw = RingStage3D.YawTo(spot, sh);
            yield return Walk(new[] { new SlideItem(R, spot, yaw) });
            var down = _s.Clip("Ref_Kneel_Down");
            if (down != null && Upright(R))
            {
                R.PlayAt(down, false, R.PosePos, yaw, RingStage3D.PlaySpeed);
                yield return new WaitForSeconds(down.length / RingStage3D.PlaySpeed);
            }
            if (_ev != ev) yield break;
            R.PlayAt(cc, false, R.PosePos, yaw, RingStage3D.PlaySpeed);
            _countT0 = Time.time;
            if (Trace) Debug.Log("[Ринг] відлік: " + countClip + " (" + ev.Tier + ")");
            if (_plan.IsCount && !_beatFired) StartCoroutine(CountBeats(ev, _plan));
        }

        /// <summary>Мітка другого дотику предмета в кліпі (пропозиція ПМ «Кліпам», <c>CLIP_NEEDS.md</c> §5).</summary>
        public const string FollowUpMark = "touch_2";

        /// <summary>
        /// Другий дотик тієї самої дії (звірка зі звуком, п. 5; <c>EVENT_SOUND.md</c>): який звук і чи він є — вирішує зона звуку
        /// (<see cref="RingSoundMap.FollowUpOf(MatchEvent)"/>; зараз — предмет 7–9 і ≤ 6). Режисер дає лише мить: мітку
        /// <see cref="FollowUpMark"/> кліпа кроку нападника. Без мітки — тиша, а не звук не в такт (підстановки «за рингом» міток
        /// не мають). Так другий дотик стрибка з каната (падіння в Springboard_Land_Catch / _Counter, знахідка звуку 1) озветься,
        /// щойно звук дасть йому звук, а «Кліпи» — мітку, без правки тут.
        /// </summary>
        private void ArmFollowUp(MatchEvent ev, string clip, float startAt)
        {
            if (_s.Sound == null || ev == null || RingSoundMap.FollowUpOf(ev) == RingCue.None) return;
            float t = _s.Events.Time(RingBeats.MarkBase(clip), FollowUpMark);
            if (t < startAt) return;
            _s.Sound.FollowUp(ev, (t - startAt) / RingStage3D.PlaySpeed);
        }

        /// <summary>
        /// Больовий, провал («терпіти» не вийшло): стукіт по мату — мітки <c>tap</c> кліпа <c>*_Tap</c> (0,48 / 0,80 / 1,12 с). Перший
        /// стук — такт події (<see cref="RingBeats"/>, звук захвату); другий і далі — тихий ляпас по мату на своїх мітках (знахідка
        /// звуку 2, 09.11.2026: звучав лише перший). Крок <c>_Tap</c> — нападника (той, хто тримає), подія — захисника, тож без умови
        /// «крок актора події».
        /// </summary>
        private void ArmTaps(MatchEvent ev, string clip, float startAt)
        {
            if (_s.Sound == null || ev == null || ev.Kind != MatchEventKind.Endure || ev.Tier != RollTier.Miss) return;
            if (clip == null || !clip.EndsWith("_Tap")) return;
            string mb = RingBeats.MarkBase(clip);
            for (int n = 1; n < 8; n++)
            {
                float t = _s.Events.Time(mb, "tap", n);
                if (t < 0f) break;
                if (t < startAt) continue;
                _s.Sound.Play(RingCue.MatSlap, 0.65f, (t - startAt) / RingStage3D.PlaySpeed, true);
            }
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
            FinisherFrame(A, B, aC, pB);
        }

        /// <summary>Кіноплан фінішера за тазом A на миті контакту (<paramref name="aC"/>) і місцем B (<paramref name="pB"/>).</summary>
        private void FinisherFrame(RingActor A, RingActor B, Vector3 aC, Vector3 pB)
        {
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
            // Зсув рефері від переможця — за статтю переможця (жіночий маніфест: убік 0,640 проти 0,750 м).
            var ia = _s.Info("Ref_Raise_Hand_A", win != MatchWinner.Draw
                && _s.IsFemale(_s.Actor(win == MatchWinner.Player ? MatchSide.Player : MatchSide.Rival)));
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
                // Ядро каже «стоїть», а постать на колінах (кікаут, стрибок, що не вдався, вихід із больового) — встає з колін.
                else if (m.Posture != Posture.Down && NodeOf(a) == "kneel_up") clip = _s.Clip(RingClipTable.KneelUp);
                if (clip == null) continue;
                // Підйом зі спини, а борець лежить обличчям донизу: кліпа підйому з живота немає — склейкою кадру, не переворотом
                // на очах (рецензія Ф2 кроків 5 і 7; CLIP_NEEDS §9).
                bool flip = clip.name == "GetUp_Back" && NodeOf(a) == StageStep.LyingFront;
                if (flip) { if (_m[0].Outside) OutsideNowShot(); else HardCut(StageCam.Overview); }
                a.PlayHere(clip, false, RingStage3D.PlaySpeed);
                if (flip) _s.Watch.Forget(a);
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
                // Пара — усередину кола охоронця центру (зсув обох разом, відстань між ними та сама: 2,2 м < 2 · SettleRadius).
                for (int it = 0; it < 3; it++)
                {
                    Vector3 sh = Vector3.zero;
                    foreach (var q in new[] { pp, pr })
                    {
                        float dq = Mathf.Sqrt(q.x * q.x + q.z * q.z);
                        if (dq <= SettleRadius) continue;
                        var over = new Vector3(q.x, 0f, q.z) * ((dq - SettleRadius) / dq);
                        if (over.sqrMagnitude > sh.sqrMagnitude) sh = over;
                    }
                    if (sh.sqrMagnitude < 1e-8f) break;
                    pp -= sh; pr -= sh;
                }
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

        /// <summary>
        /// Де борець стає після події: за рингом — на точці пари, на рингу — там, де стоїть, але не далі
        /// <see cref="SettleRadius"/> від центру (власні розстановки режисера — у межах охоронця центру, з запасом).
        /// </summary>
        private Vector3 SettlePos(MatchSide s)
        {
            if (_m[I(s)].Outside) return RingStage3D.PairPos(s, true);
            Vector3 p = _s.Actor(s).PosePos;
            float d = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            float k = d > SettleRadius ? SettleRadius / d : 1f;
            return new Vector3(p.x * k, RingStage3D.MatY, p.z * k);
        }

        /// <summary>Найдалі від центру, куди режисер сам ставить борця на рингу після події, м (запас 0,15 до <see cref="CenterLimit"/>).</summary>
        private const float SettleRadius = CenterLimit - 0.15f;

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
            if (a == _s.Referee) return _s.Clip(RefIdle(_m[0].Outside));
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
