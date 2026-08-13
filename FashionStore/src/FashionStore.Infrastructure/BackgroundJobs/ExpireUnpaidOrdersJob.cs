using FashionStore.Application.Configuration;
using FashionStore.Application.Email;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionStore.Infrastructure.BackgroundJobs;

/// <summary>
/// Cancels placed orders whose online payment was never completed before the
/// configured deadline, releases their stock reservations and notifies the
/// customer. Runs on a schedule so abandoned checkouts do not tie up inventory.
/// </summary>
public sealed class ExpireUnpaidOrdersJob
{
    private readonly AppDbContext _context;
    private readonly IInventoryService _inventory;
    private readonly IEmailNotificationService _emails;
    private readonly OrderSettings _settings;
    private readonly ILogger<ExpireUnpaidOrdersJob> _logger;

    public ExpireUnpaidOrdersJob(
        AppDbContext context,
        IInventoryService inventory,
        IEmailNotificationService emails,
        IOptions<OrderSettings> settings,
        ILogger<ExpireUnpaidOrdersJob> logger)
    {
        _context = context;
        _inventory = inventory;
        _emails = emails;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>Cutoff applied when no setting is present, in minutes.</summary>
    private const int DefaultExpiryMinutes = 120;

    /// <returns>The number of orders cancelled.</returns>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var minutes = _settings.OnlineReservationMinutes > 0 ? _settings.OnlineReservationMinutes : DefaultExpiryMinutes;
        var cutoff = DateTime.UtcNow.AddMinutes(-minutes);
        var now = DateTime.UtcNow;

        var orders = await _context.Orders
            .Where(o => o.OrderStatus == OrderStatus.Placed
                && o.PaymentStatus == PaymentStatus.Unpaid
                && o.CreatedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            order.OrderStatus = OrderStatus.Cancelled;
            order.CancelledAtUtc = now;
            order.CancelledReasonCode = "unpaid";
            order.UpdatedAtUtc = now;

            _context.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                FromStatus = OrderStatus.Placed,
                ToStatus = OrderStatus.Cancelled,
                Note = "Automatically cancelled because the payment was not completed in time.",
                CreatedBy = "system",
                CreatedAtUtc = now
            });
        }

        if (orders.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        foreach (var order in orders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reservations = await _context.StockReservations
                .Where(r => r.Status == StockReservationStatus.Active
                    && r.ReferenceId == order.PublicOrderNumber)
                .ToListAsync(cancellationToken);

            foreach (var reservation in reservations)
            {
                await _inventory.ReleaseReservationAsync(reservation.Id, cancellationToken);
            }

            await _emails.SendOrderCancelledAsync(order.Id, cancellationToken);

            _logger.LogInformation("Order {OrderNumber} expired because payment was not completed", order.PublicOrderNumber);
        }

        return orders.Count;
    }
}
