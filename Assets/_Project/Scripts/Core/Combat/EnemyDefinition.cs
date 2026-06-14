using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>Тактическая роль врага (US-3.13). База-4 для MVP; расширение [ПОЗЖЕ].</summary>
    public enum EnemyRole
    {
        Tank = 0,       // Громила: тянет фокус, ближняя угроза — нужен Шред/пробитие
        Skirmisher = 1, // Застрельщик: дальний ДПС из укрытия — рви LOS/сближайся
        Controller = 2, // Контролёр: вешает состояния — приоритетная цель
        Breacher = 3    // Прорыв: рывок в ближний, ломает позицию
    }

    /// <summary>Семейство врага (US-3.14): та же роль в другом семействе = другой пазл.</summary>
    public enum EnemyFamily
    {
        Human = 0,
        Mutant = 1,
        Robot = 2
    }

    /// <summary>
    /// Модульная сборка врага (US-3.14): роль × семейство × профиль резист/уязвимость ×
    /// оружие (позже + способности из общего пула). Чистые данные; в Unity обернётся
    /// ScriptableObject. Сложность масштабируется ролями/профилями, НЕ раздуванием HP
    /// (US-3.15). Враги симметричны игроку по правилам.
    /// </summary>
    [Serializable]
    public sealed class EnemyDefinition
    {
        public string Id;
        public string DisplayName;
        public EnemyRole Role;
        public EnemyFamily Family;

        // Статы напрямую (у врагов нет атрибутов — их «производные» заданы руками).
        public int MaxHp = 8;
        public int MaxAp = 8;
        public int Accuracy = 60;
        public int Defense = 0;
        public int Initiative = 5;
        public int CritChance = 5;
        public int Armor = 0;
        public int Resolve = 0;

        public ResistProfile Resists = new ResistProfile();
        public WeaponDefinition Weapon;

        /// <summary>Способности из общего с игроком пула (US-3.14: симметрия, гейтов скила у врагов нет).</summary>
        public List<AbilityDefinition> Abilities = new List<AbilityDefinition>();

        public EnemyDefinition() { }

        public EnemyDefinition(string id, string displayName, EnemyRole role, EnemyFamily family)
        {
            Id = id;
            DisplayName = displayName;
            Role = role;
            Family = family;
        }
    }
}
