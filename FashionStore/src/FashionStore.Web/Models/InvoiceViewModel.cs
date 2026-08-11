using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Invoices;

namespace FashionStore.Web.Models;

/// <summary>
/// View model for the invoice page (admin and customer). Carries the invoice
/// document plus the store branding block used to render the header and footer and
/// the recorded email-send history (admin only).
/// </summary>
public sealed class InvoiceViewModel
{
    public required InvoiceDto Invoice { get; init; }
    public required InvoiceSettings Branding { get; init; }
    public IReadOnlyList<InvoiceSendLogDto> SendHistory { get; init; } = Array.Empty<InvoiceSendLogDto>();
    public bool IsAdminView { get; init; }
    public string? GuestAccessToken { get; init; }
}
