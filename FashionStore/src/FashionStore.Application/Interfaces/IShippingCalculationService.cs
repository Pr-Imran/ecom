using FashionStore.Application.DTOs.Shipping;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// The extensible server-side shipping calculation engine. It resolves the active
/// shipping methods, matches the destination to the configured zones, applies
/// product / category restrictions, weight bands, maximum package weight, blackout
/// windows and free-shipping thresholds, and returns a fully priced quote. The
/// checkout must never trust a shipping cost submitted by the browser; it always
/// calls this service with the server-resolved cart.
/// </summary>
public interface IShippingCalculationService
{
    /// <summary>
    /// Computes the available delivery methods and their server-side prices for a
    /// cart and destination. The engine loads live product weights and categories
    /// itself so pricing cannot be influenced by the caller.
    /// </summary>
    Task<ShippingQuoteResultDto> QuoteAsync(ShippingCalculationInput input, CancellationToken cancellationToken = default);
}
