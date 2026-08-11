namespace FashionStore.Application.Configuration;

/// <summary>
/// Return and refund rules. The return window is enforced server-side against the
/// order's delivery date (falls back to the order creation date when the order was
/// never delivered); the browser never supplies these values.
/// </summary>
public sealed class ReturnSettings
{
    public const string SectionName = "Returns";

    /// <summary>Prefix used when generating public return numbers.</summary>
    public string ReturnNumberPrefix { get; init; } = "RMA";

    /// <summary>Prefix used when generating public refund (credit note) reference numbers.</summary>
    public string RefundNumberPrefix { get; init; } = "RFN";

    /// <summary>Global return window in days from delivery (or order creation when not delivered).</summary>
    public int ReturnWindowDays { get; init; } = 30;

    /// <summary>Whether the order's shipping charge may be refunded when all items are returned.</summary>
    public bool AllowShippingRefund { get; init; } = true;

    /// <summary>Whether manual (offline) refunds are permitted.</summary>
    public bool AllowManualRefund { get; init; } = true;

    /// <summary>Whether gateway (online) refunds are executed through the payment provider.</summary>
    public bool AllowGatewayRefund { get; init; } = true;

    /// <summary>Maximum number of photo attachments per return request.</summary>
    public int MaxAttachments { get; init; } = 6;

    /// <summary>Maximum size in bytes of a single return photo.</summary>
    public long MaxAttachmentBytes { get; init; } = 5242880;

    /// <summary>Allowed return photo extensions.</summary>
    public string[] AllowedAttachmentExtensions { get; init; } = { ".jpg", ".jpeg", ".png", ".webp" };
}
