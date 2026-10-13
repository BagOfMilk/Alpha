using System.Collections.Generic;
using Game.Core.Characters;
using Game.Gameplay.UI;
using Game.Gameplay.Visual;
using UnityEngine;

namespace Game.Gameplay.Characters
{
    /// <summary>
    /// Постать із модульного набору для села й бою (Поправка №19, трек V5–V6): модель за образом і надітим
    /// (<see cref="CharacterKitPlan"/> → <see cref="CharacterAssembler"/>), масштаб сцени
    /// (<see cref="ArtScale.World"/>) і <see cref="FigureAnimation"/> з кліпами UAL: «стоїть» — стан
    /// (робота на посту, бойова стійка), «іде», «біжить». Перебудовується лише коли змінився підпис плану.
    /// </summary>
    public sealed class KitFigure : MonoBehaviour
    {
        private string _signature;
        private GameObject _model;
        private int _alignIn; // кадрів до вирівнювання тіла за першою анімованою позою

        /// <summary>Доповорот тіла після першої анімованої пози, градуси (охоронець ходи, лукбук).</summary>
        public float AlignedYaw { get; private set; }

        public GameObject Model => _model;
        public FigureAnimation Animation => _model != null ? _model.GetComponent<FigureAnimation>() : null;

        /// <summary>Знайти бібліотеки набору в сцені (один об'єкт <c>CharacterKit</c>); null — набору немає.</summary>
        public static bool TryFindLibraries(out CharacterKitLibrary kit, out CharacterAnimLibrary anims)
        {
            kit = Object.FindFirstObjectByType<CharacterKitLibrary>();
            anims = Object.FindFirstObjectByType<CharacterAnimLibrary>();
            return kit != null && kit.IsComplete;
        }

        /// <summary>
        /// Показати постать. <paramref name="idleState"/> — що робить, стоячи (Idle, робота поста, бойова стійка);
        /// <paramref name="extraScale"/> — множник поверх масштабу сцени (бій тримає фігурки трохи більшими).
        /// </summary>
        public bool Show(Appearance look, IEnumerable<string> equipped, CharacterAnimState idleState, float extraScale, float phase, int layer)
            => Show(look, equipped, idleState, extraScale, phase, layer, false);

        /// <summary>
        /// <paramref name="brisk"/> — герой: звичайний рух підтюпцем (як у CRPG), з Shift — біг; жителі — хода й
        /// підтюпцем.
        /// </summary>
        public bool Show(Appearance look, IEnumerable<string> equipped, CharacterAnimState idleState, float extraScale, float phase, int layer, bool brisk)
            => Show(look, equipped, idleState, extraScale, phase, layer, brisk, false);

