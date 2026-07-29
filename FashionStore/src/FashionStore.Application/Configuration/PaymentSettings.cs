namespace FashionStore.Application.Configuration;

public sealed class PaymentSettings
{
    public const string SectionName = "Payments";
    public string Provider { get; init; } = "Stripe";
    public string ApiKey { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;
    public string ReturnUrl { get; init; } = string.Empty;
    public string CancelUrl { get; init; } = string.Empty;
}
