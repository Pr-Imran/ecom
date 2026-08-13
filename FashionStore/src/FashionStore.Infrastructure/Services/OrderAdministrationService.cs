using System.Globalization;
using System.Text;
using FashionStore.Application.DTOs.Orders;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Administrative order management. Every lifecycle change flows through this
/// service so a single central state machine owns the transition rules. Statuses
/// advance forward only (backwards jumps and cancellation-via-status-update are
/// refused), every transition is recorded in <see cref="OrderStatusHistory"/> with
/// the acting administrator, cancellation releases reserved stock and voids coupon
/// usage, and financial states are never mutated here - money only ever moves
/// through the payment pipeline.
/// </summary>
public sealed class OrderAdministrationService : IOrderAdministrationService
{
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    private readonly AppDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IEmailNotificationService _emailService;
    private readonly ILogger<OrderAdministrationService> _logger;

    public OrderAdministrationService(
        AppDbContext context,
        IInventoryService inventoryService,
        IEmailNotificationService emailService,
        ILogger<OrderAdministrationService> logger)
    {
        _context = context;
        _inventoryService = inventoryService;
        _emailService = emailService;
        _logger = logger;
    }

    // ---- Order list ----

    public async Task<AdminOrderListResultDto> GetOrdersAsync(
        AdminOrderQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? DefaultPageSize : query.PageSize, 1, MaxPageSize);

        var baseQuery = _context.Orders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var pattern = $"%{term.Replace("%", "[%]").Replace("_", "[_]")}%";

            var transactionOrderIds = await _context.Payments
                .AsNoTracking()
                .Where(p => p.ProviderTransactionId != null && EF.Functions.Like(p.ProviderTransactionId, pattern))
                .Select(p => p.OrderId)
                .ToListAsync(cancellationToken);

