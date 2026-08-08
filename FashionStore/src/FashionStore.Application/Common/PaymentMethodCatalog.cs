using FashionStore.Application.DTOs.Checkout;

namespace FashionStore.Application.Common;

/// <summary>
/// The supported payment methods presented at checkout. The catalog is the single
/// server-side source of truth for eligibility; the browser only ever submits a
/// stable method code. Real charging is not performed here — Phase 20 introduces
/// the extensible online payment integration — but COD support is already gated on
/// the selected shipping method.
/// </summary>
public static class PaymentMethodCatalog
{
    public static IReadOnlyList<PaymentMethodOption> All { get; } = new[]
    {
        new PaymentMethodOption(
            "cod",
            "Cash on Delivery",
            "Pay when your order is delivered.",
            RequiresCodShipping: true),
        new PaymentMethodOption(
            "card",
            "Card Payment",
            "Pay securely online when your order is placed.",
            RequiresCodShipping: false)
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
