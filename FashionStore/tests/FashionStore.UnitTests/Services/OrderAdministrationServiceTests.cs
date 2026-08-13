using FashionStore.Application.DTOs.Orders;
using FashionStore.Application.Email;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FashionStore.UnitTests.Services;

/// <summary>
/// Exercises the administrative order service: the central state machine (valid
/// forward transitions, invalid backwards jumps, quick action preconditions),
/// cancellation with stock release and coupon voiding, shipment tracking, notes
/// (internal vs customer), audit trail recording and the export pipeline.
/// </summary>
public class OrderAdministrationServiceTests
{
    private sealed class Fixture
    {
        public AppDbContext Context { get; }
        public Mock<IInventoryService> Inventory { get; } = new();
        public Mock<IEmailNotificationService> EmailService { get; } = new();

        public Fixture()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"fashionstore-admin-orders-{Guid.NewGuid()}")
                .Options;
            Context = new AppDbContext(options);

            Inventory
                .Setup(i => i.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        public OrderAdministrationService CreateService() =>
            new(Context, Inventory.Object, EmailService.Object, NullLogger<OrderAdministrationService>.Instance);
    }

    private static Order SeedOrder(
        AppDbContext context,
        string number = "ORD-2026-000001",
        OrderStatus status = OrderStatus.Placed,
        PaymentStatus payment = PaymentStatus.Unpaid,
        FulfilmentStatus fulfilment = FulfilmentStatus.Unfulfilled,
        string? email = "jane@example.com",
        string? phone = "555-0100",
        decimal total = 115m,
        bool withItem = true)
    {
        var order = new Order
        {
            PublicOrderNumber = number,
            InvoiceNumber = number.Replace("ORD", "INV"),
            UserId = "user-1",
            GuestEmail = email,
            GuestPhone = phone,
            CustomerName = "Jane Doe",
            Currency = "USD",
            Subtotal = 100m,
            ProductDiscount = 0m,
            CouponDiscount = 0m,
            ShippingCharge = 10m,
            Tax = 5m,
            GrandTotal = total,
            PaymentMethodCode = "card",
            ShippingMethodName = "Standard Delivery",
            OrderStatus = status,
            PaymentStatus = payment,
            FulfilmentStatus = fulfilment,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        if (withItem)
        {
            order.Items.Add(new OrderItem
            {
                ProductId = Guid.NewGuid(),
                ProductVariantId = Guid.NewGuid(),
                ProductName = "Cashmere Sweater",
                ProductSlug = "cashmere-sweater",
                Sku = "SW-1001-GREY-M",
                ColourName = "Grey",
                ColourValue = "#808080",
                SizeName = "M",
                ImageUrl = "/img/sweater.jpg",
                UnitPrice = 100m,
                Discount = 0m,
                Tax = 5m,
                Quantity = 1,
                LineTotal = 100m
            });
        }

        order.ShippingAddress = new OrderAddress
        {
            AddressType = OrderAddressType.Shipping,
            RecipientName = "Jane Doe",
            Phone = "555-0100",
            AddressLine1 = "1 Main Street",
            City = "New York",
            Region = "NY",
            PostalCode = "10001",
            CountryCode = "US"
        };

        order.StatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = null,
            ToStatus = OrderStatus.Placed,
            Note = "Order placed",
            CreatedBy = "Jane Doe",
            CreatedAtUtc = order.CreatedAtUtc
        });

