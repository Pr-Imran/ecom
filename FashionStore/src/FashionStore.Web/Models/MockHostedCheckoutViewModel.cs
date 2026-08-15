namespace FashionStore.Web.Models;

/// <summary>
/// View model for the placeholder hosted-checkout gateway page. Carries the values
/// the storefront handed off (provider code, order number, amount, currency) plus
/// the return/cancel URLs the browser is sent to after the customer decides. The
/// <see cref="Ticket"/> carries the guest order access ticket (when the order is a
/// guest order) so the mock process can re-verify ownership before it delivers a
/// signed webhook.
/// </summary>
public sealed record MockHostedCheckoutViewModel(
    string ProviderCode,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string ReturnUrl,
    string CancelUrl,
    string? Ticket = null);
