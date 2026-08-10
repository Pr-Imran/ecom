using FashionStore.Application.DTOs.Checkout;

namespace FashionStore.Application.Common;

/// <summary>
/// The supported payment methods presented at checkout. The catalog is the single
/// server-side source of truth for eligibility; the browser only ever submits a
/// stable method code. Each method maps to the provider code used by the payment
/// abstraction (<see cref="FashionStore.Application.Interfaces.IPaymentProvider"/>).
/// Real charging is never performed by the catalog itself; the eligible method is
/// delegated to its provider at payment initiation time.
/// </summary>
public static class PaymentMethodCatalog
{
    public static IReadOnlyList<PaymentMethodOption> All { get; } = new[]
    {
        new PaymentMethodOption(
            "cod",
            "Cash on Delivery",
            "Pay when your order is delivered.",
            RequiresCodShipping: true,
            ProviderCode: "cod"),
        new PaymentMethodOption(
            "card",
            "Card Payment",
            "Pay securely online when your order is placed.",
            RequiresCodShipping: false,
            ProviderCode: "card"),
        new PaymentMethodOption(
            "mfs",
            "Mobile Wallet",
            "Pay instantly from your mobile money wallet.",
            RequiresCodShipping: false,
            ProviderCode: "mfs"),
        new PaymentMethodOption(
            "bank",
            "Bank Transfer",
            "Pay by bank transfer using your order reference.",
            RequiresCodShipping: false,
            ProviderCode: "bank")
    }.AsReadOnly();

    public static PaymentMethodOption? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return All.FirstOrDefault(p =>
            string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
    }
}