        /// <summary>
        /// <paramref name="armed"/> — бій: зброя і в руці, і за спиною, перемикає <see cref="SetWeaponDrawn"/>. Інакше (село)
        /// усе, що тримають у руці, — за спиною (<see cref="CharacterKitPlan.Stowed"/>).
        /// </summary>
        public bool Show(Appearance look, IEnumerable<string> equipped, CharacterAnimState idleState, float extraScale, float phase, int layer,
            bool brisk, bool armed)
        {
            CharacterKitLibrary kit;
            CharacterAnimLibrary anims;
            if (look == null || !TryFindLibraries(out kit, out anims)) return false;
            var full = CharacterKitPlan.From(look, equipped);
            var plan = armed ? full.WithStowedTwins() : full.Stowed();
            string sig = plan.Signature() + "|" + idleState + "|" + extraScale + "|" + brisk;
            if (sig == _signature && _model != null) return true;
            if (_model != null) Destroy(_model);
            DropFitMeshes();
            _model = CharacterAssembler.Build(kit, plan, transform, layer);
            _signature = sig;
            if (_model == null) return false;
            _model.transform.localPosition = Vector3.zero;
            // Напрям тіла не вгадуємо з бінд-пози (через вгадування герой ходив задом наперед — власник,
            // 07.10.2026): без кліпів вирівнюємо бінд-позу за стегнами одразу, з кліпами — за першою
            // анімованою позою (LateUpdate, коли аніматор уже обчислив її).
            bool animated = anims != null && anims.IsComplete;
            if (!animated)
                _model.transform.rotation = CharacterAssembler.YawTo(CharacterAssembler.Facing(_model), transform.forward) * _model.transform.rotation;
            _alignIn = animated ? 2 : 0;
            AlignedYaw = 0f;
            _model.transform.localScale = Vector3.one * (ArtScale.World * extraScale);

            _held.Clear();
            _stowed.Clear();
            foreach (var smr in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string part = CharacterAssembler.PartOf(smr.name);
                if (CharacterKitPlan.IsHeld(part)) _held.Add(smr.gameObject);
                else if (part.EndsWith(CharacterKitPlan.StowedSuffix, System.StringComparison.Ordinal)) _stowed.Add(smr.gameObject);
            }
            _drawn = null;
            if (armed) SetWeaponDrawn(true);
            else if (Resting(idleState))
                foreach (var go in _stowed) go.SetActive(false); // сидить чи навколішки — зброю відкладено (кінець списа йшов крізь лаву)

            if (anims != null && anims.IsComplete)
            {
                var style = AnimStateTable.StyleOf(WeaponOf(plan));
                var anim = _model.AddComponent<FigureAnimation>();
                anim.idle = anims.For(idleState, style) ?? anims.For(CharacterAnimState.Idle, style);
                anim.walk = anims.For(brisk ? CharacterAnimState.Run : CharacterAnimState.Walk, style);
                anim.sprint = anims.For(brisk ? CharacterAnimState.Sprint : CharacterAnimState.Run, style);
                anim.walkNatural = anim.walk != null ? AnimStateTable.NaturalSpeed(anim.walk.name) : 0f;
                anim.sprintNatural = anim.sprint != null ? AnimStateTable.NaturalSpeed(anim.sprint.name) : 0f;
                anim.phase = phase;
                // OnEnable уже відпрацював без кліпів — перезапустити граф з ними.
                anim.enabled = false;
                anim.enabled = true;
            }
            return true;
        }

        private void LateUpdate()
        {
            if (_alignIn <= 0 || _model == null) return;
            if (--_alignIn > 0) return;
            AlignedYaw = CharacterAssembler.AlignBody(_model, transform.forward);
            CenterBody();
            FitStowed();
            // Охоронець: постать, що й досі в бінд-позі, розкидана на метри (тіло й одяг авторовані в різних місцях
            // FBX) — анімація не стартувала, і в кадрі її не видно. Помилка в журнал — тур провалює ворота G1.
            var spread = new Bounds(_model.transform.position, Vector3.zero);
            foreach (var r in _model.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (r.enabled && r.gameObject.activeInHierarchy) spread.Encapsulate(r.bounds);
            float scale = Mathf.Max(1e-3f, _model.transform.lossyScale.y);
            if (Mathf.Max(spread.size.x, spread.size.z) / scale > 3f)
                Debug.LogError("[Постать] " + name + " у бінд-позі: частини розкидані на " +
                               (Mathf.Max(spread.size.x, spread.size.z) / scale).ToString("0.0") + " м — анімація не стартувала.");
        }

        /// <summary>
        /// Пози, в яких річ за спиною пройшла б крізь лаву, землю чи стегна (сидить, навколішки, нахилений над грядкою):
        /// її тоді відкладено — як у BG3 зброя зникає, коли постать сідає. Власник 08.10.2026: «Эквіп ніколи… Не скрізь
        /// руку чи тіло».
        /// </summary>
        public static bool Resting(CharacterAnimState state) =>
            state == CharacterAnimState.Sit || state == CharacterAnimState.WorkCouncil ||
            state == CharacterAnimState.WorkCraft || state == CharacterAnimState.WorkFarm;

        private readonly List<GameObject> _held = new List<GameObject>();
        private readonly List<GameObject> _stowed = new List<GameObject>();
        private bool? _drawn;

        /// <summary>Зброя (щит, посох) у руці чи за спиною — бій перемикає за станом (<see cref="AnimStateTable.HoldsWeapon"/>).</summary>
        public void SetWeaponDrawn(bool drawn)
        {
            if (_drawn == drawn) return;
            _drawn = drawn;
            foreach (var go in _held) if (go != null) go.SetActive(drawn);
            foreach (var go in _stowed) if (go != null) go.SetActive(!drawn);
        }

        /// <summary>Ключ зброї, яку видно на моделі (надіта чи впізнавана); null — без зброї.</summary>
        public static string WeaponOf(CharacterKitPlan plan) => plan.Weapon();

        /// <summary>
        /// Кліп, авторований не в початку координат (робота на грядці), зсував тіло від кореня — постать стояла не під
        /// своїм «!» і підписом, а за хатою (тур 13.10.2026: Максим на Фермах «зник»). Таз далі 25 см — тіло повертаємо.
        /// </summary>
        private void CenterBody()
        {
            var animator = _model.GetComponentInChildren<Animator>();
            var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips == null) return;
            float scale = Mathf.Max(1e-3f, _model.transform.lossyScale.y);
            var off = hips.position - transform.position;
            float hipsHeight = off.y / scale;
            off.y = 0f;
            // Таз нижче 25 см — тіло під землею (кліп із вихідною висотою кореня): піднімаємо до пози навколішки.
            float lift = hipsHeight < 0.25f ? (0.5f - hipsHeight) * scale : 0f;
            if (off.magnitude <= 0.25f * scale && lift <= 0f) return;
            _model.transform.position += Vector3.up * lift - off;
            Debug.Log("[Постать] " + name + ": тіло зсунуте кліпом на " + (off.magnitude / scale).ToString("0.00") + " м убік, таз на висоті " +
                      hipsHeight.ToString("0.00") + " м — повернуто на місце.");
        }

