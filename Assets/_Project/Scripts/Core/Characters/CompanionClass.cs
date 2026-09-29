using Game.Core.Stats;

namespace Game.Core.Characters
{
    /// <summary>
    /// Клас-архетип (Поправка №15.2, власник: «скіли будут по класам
    /// наприклад 4 класи як у XCOM, але у кожного персонажа характер різний
    /// під його культуру та оріджин» — на уточнення «клас = архетип»
    /// затверджено).
    ///
    /// 10 скілів (GDD Е2) НЕ скасовуються і не замінюються класами: клас лише
    /// задає стартові скіли, роль у бою і «рідний набір» прийомів (нижче,
    /// <see cref="CompanionClasses"/>). Характер персонажа і його підписний
    /// прийом епохи й далі йдуть від культури й першоджерела (Поправка №2/
    /// №12.9), а не від класу — двоє Рубак різного походження б'ються
    /// по-різному, хоча обидва Рубаки.
    ///
    /// Доступ до прийомів за класом у БОЮ (гейт "цей клас — ці кнопки") тут
    /// НЕ реалізований: це трек бою (Поправка №14, docs/COMBAT_V2.md). Це
    /// лише дані персонажа — клас видно на картці, як атрибут чи скіл
    /// (інваріант 3 не порушується: клас не прихована шкала).
    /// </summary>
    public enum CompanionClass
    {
        /// <summary>Рубака: ближній бій і залякування.</summary>
        Brawler = 0,

        /// <summary>Стрілець: дальній бій і виживання.</summary>
        Shooter = 1,

        /// <summary>Знахар: медицина і переконання.</summary>
        Healer = 2,

        /// <summary>Майстер: механіка, злом, торгівля.</summary>
        Crafter = 3
    }

    /// <summary>
    /// Дані архетипу класу: головні скіли й «рідний набір» прийомів.
    /// Tactics — спільний скіл, який не належить жодному класу (владарює
    /// нею MoveOrder — "ability.move_order", доступний усім однаково).
    /// </summary>
    public static class CompanionClasses
    {
        public static readonly CompanionClass[] All =
        {
            CompanionClass.Brawler, CompanionClass.Shooter, CompanionClass.Healer, CompanionClass.Crafter
        };

        /// <summary>
        /// Головні скіли класу — ними перевіряється охоронець «найвищий
        /// скіл іменного персонажа (крім Tactics) лежить серед головних
        /// скілів його класу» (ClassArchetypeTests).
        /// </summary>
        public static SkillType[] MainSkills(CompanionClass cls)
        {
            switch (cls)
            {
                case CompanionClass.Brawler: return new[] { SkillType.Melee, SkillType.Intimidate };
                case CompanionClass.Shooter: return new[] { SkillType.Ranged, SkillType.Survival };
                case CompanionClass.Healer: return new[] { SkillType.Medicine, SkillType.Persuade };
                case CompanionClass.Crafter: return new[] { SkillType.Mechanics, SkillType.Lockpick, SkillType.Trade };
                default: return new SkillType[0];
            }
        }

        /// <summary>
        /// «Рідний набір» прийомів — ЛИШЕ id здібностей, які реально існують
        /// у <c>Game.Core.Combat.DefaultCombatContent.AbilityCatalog()</c>.
        /// Картки з docs/ABILITIES.md, яких ще немає в коді, сюди НЕ входять
        /// (доступ за класом у бою не реалізований — це просто чесні дані):
        /// <list type="bullet">
        /// <item>Рубака: "Розлютити" (§4.2) і "Пробити" (§5, фінішер) —
        /// заплановані, не в коді.</item>
        /// <item>Стрілець: дозор — окрема механіка <c>CombatState.Overwatch()</c>
        /// (поле <c>CombatUnit.Overwatch</c>), не <c>AbilityDefinition</c> з
        /// власним id, тому теж не в цьому списку; "Тенета" (§5) —
        /// заплановане, не в коді.</item>
        /// <item>Знахар: "Підбадьорити" (§4.1) — заплановане; стабілізувати —
        /// лише рядок логу (<c>CombatLog.Stabilize</c>), не гейтована
        /// здібність.</item>
        /// <item>Майстер: барикади/двері (розділ 5, новий тип контенту карти)
        /// — заплановані, в коді немає нічого, тому порожній список.</item>
        /// </list>
        /// </summary>
        public static string[] NativeAbilityIds(CompanionClass cls)
        {
            switch (cls)
            {
                // Ривок у ближній контакт — Мelee-активка, вже в коді.
                case CompanionClass.Brawler: return new[] { "ability.lunge" };
                // Залп (Ranged) і пастка (Survival) — обидві вже в коді.
                case CompanionClass.Shooter: return new[] { "ability.volley", "ability.set_trap" };
                case CompanionClass.Healer: return new string[0];
                case CompanionClass.Crafter: return new string[0];
                default: return new string[0];
            }
        }

        /// <summary>
        /// Клас протагоніста за передісторією (Поправка №15.2, власник:
        /// «warrior -&gt; Рубака, trader -&gt; Майстер, healer -&gt; Знахар»).
        /// Невідомий/null id (SkipCreation, дефолт "warrior" — той самий
        /// відкат, що і <c>ArrivalsPool.Determine</c>) дає Рубаку.
        /// </summary>
        public static CompanionClass ForBackground(string backgroundId)
        {
            switch (backgroundId)
            {
                case "trader": return CompanionClass.Crafter;
                case "healer": return CompanionClass.Healer;
                case "warrior":
                default: return CompanionClass.Brawler;
            }
        }
    }
}
