using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Checkout;
using FashionStore.Application.DTOs.Orders;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.DTOs.Promotions;
using FashionStore.Application.DTOs.Shipping;
using FashionStore.Application.Email;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FashionStore.UnitTests.Services;

public class OrderPlacementServiceTests
{
    private static CartItemDto Item(
        Guid? variantId = null,
        decimal unitPrice = 50m,
        int quantity = 1) =>
        new(
            CartItemId: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            VariantId: variantId ?? Guid.NewGuid(),
            ProductName: "Classic Tee",
            Slug: "classic-tee",
            BrandName: null,
            ImageUrl: "/img/tee.jpg",
            ImageCardUrl: null,
            ImageAltText: null,
            Sku: "TEE-001",
            ColourName: "Black",
            SizeName: "M",
            UnitPrice: unitPrice,
            CompareAtPrice: null,
            DiscountPercent: null,
            Quantity: quantity,
            LineTotal: unitPrice * quantity,
            AvailableStock: 10,
            IsAvailable: true,
            IsInStock: true,
            IsActive: true,
            UnavailableReason: null);

    private static CheckoutLineItemDto Line(Guid variantId, decimal unitPrice = 50m, int quantity = 1) =>
        new(
            ProductId: Guid.NewGuid(),
            VariantId: variantId,
            ProductName: "Classic Tee",
            Slug: "classic-tee",
            Sku: "TEE-001",
            ColourName: "Black",
            SizeName: "M",
            ImageUrl: "/img/tee.jpg",
            UnitPrice: unitPrice,
            CompareAtPrice: null,
            Quantity: quantity,
            LineSubtotal: unitPrice * quantity,
            PromotionsDiscount: 0m,
            CouponDiscount: 0m,
            Tax: 0m,
            LineTotal: unitPrice * quantity);

    private static CheckoutCalculationInput Input(Guid variantId) =>
        new(
            UserId: "user-1",
            Items: new[] { Item(variantId) },
            CouponCode: null,
            GuestEmail: "guest@example.com",
            GuestPhone: null,
            ShippingAddress: new CheckoutAddressInput(
                null,
                "Jane Doe",
                "555-0100",
                "1 Main Street",
                null,
                null,
                "New York",
                "NY",
                "10001",
                "US",
                null),
            BillingAddress: null,
            BillingSameAsShipping: true,
            ShippingMethodId: Guid.NewGuid(),
            PaymentMethodCode: "card",
            TermsAccepted: true,
            ContinuationToken: null);

    private sealed class Fixture
    {
        public AppDbContext Context { get; }
        public Mock<ICheckoutCalculationService> Checkout { get; } = new();
        public Mock<IDiscountService> Discount { get; } = new();
        public Mock<IInventoryService> Inventory { get; } = new();
        public Mock<ICustomerOrderService> CustomerOrders { get; } = new();
        public Mock<IEmailNotificationService> EmailService { get; } = new();

        public Fixture()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"fashionstore-order-{Guid.NewGuid()}")
                .Options;
            Context = new AppDbContext(options);