        // ---- зброя за спиною — до спини (плейтест 13.10.2026: «Меч отходит от персонажа») ----

        private readonly List<Mesh> _fitMeshes = new List<Mesh>();

        /// <summary>
        /// Річ за спиною авторована під одне тіло набору, а тіла культур різні за статурою — на тоншому вона висіла в повітрі.
        /// Крім того, у стійці верхній хребець зігнутий на ~33° (замір треку V 13.10.2026), і річ на ньому стояла з нахилом
        /// 20–28°: низ відходив від спини на 20–35 см. За першою анімованою позою міряємо спину (тіло й одяг) окремо у
        /// верхній і нижній половині речі, нахиляємо річ так, щоб обидва зазори зрівнялись, і притискаємо із зазором ~1 см.
        /// Річ тверда — скінінг їй не потрібен; сидить на нижньому хребці, який у ході й бігу гнеться найменше.
        /// </summary>
        private void FitStowed()
        {
            if (_stowed.Count == 0) return;
            var animator = _model.GetComponentInChildren<Animator>();
            var bone = animator != null && animator.isHuman
                ? (animator.GetBoneTransform(HumanBodyBones.Spine) ?? animator.GetBoneTransform(HumanBodyBones.Chest))
                : null;
            if (bone == null) return;
            float scale = Mathf.Max(1e-3f, _model.transform.lossyScale.y);
            var back = -transform.forward;
            var side = transform.right;

            var body = new List<Vector3>();
            foreach (var smr in _model.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!smr.enabled || _held.Contains(smr.gameObject) || _stowed.Contains(smr.gameObject)) continue;
                string part = CharacterAssembler.PartOf(smr.name);
                if (part.StartsWith("wpn_") || part.StartsWith("shield_")) continue;
                body.AddRange(Baked(smr, null));
            }
            if (body.Count == 0) return;

            for (int i = 0; i < _stowed.Count; i++)
            {
                var go = _stowed[i];
                var smr = go != null ? go.GetComponent<SkinnedMeshRenderer>() : null;
                if (smr == null) continue;
                var mesh = new Mesh { name = smr.name + "_fit" };
                var item = Baked(smr, mesh);
                if (item.Count == 0) { Destroy(mesh); continue; }

                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (var v in item) { minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y); }
                // Чотири смуги висоти; нахил — за двома крайніми, де під річчю є спина (верх лука стирчить над плечима).
                const int bands = 4;
                float band = (maxY - minY) / bands;
                int hi = -1, lo = -1;
                float gapTop = 0f, gapBottom = 0f, yTop = 0f, yBottom = 0f;
                Vector3 pivot = Vector3.zero;
                float side0 = -0.05f * scale; // від'ємний зазор — річ при боці (шабля, сагайдак на стегні), не за спиною
                for (int k = bands - 1; k >= 0; k--)
                {
                    float g;
                    Vector3 at;
                    if (!Gap(item, body, minY + band * k, minY + band * (k + 1), back, side, scale, out g, out at) || g < side0) continue;
                    if (hi < 0) { hi = k; gapTop = g; pivot = at; yTop = minY + band * (k + 0.5f); }
                    lo = k; gapBottom = g; yBottom = minY + band * (k + 0.5f);
                }
                if (hi < 0) { Destroy(mesh); continue; }

