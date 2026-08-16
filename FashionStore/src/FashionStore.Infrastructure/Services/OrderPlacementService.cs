using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Checkout;
using FashionStore.Application.DTOs.Orders;
using FashionStore.Application.DTOs.Promotions;
using FashionStore.Application.Email;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Creates orders transactionally and idempotently. The order service never trusts a
/// price or total from the browser: it re-runs the server-side checkout calculation
/// for the server-resolved cart, refuses to place when the quoted totals are stale,
/// verifies stock, then creates the order, its immutable item and address snapshots,
/// its status history, the stock reservations (policy depends on the payment method)
/// and the coupon usage record inside one transaction. A repeated request carrying
/// the same idempotency key returns the already-created order instead of placing a
/// second one.
/// </summary>
public sealed class OrderPlacementService : IOrderService
{
    private readonly AppDbContext _context;
    private readonly ICheckoutCalculationService _checkoutCalculationService;
    private readonly IDiscountService _discountService;
    private readonly IInventoryService _inventoryService;
    private readonly ICustomerOrderService _customerOrderService;
    private readonly IEmailNotificationService _emailService;
    private readonly IOptions<OrderSettings> _orderOptions;
    private readonly ILogger<OrderPlacementService> _logger;

    public OrderPlacementService(
        AppDbContext context,
        ICheckoutCalculationService checkoutCalculationService,
        IDiscountService discountService,
        IInventoryService inventoryService,
        ICustomerOrderService customerOrderService,
        IEmailNotificationService emailService,
        IOptions<OrderSettings> orderOptions,
        ILogger<OrderPlacementService> logger)
    {
        _context = context;
        _checkoutCalculationService = checkoutCalculationService;
        _discountService = discountService;
        _inventoryService = inventoryService;
        _customerOrderService = customerOrderService;
        _emailService = emailService;
        _orderOptions = orderOptions;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(
        CheckoutCalculationInput input,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var key = string.IsNullOrWhiteSpace(idempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : idempotencyKey.Trim();

        if (key.Length > 128)
        {
            key = key[..128];
        }

        // Fast path: a previous attempt with this key already created the order.
        // Returning the existing order makes a double click, refresh, slow-network
        // retry or repeated API call harmless.
        var existing = await _context.OrderIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == key, cancellationToken);

        if (existing is not null)
        {
            var prior = await LoadOrderAsync(existing.OrderId, cancellationToken);
            if (prior is not null)
            {
                _logger.LogInformation(
                    "Idempotency key {Key} already placed order {OrderNumber}; returning existing order",
                    key,
                    prior.PublicOrderNumber);

                return new PlaceOrderResult(
                    true,
                    true,
                    prior.OrderId,
                    prior.PublicOrderNumber,
                    prior.GrandTotal,
                    Array.Empty<CheckoutValidationError>());
            }
        }

        var calculation = await _checkoutCalculationService.CalculateAsync(input, cancellationToken);

        var errors = new List<CheckoutValidationError>(calculation.Errors);

        if (calculation.IsValid && calculation.PricesChanged)
        {
            errors.Add(new CheckoutValidationError(
                "totals",
                "prices-changed",
                "Prices or totals have changed since you reviewed your order. Please review again before placing."));
        }

        if (errors.Count > 0 || !calculation.IsValid)
        {
            return new PlaceOrderResult(
                false,
                false,
                null,
                null,
                0m,
                errors);
        }

        var stockErrors = await VerifyStockAsync(calculation.Lines, cancellationToken);
        if (stockErrors.Count > 0)
        {
            return new PlaceOrderResult(
                false,
                false,
                null,
                null,
                0m,
                stockErrors);
        }

        var strategy = _context.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                // Re-check the idempotency record inside the transaction so two
                // concurrent placements with the same key cannot both proceed.
                var inTx = await _context.OrderIdempotencyRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.IdempotencyKey == key, cancellationToken);

                if (inTx is not null)
                {
                    var prior = await LoadOrderAsync(inTx.OrderId, cancellationToken);
                    if (prior is not null)
                    {
                        return new PlaceOrderResult(
                            true,
                            true,
                            prior.OrderId,
                            prior.PublicOrderNumber,
                            prior.GrandTotal,
                            Array.Empty<CheckoutValidationError>());
                    }
                }

                var orderNumber = await GenerateOrderNumberAsync(cancellationToken);
                var now = DateTime.UtcNow;

                var order = BuildOrder(input, calculation, orderNumber, now);
                _context.Orders.Add(order);

                // Re-verify stock for each line before reserving so the placement
                // fails fast with a friendly error instead of a reservation exception.
                var inTxStock = await VerifyStockAsync(calculation.Lines, cancellationToken);
                if (inTxStock.Count > 0)
                {
                    throw new StockInsufficientException(inTxStock);
                }

                foreach (var line in calculation.Lines)
                {
                    var reservationExpiry = calculation.SelectedShipping is not null &&
                                            !string.Equals(input.PaymentMethodCode, "cod", StringComparison.OrdinalIgnoreCase)
                        ? _orderOptions.Value.OnlineReservationMinutes
                        : _orderOptions.Value.CodReservationMinutes;

                    await _inventoryService.ReserveStockAsync(
                        new Application.DTOs.Inventory.CreateStockReservationRequest(
                            line.VariantId,
                            null,
                            line.Quantity,
                            orderNumber,
                            reservationExpiry,
                            InventoryReferenceType.Order,
                            orderNumber),
                        cancellationToken);
                }

                if (calculation.Totals.CouponDiscount > 0m &&
                    !string.IsNullOrEmpty(calculation.Totals.Currency))
                {
                    await RecordCouponUsageAsync(
                        input,
                        calculation,
                        order,
                        cancellationToken);
                }

                _context.OrderIdempotencyRecords.Add(new OrderIdempotencyRecord
                {
                    IdempotencyKey = key,
                    OrderId = order.Id,
                    UserId = input.UserId,
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddDays(_orderOptions.Value.IdempotencyRetentionDays)
                });

                await _emailService.SendOrderPlacedAsync(order, cancellationToken);

                await _context.SaveChangesAsync(cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "Placed order {OrderNumber} for {Customer} with {ItemCount} items totalling {GrandTotal}",
                    order.PublicOrderNumber,
                    order.CustomerName ?? input.GuestEmail ?? "unknown",
                    order.Items.Count,
                    order.GrandTotal);

                var guestAccessToken = string.IsNullOrEmpty(input.UserId)
                    ? _customerOrderService.IssueGuestAccessToken(order.PublicOrderNumber)
                    : null;

                return new PlaceOrderResult(
                    true,
                    false,
                    order.Id,
                    order.PublicOrderNumber,
                    order.GrandTotal,
                    Array.Empty<CheckoutValidationError>(),
                    guestAccessToken);
            });
        }
        catch (StockInsufficientException ex)
        {
            return new PlaceOrderResult(
                false,
                false,
                null,
                null,
                0m,
                ex.Errors);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Insufficient available stock", StringComparison.OrdinalIgnoreCase))
        {
            return new PlaceOrderResult(
                false,
                false,
                null,
                null,
                0m,
                new[]
                {
                    new CheckoutValidationError(
                        "items",
                        "stock",
                        "One of the items in your cart is no longer available in the requested quantity. Please review your cart.")
                });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Coupon usage limit", StringComparison.OrdinalIgnoreCase))
        {
            return new PlaceOrderResult(
                false,
                false,
                null,
                null,
                0m,
                new[]
                {
                    new CheckoutValidationError(
                        "coupon",
                        "usage-limit",
                        "This coupon has reached its usage limit. Please remove it and continue without it.")
                });
        }
    }

    public async Task<OrderSummaryDto?> GetByPublicOrderNumberAsync(
        string publicOrderNumber,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.ShippingAddress)
            .Include(o => o.BillingAddress)
            .FirstOrDefaultAsync(o => o.PublicOrderNumber == publicOrderNumber, cancellationToken);

        return order is null ? null : ToSummaryDto(order);
    }

    // ---- Helpers ----

    private Order BuildOrder(
        CheckoutCalculationInput input,
        CheckoutCalculationResult calculation,
        string orderNumber,
        DateTime now)
    {
        var shipping = calculation.SelectedShipping;
        var shippingAddress = input.ShippingAddress;
        var billingAddress = input.BillingSameAsShipping ? input.ShippingAddress : input.BillingAddress;

        var order = new Order
        {
            PublicOrderNumber = orderNumber,
            UserId = input.UserId,
            GuestEmail = input.GuestEmail,
            GuestPhone = input.GuestPhone,
            CustomerName = shippingAddress?.RecipientName,
            Currency = calculation.Totals.Currency,
            Subtotal = calculation.Totals.Subtotal,
            ProductDiscount = calculation.Totals.PromotionsDiscount,
            CouponDiscount = calculation.Totals.CouponDiscount,
            ShippingCharge = calculation.Totals.Shipping,
            Tax = calculation.Totals.Tax,
            GrandTotal = calculation.Totals.GrandTotal,
            PaidAmount = 0m,
            RefundedAmount = 0m,
            OrderStatus = OrderStatus.Placed,
            PaymentStatus = PaymentStatus.Unpaid,
            FulfilmentStatus = FulfilmentStatus.Unfulfilled,
            PaymentMethodCode = input.PaymentMethodCode,
            ShippingMethodId = shipping?.MethodId,
            ShippingMethodCode = shipping?.Code,
            ShippingMethodName = shipping?.Name,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        if (shippingAddress is not null)
        {
            order.ShippingAddress = BuildAddress(shippingAddress, OrderAddressType.Shipping);
            order.ShippingAddressId = order.ShippingAddress.Id;
        }

        if (billingAddress is not null)
        {
            order.BillingAddress = BuildAddress(billingAddress, OrderAddressType.Billing);
            order.BillingAddressId = order.BillingAddress.Id;
        }

        foreach (var line in calculation.Lines)
        {
            order.Items.Add(new OrderItem
            {
                ProductId = line.ProductId,
                ProductVariantId = line.VariantId,
                ProductName = line.ProductName,
                ProductSlug = line.Slug,
                Sku = line.Sku,
                ColourName = line.ColourName,
                SizeName = line.SizeName,
                ImageUrl = line.ImageUrl,
                UnitPrice = line.UnitPrice,
                CompareAtPrice = line.CompareAtPrice,
                Discount = line.PromotionsDiscount,
                Tax = line.Tax,
                Quantity = line.Quantity,
                LineTotal = line.LineTotal
            });
        }

        order.StatusHistory.Add(new OrderStatusHistory
        {
            FromStatus = null,
            ToStatus = OrderStatus.Placed,
            Note = "Order placed.",
            CreatedBy = input.UserId ?? input.GuestEmail,
            CreatedAtUtc = now
        });

        return order;
    }

    private static OrderAddress BuildAddress(CheckoutAddressInput address, OrderAddressType type) =>
        new()
        {
            AddressType = type,
            RecipientName = address.RecipientName,
            Phone = address.Phone,
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            Area = address.Area,
            City = address.City,
            Region = address.Region,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode,
            DeliveryInstructions = address.DeliveryInstructions
        };

    private async Task<List<CheckoutValidationError>> VerifyStockAsync(
        IReadOnlyList<CheckoutLineItemDto> lines,
        CancellationToken cancellationToken)
    {
        var errors = new List<CheckoutValidationError>();
        if (lines.Count == 0)
        {
            errors.Add(new CheckoutValidationError("cart", "empty", "Your cart is empty."));
            return errors;
        }

        var variantIds = lines.Select(l => l.VariantId).Distinct().ToList();
        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        foreach (var line in lines)
        {
            variants.TryGetValue(line.VariantId, out var variant);
            var available = Math.Max(0, (variant?.StockQuantity ?? 0) - (variant?.ReservedStock ?? 0));

            if (line.Quantity > available)
            {
                errors.Add(new CheckoutValidationError(
                    "items",
                    "stock",
                    $"\"{line.ProductName}\" is no longer available in the requested quantity. Only {available} left."));
            }
        }

        return errors;
    }

    private async Task RecordCouponUsageAsync(
        CheckoutCalculationInput input,
        CheckoutCalculationResult calculation,
        Order order,
        CancellationToken cancellationToken)
    {
        var code = calculation.Totals.CouponDiscount > 0m
            ? calculation.Discounts.FirstOrDefault(d => d.Type == DiscountBreakdownType.Coupon)?.CouponCode
            : null;

        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var coupon = await _context.Coupons
            .AsNoTracking()
            .FirstOrDefaultAsync(
#pragma warning disable CA1862
                c => c.NormalizedCode != null && c.NormalizedCode.ToLower() == code.Trim().ToLower(),
#pragma warning restore CA1862
                cancellationToken);

        if (coupon is null)
        {
            return;
        }

        // Guests have no identity, so the per-customer usage limit is keyed by the
        // email captured at checkout; signed-in customers use their user id.
        var usageKey = input.UserId ?? input.GuestEmail ?? "guest";
        var recorded = await _discountService.RecordUsageAsync(
            coupon.Id,
            usageKey,
            calculation.Totals.CouponDiscount,
            order.PublicOrderNumber,
            cancellationToken);

        if (!recorded)
        {
            throw new InvalidOperationException("Coupon usage limit reached while placing the order.");
        }
    }

    private async Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken)
    {
        var prefix = _orderOptions.Value.OrderNumberPrefix;
        var year = DateTime.UtcNow.Year;

        var sequence = await _context.OrderNumberSequences
            .FirstOrDefaultAsync(s => s.Prefix == prefix && s.Year == year, cancellationToken);

        if (sequence is null)
        {
            sequence = new OrderNumberSequence
            {
                Prefix = prefix,
                Year = year,
                LastNumber = 0
            };
            _context.OrderNumberSequences.Add(sequence);
        }

        sequence.LastNumber++;

        var number = $"{prefix}-{year}-{sequence.LastNumber:D6}";
        return number.Length > 50 ? number[..50] : number;
    }

    private async Task<OrderSummaryDto?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.ShippingAddress)
            .Include(o => o.BillingAddress)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        return order is null ? null : ToSummaryDto(order);
    }

    private static OrderSummaryDto ToSummaryDto(Order order)
    {
        var shipping = order.ShippingAddress;
        var billing = order.BillingAddress;

        return new OrderSummaryDto(
            order.Id,
            order.PublicOrderNumber,
            order.InvoiceNumber,
            order.UserId,
            order.GuestEmail,
            order.CustomerName,
            order.Currency,
            order.Subtotal,
            order.ProductDiscount,
            order.CouponDiscount,
            order.ShippingCharge,
            order.Tax,
            order.GrandTotal,
            order.OrderStatus.ToString(),
            order.PaymentStatus.ToString(),
            order.FulfilmentStatus.ToString(),
            order.PaymentMethodCode,
            order.ShippingMethodName,
            order.CreatedAtUtc,
            order.PaidAtUtc,
            order.ShippedAtUtc,
            order.DeliveredAtUtc,
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
            shipping is null ? null : ToAddressSummaryDto(shipping),
            billing is null ? null : ToAddressSummaryDto(billing));
    }

    private static OrderAddressSummaryDto ToAddressSummaryDto(OrderAddress address) =>
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

    /// <summary>Internal marker carrying per-line stock errors to roll back the placement.</summary>
    private sealed class StockInsufficientException : Exception
    {
        public IReadOnlyList<CheckoutValidationError> Errors { get; }

        public StockInsufficientException(IReadOnlyList<CheckoutValidationError> errors)
            : base("Stock is insufficient for one or more items.")
        {
            Errors = errors;
        }
    }
}
