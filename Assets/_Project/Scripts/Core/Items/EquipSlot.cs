namespace Game.Core.Items
{
    /// <summary>Слот экипировки. По одному предмету на слот (US-6.2: гир крутит числа).</summary>
    public enum EquipSlot
    {
        Weapon = 0,    // оружие — даёт WeaponDefinition в бой (named = уникальный прок)
        Armor = 1,     // броня — обычно +Armor/+MaxHp
        Accessory = 2  // аксессуар — точность/крит/бонусы к скилам и пр.
    }
}