        context.Orders.Add(order);
        context.SaveChanges();
        return order;
    }

    private static Payment SeedPayment(AppDbContext context, Order order)
    {
        var payment = new Payment
        {
            OrderId = order.Id,
            ProviderCode = "card",
            PaymentMethodCode = "card",
            ProviderTransactionId = "TXN-987654",
            IdempotencyKey = Guid.NewGuid().ToString(),
            Amount = order.GrandTotal,
            Currency = order.Currency,
            State = PaymentState.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
        payment.Transactions.Add(new PaymentTransaction
        {
            PaymentId = payment.Id,
            Type = PaymentTransactionType.Capture,
            ProviderCode = "card",
            ProviderTransactionId = "TXN-987654",
            Succeeded = true,
            ResultCode = "OK",
            ResultMessage = "Captured",
            CreatedAtUtc = DateTime.UtcNow
        });
        context.Payments.Add(payment);
        context.SaveChanges();
        return payment;
    }

    private static ProductVariant SeedVariant(AppDbContext context, int stockQuantity = 10, int reserved = 0)
    {
        var product = new Product
        {
            Name = "Cashmere Sweater",
            Slug = "cashmere-sweater",
            CategoryId = Guid.NewGuid(),
            ProductType = "Standard",
            BaseSku = "SW-1001",
            BasePrice = 100m,
            TaxCategory = "Standard",
            IsActive = true
        };
        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = "SW-1001-GREY-M",
            Price = 100m,
            IsActive = true,
            StockQuantity = stockQuantity,
            ReservedStock = reserved
        };
        product.Variants.Add(variant);
        context.Products.Add(product);
        context.SaveChanges();
        return variant;
    }

    private static StockReservation SeedReservation(AppDbContext context, Guid variantId, string orderNumber)
    {
        var reservation = new StockReservation
        {
            ProductVariantId = variantId,
            Quantity = 1,
            CartReference = orderNumber,
            ReferenceId = orderNumber,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            Status = StockReservationStatus.Active
        };
        context.StockReservations.Add(reservation);
        context.SaveChanges();
        return reservation;
    }

    private static AdminOrderQueryRequest Query(
        string? search = null,
        OrderStatus? orderStatus = null,
        PaymentStatus? paymentStatus = null,
        FulfilmentStatus? fulfilmentStatus = null,
        int page = 1,
        int pageSize = 20) =>
        new(search, null, null, orderStatus, paymentStatus, fulfilmentStatus, null, null, null, null, page, pageSize, null, null);

    // ---- Order list ----

    [Fact]
    public async Task GetOrdersAsync_SearchMatchesNumberEmailPhoneAndTransaction()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001", email: "jane@example.com");
        SeedPayment(fixture.Context, order);
        SeedOrder(fixture.Context, "ORD-2026-000002", email: "bob@example.com");
        var service = fixture.CreateService();

        var byNumber = await service.GetOrdersAsync(Query(search: "ORD-2026-000002"), CancellationToken.None);
        Assert.Single(byNumber.Items);
        Assert.Equal("ORD-2026-000002", byNumber.Items[0].PublicOrderNumber);

        var byEmail = await service.GetOrdersAsync(Query(search: "bob@example.com"), CancellationToken.None);
        Assert.Single(byEmail.Items);
        Assert.Equal("ORD-2026-000002", byEmail.Items[0].PublicOrderNumber);

        var byPhone = await service.GetOrdersAsync(Query(search: "555-0100"), CancellationToken.None);
        Assert.Equal(2, byPhone.TotalCount);

        var byTransaction = await service.GetOrdersAsync(Query(search: "TXN-987654"), CancellationToken.None);
        Assert.Single(byTransaction.Items);
        Assert.Equal("ORD-2026-000001", byTransaction.Items[0].PublicOrderNumber);
    }

    [Fact]
    public async Task GetOrdersAsync_AppliesStatusPaymentAndFulfilmentFilters()
    {
        var fixture = new Fixture();
        SeedOrder(fixture.Context, "ORD-2026-000001", status: OrderStatus.Processing, payment: PaymentStatus.Unpaid);
        SeedOrder(fixture.Context, "ORD-2026-000002", status: OrderStatus.Shipped, payment: PaymentStatus.Paid);
        SeedOrder(fixture.Context, "ORD-2026-000003", status: OrderStatus.Shipped, payment: PaymentStatus.Paid, fulfilment: FulfilmentStatus.Fulfilled);
        var service = fixture.CreateService();

        var byStatus = await service.GetOrdersAsync(Query(orderStatus: OrderStatus.Shipped), CancellationToken.None);
        Assert.Equal(2, byStatus.TotalCount);

        var byPayment = await service.GetOrdersAsync(Query(paymentStatus: PaymentStatus.Paid), CancellationToken.None);
        Assert.Equal(2, byPayment.TotalCount);

        var byFulfilment = await service.GetOrdersAsync(Query(fulfilmentStatus: FulfilmentStatus.Fulfilled), CancellationToken.None);
        Assert.Single(byFulfilment.Items);
        Assert.Equal("ORD-2026-000003", byFulfilment.Items[0].PublicOrderNumber);
    }

    [Fact]
    public async Task GetOrdersAsync_PaginatesAndReportsHasMore()
    {
        var fixture = new Fixture();
        for (var i = 1; i <= 5; i++)
        {
            SeedOrder(fixture.Context, $"ORD-2026-00000{i}");
        }

        var service = fixture.CreateService();
        var pageOne = await service.GetOrdersAsync(Query(page: 1, pageSize: 2), CancellationToken.None);
        Assert.Equal(2, pageOne.Items.Count);
        Assert.True(pageOne.HasMore);
        Assert.Equal(5, pageOne.TotalCount);

        var pageThree = await service.GetOrdersAsync(Query(page: 3, pageSize: 2), CancellationToken.None);
        Assert.Single(pageThree.Items);
        Assert.False(pageThree.HasMore);
    }

    [Fact]
    public async Task GetOrdersAsync_ReturnsShippingAndPaymentMethodOptions()
    {
        var fixture = new Fixture();
        fixture.Context.ShippingMethods.Add(new ShippingMethod
        {
            Code = "STANDARD",
            Name = "Standard Delivery",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        fixture.Context.SaveChanges();
        SeedOrder(fixture.Context, "ORD-2026-000001", payment: PaymentStatus.Paid);
        SeedOrder(fixture.Context, "ORD-2026-000002");
        var service = fixture.CreateService();

        var result = await service.GetOrdersAsync(Query(), CancellationToken.None);

        Assert.Contains(result.PaymentMethods, m => m == "card");
        Assert.Contains(result.ShippingMethods, m => m.ShippingMethodName == "Standard Delivery");
    }

    // ---- Detail ----

    [Fact]
    public async Task GetOrderDetailAsync_IncludesAllSections()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001");
        SeedPayment(fixture.Context, order);
        fixture.Context.OrderNotes.Add(new OrderNote
        {
            OrderId = order.Id,
            Note = "staff only",
            IsInternal = true,
            CreatedBy = "admin-1",
            CreatedAtUtc = DateTime.UtcNow
        });
        fixture.Context.OrderNotes.Add(new OrderNote
        {
            OrderId = order.Id,
            Note = "customer visible",
            IsInternal = false,
            CreatedBy = "admin-1",
            CreatedAtUtc = DateTime.UtcNow
        });
        fixture.Context.SaveChanges();

        var service = fixture.CreateService();
        var detail = await service.GetOrderDetailAsync(order.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("ORD-2026-000001", detail.PublicOrderNumber);
        Assert.Equal("Jane Doe", detail.CustomerName);
        Assert.False(detail.IsGuest);
        Assert.Single(detail.Items);
        Assert.Equal("Cashmere Sweater", detail.Items[0].ProductName);
        Assert.Equal("SW-1001-GREY-M", detail.Items[0].Sku);
        Assert.Equal("Grey", detail.Items[0].ColourName);
        Assert.Equal("M", detail.Items[0].SizeName);
        Assert.NotNull(detail.ShippingAddress);
        Assert.Equal("1 Main Street", detail.ShippingAddress.AddressLine1);
        Assert.Single(detail.StatusHistory);
        Assert.Single(detail.PaymentTransactions);
        Assert.Equal("TXN-987654", detail.PaymentTransactions[0].ProviderTransactionId);
        Assert.Single(detail.InternalNotes);
        Assert.Single(detail.CustomerNotes);
        Assert.Equal("staff only", detail.InternalNotes[0].Note);
        Assert.Equal("customer visible", detail.CustomerNotes[0].Note);
        Assert.Equal(115m, detail.GrandTotal);
        Assert.True(detail.CanCancel);
        Assert.True(detail.CanProcess);
    }

    [Fact]
    public async Task GetOrderDetailAsync_ReportsGuestStateAndAmountDue()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001", email: "guest@example.com");
        order.UserId = null;
        fixture.Context.SaveChanges();

        var service = fixture.CreateService();
        var detail = await service.GetOrderDetailAsync(order.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.True(detail.IsGuest);
        Assert.Equal(115m, detail.AmountDue);
    }

    // ---- Central state machine ----

    [Fact]
    public async Task UpdateOrderStatusAsync_ForwardTransitionsThroughLifecycle_RecordEveryStep()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001");
        var service = fixture.CreateService();
        var actor = "admin-1";

        Assert.True((await service.UpdateOrderStatusAsync(order.Id, OrderStatus.Confirmed, null, actor, CancellationToken.None)).Success);
        Assert.True((await service.UpdateOrderStatusAsync(order.Id, OrderStatus.Processing, "start", actor, CancellationToken.None)).Success);
        Assert.True((await service.UpdateOrderStatusAsync(order.Id, OrderStatus.Shipped, null, actor, CancellationToken.None)).Success);
        Assert.True((await service.UpdateOrderStatusAsync(order.Id, OrderStatus.Delivered, null, actor, CancellationToken.None)).Success);
        Assert.True((await service.UpdateOrderStatusAsync(order.Id, OrderStatus.Completed, "done", actor, CancellationToken.None)).Success);

        var refreshed = fixture.Context.Orders.AsNoTracking().Single(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Completed, refreshed.OrderStatus);
        Assert.NotNull(refreshed.ShippedAtUtc);
        Assert.NotNull(refreshed.DeliveredAtUtc);
        Assert.Equal(FulfilmentStatus.Fulfilled, refreshed.FulfilmentStatus);

        var history = fixture.Context.OrderStatusHistories.Where(h => h.OrderId == order.Id).OrderBy(h => h.CreatedAtUtc).ToList();
        Assert.Equal(6, history.Count); // placement + 5 transitions
        Assert.Contains(history, h => h.ToStatus == OrderStatus.Shipped && h.FromStatus == OrderStatus.Processing);
        Assert.Contains(history, h => h.ToStatus == OrderStatus.Delivered && h.CreatedBy == actor);
    }

    [Fact]
    public async Task UpdateOrderStatusAsync_BackwardsJump_IsRefused()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001", status: OrderStatus.Shipped);
        var service = fixture.CreateService();

        var result = await service.UpdateOrderStatusAsync(order.Id, OrderStatus.Processing, null, "admin-1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        var refreshed = fixture.Context.Orders.AsNoTracking().Single(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Shipped, refreshed.OrderStatus);
    }

    [Fact]
    public async Task UpdateOrderStatusAsync_CancelledStatusIsHandledByCancelActionOnly()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001");
        var service = fixture.CreateService();

        var result = await service.UpdateOrderStatusAsync(order.Id, OrderStatus.Cancelled, null, "admin-1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("cancel", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateOrderStatusAsync_CancelledOrderCannotMove()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001", status: OrderStatus.Cancelled);
        var service = fixture.CreateService();

        var result = await service.UpdateOrderStatusAsync(order.Id, OrderStatus.Completed, null, "admin-1", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task UpdateOrderStatusAsync_SameStatus_IsIdempotentNoop()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001", status: OrderStatus.Processing);
        var service = fixture.CreateService();

        var result = await service.UpdateOrderStatusAsync(order.Id, OrderStatus.Processing, null, "admin-1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Processing", result.NewOrderStatus);
    }

    // ---- Fulfilment ----

    [Fact]
    public async Task UpdateFulfilmentStatusAsync_AdvancesForwardAndRefusesBackwards()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001");
        var service = fixture.CreateService();

        Assert.True((await service.UpdateFulfilmentStatusAsync(order.Id, FulfilmentStatus.PartiallyFulfilled, null, "admin-1", CancellationToken.None)).Success);
        Assert.True((await service.UpdateFulfilmentStatusAsync(order.Id, FulfilmentStatus.Fulfilled, null, "admin-1", CancellationToken.None)).Success);

        var backwards = await service.UpdateFulfilmentStatusAsync(order.Id, FulfilmentStatus.Unfulfilled, null, "admin-1", CancellationToken.None);
        Assert.False(backwards.Success);

        var refreshed = fixture.Context.Orders.AsNoTracking().Single(o => o.Id == order.Id);
        Assert.Equal(FulfilmentStatus.Fulfilled, refreshed.FulfilmentStatus);
    }

    // ---- Quick actions ----

    [Fact]
    public async Task MarkAsPacked_RequiresProcessingOrder()
    {
        var fixture = new Fixture();
        var placed = SeedOrder(fixture.Context, "ORD-2026-000001");
        var processing = SeedOrder(fixture.Context, "ORD-2026-000002", status: OrderStatus.Processing);
        var service = fixture.CreateService();

        var refused = await service.MarkAsPackedAsync(placed.Id, "admin-1", CancellationToken.None);
        Assert.False(refused.Success);

        var accepted = await service.MarkAsPackedAsync(processing.Id, "admin-1", CancellationToken.None);
        Assert.True(accepted.Success);
        Assert.NotNull(fixture.Context.Orders.AsNoTracking().Single(o => o.Id == processing.Id).PackedAtUtc);
    }

    [Fact]
    public async Task MarkAsShipped_SetsTrackingAndRecordsAuditEntry()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001", status: OrderStatus.Processing);
        var service = fixture.CreateService();

        var result = await service.MarkAsShippedAsync(
            order.Id,
            new AdminShipRequest("ups", "1Z999AA10123456784", "https://www.ups.com/track"),
            "admin-1",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("1Z999AA10123456784", result.TrackingNumber);

        var refreshed = fixture.Context.Orders.AsNoTracking().Single(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Shipped, refreshed.OrderStatus);
        Assert.Equal("ups", refreshed.CarrierCode);
        Assert.Equal("1Z999AA10123456784", refreshed.TrackingNumber);
        Assert.Equal("https://www.ups.com/track", refreshed.TrackingUrl);
        Assert.Equal(FulfilmentStatus.Fulfilled, refreshed.FulfilmentStatus);
        Assert.NotNull(refreshed.ShippedAtUtc);

        var history = fixture.Context.OrderStatusHistories.Where(h => h.OrderId == order.Id).ToList();
        Assert.Contains(history, h => h.ToStatus == OrderStatus.Shipped && h.Note != null && h.Note.Contains("1Z999AA10123456784"));
    }

    [Fact]
    public async Task MarkAsShipped_RequiresProcessingOrder()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001", status: OrderStatus.Placed);
        var service = fixture.CreateService();

        var result = await service.MarkAsShippedAsync(order.Id, new AdminShipRequest(null, null, null), "admin-1", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task MarkAsDelivered_RequiresShippedOrder()
    {
        var fixture = new Fixture();
        var processing = SeedOrder(fixture.Context, "ORD-2026-000001", status: OrderStatus.Processing);
        var shipped = SeedOrder(fixture.Context, "ORD-2026-000002", status: OrderStatus.Shipped);
        var service = fixture.CreateService();

        var refused = await service.MarkAsDeliveredAsync(processing.Id, "admin-1", CancellationToken.None);
        Assert.False(refused.Success);

        var accepted = await service.MarkAsDeliveredAsync(shipped.Id, "admin-1", CancellationToken.None);
        Assert.True(accepted.Success);
        var refreshed = fixture.Context.Orders.AsNoTracking().Single(o => o.Id == shipped.Id);
        Assert.Equal(OrderStatus.Delivered, refreshed.OrderStatus);
        Assert.NotNull(refreshed.DeliveredAtUtc);
    }

    // ---- Cancellation + inventory effects ----

    [Fact]
    public async Task CancelOrderAsync_PlacedUnpaidOrder_ReleasesStockVoidsCouponAndRecordsHistory()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001");
        var variant = SeedVariant(fixture.Context, stockQuantity: 10, reserved: 1);
        order.Items.First().ProductVariantId = variant.Id;
        fixture.Context.SaveChanges();
        var reservation = SeedReservation(fixture.Context, variant.Id, "ORD-2026-000001");

        var coupon = new Coupon
        {
            Code = "SAVE10",
            NormalizedCode = "SAVE10",
            Name = "Save $10",
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 10m,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        fixture.Context.Coupons.Add(coupon);
        fixture.Context.CouponUsages.Add(new CouponUsage
        {
            CouponId = coupon.Id,
            UserId = "user-1",
            OrderId = "ORD-2026-000001",
            AmountDiscounted = 10m,
            UsedAtUtc = DateTime.UtcNow
        });
        fixture.Context.SaveChanges();

        var service = fixture.CreateService();
        var result = await service.CancelOrderAsync(order.Id, "Customer requested cancellation", "admin-1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Cancelled", result.NewOrderStatus);

        var refreshed = fixture.Context.Orders.AsNoTracking().Single(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Cancelled, refreshed.OrderStatus);
        Assert.Equal("Customer requested cancellation", refreshed.CancelledReasonCode);
        Assert.NotNull(refreshed.CancelledAtUtc);

        var history = fixture.Context.OrderStatusHistories.Where(h => h.OrderId == order.Id).OrderBy(h => h.CreatedAtUtc).ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal(OrderStatus.Placed, history[1].FromStatus);
        Assert.Equal(OrderStatus.Cancelled, history[1].ToStatus);
        Assert.Equal("admin-1", history[1].CreatedBy);

        fixture.Inventory.Verify(
            i => i.ReleaseReservationAsync(reservation.Id, It.IsAny<CancellationToken>()),
            Times.Once);

        var usage = fixture.Context.CouponUsages.AsNoTracking().Single(u => u.OrderId == "ORD-2026-000001");
        Assert.NotNull(usage.VoidedAtUtc);
    }

    [Fact]
    public async Task CancelOrderAsync_PaidOrder_IsRefusedWithoutPaymentOperation()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001", payment: PaymentStatus.Paid);
        var service = fixture.CreateService();

        var result = await service.CancelOrderAsync(order.Id, "refund first", "admin-1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("refund", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Completed)]
    public async Task CancelOrderAsync_ProgressedOrder_IsRefused(OrderStatus status)
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001", status: status);
        var service = fixture.CreateService();

        var result = await service.CancelOrderAsync(order.Id, null, "admin-1", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CancelOrderAsync_AlreadyCancelled_IsRefused()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001", status: OrderStatus.Cancelled);
        var service = fixture.CreateService();

        var result = await service.CancelOrderAsync(order.Id, null, "admin-1", CancellationToken.None);

        Assert.False(result.Success);
    }

    // ---- Notes ----

    [Fact]
    public async Task AddNoteAsync_StoresInternalAndCustomerNotes()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001");
        var service = fixture.CreateService();

        var internalResult = await service.AddNoteAsync(order.Id, new AddOrderNoteRequest("staff reminder", true), "admin-1", CancellationToken.None);
        var customerResult = await service.AddNoteAsync(order.Id, new AddOrderNoteRequest("we shipped your order", false), "admin-1", CancellationToken.None);

        Assert.True(internalResult.Success);
        Assert.True(customerResult.Success);

        var notes = fixture.Context.OrderNotes.Where(n => n.OrderId == order.Id).ToList();
        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.IsInternal && n.Note == "staff reminder");
        Assert.Contains(notes, n => !n.IsInternal && n.Note == "we shipped your order");
        Assert.All(notes, n => Assert.Equal("admin-1", n.CreatedBy));
    }

    [Fact]
    public async Task AddNoteAsync_EmptyOrTooLongNote_IsRefused()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001");
        var service = fixture.CreateService();

        var empty = await service.AddNoteAsync(order.Id, new AddOrderNoteRequest("   ", true), "admin-1", CancellationToken.None);
        Assert.False(empty.Success);

        var tooLong = await service.AddNoteAsync(order.Id, new AddOrderNoteRequest(new string('x', 2001), true), "admin-1", CancellationToken.None);
        Assert.False(tooLong.Success);
    }

    // ---- Export ----

    [Fact]
    public async Task ExportOrdersAsync_ReturnsCsvMatchingFilters()
    {
        var fixture = new Fixture();
        SeedOrder(fixture.Context, "ORD-2026-000001");
        SeedOrder(fixture.Context, "ORD-2026-000002");
        var service = fixture.CreateService();

        var result = await service.ExportOrdersAsync(Query(), CancellationToken.None);

        Assert.EndsWith(".csv", result.FileName);
        Assert.Contains("OrderNumber,InvoiceNumber,Customer,Email", result.Csv);
        Assert.Contains("ORD-2026-000001", result.Csv);
        Assert.Contains("ORD-2026-000002", result.Csv);
        Assert.Contains("Jane Doe", result.Csv);
    }
}
