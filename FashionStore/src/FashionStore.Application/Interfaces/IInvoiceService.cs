using FashionStore.Application.DTOs.Invoices;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Invoice generation, regeneration and delivery. Every invoice is produced from
/// the order's immutable snapshots; numbering is concurrency safe (the unique
/// invoice-number index is the retry guard) and never depends on browser input.
/// Ownership of the underlying order is always verified by the callers before an
/// invoice is handed to a customer.
/// </summary>
public interface IInvoiceService
{
    /// <summary>
    /// Returns the invoice for an order, generating it on first access. Re-running
    /// never duplicates a number: the existing invoice is returned unchanged.
    /// </summary>
    Task<InvoiceDto> EnsureForOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the invoice for an order by order id, or null when none exists yet.</summary>
    Task<InvoiceDto?> GetByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the invoice for an order by public order number, or null when none exists yet.</summary>
    Task<InvoiceDto?> GetByOrderNumberAsync(
        string publicOrderNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes the invoice for an order from the current order snapshots. The
    /// invoice number never changes; the document version is bumped so a refund or
    /// payment update taken since the last generation is reflected.
    /// </summary>
    Task<InvoiceDto> RegenerateAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emails the invoice PDF to the order's customer email and records the outcome
    /// in the invoice send history.
    /// </summary>
    Task<InvoiceEmailResult> EmailPdfAsync(
        Guid orderId,
        string? initiatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the recorded email-send history for an order's invoice, newest first.</summary>
    Task<IReadOnlyList<InvoiceSendLogDto>> GetSendHistoryAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    /// <summary>Builds the deterministic A4 PDF bytes for an invoice document.</summary>
    Task<byte[]> BuildPdfAsync(
        InvoiceDto invoice,
        CancellationToken cancellationToken = default);
}
