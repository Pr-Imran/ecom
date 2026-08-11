namespace FashionStore.Application.DTOs.Invoices;

/// <summary>
/// One line item on an invoice. Every field is the immutable snapshot stored on
/// the order line (product name, SKU, colour, size, prices) — never re-read from
/// the live catalogue, so the invoice stays accurate after a product is renamed,
/// recoloured or removed.
/// </summary>
public sealed record InvoiceItemDto(
    Guid OrderItemId,
    string ProductName,
    string Sku,
    string? ColourName,
    string? ColourValue,
    string? SizeName,
    string? ImageUrl,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    int Quantity,
    decimal LineTotal);

/// <summary>An immutable billing or shipping address snapshot rendered on the invoice.</summary>
public sealed record InvoiceAddressDto(
    string RecipientName,
    string? Phone,
    string AddressLine1,
    string? AddressLine2,
    string? Area,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? DeliveryInstructions);

/// <summary>One recorded email-send of an invoice PDF.</summary>
public sealed record InvoiceSendLogDto(
    Guid SendLogId,
    string SentTo,
    string Subject,
    bool Succeeded,
    string? ErrorMessage,
    DateTime SentAtUtc);

/// <summary>
/// A reference to a return or refund recorded against the order, shown on the
/// invoice so the customer can reconcile money that has been returned.
/// </summary>
public sealed record InvoiceRefundReferenceDto(
    string ProviderRefundId,
    string Currency,
    decimal Amount,
    DateTime CreatedAtUtc);

/// <summary>
/// The full invoice document built entirely from the order's immutable snapshots.
/// It carries everything the HTML view and the PDF generator need: branding inputs
/// are resolved separately from <see cref="FashionStore.Application.Configuration.InvoiceSettings"/>.
/// </summary>
public sealed record InvoiceDto(
    Guid InvoiceId,
    Guid OrderId,
    string InvoiceNumber,
    string PublicOrderNumber,
    int Version,
    DateTime IssueDateUtc,
    string Currency,
    decimal Subtotal,
    decimal ProductDiscount,
    decimal CouponDiscount,
    decimal ShippingCharge,
    decimal Tax,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal OutstandingAmount,
    decimal RefundedAmount,
    string Status,
    DateTime GeneratedAtUtc,
    DateTime? SentAtUtc,
    bool IsGuest,
    string? CustomerName,
    string? GuestEmail,
    string? GuestPhone,
    string? PaymentMethodCode,
    string PaymentStatus,
    string? ShippingMethodName,
    string? TrackingNumber,
    string? CarrierCode,
    string? TrackingUrl,
    IReadOnlyList<InvoiceItemDto> Items,
    InvoiceAddressDto? BillingAddress,
    InvoiceAddressDto? ShippingAddress,
    IReadOnlyList<string> Notes,
    IReadOnlyList<InvoiceRefundReferenceDto> RefundReferences);

/// <summary>Outcome of an email-pdf action, including the persisted send-log id.</summary>
public sealed record InvoiceEmailResult(
    bool Success,
    string? ErrorMessage,
    Guid? SendLogId,
    string? SentTo);
