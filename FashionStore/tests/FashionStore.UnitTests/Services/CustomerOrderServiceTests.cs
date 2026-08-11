using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Orders;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FashionStore.UnitTests.Services;

public class CustomerOrderServiceTests
{
    private static readonly OrderSettings Settings = new()
    {
        GuestAccessTokenSecret = "test-secret-key",
        GuestAccessTokenMinutes = 60
    };

    private sealed class Fixture
    {
        public AppDbContext Context { get; }
        public Mock<IInventoryService> Inventory { get; } = new();

        public Fixture()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"fashionstore-customer-orders-{Guid.NewGuid()}")
                .Options;
            Context = new AppDbContext(options);

            Inventory
                .Setup(i => i.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        public CustomerOrderService CreateService() =>
            new(Context, Inventory.Object, Options.Create(Settings), NullLogger<CustomerOrderService>.Instance);
    }

    private static Order SeedOrder(
        AppDbContext context,
        string number = "ORD-2026-000001",
        string? userId = "user-1",
        string? guestEmail = null,
        OrderStatus status = OrderStatus.Placed,
        PaymentStatus payment = PaymentStatus.Unpaid,
        bool withAddress = true,
        bool withHistory = true)
    {
        var order = new Order
        {
            PublicOrderNumber = number,
            UserId = userId,
            GuestEmail = guestEmail,
            GuestPhone = null,
            CustomerName = "Jane Doe",
            Currency = "USD",
            Subtotal = 100m,
            ProductDiscount = 0m,
            CouponDiscount = 0m,
            ShippingCharge = 10m,
            Tax = 5m,
            GrandTotal = 115m,
            PaymentMethodCode = "card",
            ShippingMethodName = "Standard",
            OrderStatus = status,
            PaymentStatus = payment,
            FulfilmentStatus = FulfilmentStatus.Unfulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

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
            CompareAtPrice = 120m,
            Discount = 0m,
            Tax = 5m,
            Quantity = 1,
            LineTotal = 100m
        });

        if (withAddress)
        {
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
            order.BillingAddress = new OrderAddress
            {
                AddressType = OrderAddressType.Billing,
                RecipientName = "Jane Doe",
                AddressLine1 = "1 Main Street",
                City = "New York",
                Region = "NY",
                PostalCode = "10001",
                CountryCode = "US"
            };
        }

