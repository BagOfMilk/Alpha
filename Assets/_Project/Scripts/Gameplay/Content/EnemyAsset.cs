using System;
using System.Collections.Generic;
using Game.Core.Combat;
using UnityEngine;

namespace Game.Gameplay.Content
{
    /// <summary>Резист/уязвимость per тип: словарь Core не сериализуется — DTO ассета.</summary>
    [Serializable]
    public struct ResistEntry
    {
        public DamageType type;
        public double multiplier;
    }

    /// <summary>
    /// SO-обёртка врага (US-18.1/3.14): роль × семейство × профиль × способности.
    /// Резисты — DTO-списком, способности — ссылками на ОБЩИЙ пул ассетов.
    /// </summary>
    [CreateAssetMenu(menuName = "Alpha/Content/Enemy", fileName = "enemy")]
    public sealed class EnemyAsset : ScriptableObject
    {
        public EnemyDefinition definition = new EnemyDefinition();

        [Tooltip("Резист/уязвимость: ×0.5–0.75 резист, ×1.25–1.5 уязвимость (US-3.12)")]
        public List<ResistEntry> resists = new List<ResistEntry>();

        [Tooltip("Способности из ОБЩЕГО с игроком пула (US-3.14) — ссылки на ассеты")]
        public List<AbilityAsset> abilities = new List<AbilityAsset>();

        /// <summary>
        /// Собирает СВЕЖУЮ копию определения: сериализованное поле definition НЕ
        /// мутируется (иначе заполненный Abilities утёк бы на диск при сохранении
        /// ассета — дрейф контента от read-вызова).
        /// </summary>
        public EnemyDefinition ToDefinition()
        {
            var profile = new ResistProfile();
            for (int i = 0; i < resists.Count; i++) profile.With(resists[i].type, resists[i].multiplier);

            var def = new EnemyDefinition(definition.Id, definition.DisplayName, definition.Role, definition.Family)
            {
                MaxHp = definition.MaxHp,
                MaxAp = definition.MaxAp,
                Accuracy = definition.Accuracy,
                Defense = definition.Defense,
                Initiative = definition.Initiative,
                CritChance = definition.CritChance,
                Armor = definition.Armor,
                Resolve = definition.Resolve,
                Resists = profile,
                Weapon = definition.Weapon
            };
            for (int i = 0; i < abilities.Count; i++)
                if (abilities[i] != null) def.Abilities.Add(abilities[i].ToDefinition());
            return def;
        }
    }
}
