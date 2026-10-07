using System;

namespace Game.Core.Balance
{
    /// <summary>
    /// Числа предметів/крафту (Епік 6, пакет B3 тестової збірки).
    ///
    /// ЯКІР: скільки коштує підняти рідкість гіра на Майстерні і на скільки
    /// «Ріг вивідника» полегшує драбину передвісників. ПЛЕЙСХОЛДЕРИ — як і
    /// решта чисел першого зрізу (§9.2 специфікації тестової збірки).
    /// </summary>
    [Serializable]
    public sealed class ItemBalance
    {
        /// <summary>Крафт-апгрейд рідкості: скільки крафтового компонента (лише ззовні, Поправка №12.5).</summary>
        public int CraftComponentCost = 3;

        /// <summary>Крафт-апгрейд рідкості: скільки Золота (валюта відряду, Поправка №4.1).</summary>
        public int CraftGoldCost = 5;

        /// <summary>
        /// «Ріг вивідника» (item.scout_horn, ефект «forewarn_boost»): на
        /// скільки наступних передвісників діє полегшення. Число — тут, а не
        /// в контенті: одна ручка, а не перепис DefaultItems при підкрутці.
        /// </summary>
        public int ScoutHornForewarnCharges = 2;

        /// <summary>
        /// D1b (seamsForD1 B3, шов "forewarn_boost"): сирий заряд <see cref="Game.Core.World.WorldPulse.BoostCharge"/>
        /// на ОДИН заряд рогу — множиться на <see cref="ScoutHornForewarnCharges"/>
        /// при знахідці й іде разово в накопичувач Тугара (єдиний
        /// Announces-накопичувач кампанії §3.3). Підібрано так, щоб разом
        /// (2 заряди) впритул наблизити заповнення до Forewarn2At
        /// (PulseBalance, 0.80 від Threshold=60 у TuharPressureSource) — той
        /// самий детермінований механізм "рівно одна ступінь за тік"
        /// (WorldPulse.Advance) сам розтягує це на "наступні 2 попередження
        /// раніше", а не на миттєвий стрибок на 2 ступені одразу.
        /// </summary>
        public int ScoutHornForewarnBoostPerCharge = 15;

        // ---- Кузня Збройні (Поправка №19.2): ціна базового гіра. ПЛЕЙСХОЛДЕРИ. ----
        // ЯКІР: зброя й броня тулуба — головні речі, дорожчі; дрібна броня — дешевша.
        // Споживач — GameSession.ForgeItem; сигнал — запис журналу «forge.made».

        /// <summary>Золото за зброю (одно- чи дворучну).</summary>
        public int ForgeGoldWeapon = 8;
        /// <summary>Сировина за зброю.</summary>
        public int ForgeCraftWeapon = 2;
        /// <summary>Золото за броню тулуба.</summary>
        public int ForgeGoldArmor = 10;
        /// <summary>Сировина за броню тулуба.</summary>
        public int ForgeCraftArmor = 3;
        /// <summary>Золото за шолом або щит.</summary>
        public int ForgeGoldHeadOrShield = 6;
        /// <summary>Сировина за шолом або щит.</summary>
        public int ForgeCraftHeadOrShield = 2;
        /// <summary>Золото за наручі, поножі, чоботи.</summary>
        public int ForgeGoldSmall = 4;
        /// <summary>Сировина за наручі, поножі, чоботи.</summary>
        public int ForgeCraftSmall = 1;

        /// <summary>Ціна кування предмета слоту: (золото, сировина).</summary>
        public void ForgeCost(Items.EquipSlot slot, out int gold, out int craft)
        {
            switch (slot)
            {
                case Items.EquipSlot.Weapon: gold = ForgeGoldWeapon; craft = ForgeCraftWeapon; break;
                case Items.EquipSlot.Armor: gold = ForgeGoldArmor; craft = ForgeCraftArmor; break;
                case Items.EquipSlot.Head:
                case Items.EquipSlot.Offhand: gold = ForgeGoldHeadOrShield; craft = ForgeCraftHeadOrShield; break;
                default: gold = ForgeGoldSmall; craft = ForgeCraftSmall; break;
            }
        }
    }
}
