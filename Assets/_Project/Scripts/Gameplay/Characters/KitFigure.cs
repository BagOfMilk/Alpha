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

        public void Clear()
        {
            if (_model != null) Destroy(_model);
            _model = null;
            _signature = null;
        }
    }
}
