using FashionStore.Domain.Entities;

namespace FashionStore.Application.Email;

/// <summary>
/// High-level notification entry points. Each method builds the scenario-specific
/// template model, renders it and enqueues the message into the outbox under a
/// deterministic deduplication key, so the same event can never email the same
/// customer twice even if the flow runs again.
/// </summary>
public interface IEmailNotificationService
{
    Task SendConfirmationEmailAsync(string email, string userId, string token, CancellationToken cancellationToken = default);
    Task SendPasswordResetEmailAsync(string email, string token, CancellationToken cancellationToken = default);
    Task SendWelcomeEmailAsync(string email, string name, CancellationToken cancellationToken = default);

    /// <summary>Sent for the order that was just placed inside the placement transaction.</summary>
    Task SendOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    Task SendPaymentReceivedAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task SendPaymentFailedAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task SendOrderProcessingAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task SendOrderShippedAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task SendOrderDeliveredAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task SendOrderCancelledAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Emails the invoice; the PDF is generated in the background sender job.</summary>
    Task SendInvoiceAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task SendReturnRequestedAsync(Guid returnRequestId, CancellationToken cancellationToken = default);
    Task SendReturnApprovedAsync(Guid returnRequestId, CancellationToken cancellationToken = default);
    Task SendReturnRejectedAsync(Guid returnRequestId, CancellationToken cancellationToken = default);
    Task SendRefundCompletedAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task SendLowStockAlertAsync(IReadOnlyList<LowStockAlertItem> items, CancellationToken cancellationToken = default);
}
