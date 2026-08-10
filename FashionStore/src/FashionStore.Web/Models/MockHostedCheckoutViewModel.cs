namespace FashionStore.Web.Models;

/// <summary>
/// View model for the placeholder hosted-checkout gateway page. Carries the values
/// the storefront handed off (provider code, order number, amount, currency) plus
/// the return/cancel URLs the browser is sent to after the customer decides.
/// </summary>
public sealed record MockHostedCheckoutViewModel(
    string ProviderCode,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string ReturnUrl,
    string CancelUrl);
