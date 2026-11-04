using System.Collections.Generic;
using Game.Gameplay.Characters;
using Game.Gameplay.UI;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Одягає постать рингу речами модульного набору (<c>kit_m</c>): сорочка, штани, взуття, зачіска, борода. Річ
    /// переноситься на скелет тіла за іменами кісток (як <see cref="CharacterAssembler"/>), зони тіла під одягом
    /// ховаються (дзеркало <see cref="CharacterKitPlan.Covers"/>). Рефері без справжнього одягу — тіло з фарбою форми —
    /// читався «слендерменом» (власник 03.11.2026: «Суддя виглядає як слендер мен»). Не лінтується (тека Stage/Unity).
    /// </summary>
    public static class RingDress
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        /// <summary>Річ і її колір; <c>stripes</c> — смугаста тканина (сорочка рефері).</summary>
        public struct Piece
        {
            public string Part;
            public Color Color;
            public bool Stripes;
            public Piece(string part, Color color, bool stripes = false) { Part = part; Color = color; Stripes = stripes; }
        }

        private static Texture2D _stripes;

        public static void Apply(GameObject model, GameObject kitPrefab, IList<Piece> pieces)
        {
            if (model == null || kitPrefab == null || pieces == null || pieces.Count == 0) return;
            var bones = new Dictionary<string, Transform>();
            Collect(model.transform, bones);

            var hidden = new HashSet<string>();
            foreach (var p in pieces)
            {
                string[] zones;
                if (CharacterKitPlan.Covers.TryGetValue(p.Part, out zones))
                    foreach (var z in zones) hidden.Add(z);
            }
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string part = CharacterAssembler.PartOf(smr.name);
                if (part.StartsWith("body_") && hidden.Contains(part.Substring(5))) smr.gameObject.SetActive(false);
            }

            var kit = Object.Instantiate(kitPrefab);
            foreach (var smr in kit.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string part = CharacterAssembler.PartOf(smr.name);
                int at = -1;
                for (int i = 0; i < pieces.Count; i++) if (pieces[i].Part == part) { at = i; break; }
                if (at < 0 || !Rebind(smr, bones)) continue;
                smr.transform.SetParent(model.transform, false);
                smr.gameObject.SetActive(true);
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                Paint(smr, pieces[at]);
            }
            Object.Destroy(kit);
        }

        // Тканина двобічна (одяг набору — оболонка з отворами, як у CharacterAssembler.TwoSided); колір — множник.
        private static void Paint(SkinnedMeshRenderer smr, Piece p)
        {
            var shared = smr.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == null) continue;
                var m = new Material(shared[i]) { name = shared[i].name + " (ринг)" };
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
                m.SetColor(BaseColorId, p.Color);
                if (p.Stripes) { m.SetTexture(BaseMapId, Stripes()); m.SetTextureScale(BaseMapId, new Vector2(9f, 1f)); }
                shared[i] = m;
            }
            smr.sharedMaterials = shared;
        }

        // Вертикальні смуги рефері: біла й чорна по половині періоду, плитка вздовж U.
        private static Texture2D Stripes()
        {
            if (_stripes != null) return _stripes;
            _stripes = new Texture2D(16, 2, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "ref_stripes" };
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 2; y++)
                    _stripes.SetPixel(x, y, x < 8 ? new Color(0.92f, 0.92f, 0.9f) : new Color(0.05f, 0.05f, 0.05f));
            _stripes.Apply(true);
            return _stripes;
        }

        private static void Collect(Transform t, Dictionary<string, Transform> into)
        {
            if (!into.ContainsKey(t.name)) into[t.name] = t;
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), into);
        }

        private static bool Rebind(SkinnedMeshRenderer smr, Dictionary<string, Transform> bones)
        {
            var old = smr.bones;
            var fresh = new Transform[old.Length];
            for (int i = 0; i < old.Length; i++)
            {
                Transform t;
                if (old[i] == null || !bones.TryGetValue(old[i].name, out t)) return false;
                fresh[i] = t;
            }
            smr.bones = fresh;
            Transform root;
            if (smr.rootBone != null && bones.TryGetValue(smr.rootBone.name, out root)) smr.rootBone = root;
            return true;
        }
    }
}
