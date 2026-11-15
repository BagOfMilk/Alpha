using System;
using System.Collections.Generic;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>Такт розкадровки фінішера (docs/ring/FINISHER_CINE.md §2): кожен — окремий план зі склейкою.</summary>
    public enum CineBeatKind { Call = 0, Grip = 1, Apex = 2, Impact = 3, After = 4 }

    /// <summary>Розмір плану: крупний, середній, низький героїчний, низький біля мату, обличчя.</summary>
    public enum CineSize { Close = 0, Medium = 1, Hero = 2, Mat = 3, Face = 4 }

    /// <summary>На кого дивиться план.</summary>
    public enum CineSubject { Attacker = 0, Victim = 1, Pair = 2 }

    /// <summary>Мітки камери кліпа фінішера, с від початку кліпа; −1 — мітки немає (діє запасний розклад від удару).</summary>
    public struct CineMarks
    {
        public float Call, Grip, Apex, Contact, After, Length;

        /// <summary>Мітки з файлу подій: <c>cam_call</c>, <c>cam_grip</c>, <c>cam_apex</c>, <c>cam_after</c>; мить удару й довжину дає режисер.</summary>
        public static CineMarks Read(RingClipEvents ev, string clip, float contact, float length)
        {
            return new CineMarks
            {
                Call = RingBeats.MarkTime(ev, clip, "cam_call"),
                Grip = RingBeats.MarkTime(ev, clip, "cam_grip"),
                Apex = RingBeats.MarkTime(ev, clip, "cam_apex"),
                After = RingBeats.MarkTime(ev, clip, "cam_after"),
                Contact = contact,
                Length = length,
            };
        }
    }

    /// <summary>Умови показу: результат кидка, повтор у матчі, доступність, дешевий режим, ґімік нападника.</summary>
    public struct CineOptions
    {
        /// <summary>Удар відбувся (7+). Промах (≤ 6) — без вершини, удару й наслідку, без сповільнення.</summary>
        public bool Hit;
        /// <summary>Той самий фінішер у цьому матчі вдруге — без заклику.</summary>
        public bool Repeat;
        /// <summary>«Менше руху»: без сповільнення, стоп-кадру й наїздів; плани й склейки лишаються.</summary>
        public bool ReduceMotion;
        /// <summary>«Спалахи» ввімкнено.</summary>
        public bool Flashes;
        /// <summary>«Тремтіння екрана» ввімкнено.</summary>
        public bool ScreenShake;
        /// <summary>Найнижча якість графіки (Статут PERF-01): без строба світла; спалах удару лишається — він нічого не коштує.</summary>
        public bool Cheap;
        /// <summary>Ґімік нападника: 0 Важковаговик, 1 Літун, 2 Технік, 3 Шоумен, 4 Бувалий, 5 Хардкорник, 6 Самородок, 7 Силач; −1 — невідомий.</summary>
        public int Gimmick;
    }

    /// <summary>Один план розкадровки: коли склейка і як стоїть камера відносно осі дії.</summary>
    public struct CineBeat
    {
        public CineBeatKind Kind;
        /// <summary>Мить склейки на цей план у часі кліпа, с.</summary>
        public float ClipTime;
        public CineSize Size;
        public CineSubject Subject;
        /// <summary>Курс камери, ° від осі дії «нападник → жертва» у бік камери трансляції: 0 — попереду нападника (дивиться йому
        /// в обличчя), 90 — профіль пари, 180 — з-за спини нападника.</summary>
        public float Azimuth;
        /// <summary>Бажана відстань від точки погляду до камери по горизонталі, м.</summary>
        public float Distance;
        /// <summary>Найменша відстань, з якою план ще читається; ближче — план позначається «тісним».</summary>
        public float MinDistance;
        /// <summary>Висота камери, м: над матом або над точкою погляду (<see cref="HeightAboveTarget"/>).</summary>
        public float Height;
        public bool HeightAboveTarget;
        /// <summary>Межі кута огляду по вертикалі, °: план бере найвужчий, у який уміщається головне між панеллю й верхньою межею.</summary>
        public float FovMin, FovMax;
        /// <summary>Верхня межа головного, частка висоти кадру від низу: на ударі й наслідку — під словом «ФІНІШЕР!»
        /// (<see cref="RingFinisherCinePlan.SafeTop"/>), до удару слова на екрані ще немає — майже до краю кадру.</summary>
        public float Top;
        /// <summary>Наїзд до точки погляду, м/с реального часу; 0 — камера стоїть.</summary>
        public float Dolly;
    }

    /// <summary>Розклад кіно одного фінішера: плани по черзі, мить удару, кінець, що з ефектів дозволено.</summary>
    public sealed class FinisherCineTimeline
    {
        public readonly List<CineBeat> Beats = new List<CineBeat>();
        /// <summary>Мить удару в часі кліпа, с.</summary>
        public float Contact;
        /// <summary>Кіно віддає камеру режисерові на цій миті кліпа, с.</summary>
        public float End;
        /// <summary>Промах: наприкінці — склейка на загальний план (удару в кіно немає).</summary>
        public bool CutToWideAtEnd;
        /// <summary>Сповільнення рампою навколо удару.</summary>
        public bool Ramp;
        /// <summary>Стоп-кадр у мить удару.</summary>
        public bool Freeze;
        public bool Flash, Strobe, Shake;

        public bool Empty { get { return Beats.Count == 0; } }
    }

    /// <summary>Що у кадрі: місця й голови обох, рефері, ринг. Світ: Y — угору; X і Z — мат.</summary>
    public struct CineScene
    {
        /// <summary>Таз нападника й жертви в проєкції на мат (Y = висота мату). Для плану удару — місця в мить удару.</summary>
        public CapPoint Attacker, Victim;
        /// <summary>Голови в мить склейки.</summary>
        public CapPoint AttackerHead, VictimHead;
        /// <summary>Стопи рефері.</summary>
        public CapPoint Referee;
        /// <summary>Куди дивиться нападник (одиничний, по мату) — вісь дії, коли пара стоїть упритул.</summary>
        public CapPoint Facing;
        public double MatY, RingHalf;
        /// <summary>
        /// Вісь дії зафіксовано на першому кадрі кліпа (правило 1 розкадровки): <see cref="RingFinisherCinePlan.Axis"/> бере
        /// <see cref="AxisDir"/>, а не лінію тазів цього кадру. Пара за прийом розвертається, жертва опиняється над нападником —
        /// а камери всіх планів лишаються з того самого боку від тієї самої лінії.
        /// </summary>
        public bool AxisFixed;
        /// <summary>Зафіксована вісь дії «нападник → жертва» по мату (довжина довільна, не нуль).</summary>
        public CapPoint AxisDir;
    }

    /// <summary>Порахований план: де камера, куди дивиться, кут огляду.</summary>
    public struct CineShot
    {
        public CapPoint Pos, Look;
        public float Fov;
        /// <summary>false — план не ставиться (точка погляду поза рингом): режисер лишає свою камеру.</summary>
        public bool Ok;
        /// <summary>Камера ближче за <see cref="CineBeat.MinDistance"/> (пара біля канатів) — план тісний, але чистий.</summary>
        public bool Tight;
        /// <summary>Між камерою й головним — рефері (іншого ракурсу не знайшлось).</summary>
        public bool Blocked;
        public float Azimuth, Distance;
    }

    /// <summary>
    /// Фінішер як кіно (власник 14.11.2026: «А фінішери це шось кінематографічне»; розкадровка — docs/ring/FINISHER_CINE.md).
    /// Чиста модель без рушія: розклад планів за мітками кліпа («яка склейка коли»), рампа сповільнення, місце камери
    /// всередині рингу з одного боку від осі дії, кадрування між нижньою панеллю й словом «ФІНІШЕР!». Виконує
    /// <c>RingFinisherCine</c>; охоронці — <c>RingFinisherCinePlanTests</c>. Кидків і результату матчу не читає й не змінює;
    /// детерміновано, без генератора випадковості.
    /// </summary>
    public static class RingFinisherCinePlan
    {
        /// <summary>Склейка на план удару — за стільки секунд кліпа до удару.</summary>
        public const float ImpactLead = 0.2f;
        /// <summary>Запасний розклад без міток: хват — за стільки до удару…</summary>
        public const float GripBeforeContact = 0.9f;
        /// <summary>…або через стільки після мітки заклику, якщо вона є.</summary>
        public const float GripAfterCall = 0.8f;
        /// <summary>Запасний розклад: наслідок — через стільки після удару.</summary>
        public const float AfterDelay = 0.45f;
        /// <summary>План удару тримається після удару щонайменше стільки (у сповільненні — це понад 0,8 с реального часу).</summary>
        public const float ImpactHold = 0.3f;
        /// <summary>Найкоротший план, с кліпа: частіші склейки миготять.</summary>
        public const float MinShot = 0.5f;
        /// <summary>Наслідок тримається стільки, далі камера — режисерові (план утримання).</summary>
        public const float AfterHold = 1.2f;
        /// <summary>Наслідок потребує щонайменше стільки кліпа після склейки, інакше його немає.</summary>
        public const float AfterMinTail = 0.3f;
        /// <summary>Промах: кіно закінчується за стільки до миті, де був би удар, — сам промах іде загальним планом.</summary>
        public const float MissLead = 0.25f;

        /// <summary>Рампа: сповільнення починається за стільки до удару…</summary>
        public const float RampLead = 0.25f;
        /// <summary>…розганяється до <see cref="SlowSpeed"/> за стільки…</summary>
        public const float RampIn = 0.1f;
        public const float SlowSpeed = 0.35f;
        /// <summary>…тримається стільки після удару…</summary>
        public const float SlowAfter = 0.25f;
        /// <summary>…і повертається до 1 за стільки (усе — секунди кліпа).</summary>
        public const float RampOut = 0.3f;
        /// <summary>Стоп-кадр у мить удару: стільки реальних секунд на швидкості <see cref="FreezeSpeed"/>.</summary>
        public const float FreezeReal = 0.06f;
        public const float FreezeSpeed = 0.05f;
        /// <summary>Бюджет: кіно додає до фінішера не більше стільки реальних секунд (сповільнення + стоп-кадр).</summary>
        public const float MaxExtraReal = 1.2f;

        /// <summary>Нижня панель дій закриває стільки висоти кадру знизу; головне — вище.</summary>
        public const double SafeBottom = 0.29;
        /// <summary>Слово «ФІНІШЕР!» — верхні 8–22 % кадру (Статут FX-05); голови — нижче цієї межі (частка від низу).</summary>
        public const double SafeTop = 0.76;
        /// <summary>Камера не ближче стільки до лінії бійцівського квадрата (канати лишаються за камерою).</summary>
        public const double RopeMargin = 0.30;
        /// <summary>Промінь від камери до головного проходить не ближче стільки до осі тіла рефері.</summary>
        public const double RefClear = 0.45;
        /// <summary>Сусідні плани відрізняються розміром або курсом камери щонайменше на стільки градусів.</summary>
        public const float MinAngleChange = 30f;

        private static readonly float[] AzimuthTries = { 0f, 20f, -20f, 40f, -40f };

        // ------------------------------------------------------------------ розклад

        /// <summary>
        /// Розкадровка: заклик → хват → вершина → удар → наслідок. Чого бракує (мітки, часу, удару) — того плану немає;
        /// між склейками не менше <see cref="MinShot"/>. Коли часу до удару мало, першими зникають заклик, потім хват.
        /// </summary>
        public static FinisherCineTimeline Build(CineMarks m, CineOptions o)
        {
            var tl = new FinisherCineTimeline();
            if (!(m.Contact >= 0f)) return tl;                              // без миті удару (NaN теж) кіно не ставимо
            float len = m.Length > 0f ? m.Length : float.PositiveInfinity;
            float c = Math.Min(m.Contact, len);
            tl.Contact = c;

            // Плани до удару. Мить, до якої вони мають уміститись: склейка на удар або (промах) кінець кіно.
            float last = o.Hit ? Math.Max(0f, c - ImpactLead) : Math.Max(0f, c - MissLead);
            var pre = new List<CineBeat>();
            if (!o.Repeat) pre.Add(Shot(CineBeatKind.Call, m.Call >= 0f ? m.Call : 0f, o));
            pre.Add(Shot(CineBeatKind.Grip, m.Grip >= 0f ? m.Grip : (m.Call >= 0f ? m.Call + GripAfterCall : c - GripBeforeContact), o));
            if (o.Hit && m.Apex >= 0f) pre.Add(Shot(CineBeatKind.Apex, m.Apex, o));

            // Від удару назад: кожен план закінчується наступною склейкою й триває не менше MinShot. Не вмістився — немає.
            float next = last;
            var kept = new List<CineBeat>();
            for (int i = pre.Count - 1; i >= 0; i--)
            {
                float cap = next - MinShot;
                if (cap < 0f) break;                                        // раніші плани теж не вмістяться
                var b = pre[i];
                b.ClipTime = Math.Min(Math.Max(b.ClipTime, 0f), cap);
                kept.Add(b);
                next = b.ClipTime;
            }
            kept.Reverse();
            tl.Beats.AddRange(kept);

            if (!o.Hit)
            {
                if (tl.Beats.Count == 0) return tl;                         // показати нічого: звичайна камера режисера
                tl.End = last;
                tl.CutToWideAtEnd = true;
                return tl;
            }

            tl.Beats.Add(Shot(CineBeatKind.Impact, last, o));
            float after = Math.Max(m.After >= 0f ? m.After : c + AfterDelay, Math.Max(c + ImpactHold, last + MinShot));
            bool hasAfter = len - after >= AfterMinTail;
            if (hasAfter) tl.Beats.Add(Shot(CineBeatKind.After, after, o));
            tl.End = Math.Min(len, (hasAfter ? after : c) + AfterHold);

            // «Менше руху» прибирає й спалах із тремтінням (як екран виходу: спокійний режим — без різких змін кадру);
            // найнижча якість — лише строб світла.
            tl.Ramp = !o.ReduceMotion;
            tl.Freeze = !o.ReduceMotion;
            tl.Flash = o.Flashes && !o.ReduceMotion;
            tl.Strobe = tl.Flash && !o.Cheap;
            tl.Shake = o.ScreenShake && !o.ReduceMotion;
            return tl;
        }

        /// <summary>Який план іде на миті <paramref name="clipTime"/>: індекс у <see cref="FinisherCineTimeline.Beats"/>; −1 — кіно ще не почалось чи вже віддало камеру.</summary>
        public static int BeatAt(FinisherCineTimeline tl, float clipTime)
        {
            if (tl == null || tl.Beats.Count == 0 || clipTime >= tl.End) return -1;
            int k = -1;
            for (int i = 0; i < tl.Beats.Count; i++)
                if (tl.Beats[i].ClipTime <= clipTime) k = i;
            return k;
        }

        /// <summary>На кого дивиться план наслідку за ґіміком: обличчя жертви, крик нападника чи обоє (Силач накриває).</summary>
        public static CineSubject AfterSubject(int gimmick)
        {
            switch (gimmick)
            {
                case 1: case 3: case 5: case 6: return CineSubject.Attacker;   // Літун, Шоумен, Хардкорник, Самородок
                case 7: return CineSubject.Pair;                               // Силач
                default: return CineSubject.Victim;                            // Важковаговик, Технік, Бувалий, невідомий
            }
        }

        /// <summary>Таблиця планів (§2 розкадровки). Сусідні плани різняться розміром, а курс — щонайменше на 30°.</summary>
        private static CineBeat Shot(CineBeatKind kind, float clipTime, CineOptions o)
        {
            var b = new CineBeat { Kind = kind, ClipTime = clipTime };
            switch (kind)
            {
                case CineBeatKind.Call:
                    b.Size = CineSize.Close; b.Subject = CineSubject.Attacker;
                    b.Azimuth = 40f; b.Distance = 1.8f; b.MinDistance = 1.2f;
                    b.Height = -0.05f; b.HeightAboveTarget = true;
                    b.FovMin = 24f; b.FovMax = 34f; b.Dolly = 0.12f; b.Top = 0.88f;
                    break;
                case CineBeatKind.Grip:
                    b.Size = CineSize.Medium; b.Subject = CineSubject.Pair;
                    b.Azimuth = 90f; b.Distance = 2.9f; b.MinDistance = 1.9f;
                    b.Height = 1.0f;
                    b.FovMin = 34f; b.FovMax = 50f; b.Dolly = 0f; b.Top = 0.92f;
                    break;
                case CineBeatKind.Apex:
                    // Низька камера: жертва над головою нападника на тлі світла; до удару слова ще немає — верх майже до краю.
                    b.Size = CineSize.Hero; b.Subject = CineSubject.Pair;
                    b.Azimuth = 125f; b.Distance = 3.1f; b.MinDistance = 2.0f;
                    b.Height = 0.42f;
                    b.FovMin = 30f; b.FovMax = 52f; b.Dolly = 0.3f; b.Top = 0.94f;
                    break;
                case CineBeatKind.Impact:
                    b.Size = CineSize.Mat; b.Subject = CineSubject.Victim;
                    b.Azimuth = 55f; b.Distance = 2.1f; b.MinDistance = 1.4f;
                    b.Height = 0.35f;
                    b.FovMin = 38f; b.FovMax = 52f; b.Dolly = 0f; b.Top = (float)SafeTop;
                    break;
                default:
                    b.Size = CineSize.Face; b.Subject = AfterSubject(o.Gimmick);
                    b.Azimuth = 95f; b.Distance = 1.3f; b.MinDistance = 0.8f;
                    b.Height = 0.65f; b.HeightAboveTarget = true;
                    b.FovMin = 26f; b.FovMax = 38f; b.Dolly = 0.08f; b.Top = (float)SafeTop;
                    break;
            }
            if (o.ReduceMotion) b.Dolly = 0f;
            return b;
        }

        // ------------------------------------------------------------------ час

        /// <summary>
        /// Швидкість гри на миті кліпа: рампа, не сходинка. 1 → <see cref="SlowSpeed"/> за <see cref="RampIn"/> перед ударом,
        /// тримає до <see cref="SlowAfter"/> після нього, повертається за <see cref="RampOut"/>. Без рампи — завжди 1.
        /// Стоп-кадр (<see cref="FreezeReal"/>) виконавець додає окремо в мить удару.
        /// </summary>
        public static float Speed(FinisherCineTimeline tl, float clipTime)
        {
            if (tl == null || !tl.Ramp) return 1f;
            float x = clipTime - tl.Contact;
            if (x <= -RampLead) return 1f;
            if (x < -RampLead + RampIn) return Lerp(1f, SlowSpeed, Smooth((x + RampLead) / RampIn));
            if (x <= SlowAfter) return SlowSpeed;
            if (x < SlowAfter + RampOut) return Lerp(SlowSpeed, 1f, Smooth((x - SlowAfter) / RampOut));
            return 1f;
        }

        /// <summary>Скільки реальних секунд кіно додає до фінішера (сповільнення + стоп-кадр) при темпі відтворення <paramref name="playSpeed"/>.</summary>
        public static float ExtraRealSeconds(FinisherCineTimeline tl, float playSpeed)
        {
            if (tl == null || !tl.Ramp || !(playSpeed > 0f)) return 0f;
            const float dt = 1f / 480f;
            double extra = 0.0;
            // Кліп починається з 0: якщо удар раніше за RampLead, сповільнення до нього коротше.
            for (float t = Math.Max(0f, tl.Contact - RampLead); t < tl.Contact + SlowAfter + RampOut; t += dt)
                extra += (1.0 / Speed(tl, t + dt * 0.5f) - 1.0) * dt;
            return (float)(extra / playSpeed) + (tl.Freeze ? FreezeReal : 0f);
        }

        // ------------------------------------------------------------------ камера

        /// <summary>
        /// Вісь дії по мату: зафіксована на початку кіно (<see cref="CineScene.AxisFixed"/>), інакше — від нападника до жертви;
        /// пара впритул — куди дивиться нападник.
        /// </summary>
        public static CapPoint Axis(CineScene s)
        {
            if (s.AxisFixed)
            {
                double fl = Math.Sqrt(s.AxisDir.X * s.AxisDir.X + s.AxisDir.Z * s.AxisDir.Z);
                if (fl > 1e-6) return new CapPoint(s.AxisDir.X / fl, 0.0, s.AxisDir.Z / fl);
            }
            double dx = s.Victim.X - s.Attacker.X, dz = s.Victim.Z - s.Attacker.Z;
            double l = Math.Sqrt(dx * dx + dz * dz);
            if (l < 0.15)
            {
                dx = s.Facing.X; dz = s.Facing.Z;
                l = Math.Sqrt(dx * dx + dz * dz);
                if (l < 1e-6) { dx = 0.0; dz = 1.0; l = 1.0; }
            }
            return new CapPoint(dx / l, 0.0, dz / l);
        }

        /// <summary>Перпендикуляр до осі (ліворуч від напряму «нападник → жертва»).</summary>
        private static CapPoint Perp(CapPoint d) { return new CapPoint(-d.Z, 0.0, d.X); }

        /// <summary>
        /// З якого боку від осі стоять усі камери фінішера: +1 чи −1 (множник перпендикуляра). Правило осі: бік камери
        /// трансляції (зал дивиться з −Z) — тоді перша склейка не перескакує лінію дії. Інший бік — лише якщо з цього до
        /// канатів менше 1,6 м (плани не стануть) або бік трансляції не виражений, а рефері стоїть біля пари саме там.
        /// </summary>
        public static int Side(CineScene s)
        {
            var d = Axis(s);
            var p = Perp(d);
            double mx = (s.Attacker.X + s.Victim.X) * 0.5, mz = (s.Attacker.Z + s.Victim.Z) * 0.5;
            double half = 0.5 * Math.Sqrt((s.Victim.X - s.Attacker.X) * (s.Victim.X - s.Attacker.X) + (s.Victim.Z - s.Attacker.Z) * (s.Victim.Z - s.Attacker.Z));
            double lim = s.RingHalf - RopeMargin;
            double along = (s.Referee.X - mx) * d.X + (s.Referee.Z - mz) * d.Z;
            bool refNear = Math.Abs(along) <= half + 1.0;                               // рефері біля пари, а не за спинами
            double best = double.NegativeInfinity;
            int side = 1;
            for (int sg = 1; sg >= -1; sg -= 2)
            {
                double nx = p.X * sg, nz = p.Z * sg;
                double score = -nz;                                                    // камера з боку −Z — як трансляція
                double refDot = (s.Referee.X - mx) * nx + (s.Referee.Z - mz) * nz;
                if (refNear && refDot > 0.3 && refDot < 3.0) score -= 0.8;             // рефері між камерами й парою
                if (Room(mx, mz, nx, nz, lim) < 1.6) score -= 3.0;                     // з того боку канати впритул
                if (score > best + 1e-9) { best = score; side = sg; }
            }
            return side;
        }

        /// <summary>
        /// Де стати рефері на час кіно: за спиною нападника й на 1,2 м у бік камер — поза кадром усіх планів до удару (заклик
        /// дивиться нападникові в обличчя, тож рефері просто за його спиною був би тлом кадру).
        /// </summary>
        public static CapPoint RefereeSpot(CineScene s, int side)
        {
            var d = Axis(s);
            var p = Perp(d);
            double lim = s.RingHalf - RopeMargin - 0.25;
            double x = s.Attacker.X - d.X * 2.2 + p.X * side * 1.2, z = s.Attacker.Z - d.Z * 2.2 + p.Z * side * 1.2;
            return new CapPoint(Math.Max(-lim, Math.Min(lim, x)), s.MatY, Math.Max(-lim, Math.Min(lim, z)));
        }

        /// <summary>
        /// Камера плану: усередині рингу, з боку <paramref name="side"/> від осі дії; рефері не між камерою й головним; головне —
        /// між нижньою панеллю й словом «ФІНІШЕР!». Пробує курс плану й відхили ±20°, ±40° (бік осі не міняє).
        /// </summary>
        public static CineShot Solve(CineBeat b, CineScene s, int side)
        {
            var shot = new CineShot();
            CapPoint target, low, high;
            Subject(b, s, out target, out low, out high);
            double lim = s.RingHalf - RopeMargin;
            if (Math.Abs(target.X) > lim || Math.Abs(target.Z) > lim) return shot;      // дія за канатами — не наш план

            var d = Axis(s);
            var p = Perp(d);
            double bestScore = double.NegativeInfinity;
            for (int i = 0; i < AzimuthTries.Length; i++)
            {
                float az = Math.Max(15f, Math.Min(165f, b.Azimuth + AzimuthTries[i]));
                double a = az * Math.PI / 180.0;
                double dirX = Math.Cos(a) * d.X + Math.Sin(a) * p.X * side, dirZ = Math.Cos(a) * d.Z + Math.Sin(a) * p.Z * side;
                double dist = Math.Min(b.Distance, Room(target.X, target.Z, dirX, dirZ, lim));
                double y = Math.Max(s.MatY + 0.2, (b.HeightAboveTarget ? target.Y : s.MatY) + b.Height);
                var pos = new CapPoint(target.X + dirX * dist, y, target.Z + dirZ * dist);
                bool tight = dist < b.MinDistance;
                bool blocked = Blocked(pos, target, low, high, s);
                // Перший чистий і не тісний ракурс у порядку спроб; інакше — найкращий із решти: спершу без рефері, потім далі.
                double score = (blocked ? -100.0 : 0.0) + (tight ? -10.0 + dist : 0.0) - i * 0.01;
                if (score <= bestScore) continue;
                bestScore = score;
                shot.Pos = pos; shot.Azimuth = az; shot.Distance = (float)dist; shot.Tight = tight; shot.Blocked = blocked;
                if (!blocked && !tight) break;
            }
            // Камері ніде стати (пара в куті, план упирається в канати): плану немає — лишається попередній.
            if (shot.Distance < b.MinDistance * 0.5f) return shot;
            Frame(b, target, low, high, ref shot);
            shot.Ok = true;
            return shot;
        }

        /// <summary>Де точка світу по висоті кадру плану: частка від низу (0 — низ, 1 — верх).</summary>
        public static double FromBottom(CineShot shot, CapPoint point)
        {
            double x, y, depth;
            Project(shot, point, 16.0 / 9.0, out x, out y, out depth);
            return y;
        }

        /// <summary>
        /// Точка світу в кадрі плану: <paramref name="x"/> — частка від лівого краю, <paramref name="y"/> — від низу (0..1 — у кадрі),
        /// <paramref name="depth"/> — метри вперед від камери (≤ 0 — позаду камери). Камера без крену.
        /// </summary>
        public static void Project(CineShot shot, CapPoint point, double aspect, out double x, out double y, out double depth)
        {
            x = 0.5; y = 0.5; depth = 0.0;
            double fx = shot.Look.X - shot.Pos.X, fy = shot.Look.Y - shot.Pos.Y, fz = shot.Look.Z - shot.Pos.Z;
            double fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
            if (fl < 1e-9) return;
            fx /= fl; fy /= fl; fz /= fl;
            // «вправо» = up × forward, «угору» = forward × right
            double rx = fz, rz = -fx, rl = Math.Sqrt(rx * rx + rz * rz);
            if (rl < 1e-9) return;
            rx /= rl; rz /= rl;
            double ux = fy * rz, uy = fz * rx - fx * rz, uz = -fy * rx;
            double vx = point.X - shot.Pos.X, vy = point.Y - shot.Pos.Y, vz = point.Z - shot.Pos.Z;
            depth = vx * fx + vy * fy + vz * fz;
            double z = Math.Max(1e-3, depth);
            double t = Math.Tan(shot.Fov * 0.5 * Math.PI / 180.0);
            x = 0.5 + 0.5 * (vx * rx + vz * rz) / (z * t * aspect);
            y = 0.5 + 0.5 * (vx * ux + vy * uy + vz * uz) / (z * t);
        }

        /// <summary>Чи точка в кадрі плану (попереду камери й у межах кадру з полем <paramref name="margin"/> по краях).</summary>
        public static bool InFrame(CineShot shot, CapPoint point, double aspect, double margin = 0.0)
        {
            double x, y, depth;
            Project(shot, point, aspect, out x, out y, out depth);
            return depth > 0.05 && x >= -margin && x <= 1.0 + margin && y >= -margin && y <= 1.0 + margin;
        }

        // Точка погляду й найнижче/найвище головне плану.
        private static void Subject(CineBeat b, CineScene s, out CapPoint target, out CapPoint low, out CapPoint high)
        {
            switch (b.Kind)
            {
                case CineBeatKind.Call:
                    target = s.AttackerHead;
                    low = new CapPoint(target.X, target.Y - 0.32, target.Z);              // лінія плечей
                    high = new CapPoint(target.X, target.Y + 0.14, target.Z);             // маківка
                    return;
                case CineBeatKind.Grip:
                {
                    double top = Math.Max(s.AttackerHead.Y, s.VictimHead.Y) + 0.14;
                    double mx = (s.Attacker.X + s.Victim.X) * 0.5, mz = (s.Attacker.Z + s.Victim.Z) * 0.5;
                    low = new CapPoint(mx, s.MatY + 0.5, mz);                             // коліна
                    high = new CapPoint(mx, top, mz);
                    target = new CapPoint(mx, (low.Y + high.Y) * 0.5, mz);
                    return;
                }
                case CineBeatKind.Apex:
                {
                    double top = Math.Max(s.AttackerHead.Y, s.VictimHead.Y) + 0.2;
                    low = new CapPoint(s.Attacker.X, s.MatY + 1.05, s.Attacker.Z);        // груди нападника
                    high = new CapPoint(s.Attacker.X, top, s.Attacker.Z);                 // жертва над ним
                    target = new CapPoint(s.Attacker.X, (low.Y + high.Y) * 0.5, s.Attacker.Z);
                    return;
                }
                case CineBeatKind.Impact:
                    // Кадр розкладено на КІНЕЦЬ удару — мат у точці удару й тіла над ним, а не на голови в мить склейки: за
                    // ImpactLead до удару тіла ще вище й заходять у кадр згори, слово «ФІНІШЕР!» з'являється вже на ударі.
                    low = new CapPoint(s.Victim.X, s.MatY, s.Victim.Z);                   // мат у точці удару
                    high = new CapPoint(s.Victim.X, s.MatY + 0.95, s.Victim.Z);           // тіла над ним у мить удару
                    target = new CapPoint(s.Victim.X, s.MatY + 0.35, s.Victim.Z);
                    return;
                default:
                {
                    CapPoint h = b.Subject == CineSubject.Attacker ? s.AttackerHead
                        : b.Subject == CineSubject.Victim ? s.VictimHead
                        : new CapPoint((s.AttackerHead.X + s.VictimHead.X) * 0.5, (s.AttackerHead.Y + s.VictimHead.Y) * 0.5, (s.AttackerHead.Z + s.VictimHead.Z) * 0.5);
                    double span = b.Subject == CineSubject.Pair ? 0.45 : 0.24;
                    target = h;
                    low = new CapPoint(h.X, Math.Max(s.MatY, h.Y - span), h.Z);
                    high = new CapPoint(h.X, h.Y + span * 0.6, h.Z);
                    return;
                }
            }
        }

        /// <summary>Скільки метрів від точки в напрямі до межі квадрата |x|, |z| ≤ lim (0 — точка вже за межею).</summary>
        private static double Room(double x, double z, double dirX, double dirZ, double lim)
        {
            double t = double.PositiveInfinity;
            if (dirX > 1e-9) t = Math.Min(t, (lim - x) / dirX); else if (dirX < -1e-9) t = Math.Min(t, (-lim - x) / dirX);
            if (dirZ > 1e-9) t = Math.Min(t, (lim - z) / dirZ); else if (dirZ < -1e-9) t = Math.Min(t, (-lim - z) / dirZ);
            return Math.Max(0.0, t);
        }

        private static bool Blocked(CapPoint pos, CapPoint target, CapPoint low, CapPoint high, CineScene s)
        {
            var foot = new CapPoint(s.Referee.X, s.MatY, s.Referee.Z);
            var top = new CapPoint(s.Referee.X, s.MatY + 1.8, s.Referee.Z);
            return RingCapsules.SegmentDistance(pos, target, foot, top) < RefClear
                || RingCapsules.SegmentDistance(pos, low, foot, top) < RefClear
                || RingCapsules.SegmentDistance(pos, high, foot, top) < RefClear;
        }

        /// <summary>
        /// Кут огляду й точка погляду: найнижче головне — на <see cref="SafeBottom"/>, найвище — на верхній межі плану
        /// (<see cref="CineBeat.Top"/>). Якщо головне менше за цю смугу навіть на найвужчому куті — по центру смуги; якщо
        /// більше навіть на найширшому — низ стоїть над панеллю, а верх виходить за межу (панель закриває дію, слово — ні).
        /// </summary>
        private static void Frame(CineBeat b, CapPoint target, CapPoint low, CapPoint high, ref CineShot shot)
        {
            double eLow = Elevation(shot.Pos, low), eHigh = Elevation(shot.Pos, high);
            double topFrac = b.Top > 0.5f ? b.Top : SafeTop;
            double kLow = 1.0 - 2.0 * SafeBottom, kHigh = 2.0 * topFrac - 1.0;          // частки пів кадру вниз і вгору від центру
            double tMin = Math.Tan(b.FovMin * 0.5 * Math.PI / 180.0), tMax = Math.Tan(b.FovMax * 0.5 * Math.PI / 180.0);
            double span = eHigh - eLow;
            double t, pitch;
            if (span <= Math.Atan(kLow * tMin) + Math.Atan(kHigh * tMin))
            {
                t = tMin;                                                               // уміщається із запасом: по центру смуги
                double slack = Math.Atan(kLow * t) + Math.Atan(kHigh * t) - span;
                pitch = eLow + Math.Atan(kLow * t) - slack * 0.5;
            }
            else if (span >= Math.Atan(kLow * tMax) + Math.Atan(kHigh * tMax))
            {
                t = tMax;                                                               // не вміщається: низ над панеллю
                pitch = eLow + Math.Atan(kLow * t);
            }
            else
            {
                double a = tMin, c = tMax;
                for (int i = 0; i < 40; i++)
                {
                    double mid = (a + c) * 0.5;
                    if (Math.Atan(kLow * mid) + Math.Atan(kHigh * mid) < span) a = mid; else c = mid;
                }
                t = (a + c) * 0.5;
                pitch = eLow + Math.Atan(kLow * t);
            }
            shot.Fov = (float)(2.0 * Math.Atan(t) * 180.0 / Math.PI);
            double hx = target.X - shot.Pos.X, hz = target.Z - shot.Pos.Z;
            double hl = Math.Max(1e-6, Math.Sqrt(hx * hx + hz * hz));
            shot.Look = new CapPoint(target.X, shot.Pos.Y + Math.Tan(pitch) * hl, target.Z);
        }

        /// <summary>Кут підвищення точки від камери, рад (додатний — вище камери).</summary>
        private static double Elevation(CapPoint from, CapPoint to)
        {
            double hx = to.X - from.X, hz = to.Z - from.Z;
            return Math.Atan2(to.Y - from.Y, Math.Max(1e-6, Math.Sqrt(hx * hx + hz * hz)));
        }

        private static float Smooth(float u) { u = u < 0f ? 0f : u > 1f ? 1f : u; return u * u * (3f - 2f * u); }
        private static float Lerp(float a, float b, float k) { return a + (b - a) * k; }
    }
}