        if (withHistory)
        {
            order.StatusHistory.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                FromStatus = null,
                ToStatus = OrderStatus.Placed,
                Note = "Order placed",
                CreatedBy = "Jane Doe",
                CreatedAtUtc = order.CreatedAtUtc
            });
        }

        context.Orders.Add(order);
        context.SaveChanges();
        return order;
    }

    private static ProductVariant SeedVariant(
        AppDbContext context,
        int stockQuantity = 10,
        int reserved = 0,
        bool active = true)
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
            IsActive = active
        };
        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = "SW-1001-GREY-M",
            Price = 100m,
            IsActive = active,
            StockQuantity = stockQuantity,
            ReservedStock = reserved
        };
        product.Variants.Add(variant);
        context.Products.Add(product);
        context.SaveChanges();
        return variant;
    }

    private static StockReservation SeedReservation(
        AppDbContext context,
        Guid variantId,
        string orderNumber,
        StockReservationStatus status = StockReservationStatus.Active)
    {
        var reservation = new StockReservation
        {
            ProductVariantId = variantId,
            Quantity = 1,
            CartReference = orderNumber,
            ReferenceId = orderNumber,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            Status = status
        };
        context.StockReservations.Add(reservation);
        context.SaveChanges();
        return reservation;
    }

    // ---- List / ownership ----

    [Fact]
    public async Task GetCustomerOrdersAsync_OnlyReturnsCallersOwnOrders()
    {
        var fixture = new Fixture();
        SeedOrder(fixture.Context, "ORD-2026-000001", userId: "user-1");
        SeedOrder(fixture.Context, "ORD-2026-000002", userId: "user-2");
        var service = fixture.CreateService();

        var result = await service.GetCustomerOrdersAsync("user-1", new CustomerOrderQueryRequest(null, null), CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("ORD-2026-000001", result.Items[0].PublicOrderNumber);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetCustomerOrdersAsync_SearchMatchesOrderNumberOrProductName()
    {
        var fixture = new Fixture();
        SeedOrder(fixture.Context, "ORD-2026-000001", userId: "user-1");
        SeedOrder(fixture.Context, "ORD-2026-000002", userId: "user-1");
        var service = fixture.CreateService();

        var byNumber = await service.GetCustomerOrdersAsync(
            "user-1",
            new CustomerOrderQueryRequest("ORD-2026-000002", null),
            CancellationToken.None);
        var byProduct = await service.GetCustomerOrdersAsync(
            "user-1",
            new CustomerOrderQueryRequest("Cashmere", null),
            CancellationToken.None);

        Assert.Single(byNumber.Items);
        Assert.Equal("ORD-2026-000002", byNumber.Items[0].PublicOrderNumber);
        Assert.Equal(2, byProduct.TotalCount);
    }

    [Fact]
    public async Task GetCustomerOrdersAsync_StatusFilterNarrowsResults()
    {
        var fixture = new Fixture();
        SeedOrder(fixture.Context, "ORD-2026-000001", userId: "user-1", status: OrderStatus.Shipped);
        SeedOrder(fixture.Context, "ORD-2026-000002", userId: "user-1", status: OrderStatus.Delivered);
        var service = fixture.CreateService();

        var result = await service.GetCustomerOrdersAsync(
            "user-1",
            new CustomerOrderQueryRequest(null, OrderStatus.Shipped),
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("ORD-2026-000001", result.Items[0].PublicOrderNumber);
    }

    // ---- Detail / ownership ----

    [Fact]
    public async Task GetOrderDetailAsync_ReturnsOwnOrderWithTimelineAndItems()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001", userId: "user-1");
        var service = fixture.CreateService();

        var detail = await service.GetOrderDetailAsync("user-1", "ORD-2026-000001", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(order.Id, detail.OrderId);
        Assert.Equal("ORD-2026-000001", detail.PublicOrderNumber);
        Assert.Single(detail.Items);
        Assert.Equal("Cashmere Sweater", detail.Items[0].ProductName);
        Assert.Equal("Grey", detail.Items[0].ColourName);
        Assert.Equal("M", detail.Items[0].SizeName);
        Assert.Equal(100m, detail.Items[0].UnitPrice);
        Assert.NotNull(detail.ShippingAddress);
        Assert.NotNull(detail.Delivery);
        Assert.Equal("Jane Doe", detail.Delivery.RecipientName);
        Assert.Single(detail.Timeline);
        Assert.Equal(OrderStatus.Placed.ToString(), detail.Timeline[0].ToStatus);
        Assert.True(detail.CanCancel);
    }

    [Fact]
    public async Task GetOrderDetailAsync_AnotherUsersOrder_ReturnsNull()
    {
        var fixture = new Fixture();
        SeedOrder(fixture.Context, "ORD-2026-000001", userId: "user-1");
        var service = fixture.CreateService();

        var detail = await service.GetOrderDetailAsync("user-2", "ORD-2026-000001", CancellationToken.None);

        Assert.Null(detail);
    }

    [Fact]
    public async Task GetOrderDetailAsync_UnknownNumber_ReturnsNull()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var detail = await service.GetOrderDetailAsync("user-1", "ORD-0000-000000", CancellationToken.None);

        Assert.Null(detail);
    }

    [Fact]
    public async Task GetGuestOrderDetailAsync_GuestOrderReturnsDetail()
    {
        var fixture = new Fixture();
        SeedOrder(fixture.Context, "ORD-2026-000003", userId: null, guestEmail: "guest@example.com");
        var service = fixture.CreateService();

        var detail = await service.GetGuestOrderDetailAsync("ORD-2026-000003", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.True(detail.IsGuest);
    }

    [Fact]
    public async Task GetGuestOrderDetailAsync_SignedInOrder_ReturnsNull()
    {
        var fixture = new Fixture();
        SeedOrder(fixture.Context, "ORD-2026-000001", userId: "user-1");
        var service = fixture.CreateService();

        var detail = await service.GetGuestOrderDetailAsync("ORD-2026-000001", CancellationToken.None);

        Assert.Null(detail);
    }

    // ---- Guest lookup ----

    [Fact]
    public async Task VerifyGuestLookupAsync_MatchesEmail_ReturnsSignedToken()
    {
        var fixture = new Fixture();
        SeedOrder(fixture.Context, "ORD-2026-000003", userId: null, guestEmail: "guest@example.com");
        var service = fixture.CreateService();

        var result = await service.VerifyGuestLookupAsync("ORD-2026-000003", "GUEST@example.com", CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Token);
        Assert.Equal("ORD-2026-000003", result.OrderNumber);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task VerifyGuestLookupAsync_WrongEmail_FailsAmbiguously()
    {
        var fixture = new Fixture();
        SeedOrder(fixture.Context, "ORD-2026-000003", userId: null, guestEmail: "guest@example.com");
        var service = fixture.CreateService();

        var result = await service.VerifyGuestLookupAsync("ORD-2026-000003", "other@example.com", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task VerifyGuestLookupAsync_UnknownOrder_FailsAmbiguously()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var result = await service.VerifyGuestLookupAsync("ORD-0000-000000", "guest@example.com", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ValidateGuestToken_ValidToken_ReturnsOrderNumber()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var token = IssueTokenForTest("ORD-2026-000003", minutes: 60);
        var orderNumber = service.ValidateGuestToken(token, "ORD-2026-000003");

        Assert.Equal("ORD-2026-000003", orderNumber);
    }

    [Fact]
    public void ValidateGuestToken_ExpiredToken_ReturnsNull()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var token = IssueTokenForTest("ORD-2026-000003", minutes: -60);
        var orderNumber = service.ValidateGuestToken(token, "ORD-2026-000003");

        Assert.Null(orderNumber);
    }

    [Fact]
    public void ValidateGuestToken_TokenBoundToDifferentOrder_ReturnsNull()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var token = IssueTokenForTest("ORD-2026-000001", minutes: 60);
        var orderNumber = service.ValidateGuestToken(token, "ORD-2026-000003");

        Assert.Null(orderNumber);
    }

    [Fact]
    public void ValidateGuestToken_TamperedSignature_ReturnsNull()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var token = IssueTokenForTest("ORD-2026-000003", minutes: 60) + "tampered";
        var orderNumber = service.ValidateGuestToken(token, "ORD-2026-000003");

        Assert.Null(orderNumber);
    }

    // ---- Cancellation ----

    [Fact]
    public async Task CancelAsync_PlacedUnpaidOrder_RecordsHistoryReleasesStockAndVoidsCoupon()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000001", userId: "user-1");
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
        var couponUsage = new CouponUsage
        {
            CouponId = coupon.Id,
            UserId = "user-1",
            OrderId = "ORD-2026-000001",
            AmountDiscounted = 10m,
            UsedAtUtc = DateTime.UtcNow
        };
        fixture.Context.CouponUsages.Add(couponUsage);
        fixture.Context.SaveChanges();

        var service = fixture.CreateService();
        var result = await service.CancelAsync(
            "ORD-2026-000001",
            OrderCancellationReason.ChangedMind,
            "user-1",
            "Jane Doe",
            CancellationToken.None);

        Assert.True(result.Success);

        var refreshed = fixture.Context.Orders.AsNoTracking().Single(o => o.PublicOrderNumber == "ORD-2026-000001");
        Assert.Equal(OrderStatus.Cancelled, refreshed.OrderStatus);
        Assert.NotNull(refreshed.CancelledAtUtc);
        Assert.Equal(OrderCancellationReason.ChangedMind.ToString(), refreshed.CancelledReasonCode);

        var history = fixture.Context.OrderStatusHistories
            .Where(h => h.OrderId == order.Id)
            .OrderBy(h => h.CreatedAtUtc)
            .ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal(OrderStatus.Placed, history[0].ToStatus);
        Assert.Equal(OrderStatus.Placed, history[1].FromStatus);
        Assert.Equal(OrderStatus.Cancelled, history[1].ToStatus);
        Assert.Equal("Jane Doe", history[1].CreatedBy);

        fixture.Inventory.Verify(
            i => i.ReleaseReservationAsync(reservation.Id, It.IsAny<CancellationToken>()),
            Times.Once);

        var usage = fixture.Context.CouponUsages.AsNoTracking().Single(u => u.OrderId == "ORD-2026-000001");
        Assert.NotNull(usage.VoidedAtUtc);
    }

    [Fact]
    public async Task CancelAsync_UnknownOrder_ReturnsFailure()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var result = await service.CancelAsync(
            "ORD-0000-000000",
            OrderCancellationReason.Other,
            "user-1",
            null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Message);
    }

    [Theory]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Completed)]
    public async Task CancelAsync_ProgressedOrder_RefusesCancellation(OrderStatus status)
    {
        var fixture = new Fixture();
        SeedOrder(fixture.Context, "ORD-2026-000001", userId: "user-1", status: status);
        var service = fixture.CreateService();

        var result = await service.CancelAsync(
            "ORD-2026-000001",
            OrderCancellationReason.ChangedMind,
            "user-1",
            null,
            CancellationToken.None);

        Assert.False(result.Success);
        var order = fixture.Context.Orders.AsNoTracking().Single(o => o.PublicOrderNumber == "ORD-2026-000001");
        Assert.Equal(status, order.OrderStatus);
        Assert.Null(order.CancelledAtUtc);
    }

    [Theory]
    [InlineData(PaymentStatus.Paid)]
    [InlineData(PaymentStatus.PartiallyPaid)]
    public async Task CancelAsync_PaidOrder_RefusesCancellation(PaymentStatus payment)
    {
        var fixture = new Fixture();
        SeedOrder(fixture.Context, "ORD-2026-000001", userId: "user-1", payment: payment);
        var service = fixture.CreateService();

        var result = await service.CancelAsync(
            "ORD-2026-000001",
            OrderCancellationReason.ChangedMind,
            "user-1",
            null,
            CancellationToken.None);

        Assert.False(result.Success);
        var order = fixture.Context.Orders.AsNoTracking().Single(o => o.PublicOrderNumber == "ORD-2026-000001");
        Assert.Equal(OrderStatus.Placed, order.OrderStatus);
    }

    [Fact]
    public async Task CancelAsync_AlreadyCancelled_RefusesAgain()
    {
        var fixture = new Fixture();
        SeedOrder(fixture.Context, "ORD-2026-000001", userId: "user-1", status: OrderStatus.Cancelled);
        var service = fixture.CreateService();

        var result = await service.CancelAsync(
            "ORD-2026-000001",
            OrderCancellationReason.Other,
            "user-1",
            null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("already", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Buy again ----

    [Fact]
    public async Task GetBuyAgainAsync_AvailableVariant_MarksAvailable()
    {
        var fixture = new Fixture();
        var variant = SeedVariant(fixture.Context, stockQuantity: 10, reserved: 0);
        SeedOrderWithVariant(fixture.Context, "ORD-2026-000001", variant.Id);
        var service = fixture.CreateService();

        var items = await service.GetBuyAgainAsync("ORD-2026-000001", CancellationToken.None);

        var item = Assert.Single(items);
        Assert.True(item.IsAvailable);
        Assert.Null(item.UnavailableReason);
        Assert.Equal(variant.Id, item.VariantId);
    }

    [Fact]
    public async Task GetBuyAgainAsync_MissingVariant_MarksUnavailable()
    {
        var fixture = new Fixture();
        // Order line references a variant that no longer exists in the catalogue.
        SeedOrder(fixture.Context, "ORD-2026-000001", userId: "user-1");
        var service = fixture.CreateService();

        var items = await service.GetBuyAgainAsync("ORD-2026-000001", CancellationToken.None);

        var item = Assert.Single(items);
        Assert.False(item.IsAvailable);
        Assert.NotNull(item.UnavailableReason);
    }

    [Fact]
    public async Task GetBuyAgainAsync_InactiveVariant_MarksUnavailable()
    {
        var fixture = new Fixture();
        var variant = SeedVariant(fixture.Context, stockQuantity: 10, reserved: 0, active: false);
        SeedOrderWithVariant(fixture.Context, "ORD-2026-000001", variant.Id);
        var service = fixture.CreateService();

        var items = await service.GetBuyAgainAsync("ORD-2026-000001", CancellationToken.None);

        var item = Assert.Single(items);
        Assert.False(item.IsAvailable);
        Assert.NotNull(item.UnavailableReason);
    }

    [Fact]
    public async Task GetBuyAgainAsync_InsufficientStock_MarksUnavailableWithReason()
    {
        var fixture = new Fixture();
        // Order line wants 2 units but only 1 is available after reservations.
        var variant = SeedVariant(fixture.Context, stockQuantity: 3, reserved: 2);
        SeedOrderWithVariant(fixture.Context, "ORD-2026-000001", variant.Id, quantity: 2);
        var service = fixture.CreateService();

        var items = await service.GetBuyAgainAsync("ORD-2026-000001", CancellationToken.None);

        var item = Assert.Single(items);
        Assert.False(item.IsAvailable);
        Assert.Contains("stock", item.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Helpers ----

    private static Order SeedOrderWithVariant(
        AppDbContext context,
        string number,
        Guid variantId,
        int quantity = 1)
    {
        var order = new Order
        {
            PublicOrderNumber = number,
            UserId = "user-1",
            Currency = "USD",
            Subtotal = 100m,
            GrandTotal = 100m,
            OrderStatus = OrderStatus.Placed,
            PaymentStatus = PaymentStatus.Unpaid,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        order.Items.Add(new OrderItem
        {
            ProductId = Guid.NewGuid(),
            ProductVariantId = variantId,
            ProductName = "Cashmere Sweater",
            ProductSlug = "cashmere-sweater",
            Sku = "SW-1001-GREY-M",
            ColourName = "Grey",
            SizeName = "M",
            UnitPrice = 100m,
            Quantity = quantity,
            LineTotal = 100m * quantity
        });
        context.Orders.Add(order);
        context.SaveChanges();
        return order;
    }

    private static string IssueTokenForTest(string orderNumber, int minutes)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(Settings.GuestAccessTokenSecret));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new { n = orderNumber, exp = now + minutes * 60L }));
        var payload = Convert.ToBase64String(payloadBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var sig = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return $"{payload}.{sig}";
    }
}
