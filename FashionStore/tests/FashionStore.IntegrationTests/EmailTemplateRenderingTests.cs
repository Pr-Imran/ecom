using FashionStore.Application.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

/// <summary>
/// Renders every transactional email template through the real Razor view engine and
/// asserts the responsive layout + scenario content appear. This exercises the
/// synthesized ActionContext used by <see cref="RazorEmailTemplateRenderer"/> so the
/// templates are proven to resolve inside Hangfire jobs, not just request pipelines.
/// </summary>
public class EmailTemplateRenderingTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public EmailTemplateRenderingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<string> RenderAsync(string template, EmailTemplateModel model)
    {
        using var scope = _factory.Services.CreateScope();
        var renderer = scope.ServiceProvider.GetRequiredService<IEmailTemplateRenderer>();
        return await renderer.RenderAsync(template, model, CancellationToken.None);
    }

    private static T OrderModel<T>() where T : OrderEmail, new() =>
        new()
        {
            Subject = $"Subject {typeof(T).Name}",
            Title = "Order notification",
            Preheader = "Order ORD-2026-000123",
            RecipientName = "Jane Doe",
            OrderNumber = "ORD-2026-000123",
            OrderUrl = "https://fashionstore.example.com/orders/ORD-2026-000123",
            Currency = "USD",
            GrandTotal = 137.99m,
            TrackingNumber = "1Z999AA10123456784",
            CarrierCode = "UPS",
            TrackingUrl = "https://www.ups.com/track",
            Items = new[]
            {
                new OrderLineEmail
                {
                    ProductName = "Cashmere Crew Neck Sweater",
                    Variant = "Heather Grey / M",
                    Quantity = 1,
                    UnitPrice = 128.00m,
                    LineTotal = 128.00m,
                    Currency = "USD"
                }
            },
            StoreName = "FashionStore",
            StoreUrl = "https://fashionstore.example.com"
        };

    [Fact]
    public async Task AllSixteenTemplates_RenderResponsiveHtmlWithScenarioContent()
    {
        var templates = new (string Name, EmailTemplateModel Model, string ExpectedFragment)[]
        {
            ("ConfirmEmail", new ConfirmEmailEmail
            {
                Title = "Confirm your email address",
                Preheader = "One more step",
                ConfirmUrl = "https://fashionstore.example.com/Account/ConfirmEmail?userId=sample&token=sample",
                StoreName = "FashionStore",
                StoreUrl = "https://fashionstore.example.com"
            }, "Confirm your email address"),
            ("PasswordReset", new PasswordResetEmail
            {
                Title = "Password reset request",
                ResetUrl = "https://fashionstore.example.com/Account/ResetPassword?token=sample",
                StoreName = "FashionStore",
                StoreUrl = "https://fashionstore.example.com"
            }, "Password reset request"),
            ("Welcome", new WelcomeEmail
            {
                Title = "Welcome, Jane!",
                ShopUrl = "https://fashionstore.example.com",
                StoreName = "FashionStore",
                StoreUrl = "https://fashionstore.example.com"
            }, "Welcome, Jane!"),
            ("OrderPlaced", OrderModel<OrderPlacedEmail>(), "Order ORD-2026-000123"),
            ("PaymentReceived", OrderModel<PaymentReceivedEmail>(), "Cashmere Crew Neck Sweater"),
            ("PaymentFailed", new PaymentFailedEmail
            {
                Title = "Payment failed",
                OutstandingAmount = 137.99m,
                OrderNumber = "ORD-2026-000123",
                GrandTotal = 137.99m,
                StoreName = "FashionStore",
                StoreUrl = "https://fashionstore.example.com"
            }, "Payment failed"),
            ("OrderProcessing", OrderModel<OrderProcessingEmail>(), "Order ORD-2026-000123"),
            ("OrderShipped", OrderModel<OrderShippedEmail>(), "1Z999AA10123456784"),
            ("OrderDelivered", OrderModel<OrderDeliveredEmail>(), "has been delivered"),
            ("OrderCancelled", OrderModel<OrderCancelledEmail>(), "has been cancelled"),
            ("Invoice", new InvoiceEmail
            {
                Title = "Your FashionStore invoice",
                InvoiceNumber = "INV-2026-000123",
                OrderNumber = "ORD-2026-000123",
                StoreName = "FashionStore",
                StoreUrl = "https://fashionstore.example.com"
            }, "INV-2026-000123"),
            ("ReturnRequested", new ReturnRequestedEmail
            {
                Title = "Return request received",
                ReturnNumber = "RMA-2026-000123",
                OrderNumber = "ORD-2026-000123",
                StoreName = "FashionStore",
                StoreUrl = "https://fashionstore.example.com"
            }, "RMA-2026-000123"),
            ("ReturnApproved", new ReturnApprovedEmail
            {
                Title = "Return approved",
                ReturnNumber = "RMA-2026-000123",
                Instructions = "Send the items back",
                StoreName = "FashionStore",
                StoreUrl = "https://fashionstore.example.com"
            }, "Send the items back"),
            ("ReturnRejected", new ReturnRejectedEmail
            {
                Title = "Return not approved",
                ReturnNumber = "RMA-2026-000123",
                Reason = "Did not meet return policy",
                StoreName = "FashionStore",
                StoreUrl = "https://fashionstore.example.com"
            }, "Did not meet return policy"),
            ("RefundCompleted", new RefundCompletedEmail
            {
                Title = "Refund completed",
                OrderNumber = "ORD-2026-000123",
                RefundedAmount = 137.99m,
                Currency = "USD",
                StoreName = "FashionStore",
                StoreUrl = "https://fashionstore.example.com"
            }, "137.99"),
            ("LowStockAlert", new LowStockAlertEmail
            {
                Title = "Low stock alert",
                InventoryUrl = "https://fashionstore.example.com/admin/inventory",
                Items = new[]
                {
                    new LowStockAlertItem { ProductName = "Cashmere Crew Neck Sweater", Sku = "SW-1001-GREY-M", Variant = "Heather Grey / M", Available = 3, Threshold = 5 }
                },
                StoreName = "FashionStore",
                StoreUrl = "https://fashionstore.example.com"
            }, "Cashmere Crew Neck Sweater")
        };

        Assert.Equal(16, templates.Length);

        foreach (var (name, model, fragment) in templates)
        {
            var html = await RenderAsync(name, model);

            Assert.False(string.IsNullOrWhiteSpace(html), $"{name} rendered empty.");
            Assert.Contains("<!DOCTYPE html>", html);
            Assert.Contains("max-width: 600px", html); // responsive layout
            Assert.Contains("FashionStore", html);
            Assert.Contains(fragment, html);
        }
    }

    [Fact]
    public async Task UnknownTemplate_ThrowsWithSearchedLocations()
    {
        using var scope = _factory.Services.CreateScope();
        var renderer = scope.ServiceProvider.GetRequiredService<IEmailTemplateRenderer>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            renderer.RenderAsync("DoesNotExist", new WelcomeEmail { StoreName = "FashionStore", StoreUrl = "https://fashionstore.example.com" }, CancellationToken.None));

        Assert.Contains("DoesNotExist", ex.Message);
    }
}
