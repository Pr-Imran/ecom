namespace FashionStore.Application.Configuration;

public sealed class StoreSettings
{
    public const string SectionName = "Store";
    public string Name { get; init; } = "FashionStore";
    public string Tagline { get; init; } = string.Empty;
    public string ContactEmail { get; init; } = string.Empty;
    public string ContactPhone { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string CurrencyCode { get; init; } = "USD";
    public string CurrencySymbol { get; init; } = "$";
}
