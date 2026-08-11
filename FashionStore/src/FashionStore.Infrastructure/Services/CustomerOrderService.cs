using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Orders;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// The customer order panel service. Order reads are always scoped to the caller's
/// identity: signed-in customers see only their own orders and a verified guest
/// access ticket (signed and short-lived) is required to view a guest order.
/// Cancellation enforces the lifecycle rules, records the transition in the status
/// history and releases the order's stock reservations and coupon usage.
/// </summary>
public sealed class CustomerOrderService : ICustomerOrderService
{
    private const int MaxPageSize = 50;

    private readonly AppDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IOptions<OrderSettings> _orderOptions;
    private readonly ILogger<CustomerOrderService> _logger;

    public CustomerOrderService(
        AppDbContext context,
        IInventoryService inventoryService,
        IOptions<OrderSettings> orderOptions,
        ILogger<CustomerOrderService> logger)
    {
        _context = context;
        _inventoryService = inventoryService;
        _orderOptions = orderOptions;
        _logger = logger;
    }

    public async Task<CustomerOrderListResultDto> GetCustomerOrdersAsync(
        string userId,
        CustomerOrderQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 10 : query.PageSize, 1, MaxPageSize);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        var baseQuery = _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId);

        if (query.Status.HasValue)
        {
            baseQuery = baseQuery.Where(o => o.OrderStatus == query.Status.Value);
        }

        if (!string.IsNullOrEmpty(search))
        {
            var term = search;
            var pattern = $"%{term.Replace("%", "[%]").Replace("_", "[_]")}%";
            baseQuery = baseQuery.Where(o =>
                EF.Functions.Like(o.PublicOrderNumber, pattern) ||
                o.Items.Any(i => EF.Functions.Like(i.ProductName, pattern)));
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var orders = await baseQuery
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(o => o.Items)
            .ToListAsync(cancellationToken);

        var items = orders.Select(o => BuildListItem(o)).ToList();

        return new CustomerOrderListResultDto(
            items,
            totalCount,
            page,
            pageSize,
            page * pageSize < totalCount,
            query.Status,
            search);
    }

    public async Task<OrderDetailDto?> GetOrderDetailAsync(
        string userId,
        string publicOrderNumber,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(publicOrderNumber, cancellationToken);
        if (order is null || !string.Equals(order.UserId, userId, StringComparison.Ordinal))
        {
            return null;
        }

        return BuildDetail(order);
    }

    public async Task<OrderDetailDto?> GetGuestOrderDetailAsync(
        string publicOrderNumber,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(publicOrderNumber, cancellationToken);
        if (order is null || !string.IsNullOrEmpty(order.UserId))
        {
            return null;
        }

        return BuildDetail(order);
    }

    public async Task<GuestOrderLookupResult> VerifyGuestLookupAsync(
        string publicOrderNumber,
        string email,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicOrderNumber) || string.IsNullOrWhiteSpace(email))
        {
            return new GuestOrderLookupResult(false, null, null, "Enter your order number and the email you used at checkout.");
        }

        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.PublicOrderNumber == publicOrderNumber.Trim(), cancellationToken);

        if (order is null ||
            string.IsNullOrEmpty(order.GuestEmail) ||
            !string.Equals(order.GuestEmail, email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            // Deliberately ambiguous so order numbers cannot be probed.
            return new GuestOrderLookupResult(false, null, null, "We could not find an order matching that order number and email.");
        }

        var token = IssueGuestToken(order.PublicOrderNumber);
        return new GuestOrderLookupResult(true, token, order.PublicOrderNumber, null);
    }

    public string? ValidateGuestToken(string token, string publicOrderNumber)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(publicOrderNumber))
        {
            return null;
        }

        var dot = token.LastIndexOf('.');
        if (dot <= 0 || dot >= token.Length - 1)
        {
            return null;
        }

        var payload = token[..dot];
        var signature = token[(dot + 1)..];

        var expected = ComputeSignature(payload);
        if (!FixedTimeEquals(signature, expected))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(payload));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var orderNumber = root.GetProperty("n").GetString();
            var expiresAt = root.GetProperty("exp").GetInt64();

            if (!string.Equals(orderNumber, publicOrderNumber, StringComparison.Ordinal))
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return now <= expiresAt ? orderNumber : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<OrderCancellationResult> CancelAsync(
        string publicOrderNumber,
        OrderCancellationReason reason,
        string actorId,
        string? actorName,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(
            o => o.PublicOrderNumber == publicOrderNumber,
            cancellationToken);

        if (order is null)
        {
            return new OrderCancellationResult(false, "Order not found.");
        }

        if (order.OrderStatus is OrderStatus.Cancelled)
        {
            return new OrderCancellationResult(false, "This order has already been cancelled.");
        }

        if (order.OrderStatus is not (OrderStatus.Placed or OrderStatus.Confirmed))
        {
            return new OrderCancellationResult(false, "This order can no longer be cancelled because it has already progressed.");
        }

        if (order.PaymentStatus is PaymentStatus.Paid or PaymentStatus.PartiallyPaid)
        {
            return new OrderCancellationResult(false, "Paid orders cannot be cancelled here. Please contact support for a refund.");
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

                order.OrderStatus = OrderStatus.Cancelled;
                order.CancelledAtUtc = now;
                order.CancelledReasonCode = reason.ToString();
                order.UpdatedAtUtc = now;

                _context.OrderStatusHistories.Add(new OrderStatusHistory
                {
                    OrderId = order.Id,
                    FromStatus = previousStatus,
                    ToStatus = OrderStatus.Cancelled,
                    Note = $"Cancelled: {reason}",
                    CreatedBy = actorName ?? actorId,
                    CreatedAtUtc = now
                });

                // Release any stock held for this order so the items return to sale.
                var reservationIds = await _context.StockReservations
                    .Where(r => r.Status == StockReservationStatus.Active &&
                                (r.CartReference == publicOrderNumber || r.ReferenceId == publicOrderNumber))
                    .Select(r => r.Id)
                    .ToListAsync(cancellationToken);

                foreach (var reservationId in reservationIds)
                {
                    await _inventoryService.ReleaseReservationAsync(reservationId, cancellationToken);
                }

                // Void coupon usage recorded against this order so the discount is
                // released back into the coupon's usage budget.
                var usages = await _context.CouponUsages
                    .Where(u => u.OrderId == publicOrderNumber && u.VoidedAtUtc == null)
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
                    "Order {OrderNumber} cancelled by {Actor} (reason {Reason}); {Reservations} reservations released, {Usages} coupon usages voided",
                    publicOrderNumber,
                    actorName ?? actorId,
                    reason,
                    reservationIds.Count,
                    usages.Count);

                return new OrderCancellationResult(true, "Your order has been cancelled. Any reserved stock has been released and your coupon (if used) is available again.");
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cancellation failed for order {OrderNumber}", publicOrderNumber);
            return new OrderCancellationResult(false, "We could not cancel your order. Please try again or contact support.");
        }
    }

    public async Task<IReadOnlyList<BuyAgainItemDto>> GetBuyAgainAsync(
        string publicOrderNumber,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.PublicOrderNumber == publicOrderNumber, cancellationToken);

        if (order is null || order.Items.Count == 0)
        {
            return Array.Empty<BuyAgainItemDto>();
        }

        var variantIds = order.Items
            .Where(i => i.ProductVariantId.HasValue)
            .Select(i => i.ProductVariantId!.Value)
            .Distinct()
            .ToList();

        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        var result = new List<BuyAgainItemDto>(order.Items.Count);
        foreach (var item in order.Items.OrderBy(i => i.Id))
        {
            if (!item.ProductVariantId.HasValue)
            {
                result.Add(new BuyAgainItemDto(
                    item.ProductId,
                    null,
                    item.ProductName,
                    item.Sku,
                    item.Quantity,
                    false,
                    "This item is no longer sold."));
                continue;
            }

            variants.TryGetValue(item.ProductVariantId.Value, out var variant);
            if (variant is null || !variant.IsActive || variant.Product is null || !variant.Product.IsActive)
            {
                result.Add(new BuyAgainItemDto(
                    item.ProductId,
                    item.ProductVariantId,
                    item.ProductName,
                    item.Sku,
                    item.Quantity,
                    false,
                    "This item is no longer available."));
                continue;
            }

            var available = Math.Max(0, (variant.StockQuantity ?? 0) - (variant.ReservedStock ?? 0));
            if (available < item.Quantity)
            {
                result.Add(new BuyAgainItemDto(
                    item.ProductId,
                    item.ProductVariantId,
                    item.ProductName,
                    item.Sku,
                    item.Quantity,
                    false,
                    $"Only {available} left in stock."));
                continue;
            }

            result.Add(new BuyAgainItemDto(
                item.ProductId,
                item.ProductVariantId,
                item.ProductName,
                item.Sku,
                item.Quantity,
                true,
                null));
        }

        return result;
    }

    // ---- Helpers ----

    private async Task<Order?> LoadOrderAsync(string publicOrderNumber, CancellationToken cancellationToken) =>
        await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.ShippingAddress)
            .Include(o => o.BillingAddress)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.PublicOrderNumber == publicOrderNumber, cancellationToken);

    private OrderDetailDto BuildDetail(Order order)
    {
        var shipping = order.ShippingAddress;
        var billing = order.BillingAddress;

        var timeline = order.StatusHistory
            .OrderBy(h => h.CreatedAtUtc)
            .ThenBy(h => h.Id.ToString())
            .Select((h, index) => new OrderTimelineEntryDto(
                index + 1,
                h.FromStatus?.ToString() ?? "—",
                h.ToStatus.ToString(),
                h.Note,
                h.CreatedBy,
                h.CreatedAtUtc))
            .ToList();

        var canCancel =
            order.OrderStatus is OrderStatus.Placed or OrderStatus.Confirmed &&
            order.PaymentStatus is not (PaymentStatus.Paid or PaymentStatus.PartiallyPaid);

        var delivery = shipping is null
            ? null
            : new OrderDeliveryInfoDto(
                order.ShippingMethodName,
                shipping.RecipientName,
                shipping.Phone,
                shipping.AddressLine1,
                shipping.AddressLine2,
                shipping.Area,
                shipping.City,
                shipping.Region,
                shipping.PostalCode,
                shipping.CountryCode,
                shipping.DeliveryInstructions);

        return new OrderDetailDto(
            order.Id,
            order.PublicOrderNumber,
            order.InvoiceNumber,
            string.IsNullOrEmpty(order.UserId),
            order.CustomerName,
            order.GuestEmail,
            order.GuestPhone,
            order.Currency,
            order.Subtotal,
            order.ProductDiscount,
            order.CouponDiscount,
            order.ShippingCharge,
            order.Tax,
            order.GrandTotal,
            order.PaidAmount,
            order.RefundedAmount,
            order.OrderStatus.ToString(),
            order.PaymentStatus.ToString(),
            order.FulfilmentStatus.ToString(),
            order.PaymentMethodCode,
            order.ShippingMethodName,
            order.CreatedAtUtc,
            order.PaidAtUtc,
            order.ShippedAtUtc,
            order.DeliveredAtUtc,
            order.CancelledAtUtc,
            order.CancelledReasonCode,
            canCancel,
            order.Items
                .OrderBy(i => i.Id)
                .Select(i => new OrderItemSummaryDto(
                    i.ProductId ?? Guid.Empty,
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
            shipping is null ? null : ToAddressSummary(shipping),
            billing is null ? null : ToAddressSummary(billing),
            delivery,
            timeline,
            null);
    }

    private static CustomerOrderListItemDto BuildListItem(Order order)
    {
        var first = order.Items.OrderBy(i => i.Id).FirstOrDefault();

        return new CustomerOrderListItemDto(
            order.Id,
            order.PublicOrderNumber,
            order.OrderStatus.ToString(),
            order.PaymentStatus.ToString(),
            order.FulfilmentStatus.ToString(),
            order.Currency,
            order.GrandTotal,
            order.Items.Sum(i => i.Quantity),
            first?.ImageUrl,
            first?.ProductName ?? "Order",
            order.CreatedAtUtc,
            order.PaidAtUtc,
            order.CancelledAtUtc);
    }

    private static OrderAddressSummaryDto ToAddressSummary(OrderAddress address) =>
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

    private string IssueGuestToken(string orderNumber)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiresAt = now + _orderOptions.Value.GuestAccessTokenMinutes * 60L;
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { n = orderNumber, exp = expiresAt })));
        var signature = ComputeSignature(payload);
        return $"{payload}.{signature}";
    }

    private string ComputeSignature(string payload)
    {
        var secret = _orderOptions.Value.GuestAccessTokenSecret;
        var bytes = Encoding.UTF8.GetBytes(secret);
        using var hmac = new HMACSHA256(bytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.ASCII.GetBytes(a);
        var bBytes = Encoding.ASCII.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