            Inventory
                .Setup(i => i.ReserveStockAsync(
                    It.IsAny<Application.DTOs.Inventory.CreateStockReservationRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Application.DTOs.Inventory.CreateStockReservationRequest r, CancellationToken _) =>
                    new Application.DTOs.Inventory.StockReservationDto(
                        Guid.NewGuid(),
                        r.VariantId,
                        "TEE-001",
                        null,
                        r.Quantity,
                        r.CartReference,
                        DateTime.UtcNow.AddMinutes(r.ExpirationMinutes),
                        Domain.Enums.StockReservationStatus.Active,
                        DateTime.UtcNow,
                        null));
        }

        public OrderPlacementService CreateService()
        {
            return new OrderPlacementService(
                Context,
                Checkout.Object,
                Discount.Object,
                Inventory.Object,
                CustomerOrders.Object,
                EmailService.Object,
                Options.Create(new OrderSettings { CodReservationMinutes = 4320, OnlineReservationMinutes = 30 }),
                NullLogger<OrderPlacementService>.Instance);
        }

        public void SetupValidCalculation(Guid variantId, decimal unitPrice = 50m, int quantity = 1)
        {
            var line = Line(variantId, unitPrice, quantity);
            Checkout
                .Setup(c => c.CalculateAsync(It.IsAny<CheckoutCalculationInput>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CheckoutCalculationResult(
                    true,
                    Array.Empty<CheckoutValidationError>(),
                    Array.Empty<string>(),
                    new[] { line },
                    Array.Empty<ShippingQuoteDto>(),
                    null,
                    new CheckoutTotalsDto(unitPrice * quantity, 0m, 0m, 0m, 0m, unitPrice * quantity, unitPrice * quantity, "USD", false),
                    new CheckoutTaxBreakdownDto(0m, unitPrice * quantity, 0m, "USD"),
                    Array.Empty<DiscountBreakdownItem>(),
                    "token-valid",
                    false));
        }

        public Guid SetupStock(Guid variantId, int onHand)
        {
            var variant = new ProductVariant
            {
                ProductId = Guid.NewGuid(),
                Sku = "TEE-001",
                Price = 50m,
                IsActive = true,
                StockQuantity = onHand,
                ReservedStock = 0
            };
            Context.ProductVariants.Add(variant);
            Context.SaveChanges();
            return variant.Id;
        }
    }

    [Fact]
    public async Task PlaceOrder_ValidInput_CreatesOrderAndIdempotencyRecord()
    {
        var fixture = new Fixture();
        var variantId = fixture.SetupStock(Guid.NewGuid(), 10);
        fixture.SetupValidCalculation(variantId);
        var service = fixture.CreateService();

        var result = await service.PlaceOrderAsync(Input(variantId), "key-1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.IsDuplicate);
        Assert.False(string.IsNullOrEmpty(result.OrderNumber));
        Assert.Equal(50m, result.GrandTotal);

        Assert.Single(await fixture.Context.Orders.ToListAsync());
        Assert.Single(await fixture.Context.OrderIdempotencyRecords.ToListAsync());
        Assert.Single(await fixture.Context.OrderStatusHistories.ToListAsync());
        Assert.Equal(1, await fixture.Context.OrderItems.CountAsync());
    }

    [Fact]
    public async Task PlaceOrder_SameKeyTwice_ReturnsExistingOrder()
    {
        var fixture = new Fixture();
        var variantId = fixture.SetupStock(Guid.NewGuid(), 10);
        fixture.SetupValidCalculation(variantId);
        var service = fixture.CreateService();

        var first = await service.PlaceOrderAsync(Input(variantId), "key-same", CancellationToken.None);
        var second = await service.PlaceOrderAsync(Input(variantId), "key-same", CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.True(second.IsDuplicate);
        Assert.Equal(first.OrderNumber, second.OrderNumber);
        Assert.Equal(1, await fixture.Context.Orders.CountAsync());
    }

    [Fact]
    public async Task PlaceOrder_StaleTotals_RefusesPlacement()
    {
        var fixture = new Fixture();
        var variantId = fixture.SetupStock(Guid.NewGuid(), 10);
        fixture.SetupValidCalculation(variantId);
        fixture.Checkout
            .Setup(c => c.CalculateAsync(It.IsAny<CheckoutCalculationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutCalculationResult(
                true,
                Array.Empty<CheckoutValidationError>(),
                new[] { "Prices changed." },
                Array.Empty<CheckoutLineItemDto>(),
                Array.Empty<ShippingQuoteDto>(),
                null,
                new CheckoutTotalsDto(0m, 0m, 0m, 0m, 0m, 0m, 0m, "USD", false),
                new CheckoutTaxBreakdownDto(0m, 0m, 0m, "USD"),
                Array.Empty<DiscountBreakdownItem>(),
                "token-stale",
                true));
        var service = fixture.CreateService();

        var result = await service.PlaceOrderAsync(Input(variantId), "key-stale", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Code == "prices-changed");
        Assert.Equal(0, await fixture.Context.Orders.CountAsync());
    }

    [Fact]
    public async Task PlaceOrder_InsufficientStock_ReturnsStockError()
    {
        var fixture = new Fixture();
        var variantId = fixture.SetupStock(Guid.NewGuid(), 1);
        fixture.SetupValidCalculation(variantId, quantity: 2);
        var service = fixture.CreateService();

        // The checkout engine reports a valid calculation for 2 units, but only 1
        // unit is on hand at placement time.
        fixture.Checkout
            .Setup(c => c.CalculateAsync(It.IsAny<CheckoutCalculationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutCalculationResult(
                true,
                Array.Empty<CheckoutValidationError>(),
                Array.Empty<string>(),
                new[] { Line(variantId, quantity: 2) },
                Array.Empty<ShippingQuoteDto>(),
                null,
                new CheckoutTotalsDto(100m, 0m, 0m, 0m, 0m, 100m, 100m, "USD", false),
                new CheckoutTaxBreakdownDto(0m, 100m, 0m, "USD"),
                Array.Empty<DiscountBreakdownItem>(),
                "token-valid",
                false));

        var result = await service.PlaceOrderAsync(Input(variantId), "key-stock", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Code == "stock");
        Assert.Equal(0, await fixture.Context.Orders.CountAsync());
    }

    [Fact]
    public async Task GetByPublicOrderNumber_UnknownNumber_ReturnsNull()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var result = await service.GetByPublicOrderNumberAsync("ORD-0000-000000", CancellationToken.None);

        Assert.Null(result);
    }
}
