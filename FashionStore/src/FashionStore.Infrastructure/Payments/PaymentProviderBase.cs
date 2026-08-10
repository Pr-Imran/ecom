using System.Text.Json;
using System.Text.Json.Serialization;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Payments;

namespace FashionStore.Infrastructure.Payments;

/// <summary>
/// Common provider plumbing: configured settings, webhook signature verification
/// and the normalized webhook envelope parser. Placeholder providers use a shared
/// envelope shape so the payment service can process events without knowing
/// gateway details; live integrations override <see cref="TryParseWebhook"/> and
/// <see cref="VerifyWebhookSignature"/> with their own formats.
/// </summary>
public abstract class PaymentProviderBase
{
    protected PaymentProviderBase(PaymentProviderSettings settings)
    {
        Settings = settings;
    }

    protected PaymentProviderSettings Settings { get; }

    /// <summary>
    /// Verifies an HMAC-SHA256 signature over the raw body using the provider
    /// secret. Placeholder providers all use the shared scheme.
    /// </summary>
    public virtual bool VerifyWebhookSignature(string rawPayload, string? signature)
    {
        return PaymentWebhookSignature.Verify(Settings.WebhookSecret, rawPayload, signature);
    }

    /// <summary>
    /// Parses the shared webhook envelope. The envelope carries the event id, type,
    /// timestamp and the transaction/order/amount/currency the payment service
    /// verifies before applying any transition.
    /// </summary>
    public virtual PaymentWebhookEvent? TryParseWebhook(string rawPayload)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<WebhookEnvelope>(rawPayload, JsonOptions);
            if (envelope is null ||
                string.IsNullOrWhiteSpace(envelope.Id) ||
                string.IsNullOrWhiteSpace(envelope.Type) ||
                envelope.Data is null)
            {
                return null;
            }

            var type = envelope.Type.ToLowerInvariant();
            if (!type.StartsWith("payment.", StringComparison.Ordinal))
            {
                return null;
            }

            return new PaymentWebhookEvent(
                envelope.Id,
                type,
                envelope.Data.TransactionId,
                envelope.Data.OrderNumber,
                envelope.Data.Amount,
                envelope.Data.Currency ?? string.Empty,
                DateTimeOffset.FromUnixTimeSeconds(envelope.Timestamp).ToUniversalTime(),
                envelope.Data.FailureReason);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private sealed class WebhookEnvelope
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public long Timestamp { get; set; }
        public WebhookEnvelopeData? Data { get; set; }
    }

    private sealed class WebhookEnvelopeData
    {
        public string? TransactionId { get; set; }
        public string? OrderNumber { get; set; }
        public decimal Amount { get; set; }
        public string? Currency { get; set; }
        public string? FailureReason { get; set; }
    }
}
