using System.Text.Json;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Payments;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FashionStore.UnitTests.Services;

public class PaymentServiceTests
{
    private const string CardSecret = "test-card-secret";
    private const string MfsSecret = "test-mfs-secret";
    private const string BankSecret = "test-bank-secret";

    [Fact]
    public async Task Initiate_OrderWithCard_ReturnsHostedRedirect()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        var service = fixture.CreateService();

        var placement = await service.InitiateForOrderAsync(order.Id, "/checkout/confirmation/ord", "/checkout/confirmation/ord", CancellationToken.None);

        Assert.True(placement.PaymentRequired);
        Assert.NotNull(placement.RedirectUrl);
        Assert.StartsWith("/payments/mock-hosted-checkout", placement.RedirectUrl);
        Assert.Equal("card", placement.ProviderCode);

        var payment = await fixture.Context.Payments.SingleAsync();
        Assert.Equal(PaymentState.Initiated, payment.State);
        Assert.Equal(order.GrandTotal, payment.Amount);
        Assert.NotNull(payment.ProviderTransactionId);
        Assert.StartsWith("card_", payment.ProviderTransactionId);
        Assert.Single(await fixture.Context.PaymentAttempts.ToListAsync());
    }

    [Fact]
    public async Task Initiate_OrderWithMfs_ReturnsReferenceAndInstructions()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "mfs");
        var service = fixture.CreateService();

        var placement = await service.InitiateForOrderAsync(order.Id, null, null, CancellationToken.None);

        Assert.True(placement.PaymentRequired);
        Assert.Null(placement.RedirectUrl);
        Assert.NotNull(placement.HostedCheckoutReference);
        Assert.StartsWith("MFS-", placement.HostedCheckoutReference);
        Assert.False(string.IsNullOrWhiteSpace(placement.Instructions));
    }

    [Fact]
    public async Task Initiate_ZeroTotalOrder_NoPaymentRecord()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(grandTotal: 0m, paymentMethodCode: "card");
        var service = fixture.CreateService();

        var placement = await service.InitiateForOrderAsync(order.Id, null, null, CancellationToken.None);

        Assert.False(placement.PaymentRequired);
        Assert.Equal(0, await fixture.Context.Payments.CountAsync());
    }

    [Fact]
    public async Task Initiate_SameOrderTwice_ReusesPayment()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        var service = fixture.CreateService();

        var first = await service.InitiateForOrderAsync(order.Id, null, null, CancellationToken.None);
        var second = await service.InitiateForOrderAsync(order.Id, null, null, CancellationToken.None);

        Assert.Single(await fixture.Context.Payments.ToListAsync());
        Assert.Equal(first.ProviderCode, second.ProviderCode);
        Assert.Equal(first.State, second.State);
    }

    [Fact]
    public async Task Webhook_ValidSucceeded_MarksPaymentPaid()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        var service = fixture.CreateService();
        await service.InitiateForOrderAsync(order.Id, null, null, CancellationToken.None);

        var payload = Fixture.BuildPayload("evt-success", "payment.succeeded", "card_txn", order.PublicOrderNumber, order.GrandTotal, "USD");
        var result = await Fixture.DeliverWebhookAsync(service, "card", CardSecret, payload);

        Assert.True(result.Success);
        Assert.Equal(PaymentWebhookStatus.Processed, result.Status);

        var payment = await fixture.Context.Payments.SingleAsync();
        var storedOrder = await fixture.Context.Orders.SingleAsync();
        Assert.Equal(PaymentState.Paid, payment.State);
        Assert.Equal(PaymentStatus.Paid, storedOrder.PaymentStatus);
        Assert.Equal(order.GrandTotal, storedOrder.PaidAmount);
        Assert.NotNull(storedOrder.PaidAtUtc);
        fixture.Inventory.Verify(i => i.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Webhook_ValidFailed_ReleasesStock()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        fixture.SeedReservation(order.PublicOrderNumber);
        var service = fixture.CreateService();
        await service.InitiateForOrderAsync(order.Id, null, null, CancellationToken.None);

        var payload = Fixture.BuildPayload("evt-fail", "payment.failed", "card_txn", order.PublicOrderNumber, order.GrandTotal, "USD", failureReason: "Card declined");
        var result = await Fixture.DeliverWebhookAsync(service, "card", CardSecret, payload);

        Assert.True(result.Success);
        var payment = await fixture.Context.Payments.SingleAsync();
        var storedOrder = await fixture.Context.Orders.SingleAsync();
        Assert.Equal(PaymentState.Failed, payment.State);
        Assert.Equal(PaymentStatus.Failed, storedOrder.PaymentStatus);
        Assert.Equal("Card declined", payment.FailureReason);
        fixture.Inventory.Verify(i => i.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Webhook_DuplicateEvent_AppliesOnce()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        var service = fixture.CreateService();
        await service.InitiateForOrderAsync(order.Id, null, null, CancellationToken.None);

        var payload = Fixture.BuildPayload("evt-dup", "payment.succeeded", "card_txn", order.PublicOrderNumber, order.GrandTotal, "USD");
        var first = await Fixture.DeliverWebhookAsync(service, "card", CardSecret, payload);
        var second = await Fixture.DeliverWebhookAsync(service, "card", CardSecret, payload);

        Assert.True(first.Success);
        Assert.Equal(PaymentWebhookStatus.Processed, first.Status);
        Assert.False(second.Success);
        Assert.Equal(PaymentWebhookStatus.Duplicate, second.Status);

        var payment = await fixture.Context.Payments.SingleAsync();
        Assert.Equal(PaymentState.Paid, payment.State);
        var logs = await fixture.Context.PaymentWebhookLogs
            .Where(l => l.Status == PaymentWebhookStatus.Processed && l.ProviderEventId == "evt-dup")
            .ToListAsync();
        Assert.Single(logs);
    }

    [Fact]
    public async Task Webhook_InvalidSignature_Rejected()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        var service = fixture.CreateService();
        await service.InitiateForOrderAsync(order.Id, null, null, CancellationToken.None);

        var payload = Fixture.BuildPayload("evt-bad-sig", "payment.succeeded", "card_txn", order.PublicOrderNumber, order.GrandTotal, "USD");
        var result = await Fixture.DeliverWebhookAsync(service, "card", "wrong-secret", payload);

        Assert.False(result.Success);
        Assert.Equal(PaymentWebhookStatus.InvalidSignature, result.Status);
        var payment = await fixture.Context.Payments.SingleAsync();
        Assert.Equal(PaymentState.Initiated, payment.State);
    }

    [Fact]
    public async Task Webhook_WrongAmount_Rejected()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        var service = fixture.CreateService();
        await service.InitiateForOrderAsync(order.Id, null, null, CancellationToken.None);

        var payload = Fixture.BuildPayload("evt-amount", "payment.succeeded", "card_txn", order.PublicOrderNumber, order.GrandTotal + 10m, "USD");
        var result = await Fixture.DeliverWebhookAsync(service, "card", CardSecret, payload);

        Assert.False(result.Success);
        Assert.Equal(PaymentWebhookStatus.AmountMismatch, result.Status);
        var payment = await fixture.Context.Payments.SingleAsync();
        Assert.Equal(PaymentState.Initiated, payment.State);
    }

    [Fact]
    public async Task Webhook_WrongCurrency_Rejected()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        var service = fixture.CreateService();
        await service.InitiateForOrderAsync(order.Id, null, null, CancellationToken.None);

        var payload = Fixture.BuildPayload("evt-currency", "payment.succeeded", "card_txn", order.PublicOrderNumber, order.GrandTotal, "EUR");
        var result = await Fixture.DeliverWebhookAsync(service, "card", CardSecret, payload);

        Assert.False(result.Success);
        Assert.Equal(PaymentWebhookStatus.CurrencyMismatch, result.Status);
        var payment = await fixture.Context.Payments.SingleAsync();
        Assert.Equal(PaymentState.Initiated, payment.State);
    }

    [Fact]
    public async Task Webhook_UnknownEventType_AcknowledgedWithoutTransition()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        var service = fixture.CreateService();
        await service.InitiateForOrderAsync(order.Id, null, null, CancellationToken.None);

        var payload = Fixture.BuildPayload("evt-other", "payment.hold", "card_txn", order.PublicOrderNumber, order.GrandTotal, "USD");
        var result = await Fixture.DeliverWebhookAsync(service, "card", CardSecret, payload);

        Assert.True(result.Success);
        Assert.Equal(PaymentWebhookStatus.Processed, result.Status);
        var payment = await fixture.Context.Payments.SingleAsync();
        Assert.Equal(PaymentState.Initiated, payment.State);
    }

    [Fact]
    public async Task Webhook_UnknownTransaction_Rejected()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        var service = fixture.CreateService();

        var payload = Fixture.BuildPayload("evt-unknown", "payment.succeeded", "card_txn", order.PublicOrderNumber, order.GrandTotal, "USD");
        var result = await Fixture.DeliverWebhookAsync(service, "card", CardSecret, payload);

        Assert.False(result.Success);
        Assert.Equal(PaymentWebhookStatus.UnknownTransaction, result.Status);
    }

    [Fact]
    public async Task BrowserCallback_WithoutWebhook_DoesNotMarkPaid()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        var service = fixture.CreateService();
        await service.InitiateForOrderAsync(order.Id, null, null, CancellationToken.None);

        var status = await service.HandleBrowserCallbackAsync(order.PublicOrderNumber, CancellationToken.None);

        Assert.NotNull(status);
        // The placeholder provider echoes the locally recorded state; a browser
        // redirect alone must never settle the payment.
        Assert.Equal(PaymentState.Initiated, status.State);
        Assert.False(status.OrderPaid);
        var payment = await fixture.Context.Payments.SingleAsync();
        Assert.Equal(PaymentState.Initiated, payment.State);
        var storedOrder = await fixture.Context.Orders.SingleAsync();
        Assert.Equal(PaymentStatus.Unpaid, storedOrder.PaymentStatus);
        Assert.Equal(0m, storedOrder.PaidAmount);
    }

    [Fact]
    public async Task GetStatus_ExpiredPayment_TransitionsAndReleasesStock()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        fixture.SeedReservation(order.PublicOrderNumber);
        fixture.SeedPayment(order, state: PaymentState.Initiated, expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));
        var service = fixture.CreateService();

        var status = await service.GetStatusByOrderNumberAsync(order.PublicOrderNumber, CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(PaymentState.Expired, status.State);
        var payment = await fixture.Context.Payments.SingleAsync();
        Assert.Equal(PaymentState.Expired, payment.State);
        var storedOrder = await fixture.Context.Orders.SingleAsync();
        Assert.Equal(PaymentStatus.Failed, storedOrder.PaymentStatus);
        fixture.Inventory.Verify(i => i.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConcurrentWebhooks_SameEvent_SettlesConsistently()
    {
        var dbName = $"fashionstore-payment-concurrent-{Guid.NewGuid()}";
        using var contextA = new AppDbContext(Fixture.CreateOptions(dbName));
        using var contextB = new AppDbContext(Fixture.CreateOptions(dbName));
        var fixture = new Fixture(dbName);
        var order = fixture.SeedOrder(paymentMethodCode: "card");

        var serviceA = new PaymentService(
            contextA,
            fixture.Factory,
            fixture.Inventory.Object,
            Options.Create(fixture.Settings),
            Options.Create(new OrderSettings { CodReservationMinutes = 4320, OnlineReservationMinutes = 30 }),
            NullLogger<PaymentService>.Instance);

        var serviceB = new PaymentService(
            contextB,
            fixture.Factory,
            fixture.Inventory.Object,
            Options.Create(fixture.Settings),
            Options.Create(new OrderSettings { CodReservationMinutes = 4320, OnlineReservationMinutes = 30 }),
            NullLogger<PaymentService>.Instance);

        await serviceA.InitiateForOrderAsync(order.Id, null, null, CancellationToken.None);

        var payload = Fixture.BuildPayload("evt-concurrent", "payment.succeeded", "card_txn", order.PublicOrderNumber, order.GrandTotal, "USD");
        var signature = PaymentWebhookSignature.Compute(CardSecret, payload);

        var results = await Task.WhenAll(
            serviceA.HandleWebhookAsync("card", payload, signature, CancellationToken.None),
            serviceB.HandleWebhookAsync("card", payload, signature, CancellationToken.None));

        // The payment settles exactly once with the correct captured amount.
        var payment = await fixture.Context.Payments.SingleAsync();
        var storedOrder = await fixture.Context.Orders.SingleAsync();
        Assert.Equal(PaymentState.Paid, payment.State);
        Assert.Equal(PaymentStatus.Paid, storedOrder.PaymentStatus);
        Assert.Equal(order.GrandTotal, storedOrder.PaidAmount);
        Assert.Contains(results, r => r.Status == PaymentWebhookStatus.Processed);
    }

    [Fact]
    public async Task Refund_FullRefund_MarksRefunded()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        fixture.SeedPayment(order, state: PaymentState.Paid, amount: 100m);
        var payment = await fixture.Context.Payments.SingleAsync();
        var storedOrder = await fixture.Context.Orders.SingleAsync();
        storedOrder.PaymentStatus = PaymentStatus.Paid;
        storedOrder.PaidAmount = 100m;
        await fixture.Context.SaveChangesAsync();
        var service = fixture.CreateService();

        var result = await service.RefundAsync(payment.Id, 100m, "operator", CancellationToken.None);

        Assert.True(result.Success);
        var refunded = await fixture.Context.Payments.Include(p => p.Refunds).SingleAsync();
        Assert.Equal(PaymentState.Refunded, refunded.State);
        Assert.Equal(100m, refunded.Refunds.Single().Amount);
        var updatedOrder = await fixture.Context.Orders.SingleAsync();
        Assert.Equal(PaymentStatus.Refunded, updatedOrder.PaymentStatus);
        Assert.Equal(100m, updatedOrder.RefundedAmount);
    }

    [Fact]
    public async Task Refund_PartialRefund_MarksPartiallyRefunded()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        fixture.SeedPayment(order, state: PaymentState.Paid, amount: 100m);
        var payment = await fixture.Context.Payments.SingleAsync();
        var storedOrder = await fixture.Context.Orders.SingleAsync();
        storedOrder.PaymentStatus = PaymentStatus.Paid;
        storedOrder.PaidAmount = 100m;
        await fixture.Context.SaveChangesAsync();
        var service = fixture.CreateService();

        var result = await service.RefundAsync(payment.Id, 30m, "operator", CancellationToken.None);

        Assert.True(result.Success);
        var refunded = await fixture.Context.Payments.Include(p => p.Refunds).SingleAsync();
        Assert.Equal(PaymentState.PartiallyRefunded, refunded.State);
        Assert.Equal(30m, refunded.Refunds.Single().Amount);
        var updatedOrder = await fixture.Context.Orders.SingleAsync();
        Assert.Equal(PaymentStatus.Paid, updatedOrder.PaymentStatus);
        Assert.Equal(30m, updatedOrder.RefundedAmount);
    }

    [Fact]
    public async Task Refund_ExceedsCapturedAmount_Rejected()
    {
        var fixture = new Fixture();
        var order = fixture.SeedOrder(paymentMethodCode: "card");
        fixture.SeedPayment(order, state: PaymentState.Paid, amount: 100m);
        var payment = await fixture.Context.Payments.SingleAsync();
        var service = fixture.CreateService();

        var result = await service.RefundAsync(payment.Id, 150m, "operator", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("invalid-amount", result.FailureCode);
    }

    private sealed class Fixture
    {
        public AppDbContext Context { get; }
        public PaymentSettings Settings { get; }
        public PaymentProviderFactory Factory { get; }
        public Mock<FashionStore.Application.Interfaces.IInventoryService> Inventory { get; } = new();

        public Fixture(string? dbName = null)
        {
            Context = new AppDbContext(CreateOptions(dbName ?? $"fashionstore-payment-{Guid.NewGuid()}"));
            Settings = new PaymentSettings
            {
                WebhookTimestampToleranceSeconds = 300,
                ReturnUrl = "/checkout/confirmation",
                CancelUrl = "/checkout/confirmation",
                Providers = new[]
                {
                    new PaymentProviderSettings { ProviderCode = "cod", DisplayName = "Cash on Delivery", IsEnabled = true, WebhookSecret = "", SupportsHostedCheckout = false },
                    new PaymentProviderSettings { ProviderCode = "card", DisplayName = "Card Payment", IsEnabled = true, WebhookSecret = CardSecret, SupportsHostedCheckout = true },
                    new PaymentProviderSettings { ProviderCode = "mfs", DisplayName = "Mobile Wallet", IsEnabled = true, WebhookSecret = MfsSecret, SupportsHostedCheckout = false },
                    new PaymentProviderSettings { ProviderCode = "bank", DisplayName = "Bank Transfer", IsEnabled = true, WebhookSecret = BankSecret, SupportsHostedCheckout = false }
                }
            };
            Factory = new PaymentProviderFactory(Options.Create(Settings));

            Inventory
                .Setup(i => i.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        public PaymentService CreateService()
        {
            return new PaymentService(
                Context,
                Factory,
                Inventory.Object,
                Options.Create(Settings),
                Options.Create(new OrderSettings { CodReservationMinutes = 4320, OnlineReservationMinutes = 30 }),
                NullLogger<PaymentService>.Instance);
        }

        public static DbContextOptions<AppDbContext> CreateOptions(string dbName) =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;

        public Order SeedOrder(string paymentMethodCode = "card", decimal grandTotal = 100m)
        {
            var order = new Order
            {
                PublicOrderNumber = $"ORD-{Guid.NewGuid():N}"[..18].ToUpperInvariant(),
                Currency = "USD",
                GrandTotal = grandTotal,
                PaymentStatus = PaymentStatus.Unpaid,
                PaymentMethodCode = paymentMethodCode,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            Context.Orders.Add(order);
            Context.SaveChanges();
            return order;
        }

        public Payment SeedPayment(Order order, PaymentState state, DateTime? expiresAtUtc = null, decimal amount = 100m)
        {
            var payment = new Payment
            {
                OrderId = order.Id,
                ProviderCode = "card",
                PaymentMethodCode = order.PaymentMethodCode ?? "card",
                IdempotencyKey = $"order-{order.Id:N}",
                ProviderTransactionId = "card_seeded",
                Amount = amount,
                Currency = "USD",
                State = state,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = expiresAtUtc
            };
            Context.Payments.Add(payment);
            Context.SaveChanges();
            return payment;
        }

        public void SeedReservation(string orderNumber)
        {
            Context.StockReservations.Add(new StockReservation
            {
                ProductVariantId = Guid.NewGuid(),
                CartReference = orderNumber,
                ReferenceId = orderNumber,
                Quantity = 1,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
                Status = StockReservationStatus.Active,
                CreatedAtUtc = DateTime.UtcNow
            });
            Context.SaveChanges();
        }

        public static string BuildPayload(
            string eventId,
            string eventType,
            string? transactionId,
            string? orderNumber,
            decimal amount,
            string currency,
            string? failureReason = null)
        {
            var envelope = new
            {
                id = eventId,
                type = eventType,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                data = new
                {
                    transactionId,
                    orderNumber,
                    amount,
                    currency,
                    failureReason
                }
            };
            return JsonSerializer.Serialize(envelope);
        }

        public static async Task<PaymentWebhookHandlingResult> DeliverWebhookAsync(
            PaymentService service,
            string providerCode,
            string secret,
            string payload)
        {
            var signature = PaymentWebhookSignature.Compute(secret, payload);
            return await service.HandleWebhookAsync(providerCode, payload, signature, CancellationToken.None);
        }
    }
}
