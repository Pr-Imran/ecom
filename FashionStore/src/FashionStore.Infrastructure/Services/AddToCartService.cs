using FashionStore.Application.DTOs.Products;
using FashionStore.Application.Interfaces;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Server-side validation for the add-to-cart action. The client supplies only the
/// product, the variant and the quantity; the unit price, SKU, names, image, stock
/// and totals are always recomputed from the database so tampered requests cannot
/// influence pricing or bypass inventory checks.
/// </summary>
public sealed class AddToCartService : IAddToCartService
{
    private const int MaxQuantity = 99;
    private const string ColourAttributeName = "Colour";
    private const string SizeAttributeName = "Size";

    private readonly AppDbContext _context;
    private readonly ILogger<AddToCartService> _logger;

    public AddToCartService(AppDbContext context, ILogger<AddToCartService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<AddToCartResult> ValidateAsync(AddToCartRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ProductId == Guid.Empty || request.VariantId == Guid.Empty)
        {
            return Fail("Product or variation is missing.");
        }

        if (request.Quantity < 1)
        {
            return Fail("Quantity must be at least 1.");
        }

        if (request.Quantity > MaxQuantity)
        {
            return Fail($"Quantity cannot exceed {MaxQuantity}.");
        }

        var now = DateTime.UtcNow;

        var variant = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Include(v => v.VariantAttributeValues)
                .ThenInclude(vav => vav.AttributeValue)
                    .ThenInclude(av => av!.ProductAttribute)
            .FirstOrDefaultAsync(v => v.Id == request.VariantId, cancellationToken);

        if (variant is null || variant.ProductId != request.ProductId)
        {
            return Fail("The selected variation is no longer available.");
        }

        var product = variant.Product;
        if (product is null || !product.IsActive || product.PublishedAtUtc == null || product.PublishedAtUtc > now)
        {
            return Fail("This product is no longer available.");
        }

        if (!variant.IsActive)
        {
            return Fail("This variation is currently unavailable.");
        }

        var availableStock = (variant.StockQuantity ?? 0) - (variant.ReservedStock ?? 0);
        if (availableStock <= 0)
        {
            return Fail("This variation is out of stock.");
        }

        if (request.Quantity > availableStock)
        {
            return Fail($"Only {availableStock} item(s) left in stock.");
        }

        var attributeValues = variant.VariantAttributeValues
            .Where(vav => vav.AttributeValue != null && vav.AttributeValue.ProductAttribute != null)
            .ToList();

        var colourName = attributeValues
            .FirstOrDefault(vav => string.Equals(
                vav.AttributeValue!.ProductAttribute!.Name,
                ColourAttributeName,
                StringComparison.OrdinalIgnoreCase))?.AttributeValue?.Name;

        var sizeName = attributeValues
            .FirstOrDefault(vav => string.Equals(
                vav.AttributeValue!.ProductAttribute!.Name,
                SizeAttributeName,
                StringComparison.OrdinalIgnoreCase))?.AttributeValue?.Name;

        var item = new AddToCartItemDto(
            request.ProductId,
            variant.Id,
            product.Name,
            variant.Sku,
            variant.ImageUrl,
            colourName,
            sizeName,
            variant.Price,
            variant.CompareAtPrice,
            request.Quantity,
            variant.Price * request.Quantity,
            availableStock);

        _logger.LogInformation(
            "Validated add-to-cart for variant {VariantId} of product {ProductId} quantity {Quantity}",
            variant.Id,
            product.Id,
            request.Quantity);

        return new AddToCartResult(true, null, item);
    }

    private static AddToCartResult Fail(string message) => new(false, message, null);
}
