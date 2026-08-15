using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Inventory;
using FashionStore.Application.DTOs.Payments;
using FashionStore.Application.DTOs.Returns;
using FashionStore.Application.Email;
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

/// <summary>
/// Exercises the administrative return state machine: the forward-only lifecycle
/// transitions (review/approve/reject/receive/inspect/restock/refund/exchange/
/// complete), inspection condition capture, sellable-only inventory restoration,
/// idempotent gateway and manual refunds, exchange arrangement and the notes trail.
/// Every invalid jump and each business rule is verified against the persisted state.
/// </summary>
public class AdminReturnServiceTests
{
    private static readonly ReturnSettings Settings = new()
    {
        ReturnNumberPrefix = "RMA",
        RefundNumberPrefix = "RFN",
        ReturnWindowDays = 30,
        AllowShippingRefund = true,
        AllowManualRefund = true,
        AllowGatewayRefund = true,
        MaxAttachments = 6,
        MaxAttachmentBytes = 5242880,
        AllowedAttachmentExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" }
    };

    private sealed class Fixture
    {
        public AppDbContext Context { get; }
        public Mock<IInventoryService> Inventory { get; } = new();
        public Mock<IPaymentService> Payment { get; } = new();
        public Mock<IEmailNotificationService> EmailService { get; } = new();
        public Mock<IAuditService> AuditService { get; } = new();

        public Fixture()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"fashionstore-admin-returns-{Guid.NewGuid()}")
                .Options;
            Context = new AppDbContext(options);
            Context.Database.EnsureCreated();

            Inventory
                .Setup(i => i.AdjustStockAsync(It.IsAny<AdjustStockRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AdjustStockRequest request, CancellationToken _) => new WarehouseStockDto(
                    Guid.NewGuid(),
                    request.WarehouseId ?? Guid.NewGuid(),
                    "Main Warehouse",
                    request.VariantId,
                    "SW-1001-GREY-M",
                    10,
                    0,
                    10,
                    null,
                    null,
                    false,
                    DateTime.UtcNow));

