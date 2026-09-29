using System;
using System.Collections.Generic;

namespace Game.Gameplay.Walk
{
    /// <summary>Що можна вибрати в селі (docs/UX_DESIGN.md §4.1).</summary>
    public enum PickKind
    {
        Building = 0,
        Plot,
        Landmark,
        Station,
        Exit
    }

    /// <summary>
    /// Ціль вибору: ідентифікатор місця і його межі у світі (осі вирівняні — як
    /// <c>Renderer.bounds</c> у рушії).
    /// </summary>
    public struct Pickable
    {
        public string Id;
        public PickKind Kind;
        public float MinX, MinY, MinZ;
        public float MaxX, MaxY, MaxZ;

        public Pickable(string id, PickKind kind, float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            Id = id;
            Kind = kind;
            MinX = Math.Min(minX, maxX); MaxX = Math.Max(minX, maxX);
            MinY = Math.Min(minY, maxY); MaxY = Math.Max(minY, maxY);
            MinZ = Math.Min(minZ, maxZ); MaxZ = Math.Max(minZ, maxZ);
        }
    }

    /// <summary>
    /// Що під курсором (UX-07): промінь камери проти меж будівель, ділянок і
    /// орієнтирів — чиста математика без рушія й без фізики (модуль фізики в
    /// проєкті не підключений, а межі перевіряються headless). Клік по даху
    /// влучає в будівлю, а не в землю за нею — раніше <c>HeroWalker</c> бачив
    /// лише площину землі (docs/UX_DESIGN.md §2, пункт 17). Найближча ціль
    /// уздовж променя виграє; при рівній відстані — та, що раніше в списку
    /// (детерміновано).
    /// </summary>
    public static class WorldPicker
    {
        /// <summary>Ідентифікатор найближчої цілі на промені або null.</summary>
        public static string Pick(float ox, float oy, float oz, float dx, float dy, float dz, IReadOnlyList<Pickable> targets)
        {
            float best;
            int index = PickIndex(ox, oy, oz, dx, dy, dz, targets, out best);
            return index >= 0 ? targets[index].Id : null;
        }

        /// <summary>Індекс найближчої цілі на промені (−1 — промах) і відстань до неї в одиницях напрямку.</summary>
        public static int PickIndex(float ox, float oy, float oz, float dx, float dy, float dz,
            IReadOnlyList<Pickable> targets, out float distance)
        {
            distance = float.PositiveInfinity;
            int best = -1;
            if (targets == null) return best;
            for (int i = 0; i < targets.Count; i++)
            {
                float t;
                if (!Hit(ox, oy, oz, dx, dy, dz, targets[i], out t)) continue;
                if (t < distance)
                {
                    distance = t;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Перетин променя з коробкою методом плит; <paramref name="t"/> — вхід у коробку (0, якщо початок усередині).</summary>
        public static bool Hit(float ox, float oy, float oz, float dx, float dy, float dz, Pickable box, out float t)
        {
            float tMin = 0f;
            float tMax = float.PositiveInfinity;
            t = 0f;
            if (!Slab(ox, dx, box.MinX, box.MaxX, ref tMin, ref tMax)) return false;
            if (!Slab(oy, dy, box.MinY, box.MaxY, ref tMin, ref tMax)) return false;
            if (!Slab(oz, dz, box.MinZ, box.MaxZ, ref tMin, ref tMax)) return false;
            t = tMin;
            return true;
        }

        private static bool Slab(float origin, float dir, float min, float max, ref float tMin, ref float tMax)
        {
            if (Math.Abs(dir) < 1e-8f)
                return origin >= min && origin <= max;
            float inv = 1f / dir;
            float t1 = (min - origin) * inv;
            float t2 = (max - origin) * inv;
            if (t1 > t2)
            {
                float tmp = t1; t1 = t2; t2 = tmp;
            }
            if (t1 > tMin) tMin = t1;
            if (t2 < tMax) tMax = t2;
            return tMin <= tMax;
        }
    }
}
