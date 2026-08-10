using FashionStore.Application.DTOs.Checkout;
using FashionStore.Application.DTOs.Orders;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Order placement and retrieval for the storefront. Placement is transactional
/// and idempotent: the idempotency key is validated first, the checkout is
/// recalculated server-side, stock is verified, the order (with immutable product
/// and address snapshots) is created, stock is reserved according to the payment
/// method and coupon usage is recorded, all inside a single transaction. A repeated
/// request with the same idempotency key returns the existing order instead of
/// creating a duplicate.
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// Places an order. The input carries the server-resolved cart and the free-form
    /// destination, method ids, guest contact details and terms; the engine re-runs
    /// the full server-side checkout calculation, refuses to place when the quoted
    /// totals are stale (the continuation token no longer matches) and commits the
    /// order, snapshots, stock reservation and coupon usage in one transaction.
    /// </summary>
    Task<PlaceOrderResult> PlaceOrderAsync(
        CheckoutCalculationInput input,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a stored order summary by its public order number for the mobile
    /// result screen. The snapshot fields make the summary safe to render even when
    /// the underlying products or addresses have changed.
    /// </summary>
    Task<OrderSummaryDto?> GetByPublicOrderNumberAsync(
        string publicOrderNumber,
        CancellationToken cancellationToken = default);
}
