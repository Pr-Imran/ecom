using System.Text.Json;
using FashionStore.Application.Configuration;
using FashionStore.Application.Interfaces;
using FashionStore.Infrastructure.Payments;
using FashionStore.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FashionStore.Web.Controllers;

/// <summary>
/// Payment gateway endpoints. The webhook endpoint is anonymous because a gateway
/// has no browser session; security comes from the shared-secret HMAC signature
/// that the payment service verifies before applying any transition. The mock
/// hosted-checkout page simulates a gateway so the whole hosted flow (redirect,
/// pay/cancel, signed webhook) is exercisable without a live provider.
/// </summary>
[Route("payments")]
public class PaymentController : Controller
{
    private readonly IPaymentService _paymentService;
    private readonly IOptions<PaymentSettings> _paymentOptions;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(
        IPaymentService paymentService,
        IOptions<PaymentSettings> paymentOptions,
        ILogger<PaymentController> logger)
    {
        _paymentService = paymentService;
        _paymentOptions = paymentOptions;
        _logger = logger;
    }

    /// <summary>
    /// Verifies and applies a provider webhook. The raw body is read verbatim so the
    /// signature can be validated against the exact bytes the provider signed.
    /// </summary>
    [HttpPost("webhook/{providerCode}")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(string providerCode, CancellationToken cancellationToken)
    {
        var rawPayload = await ReadRawBodyAsync(cancellationToken);
        var signature = Request.Headers[PaymentWebhookSignature.HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return BadRequest(new { success = false, failureReason = "Empty webhook payload." });
        }

        var result = await _paymentService.HandleWebhookAsync(
            providerCode,
            rawPayload,
            signature,
            cancellationToken);

        return Ok(new
        {
            success = result.Success,
            status = result.Status.ToString(),
            providerEventId = result.ProviderEventId,
            failureReason = result.FailureReason
        });
    }

    /// <summary>
    /// Placeholder gateway checkout page. The page simulates a hosted provider that
    /// the storefront redirects the customer to; choosing pay or cancel delivers a
    /// signed webhook back into the storefront webhook endpoint and then redirects
    /// the browser to the provider's return/cancel URL.
    /// </summary>
    [HttpGet("mock-hosted-checkout")]
    [AllowAnonymous]
    public IActionResult MockHostedCheckout(MockHostedCheckoutViewModel? model)
    {
        if (model is null ||
            string.IsNullOrWhiteSpace(model.ProviderCode) ||
            string.IsNullOrWhiteSpace(model.OrderNumber) ||
            model.Amount <= 0m)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(model);
    }

    /// <summary>
    /// Handles the customer's decision on the mock gateway page. Delivering the
    /// webhook is done server-side with the shared secret so the signature never
    /// reaches the browser.
    /// </summary>
    [HttpPost("mock-hosted-checkout/process")]
    [AllowAnonymous]
    public async Task<IActionResult> MockProcess(
        [FromForm] string providerCode,
        [FromForm] string orderNumber,
        [FromForm] decimal amount,
        [FromForm] string currency,
        [FromForm] string returnUrl,
        [FromForm] string cancelUrl,
        [FromForm] string decision,
        CancellationToken cancellationToken)
    {
        var isPay = string.Equals(decision, "pay", StringComparison.OrdinalIgnoreCase);

        var settings = _paymentOptions.Value.GetProvider(providerCode);
        if (settings is not null && !string.IsNullOrWhiteSpace(settings.WebhookSecret))
        {
            var eventType = isPay ? "payment.succeeded" : "payment.cancelled";
            var payload = BuildMockPayload(providerCode, orderNumber, amount, currency, eventType);
            var signature = PaymentWebhookSignature.Compute(settings.WebhookSecret, payload);

            try
            {
                var result = await _paymentService.HandleWebhookAsync(
                    providerCode,
                    payload,
                    signature,
                    cancellationToken);

                _logger.LogInformation(
                    "Mock hosted checkout delivered webhook for order {OrderNumber}: {Status}",
                    orderNumber,
                    result.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mock hosted checkout webhook delivery failed for order {OrderNumber}", orderNumber);
            }
        }

        var chosen = isPay ? returnUrl : cancelUrl;
        var target = string.IsNullOrWhiteSpace(chosen)
            ? Url.Action("Index", "Home") ?? "/"
            : chosen;

        return Redirect(target);
    }

    private async Task<string> ReadRawBodyAsync(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;
        return body;
    }

    /// <summary>
    /// Builds a webhook payload matching the shared envelope the placeholder
    /// providers parse, signed by the caller.
    /// </summary>
    private static string BuildMockPayload(
        string providerCode,
        string orderNumber,
        decimal amount,
        string currency,
        string eventType)
    {
        var envelope = new
        {
            id = $"mock-{Guid.NewGuid():N}",
            type = eventType,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = new
            {
                transactionId = (string?)null,
                orderNumber,
                amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
                currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.ToUpperInvariant(),
                failureReason = (string?)null
            }
        };

        return JsonSerializer.Serialize(envelope);
    }
}
