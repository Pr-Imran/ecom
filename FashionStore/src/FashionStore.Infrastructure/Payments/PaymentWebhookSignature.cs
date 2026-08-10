using System.Security.Cryptography;
using System.Text;

namespace FashionStore.Infrastructure.Payments;

/// <summary>
/// HMAC-SHA256 signature scheme used to verify payment webhooks. The signature is
/// a hex-encoded HMAC of the raw request body computed with the provider's shared
/// secret. The same helper signs the mock provider callbacks so the full flow is
/// testable without a live gateway.
/// </summary>
internal static class PaymentWebhookSignature
{
    /// <summary>Header carrying the provider webhook signature.</summary>
    public const string HeaderName = "X-Payment-Signature";

    /// <summary>Computes a hex-encoded HMAC-SHA256 over the raw payload.</summary>
    public static string Compute(string secret, string rawPayload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawPayload));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>Verifies that the supplied signature matches the raw payload, using a fixed-time comparison.</summary>
    public static bool Verify(string secret, string rawPayload, string? signature)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = Compute(secret, rawPayload);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature.Trim()));
    }
}
