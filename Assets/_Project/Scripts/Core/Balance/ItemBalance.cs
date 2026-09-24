using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа предметів/крафту (Епік 6, пакет B3 тестової збірки).
    ///
    /// ЯКОРЬ: скільки коштує підняти рідкість гіра на Майстерні і на скільки
    /// «Ріг вивідника» полегшує драбину передвісників. ПЛЕЙСХОЛДЕРИ — як і
    /// решта чисел першого зрізу (§9.2 специфікації тестової збірки).
    /// </summary>
    [Serializable]
    public sealed class ItemBalance
    {
        /// <summary>Крафт-апгрейд рідкості: скільки Матеріалів (лише з вилазок, Поправка №4.1).</summary>
        public int CraftMaterialsCost = 3;

        /// <summary>Крафт-апгрейд рідкості: скільки Золота (валюта відряду, Поправка №4.1).</summary>
        public int CraftGoldCost = 5;

        /// <summary>
        /// «Ріг вивідника» (item.scout_horn, ефект «forewarn_boost»): на
        /// скільки наступних передвісників діє полегшення. Число — тут, а не
        /// в контенті: одна ручка, а не перепис DefaultItems при підкрутці.
        /// </summary>
        public int ScoutHornForewarnCharges = 2;
    }
}
