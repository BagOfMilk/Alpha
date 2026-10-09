using System;
using Game.Core.Scenes;

namespace Game.Gameplay.UI
{
    /// <summary>Як знято кадр діалогу в світі (власник, 08.10.2026: діалоги «як в baldursgayte 3», «Камера в світі»).</summary>
    public enum DialogueShotKind
    {
        /// <summary>Загальний план місця: людей не виділяємо (<see cref="ShotFraming.Empty"/> без мовця).</summary>
        Wide = 0,
        /// <summary>Крупний: обличчя одного.</summary>
        Close = 1,
        /// <summary>Через плече слухача на того, хто говорить.</summary>
        OverShoulder = 2,
        /// <summary>Двоє в кадрі збоку.</summary>
        Two = 3
    }

    /// <summary>Кого знімати: <see cref="SubjectId"/> — на кому фокус, <see cref="OtherId"/> — співрозмовник (вісь і плече).</summary>
    public sealed class DialogueShot
    {
        public DialogueShotKind Kind;
        public string SubjectId;
        public string OtherId;

        /// <summary>Підпис кадру — та сама людина в тому самому плані не перезнімається (без склейки на кожній репліці).</summary>
        public string Signature => Kind + "|" + (SubjectId ?? "") + "|" + (OtherId ?? "");
    }

    /// <summary>Точка без типів рушія: модель тестується headless.</summary>
    public struct DialogueVec
    {
        public float X, Y, Z;

        public DialogueVec(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static DialogueVec operator +(DialogueVec a, DialogueVec b) => new DialogueVec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static DialogueVec operator -(DialogueVec a, DialogueVec b) => new DialogueVec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static DialogueVec operator *(DialogueVec a, float k) => new DialogueVec(a.X * k, a.Y * k, a.Z * k);

        public float FlatLength => (float)Math.Sqrt(X * X + Z * Z);
    }

    /// <summary>Поза камери: де стоїть, куди дивиться, кут огляду (градуси, по вертикалі).</summary>
    public struct DialogueCameraPose
    {
        public DialogueVec Position;
        public DialogueVec LookAt;
        public float Fov;
    }

    /// <summary>
    /// Режисура діалогу в світі: ЩО знімати (з кадру сцени ядра) і ЗВІДКИ (з позицій голів). Чистий C#:
    /// рішення про кадр лишається за даними сцени (<see cref="SceneStep.Shot"/>), а камера лише виконує.
    /// Правило 180°: обидва крупні плани знято з одного боку лінії між співрозмовниками — інакше при склейці
    /// здається, що люди дивляться в один бік.
    /// </summary>
    public static class DialogueDirector
    {
        /// <summary>Крупний: відстань від обличчя в зростах постаті і кут огляду.</summary>
        public const float CloseDistance = 0.85f, CloseFov = 30f;
        /// <summary>Кут, на який камера крупного плану відходить від лінії погляду (щоб обличчя було в три чверті).</summary>
        public const float CloseSideDegrees = 28f;
        /// <summary>
        /// Другий варіант крупного (повернення до тієї самої людини): тісніше й ближче до осі погляду — склейка на того
        /// самого не дає однакового кадру (розбір BG3, 08.10.2026: у кожного персонажа два крупні плани).
        /// </summary>
        public const float CloseBSideDegrees = 16f, CloseBDistance = 0.7f;
        public const float ShoulderFov = 32f, TwoFov = 36f, WideFov = 42f;

        /// <summary>
        /// Кадр для кроку сцени. <paramref name="protagonistId"/> — хто з учасників герой (співрозмовник за
        /// замовчуванням). Мовець, якого немає в плані сцени, отримує крупний план: у BG3 видно того, хто говорить.
        /// На виборі й на панелі наслідку кадр не змінюється — він уже заданий сценою (зазвичай крупний на героя).
        /// </summary>
        public static DialogueShot Plan(ShotFraming framing, string actorId, string secondActorId, string speakerId,
            string protagonistId)
        {
            bool hasActor = !string.IsNullOrEmpty(actorId);
            bool hasSecond = !string.IsNullOrEmpty(secondActorId);
            bool hasSpeaker = !string.IsNullOrEmpty(speakerId);

            if (framing == ShotFraming.Two && hasActor && hasSecond)
            {
                if (hasSpeaker && (speakerId == actorId || speakerId == secondActorId))
                    return new DialogueShot
                    {
                        Kind = DialogueShotKind.OverShoulder,
                        SubjectId = speakerId,
                        OtherId = speakerId == actorId ? secondActorId : actorId
                    };
                if (!hasSpeaker)
                    return new DialogueShot { Kind = DialogueShotKind.Two, SubjectId = actorId, OtherId = secondActorId };
            }

            // Говорить той, кого в плані немає (порожній план із репліками прибульців, чужий крупний) — крупний на мовця.
            if (hasSpeaker && speakerId != actorId && speakerId != secondActorId)
                return Close(speakerId, protagonistId);

            if (framing == ShotFraming.Close && hasActor) return Close(actorId, protagonistId);
            if (framing == ShotFraming.Two && hasActor) return Close(hasSpeaker ? speakerId : actorId, protagonistId);
            if (hasSpeaker) return Close(speakerId, protagonistId);
            if (framing == ShotFraming.Empty) return new DialogueShot { Kind = DialogueShotKind.Wide, SubjectId = protagonistId };
            return null; // план не задано — лишаємо попередній
        }

