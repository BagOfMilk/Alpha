using Game.Core.Items;

namespace Game.Core.Session.Views
{
    /// <summary>
    /// Пропозиція кузні Збройні (Поправка №19.2): що можна викувати, у який слот і за скільки.
    /// Ціна видна до кліку (Статут UI-02) — це не прихована шкала.
    /// </summary>
    public sealed class ForgeOfferView
    {
        public string ItemId;
        public EquipSlot Slot;
        /// <summary>Частина модульного набору, яку буде видно на моделі (для 3D-прев'ю).</summary>
        public string VisualKey;
        public bool TwoHanded;
        public int GoldCost;
        public int CraftCost;
        public bool ArmoryOpen;
        public bool Affordable;
    }

    /// <summary>Один слот «ляльки» (№19.3): що надіто і яку частину набору видно.</summary>
    public sealed class EquipSlotView
    {
        public EquipSlot Slot;
        public string ItemId;          // null — порожньо
        public string InstanceId;
        public string VisualKey;
        public bool BlockedByTwoHanded; // щит недоступний, поки в руках дворучна зброя
    }
}
