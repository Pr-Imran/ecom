using FashionStore.Application.Email;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Emails;

/// <summary>
/// Read and re-send operations over the email log backing the admin pages.
/// </summary>
public sealed class EmailAdminService : IEmailAdminService
{
    private static readonly string[] TemplateNames =
    {
        "ConfirmEmail", "PasswordReset", "Welcome",
        "OrderPlaced", "PaymentReceived", "PaymentFailed", "OrderProcessing",
        "OrderShipped", "OrderDelivered", "OrderCancelled", "Invoice",
        "ReturnRequested", "ReturnApproved", "ReturnRejected", "RefundCompleted",
        "LowStockAlert"
    };

    private readonly AppDbContext _context;
    private readonly IEmailTemplateRenderer _renderer;
    private readonly ILogger<EmailAdminService> _logger;

    public EmailAdminService(AppDbContext context, IEmailTemplateRenderer renderer, ILogger<EmailAdminService> logger)
    {
        _context = context;
        _renderer = renderer;
        _logger = logger;
    }

    public async Task<EmailLogPage> GetLogAsync(
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.EmailMessages.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            query = query.Where(e => e.ToEmail.Contains(needle) || e.Subject.Contains(needle));
        }

        if (Enum.TryParse<EmailStatus>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            query = query.Where(e => e.Status == parsed);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EmailLogItem(
                e.Id,
                e.ToEmail,
                e.RecipientName,
                e.Subject,
                e.Status.ToString(),
                e.AttemptCount,
                e.MaxAttempts,
                e.NextAttemptAtUtc,
                e.SentAtUtc,
                e.LastError,
                e.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new EmailLogPage(items, totalCount, page, pageSize, page * pageSize < totalCount);
    }

    public async Task<EmailLogItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.EmailMessages.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new EmailLogItem(
                e.Id,
                e.ToEmail,
                e.RecipientName,
                e.Subject,
                e.Status.ToString(),
                e.AttemptCount,
                e.MaxAttempts,
                e.NextAttemptAtUtc,
                e.SentAtUtc,
                e.LastError,
                e.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<ResendEmailResult> ResendAsync(Guid id, string? initiatedBy, CancellationToken cancellationToken = default)
    {
        var email = await _context.EmailMessages.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (email is null)
        {
            return new ResendEmailResult(false, "Email not found.");
        }

        if (email.Status == EmailStatus.Cancelled)
        {
            return new ResendEmailResult(false, "A cancelled email cannot be resent.");
        }

        var now = DateTime.UtcNow;
        email.Status = EmailStatus.Pending;
        email.AttemptCount = 0;
        email.LastError = null;
        email.NextAttemptAtUtc = now;
        email.SentAtUtc = null;
        email.UpdatedAtUtc = now;
        email.UpdatedBy = initiatedBy;

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Email {EmailId} to {To} re-queued for delivery by {By}", email.Id, email.ToEmail, initiatedBy);

        return new ResendEmailResult(true, null);
    }

    public async Task<IReadOnlyList<EmailTemplatePreview>> GetTemplatePreviewsAsync(CancellationToken cancellationToken = default)
    {
        var previews = new List<EmailTemplatePreview>(TemplateNames.Length);
        foreach (var name in TemplateNames)
        {
            var model = BuildSampleModel(name);
            var html = await _renderer.RenderAsync(name, model, cancellationToken);
            previews.Add(new EmailTemplatePreview(name, model.Subject, html));
        }

        return previews;
    }

    private static EmailTemplateModel BuildSampleModel(string name)
    {
        const string storeUrl = "https://fashionstore.example.com";
        const string storeName = "FashionStore";

        var orderLines = new[]
        {
            new OrderLineEmail { ProductName = "Cashmere Crew Neck Sweater", Variant = "Heather Grey / M", Quantity = 1, UnitPrice = 128.00m, LineTotal = 128.00m, Currency = "USD" },
            new OrderLineEmail { ProductName = "Merino Wool Scarf", Variant = "Camel", Quantity = 2, UnitPrice = 45.00m, LineTotal = 90.00m, Currency = "USD" }
        };

        return name switch
        {
            "ConfirmEmail" => new ConfirmEmailEmail
            {
                Subject = "Confirm your FashionStore account",
                Title = "Confirm your email address",
                Preheader = "One more step to start shopping with FashionStore.",
                RecipientName = "there",
                ConfirmUrl = $"{storeUrl}/Account/ConfirmEmail?userId=sample&token=sample",
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "PasswordReset" => new PasswordResetEmail
            {
                Subject = "Reset your FashionStore password",
                Title = "Password reset request",
                Preheader = "Follow the link to choose a new password.",
                RecipientName = "there",
                ResetUrl = $"{storeUrl}/Account/ResetPassword?token=sample",
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "Welcome" => new WelcomeEmail
            {
                Subject = "Welcome to FashionStore!",
                Title = "Welcome, Jane!",
                Preheader = "Your FashionStore account is ready.",
                RecipientName = "Jane",
                ShopUrl = storeUrl,
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "OrderPlaced" => new OrderPlacedEmail
            {
                Subject = "Your FashionStore order is confirmed",
                Title = "Order confirmed",
                Preheader = "Order ORD-2026-000123 — order confirmed.",
                RecipientName = "Jane Doe",
                OrderNumber = "ORD-2026-000123",
                OrderUrl = $"{storeUrl}/orders/ORD-2026-000123",
                Currency = "USD",
                GrandTotal = 218.00m,
                Items = orderLines,
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "PaymentReceived" => new PaymentReceivedEmail
            {
                Subject = "Payment received for your FashionStore order",
                Title = "Payment received",
                Preheader = "Order ORD-2026-000123 — payment received.",
                RecipientName = "Jane Doe",
                OrderNumber = "ORD-2026-000123",
                OrderUrl = $"{storeUrl}/orders/ORD-2026-000123",
                Currency = "USD",
                GrandTotal = 218.00m,
                Items = orderLines,
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "PaymentFailed" => new PaymentFailedEmail
            {
                Subject = "Payment unsuccessful for your FashionStore order",
                Title = "Payment failed",
                Preheader = "Order ORD-2026-000123 — payment failed.",
                RecipientName = "Jane Doe",
                OrderNumber = "ORD-2026-000123",
                OrderUrl = $"{storeUrl}/orders/ORD-2026-000123",
                Currency = "USD",
                GrandTotal = 218.00m,
                OutstandingAmount = 218.00m,
                Items = orderLines,
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "OrderProcessing" => new OrderProcessingEmail
            {
                Subject = "Your FashionStore order is being prepared",
                Title = "Order being processed",
                Preheader = "Order ORD-2026-000123 — order being processed.",
                RecipientName = "Jane Doe",
                OrderNumber = "ORD-2026-000123",
                OrderUrl = $"{storeUrl}/orders/ORD-2026-000123",
                Currency = "USD",
                GrandTotal = 218.00m,
                Items = orderLines,
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "OrderShipped" => new OrderShippedEmail
            {
                Subject = "Your FashionStore order has shipped",
                Title = "Order on its way",
                Preheader = "Order ORD-2026-000123 — order on its way.",
                RecipientName = "Jane Doe",
                OrderNumber = "ORD-2026-000123",
                OrderUrl = $"{storeUrl}/orders/ORD-2026-000123",
                Currency = "USD",
                GrandTotal = 218.00m,
                TrackingNumber = "1Z999AA10123456784",
                CarrierCode = "UPS",
                TrackingUrl = "https://www.ups.com/track",
                Items = orderLines,
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "OrderDelivered" => new OrderDeliveredEmail
            {
                Subject = "Your FashionStore order has been delivered",
                Title = "Order delivered",
                Preheader = "Order ORD-2026-000123 — order delivered.",
                RecipientName = "Jane Doe",
                OrderNumber = "ORD-2026-000123",
                OrderUrl = $"{storeUrl}/orders/ORD-2026-000123",
                Currency = "USD",
                GrandTotal = 218.00m,
                Items = orderLines,
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "OrderCancelled" => new OrderCancelledEmail
            {
                Subject = "Your FashionStore order has been cancelled",
                Title = "Order cancelled",
                Preheader = "Order ORD-2026-000123 — order cancelled.",
                RecipientName = "Jane Doe",
                OrderNumber = "ORD-2026-000123",
                OrderUrl = $"{storeUrl}/orders/ORD-2026-000123",
                Currency = "USD",
                GrandTotal = 218.00m,
                Items = orderLines,
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "Invoice" => new InvoiceEmail
            {
                Subject = "Invoice INV-2026-000123 for order ORD-2026-000123",
                Title = "Your FashionStore invoice",
                Preheader = "Invoice INV-2026-000123.",
                RecipientName = "Jane Doe",
                OrderNumber = "ORD-2026-000123",
                InvoiceNumber = "INV-2026-000123",
                OrderUrl = $"{storeUrl}/orders/ORD-2026-000123",
                Currency = "USD",
                GrandTotal = 218.00m,
                Items = orderLines,
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "ReturnRequested" => new ReturnRequestedEmail
            {
                Subject = "We received your FashionStore return request",
                Title = "Return request received",
                Preheader = "Your return RMA-2026-000123 is under review.",
                RecipientName = "Jane Doe",
                ReturnNumber = "RMA-2026-000123",
                OrderNumber = "ORD-2026-000123",
                ReturnUrl = $"{storeUrl}/returns/RMA-2026-000123",
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "ReturnApproved" => new ReturnApprovedEmail
            {
                Subject = "Your FashionStore return has been approved",
                Title = "Return approved",
                Preheader = "Return RMA-2026-000123 has been approved.",
                RecipientName = "Jane Doe",
                ReturnNumber = "RMA-2026-000123",
                OrderNumber = "ORD-2026-000123",
                Instructions = "Send the items back using the return instructions below.",
                ReturnUrl = $"{storeUrl}/returns/RMA-2026-000123",
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "ReturnRejected" => new ReturnRejectedEmail
            {
                Subject = "Your FashionStore return was not approved",
                Title = "Return not approved",
                Preheader = "Return RMA-2026-000123 could not be approved.",
                RecipientName = "Jane Doe",
                ReturnNumber = "RMA-2026-000123",
                OrderNumber = "ORD-2026-000123",
                Reason = "The return request did not meet our return policy.",
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "RefundCompleted" => new RefundCompletedEmail
            {
                Subject = "Your FashionStore refund has been processed",
                Title = "Refund completed",
                Preheader = "A refund for order ORD-2026-000123 has been processed.",
                RecipientName = "Jane Doe",
                OrderNumber = "ORD-2026-000123",
                RefundedAmount = 218.00m,
                Currency = "USD",
                OrderUrl = $"{storeUrl}/orders/ORD-2026-000123",
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            "LowStockAlert" => new LowStockAlertEmail
            {
                Subject = "Low stock alert: 2 item(s) need attention",
                Title = "Low stock alert",
                Preheader = "Some products are running low on inventory.",
                RecipientName = "Administrator",
                Items = new[]
                {
                    new LowStockAlertItem { ProductName = "Cashmere Crew Neck Sweater", Sku = "SW-1001-GREY-M", Variant = "Heather Grey / M", Available = 3, Threshold = 5 },
                    new LowStockAlertItem { ProductName = "Merino Wool Scarf", Sku = "AC-2001-CAM", Variant = "Camel", Available = 1, Threshold = 5 }
                },
                InventoryUrl = $"{storeUrl}/admin/inventory",
                StoreName = storeName,
                StoreUrl = storeUrl
            },
            _ => new WelcomeEmail
            {
                Subject = "FashionStore",
                Title = "FashionStore",
                Preheader = string.Empty,
                RecipientName = "there",
                StoreName = storeName,
                StoreUrl = storeUrl
            }
        };
    }
}
