namespace FashionStore.Application.Configuration;

/// <summary>
/// Presentation settings for the invoice: the store's branding block shown on the
/// document (name, address, tax / registration numbers, contact details, logo) and
/// the numbering options (prefix, optional year-aware numbering).
/// </summary>
public sealed class InvoiceSettings
{
    public const string SectionName = "Invoice";
    public string CompanyName { get; init; } = string.Empty;
    public string CompanyAddress { get; init; } = string.Empty;
    public string CompanyEmail { get; init; } = string.Empty;
    public string CompanyPhone { get; init; } = string.Empty;
    public string TaxId { get; init; } = string.Empty;
    public string RegistrationNumber { get; init; } = string.Empty;
    public string LogoPath { get; init; } = string.Empty;
    public string InvoicePrefix { get; init; } = "INV-";

    /// <summary>When true the generated number includes the year (INV-2026-000001).</summary>
    public bool YearAware { get; init; } = true;
}
