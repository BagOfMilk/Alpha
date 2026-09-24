using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Creation
{
    /// <summary>
    /// Один преset предыстории (R12/US-2.7): готовый набор атрибутов и базовых
    /// скилов, а не бюджет очков. Поправка №5.9 отменяет полное поинт-бай
    /// создание (US-2.7) для основного темпа игры — тестовая сборка показывает
    /// саму механику быстрым экраном: preset выбирается, а не собирается по
    /// очку, как делал архивный <c>ProtagonistBuilder</c>.
    ///
    /// <c>DisplayNameKey</c> — ключ (R7): Core не знает украинского текста,
    /// сам текст живёт в <c>UkrainianText</c> (E3), тут только ключ и авторские
    /// числа.
    /// </summary>
    public sealed class BackgroundPreset
    {
        public string Id;
        public string DisplayNameKey;

        public AttributeSet Attributes = new AttributeSet();
        public SkillSet Skills = new SkillSet();
    }

    /// <summary>
    /// Три преsety предыстории — порт идей архивного <c>Background.cs</c> /
    /// <c>ProtagonistBuilder.cs</c> (архивная линия, коммит <c>20b8dcf</c>) на новую
    /// модель персонажа (4 атрибута / 10 скилов, GDD Э2). Числа — ПЛЕЙСХОЛДЕР,
    /// как и весь остальной баланс этой сборки: каждый преset держит ту же сумму
    /// очков (16 атрибутов, 13 скилов), что и заглушка протагониста
    /// <see cref="Session.FirstHourWorld"/> ставила раньше самостоятельно.
    /// </summary>
    public static class Backgrounds
    {
        /// <summary>Изгнанник с оружием: больше Силы и Ближнего боя, меньше Смекалки.</summary>
        public static BackgroundPreset Warrior()
        {
            var preset = new BackgroundPreset { Id = "warrior", DisplayNameKey = "background.warrior" };
            preset.Attributes[AttributeType.Strength] = 6;
            preset.Attributes[AttributeType.Agility] = 4;
            preset.Attributes[AttributeType.Wits] = 2;
            preset.Attributes[AttributeType.Will] = 4;

            preset.Skills[SkillType.Melee] = 6;
            preset.Skills[SkillType.Tactics] = 4;
            preset.Skills[SkillType.Survival] = 3;
            return preset;
        }

        /// <summary>Странствующий торговец: больше Смекалки и Торговли, меньше Силы.</summary>
        public static BackgroundPreset Trader()
        {
            var preset = new BackgroundPreset { Id = "trader", DisplayNameKey = "background.trader" };
            preset.Attributes[AttributeType.Strength] = 2;
            preset.Attributes[AttributeType.Agility] = 4;
            preset.Attributes[AttributeType.Wits] = 6;
            preset.Attributes[AttributeType.Will] = 4;

            preset.Skills[SkillType.Trade] = 6;
            preset.Skills[SkillType.Persuade] = 4;
            preset.Skills[SkillType.Survival] = 3;
            return preset;
        }

        /// <summary>Ученик знахарки: больше Воли и Медицины, меньше Ловкости.</summary>
        public static BackgroundPreset Healer()
        {
            var preset = new BackgroundPreset { Id = "healer", DisplayNameKey = "background.healer" };
            preset.Attributes[AttributeType.Strength] = 3;
            preset.Attributes[AttributeType.Agility] = 2;
            preset.Attributes[AttributeType.Wits] = 4;
            preset.Attributes[AttributeType.Will] = 7;

            preset.Skills[SkillType.Medicine] = 6;
            preset.Skills[SkillType.Persuade] = 4;
            preset.Skills[SkillType.Survival] = 3;
            return preset;
        }

        public static IReadOnlyList<BackgroundPreset> All() => new List<BackgroundPreset>
        {
            Warrior(), Trader(), Healer()
        };

        public static BackgroundPreset ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var all = All();
            for (int i = 0; i < all.Count; i++)
                if (all[i].Id == id) return all[i];
            return null;
        }
    }
}
