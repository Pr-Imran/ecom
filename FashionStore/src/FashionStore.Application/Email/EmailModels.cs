namespace FashionStore.Application.Email;

/// <summary>
/// Shared fields rendered by every transactional email template and the responsive
/// email layout. Individual templates derive from this and add scenario-specific
/// content.
/// </summary>
public abstract class EmailTemplateModel
{
    public string Subject { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Preheader { get; set; } = string.Empty;
    public string RecipientName { get; set; } = "there";
    public string StoreName { get; set; } = "FashionStore";
    public string StoreUrl { get; set; } = string.Empty;
}

public sealed class ConfirmEmailEmail : EmailTemplateModel
{
    public string ConfirmUrl { get; set; } = string.Empty;
    public int ExpiryHours { get; set; } = 24;
}

public sealed class PasswordResetEmail : EmailTemplateModel
{
    public string ResetUrl { get; set; } = string.Empty;
    public int ExpiryHours { get; set; } = 1;
}

public sealed class WelcomeEmail : EmailTemplateModel
{
    public string ShopUrl { get; set; } = string.Empty;
}

/// <summary>A single line shown inside an order email.</summary>
public sealed record OrderLineEmail
{
    public string ProductName { get; set; } = string.Empty;
    public string Variant { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>Common state for every order-related notification.</summary>
public abstract class OrderEmail : EmailTemplateModel
{
    public string OrderNumber { get; set; } = string.Empty;
    public string OrderUrl { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
    public IReadOnlyList<OrderLineEmail> Items { get; set; } = Array.Empty<OrderLineEmail>();
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public string TrackingUrl { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class OrderPlacedEmail : OrderEmail { }

public sealed class PaymentReceivedEmail : OrderEmail { }

public sealed class PaymentFailedEmail : OrderEmail
{
    public decimal OutstandingAmount { get; set; }
}

public sealed class OrderProcessingEmail : OrderEmail { }

public sealed class OrderShippedEmail : OrderEmail { }

public sealed class OrderDeliveredEmail : OrderEmail { }

public sealed class OrderCancelledEmail : OrderEmail { }

public sealed class InvoiceEmail : OrderEmail
{
    public string InvoiceNumber { get; set; } = string.Empty;
}

public sealed class ReturnRequestedEmail : EmailTemplateModel
{
    public string ReturnNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
}

public sealed class ReturnApprovedEmail : EmailTemplateModel
{
    public string ReturnNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
}

public sealed class ReturnRejectedEmail : EmailTemplateModel
{
    public string ReturnNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class RefundCompletedEmail : EmailTemplateModel
{
    public string OrderNumber { get; set; } = string.Empty;
    public decimal RefundedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string OrderUrl { get; set; } = string.Empty;
}

public sealed record LowStockAlertItem
{
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Available { get; set; }
    public int? Threshold { get; set; }
    public string Variant { get; set; } = string.Empty;
}

public sealed class LowStockAlertEmail : EmailTemplateModel
{
    public IReadOnlyList<LowStockAlertItem> Items { get; set; } = Array.Empty<LowStockAlertItem>();
    public string InventoryUrl { get; set; } = string.Empty;
}
