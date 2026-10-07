using System.Collections.Generic;
using Game.Gameplay.UI;
using UnityEngine;

namespace Game.Gameplay.Characters
{
    /// <summary>
    /// Кліпи анімацій набору (Quaternius UAL1/UAL2, CC0; Humanoid — ретаргет на скелет тіл набору). Поля
    /// заповнює редактор (<c>Editor/CharacterKitBuilder</c>); ЯКИЙ кліп для стану — <see cref="AnimStateTable"/>.
    /// </summary>
    public sealed class CharacterAnimLibrary : MonoBehaviour
    {
        public AnimationClip[] Clips = new AnimationClip[0];

        private Dictionary<string, AnimationClip> _byName;

        public AnimationClip Clip(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_byName == null)
            {
                _byName = new Dictionary<string, AnimationClip>();
                foreach (var c in Clips)
                    if (c != null) _byName[AnimStateTable.NormalizeClipName(c.name)] = c;
            }
            AnimationClip clip;
            return _byName.TryGetValue(name, out clip) ? clip : null;
        }

        public AnimationClip For(CharacterAnimState state, WeaponStyle style) => Clip(AnimStateTable.For(state, style).Clip);

        /// <summary>Набір бойових кліпів у форматі презентера бою.</summary>
        public BattleCharacterClips BattleClips(WeaponStyle style) => new BattleCharacterClips
        {
            Idle = For(CharacterAnimState.CombatIdle, style),
            Walk = For(CharacterAnimState.Walk, style),
            Sprint = For(CharacterAnimState.Run, style),
            AttackMelee = For(CharacterAnimState.Attack, style == WeaponStyle.Ranged ? WeaponStyle.Unarmed : style),
            HoldingShoot = For(CharacterAnimState.Attack, WeaponStyle.Ranged),
            Die = For(CharacterAnimState.Down, style),
            Interact = For(CharacterAnimState.Ability, style),
            Crouch = For(CharacterAnimState.CoverIdle, style)
        };

        public bool IsComplete => Clips != null && Clips.Length > 0;
    }
}