            baseQuery = baseQuery.Where(o =>
                EF.Functions.Like(o.PublicOrderNumber, pattern) ||
                (o.CustomerName != null && EF.Functions.Like(o.CustomerName, pattern)) ||
                (o.GuestEmail != null && EF.Functions.Like(o.GuestEmail, pattern)) ||
                (o.GuestPhone != null && EF.Functions.Like(o.GuestPhone, pattern)) ||
                transactionOrderIds.Contains(o.Id));
        }

        if (query.DateFromUtc.HasValue)
        {
            baseQuery = baseQuery.Where(o => o.CreatedAtUtc >= query.DateFromUtc.Value);
        }

        if (query.DateToUtc.HasValue)
        {
            baseQuery = baseQuery.Where(o => o.CreatedAtUtc <= query.DateToUtc.Value);
        }

        if (query.OrderStatus.HasValue)
        {
            baseQuery = baseQuery.Where(o => o.OrderStatus == query.OrderStatus.Value);
        }

        if (query.PaymentStatus.HasValue)
        {
            baseQuery = baseQuery.Where(o => o.PaymentStatus == query.PaymentStatus.Value);
        }

        if (query.FulfilmentStatus.HasValue)
        {
            baseQuery = baseQuery.Where(o => o.FulfilmentStatus == query.FulfilmentStatus.Value);
        }

        if (query.ShippingMethodId.HasValue)
        {
            baseQuery = baseQuery.Where(o => o.ShippingMethodId == query.ShippingMethodId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.PaymentMethodCode))
        {
            var method = query.PaymentMethodCode.Trim();
            baseQuery = baseQuery.Where(o => o.PaymentMethodCode == method);
        }

        if (query.MinAmount.HasValue)
        {
            baseQuery = baseQuery.Where(o => o.GrandTotal >= query.MinAmount.Value);
        }

        if (query.MaxAmount.HasValue)
        {
            baseQuery = baseQuery.Where(o => o.GrandTotal <= query.MaxAmount.Value);
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        baseQuery = ApplySorting(baseQuery, query.SortBy, query.SortDirection);

        var orders = await baseQuery
            .Include(o => o.Items)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = orders.Select(o => new AdminOrderListItemDto(
            o.Id,
            o.PublicOrderNumber,
            o.InvoiceNumber,
            string.IsNullOrEmpty(o.UserId),
            o.CustomerName,
            o.GuestEmail,
            o.GuestPhone,
            o.Currency,
            o.GrandTotal,
            o.OrderStatus.ToString(),
            o.PaymentStatus.ToString(),
            o.FulfilmentStatus.ToString(),
            o.PaymentMethodCode,
            o.ShippingMethodName,
            o.Items.Sum(i => i.Quantity),
            o.CreatedAtUtc,
            o.PaidAtUtc,
            o.ShippedAtUtc,
            o.DeliveredAtUtc)).ToList();

        var shippingMethods = await _context.ShippingMethods.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Name)
            .Select(m => new AdminShippingMethodOptionDto(m.Id, m.Name))
            .ToListAsync(cancellationToken);

        var paymentMethods = await _context.Orders.AsNoTracking()
            .Where(o => o.PaymentMethodCode != null)
            .Select(o => o.PaymentMethodCode!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

        return new AdminOrderListResultDto(
            items,
            totalCount,
            page,
            pageSize,
            page * pageSize < totalCount,
            shippingMethods,
            paymentMethods);
    }

    public async Task<AdminOrderDetailDto?> GetOrderDetailAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(o => o.Id == orderId, cancellationToken);
        return order is null ? null : await BuildDetailAsync(order, cancellationToken);
    }

    public async Task<AdminOrderDetailDto?> GetOrderDetailByNumberAsync(
        string publicOrderNumber,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(o => o.PublicOrderNumber == publicOrderNumber, cancellationToken);
        return order is null ? null : await BuildDetailAsync(order, cancellationToken);
    }

    // ---- Central state machine ----

    public async Task<AdminOrderTransitionResult> UpdateOrderStatusAsync(
        Guid orderId,
        OrderStatus toStatus,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync(new object[] { orderId }, cancellationToken);
        if (order is null)
        {
            return new AdminOrderTransitionResult(false, "Order not found.", null, null, null, null);
        }

        if (toStatus == OrderStatus.Cancelled)
        {
            return new AdminOrderTransitionResult(false, "Cancellation is handled by the cancel action.", order.PublicOrderNumber, null, null, null);
        }

        if (order.OrderStatus == OrderStatus.Cancelled)
        {
            return new AdminOrderTransitionResult(false, "Cancelled orders cannot change status.", order.PublicOrderNumber, null, null, null);
        }

        if (order.OrderStatus == toStatus)
        {
            return new AdminOrderTransitionResult(true, null, order.PublicOrderNumber, order.OrderStatus.ToString(), null, null);
        }

        if (toStatus < order.OrderStatus)
        {
            return new AdminOrderTransitionResult(
                false,
                $"Invalid transition: an order cannot move backwards from {order.OrderStatus} to {toStatus}.",
                order.PublicOrderNumber,
                null,
                null,
                null);
        }

        await ApplyStatusTransitionAsync(order, toStatus, note, actorId, cancellationToken);

        return new AdminOrderTransitionResult(
            true,
            null,
            order.PublicOrderNumber,
            order.OrderStatus.ToString(),
            order.FulfilmentStatus.ToString(),
            null);
    }

    public async Task<AdminOrderTransitionResult> UpdateFulfilmentStatusAsync(
        Guid orderId,
        FulfilmentStatus toStatus,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync(new object[] { orderId }, cancellationToken);
        if (order is null)
        {
            return new AdminOrderTransitionResult(false, "Order not found.", null, null, null, null);
        }

        if (toStatus < order.FulfilmentStatus)
        {
            return new AdminOrderTransitionResult(
                false,
                $"Invalid transition: fulfilment cannot move backwards from {order.FulfilmentStatus} to {toStatus}.",
                order.PublicOrderNumber,
                null,
                null,
                null);
        }

        if (order.FulfilmentStatus == toStatus)
        {
            return new AdminOrderTransitionResult(true, null, order.PublicOrderNumber, null, order.FulfilmentStatus.ToString(), null);
        }

        var from = order.FulfilmentStatus;
        order.FulfilmentStatus = toStatus;
        order.UpdatedAtUtc = DateTime.UtcNow;

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = order.OrderStatus,
            ToStatus = order.OrderStatus,
            Note = $"Fulfilment {from} → {toStatus}{(string.IsNullOrWhiteSpace(note) ? string.Empty : $": {note.Trim()}")}",
            CreatedBy = actorId,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new AdminOrderTransitionResult(
            true,
            null,
            order.PublicOrderNumber,
            order.OrderStatus.ToString(),
            order.FulfilmentStatus.ToString(),
            null);
    }

    public async Task<AdminOrderTransitionResult> MarkAsPackedAsync(
        Guid orderId,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync(new object[] { orderId }, cancellationToken);
        if (order is null)
        {
            return new AdminOrderTransitionResult(false, "Order not found.", null, null, null, null);
        }

        if (order.OrderStatus != OrderStatus.Processing)
        {
            return new AdminOrderTransitionResult(
                false,
                "Only a processing order can be marked as packed.",
                order.PublicOrderNumber,
                null,
                null,
                null);
        }

        if (order.PackedAtUtc.HasValue)
        {
            return new AdminOrderTransitionResult(true, null, order.PublicOrderNumber, order.OrderStatus.ToString(), null, null);
        }

        var now = DateTime.UtcNow;
        order.PackedAtUtc = now;
        order.UpdatedAtUtc = now;

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = order.OrderStatus,
            ToStatus = order.OrderStatus,
            Note = "Marked as packed",
            CreatedBy = actorId,
            CreatedAtUtc = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new AdminOrderTransitionResult(true, null, order.PublicOrderNumber, order.OrderStatus.ToString(), null, null);
    }

    public async Task<AdminOrderTransitionResult> MarkAsShippedAsync(
        Guid orderId,
        AdminShipRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync(new object[] { orderId }, cancellationToken);
        if (order is null)
        {
            return new AdminOrderTransitionResult(false, "Order not found.", null, null, null, null);
        }

        if (order.OrderStatus != OrderStatus.Processing)
        {
            return new AdminOrderTransitionResult(
                false,
                "Only a processing order can be marked as shipped.",
                order.PublicOrderNumber,
                null,
                null,
                null);
        }

        var now = DateTime.UtcNow;
        var from = order.OrderStatus;
        order.OrderStatus = OrderStatus.Shipped;
        order.ShippedAtUtc = now;
        order.FulfilmentStatus = FulfilmentStatus.Fulfilled;
        order.TrackingNumber = request?.TrackingNumber?.Trim();
        order.CarrierCode = request?.CarrierCode?.Trim();
        order.TrackingUrl = request?.TrackingUrl?.Trim();
        order.UpdatedAtUtc = now;

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = from,
            ToStatus = OrderStatus.Shipped,
            Note = BuildShippingNote(request),
            CreatedBy = actorId,
            CreatedAtUtc = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Order {OrderNumber} marked as shipped by {Actor} with carrier {Carrier} tracking {Tracking}",
            order.PublicOrderNumber,
            actorId,
            order.CarrierCode,
            order.TrackingNumber);

        return new AdminOrderTransitionResult(
            true,
            null,
            order.PublicOrderNumber,
            order.OrderStatus.ToString(),
            order.FulfilmentStatus.ToString(),
            order.TrackingNumber);
    }

    public async Task<AdminOrderTransitionResult> MarkAsDeliveredAsync(
        Guid orderId,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync(new object[] { orderId }, cancellationToken);
        if (order is null)
        {
            return new AdminOrderTransitionResult(false, "Order not found.", null, null, null, null);
        }

        if (order.OrderStatus != OrderStatus.Shipped)
        {
            return new AdminOrderTransitionResult(
                false,
                "Only a shipped order can be marked as delivered.",
                order.PublicOrderNumber,
                null,
                null,
                null);
        }

        await ApplyStatusTransitionAsync(order, OrderStatus.Delivered, "Marked as delivered", actorId, cancellationToken);

        return new AdminOrderTransitionResult(
            true,
            null,
            order.PublicOrderNumber,
            order.OrderStatus.ToString(),
            order.FulfilmentStatus.ToString(),
            order.TrackingNumber);
    }

    public async Task<AdminOrderTransitionResult> CancelOrderAsync(
        Guid orderId,
        string? reason,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return new AdminOrderTransitionResult(false, "Order not found.", null, null, null, null);
        }

        if (order.OrderStatus == OrderStatus.Cancelled)
        {
            return new AdminOrderTransitionResult(false, "This order has already been cancelled.", order.PublicOrderNumber, null, null, null);
        }

        if (order.OrderStatus is not (OrderStatus.Placed or OrderStatus.Confirmed))
        {
            return new AdminOrderTransitionResult(
                false,
                "This order can no longer be cancelled because it has already progressed.",
                order.PublicOrderNumber,
                null,
                null,
                null);
        }

        if (order.PaymentStatus is PaymentStatus.Paid or PaymentStatus.PartiallyPaid)
        {
            return new AdminOrderTransitionResult(
                false,
                "Money has already been collected for this order. Issue a refund through the payment pipeline before cancelling.",
                order.PublicOrderNumber,
                null,
                null,
                null);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                var previousStatus = order.OrderStatus;
                var now = DateTime.UtcNow;
                var reasonCode = NormalizeReason(reason);

                order.OrderStatus = OrderStatus.Cancelled;
                order.CancelledAtUtc = now;
                order.CancelledReasonCode = reasonCode;
                order.UpdatedAtUtc = now;

                _context.OrderStatusHistories.Add(new OrderStatusHistory
                {
                    OrderId = order.Id,
                    FromStatus = previousStatus,
                    ToStatus = OrderStatus.Cancelled,
                    Note = $"Cancelled by administrator{(string.IsNullOrWhiteSpace(reasonCode) ? string.Empty : $": {reasonCode}")}",
                    CreatedBy = actorId,
                    CreatedAtUtc = now
                });

                var reservationIds = await _context.StockReservations
                    .Where(r => r.Status == StockReservationStatus.Active &&
                                (r.CartReference == order.PublicOrderNumber || r.ReferenceId == order.PublicOrderNumber))
                    .Select(r => r.Id)
                    .ToListAsync(cancellationToken);

                foreach (var reservationId in reservationIds)
                {
                    await _inventoryService.ReleaseReservationAsync(reservationId, cancellationToken);
                }

                var usages = await _context.CouponUsages
                    .Where(u => u.OrderId == order.PublicOrderNumber && u.VoidedAtUtc == null)
                    .ToListAsync(cancellationToken);

                foreach (var usage in usages)
                {
                    usage.VoidedAtUtc = now;
                }

                await _context.SaveChangesAsync(cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "Order {OrderNumber} cancelled by administrator {Actor} (reason {Reason}); {Reservations} reservations released, {Usages} coupon usages voided",
                    order.PublicOrderNumber,
                    actorId,
                    reasonCode,
                    reservationIds.Count,
                    usages.Count);

                return new AdminOrderTransitionResult(
                    true,
                    null,
                    order.PublicOrderNumber,
                    order.OrderStatus.ToString(),
                    null,
                    null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Administrative cancellation failed for order {OrderId}", orderId);
            return new AdminOrderTransitionResult(false, "The order could not be cancelled. Please try again.", order.PublicOrderNumber, null, null, null);
        }
    }

    public async Task<AdminOrderTransitionResult> AddNoteAsync(
        Guid orderId,
        AddOrderNoteRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync(new object[] { orderId }, cancellationToken);
        if (order is null)
        {
            return new AdminOrderTransitionResult(false, "Order not found.", null, null, null, null);
        }

        var text = request?.Note?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AdminOrderTransitionResult(false, "A note cannot be empty.", order.PublicOrderNumber, null, null, null);
        }

        if (text.Length > 2000)
        {
            return new AdminOrderTransitionResult(false, "A note cannot exceed 2000 characters.", order.PublicOrderNumber, null, null, null);
        }

        var now = DateTime.UtcNow;
        var note = new OrderNote
        {
            OrderId = order.Id,
            Note = text,
            IsInternal = request?.IsInternal ?? true,
            CreatedBy = actorId,
            CreatedAtUtc = now
        };

        _context.OrderNotes.Add(note);
        await _context.SaveChangesAsync(cancellationToken);

        return new AdminOrderTransitionResult(
            true,
            null,
            order.PublicOrderNumber,
            order.OrderStatus.ToString(),
            null,
            null);
    }

    // ---- Export ----

    public async Task<AdminOrderExportResult> ExportOrdersAsync(
        AdminOrderQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await GetOrdersAsync(query with { Page = 1, PageSize = MaxPageSize }, cancellationToken);

        var header = "OrderNumber,InvoiceNumber,Customer,Email,Phone,IsGuest,Currency,GrandTotal,OrderStatus,PaymentStatus,FulfilmentStatus,PaymentMethod,ShippingMethod,ItemCount,CreatedAtUtc,PaidAtUtc,ShippedAtUtc,DeliveredAtUtc";

        var sb = new StringBuilder();
        sb.AppendLine(header);

        foreach (var item in result.Items)
        {
            sb.AppendLine(string.Join(",",
                Csv(item.PublicOrderNumber),
                Csv(item.InvoiceNumber),
                Csv(item.CustomerName),
                Csv(item.GuestEmail),
                Csv(item.GuestPhone),
                item.IsGuest ? "true" : "false",
                Csv(item.Currency),
                item.GrandTotal.ToString("0.00", CultureInfo.InvariantCulture),
                Csv(item.OrderStatus),
                Csv(item.PaymentStatus),
                Csv(item.FulfilmentStatus),
                Csv(item.PaymentMethodCode),
                Csv(item.ShippingMethodName),
                item.ItemCount.ToString(CultureInfo.InvariantCulture),
                item.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                item.PaidAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                item.ShippedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                item.DeliveredAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty));
        }

        return new AdminOrderExportResult(
            $"orders-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv",
            sb.ToString());
    }

    // ---- Helpers ----

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private static string? NormalizeReason(string? reason)
    {
        var trimmed = reason?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.Length > 50 ? trimmed[..50] : trimmed;
    }

    private static string BuildShippingNote(AdminShipRequest? request)
    {
        var parts = new List<string> { "Marked as shipped" };
        if (!string.IsNullOrWhiteSpace(request?.CarrierCode))
        {
            parts.Add($"carrier {request.CarrierCode.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(request?.TrackingNumber))
        {
            parts.Add($"tracking {request.TrackingNumber.Trim()}");
        }

        return string.Join(", ", parts);
    }

    private static IQueryable<Order> ApplySorting(IQueryable<Order> query, string? sortBy, string? sortDirection)
    {
        var descending = !string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        return (sortBy, descending) switch
        {
            ("total", true) => query.OrderByDescending(o => o.GrandTotal).ThenByDescending(o => o.CreatedAtUtc),
            ("total", false) => query.OrderBy(o => o.GrandTotal).ThenBy(o => o.CreatedAtUtc),
            ("number", true) => query.OrderByDescending(o => o.PublicOrderNumber).ThenByDescending(o => o.CreatedAtUtc),
            ("number", false) => query.OrderBy(o => o.PublicOrderNumber).ThenBy(o => o.CreatedAtUtc),
            ("date", false) => query.OrderBy(o => o.CreatedAtUtc).ThenBy(o => o.Id),
            _ => query.OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.Id)
        };
    }

    private async Task ApplyStatusTransitionAsync(
        Order order,
        OrderStatus toStatus,
        string? note,
        string actorId,
        CancellationToken cancellationToken)
    {
        var from = order.OrderStatus;
        var now = DateTime.UtcNow;

        order.OrderStatus = toStatus;
        order.UpdatedAtUtc = now;

        switch (toStatus)
        {
            case OrderStatus.Shipped:
                order.ShippedAtUtc ??= now;
                order.FulfilmentStatus = FulfilmentStatus.Fulfilled;
                break;
            case OrderStatus.Delivered:
                order.DeliveredAtUtc ??= now;
                break;
            case OrderStatus.Completed:
                order.DeliveredAtUtc ??= now;
                break;
        }

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = from,
            ToStatus = toStatus,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedBy = actorId,
            CreatedAtUtc = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Order {OrderNumber} status changed from {From} to {To} by {Actor}",
            order.PublicOrderNumber,
            from,
            toStatus,
            actorId);

        switch (toStatus)
        {
            case OrderStatus.Processing:
                await _emailService.SendOrderProcessingAsync(order.Id, cancellationToken);
                break;
            case OrderStatus.Shipped:
                await _emailService.SendOrderShippedAsync(order.Id, cancellationToken);
                break;
            case OrderStatus.Delivered:
                await _emailService.SendOrderDeliveredAsync(order.Id, cancellationToken);
                break;
        }
    }

    private async Task<Order?> LoadOrderAsync(
        System.Linq.Expressions.Expression<Func<Order, bool>> predicate,
        CancellationToken cancellationToken) =>
        await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.ShippingAddress)
            .Include(o => o.BillingAddress)
            .Include(o => o.StatusHistory)
            .Include(o => o.Notes)
            .FirstOrDefaultAsync(predicate, cancellationToken);

    private async Task<AdminOrderDetailDto?> BuildDetailAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        var payment = await _context.Payments
            .AsNoTracking()
            .Include(p => p.Transactions)
            .FirstOrDefaultAsync(p => p.OrderId == order.Id, cancellationToken);

        var inventoryHistory = await BuildInventoryHistoryAsync(order.PublicOrderNumber, cancellationToken);

        var statusHistory = order.StatusHistory
            .OrderBy(h => h.CreatedAtUtc)
            .ThenBy(h => h.Id.ToString())
            .Select(h => new AdminOrderHistoryEntryDto(
                h.FromStatus,
                h.ToStatus.ToString(),
                h.Note,
                h.CreatedBy,
                h.CreatedAtUtc))
            .ToList();

        var internalNotes = order.Notes
            .Where(n => n.IsInternal)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new AdminNoteDto(n.Id, n.Note, true, n.CreatedBy, n.CreatedAtUtc))
            .ToList();

        var customerNotes = order.Notes
            .Where(n => !n.IsInternal)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new AdminNoteDto(n.Id, n.Note, false, n.CreatedBy, n.CreatedAtUtc))
            .ToList();

        var amountDue = order.GrandTotal - order.PaidAmount - order.RefundedAmount;

        return new AdminOrderDetailDto(
            order.Id,
            order.PublicOrderNumber,
            order.InvoiceNumber,
            string.IsNullOrEmpty(order.UserId),
            order.CustomerName,
            order.GuestEmail,
            order.GuestPhone,
            order.UserId,
            order.Currency,
            order.Subtotal,
            order.ProductDiscount,
            order.CouponDiscount,
            order.ShippingCharge,
            order.Tax,
            order.GrandTotal,
            order.PaidAmount,
            order.RefundedAmount,
            amountDue,
            order.OrderStatus.ToString(),
            order.PaymentStatus.ToString(),
            order.FulfilmentStatus.ToString(),
            order.PaymentMethodCode,
            order.ShippingMethodId,
            order.ShippingMethodCode,
            order.ShippingMethodName,
            order.CreatedAtUtc,
            order.UpdatedAtUtc,
            order.PaidAtUtc,
            order.PackedAtUtc,
            order.ShippedAtUtc,
            order.DeliveredAtUtc,
            order.CancelledAtUtc,
            order.CancelledReasonCode,
            order.TrackingNumber,
            order.CarrierCode,
            order.TrackingUrl,
            order.Items
                .OrderBy(i => i.Id)
                .Select(i => new AdminOrderItemDto(
                    i.Id,
                    i.ProductId,
                    i.ProductVariantId,
                    i.ProductName,
                    i.ProductSlug,
                    i.Sku,
                    i.ColourName,
                    i.ColourValue,
                    i.SizeName,
                    i.ImageUrl,
                    i.UnitPrice,
                    i.CompareAtPrice,
                    i.Discount,
                    i.Tax,
                    i.Quantity,
                    i.LineTotal))
                .ToList(),
            order.ShippingAddress is null ? null : ToAddressDto(order.ShippingAddress),
            order.BillingAddress is null ? null : ToAddressDto(order.BillingAddress),
            statusHistory,
            payment?.Transactions?
                .OrderByDescending(t => t.CreatedAtUtc)
                .Select(t => new AdminPaymentTransactionDto(
                    t.Type.ToString(),
                    t.ProviderCode,
                    t.ProviderTransactionId,
                    t.Succeeded,
                    t.ResultCode,
                    t.ResultMessage,
                    t.CreatedAtUtc))
                .ToList() ?? new List<AdminPaymentTransactionDto>(),
            inventoryHistory,
            internalNotes,
            customerNotes,
            CanCancel(order),
            order.OrderStatus is OrderStatus.Placed or OrderStatus.Confirmed,
            order.OrderStatus == OrderStatus.Processing && !order.PackedAtUtc.HasValue,
            order.OrderStatus == OrderStatus.Processing,
            order.OrderStatus == OrderStatus.Shipped,
            order.OrderStatus == OrderStatus.Delivered);
    }

    private async Task<IReadOnlyList<AdminInventoryHistoryEntryDto>> BuildInventoryHistoryAsync(
        string orderNumber,
        CancellationToken cancellationToken)
    {
        var variantIds = await _context.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.ReferenceId == orderNumber)
            .Select(t => t.ProductVariantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        Dictionary<Guid, (string Sku, string? Name)>? variantLookup = null;
        if (variantIds.Count > 0)
        {
            variantLookup = await _context.ProductVariants
                .AsNoTracking()
                .Include(v => v.Product)
                .Where(v => variantIds.Contains(v.Id))
                .ToDictionaryAsync(
                    v => v.Id,
                    v => (v.Sku, v.Product != null ? v.Product.Name : null),
                    cancellationToken);
        }

        var rows = await _context.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.ReferenceId == orderNumber)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return rows
            .Select(t =>
            {
                var sku = t.ProductVariantId.ToString();
                string? productName = null;
                if (variantLookup is not null &&
                    variantLookup.TryGetValue(t.ProductVariantId, out var meta))
                {
                    sku = meta.Sku;
                    productName = meta.Name;
                }

                return new AdminInventoryHistoryEntryDto(
                    sku,
                    productName,
                    t.QuantityChange,
                    t.PreviousOnHand,
                    t.NewOnHand,
                    t.ReservedQuantityChange,
                    t.PreviousReserved,
                    t.NewReserved,
                    t.Reason.ToString(),
                    t.Notes,
                    t.CreatedAtUtc);
            })
            .ToList();
    }

    private static bool CanCancel(Order order) =>
        order.OrderStatus is OrderStatus.Placed or OrderStatus.Confirmed &&
        order.PaymentStatus is not (PaymentStatus.Paid or PaymentStatus.PartiallyPaid);

    private static AdminOrderAddressDto ToAddressDto(OrderAddress address) =>
        new(
            address.RecipientName,
            address.Phone,
            address.AddressLine1,
            address.AddressLine2,
            address.Area,
            address.City,
            address.Region,
            address.PostalCode,
            address.CountryCode,
            address.DeliveryInstructions);
}
