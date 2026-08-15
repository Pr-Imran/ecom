using FashionStore.Application.DTOs.Orders;
using FashionStore.Domain.Enums;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// The customer order panel: order list, search and status filter, order detail with
/// lifecycle timeline, secure guest lookup, cancellation and buy-again. Every read
/// is scoped to the caller's identity (the authenticated user id or a verified guest
/// token); ownership is enforced inside the service so a caller can never read or
/// mutate another customer's order.
/// </summary>
public interface ICustomerOrderService
{
    /// <summary>
    /// Lists the orders belonging to a signed-in customer, with optional search
    /// (order number or product name) and lifecycle-status filter, paginated newest
    /// first.
    /// </summary>
    Task<CustomerOrderListResultDto> GetCustomerOrdersAsync(
        string userId,
        CustomerOrderQueryRequest query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a full order detail for a signed-in customer. Returns null when the
    /// order does not exist or does not belong to the supplied user.
    /// </summary>
    Task<OrderDetailDto?> GetOrderDetailAsync(
        string userId,
        string publicOrderNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a full order detail for a verified guest. The caller must already have
    /// a verified access ticket for this order number (issued by
    /// <see cref="VerifyGuestLookupAsync"/>); this method enforces that the order is
    /// a guest order but does not by itself re-verify email.
    /// </summary>
    Task<OrderDetailDto?> GetGuestOrderDetailAsync(
        string publicOrderNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a guest lookup: the public order number must exist and match the
    /// email captured at checkout (case-insensitive). On success a signed, short-lived
    /// access token is returned. The token is bound to the order number and expiry so
    /// an order number alone is never sufficient to view an order.
    /// </summary>
    Task<GuestOrderLookupResult> VerifyGuestLookupAsync(
        string publicOrderNumber,
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a guest access token and returns the order number it authorizes, or
    /// null when the token is invalid, expired or not bound to the requested order.
    /// </summary>
    string? ValidateGuestToken(string token, string publicOrderNumber);

    /// <summary>
    /// Issues a fresh signed, short-lived guest access token bound to the given
    /// public order number. Used at placement time so an order number alone is never
    /// sufficient to view or act on a guest order.
    /// </summary>
    string IssueGuestAccessToken(string publicOrderNumber);

    /// <summary>
    /// Cancels an order. Allowed only from the placed/confirmed states for an order
    /// that has not been paid. Records who, the reason, the previous and new status
    /// and the timestamp; releases the order's stock reservations and voids any
    /// coupon usage recorded against the order. Returns a friendly message when the
    /// order cannot be cancelled.
    /// </summary>
    Task<OrderCancellationResult> CancelAsync(
        string publicOrderNumber,
        OrderCancellationReason reason,
        string actorId,
        string? actorName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the buy-again availability of each order line, resolved against the
    /// live catalogue. A variant that no longer exists, is inactive, or lacks
    /// available stock is reported as unavailable with a reason.
    /// </summary>
    Task<IReadOnlyList<BuyAgainItemDto>> GetBuyAgainAsync(
        string publicOrderNumber,
        CancellationToken cancellationToken = default);
}
