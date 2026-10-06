using System;

namespace Game.Gameplay.Walk
{
    /// <summary>
    /// Поворот камери селом навколо героя (Поправка №18.5 — керування як у
    /// Wasteland 3: Q/E обертають камеру, колесо наближає). Кроки по 90° — той
    /// самий жест, що в бою (<c>BattleArenaController</c>, Q/E), щоб одне
    /// дієслово мало одну клавішу в усіх шарах (UX-17). Чиста математика без
    /// рушія: <c>HeroWalker</c> лише застосовує кут.
    /// </summary>
    public static class CameraOrbit
    {
        public const float Step = 90f;

        /// <summary>Новий цільовий кут після натискання: Q — <paramref name="direction"/> = −1, E — +1. Завжди 0, 90, 180 чи 270.</summary>
        public static float Turn(float yaw, int direction) => Wrap(Snap(yaw) + Math.Sign(direction) * Step);

        /// <summary>Кут у межах [0, 360).</summary>
        public static float Wrap(float degrees)
        {
            float d = degrees % 360f;
            return d < 0f ? d + 360f : d;
        }

        /// <summary>Найближчий кратний 90° (захист від накопичення похибки).</summary>
        public static float Snap(float degrees) => Wrap((float)Math.Round(degrees / Step) * Step);

        /// <summary>Плавний крок кута до цілі найкоротшою дугою: <paramref name="t"/> ∈ [0, 1].</summary>
        public static float Approach(float current, float target, float t)
        {
            float delta = Wrap(target - current + 180f) - 180f;
            if (Math.Abs(delta) < 0.01f) return Wrap(target);
            return Wrap(current + delta * Math.Max(0f, Math.Min(1f, t)));
        }

        /// <summary>Зсув камери від героя, повернутий навколо вертикалі на <paramref name="yaw"/> (висота й відстань не змінюються).</summary>
        public static void Rotate(float x, float z, float yaw, out float rx, out float rz)
        {
            double r = yaw * Math.PI / 180.0;
            double c = Math.Cos(r), s = Math.Sin(r);
            // Той самий знак, що Quaternion.Euler(0, yaw, 0) у Unity (поворот за годинниковою стрілкою, якщо дивитися згори).
            rx = (float)(x * c + z * s);
            rz = (float)(-x * s + z * c);
        }
    }
}
