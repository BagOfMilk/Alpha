using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа того, як місто відповідає на гравця: люди, рада, тіри (Поправка №6).
    ///
    /// УСЕ ТУТ — ПЛЕЙСХОЛДЕР. Числа виставлені так, щоб механіка була
    /// видна в тестах і на плівці діб; справжні значення ставить харнес за
    /// ціллю §6.4: при хорошій грі тір 2 близько тридцятої доби, тір 3 близько
    /// шістдесятої.
    /// </summary>
    [Serializable]
    public sealed class CityBalance
    {
        // ---- Люди приходять (§6.3) ----

        /// <summary>Природний приріст: одна людина раз на стільки діб.</summary>
        public int NaturalGrowthEveryDays = 3;

        /// <summary>Таверна: стільки людей за добу понад природний приріст.</summary>
        public int TavernArrivalsPerDay = 1;

        /// <summary>Прийом переселенців рішенням ради: скільки приходить.</summary>
        public int SettlersPerOrder = 10;

        /// <summary>Ціна прийому в їжі: нові роти треба годувати.</summary>
        public int SettlersFoodCost = 30;

        /// <summary>
        /// Відкат прийому. Без нього рада кликала людей через день, і плівка
        /// діб показала: хутір ставав селом на одинадцяту добу замість
        /// тридцятої — прийом був дешевою кнопкою, а не рішенням.
        /// </summary>
        public int SettlersCooldownDays = 7;

        // ---- Люди йдуть (§6.3) ----

        /// <summary>Стільки йде щодоби голоду.</summary>
        public int HungryDepartures = 2;

        /// <summary>Стільки йде за добу, поки громада пам'ятає кров.</summary>
        public int FearDepartures = 1;

        // ---- Рада (§6.2) ----

        /// <summary>Ціна облави золотом. Сила і відкат — у TensionBalance (RaidDelta, RaidCooldownDays).</summary>
        public int RaidGoldCost = 15;

        // ---- Тіри (§6.4): населення І ключова будівля ----

        /// <summary>Поріг населення для тіра 2, 3, 4 (індекс 0 — тір 2).</summary>
        public int[] TierPopulation = { 150, 250, 400 };
    }
}
