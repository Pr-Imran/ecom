using FashionStore.Application.DTOs.Payments;
using FashionStore.Domain.Enums;

namespace FashionStore.Application.DTOs.Orders;

/// <summary>
/// Customer-facing query for the order list screen. Search matches the public order
/// number or any line's product name; status filters the order lifecycle state.
/// Pagination is page/page-size based with server-computed totals.
/// </summary>
public sealed record CustomerOrderQueryRequest(
    string? Search,
    OrderStatus? Status,
    int Page = 1,
    int PageSize = 10);

/// <summary>
/// A compact order card shown in the customer order list. The thumbnail and product
/// name come from the immutable order line snapshots so the card renders correctly
/// even if the underlying product has changed.
/// </summary>
public sealed record CustomerOrderListItemDto(
    Guid OrderId,
    string PublicOrderNumber,
    string OrderStatus,
    string PaymentStatus,
    string FulfilmentStatus,
    string Currency,
    decimal GrandTotal,
    int ItemCount,
    string? ThumbnailUrl,
    string FirstItemName,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    DateTime? CancelledAtUtc);

public sealed record CustomerOrderListResultDto(
    IReadOnlyList<CustomerOrderListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasMore,
    OrderStatus? AppliedStatus,
    string? AppliedSearch);

/// <summary>
/// A single entry in an order's lifecycle timeline. Every transition is recorded at
/// placement and by the cancellation/fulfilment services, so the timeline is a full
/// audit trail of who changed what and when.
/// </summary>
public sealed record OrderTimelineEntryDto(
    int Sequence,
    string FromStatus,
    string ToStatus,
    string? Note,
    string? Actor,
    DateTime CreatedAtUtc);

/// <summary>
/// Immutable delivery snapshot rendered on the order detail screen.
/// </summary>
public sealed record OrderDeliveryInfoDto(
    string? ShippingMethodName,
    string? RecipientName,
    string? Phone,
    string AddressLine1,
    string? AddressLine2,
    string? Area,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? DeliveryInstructions);

/// <summary>
/// Full customer order detail rendered from the stored snapshots, plus the payment
/// status (when a payment has been initiated) and the lifecycle timeline.
/// </summary>
public sealed record OrderDetailDto(
    Guid OrderId,
    string PublicOrderNumber,
    string? InvoiceNumber,
    bool IsGuest,
    string? CustomerName,
    string? GuestEmail,
    string? GuestPhone,
    string Currency,
    decimal Subtotal,
    decimal ProductDiscount,
    decimal CouponDiscount,
    decimal ShippingCharge,
    decimal Tax,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal RefundedAmount,
    string OrderStatus,
    string PaymentStatus,
    string FulfilmentStatus,
    string? PaymentMethodCode,
    string? ShippingMethodName,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? CancelledAtUtc,
    string? CancelledReasonCode,
    bool CanCancel,
    IReadOnlyList<OrderItemSummaryDto> Items,
    OrderAddressSummaryDto? ShippingAddress,
    OrderAddressSummaryDto? BillingAddress,
    OrderDeliveryInfoDto? Delivery,
    IReadOnlyList<OrderTimelineEntryDto> Timeline,
    PaymentStatusDto? Payment);

/// <summary>
/// The outcome of verifying a guest order lookup. A token is only issued when the
/// public order number matches the email captured at checkout; the token is signed
/// and short-lived so an order number alone is never enough to view an order.
/// </summary>
public sealed record GuestOrderLookupResult(
    bool Success,
    string? Token,
    string? OrderNumber,
    string? ErrorMessage);

/// <summary>
/// The outcome of a customer-initiated cancellation. Business rules (only placed /
/// confirmed, unpaid orders can be cancelled) are enforced by the service.
/// </summary>
public sealed record OrderCancellationResult(bool Success, string? Message);

/// <summary>
/// A single "buy again" line. The variant availability is resolved against the live
/// catalogue so an unavailable or deleted variant is reported instead of silently
/// dropped.
/// </summary>
public sealed record BuyAgainItemDto(
    Guid? ProductId,
    Guid? VariantId,
    string ProductName,
    string Sku,
    int Quantity,
    bool IsAvailable,
    string? UnavailableReason);
