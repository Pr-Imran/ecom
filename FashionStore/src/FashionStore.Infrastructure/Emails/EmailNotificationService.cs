using FashionStore.Application.Configuration;
using FashionStore.Application.Email;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Emails;

/// <summary>
/// Builds scenario-specific template models, renders them and enqueues the result
/// into the outbox under a deterministic deduplication key. Nothing is sent here —
/// the sender job delivers after the outbox transaction commits.
/// </summary>
public sealed class EmailNotificationService : IEmailNotificationService
{
    private readonly IEmailOutbox _queue;
    private readonly IEmailTemplateRenderer _renderer;
    private readonly AppDbContext _context;
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(
        IEmailOutbox queue,
        IEmailTemplateRenderer renderer,
        AppDbContext context,
        EmailSettings settings,
        ILogger<EmailNotificationService> logger)
    {
        _queue = queue;
        _renderer = renderer;
        _context = context;
        _settings = settings;
        _logger = logger;
    }

    private string StoreName => string.IsNullOrWhiteSpace(_settings.FromName) ? "FashionStore" : _settings.FromName;
    private string BaseUrl => string.IsNullOrWhiteSpace(_settings.BaseUrl) ? "https://localhost:5001" : _settings.BaseUrl.TrimEnd('/');

    public Task SendConfirmationEmailAsync(string email, string userId, string token, CancellationToken cancellationToken = default)
    {
        var confirmUrl = $"{BaseUrl}/Account/ConfirmEmail?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";
        var model = new ConfirmEmailEmail
        {
            Subject = "Confirm your FashionStore account",
            Title = "Confirm your email address",
            Preheader = "One more step to start shopping with FashionStore.",
            RecipientName = "there",
            ConfirmUrl = confirmUrl,
            StoreName = StoreName,
            StoreUrl = BaseUrl
        };

        return SendAsync("ConfirmEmail", model, email, $"account-confirm:{userId}", createdBy: null, cancellationToken);
    }

    public Task SendPasswordResetEmailAsync(string email, string token, CancellationToken cancellationToken = default)
    {
        var resetUrl = $"{BaseUrl}/Account/ResetPassword?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
        var model = new PasswordResetEmail
        {
            Subject = "Reset your FashionStore password",
            Title = "Password reset request",
            Preheader = "Follow the link to choose a new password for your FashionStore account.",
            RecipientName = "there",
            ResetUrl = resetUrl,
            StoreName = StoreName,
            StoreUrl = BaseUrl
        };

        return SendAsync("PasswordReset", model, email, $"password-reset:{email.ToLowerInvariant()}:{token[..Math.Min(8, token.Length)]}", createdBy: null, cancellationToken);
    }

    public Task SendWelcomeEmailAsync(string email, string name, CancellationToken cancellationToken = default)
    {
        var model = new WelcomeEmail
        {
            Subject = "Welcome to FashionStore!",
            Title = $"Welcome{(!string.IsNullOrWhiteSpace(name) ? $", {name}" : string.Empty)}!",
            Preheader = "Your FashionStore account is ready.",
            RecipientName = string.IsNullOrWhiteSpace(name) ? "there" : name,
            ShopUrl = BaseUrl,
            StoreName = StoreName,
            StoreUrl = BaseUrl
        };

        return SendAsync("Welcome", model, email, $"welcome:{email.ToLowerInvariant()}", createdBy: null, cancellationToken);
    }

    public Task SendOrderPlacedAsync(Order order, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(order.GuestEmail))
        {
            return Task.CompletedTask;
        }

