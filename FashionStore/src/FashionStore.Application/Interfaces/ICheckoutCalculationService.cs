using FashionStore.Application.DTOs.Checkout;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// The central server-side checkout calculation and validation engine. It takes a
/// server-resolved cart plus the free-form checkout inputs, re-computes every price
/// and discount through the pricing and shipping engines, applies tax, validates
/// every rule and returns a normalized result with a continuation token. The
/// checkout must never trust a price or total supplied by the browser; this service
/// is the single authoritative source for what the customer is asked to pay.
/// </summary>
public interface ICheckoutCalculationService
{
    /// <summary>
    /// Calculates the full checkout for a server-resolved cart. The result is always
    /// deterministic and safe to display: prices, discounts, shipping, tax and totals
    /// are recomputed server-side, validation errors are returned grouped by field and
    /// a signature token is produced for safe continuation. When the caller supplies
    /// a <see cref="CheckoutCalculationInput.ContinuationToken"/> from a previous
    /// calculation that no longer matches, <see cref="CheckoutCalculationResult.PricesChanged"/>
    /// is set so the UI can warn that the quoted totals are stale.
    /// </summary>
    Task<CheckoutCalculationResult> CalculateAsync(
        CheckoutCalculationInput input,
        CancellationToken cancellationToken = default);
}