        private static DialogueShot Close(string subject, string protagonistId)
            => new DialogueShot
            {
                Kind = DialogueShotKind.Close,
                SubjectId = subject,
                OtherId = subject == protagonistId ? null : protagonistId
            };

        /// <summary>
        /// Поза камери. <paramref name="subjectHead"/>/<paramref name="otherHead"/> — голови (світ), <paramref name="height"/> —
        /// зріст постаті. <paramref name="axisFrom"/>→<paramref name="axisTo"/> — стала вісь сцени (герой → співрозмовник):
        /// з неї береться бік камери для ВСІХ планів сцени (правило 180°).
        /// </summary>
        public static DialogueCameraPose Pose(DialogueShotKind kind, DialogueVec subjectHead, DialogueVec otherHead,
            DialogueVec axisFrom, DialogueVec axisTo, float height)
            => Pose(kind, subjectHead, otherHead, axisFrom, axisTo, height, 0);

        /// <summary>Те саме з варіантом крупного: 0 — основний, 1 — другий (<see cref="CloseBSideDegrees"/>).</summary>
        public static DialogueCameraPose Pose(DialogueShotKind kind, DialogueVec subjectHead, DialogueVec otherHead,
            DialogueVec axisFrom, DialogueVec axisTo, float height, int variant)
        {
            float h = height > 0.05f ? height : 1f;
            var axis = Flat(axisTo - axisFrom);
            if (axis.FlatLength < 1e-4f) axis = new DialogueVec(0f, 0f, 1f);
            axis = axis * (1f / axis.FlatLength);
            var side = new DialogueVec(-axis.Z, 0f, axis.X); // ліворуч від осі — бік камери на всю сцену

            var toOther = Flat(otherHead - subjectHead);
            if (toOther.FlatLength < 1e-4f) toOther = axis;
            toOther = toOther * (1f / toOther.FlatLength);
            // Перпендикуляр до погляду мовця, звернений у бік камери (правило 180°).
            var across = new DialogueVec(-toOther.Z, 0f, toOther.X);
            if (Dot(across, side) < 0f) across = across * -1f;

            switch (kind)
            {
                case DialogueShotKind.Close:
                {
                    bool b = (variant & 1) == 1;
                    float a = (b ? CloseBSideDegrees : CloseSideDegrees) * (float)Math.PI / 180f;
                    float d = (b ? CloseBDistance : CloseDistance) * h;
                    var pos = subjectHead + toOther * (d * (float)Math.Cos(a)) + across * (d * (float)Math.Sin(a));
                    pos.Y = subjectHead.Y - 0.03f * h;
                    return new DialogueCameraPose { Position = pos, LookAt = subjectHead + new DialogueVec(0f, -0.06f * h, 0f), Fov = CloseFov };
                }
                case DialogueShotKind.OverShoulder:
                {
                    // За плечем слухача (other): трохи позаду, трохи вбік від осі і над плечем.
                    var back = toOther; // від мовця до слухача — далі за слухача
                    var pos = otherHead + back * (0.45f * h) + across * (0.22f * h) + new DialogueVec(0f, 0.04f * h, 0f);
                    return new DialogueCameraPose { Position = pos, LookAt = subjectHead + new DialogueVec(0f, -0.05f * h, 0f), Fov = ShoulderFov };
                }
                case DialogueShotKind.Two:
                {
                    var mid = (subjectHead + otherHead) * 0.5f;
                    float gap = Math.Max(Flat(otherHead - subjectHead).FlatLength, 0.6f * h);
                    var pos = mid + across * (1.5f * gap + 0.6f * h) + new DialogueVec(0f, 0.08f * h, 0f);
                    return new DialogueCameraPose { Position = pos, LookAt = mid + new DialogueVec(0f, -0.15f * h, 0f), Fov = TwoFov };
                }
                default:
                {
                    var mid = (subjectHead + otherHead) * 0.5f;
                    var pos = mid + side * (3.2f * h) - axis * (1.2f * h) + new DialogueVec(0f, 1.3f * h, 0f);
                    return new DialogueCameraPose { Position = pos, LookAt = mid + new DialogueVec(0f, -0.4f * h, 0f), Fov = WideFov };
                }
            }
        }

        /// <summary>Повільний наїзд у межах одного плану (жива камера, без склейки): частка відстані, 0..1 → 1..<paramref name="minScale"/>.</summary>
        public static DialogueVec PushIn(DialogueCameraPose pose, float t, float minScale = 0.94f)
        {
            float k = t <= 0f ? 1f : (t >= 1f ? minScale : 1f - (1f - minScale) * t);
            return pose.LookAt + (pose.Position - pose.LookAt) * k;
        }

        /// <summary>Знак боку точки відносно осі (для перевірки правила 180°): &gt;0 — ліворуч.</summary>
        public static float SideOf(DialogueVec point, DialogueVec axisFrom, DialogueVec axisTo)
        {
            var a = Flat(axisTo - axisFrom);
            var p = Flat(point - axisFrom);
            return a.X * p.Z - a.Z * p.X;
        }

        private static DialogueVec Flat(DialogueVec v) => new DialogueVec(v.X, 0f, v.Z);
        private static float Dot(DialogueVec a, DialogueVec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }
}