            Payment
                .Setup(p => p.RefundAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<decimal>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PaymentRefundResult(true, "PR-REF-0001", null, null));
        }

        public AdminReturnService CreateService(ReturnSettings? settings = null) =>
            new(
                Context,
                Inventory.Object,
                Payment.Object,
                Options.Create(settings ?? Settings),
                EmailService.Object,
                AuditService.Object,
                NullLogger<AdminReturnService>.Instance);
    }

    private static Order SeedOrder(
        AppDbContext context,
        string number = "ORD-2026-000001",
        decimal shippingCharge = 10m)
    {
        var order = new Order
        {
            PublicOrderNumber = number,
            UserId = "user-1",
            GuestEmail = null,
            CustomerName = "Jane Doe",
            Currency = "USD",
            Subtotal = 100m,
            ProductDiscount = 0m,
            CouponDiscount = 0m,
            ShippingCharge = shippingCharge,
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
        return order;
    }

    private static Payment SeedPayment(AppDbContext context, Order order)
    {
        var payment = new Payment
        {
            OrderId = order.Id,
            ProviderCode = "card",
            PaymentMethodCode = "card",
            ProviderTransactionId = "TXN-000001",
            IdempotencyKey = Guid.NewGuid().ToString(),
            Amount = order.GrandTotal,
            Currency = order.Currency,
            State = PaymentState.Paid,
            CreatedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow
        };
        context.Payments.Add(payment);
        context.SaveChanges();
        return payment;
    }

    private static (Product Product, ProductVariant Variant) SeedActiveVariant(
        AppDbContext context,
        string sku = "SW-1002-GREY-M",
        bool active = true)
    {
        var product = new Product
        {
            Name = "Cashmere Crew Neck",
            Slug = "cashmere-crew-neck",
            CategoryId = Guid.NewGuid(),
            ProductType = "Standard",
            BaseSku = "SW-1002",
            BasePrice = 110m,
            TaxCategory = "Standard",
            IsActive = active,
            IsReturnable = true
        };
        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = sku,
            Price = 110m,
            IsActive = active,
            IsDefault = true,
            StockQuantity = 5,
            ReservedStock = 0
        };
        context.Products.Add(product);
        context.ProductVariants.Add(variant);
        context.SaveChanges();
        return (product, variant);
    }

    private static ReturnRequest SeedReturn(
        AppDbContext context,
        Order order,
        string returnNumber = "RMA-20260811-000001",
        ReturnStatus status = ReturnStatus.Requested,
        ReturnResolution resolution = ReturnResolution.None,
        int quantity = 1,
        int purchasedQuantity = 2,
        ReturnItemCondition condition = ReturnItemCondition.Undetermined,
        Guid? productVariantId = null,
        decimal refundableAmount = 105m,
        bool isExchange = false)
    {
        var returnRequest = new ReturnRequest
        {
            ReturnNumber = returnNumber,
            OrderId = order.Id,
            UserId = order.UserId,
            GuestEmail = order.GuestEmail,
            CustomerName = order.CustomerName,
            Currency = order.Currency,
            Status = status,
            ReasonCode = ReturnReasonCode.ChangedMind,
            IsExchange = isExchange,
            RefundableAmount = refundableAmount,
            Resolution = resolution,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var item = order.Items.First();
        returnRequest.Items.Add(new ReturnItem
        {
            ReturnRequestId = returnRequest.Id,
            OrderItemId = item.Id,
            ProductId = item.ProductId,
            ProductVariantId = productVariantId ?? item.ProductVariantId,
            ProductName = item.ProductName,
            Sku = item.Sku,
            ColourName = item.ColourName,
            ColourValue = item.ColourValue,
            SizeName = item.SizeName,
            ImageUrl = item.ImageUrl,
            UnitPrice = item.UnitPrice,
            Discount = item.Discount,
            Tax = item.Tax,
            Quantity = quantity,
            PurchasedQuantity = purchasedQuantity,
            RefundableAmount = refundableAmount,
            Condition = condition
        });

        returnRequest.StatusHistory.Add(new ReturnStatusHistory
        {
            FromStatus = null,
            ToStatus = ReturnStatus.Requested,
            Note = "Return requested",
            CreatedBy = order.CustomerName,
            CreatedAtUtc = returnRequest.CreatedAtUtc
        });

        context.ReturnRequests.Add(returnRequest);
        context.SaveChanges();
        return returnRequest;
    }

    private static InspectReturnRequest InspectRequest(
        Guid itemId,
        string condition = nameof(ReturnItemCondition.Sellable),
        string resolution = nameof(ReturnResolution.Refund)) =>
        new(resolution, new[] { new InspectReturnItemRequest(itemId, condition, null) }, null);

    private static RefundReturnRequest ManualRefund(string type, decimal? amount = null, string? key = null) =>
        new(type, amount, null, false, null, true, key);

    // ---- Review ----

    [Fact]
    public async Task Review_MovesRequestedReturnToUnderReview()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Requested);
        var service = fixture.CreateService();

        var result = await service.ReviewAsync(returnRequest.Id, "Looks valid", "admin-1", CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(nameof(ReturnStatus.UnderReview), result.Status);

        var saved = await fixture.Context.ReturnRequests
            .Include(r => r.StatusHistory)
            .SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(ReturnStatus.UnderReview, saved.Status);
        Assert.Contains(saved.StatusHistory, h => h.ToStatus == ReturnStatus.UnderReview && h.CreatedBy == "admin-1");
    }

    [Fact]
    public async Task Review_RejectsReturnAlreadyUnderReview()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.UnderReview);
        var service = fixture.CreateService();

        var result = await service.ReviewAsync(returnRequest.Id, null, "admin-1", CancellationToken.None);

        Assert.False(result.Success);
    }

    // ---- Approve ----

    [Fact]
    public async Task Approve_MovesUnderReviewReturnToAwaitingShipment()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.UnderReview);
        var service = fixture.CreateService();

        var result = await service.ApproveAsync(returnRequest.Id, "Approved", "admin-1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nameof(ReturnStatus.AwaitingShipment), result.Status);

        var saved = await fixture.Context.ReturnRequests.SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(ReturnStatus.AwaitingShipment, saved.Status);
        Assert.NotNull(saved.ApprovedAtUtc);
    }

    [Fact]
    public async Task Approve_RejectsReturnAlreadyRejected()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Rejected);
        var service = fixture.CreateService();

        var result = await service.ApproveAsync(returnRequest.Id, null, "admin-1", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Approve_RejectsReturnAlreadyInTransit()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.InTransit);
        var service = fixture.CreateService();

        var result = await service.ApproveAsync(returnRequest.Id, null, "admin-1", CancellationToken.None);

        Assert.False(result.Success);
    }

    // ---- Reject ----

    [Fact]
    public async Task Reject_RecordsTerminalStateAndRejectionDetails()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.UnderReview);
        var service = fixture.CreateService();

        var result = await service.RejectAsync(returnRequest.Id, "OutsideWindow", "Returned too late.", "admin-1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nameof(ReturnStatus.Rejected), result.Status);

        var saved = await fixture.Context.ReturnRequests
            .Include(r => r.StatusHistory)
            .SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(ReturnStatus.Rejected, saved.Status);
        Assert.NotNull(saved.RejectedAtUtc);
        Assert.Equal("OutsideWindow", saved.RejectionReasonCode);
        Assert.Equal("Returned too late.", saved.RejectionNote);
        Assert.Contains(saved.StatusHistory, h => h.ToStatus == ReturnStatus.Rejected && h.CreatedBy == "admin-1");
    }

    [Fact]
    public async Task Reject_RejectsReturnAlreadyReceived()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Received);
        var service = fixture.CreateService();

        var result = await service.RejectAsync(returnRequest.Id, null, null, "admin-1", CancellationToken.None);

        Assert.False(result.Success);
    }

    // ---- MarkReceived ----

    [Fact]
    public async Task MarkReceived_MovesInTransitReturnToReceived()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.InTransit);
        var service = fixture.CreateService();

        var result = await service.MarkReceivedAsync(returnRequest.Id, "Arrived", "admin-1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nameof(ReturnStatus.Received), result.Status);

        var saved = await fixture.Context.ReturnRequests.SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(ReturnStatus.Received, saved.Status);
        Assert.NotNull(saved.ReceivedAtUtc);
    }

    [Fact]
    public async Task MarkReceived_RejectsReturnNotInTransit()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.AwaitingShipment);
        var service = fixture.CreateService();

        var result = await service.MarkReceivedAsync(returnRequest.Id, null, "admin-1", CancellationToken.None);

        Assert.False(result.Success);
    }

    // ---- Inspect ----

    [Fact]
    public async Task Inspect_RecordsItemConditionAndResolution()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Received);
        var item = returnRequest.Items.Single();
        var service = fixture.CreateService();

        var result = await service.InspectAsync(
            returnRequest.Id,
            InspectRequest(item.Id, nameof(ReturnItemCondition.Sellable), nameof(ReturnResolution.Refund)),
            "admin-1",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nameof(ReturnStatus.Inspected), result.Status);

        var saved = await fixture.Context.ReturnRequests
            .Include(r => r.Items)
            .SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(ReturnStatus.Inspected, saved.Status);
        Assert.Equal(ReturnResolution.Refund, saved.Resolution);
        Assert.NotNull(saved.InspectedAtUtc);
        var savedItem = saved.Items.Single();
        Assert.Equal(ReturnItemCondition.Sellable, savedItem.Condition);
        Assert.NotNull(savedItem.InspectedAtUtc);
    }

    [Fact]
    public async Task Inspect_RejectsWhenNotEveryItemHasCondition()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Received);
        var service = fixture.CreateService();

        var request = new InspectReturnRequest(
            nameof(ReturnResolution.Refund),
            Array.Empty<InspectReturnItemRequest>(),
            null);

        var result = await service.InspectAsync(returnRequest.Id, request, "admin-1", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Inspect_RejectsInvalidResolution()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Received);
        var item = returnRequest.Items.Single();
        var service = fixture.CreateService();

        var result = await service.InspectAsync(
            returnRequest.Id,
            InspectRequest(item.Id, nameof(ReturnItemCondition.Sellable), "Nothing"),
            "admin-1",
            CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Inspect_RejectsItemThatDoesNotBelongToReturn()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Received);
        var service = fixture.CreateService();

        var request = new InspectReturnRequest(
            nameof(ReturnResolution.Refund),
            new[] { new InspectReturnItemRequest(Guid.NewGuid(), nameof(ReturnItemCondition.Sellable), null) },
            null);

        var result = await service.InspectAsync(returnRequest.Id, request, "admin-1", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Inspect_RejectsReturnThatIsNotReceived()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.InTransit);
        var item = returnRequest.Items.Single();
        var service = fixture.CreateService();

        var result = await service.InspectAsync(
            returnRequest.Id,
            InspectRequest(item.Id),
            "admin-1",
            CancellationToken.None);

        Assert.False(result.Success);
    }

    // ---- Restock ----

    [Fact]
    public async Task Restock_SellableItemAdjustsInventoryAndMarksRestocked()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Refund,
            condition: ReturnItemCondition.Sellable);
        var item = returnRequest.Items.Single();
        var service = fixture.CreateService();

        var result = await service.RestockItemAsync(
            returnRequest.Id,
            new RestockReturnItemRequest(item.Id, Guid.NewGuid(), "Back to shelf"),
            "admin-1",
            CancellationToken.None);

        Assert.True(result.Success);

        fixture.Inventory.Verify(
            i => i.AdjustStockAsync(
                It.Is<AdjustStockRequest>(r => r.VariantId == item.ProductVariantId && r.AdjustmentQuantity == item.Quantity),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var savedItem = await fixture.Context.ReturnItems.SingleAsync(i => i.Id == item.Id);
        Assert.True(savedItem.IsRestocked);
        Assert.NotNull(savedItem.RestockedAtUtc);
    }

    [Fact]
    public async Task Restock_RejectsDamagedItemAndDoesNotTouchInventory()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Refund,
            condition: ReturnItemCondition.Damaged);
        var item = returnRequest.Items.Single();
        var service = fixture.CreateService();

        var result = await service.RestockItemAsync(
            returnRequest.Id,
            new RestockReturnItemRequest(item.Id, null, null),
            "admin-1",
            CancellationToken.None);

        Assert.False(result.Success);
        fixture.Inventory.Verify(
            i => i.AdjustStockAsync(It.IsAny<AdjustStockRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var savedItem = await fixture.Context.ReturnItems.SingleAsync(i => i.Id == item.Id);
        Assert.False(savedItem.IsRestocked);
    }

    [Fact]
    public async Task Restock_RejectsAlreadyRestockedItem()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Refund,
            condition: ReturnItemCondition.Sellable);
        var item = returnRequest.Items.Single();
        item.IsRestocked = true;
        await fixture.Context.SaveChangesAsync();
        var service = fixture.CreateService();

        var result = await service.RestockItemAsync(
            returnRequest.Id,
            new RestockReturnItemRequest(item.Id, null, null),
            "admin-1",
            CancellationToken.None);

        Assert.False(result.Success);
        fixture.Inventory.Verify(
            i => i.AdjustStockAsync(It.IsAny<AdjustStockRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Restock_RejectsWhenReturnIsNotInspected()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Received);
        var item = returnRequest.Items.Single();
        var service = fixture.CreateService();

        var result = await service.RestockItemAsync(
            returnRequest.Id,
            new RestockReturnItemRequest(item.Id, null, null),
            "admin-1",
            CancellationToken.None);

        Assert.False(result.Success);
        fixture.Inventory.Verify(
            i => i.AdjustStockAsync(It.IsAny<AdjustStockRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---- Refund ----

    [Fact]
    public async Task Refund_ManualRefundCompletesWithoutGateway()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Refund,
            refundableAmount: 105m);
        var service = fixture.CreateService();

        var result = await service.RefundAsync(
            returnRequest.Id,
            ManualRefund(nameof(RefundType.Partial), 100m, "manual-1"),
            "admin-1",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nameof(ReturnStatus.Refunded), result.Status);

        fixture.Payment.Verify(
            p => p.RefundAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var saved = await fixture.Context.ReturnRequests
            .Include(r => r.Refunds).ThenInclude(rf => rf.Transactions)
            .SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(ReturnStatus.Refunded, saved.Status);
        Assert.Equal(100m, saved.RefundedAmount);
        Assert.NotNull(saved.RefundedAtUtc);

        var refund = saved.Refunds.Single();
        Assert.Equal(RefundStatus.Succeeded, refund.Status);
        Assert.Equal(RefundType.Partial, refund.Type);
        Assert.False(refund.IsGatewayRefund);
        Assert.Equal(100m, refund.Amount);
        Assert.Contains(refund.Transactions, t => t.Succeeded);
    }

    [Fact]
    public async Task Refund_GatewayRefundRunsThroughPaymentPipeline()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        SeedPayment(fixture.Context, order);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Refund,
            refundableAmount: 105m);
        var service = fixture.CreateService();

        var result = await service.RefundAsync(
            returnRequest.Id,
            new RefundReturnRequest(nameof(RefundType.Full), null, null, false, null, false, "gateway-1"),
            "admin-1",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nameof(ReturnStatus.Refunded), result.Status);

        fixture.Payment.Verify(
            p => p.RefundAsync(It.IsAny<Guid>(), 105m, "admin-1", It.IsAny<CancellationToken>()),
            Times.Once);

        var saved = await fixture.Context.ReturnRequests
            .Include(r => r.Refunds).ThenInclude(rf => rf.Transactions)
            .SingleAsync(r => r.Id == returnRequest.Id);
        var refund = saved.Refunds.Single();
        Assert.Equal(RefundStatus.Succeeded, refund.Status);
        Assert.True(refund.IsGatewayRefund);
        Assert.Equal("PR-REF-0001", refund.ProviderRefundId);
        Assert.Equal(105m, refund.Amount);
    }

    [Fact]
    public async Task Refund_IsIdempotentForTheSameKey()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Refund);
        var service = fixture.CreateService();

        var first = await service.RefundAsync(
            returnRequest.Id,
            ManualRefund(nameof(RefundType.Partial), 100m, "dup-key"),
            "admin-1",
            CancellationToken.None);
        var second = await service.RefundAsync(
            returnRequest.Id,
            ManualRefund(nameof(RefundType.Partial), 100m, "dup-key"),
            "admin-1",
            CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);

        var saved = await fixture.Context.ReturnRequests
            .Include(r => r.Refunds)
            .SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Single(saved.Refunds);
        Assert.Equal(100m, saved.RefundedAmount);
    }

    [Fact]
    public async Task Refund_RejectsReturnResolvedAsExchange()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Exchange);
        var service = fixture.CreateService();

        var result = await service.RefundAsync(
            returnRequest.Id,
            ManualRefund(nameof(RefundType.Partial), 100m),
            "admin-1",
            CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Refund_RejectsWhenManualRefundsAreDisabled()
    {
        var fixture = new Fixture();
        var settings = new ReturnSettings
        {
            ReturnNumberPrefix = "RMA",
            RefundNumberPrefix = "RFN",
            AllowManualRefund = false,
            AllowGatewayRefund = true,
            AllowShippingRefund = true
        };
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Refund);
        var service = fixture.CreateService(settings);

        var result = await service.RefundAsync(
            returnRequest.Id,
            ManualRefund(nameof(RefundType.Manual), 100m),
            "admin-1",
            CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Refund_RejectsZeroOrNegativeAmount()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Refund);
        var service = fixture.CreateService();

        var result = await service.RefundAsync(
            returnRequest.Id,
            ManualRefund(nameof(RefundType.Partial), 0m),
            "admin-1",
            CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Refund_RejectsAmountExceedingRemainingRefundable()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Refund,
            refundableAmount: 105m);
        var service = fixture.CreateService();

        var result = await service.RefundAsync(
            returnRequest.Id,
            ManualRefund(nameof(RefundType.Partial), 200m),
            "admin-1",
            CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Refund_ShippingRefundAllowedWhenReturnCoversEntireOrder()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Refund,
            quantity: 2,
            purchasedQuantity: 2,
            refundableAmount: 210m);
        var service = fixture.CreateService();

        var result = await service.RefundAsync(
            returnRequest.Id,
            ManualRefund(nameof(RefundType.Shipping)),
            "admin-1",
            CancellationToken.None);

        Assert.True(result.Success);
        var saved = await fixture.Context.ReturnRequests
            .Include(r => r.Refunds)
            .SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(10m, saved.RefundedAmount);
        Assert.Equal(10m, saved.Refunds.Single().Amount);
    }

    [Fact]
    public async Task Refund_FullRefundSumsItemRefundableAmounts()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Refund,
            refundableAmount: 105m);
        var service = fixture.CreateService();

        var result = await service.RefundAsync(
            returnRequest.Id,
            ManualRefund(nameof(RefundType.Full)),
            "admin-1",
            CancellationToken.None);

        Assert.True(result.Success);
        var saved = await fixture.Context.ReturnRequests
            .Include(r => r.Refunds)
            .SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(105m, saved.RefundedAmount);
        Assert.Equal(105m, saved.Refunds.Single().Amount);
    }

    // ---- Exchange ----

    [Fact]
    public async Task Exchange_ArrangesReplacementAndMovesToExchanged()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var (_, variant) = SeedActiveVariant(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Exchange);
        var service = fixture.CreateService();

        var result = await service.ExchangeAsync(
            returnRequest.Id,
            new ExchangeReturnRequest(variant.Id, 1, "Prefer this one"),
            "admin-1",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nameof(ReturnStatus.Exchanged), result.Status);

        var saved = await fixture.Context.ReturnRequests
            .Include(r => r.ExchangeRequests)
            .SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(ReturnStatus.Exchanged, saved.Status);
        var exchange = saved.ExchangeRequests.Single();
        Assert.Equal(ExchangeStatus.Pending, exchange.Status);
        Assert.Equal("Cashmere Crew Neck", exchange.ProductName);
        Assert.Equal(110m, exchange.UnitPrice);
        Assert.Equal(1, exchange.Quantity);
    }

    [Fact]
    public async Task Exchange_CancelsPreviouslyPendingExchanges()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var (_, variant) = SeedActiveVariant(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Exchange);
        fixture.Context.ExchangeRequests.Add(new ExchangeRequest
        {
            ReturnRequestId = returnRequest.Id,
            OrderId = order.Id,
            ProductVariantId = variant.Id,
            ProductName = "Old",
            Sku = "OLD",
            Quantity = 1,
            UnitPrice = 10m,
            Status = ExchangeStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();
        var service = fixture.CreateService();

        var result = await service.ExchangeAsync(
            returnRequest.Id,
            new ExchangeReturnRequest(variant.Id, 2, null),
            "admin-1",
            CancellationToken.None);

        Assert.True(result.Success);

        var saved = await fixture.Context.ReturnRequests
            .Include(r => r.ExchangeRequests)
            .SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(2, saved.ExchangeRequests.Count);
        Assert.Contains(saved.ExchangeRequests, e => e.Status == ExchangeStatus.Cancelled);
        Assert.Contains(saved.ExchangeRequests, e => e.Status == ExchangeStatus.Pending);
    }

    [Fact]
    public async Task Exchange_RejectsReturnResolvedAsRefund()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var (_, variant) = SeedActiveVariant(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Refund);
        var service = fixture.CreateService();

        var result = await service.ExchangeAsync(
            returnRequest.Id,
            new ExchangeReturnRequest(variant.Id, 1, null),
            "admin-1",
            CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Exchange_RejectsInactiveReplacementVariant()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var (_, variant) = SeedActiveVariant(fixture.Context, active: false);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Exchange);
        var service = fixture.CreateService();

        var result = await service.ExchangeAsync(
            returnRequest.Id,
            new ExchangeReturnRequest(variant.Id, 1, null),
            "admin-1",
            CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Exchange_RejectsZeroQuantity()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var (_, variant) = SeedActiveVariant(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Inspected,
            resolution: ReturnResolution.Exchange);
        var service = fixture.CreateService();

        var result = await service.ExchangeAsync(
            returnRequest.Id,
            new ExchangeReturnRequest(variant.Id, 0, null),
            "admin-1",
            CancellationToken.None);

        Assert.False(result.Success);
    }

    // ---- Complete ----

    [Fact]
    public async Task Complete_ClosesRefundedReturn()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Refunded);
        var service = fixture.CreateService();

        var result = await service.CompleteAsync(returnRequest.Id, "Done", "admin-1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nameof(ReturnStatus.Closed), result.Status);

        var saved = await fixture.Context.ReturnRequests.SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(ReturnStatus.Closed, saved.Status);
        Assert.NotNull(saved.CompletedAtUtc);
    }

    [Fact]
    public async Task Complete_ClosesExchangedReturnAndCompletesExchanges()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var (_, variant) = SeedActiveVariant(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Exchanged,
            resolution: ReturnResolution.Exchange);
        fixture.Context.ExchangeRequests.Add(new ExchangeRequest
        {
            ReturnRequestId = returnRequest.Id,
            OrderId = order.Id,
            ProductVariantId = variant.Id,
            ProductName = "Replacement",
            Sku = variant.Sku,
            Quantity = 1,
            UnitPrice = 110m,
            Status = ExchangeStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();
        var service = fixture.CreateService();

        var result = await service.CompleteAsync(returnRequest.Id, null, "admin-1", CancellationToken.None);

        Assert.True(result.Success);

        var saved = await fixture.Context.ReturnRequests
            .Include(r => r.ExchangeRequests)
            .SingleAsync(r => r.Id == returnRequest.Id);
        Assert.Equal(ReturnStatus.Closed, saved.Status);
        var exchange = saved.ExchangeRequests.Single();
        Assert.Equal(ExchangeStatus.Completed, exchange.Status);
        Assert.Equal("admin-1", exchange.CompletedBy);
        Assert.NotNull(exchange.CompletedAtUtc);
    }

    [Fact]
    public async Task Complete_RejectsReturnNotRefundedOrExchanged()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Inspected);
        var service = fixture.CreateService();

        var result = await service.CompleteAsync(returnRequest.Id, null, "admin-1", CancellationToken.None);

        Assert.False(result.Success);
    }

    // ---- Notes ----

    [Fact]
    public async Task UpdateNotes_AppendsTimestampedNote()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Received);
        var service = fixture.CreateService();

        var result = await service.UpdateNotesAsync(returnRequest.Id, "Customer was friendly", "admin-1", CancellationToken.None);

        Assert.True(result.Success);

        var saved = await fixture.Context.ReturnRequests.SingleAsync(r => r.Id == returnRequest.Id);
        Assert.NotNull(saved.AdminNotes);
        Assert.Contains("Customer was friendly", saved.AdminNotes);
    }

    [Fact]
    public async Task UpdateNotes_RejectsClosedReturn()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(fixture.Context, order, status: ReturnStatus.Closed);
        var service = fixture.CreateService();

        var result = await service.UpdateNotesAsync(returnRequest.Id, "Too late", "admin-1", CancellationToken.None);

        Assert.False(result.Success);
    }

    // ---- List / detail ----

    [Fact]
    public async Task GetReturns_FiltersByStatusAndSearch()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        SeedReturn(fixture.Context, order, "RMA-20260811-000001", status: ReturnStatus.Requested);
        SeedReturn(fixture.Context, order, "RMA-20260811-000002", status: ReturnStatus.Rejected);
        var service = fixture.CreateService();

        var rejected = await service.GetReturnsAsync(
            new AdminReturnQueryRequest(1, 50, ReturnStatus.Rejected),
            CancellationToken.None);
        Assert.Single(rejected.Items);
        Assert.Equal("RMA-20260811-000002", rejected.Items[0].ReturnNumber);

        var bySearch = await service.GetReturnsAsync(
            new AdminReturnQueryRequest(1, 50, null, "000002"),
            CancellationToken.None);
        Assert.Single(bySearch.Items);
        Assert.Equal("RMA-20260811-000002", bySearch.Items[0].ReturnNumber);
    }

    [Fact]
    public async Task GetReturnDetail_ReturnsItemsTimelineAndRefunds()
    {
        var fixture = new Fixture();
        var order = SeedOrder(fixture.Context);
        var returnRequest = SeedReturn(
            fixture.Context,
            order,
            status: ReturnStatus.Refunded,
            resolution: ReturnResolution.Refund);
        var service = fixture.CreateService();

        var detail = await service.GetReturnDetailAsync(returnRequest.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(returnRequest.ReturnNumber, detail.ReturnNumber);
        Assert.Single(detail.Items);
        Assert.Equal("Cashmere Sweater", detail.Items[0].ProductName);
        Assert.NotEmpty(detail.Timeline);
        Assert.Equal(order.PublicOrderNumber, detail.OrderNumber);
    }

    [Fact]
    public async Task GetReturnDetail_ReturnsNullForUnknownReturn()
    {
        var fixture = new Fixture();
        var service = fixture.CreateService();

        var detail = await service.GetReturnDetailAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(detail);
    }
}
