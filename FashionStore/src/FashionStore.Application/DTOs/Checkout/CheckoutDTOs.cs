using FashionStore.Application.DTOs.Account;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.DTOs.Promotions;
using FashionStore.Application.DTOs.Shipping;

namespace FashionStore.Application.DTOs.Checkout;

/// <summary>
/// Free-form checkout address submitted by the browser. Only the address fields
/// are accepted; the engine validates the country and required fields server-side
/// and never trusts a client-computed total. A logged-in customer may select a
/// saved address card instead, in which case the card id is carried as
/// <see cref="SavedAddressId"/> and the free-form fields are ignored.
/// </summary>
public sealed record CheckoutAddressInput(
    string? SavedAddressId,
    string RecipientName,
    string? Phone,
    string AddressLine1,
    string? AddressLine2,
    string? Area,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? DeliveryInstructions);

/// <summary>
/// A supported payment method presented during checkout. <see cref="Code"/> is a
/// stable machine key (for example "cod" or "card"); the checkout engine resolves
/// eligibility from the catalog server-side. When <see cref="RequiresCodShipping"/>
/// is true the method is only eligible if the selected shipping method supports
/// cash on delivery.
/// </summary>
public sealed record PaymentMethodOption(
    string Code,
    string Name,
    string Description,
    bool RequiresCodShipping);

/// <summary>
/// Input for the central server-side checkout calculation. The cart lines and
/// subtotal are always resolved server-side before this record is built; the
/// browser only ever supplies the free-form destination, the selected method ids,
/// guest contact details and the terms flag. All pricing is recomputed on the
/// server, so a browser can never influence a displayed or payable total.
/// </summary>
public sealed record CheckoutCalculationInput(
    string? UserId,
    IReadOnlyList<CartItemDto> Items,
    string? CouponCode,
    string? GuestEmail,
    string? GuestPhone,
    CheckoutAddressInput? ShippingAddress,
    CheckoutAddressInput? BillingAddress,
    bool BillingSameAsShipping,
    Guid? ShippingMethodId,
    string? PaymentMethodCode,
    bool TermsAccepted,
    string? ContinuationToken);

/// <summary>
/// A single checkout line normalized by the engine from the server-resolved cart.
/// Prices are always recomputed server-side; snapshots of the product and variation
/// (name, slug, SKU, colour, size, image) are included so the review step can render
/// them and so a future order can persist the exact values at placement time.
/// </summary>
public sealed record CheckoutLineItemDto(
    Guid ProductId,
    Guid VariantId,
    string ProductName,
    string Slug,
    string Sku,
    string? ColourName,
    string? SizeName,
    string? ImageUrl,
    decimal UnitPrice,
    decimal? CompareAtPrice,
    int Quantity,
    decimal LineSubtotal,
    decimal PromotionsDiscount,
    decimal CouponDiscount,
    decimal LineTotal);

/// <summary>
/// The selected delivery method and its server-computed price, as chosen from the
/// available <see cref="ShippingQuoteDto"/> options.
/// </summary>
public sealed record CheckoutSelectedShippingDto(
    Guid MethodId,
    string Code,
    string Name,
    decimal Price,
    bool IsFree,
    int EstimatedMinDays,
    int EstimatedMaxDays,
    bool SupportsCashOnDelivery,
    string? PickupInstructions);

/// <summary>
/// The final financial breakdown. Every value is computed on the server; the
/// browser never supplies a subtotal, shipping charge, tax or total.
/// </summary>
public sealed record CheckoutTotalsDto(
    decimal Subtotal,
    decimal PromotionsDiscount,
    decimal CouponDiscount,
    decimal Shipping,
    decimal Tax,
    decimal GrandTotal,
    decimal AmountPayable,
    string Currency,
    bool IsFreeShipping);

/// <summary>
/// The tax breakdown: the effective rate applied for the destination country and
/// the taxable base (post-discount goods total plus shipping) that produced the
/// tax amount.
/// </summary>
public sealed record CheckoutTaxBreakdownDto(
    decimal RatePercent,
    decimal TaxableAmount,
    decimal TaxAmount,
    string Currency);

/// <summary>
/// A single validation problem detected by the engine. <see cref="Field"/> targets
/// the affected input for inline display; <see cref="Code"/> is a stable machine
/// key the UI can switch on and <see cref="Message"/> is a human-readable reason.
/// </summary>
public sealed record CheckoutValidationError(
    string Field,
    string Code,
    string Message);

/// <summary>
/// Result of a server-side checkout calculation. <see cref="IsValid"/> is true only
/// when every rule passes (review-ready). <see cref="ContinuationToken"/> is a
/// deterministic signature over the calculation inputs and totals; when the client
/// returns a previous token on a later call and it differs, <see cref="PricesChanged"/>
/// is raised so the UI can warn that the quoted totals are stale.
/// </summary>
public sealed record CheckoutCalculationResult(
    bool IsValid,
    IReadOnlyList<CheckoutValidationError> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CheckoutLineItemDto> Lines,
    IReadOnlyList<ShippingQuoteDto> ShippingOptions,
    CheckoutSelectedShippingDto? SelectedShipping,
    CheckoutTotalsDto Totals,
    CheckoutTaxBreakdownDto Tax,
    IReadOnlyList<DiscountBreakdownItem> Discounts,
    string ContinuationToken,
    bool PricesChanged);

/// <summary>
/// View model for the multi-step checkout page: the server-resolved cart, the
/// customer's saved addresses (when signed in), the selectable countries and the
/// eligible payment methods. All pricing shown here is server-computed.
/// </summary>
public sealed record CheckoutViewData(
    IReadOnlyList<CartItemDto> Items,
    decimal Subtotal,
    string FormattedSubtotal,
    bool IsAuthenticated,
    string? UserEmail,
    string? UserPhone,
    IReadOnlyList<AddressDto> SavedAddresses,
    IReadOnlyList<CountryOption> Countries,
    IReadOnlyList<PaymentMethodOption> PaymentMethods);
