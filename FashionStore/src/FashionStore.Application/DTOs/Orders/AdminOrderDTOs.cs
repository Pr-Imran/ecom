using FashionStore.Domain.Enums;

namespace FashionStore.Application.DTOs.Orders;

/// <summary>
/// Admin order list query. Every filter is optional; when omitted the full order
/// set is returned. Search matches order number, customer name, email, phone and
/// the provider transaction id recorded against the order's payment.
/// </summary>
public sealed record AdminOrderQueryRequest(
    string? Search,
    DateTime? DateFromUtc,
    DateTime? DateToUtc,
    OrderStatus? OrderStatus,
    PaymentStatus? PaymentStatus,
    FulfilmentStatus? FulfilmentStatus,
    Guid? ShippingMethodId,
    string? PaymentMethodCode,
    decimal? MinAmount,
    decimal? MaxAmount,
    int Page,
    int PageSize,
    string? SortBy,
    string? SortDirection);

/// <summary>One row on the admin order list rendered as a mobile order card.</summary>
public sealed record AdminOrderListItemDto(
    Guid OrderId,
    string PublicOrderNumber,
    string? InvoiceNumber,
    bool IsGuest,
    string? CustomerName,
    string? GuestEmail,
    string? GuestPhone,
    string Currency,
    decimal GrandTotal,
    string OrderStatus,
    string PaymentStatus,
    string FulfilmentStatus,
    string? PaymentMethodCode,
    string? ShippingMethodName,
    int ItemCount,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc);

public sealed record AdminOrderListResultDto(
    IReadOnlyList<AdminOrderListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasMore,
    IReadOnlyList<AdminShippingMethodOptionDto> ShippingMethods,
    IReadOnlyList<string> PaymentMethods);

/// <summary>Shipping method option for the admin order filter sheet.</summary>
public sealed record AdminShippingMethodOptionDto(
    Guid ShippingMethodId,
    string ShippingMethodName);

/// <summary>
/// A product line on an order rendered in the admin detail. All fields are the
/// immutable snapshots captured at placement time, so the line stays readable
/// even when the original product or variant is later renamed or removed.
/// </summary>
public sealed record AdminOrderItemDto(
    Guid OrderItemId,
    Guid? ProductId,
    Guid? ProductVariantId,
    string ProductName,
    string ProductSlug,
    string Sku,
    string? ColourName,
    string? ColourValue,
    string? SizeName,
    string? ImageUrl,
    decimal UnitPrice,
    decimal? CompareAtPrice,
    decimal Discount,
    decimal Tax,
    int Quantity,
    decimal LineTotal);

/// <summary>Immutable address snapshot rendered in the admin detail.</summary>
public sealed record AdminOrderAddressDto(
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

/// <summary>A recorded order lifecycle transition (audit entry).</summary>
public sealed record AdminOrderHistoryEntryDto(
    OrderStatus? FromStatus,
    string ToStatus,
    string? Note,
    string? CreatedBy,
    DateTime CreatedAtUtc);

/// <summary>One immutable payment action recorded against the order's payment.</summary>
public sealed record AdminPaymentTransactionDto(
    string Type,
    string ProviderCode,
    string? ProviderTransactionId,
    bool Succeeded,
    string? ResultCode,
    string? ResultMessage,
    DateTime CreatedAtUtc);

/// <summary>One inventory stock movement that references this order.</summary>
public sealed record AdminInventoryHistoryEntryDto(
    string Sku,
    string? ProductName,
    int QuantityChange,
    int PreviousOnHand,
    int NewOnHand,
    int ReservedQuantityChange,
    int PreviousReserved,
    int NewReserved,
    string Reason,
    string? Notes,
    DateTime CreatedAtUtc);

/// <summary>An order note. Internal notes are staff-facing; customer notes are shown to the shopper.</summary>
public sealed record AdminNoteDto(
    Guid NoteId,
    string Note,
    bool IsInternal,
    string? CreatedBy,
    DateTime CreatedAtUtc);

/// <summary>
/// The full administrative order detail. Financial fields are the immutable
/// placement snapshots; sections cover customer, contact, addresses, product
/// lines, payment information and transaction history, shipping and tracking,
/// status history, inventory history and notes.
/// </summary>
public sealed record AdminOrderDetailDto(
    Guid OrderId,
    string PublicOrderNumber,
    string? InvoiceNumber,
    bool IsGuest,
    string? CustomerName,
    string? GuestEmail,
    string? GuestPhone,
    string? UserId,
    string Currency,
    decimal Subtotal,
    decimal ProductDiscount,
    decimal CouponDiscount,
    decimal ShippingCharge,
    decimal Tax,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal RefundedAmount,
    decimal AmountDue,
    string OrderStatus,
    string PaymentStatus,
    string FulfilmentStatus,
    string? PaymentMethodCode,
    Guid? ShippingMethodId,
    string? ShippingMethodCode,
    string? ShippingMethodName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? PaidAtUtc,
    DateTime? PackedAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? CancelledAtUtc,
    string? CancelledReasonCode,
    string? TrackingNumber,
    string? CarrierCode,
    string? TrackingUrl,
    IReadOnlyList<AdminOrderItemDto> Items,
    AdminOrderAddressDto? ShippingAddress,
    AdminOrderAddressDto? BillingAddress,
    IReadOnlyList<AdminOrderHistoryEntryDto> StatusHistory,
    IReadOnlyList<AdminPaymentTransactionDto> PaymentTransactions,
    IReadOnlyList<AdminInventoryHistoryEntryDto> InventoryHistory,
    IReadOnlyList<AdminNoteDto> InternalNotes,
    IReadOnlyList<AdminNoteDto> CustomerNotes,
    bool CanCancel,
    bool CanProcess,
    bool CanPack,
    bool CanShip,
    bool CanDeliver,
    bool CanComplete);

/// <summary>Result of an administrative order state transition or note operation.</summary>
public sealed record AdminOrderTransitionResult(
    bool Success,
    string? Error,
    string? OrderNumber,
    string? NewOrderStatus,
    string? NewFulfilmentStatus,
    string? TrackingNumber);

public sealed record AddOrderNoteRequest(
    string Note,
    bool IsInternal);

public sealed record AdminShipRequest(
    string? CarrierCode,
    string? TrackingNumber,
    string? TrackingUrl);

public sealed record AdminOrderExportResult(
    string FileName,
    string Csv);
