using System.Collections.Generic;
using Game.Gameplay.UI;
using UnityEngine;

namespace Game.Gameplay.Characters
{
    /// <summary>
    /// Збирає персонажа з набору за <see cref="CharacterKitPlan"/>: бере скелет і зони тіла з FBX культури,
    /// а потрібні речі з FBX набору переносить на цей скелет за іменами кісток. Bindpose речі лишається
    /// від набору, тож річ сідає на кістки тіла іншої культури сама (так і задумано в Blender). Колір
    /// тканини — множник базового кольору матеріалу (<c>_BaseColor</c>), текстура нейтральна.
    /// ЩО вмикати вирішує план (чистий C#, під тестами); тут — лише застосування.
    /// </summary>
    public static class CharacterAssembler
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Повертає корінь моделі або null, якщо в бібліотеці бракує FBX.</summary>
        public static GameObject Build(CharacterKitLibrary library, CharacterKitPlan plan, Transform parent, int layer)
        {
            if (library == null || plan == null) return null;
            var bodyPrefab = library.Body(plan.BodyId);
            var kitPrefab = library.Kit(plan.KitId);
            if (bodyPrefab == null || kitPrefab == null)
            {
                Debug.LogWarning("[Kit] Немає FBX " + (bodyPrefab == null ? plan.BodyId : plan.KitId) + " — модель не зібрано.");
                return null;
            }

            var root = Object.Instantiate(bodyPrefab, parent, false);
            root.name = "Character(" + plan.BodyId + ")";
            var bones = new Dictionary<string, Transform>();
            CollectBones(root.transform, bones);

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string part = PartOf(smr.name);
                // Межі скінованих частин рахуються з бінд-пози, а тіло у FBX зсунуте від кореня (~2,6 м);
                // анімація ставить його в центр, і Unity відсікав голову як «поза кадром» (обличчя зникало).
                smr.updateWhenOffscreen = true;
                if (part.StartsWith("body_"))
                    smr.gameObject.SetActive(plan.ShowsBodyZone(part.Substring(5)));
                else if (!IsFacePart(part))
                    smr.gameObject.SetActive(false); // повне тіло-проксі й базова сітка — не в грі
            }

            var kit = Object.Instantiate(kitPrefab);
            foreach (var smr in kit.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string part = PartOf(smr.name);
                if (part.StartsWith("body_") || IsFacePart(part) || part == "body" || part == "basemesh") continue;
                string tint;
                if (!plan.Wants(part, out tint)) continue;
                if (!Rebind(smr, bones)) continue;
                smr.transform.SetParent(root.transform, false);
                smr.gameObject.SetActive(true);
                smr.updateWhenOffscreen = true;
                if (tint != null) Tint(smr, tint);
            }
            if (Application.isPlaying) Object.Destroy(kit); else Object.DestroyImmediate(kit); // лукбук редактора — поза Play
            SetLayer(root.transform, layer);
            return root;
        }

        /// <summary>
        /// Частини обличчя з тіла MPFB, що завжди лишаються: очі («low-poly»), брови, вії. Без брів і вій
        /// обличчя читалось манекеном (власник 07.10.2026).
        /// </summary>
        public static bool IsFacePart(string part)
        {
            string p = part.ToLowerInvariant();
            return p == "low-poly" || p.Contains("eyebrow") || p.Contains("eyelash");
        }

        /// <summary>«kit_m.shirt» → «shirt»; «body_m_latin.body_torso» → «body_torso».</summary>
        public static string PartOf(string objectName)
        {
            int dot = objectName.IndexOf('.');
            return dot >= 0 ? objectName.Substring(dot + 1) : objectName;
        }

        private static void CollectBones(Transform t, Dictionary<string, Transform> into)
        {
            if (!into.ContainsKey(t.name)) into[t.name] = t;
            for (int i = 0; i < t.childCount; i++) CollectBones(t.GetChild(i), into);
        }

        private static bool Rebind(SkinnedMeshRenderer smr, Dictionary<string, Transform> bones)
        {
            var old = smr.bones;
            var fresh = new Transform[old.Length];
            for (int i = 0; i < old.Length; i++)
            {
                Transform t;
                if (old[i] == null || !bones.TryGetValue(old[i].name, out t))
                {
                    Debug.LogWarning("[Kit] " + smr.name + ": кістки " + (old[i] != null ? old[i].name : "null") + " немає на скелеті тіла.");
                    return false;
                }
                fresh[i] = t;
            }
            smr.bones = fresh;
            Transform rootBone;
            if (smr.rootBone != null && bones.TryGetValue(smr.rootBone.name, out rootBone)) smr.rootBone = rootBone;
            return true;
        }

        private static void Tint(Renderer r, string hex)
        {
            float cr, cg, cb;
            CharacterKitPlan.ParseColor(hex, out cr, out cg, out cb);
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, new Color(cr, cg, cb, 1f));
            r.SetPropertyBlock(block);
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        /// <summary>
        /// Напрям «обличчям уперед» у світових координатах — з поточної пози: вектор від лівого стегна до
        /// правого, повернутий навколо вертикалі (права рука персонажа — +X, коли він дивиться в +Z). Стегна
        /// не міняються місцями ні в бінд-позі, ні в анімації. Перша версія брала «п'ята → носок» і
        /// вказувала назад: герой ходив задом наперед (власник, 07.10.2026).
        /// </summary>
        public static Vector3 Facing(GameObject model)
        {
            Transform left = null, right = null;
            var animator = model.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                left = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                right = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            }
            if (left == null || right == null)
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "thigh_l") left = t;
                    else if (t.name == "thigh_r") right = t;
                }
            if (left == null || right == null) return model.transform.forward;
            var across = right.position - left.position;
            across.y = 0f;
            if (across.sqrMagnitude < 1e-6f) return model.transform.forward;
            return Vector3.Cross(across.normalized, Vector3.up).normalized;
        }

        /// <summary>
        /// Напрям тіла в анімованій позі — за стегнами (<see cref="Facing"/>). Не через Animator.bodyRotation: його
        /// можна читати лише в OnAnimatorIK, деінде Unity попереджає й віддає ненадійне значення (тур 07.10.2026).
        /// Лукбук редактора звіряв обидва: у стійці — 0°, у ході стегна гойдаються в межах 4–9°.
        /// </summary>
        public static Vector3 BodyFacing(GameObject model) => Facing(model);

        /// <summary>
        /// Поворот навколо вертикалі, що переводить напрям <paramref name="from"/> у <paramref name="to"/>.
        /// <see cref="Quaternion.FromToRotation"/> для протилежних векторів бере довільну вісь і може
        /// перекинути модель догори ногами.
        /// </summary>
        public static Quaternion YawTo(Vector3 from, Vector3 to)
        {
            from.y = 0f; to.y = 0f;
            return Quaternion.Euler(0f, Vector3.SignedAngle(from, to, Vector3.up), 0f);
        }

        /// <summary>Доповернути модель навколо вертикалі так, щоб тіло дивилось у <paramref name="forward"/>; кут доповороту.</summary>
        public static float AlignBody(GameObject model, Vector3 forward)
        {
            var body = BodyFacing(model);
            forward.y = 0f;
            float yaw = Vector3.SignedAngle(body, forward, Vector3.up);
            if (Mathf.Abs(yaw) > 0.5f) model.transform.Rotate(0f, yaw, 0f, Space.World);
            return yaw;
        }
    }
}
