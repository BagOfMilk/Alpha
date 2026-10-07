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
        {
            CharacterKitLibrary kit;
            CharacterAnimLibrary anims;
            if (look == null || !TryFindLibraries(out kit, out anims)) return false;
            var plan = CharacterKitPlan.From(look, equipped);
            string sig = plan.Signature() + "|" + idleState + "|" + extraScale;
            if (sig == _signature && _model != null) return true;
            if (_model != null) Destroy(_model);
            _model = CharacterAssembler.Build(kit, plan, transform, layer);
            _signature = sig;
            if (_model == null) return false;
            _model.transform.localPosition = Vector3.zero;
            // Гуманоїдна анімація ставить тіло вздовж +Z кореня — анімованій постаті доповорот не потрібен
            // (він і розвертав героя задом наперед). Без кліпів — вирівнюємо бінд-позу за стегнами.
            bool animated = anims != null && anims.IsComplete;
            if (!animated)
                _model.transform.rotation = Quaternion.FromToRotation(CharacterAssembler.Facing(_model), transform.forward) * _model.transform.rotation;
            _model.transform.localScale = Vector3.one * (ArtScale.World * extraScale);

            if (anims != null && anims.IsComplete)
            {
                var style = AnimStateTable.StyleOf(WeaponOf(plan));
                var anim = _model.AddComponent<FigureAnimation>();
                anim.idle = anims.For(idleState, style) ?? anims.For(CharacterAnimState.Idle, style);
                anim.walk = anims.For(CharacterAnimState.Walk, style);
                anim.sprint = anims.For(CharacterAnimState.Run, style);
                anim.phase = phase;
                // OnEnable уже відпрацював без кліпів — перезапустити граф з ними.
                anim.enabled = false;
                anim.enabled = true;
            }
            return true;
        }

        /// <summary>Ключ зброї, яку видно на моделі (надіта чи впізнавана); null — без зброї.</summary>
        public static string WeaponOf(CharacterKitPlan plan)
        {
            foreach (var p in plan.Parts)
                if (p.Part.StartsWith("wpn_", System.StringComparison.Ordinal)) return p.Part;
            return null;
        }

        public void Clear()
        {
            if (_model != null) Destroy(_model);
            _model = null;
            _signature = null;
        }
    }
}
