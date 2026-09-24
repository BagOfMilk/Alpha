using System.Collections.Generic;

namespace Game.Core.Items
{
    /// <summary>
    /// Дані про ефект іменного предмета, що сягає ЗА межі інвентаря/агрегатора
    /// статів — наприклад, «Ріг вивідника»: наступні два передвісники чуються
    /// чіткіше й раніше (полегшення драбини <c>WorldPulse</c>/<c>PressureTrack</c>,
    /// Core/World).
    ///
    /// Це СВІДОМИЙ ШОВ (порт §1.1 специфікації тестової збірки): пакет Items
    /// (B3) НЕ знає про Core/World і не застосовує цей ефект сам — він лише
    /// оголошує дані. Хто зводить гру в одне ціле (D1, фасад GameSession),
    /// читає <see cref="IWorldEffectSource.ActiveWorldEffects"/> з надітого
    /// спорядження і застосовує ефект до тієї системи, на яку він націлений
    /// (ключ <see cref="Key"/> — рядок контенту, а не тип: тим самим прийомом,
    /// яким сигнали й перевірки посилаються на теми/скіли рядком).
    /// </summary>
    public sealed class ItemWorldEffect
    {
        /// <summary>Ключ ефекту, напр. <c>"forewarn_boost"</c>.</summary>
        public readonly string Key;

        /// <summary>«Заряди» ефекту — напр. на скільки наступних застосувань його вистачить.</summary>
        public readonly int Charges;

        public ItemWorldEffect(string key, int charges)
        {
            Key = key;
            Charges = charges;
        }
    }

    /// <summary>
    /// Порт: у кого з надітого спорядження є активний ефект, що сягає за межі
    /// інвентаря. Держить лише дані (жодної логіки застосування) — саме тому
    /// цей інтерфейс і є швом, а не рефакторингом Core/World.
    /// </summary>
    public interface IWorldEffectSource
    {
        IEnumerable<ItemWorldEffect> ActiveWorldEffects();
    }
}
