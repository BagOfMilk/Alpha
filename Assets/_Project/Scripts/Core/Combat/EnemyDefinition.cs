using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>Тактична роль ворога. База-4 для зрізу; розширення [ПІЗНІШЕ].</summary>
    public enum EnemyRole
    {
        Tank = 0,       // Громила: тягне фокус, ближня загроза — потрібен Шред/пробиття
        Skirmisher = 1, // Застрільщик: дальній ДПС з укриття — рви LOS/зближуйся
        Controller = 2, // Контролер: вішає стани — пріоритетна ціль
        Breacher = 3    // Прорив: ривок у ближній, ламає позицію
    }

    /// <summary>Сімейство ворога: та сама роль в іншому сімействі = інший пазл.</summary>
    public enum EnemyFamily
    {
        Human = 0,
        Mutant = 1,
        Robot = 2
    }

    /// <summary>
    /// Модульна збірка ворога: роль × сімейство × профіль резист/вразливість ×
    /// зброя (пізніше + здібності із загального пулу). Чисті дані; в Unity
    /// обгорнеться ScriptableObject. Складність масштабується ролями/профілями,
    /// НЕ роздуванням HP. Вороги симетричні гравцю за правилами.
    /// </summary>
    [Serializable]
    public sealed class EnemyDefinition
    {
        public string Id;
        public string DisplayName;
        public EnemyRole Role;
        public EnemyFamily Family;

        // Стати напряму (у ворогів немає атрибутів — їхні «похідні» задані руками).
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

        /// <summary>Здібності із загального з гравцем пулу (симетрія, гейтів скіла у ворогів немає).</summary>
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
