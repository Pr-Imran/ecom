using System.Net;
using System.Text;
using System.Text.Json;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Payments;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

/// <summary>
/// Exercises the payment webhook endpoint end to end: a valid signed webhook
/// settles an initiated payment and marks the order paid, a replay of the same
/// event is acknowledged as a duplicate without double-applying, and an invalid
/// signature or an unknown transaction is rejected.
/// </summary>
public class PaymentWebhookTests : IClassFixture<TestWebApplicationFactory>
{
    private const string CardSecret = "dev-placeholder-card-secret";

    private readonly WebApplicationFactory<Program> _factory;

    public PaymentWebhookTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static string BuildPayload(string eventId, string type, string orderNumber, decimal amount, string currency = "USD")
    {
        var envelope = new
        {
            id = eventId,
            type,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = new
            {
                transactionId = $"card_{eventId}",
                orderNumber,
                amount,
                currency,
                failureReason = (string?)null
            }
        };
        return JsonSerializer.Serialize(envelope);
    }

    private async Task<(Order Order, Payment Payment)> SeedInitiatedOrderAsync(string? number = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var order = new Order
        {
            PublicOrderNumber = number ?? $"ORD-W{DateTime.UtcNow.Ticks % 1000000:D6}",
            InvoiceNumber = null,
            CustomerName = "Jane Doe",
            Currency = "USD",
            Subtotal = 128m,
            ShippingCharge = 9.99m,
            GrandTotal = 137.99m,
            PaymentMethodCode = "card",
            OrderStatus = OrderStatus.Placed,
            PaymentStatus = PaymentStatus.Unpaid,
            FulfilmentStatus = FulfilmentStatus.Unfulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var payment = new Payment
        {
            OrderId = order.Id,
            ProviderCode = "card",
            PaymentMethodCode = "card",
            ProviderTransactionId = $"card_{Guid.NewGuid():N}",
            IdempotencyKey = $"order-{order.Id:N}",
            Amount = order.GrandTotal,
            Currency = order.Currency,
            State = PaymentState.Initiated,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        return (order, payment);
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string payload, string? signature)
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/payments/webhook/card")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (signature is not null)
        {
            request.Headers.Add(PaymentWebhookSignature.HeaderName, signature);
        }
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task ValidSucceededWebhook_MarksOrderPaid()
    {
        var (order, payment) = await SeedInitiatedOrderAsync();
        var payload = BuildPayload($"evt-{Guid.NewGuid():N}", "payment.succeeded", order.PublicOrderNumber, order.GrandTotal);
        var signature = PaymentWebhookSignature.Compute(CardSecret, payload);

        var response = await PostWebhookAsync(payload, signature);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal("Processed", GetString(body, "status"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var storedPayment = await db.Payments.SingleAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentState.Paid, storedPayment.State);

        var storedOrder = await db.Orders.SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentStatus.Paid, storedOrder.PaymentStatus);
        Assert.Equal(order.GrandTotal, storedOrder.PaidAmount);
        Assert.NotNull(storedOrder.PaidAtUtc);
    }

    [Fact]
    public async Task DuplicateWebhookEvent_IsAcknowledgedWithoutDoubleApplying()
    {
        var (order, payment) = await SeedInitiatedOrderAsync();
        var eventId = $"evt-{Guid.NewGuid():N}";
        var payload = BuildPayload(eventId, "payment.succeeded", order.PublicOrderNumber, order.GrandTotal);
        var signature = PaymentWebhookSignature.Compute(CardSecret, payload);

        var first = await PostWebhookAsync(payload, signature);
        var second = await PostWebhookAsync(payload, signature);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True((await ReadJsonAsync(first)).GetProperty("success").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await ReadJsonAsync(second);
        Assert.False(secondBody.GetProperty("success").GetBoolean());
        Assert.Equal("Duplicate", GetString(secondBody, "status"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var processedLogs = await db.PaymentWebhookLogs.CountAsync(l =>
            l.ProviderEventId == eventId && l.Status == PaymentWebhookStatus.Processed);
        Assert.Equal(1, processedLogs);
    }

    [Fact]
    public async Task InvalidSignature_IsRejected()
    {
        var (order, payment) = await SeedInitiatedOrderAsync();
        var payload = BuildPayload($"evt-{Guid.NewGuid():N}", "payment.succeeded", order.PublicOrderNumber, order.GrandTotal);
        var wrongSignature = PaymentWebhookSignature.Compute("wrong-secret", payload);

        var response = await PostWebhookAsync(payload, wrongSignature);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("InvalidSignature", GetString(body, "status"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedPayment = await db.Payments.SingleAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentState.Initiated, storedPayment.State);
    }

    [Fact]
    public async Task UnknownTransaction_IsRejected()
    {
        var payload = BuildPayload($"evt-{Guid.NewGuid():N}", "payment.succeeded", $"ORD-{Guid.NewGuid():N}"[..18].ToUpperInvariant(), 99m);
        var signature = PaymentWebhookSignature.Compute(CardSecret, payload);

        var response = await PostWebhookAsync(payload, signature);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("UnknownTransaction", GetString(body, "status"));
    }

    [Fact]
    public async Task AmountMismatch_IsRejected()
    {
        var (order, payment) = await SeedInitiatedOrderAsync();
        var payload = BuildPayload($"evt-{Guid.NewGuid():N}", "payment.succeeded", order.PublicOrderNumber, order.GrandTotal + 10m);
        var signature = PaymentWebhookSignature.Compute(CardSecret, payload);

        var response = await PostWebhookAsync(payload, signature);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("AmountMismatch", GetString(body, "status"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(PaymentState.Initiated, (await db.Payments.SingleAsync(p => p.Id == payment.Id)).State);
    }
}
