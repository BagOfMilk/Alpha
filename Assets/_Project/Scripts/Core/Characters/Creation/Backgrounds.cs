using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Characters.Creation
{
    /// <summary>
    /// Один преset передісторії (R12/US-2.7): готовий набір атрибутів і базових
    /// скілів, а не бюджет очок. Поправка №5.9 скасовує повне поінт-бай
    /// створення (US-2.7) для основного темпу гри — тестова сборка показує
    /// саму механіку швидким екраном: preset вибирається, а не збирається по
    /// очку, як робив архівний <c>ProtagonistBuilder</c>.
    ///
    /// <c>DisplayNameKey</c> — ключ (R7): Core не знає українського тексту,
    /// сам текст живе в <c>UkrainianText</c> (E3), тут лише ключ і авторські
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
    /// Три преsety передісторії — порт ідей архівного <c>Background.cs</c> /
    /// <c>ProtagonistBuilder.cs</c> (архівна лінія, коміт <c>20b8dcf</c>) на нову
    /// модель персонажа (4 атрибути / 10 скілів, GDD Э2). Числа — ПЛЕЙСХОЛДЕР,
    /// як і решта балансу цієї сборки: кожен преset тримає ту саму суму очок
    /// (16 атрибутів, 13 скілів), що й заглушка протагоніста
    /// <see cref="Session.FirstHourWorld"/> ставила раніше самотужки.
    /// </summary>
    public static class Backgrounds
    {
        /// <summary>Вигнанець зі зброєю: більше Сили й Ближнього бою, менше Кмітливості.</summary>
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

        /// <summary>Мандрівний торговець: більше Кмітливості й Торгівлі, менше Сили.</summary>
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

        /// <summary>Учень знахарки: більше Волі й Медицини, менше Ловкості.</summary>
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
