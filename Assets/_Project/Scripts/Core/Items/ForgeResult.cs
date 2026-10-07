namespace Game.Core.Items
{
    /// <summary>Результат кування у Збройні (Поправка №19.2).</summary>
    public enum ForgeResult
    {
        Success = 0,
        UnknownItem = 1,   // такого в каталозі кузні немає
        ArmoryClosed = 2,  // Збройня ще не збудована
        CannotAfford = 3   // бракує золота чи сировини
    }
}
