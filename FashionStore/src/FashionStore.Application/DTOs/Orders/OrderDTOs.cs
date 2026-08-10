using FashionStore.Application.DTOs.Checkout;

namespace FashionStore.Application.DTOs.Orders;

/// <summary>
/// Browser-supplied order placement request. Mirrors the checkout calculation
/// request but adds an idempotency key so a double click, refresh, retry or slow
/// network can never place the same order twice. Every price is still recomputed
/// server-side; only free-form destination fields, method ids, guest contact
/// details, the terms flag and the continuation token are accepted.
/// </summary>
public sealed record PlaceOrderRequest(
    string? GuestEmail,
    string? GuestPhone,
    CheckoutAddressInput? ShippingAddress,
    CheckoutAddressInput? BillingAddress,
    bool BillingSameAsShipping,
    Guid? ShippingMethodId,
    string? PaymentMethodCode,
    bool TermsAccepted,
    string? ContinuationToken,
    string? IdempotencyKey);

/// <summary>
/// Result of an order placement attempt. <see cref="IsDuplicate"/> is true when a
/// previous attempt with the same idempotency key already created the order, in
/// which case the existing order is returned so a retry never creates a second
/// order. On validation failure <see cref="Errors"/> carries the problems grouped
/// by field and no order is created.
/// </summary>
public sealed record PlaceOrderResult(
    bool Success,
    bool IsDuplicate,
    Guid? OrderId,
    string? OrderNumber,
    decimal GrandTotal,
    IReadOnlyList<CheckoutValidationError> Errors);

/// <summary>
/// Immutable order summary used on the mobile result screen. Financial fields are
/// the snapshots stored at placement time; items carry the persisted product,
/// colour, size, image and pricing snapshots so the summary renders correctly even
/// if the catalogue later changes.
/// </summary>
public sealed record OrderSummaryDto(
    Guid OrderId,
    string PublicOrderNumber,
    string? InvoiceNumber,
    string? UserId,
    string? GuestEmail,
    string? CustomerName,
    string Currency,
    decimal Subtotal,
    decimal ProductDiscount,
    decimal CouponDiscount,
    decimal ShippingCharge,
    decimal Tax,
    decimal GrandTotal,
    string OrderStatus,
    string PaymentStatus,
    string FulfilmentStatus,
    string? PaymentMethodCode,
    string? ShippingMethodName,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc,
    IReadOnlyList<OrderItemSummaryDto> Items,
    OrderAddressSummaryDto? ShippingAddress,
    OrderAddressSummaryDto? BillingAddress);

public sealed record OrderItemSummaryDto(
    Guid ProductId,
    Guid? ProductVariantId,
    string ProductName,
    string Slug,
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

public sealed record OrderAddressSummaryDto(
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
