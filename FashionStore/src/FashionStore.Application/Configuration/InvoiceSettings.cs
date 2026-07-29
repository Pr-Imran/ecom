namespace FashionStore.Application.Configuration;

public sealed class InvoiceSettings
{
    public const string SectionName = "Invoice";
    public string CompanyName { get; init; } = string.Empty;
    public string CompanyAddress { get; init; } = string.Empty;
    public string TaxId { get; init; } = string.Empty;
    public string LogoPath { get; init; } = string.Empty;
    public string InvoicePrefix { get; init; } = "INV-";
}
