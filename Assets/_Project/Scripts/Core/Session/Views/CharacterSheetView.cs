using System.Collections.Generic;
using Game.Core.Characters;

namespace Game.Core.Session.Views
{
    /// <summary>
    /// Повна картка персонажа (полірування, ціль 1 «Картка персонажа», owner
    /// feedback: "CharacterSheetView(companionId): 4 атрибути, 10 скілів,
    /// трейти, шрами, перки, рівень/xp, стан, лояльність БАНД, спорядження,
    /// ключові похідні бойові стати"). Усі числа тут — характеристики
    /// персонажа (R17: HP/AP/точність/захист — це "character stats, not
    /// hidden scales"), не приховані шкали міста — allow-list §4.9 розширено
    /// іменами полів цього файлу (ArchitectureGuardTests).
    ///
    /// Жодного Core-контентного <c>DisplayName</c> (R7): екрани резолвять
    /// AttributeKey/SkillKey/TraitId/PerkId/ScarId/WeaponId/ArmorId/
    /// AccessoryId через UkrainianText за ключем ("attr.&lt;key&gt;",
    /// "skill.&lt;key&gt;", "trait.&lt;id&gt;", "perk.&lt;id&gt;", "scar.&lt;id&gt;",
    /// "item.&lt;id&gt;") — той самий принцип, що вже тримає CompanionSummary.
    /// </summary>
    public sealed class CharacterSheetView
    {
        public string CompanionId;
        public int Level;
        public int Xp;
        public int XpToNextLevel;
        public CompanionStatus Status;

        /// <summary>null — не напарник (протагоніст або фольклорний NPC без арки), як у CompanionSummary.</summary>
        public LoyaltyBand? Loyalty;

        public IReadOnlyList<AttributeLineView> Attributes;
        public IReadOnlyList<SkillLineView> Skills;
        public IReadOnlyList<TraitLineView> Traits;

        /// <summary>Id шрамів (§"scar.&lt;id&gt;") — вечний трек, без модифікаторів у view (R17).</summary>
        public IReadOnlyList<string> ScarIds;

        /// <summary>Уже взяті перки.</summary>
        public IReadOnlyList<string> UnlockedPerkIds;

        /// <summary>Доступні для взяття зараз (скіл-гейт і пререквізити пройдені, ще не взято).</summary>
        public IReadOnlyList<PerkPreviewLineView> AvailablePerks;

        public CombatStatsView Combat;
        public EquipmentSheetView Equipment;
    }

    /// <summary>Один атрибут (шкала 1..10) — <c>AttributeKey</c> резолвиться через "attr.&lt;key&gt;".</summary>
    public sealed class AttributeLineView
    {
        public string AttributeKey;
        public int Score;
    }

    /// <summary>Один скіл (шкала 0..10) — <c>SkillKey</c> резолвиться через "skill.&lt;key&gt;".</summary>
    public sealed class SkillLineView
    {
        public string SkillKey;
        public int Score;
    }

    /// <summary>Один трейт: ім'я/ефект — за ключами "trait.&lt;id&gt;"/"trait.&lt;id&gt;.effect", знак — сам по собі текстовий.</summary>
    public sealed class TraitLineView
    {
        public string TraitId;
        public string Polarity; // "Virtue" | "Neutral" | "Vice" — ToString() enum'а, переклад — ui.trait.polarity.<lower>
    }

    /// <summary>
    /// Перк, доступний для взяття, але ще не взятий: <c>ReasonKey</c> —
    /// причина недоступності, коли <c>Available=false</c> (та ж легальність-з-причиною,
    /// що ScreenText.Legality для кнопок § ціль 2).
    /// </summary>
    public sealed class PerkPreviewLineView
    {
        public string PerkId;
        public bool Available;
        public string ReasonKey; // "ui.reason.perk.skill_too_low" | "ui.reason.perk.missing_prerequisite" | null
    }

    /// <summary>
    /// Ключові похідні бойові стати (owner: "HP, AP, initiative,
    /// accuracy/defence etc. — they are character stats, not hidden
    /// scales") — резолвлені тим самим агрегатором, що й бій (StatSnapshot),
    /// тож картка і арена ніколи не розходяться в числах.
    /// </summary>
    public sealed class CombatStatsView
    {
        // HpMax/ApMax (не Hp/Ap): картка читається ПОЗА боєм, живого пулу HP/AP
        // тут немає (він з'являється лише в CombatUnit на час бою — BattleView.Hp/Ap) —
        // це стеля, а не "поточне" значення, щоб не змішувати два різних поняття
        // під однаковим ім'ям.
        public int HpMax;
        public int ApMax;
        public int Initiative;
        public int Accuracy;
        public int Defense;
        public int Armor;
        public int CritChance; // відсоток (0..100) — округлене ціле, не сирий double 0..1
    }

    /// <summary>Три слоти екіпірування — id предмета або null (порожній слот), резолв через "item.&lt;id&gt;".</summary>
    public sealed class EquipmentSheetView
    {
        public string WeaponId;
        public string ArmorId;
        public string AccessoryId;
    }
}
