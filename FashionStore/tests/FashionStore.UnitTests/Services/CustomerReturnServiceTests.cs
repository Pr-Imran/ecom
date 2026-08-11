using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Returns;
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

public class CustomerReturnServiceTests
{
    private static readonly ReturnSettings Settings = new()
    {
        ReturnNumberPrefix = "RMA",
        ReturnWindowDays = 30,
        AllowShippingRefund = true,
        MaxAttachments = 6,
        MaxAttachmentBytes = 5242880,
        AllowedAttachmentExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" }
    };

    private sealed class Fixture
    {
        public AppDbContext Context { get; }
        public Mock<IFileStorageService> FileStorage { get; } = new();

        public Fixture()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"fashionstore-customer-returns-{Guid.NewGuid()}")
                .Options;
            Context = new AppDbContext(options);
            Context.Database.EnsureCreated();
        }

        public CustomerReturnService CreateService() =>
            new(Context, FileStorage.Object, Options.Create(Settings), NullLogger<CustomerReturnService>.Instance);
    }

    private static Order SeedOrder(
        AppDbContext context,
        string number = "ORD-2026-000001",
        string? userId = "user-1",
        string? guestEmail = null,
        bool returnable = true)
    {
        var order = new Order
        {
            PublicOrderNumber = number,
            UserId = userId,
            GuestEmail = guestEmail,
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
            OrderStatus = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Paid,
            FulfilmentStatus = FulfilmentStatus.Fulfilled,
            DeliveredAtUtc = DateTime.UtcNow,
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
            Quantity = 2,
            LineTotal = 200m
        });

        context.Orders.Add(order);
        context.SaveChanges();

        if (returnable)
        {
            SeedProduct(context, order.Items.First().ProductId!.Value);
        }

        return order;
    }

    private static void SeedProduct(AppDbContext context, Guid productId)
    {
        context.Products.Add(new Product
        {
            Id = productId,
            Name = "Cashmere Sweater",
            Slug = "cashmere-sweater",
            CategoryId = Guid.NewGuid(),
            ProductType = "Standard",
            BaseSku = "SW-1001",
            BasePrice = 100m,
            TaxCategory = "Standard",
            IsActive = true,
            IsReturnable = true,
            ReturnWindowDays = null
        });
        context.SaveChanges();
    }

    private static ReturnRequest SeedReturn(
        AppDbContext context,
        Order order,
        string returnNumber = "RMA-20260811-000001",
        ReturnStatus status = ReturnStatus.Requested,
        int quantity = 1,
        string? userId = "user-1")
    {
        var returnRequest = new ReturnRequest
        {
            ReturnNumber = returnNumber,
            OrderId = order.Id,
            UserId = userId,
            GuestEmail = order.GuestEmail,
            CustomerName = order.CustomerName,
            Currency = order.Currency,
            Status = status,
            ReasonCode = ReturnReasonCode.ChangedMind,
            IsExchange = false,
            RefundableAmount = 105m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var item = order.Items.First();
        returnRequest.Items.Add(new ReturnItem
        {
            ReturnRequestId = returnRequest.Id,
            OrderItemId = item.Id,
            ProductId = item.ProductId,
            ProductVariantId = item.ProductVariantId,
            ProductName = item.ProductName,
            Sku = item.Sku,
            UnitPrice = item.UnitPrice,
            Discount = item.Discount,
            Tax = item.Tax,
            Quantity = quantity,
            PurchasedQuantity = item.Quantity,
            RefundableAmount = 105m,
            Condition = ReturnItemCondition.Undetermined
        });

        context.ReturnRequests.Add(returnRequest);
        context.SaveChanges();
        return returnRequest;
    }

    // ---- GetReturnableItemsAsync ----

    [Fact]
    public async Task GetReturnableItems_ReturnsScopedItemsWithQuantityAndRefundableAmount()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var service = fixture.CreateService();

        var result = await service.GetReturnableItemsAsync(order.PublicOrderNumber, "user-1", CancellationToken.None);

        Assert.True(result.WithinWindow);
        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.True(item.IsReturnable);
        Assert.Equal(2, item.QuantityAvailable);
        Assert.Equal(2, item.Quantity);
        // UnitPrice 100 * 2 - 0 discount + 5 tax = 205, / 2 = 102.50 per unit
        Assert.Equal(205m, item.RefundableAmount);
    }

    [Fact]
    public async Task GetReturnableItems_ReturnsEmptyForForeignUser()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var service = fixture.CreateService();

        var result = await service.GetReturnableItemsAsync(order.PublicOrderNumber, "user-2", CancellationToken.None);

        Assert.False(result.WithinWindow);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetReturnableItems_FlagsItemAsNotReturnableWhenProductIsNotReturnable()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, returnable: false);
        var service = fixture.CreateService();

        var result = await service.GetReturnableItemsAsync(order.PublicOrderNumber, "user-1", CancellationToken.None);

        Assert.False(result.Items[0].IsReturnable);
        Assert.NotNull(result.Items[0].RestrictionReason);
    }

    [Fact]
    public async Task GetReturnableItems_ReducesAvailableByAlreadyClaimedQuantity()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        SeedReturn(fixture.Context, order, "RMA-20260811-000002");
        var service = fixture.CreateService();

        var result = await service.GetReturnableItemsAsync(order.PublicOrderNumber, "user-1", CancellationToken.None);

        Assert.Equal(1, result.Items[0].QuantityAvailable);
        Assert.Equal(102.50m, result.Items[0].RefundableAmount);
    }

    // ---- CreateReturnAsync ----

    [Fact]
    public async Task CreateReturn_SucceedsAndRecordsStatusHistory()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var service = fixture.CreateService();

        var request = new CreateReturnRequest(
            "ChangedMind",
            "Just not right for me.",
            false,
            new[] { new ReturnItemSelectionDto(order.Items.First().Id, 1) });

        var result = await service.CreateReturnAsync(order.PublicOrderNumber, request, "user-1", "Jane Doe", CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.ReturnNumber);

        var saved = await fixture.Context.ReturnRequests
            .Include(r => r.Items)
            .Include(r => r.StatusHistory)
            .SingleAsync(r => r.ReturnNumber == result.ReturnNumber);

        Assert.Equal(ReturnStatus.Requested, saved.Status);
        Assert.Single(saved.Items);
        Assert.Equal(1, saved.Items[0].Quantity);
        Assert.Equal(102.5m, saved.RefundableAmount);
        Assert.Single(saved.StatusHistory);
        Assert.Equal(ReturnStatus.Requested, saved.StatusHistory[0].ToStatus);
    }

    [Fact]
    public async Task CreateReturn_RejectsEmptySelection()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var service = fixture.CreateService();

        var request = new CreateReturnRequest("ChangedMind", null, false, Array.Empty<ReturnItemSelectionDto>());

        var result = await service.CreateReturnAsync(order.PublicOrderNumber, request, "user-1", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("at least one item", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateReturn_RejectsInvalidReasonCode()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var service = fixture.CreateService();

        var request = new CreateReturnRequest("NotARealReason", null, false, new[] { new ReturnItemSelectionDto(order.Items.First().Id, 1) });

        var result = await service.CreateReturnAsync(order.PublicOrderNumber, request, "user-1", null, CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CreateReturn_RejectsForeignUser()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var service = fixture.CreateService();

        var request = new CreateReturnRequest("ChangedMind", null, false, new[] { new ReturnItemSelectionDto(order.Items.First().Id, 1) });

        var result = await service.CreateReturnAsync(order.PublicOrderNumber, request, "user-2", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("do not have access", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateReturn_RejectsQuantityAbovePurchased()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var service = fixture.CreateService();

        var request = new CreateReturnRequest("ChangedMind", null, false, new[] { new ReturnItemSelectionDto(order.Items.First().Id, 3) });

        var result = await service.CreateReturnAsync(order.PublicOrderNumber, request, "user-1", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("can return up to 2", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateReturn_RejectsWhenOutsideReturnWindow()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        order.DeliveredAtUtc = DateTime.UtcNow.AddDays(-40);
        order.CreatedAtUtc = DateTime.UtcNow.AddDays(-40);
        await fixture.Context.SaveChangesAsync();
        var service = fixture.CreateService();

        var request = new CreateReturnRequest("ChangedMind", null, false, new[] { new ReturnItemSelectionDto(order.Items.First().Id, 1) });

        var result = await service.CreateReturnAsync(order.PublicOrderNumber, request, "user-1", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("outside the window", result.ErrorMessage);
    }

    // ---- GetReturnReasonsAsync ----

    [Fact]
    public async Task GetReturnReasons_ReturnsOnlyActiveReasonsOrderedBySortOrder()
    {
        var fixture = new Fixture();
        fixture.Context.ReturnReasons.AddRange(
            new ReturnReason { Code = "A", Label = "Alpha", IsActive = true, SortOrder = 2 },
            new ReturnReason { Code = "B", Label = "Beta", IsActive = true, SortOrder = 1 },
            new ReturnReason { Code = "C", Label = "Gamma", IsActive = false, SortOrder = 0 });
        await fixture.Context.SaveChangesAsync();
        var service = fixture.CreateService();

        var result = await service.GetReturnReasonsAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Beta", result[0].Label);
        Assert.Equal("Alpha", result[1].Label);
    }

    // ---- Ownership / detail ----

    [Fact]
    public async Task GetReturnDetail_ReturnsNullForForeignUser()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order);
        var service = fixture.CreateService();

        var result = await service.GetReturnDetailAsync("user-2", returnRequest.ReturnNumber, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetReturnDetail_ReturnsForOwner()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order);
        var service = fixture.CreateService();

        var result = await service.GetReturnDetailAsync("user-1", returnRequest.ReturnNumber, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(returnRequest.ReturnNumber, result.ReturnNumber);
        Assert.Single(result.Items);
        Assert.Equal("Cashmere Sweater", result.Items[0].ProductName);
    }

    // ---- MarkShippedAsync ----

    [Fact]
    public async Task MarkShipped_MovesApprovedReturnToInTransit()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Approved);
        var service = fixture.CreateService();

        var result = await service.MarkShippedAsync(returnRequest.ReturnNumber, "ups", "1Z999AA10123456784", "user-1", "Jane Doe", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nameof(ReturnStatus.InTransit), result.Status);

        var saved = await fixture.Context.ReturnRequests.SingleAsync(r => r.ReturnNumber == returnRequest.ReturnNumber);
        Assert.Equal(ReturnStatus.InTransit, saved.Status);
        Assert.Equal("1Z999AA10123456784", saved.TrackingNumber);
        Assert.Equal("ups", saved.CarrierCode);
    }

    [Fact]
    public async Task MarkShipped_RejectsWhenNotApproved()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Requested);
        var service = fixture.CreateService();

        var result = await service.MarkShippedAsync(returnRequest.ReturnNumber, null, "1Z999AA10123456784", "user-1", null, CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task MarkShipped_RejectsMissingTrackingNumber()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Approved);
        var service = fixture.CreateService();

        var result = await service.MarkShippedAsync(returnRequest.ReturnNumber, "ups", "", "user-1", null, CancellationToken.None);

        Assert.False(result.Success);
    }

    // ---- CancelAsync ----

    [Fact]
    public async Task Cancel_WithdrawsRequestedReturn()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Requested);
        var service = fixture.CreateService();

        var result = await service.CancelAsync(returnRequest.ReturnNumber, "user-1", "Jane Doe", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nameof(ReturnStatus.Closed), result.Status);

        var saved = await fixture.Context.ReturnRequests.SingleAsync(r => r.ReturnNumber == returnRequest.ReturnNumber);
        Assert.True(saved.IsWithdrawn);
        Assert.Equal(ReturnStatus.Closed, saved.Status);
    }

    [Fact]
    public async Task Cancel_RejectsReturnAlreadyInTransit()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.InTransit);
        var service = fixture.CreateService();

        var result = await service.CancelAsync(returnRequest.ReturnNumber, "user-1", null, CancellationToken.None);

        Assert.False(result.Success);
    }

    // ---- Guest flows ----

    [Fact]
    public async Task GetGuestReturnDetail_AllowsOnlyGuestReturnOnGuestOrder()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context, "ORD-2026-000010", userId: null, guestEmail: "guest@example.com");
        var returnRequest = SeedReturn(fixture.Context, order, "RMA-20260811-000010", userId: null);
        var service = fixture.CreateService();

        var result = await service.GetGuestReturnDetailAsync(order.PublicOrderNumber, returnRequest.ReturnNumber, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(returnRequest.ReturnNumber, result.ReturnNumber);
    }

    [Fact]
    public async Task GetGuestReturnDetail_ReturnsNullForSignedInReturn()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order);
        var service = fixture.CreateService();

        var result = await service.GetGuestReturnDetailAsync(order.PublicOrderNumber, returnRequest.ReturnNumber, CancellationToken.None);

        Assert.Null(result);
    }
}
