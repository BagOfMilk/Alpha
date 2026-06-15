using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>
    /// Уникальный эффект именного предмета (Эпик 6.1, паттерн «стратегия»): то, что
    /// делает именную вещь яркой, а не просто набором статов. В Unity обернётся
    /// ScriptableObject (ItemEffect). Базовая форма — пассивные модификаторы поверх
    /// базовых роллов (сигнатурный бонус); реальные боевые проки именного ОРУЖИЯ
    /// идут через его WeaponDefinition (StatusOnHit/ShredOnHit), которое бой уже
    /// поддерживает — отдельный хук в конвейер появится с расширением.
    /// </summary>
    public interface IItemEffect
    {
        string Name { get; }

        /// <summary>Дополнительные модификаторы производных (Source = Gear), поверх базовых роллов.</summary>
        IEnumerable<StatModifier> ExtraModifiers();
    }

    /// <summary>
    /// Простой «сигнатурный» эффект именного: набор фикс-модификаторов поверх базы.
    /// Готовый кирпичик контента (для эффектов, которые «только статы, но особенные»).
    /// </summary>
    public sealed class SignatureEffect : IItemEffect
    {
        public string Name { get; }
        private readonly List<StatModifier> _mods;

        public SignatureEffect(string name, params StatModifier[] mods)
        {
            Name = name;
            _mods = new List<StatModifier>(mods ?? new StatModifier[0]);
        }

        public IEnumerable<StatModifier> ExtraModifiers() => _mods;
    }
}
