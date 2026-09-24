using System.Collections.Generic;
using Game.Core.Stats;

namespace Game.Core.Items
{
    /// <summary>
    /// Унікальний ЧИСЛОВИЙ ефект іменного предмета (Епік 6.1, паттерн
    /// «стратегія»): те, що робить іменну річ яскравою поверх базових роллів,
    /// лишаючись у межах правила «один ефект — одна система» (гір крутить
    /// числа, US-6.2/18.2). У Unity обгортається ScriptableObject.
    ///
    /// Ефекти, що виходять ЗА межі агрегатора статів (наприклад, вплив на
    /// драбину передвісників) — НЕ сюди: див. <see cref="ItemWorldEffect"/>.
    /// </summary>
    public interface IItemEffect
    {
        string Name { get; }

        /// <summary>Додаткові модифікатори похідних (Source = Gear), поверх базових роллів.</summary>
        IEnumerable<StatModifier> ExtraModifiers();
    }

    /// <summary>
    /// Простий «сигнатурний» ефект іменного предмета: набір фікс-модифікаторів
    /// поверх бази. Готовий кірпичик контенту для ефектів «лише стати, але особливі».
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
