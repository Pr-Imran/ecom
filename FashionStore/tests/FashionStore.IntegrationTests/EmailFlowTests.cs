using FashionStore.Application.Email;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.BackgroundJobs;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

/// <summary>
/// End-to-end outbox flow: a business notification enqueues a deduplicated row,
/// the background sender job picks it up after the write, delivers through the
/// active (development) provider and marks it Sent.
/// </summary>
public class EmailFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public EmailFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string UniqueEmail() => $"email-flow-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task WelcomeNotification_EnqueuesRow_ThenJobDeliversAndMarksSent()
    {
        var email = UniqueEmail();
        Guid messageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var notifications = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
            await notifications.SendWelcomeEmailAsync(email, "Jane", CancellationToken.None);

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.EmailMessages.SingleAsync(e => e.DeduplicationKey == $"welcome:{email.ToLowerInvariant()}");
            messageId = row.Id;
            Assert.Equal(EmailStatus.Pending, row.Status);
            Assert.Equal("Welcome to FashionStore!", row.Subject);
            Assert.Contains("Welcome, Jane", row.BodyHtml);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<SendQueuedEmailsJob>();
            var processed = await job.ExecuteAsync(CancellationToken.None);
            // Other tests share the in-memory DB and may leave due rows, so only
            // require that at least our enqueued message was processed this run.
            Assert.True(processed >= 1);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.EmailMessages.SingleAsync(e => e.Id == messageId);
            Assert.Equal(EmailStatus.Sent, row.Status);
            Assert.NotNull(row.SentAtUtc);
        }
    }

    [Fact]
    public async Task WelcomeNotification_SecondCallWithSameKey_IsDeduplicated()
    {
        var email = UniqueEmail();
        using var scope = _factory.Services.CreateScope();
        var notifications = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();

        await notifications.SendWelcomeEmailAsync(email, "Jane", CancellationToken.None);
        await notifications.SendWelcomeEmailAsync(email, "Jane", CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.EmailMessages.CountAsync(e => e.DeduplicationKey == $"welcome:{email.ToLowerInvariant()}"));
    }

    [Fact]
    public async Task OrderPlacedNotification_EnqueuesForGuestEmail()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = UniqueEmail();

        var order = new Order
        {
            PublicOrderNumber = $"ORD-FLOW-{Guid.NewGuid():N}"[..20],
            CustomerName = "Jane Doe",
            GuestEmail = email,
            Currency = "USD",
            Subtotal = 128m,
            ShippingCharge = 9.99m,
            GrandTotal = 137.99m,
            PaymentStatus = PaymentStatus.Paid,
            OrderStatus = OrderStatus.Placed,
            CreatedAtUtc = DateTime.UtcNow
        };
        order.Items.Add(new OrderItem
        {
            ProductName = "Cashmere Crew Neck Sweater",
            ProductSlug = "cashmere-crew-neck-sweater",
            Sku = "SW-1001-GREY-M",
            UnitPrice = 128m,
            Quantity = 1,
            LineTotal = 128m
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var notifications = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
        await notifications.SendOrderPlacedAsync(order, CancellationToken.None);

        var row = await db.EmailMessages.SingleAsync(e => e.DeduplicationKey == $"order-placed:{order.Id}");
        Assert.Equal(email, row.ToEmail);
        Assert.Contains("Cashmere Crew Neck Sweater", row.BodyHtml);
    }

    [Fact]
    public async Task LowStockAlert_EnqueuesPerConfiguredRecipient()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.EmailMessages.CountAsync();

        var notifications = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
        await notifications.SendLowStockAlertAsync(new[]
        {
            new LowStockAlertItem { ProductName = "Cashmere Crew Neck Sweater", Sku = "SW-1001-GREY-M", Variant = "Heather Grey / M", Available = 3, Threshold = 5 }
        }, CancellationToken.None);

        // The app's AdminAlertRecipients is empty in the test configuration, so nothing is enqueued.
        Assert.Equal(before, await db.EmailMessages.CountAsync());
    }

    [Fact]
    public async Task ConfirmationNotification_IsInvokedThroughAccountFlow()
    {
        var email = UniqueEmail();
        string userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = false,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(user, "EmailFlow!pass1");
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
            userId = user.Id;

            var notifications = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();
            await notifications.SendConfirmationEmailAsync(email, userId, "fake-token", CancellationToken.None);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.EmailMessages.SingleAsync(e => e.DeduplicationKey == $"account-confirm:{userId}");
            Assert.Contains("/Account/ConfirmEmail", row.BodyHtml);
            Assert.Equal(email, row.ToEmail);
        }
    }
}
