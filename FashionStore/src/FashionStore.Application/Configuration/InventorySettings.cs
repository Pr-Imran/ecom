namespace FashionStore.Application.Configuration;

public sealed class InventorySettings
{
    public const string SectionName = "Inventory";
    public int DefaultReservationExpirationMinutes { get; init; } = 30;
    public string ExpiredReservationReleaseCron { get; init; } = "0 * * * *";
}