                var fit = new GameObject(smr.name);
                fit.layer = go.layer;
                fit.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                fit.transform.localScale = smr.transform.lossyScale; // поки без батька: локальний = світовий
                float tilt = 0f;
                if (lo != hi)
                {
                    // Поворот навколо бічної осі через верхню точку дотику: низ іде до спини на різницю зазорів.
                    tilt = Mathf.Clamp(-Mathf.Atan2(gapBottom - gapTop, Mathf.Max(1e-3f, yTop - yBottom)) * Mathf.Rad2Deg, -35f, 35f);
                    fit.transform.RotateAround(pivot, side, tilt);
                }
                float gap = gapTop;
                float shift = Mathf.Clamp(gap - 0.01f * scale, -0.02f * scale, 0.3f * scale);
                fit.transform.position -= back * shift;
                fit.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = fit.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = smr.sharedMaterials;
                renderer.shadowCastingMode = smr.shadowCastingMode;
                renderer.enabled = smr.enabled; // розмова могла сховати річ (DialogueStage.Sheathe)
                fit.transform.SetParent(bone, true);
                fit.SetActive(go.activeSelf);
                go.SetActive(false);
                _stowed[i] = fit;
                _fitMeshes.Add(mesh);
                if (Mathf.Abs(gap) > 0.03f * scale || Mathf.Abs(tilt) > 5f)
                    Debug.Log("[Постать] " + name + ": " + smr.name + " за спиною — зазор угорі " + (gapTop / scale).ToString("0.00") +
                              " м, унизу " + (gapBottom / scale).ToString("0.00") + " м, нахил " + tilt.ToString("0") + "° — притиснуто до спини.");
            }
        }

        /// <summary>
        /// Зазор між спиною й річчю в смузі висот [<paramref name="y0"/>, <paramref name="y1"/>]: найближча до спини точка
        /// речі мінус найдальша назад точка тіла під нею. <paramref name="pivot"/> — та точка речі.
        /// </summary>
        private static bool Gap(List<Vector3> item, List<Vector3> body, float y0, float y1, Vector3 back, Vector3 side, float scale,
            out float gap, out Vector3 pivot)
        {
            gap = 0f;
            pivot = Vector3.zero;
            float near = float.MaxValue, minS = float.MaxValue, maxS = float.MinValue;
            foreach (var v in item)
            {
                if (v.y < y0 || v.y > y1) continue;
                float sd = Vector3.Dot(v, side);
                minS = Mathf.Min(minS, sd); maxS = Mathf.Max(maxS, sd);
                float b = Vector3.Dot(v, back);
                if (b < near) { near = b; pivot = v; }
            }
            if (near == float.MaxValue) return false;
            float spine = float.MinValue;
            foreach (var v in body)
            {
                if (v.y < y0 || v.y > y1) continue;
                float sd = Vector3.Dot(v, side);
                if (sd < minS - 0.05f * scale || sd > maxS + 0.05f * scale) continue;
                spine = Mathf.Max(spine, Vector3.Dot(v, back));
            }
            if (spine == float.MinValue) return false;
            gap = near - spine;
            return true;
        }

        /// <summary>Вершини поточної пози у світі; <paramref name="into"/> — куди запекти (null — тимчасова сітка).</summary>
        private static List<Vector3> Baked(SkinnedMeshRenderer smr, Mesh into)
        {
            var mesh = into ?? new Mesh();
            // Без масштабу в сітці, у світ — повною матрицею рендерера: масштаб постаті (село 0,4, бій 1,35) живе в
            // батьках, і запікання «з масштабом» його не враховувало — річ виходила в 2,5 раза більшою (трек V, 13.10.2026).
            smr.BakeMesh(mesh, false);
            var verts = mesh.vertices;
            var list = new List<Vector3>(verts.Length);
            var m = smr.transform.localToWorldMatrix;
            foreach (var v in verts) list.Add(m.MultiplyPoint3x4(v));
            if (into == null) Destroy(mesh);
            return list;
        }

        private void DropFitMeshes()
        {
            foreach (var m in _fitMeshes) if (m != null) Destroy(m);
            _fitMeshes.Clear();
        }

        private void OnDestroy() => DropFitMeshes();

        public void Clear()
        {
            DropFitMeshes();
            if (_model != null) Destroy(_model);
            _model = null;
            _signature = null;
        }
    }
}
