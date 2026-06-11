using Game.Core.Balance;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// ScriptableObject-обёртка над <see cref="BalanceConfig"/> (GDD §18 US-18.3).
    /// Конфиг вложен напрямую как сериализуемое поле — все числа баланса видны и
    /// правятся в инспекторе (и в Play-режиме), а добавление нового поля в
    /// <see cref="BalanceConfig"/> автоматически появляется здесь без правки обёртки.
    ///
    /// Создание: ПКМ в Project → Create → Alpha → Balance Config. Несколько ассетов
    /// = пресеты сложности (Easy/Normal/Hard).
    /// </summary>
    [CreateAssetMenu(fileName = "BalanceConfig", menuName = "Alpha/Balance Config", order = 0)]
    public sealed class BalanceConfigAsset : ScriptableObject
    {
        [Tooltip("Все числа баланса. Стартовые значения — плейсхолдеры из Приложения Б GDD.")]
        public BalanceConfig config = new BalanceConfig();

        /// <summary>Чистый конфиг для игровой логики (копия — чтобы рантайм не мутировал ассет).</summary>
        public BalanceConfig ToConfig() => (config ?? new BalanceConfig()).Clone();
    }
}
