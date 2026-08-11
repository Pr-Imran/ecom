using FashionStore.Application.DTOs.Orders;
using FashionStore.Domain.Enums;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Administrative order management. All order lifecycle changes flow through this
/// service so a single central state machine owns the transition rules: statuses
/// advance forward only, every transition is recorded in the order's status
/// history with the acting administrator, cancellation releases reserved stock
/// and voids coupon usage, and financial states are never mutated here - money
/// changes only ever happen through the payment pipeline.
/// </summary>
public interface IOrderAdministrationService
{
    /// <summary>
    /// Filters and pages the full order set. Search covers order number, customer
    /// name, email, phone and the provider transaction id. The result also carries
    /// the distinct shipping / payment method options so the filter sheet can be
    /// populated from live data.
    /// </summary>
    Task<AdminOrderListResultDto> GetOrdersAsync(
        AdminOrderQueryRequest query,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the full administrative order detail with all sections.</summary>
    Task<AdminOrderDetailDto?> GetOrderDetailAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the full administrative order detail by its public order number.</summary>
    Task<AdminOrderDetailDto?> GetOrderDetailByNumberAsync(
        string publicOrderNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances an order to a later lifecycle state. Only forward transitions are
    /// accepted; moving backwards or moving to Cancelled here is refused. The
    /// transition is recorded in the status history with the acting administrator.
    /// </summary>
    Task<AdminOrderTransitionResult> UpdateOrderStatusAsync(
        Guid orderId,
        OrderStatus toStatus,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Advances the fulfilment status forward (unfulfilled → partial → fulfilled).</summary>
    Task<AdminOrderTransitionResult> UpdateFulfilmentStatusAsync(
        Guid orderId,
        FulfilmentStatus toStatus,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a processing order as packed; records PackedAtUtc.</summary>
    Task<AdminOrderTransitionResult> MarkAsPackedAsync(
        Guid orderId,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a processing order as shipped: advances the status to Shipped, records
    /// the shipping timestamp, marks the order fulfilled and stores the courier
    /// tracking information supplied by the administrator.
    /// </summary>
    Task<AdminOrderTransitionResult> MarkAsShippedAsync(
        Guid orderId,
        AdminShipRequest request,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a shipped order as delivered; records the delivery timestamp.</summary>
    Task<AdminOrderTransitionResult> MarkAsDeliveredAsync(
        Guid orderId,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an order. Only placed / confirmed orders with no money collected can
    /// be cancelled here; a refund is a payment operation and is never performed by
    /// this service. Cancellation records the transition and releases the order's
    /// stock reservations and coupon usage.
    /// </summary>
    Task<AdminOrderTransitionResult> CancelOrderAsync(
        Guid orderId,
        string? reason,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds an internal or customer-visible note to an order.</summary>
    Task<AdminOrderTransitionResult> AddNoteAsync(
        Guid orderId,
        AddOrderNoteRequest request,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the filtered order set as CSV (export-ready service). The same query
    /// object powers the on-screen list so the export always matches the filters.
    /// </summary>
    Task<AdminOrderExportResult> ExportOrdersAsync(
        AdminOrderQueryRequest query,
        CancellationToken cancellationToken = default);
}
