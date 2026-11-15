using System;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.UI.Menus;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Фінішер як кіно — виконавець у сцені (власник 14.11.2026: «А фінішери це шось кінематографічне»). Що і коли показати,
    /// вирішує чиста модель <see cref="RingFinisherCinePlan"/> (розкадровка docs/ring/FINISHER_CINE.md); тут — лише
    /// застосування: склейки камери через <see cref="RingStage3D.Cine"/>, наїзд, рампа сповільнення й стоп-кадр через
    /// <see cref="RingTime"/>.
    /// <para>Годинник кіно — час кліпа нападника, тож пауза гри зупиняє й кіно, а сповільнення саме себе не розганяє. Кидків і
    /// результату матчу не читає й не змінює; без генератора випадковості.</para>
    /// <para><b>Порядок виклику.</b> Режисер кличе <see cref="Begin"/> там, де досі ставив свій кадр фінішера, — у кадрі старту
    /// кліпа, ДО <c>PlayAtTime</c> / <c>PlayInSlot</c> (місця старту дає в запиті) або одразу після. Перший план стає в тому
    /// самому кадрі. Сцена кличе <see cref="Tick"/> щокадру у своєму <c>LateUpdate</c> після <c>RingActor.Tick</c> і до
    /// розрахунку камери.</para>
    /// <para><b>Хто веде час.</b> Поки кіно йде, темп гри (<see cref="RingTime.Warp"/>) щокадру ставить воно; сповільнення
    /// режисера на цей час вимикається (<c>RingStage3D.TimeWarp</c> не кликати, поки <see cref="Active"/>). Кінець кіно
    /// повертає темп 1.</para>
    /// <para><b>Хто веде камеру.</b> Після кіно влучання камера лишається на плані наслідку, доки режисер не поставить план
    /// наступного кроку — так само, як після його власного кадру фінішера. Промах, пропуск гравцем і кліп, що так і не
    /// почався, повертають камеру одразу (<see cref="Request.CutToWide"/> або загальний план сцени).</para>
    /// <para>Режисер — головний: якщо нападник уже грає інший кліп, кіно зникає й повертає темп. «Менше руху» — без
    /// сповільнення, стоп-кадру й наїздів, плани лишаються; найнижча якість — без строба (прапорці
    /// <see cref="FinisherCineTimeline.Flash"/> / <see cref="FinisherCineTimeline.Strobe"/> /
    /// <see cref="FinisherCineTimeline.Shake"/> читає режисер).</para>
    /// Не <c>MonoBehaviour</c>: живе, доки його тримає сцена; на кінець матчу й знесення сцени власник кличе <see cref="End"/>.
    /// </summary>
    public sealed class RingFinisherCine
    {
        /// <summary>Що режисер знає на старті кліпа фінішера.</summary>
        public struct Request
        {
            public RingActor Attacker, Victim, Referee;
            /// <summary>Кліп нападника кроку, що несе такт: його час — годинник кіно.</summary>
            public AnimationClip Clip;
            /// <summary>Ім'я кліпа для міток камери (<c>cam_call</c> тощо); null — ім'я <see cref="Clip"/>. Половина пари шукається й за базою.</summary>
            public string MarkClip;
            /// <summary>Мить удару в часі кліпа, с (<c>BeatPlan.ClipTime</c>).</summary>
            public float Contact;
            /// <summary>Мить кліпа, з якої режисер його стартує (<c>PlayAtTime</c>), с; зазвичай 0.</summary>
            public float StartTime;
            /// <summary>
            /// Де таз нападника й жертви будуть на старті кліпа, світ. Потрібні, коли <see cref="Begin"/> кличуть до старту кліпа
            /// (борці ще стоять на старих місцях); null — де вони зараз.
            /// </summary>
            public Vector3? AttackerStart, VictimStart;
            /// <summary>Курс нападника на старті кліпа, °; null — поточний.</summary>
            public float? AttackerYaw;
            /// <summary>Місце жертви в мить удару, світ (точка удару на маті — туди дивиться план удару); null — де жертва на мить склейки.</summary>
            public Vector3? ContactVictim;
            /// <summary>Удар відбувся (7+); промах — без удару й наслідку.</summary>
            public bool Hit;
            /// <summary>Той самий фінішер у цьому матчі вже був — без заклику.</summary>
            public bool Repeat;
            /// <summary>Індекс ґіміка нападника (0–7); невідомий — −1 (план наслідку тоді дивиться на жертву, як і за індексу 0).</summary>
            public int Gimmick;
            /// <summary>Режисер ставить рефері в точку поза кадром (null — рефері не чіпати; камера обійде його курсом).</summary>
            public Action<Vector3> PlaceReferee;
            /// <summary>
            /// Режисер повертає свою камеру склейкою: кіно промаху скінчилось, гравець пропустив кіно або кліп так і не почався.
            /// null — загальний план сцени (<see cref="StageCam.Overview"/>).
            /// </summary>
            public Action CutToWide;
            /// <summary>
            /// Режисер ставить свій кадр фінішера, як до кіно: розкадровка є, але жоден план не став (пара біля канатів).
            /// null — камера лишається, як є.
            /// </summary>
            public Action Fallback;
        }

        /// <summary>Журнал планів у консоль (огляд і тури).</summary>
        public static bool Trace;

        /// <summary>Наїзд не довший за стільки метрів — далі камера стоїть.</summary>
        public const float MaxDolly = 0.6f;

        /// <summary>Скільки кадрів чекати, поки режисер почне кліп після <see cref="Begin"/>; далі кіно знімається.</summary>
        public const int MaxWaitFrames = 3;

        private readonly RingStage3D _s;
        private Request _r;
        private FinisherCineTimeline _tl;
        private CapPoint _axis;
        private int _side = 1, _beat = -1, _wait;
        private Vector3 _pos, _look;
        private float _fov, _dolly, _dollied, _freezeLeft, _lastT;
        private bool _axisSet, _seen, _shotOk, _everCut, _fellBack, _froze, _warped;

        public RingFinisherCine(RingStage3D stage)
        {
            _s = stage;
        }

        /// <summary>Кіно йде: звичайна камера, живе очікування й сповільнення режисера не втручаються.</summary>
        public bool Active { get; private set; }

        /// <summary>Розкадровка поточного (чи останнього) фінішера; null — кіно ще не було.</summary>
        public FinisherCineTimeline Timeline { get { return _tl; } }

        /// <summary>Індекс плану, що йде зараз (у <see cref="FinisherCineTimeline.Beats"/>), або −1.</summary>
        public int BeatIndex { get { return Active ? _beat : -1; } }

        /// <summary>Склейка на план: для знімків огляду й звуку (план, порахована камера).</summary>
        public event Action<CineBeat, CineShot> Cut;

        /// <summary>
        /// Почати кіно в кадрі старту кліпа — до <c>PlayAtTime</c> (тоді місця старту — у запиті) або після. false — кіно
        /// не буде: розкадровки немає (немає миті удару чи кліп закороткий) або перший план не став; режисер ставить камеру,
        /// як раніше. true — камера й темп до кінця кіно за ним.
        /// </summary>
        public bool Begin(Request r)
        {
            End();
            if (_s == null || Gone(r.Attacker) || Gone(r.Victim) || r.Clip == null) return false;
            var marks = CineMarks.Read(_s.Events, r.MarkClip ?? r.Clip.name, r.Contact, r.Clip.length);
            var o = new CineOptions
            {
                Hit = r.Hit, Repeat = r.Repeat, Gimmick = r.Gimmick,
                ReduceMotion = RingAccessibility.ReduceMotion,
                Flashes = RingAccessibility.Flashes,
                ScreenShake = RingAccessibility.ScreenShake,
                Cheap = Game.Gameplay.GraphicsTier.IsLow,
            };
            var tl = RingFinisherCinePlan.Build(marks, o);
            if (tl.Empty) return false;
            _r = r; _tl = tl;
            _beat = -1; _wait = 0;
            _froze = false; _freezeLeft = 0f;
            _shotOk = false; _everCut = false; _fellBack = false;
            _seen = r.Attacker.CurrentClip == r.Clip;
            float t0 = _seen ? r.Attacker.ClipTime : Math.Max(0f, r.StartTime);
            _lastT = t0;

            // Вісь дії й бік камер — раз на все кіно, за місцями старту кліпа (правило 1 розкадровки).
            _axisSet = false;
            var sc = Scene(false);
            _axis = RingFinisherCinePlan.Axis(sc);
            _axisSet = true;
            _side = RingFinisherCinePlan.Side(sc);
            if (r.PlaceReferee != null && !Gone(r.Referee)) r.PlaceReferee(V(RingFinisherCinePlan.RefereeSpot(sc, _side)));

            // Перший план — у цьому ж кадрі, склейкою: до старту кліпа камера вже стоїть.
            int k = RingFinisherCinePlan.BeatAt(tl, t0);
            if (k >= 0)
            {
                _beat = k;
                CutTo(tl.Beats[k]);
                if (!_shotOk) { _beat = -1; return false; }           // камері ніде стати: кадр фінішера ставить режисер
            }
            Active = true;
            if (Trace)
                Debug.Log("[Ринг] кіно фінішера: " + r.Clip.name + ", планів " + tl.Beats.Count + ", удар " + tl.Contact.ToString("0.00")
                    + " с, кінець " + tl.End.ToString("0.00") + " с, додає " + RingFinisherCinePlan.ExtraRealSeconds(tl, RingStage3D.PlaySpeed).ToString("0.00") + " с");
            return true;
        }

        /// <summary>Кадр кіно; кликати після <c>RingActor.Tick</c> і до розрахунку камери сцени.</summary>
        public void Tick()
        {
            if (!Active) return;
            var a = _r.Attacker;
            if (Gone(a) || Gone(_r.Victim)) { End(); return; }              // сцену знесено
            if (!_seen)
            {
                if (a.CurrentClip == _r.Clip) _seen = true;
                else
                {
                    if (++_wait > MaxWaitFrames) Abort();                   // режисер так і не почав кліп
                    return;
                }
            }
            else if (a.CurrentClip != _r.Clip) { End(); return; }           // режисер уже грає інше — камера його
            float t = a.ClipTime;
            if (t < _lastT - 0.1f) { End(); return; }                       // той самий кліп почато знову — кіно не продовжуємо
            _lastT = t;
            if (a.Finished || t >= _tl.End)
            {
                // Кліп, що не петляє, завмирає за 0,02 с до свого кінця (RingActor.Tick), тож кінець кіно — ще й Finished.
                bool wide = _tl.CutToWideAtEnd;
                End();
                if (wide) Release();
                return;
            }
            int k = RingFinisherCinePlan.BeatAt(_tl, t);
            if (k >= 0 && k != _beat)
            {
                _beat = k;
                CutTo(_tl.Beats[k]);
                if (!_shotOk && !_everCut && !_fellBack)
                {
                    // Жодного плану кіно ще не було, і цей не став: кадр фінішера ставить режисер (раз).
                    _fellBack = true;
                    var f = _r.Fallback;
                    if (f != null) f();
                }
            }
            else if (k >= 0 && _shotOk && _dolly > 0f && !RingTime.Paused && _dollied < MaxDolly)
            {
                // Наїзд по мату до точки погляду; висота камери не міняється.
                float dx = _look.x - _pos.x, dz = _look.z - _pos.z;
                float len = (float)Math.Sqrt(dx * dx + dz * dz);
                float step = Math.Min(_dolly * Time.unscaledDeltaTime, MaxDolly - _dollied);
                if (len > 1e-3f && step > 0f)
                {
                    _pos = new Vector3(_pos.x + dx / len * step, _pos.y, _pos.z + dz / len * step);
                    _dollied += step;
                    _s.Cine(_pos, _look, _fov, false);
                }
            }

            float speed = RingFinisherCinePlan.Speed(_tl, t);
            if (_tl.Freeze && !_froze && t >= _tl.Contact) { _froze = true; _freezeLeft = RingFinisherCinePlan.FreezeReal; }
            if (_freezeLeft > 0f)
            {
                speed = RingFinisherCinePlan.FreezeSpeed;
                if (!RingTime.Paused) _freezeLeft -= Time.unscaledDeltaTime;
            }
            SetWarp(speed);
        }

        /// <summary>
        /// Гравець пропускає кіно: темп звичайний, камера — режисерові склейкою (перемотати кліп до накриття — справа
        /// режисера).
        /// </summary>
        public void Skip()
        {
            if (!Active) return;
            Abort();
        }

        /// <summary>
        /// Закінчити кіно й повернути темп; камери не чіпає. Безпечно кликати будь-коли: режисер кличе на початку й наприкінці
        /// матчу, сцена — коли її зносять (інакше сповільнення лишилося б у статичному <see cref="RingTime.Warp"/>).
        /// </summary>
        public void End()
        {
            if (_warped)
            {
                RingTime.Warp = 1f;
                Time.timeScale = RingTime.Scale;
                _warped = false;
            }
            Active = false;
            _beat = -1;
        }

        /// <summary>Кіно обірвано до свого кінця: темп звичайний, і якщо камера вже стояла на плані кіно — назад режисерові.</summary>
        private void Abort()
        {
            bool had = _everCut;
            End();
            if (had) Release();
        }

        private void Release()
        {
            var cut = _r.CutToWide;
            if (cut != null) cut();
            else if (_s != null) _s.CutTo(StageCam.Overview);
        }

        private void CutTo(CineBeat beat)
        {
            var shot = RingFinisherCinePlan.Solve(beat, Scene(beat.Kind == CineBeatKind.Impact), _side);
            _shotOk = shot.Ok;
            if (!shot.Ok)
            {
                // Камері ніде стати (дія біля канатів чи за ними): лишається попередній план.
                if (Trace) Debug.Log("[Ринг] кіно фінішера: план «" + beat.Kind + "» не став — лишається попередній");
                return;
            }
            _pos = V(shot.Pos); _look = V(shot.Look); _fov = shot.Fov;
            _dolly = beat.Dolly; _dollied = 0f;
            _s.Cine(_pos, _look, _fov, true);
            _everCut = true;
            if (Trace)
                Debug.Log("[Ринг] кіно фінішера: план «" + beat.Kind + "» на " + beat.ClipTime.ToString("0.00") + " с, курс " + shot.Azimuth.ToString("0")
                    + "°, " + shot.Distance.ToString("0.00") + " м, кут " + shot.Fov.ToString("0") + "°" + (shot.Tight ? ", тісний" : "") + (shot.Blocked ? ", рефері в кадрі" : ""));
            var h = Cut;
            if (h != null) h(beat, shot);
        }

        /// <summary>
        /// Сцена для розкладки плану. Поки кліп не почався, місця й курс — зі старту, який дав режисер (борці ще стоять на
        /// старих місцях); далі — живі. Вісь дії після <see cref="Begin"/> зафіксована. <paramref name="atContact"/> — жертва в
        /// точці удару (план удару).
        /// </summary>
        private CineScene Scene(bool atContact)
        {
            float mat = RingStage3D.MatY;
            var a = _r.Attacker;
            var b = _r.Victim;
            Vector3 pa = a.PosePos, pb = b.PosePos, ha = a.HeadPos, hb = b.HeadPos;
            float yaw = a.PoseYaw;
            if (!_seen)
            {
                if (_r.AttackerStart.HasValue) { var s = _r.AttackerStart.Value; ha = new Vector3(s.x + ha.x - pa.x, ha.y, s.z + ha.z - pa.z); pa = s; }
                if (_r.VictimStart.HasValue) { var s = _r.VictimStart.Value; hb = new Vector3(s.x + hb.x - pb.x, hb.y, s.z + hb.z - pb.z); pb = s; }
                if (_r.AttackerYaw.HasValue) yaw = _r.AttackerYaw.Value;
            }
            if (atContact && _r.ContactVictim.HasValue) pb = _r.ContactVictim.Value;
            var sc = new CineScene
            {
                MatY = mat,
                RingHalf = RingStage3D.RingHalf,
                Attacker = OnMat(pa, mat),
                Victim = OnMat(pb, mat),
                AttackerHead = P(ha),
                VictimHead = P(hb),
                Referee = !Gone(_r.Referee) ? OnMat(_r.Referee.PosePos, mat) : new CapPoint(1e6, mat, 1e6),
                Facing = new CapPoint(Math.Sin(yaw * Math.PI / 180.0), 0.0, Math.Cos(yaw * Math.PI / 180.0)),
            };
            if (_axisSet) { sc.AxisFixed = true; sc.AxisDir = _axis; }
            return sc;
        }

        /// <summary>Поки кіно йде, темп щокадру ставить воно — і коли швидкість 1: чуже сповільнення в кіно не втручається.</summary>
        private void SetWarp(float speed)
        {
            RingTime.Warp = speed;
            Time.timeScale = RingTime.Scale;
            _warped = true;
        }

        /// <summary>Актора немає або його сцену вже знесено (<c>RingActor</c> — звичайний клас, тож дивимось на його слот).</summary>
        private static bool Gone(RingActor a) { return a == null || a.Slot == null; }

        private static CapPoint P(Vector3 v) { return new CapPoint(v.x, v.y, v.z); }
        private static CapPoint OnMat(Vector3 v, float mat) { return new CapPoint(v.x, mat, v.z); }
        private static Vector3 V(CapPoint p) { return new Vector3((float)p.X, (float)p.Y, (float)p.Z); }
    }
}
