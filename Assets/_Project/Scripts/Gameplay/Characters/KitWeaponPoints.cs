using UnityEngine;

namespace Game.Gameplay.Characters
{
    /// <summary>
    /// Точки жорсткої зброї постаті набору в просторі кисті, що її тримає: кінці плечей лука, руків'я, центр правої
    /// долоні (нею тягнуть тятиву) і дуло рушниці. Рахуються ОДИН раз зі скелета в позі спокою (одразу після збирання,
    /// до першого кадру анімації) тими самими формулами, якими зброю змодельовано в <c>tools/blender/alpha_wardrobe.py</c>
    /// (<c>_grip</c>, <c>_weapon_frame</c>, <c>_bow</c>, <c>_musket</c>): центр долоні = зап'ястя + передпліччя × 0,07;
    /// зброя виходить з кулака в бік великого пальця. Сітку не читаємо: моделі набору імпортовані без Read/Write.
    /// </summary>
    public sealed class KitWeaponPoints : MonoBehaviour
    {
        // Розміри — як у alpha_wardrobe.py (метри моделі).
        private const float Palm = 0.07f;
        private const float BowHalf = 0.62f, BowBack = 0.15f;
        private const float MusketReach = 0.96f, MusketBarrelUp = 0.08f;

        public Transform HandL, HandR;
        public bool HasBow, HasMusket;
        /// <summary>У просторі HandL: кінці плечей лука й руків'я (найдальша від тятиви точка).</summary>
        public Vector3 BowTipA, BowTipB, BowGrip;
        /// <summary>У просторі HandR: центр долоні (точка натягу) і дуло рушниці.</summary>
        public Vector3 PalmR, Muzzle;

        /// <summary>Зняти точки з моделі в позі спокою. false — скелета з потрібними кістками немає.</summary>
        public bool Capture(bool bow, bool musket)
        {
            Transform lowL = null, lowR = null, thumbL = null, thumbR = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "hand_l": HandL = t; break;
                    case "hand_r": HandR = t; break;
                    case "lowerarm_l": lowL = t; break;
                    case "lowerarm_r": lowR = t; break;
                    case "thumb_01_l": thumbL = t; break;
                    case "thumb_01_r": thumbR = t; break;
                }
            }
            if (HandL == null || HandR == null || lowL == null || lowR == null || thumbL == null || thumbR == null) return false;

            Frame(HandL, lowL, thumbL, out var foreL, out var upL);
            Frame(HandR, lowR, thumbR, out var foreR, out var upR);
            var gripL = foreL * Palm;
            BowTipA = gripL - foreL * BowBack + upL * BowHalf;
            BowTipB = gripL - foreL * BowBack - upL * BowHalf;
            BowGrip = gripL;
            PalmR = foreR * Palm;
            // Ствол = (кисть − бік)/√2, бік = великий палець × кисть у Blender. Unity — ліва система координат: той самий
            // бік тут — Cross(кисть, палець). Ствол на 0,08 над хватом (у бік великого пальця).
            var barrel = (foreR - Vector3.Cross(foreR, upR)).normalized;
            Muzzle = PalmR + barrel * MusketReach + upR * MusketBarrelUp;
            HasBow = bow;
            HasMusket = musket;
            return true;
        }

        /// <summary>Рамка хвату (передпліччя, великий палець) у просторі кисті — одиниці кисті = метри моделі.</summary>
        private static void Frame(Transform hand, Transform lower, Transform thumb, out Vector3 fore, out Vector3 up)
        {
            var f = (hand.position - lower.position).normalized;
            var th = thumb.position - hand.position;
            var u = (th - f * Vector3.Dot(th, f)).normalized;
            fore = hand.InverseTransformDirection(f).normalized;
            up = hand.InverseTransformDirection(u).normalized;
        }

        public Vector3 World(Transform hand, Vector3 local) => hand.TransformPoint(local);

        // ---------------------------------------------------------------- стріла (спільна для тятиви й польоту)

        private static Material _shaft, _head;

        /// <summary>Стріла довжиною <paramref name="length"/> (одиниці батька) уздовж +Z, вістря — на +Z.</summary>
        public static GameObject CreateArrow(Transform parent, float length)
        {
            var root = new GameObject("arrow");
            root.transform.SetParent(parent, false);
            float t = length * 0.012f;
            Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0f, length * 0.5f), new Vector3(t, length * 0.5f, t),
                 Quaternion.Euler(90f, 0f, 0f), Mat(ref _shaft, new Color(0.46f, 0.33f, 0.2f)));
            Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0f, length), new Vector3(t * 2.6f, t * 2.6f, t * 6f),
                 Quaternion.Euler(0f, 0f, 45f), Mat(ref _head, new Color(0.25f, 0.25f, 0.27f)));
            Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0f, length * 0.08f), new Vector3(t * 4f, t * 0.4f, length * 0.12f),
                 Quaternion.identity, Mat(ref _head, new Color(0.25f, 0.25f, 0.27f)));
            return root;
        }

        private static void Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Material Mat(ref Material cache, Color c)
        {
            if (cache != null) return cache;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            cache = new Material(shader != null ? shader : Shader.Find("Standard"));
            if (cache.HasProperty("_BaseColor")) cache.SetColor("_BaseColor", c);
            cache.color = c;
            return cache;
        }
    }
}