        var model = PopulateOrder(new OrderPlacedEmail(), order, "Your FashionStore order is confirmed", "Order confirmed");
        return SendAsync("OrderPlaced", model, order.GuestEmail, $"order-placed:{order.Id}", null, cancellationToken);
    }

    public async Task SendPaymentReceivedAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null || string.IsNullOrWhiteSpace(order.GuestEmail))
        {
            return;
        }

        var model = PopulateOrder(new PaymentReceivedEmail(), order, "Payment received for your FashionStore order", "Payment received");
        await SendAsync("PaymentReceived", model, order.GuestEmail, $"payment-received:{order.Id}", null, cancellationToken);
    }

    public async Task SendPaymentFailedAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null || string.IsNullOrWhiteSpace(order.GuestEmail))
        {
            return;
        }

        var model = PopulateOrder(new PaymentFailedEmail(), order, "Payment unsuccessful for your FashionStore order", "Payment failed");
        model.OutstandingAmount = Math.Max(0m, order.GrandTotal - order.PaidAmount - order.RefundedAmount);
        await SendAsync("PaymentFailed", model, order.GuestEmail, $"payment-failed:{order.Id}", null, cancellationToken);
    }

    public async Task SendOrderProcessingAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null || string.IsNullOrWhiteSpace(order.GuestEmail))
        {
            return;
        }

        var model = PopulateOrder(new OrderProcessingEmail(), order, "Your FashionStore order is being prepared", "Order being processed");
        await SendAsync("OrderProcessing", model, order.GuestEmail, $"order-processing:{order.Id}", null, cancellationToken);
    }

    public async Task SendOrderShippedAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null || string.IsNullOrWhiteSpace(order.GuestEmail))
        {
            return;
        }

        var model = PopulateOrder(new OrderShippedEmail(), order, "Your FashionStore order has shipped", "Order on its way");
        await SendAsync("OrderShipped", model, order.GuestEmail, $"order-shipped:{order.Id}", null, cancellationToken);
    }

    public async Task SendOrderDeliveredAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null || string.IsNullOrWhiteSpace(order.GuestEmail))
        {
            return;
        }

        var model = PopulateOrder(new OrderDeliveredEmail(), order, "Your FashionStore order has been delivered", "Order delivered");
        await SendAsync("OrderDelivered", model, order.GuestEmail, $"order-delivered:{order.Id}", null, cancellationToken);
    }

    public async Task SendOrderCancelledAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null || string.IsNullOrWhiteSpace(order.GuestEmail))
        {
            return;
        }

        var model = PopulateOrder(new OrderCancelledEmail(), order, "Your FashionStore order has been cancelled", "Order cancelled");
        model.Reason = order.CancelledReasonCode ?? string.Empty;
        await SendAsync("OrderCancelled", model, order.GuestEmail, $"order-cancelled:{order.Id}", null, cancellationToken);
    }

    public async Task SendInvoiceAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null || string.IsNullOrWhiteSpace(order.GuestEmail))
        {
            return;
        }

        var model = PopulateOrder(new InvoiceEmail(), order, $"Invoice for order {order.PublicOrderNumber}", "Your FashionStore invoice");
        model.InvoiceNumber = order.InvoiceNumber ?? string.Empty;
        await SendAsync("Invoice", model, order.GuestEmail, $"invoice:{order.Id}", null, cancellationToken, attachmentKind: "InvoicePdf", templateDataJson: order.Id.ToString());
    }

    public async Task SendReturnRequestedAsync(Guid returnRequestId, CancellationToken cancellationToken = default)
    {
        var request = await LoadReturnAsync(returnRequestId, cancellationToken);
        if (request is null || string.IsNullOrWhiteSpace(request.GuestEmail))
        {
            return;
        }

        var model = new ReturnRequestedEmail
        {
            Subject = "We received your FashionStore return request",
            Title = "Return request received",
            Preheader = $"Your return {request.ReturnNumber} is under review.",
            RecipientName = string.IsNullOrWhiteSpace(request.CustomerName) ? "there" : request.CustomerName,
            ReturnNumber = request.ReturnNumber,
            OrderNumber = request.Order?.PublicOrderNumber ?? string.Empty,
            ReturnUrl = $"{BaseUrl}/returns/{request.ReturnNumber}",
            StoreName = StoreName,
            StoreUrl = BaseUrl
        };

        await SendAsync("ReturnRequested", model, request.GuestEmail, $"return-requested:{request.Id}", null, cancellationToken);
    }

    public async Task SendReturnApprovedAsync(Guid returnRequestId, CancellationToken cancellationToken = default)
    {
        var request = await LoadReturnAsync(returnRequestId, cancellationToken);
        if (request is null || string.IsNullOrWhiteSpace(request.GuestEmail))
        {
            return;
        }

        var model = new ReturnApprovedEmail
        {
            Subject = "Your FashionStore return has been approved",
            Title = "Return approved",
            Preheader = $"Return {request.ReturnNumber} has been approved.",
            RecipientName = string.IsNullOrWhiteSpace(request.CustomerName) ? "there" : request.CustomerName,
            ReturnNumber = request.ReturnNumber,
            OrderNumber = request.Order?.PublicOrderNumber ?? string.Empty,
            Instructions = "Send the items back using the return instructions below. The refund is processed once the items are received.",
            ReturnUrl = $"{BaseUrl}/returns/{request.ReturnNumber}",
            StoreName = StoreName,
            StoreUrl = BaseUrl
        };

        await SendAsync("ReturnApproved", model, request.GuestEmail, $"return-approved:{request.Id}", null, cancellationToken);
    }

    public async Task SendReturnRejectedAsync(Guid returnRequestId, CancellationToken cancellationToken = default)
    {
        var request = await LoadReturnAsync(returnRequestId, cancellationToken);
        if (request is null || string.IsNullOrWhiteSpace(request.GuestEmail))
        {
            return;
        }

        var model = new ReturnRejectedEmail
        {
            Subject = "Your FashionStore return was not approved",
            Title = "Return not approved",
            Preheader = $"Return {request.ReturnNumber} could not be approved.",
            RecipientName = string.IsNullOrWhiteSpace(request.CustomerName) ? "there" : request.CustomerName,
            ReturnNumber = request.ReturnNumber,
            OrderNumber = request.Order?.PublicOrderNumber ?? string.Empty,
            Reason = request.RejectionNote ?? "The return request did not meet our return policy.",
            StoreName = StoreName,
            StoreUrl = BaseUrl
        };

        await SendAsync("ReturnRejected", model, request.GuestEmail, $"return-rejected:{request.Id}", null, cancellationToken);
    }

    public async Task SendRefundCompletedAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null || string.IsNullOrWhiteSpace(order.GuestEmail))
        {
            return;
        }

        var model = new RefundCompletedEmail
        {
            Subject = "Your FashionStore refund has been processed",
            Title = "Refund completed",
            Preheader = $"A refund for order {order.PublicOrderNumber} has been processed.",
            RecipientName = string.IsNullOrWhiteSpace(order.CustomerName) ? "there" : order.CustomerName,
            OrderNumber = order.PublicOrderNumber,
            RefundedAmount = order.RefundedAmount,
            Currency = order.Currency,
            OrderUrl = $"{BaseUrl}/orders/{order.PublicOrderNumber}",
            StoreName = StoreName,
            StoreUrl = BaseUrl
        };

        await SendAsync("RefundCompleted", model, order.GuestEmail, $"refund-completed:{order.Id}", null, cancellationToken);
    }

    public async Task SendLowStockAlertAsync(IReadOnlyList<LowStockAlertItem> items, CancellationToken cancellationToken = default)
    {
        var recipients = (_settings.AdminAlertRecipients ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (recipients.Length == 0 || items.Count == 0)
        {
            return;
        }

        var model = new LowStockAlertEmail
        {
            Subject = $"Low stock alert: {items.Count} item(s) need attention",
            Title = "Low stock alert",
            Preheader = "Some products are running low on inventory.",
            RecipientName = "Administrator",
            Items = items,
            InventoryUrl = $"{BaseUrl}/admin/inventory",
            StoreName = StoreName,
            StoreUrl = BaseUrl
        };

        foreach (var recipient in recipients)
        {
            await SendAsync("LowStockAlert", model, recipient, $"low-stock:{DateTime.UtcNow:yyyyMMdd}:{recipient.ToLowerInvariant()}", null, cancellationToken);
        }
    }

    // ---- Helpers ----

    private async Task SendAsync<TModel>(
        string template,
        TModel model,
        string toEmail,
        string? dedupKey,
        string? createdBy,
        CancellationToken cancellationToken,
        string? attachmentKind = null,
        string? templateDataJson = null)
        where TModel : EmailTemplateModel
    {
        var html = await _renderer.RenderAsync(template, model, cancellationToken);
        await _queue.EnqueueAsync(new QueuedEmailDraft(
            toEmail,
            model.RecipientName,
            model.Subject,
            html,
            template,
            templateDataJson,
            attachmentKind,
            dedupKey,
            createdBy), cancellationToken);
    }

    private TModel PopulateOrder<TModel>(TModel model, Order order, string subject, string title)
        where TModel : OrderEmail
    {
        model.Subject = subject;
        model.Title = title;
        model.Preheader = $"Order {order.PublicOrderNumber} — {title.ToLowerInvariant()}.";
        model.RecipientName = string.IsNullOrWhiteSpace(order.CustomerName) ? "there" : order.CustomerName;
        model.OrderNumber = order.PublicOrderNumber;
        model.OrderUrl = $"{BaseUrl}/orders/{order.PublicOrderNumber}";
        model.Currency = order.Currency;
        model.GrandTotal = order.GrandTotal;
        model.Items = order.Items
            .Select(i => new OrderLineEmail
            {
                ProductName = i.ProductName,
                Variant = VariantLabel(i),
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                LineTotal = i.LineTotal,
                Currency = order.Currency
            })
            .ToList();
        model.TrackingNumber = order.TrackingNumber ?? string.Empty;
        model.CarrierCode = order.CarrierCode ?? string.Empty;
        model.TrackingUrl = order.TrackingUrl ?? string.Empty;
        model.Reason = order.CancelledReasonCode ?? string.Empty;
        model.StoreName = StoreName;
        model.StoreUrl = BaseUrl;
        return model;
    }

    private static string VariantLabel(OrderItem item)
    {
        var parts = new[] { item.ColourName, item.SizeName }.Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(" / ", parts);
    }

    private async Task<Order?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    private async Task<ReturnRequest?> LoadReturnAsync(Guid returnRequestId, CancellationToken cancellationToken) =>
        await _context.ReturnRequests
            .AsNoTracking()
            .Include(r => r.Order)
            .FirstOrDefaultAsync(r => r.Id == returnRequestId, cancellationToken);
}
