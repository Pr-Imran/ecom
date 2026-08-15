using System.Text.Json;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Products;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

public class ProductVariationService : IProductVariationService
{
    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ProductVariationService> _logger;
    private readonly CacheSettings _cacheSettings;

    public ProductVariationService(
        AppDbContext context,
        IDistributedCache cache,
        ILogger<ProductVariationService> logger,
        CacheSettings cacheSettings)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
        _cacheSettings = cacheSettings;
    }

    public async Task<IEnumerable<ProductAttributeDto>> GetVariationAttributesAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _context.ProductAttributes
            .Include(a => a.Values)
            .AsNoTracking();

        if (!includeInactive)
            query = query.Where(a => a.IsActive);

        var attributes = await query
            .OrderBy(a => a.DisplayOrder)
            .ThenBy(a => a.Name)
            .ToListAsync(cancellationToken);

        return attributes.Select(ToDto);
    }

    public async Task<ProductAttributeDto?> GetAttributeByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var attribute = await _context.ProductAttributes
            .Include(a => a.Values)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        return attribute != null ? ToDto(attribute) : null;
    }

    public async Task<ProductAttributeDto> CreateAttributeAsync(CreateProductAttributeRequest request, CancellationToken cancellationToken = default)
    {
        var slug = GenerateSlug(request.Name);
        if (!await IsAttributeSlugUniqueAsync(slug, null, cancellationToken))
            throw new InvalidOperationException($"Attribute with slug '{slug}' already exists");

        var attribute = new ProductAttribute
        {
            Name = request.Name,
            Slug = slug,
            DisplayType = request.DisplayType,
            IsVariationAttribute = request.IsVariationAttribute,
            DisplayOrder = request.DisplayOrder,
            Description = request.Description
        };

        _context.ProductAttributes.Add(attribute);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        _logger.LogInformation("Created attribute {AttributeId} - {Name}", attribute.Id, attribute.Name);
        return ToDto(attribute);
    }

    public async Task<ProductAttributeDto?> UpdateAttributeAsync(UpdateProductAttributeRequest request, CancellationToken cancellationToken = default)
    {
        var attribute = await _context.ProductAttributes.FindAsync(new object[] { request.Id }, cancellationToken);
        if (attribute == null) return null;

        var slug = GenerateSlug(request.Name);
        if (!await IsAttributeSlugUniqueAsync(slug, request.Id, cancellationToken))
            throw new InvalidOperationException($"Attribute with slug '{slug}' already exists");

        attribute.Name = request.Name;
        attribute.Slug = slug;
        attribute.DisplayType = request.DisplayType;
        attribute.IsVariationAttribute = request.IsVariationAttribute;
        attribute.DisplayOrder = request.DisplayOrder;
        attribute.Description = request.Description;

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        _logger.LogInformation("Updated attribute {AttributeId}", request.Id);
        return ToDto(attribute);
    }

    public async Task<bool> DeleteAttributeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var attribute = await _context.ProductAttributes
            .Include(a => a.Values)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (attribute == null) return false;

        if (attribute.Values.Any(v => v.VariantAttributeValues.Count > 0))
            throw new InvalidOperationException("Cannot delete attribute that has values used by variants");

        _context.ProductAttributes.Remove(attribute);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        _logger.LogInformation("Deleted attribute {AttributeId}", id);
        return true;
    }

    public async Task<ProductAttributeValueDto?> GetAttributeValueByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var value = await _context.ProductAttributeValues.FindAsync(new object[] { id }, cancellationToken);
        return value != null ? ToDto(value) : null;
    }

    public async Task<ProductAttributeValueDto> CreateAttributeValueAsync(CreateProductAttributeValueRequest request, CancellationToken cancellationToken = default)
    {
        var attribute = await _context.ProductAttributes.FindAsync(new object[] { request.ProductAttributeId }, cancellationToken);
        if (attribute == null) throw new InvalidOperationException($"Attribute {request.ProductAttributeId} not found");

        var slug = GenerateSlug(request.Name);
        if (!await IsAttributeValueSlugUniqueAsync(slug, request.ProductAttributeId, null, cancellationToken))
            throw new InvalidOperationException($"Attribute value with slug '{slug}' already exists for this attribute");

        var value = new ProductAttributeValue
        {
            ProductAttributeId = request.ProductAttributeId,
            Name = request.Name,
            Slug = slug,
            DisplayValue = request.DisplayValue ?? request.Name,
            HexColour = request.HexColour,
            ImageUrl = request.ImageUrl,
            DisplayOrder = request.DisplayOrder
        };

        _context.ProductAttributeValues.Add(value);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        _logger.LogInformation("Created attribute value {ValueId} - {Name} for attribute {AttributeId}", value.Id, value.Name, request.ProductAttributeId);
        return ToDto(value);
    }

    public async Task<ProductAttributeValueDto?> UpdateAttributeValueAsync(UpdateProductAttributeValueRequest request, CancellationToken cancellationToken = default)
    {
        var value = await _context.ProductAttributeValues.FindAsync(new object[] { request.Id }, cancellationToken);
        if (value == null) return null;

        if (!await IsAttributeValueSlugUniqueAsync(GenerateSlug(request.Name), value.ProductAttributeId, request.Id, cancellationToken))
            throw new InvalidOperationException($"Attribute value with this slug already exists");

        value.Name = request.Name;
        value.Slug = GenerateSlug(request.Name);
        value.DisplayValue = request.DisplayValue ?? request.Name;
        value.HexColour = request.HexColour;
        value.ImageUrl = request.ImageUrl;
        value.DisplayOrder = request.DisplayOrder;

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        _logger.LogInformation("Updated attribute value {ValueId}", request.Id);
        return ToDto(value);
    }

    public async Task<bool> DeleteAttributeValueAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var value = await _context.ProductAttributeValues
            .Include(v => v.VariantAttributeValues)
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (value == null) return false;

        if (value.VariantAttributeValues.Count > 0)
            throw new InvalidOperationException("Cannot delete attribute value used by variants");

        _context.ProductAttributeValues.Remove(value);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);

        _logger.LogInformation("Deleted attribute value {ValueId}", id);
        return true;
    }

    public async Task<IEnumerable<ProductVariantDto>> GetVariantsByProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var variants = await _context.ProductVariants
            .Where(v => v.ProductId == productId)
            .Include(v => v.VariantAttributeValues)
                .ThenInclude(v => v.AttributeValue)
            .OrderBy(v => v.IsDefault ? 0 : 1)
            .ThenBy(v => v.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return variants.Select(v => ToDto(v));
    }

    public async Task<ProductVariantDto?> GetVariantByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var variant = await _context.ProductVariants.FindAsync(new object[] { id }, cancellationToken);
        if (variant == null) return null;

        return await ToDtoAsync(variant, cancellationToken);
    }

    public async Task<ProductVariantDto?> GetVariantBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        var variant = await _context.ProductVariants
            .Include(v => v.VariantAttributeValues)
                .ThenInclude(vav => vav.AttributeValue)
            .FirstOrDefaultAsync(v => v.Sku == sku, cancellationToken);

        return variant != null ? ToDto(variant) : null;
    }

    public async Task<ProductVariantDto> CreateVariantAsync(CreateProductVariantRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsSkuUniqueAsync(request.Sku, null, cancellationToken))
            throw new InvalidOperationException($"Variant with SKU '{request.Sku}' already exists");

        if (request.IsDefault)
        {
            await SetNoDefaultVariantAsync(request.ProductId, null, cancellationToken);
        }

        if (await HasDuplicateCombinationsAsync(request.ProductId, request.AttributeValueIds, null, cancellationToken))
            throw new InvalidOperationException("A variant with this combination of attribute values already exists");

        ValidatePrice(request.Price, request.CompareAtPrice, request.CostPrice);

        var variant = new ProductVariant
        {
            ProductId = request.ProductId,
            Sku = request.Sku,
            Barcode = request.Barcode,
            Price = request.Price,
            CompareAtPrice = request.CompareAtPrice,
            CostPrice = request.CostPrice,
            Weight = request.Weight,
            IsDefault = request.IsDefault,
            IsActive = request.IsActive,
            StockQuantity = request.StockQuantity,
            ImageUrl = request.ImageUrl,
            Notes = request.Notes
        };

        _context.ProductVariants.Add(variant);

        foreach (var attributeValueId in request.AttributeValueIds)
        {
            variant.VariantAttributeValues.Add(new ProductVariantAttributeValue
            {
                ProductVariantId = variant.Id,
                ProductAttributeValueId = attributeValueId
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);
        await InvalidateVariationsCacheAsync(variant.ProductId, cancellationToken);

        _logger.LogInformation("Created variant {VariantId} - {Sku} for product {ProductId}", variant.Id, variant.Sku, request.ProductId);
        return ToDto(variant);
    }

    public async Task<ProductVariantDto?> UpdateVariantAsync(UpdateProductVariantRequest request, CancellationToken cancellationToken = default)
    {
        var variant = await _context.ProductVariants
            .Include(v => v.VariantAttributeValues)
            .FirstOrDefaultAsync(v => v.Id == request.Id, cancellationToken);

        if (variant == null) return null;

        if (!await IsSkuUniqueAsync(request.Sku, request.Id, cancellationToken))
            throw new InvalidOperationException($"Variant with SKU '{request.Sku}' already exists");

        if (request.IsDefault && variant.IsDefault != request.IsDefault)
        {
            await SetNoDefaultVariantAsync(request.ProductId, request.Id, cancellationToken);
        }

        ValidatePrice(request.Price, request.CompareAtPrice, request.CostPrice);

        variant.Sku = request.Sku;
        variant.Barcode = request.Barcode;
        variant.Price = request.Price;
        variant.CompareAtPrice = request.CompareAtPrice;
        variant.CostPrice = request.CostPrice;
        variant.Weight = request.Weight;
        variant.IsDefault = request.IsDefault;
        variant.IsActive = request.IsActive;
        variant.StockQuantity = request.StockQuantity;
        variant.ImageUrl = request.ImageUrl;
        variant.Notes = request.Notes;

        variant.VariantAttributeValues.Clear();
        foreach (var attributeValueId in request.AttributeValueIds)
        {
            variant.VariantAttributeValues.Add(new ProductVariantAttributeValue
            {
                ProductVariantId = variant.Id,
                ProductAttributeValueId = attributeValueId
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);
        await InvalidateVariationsCacheAsync(variant.ProductId, cancellationToken);

        _logger.LogInformation("Updated variant {VariantId}", request.Id);
        return ToDto(variant);
    }

    public async Task<bool> DeleteVariantAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var variant = await _context.ProductVariants.FindAsync(new object[] { id }, cancellationToken);
        if (variant == null) return false;

        var productId = variant.ProductId;
        _context.ProductVariants.Remove(variant);
        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);
        await InvalidateVariationsCacheAsync(productId, cancellationToken);

        _logger.LogInformation("Deleted variant {VariantId}", id);
        return true;
    }

    public async Task<List<VariantCombinationDto>> GenerateCombinationsAsync(GenerateVariantsRequest request, CancellationToken cancellationToken = default)
    {
        var attributeValues = await _context.ProductAttributeValues
            .Include(v => v.ProductAttribute)
            .Where(v => request.AttributeValueIds.Contains(v.Id))
            .ToListAsync(cancellationToken);

        var groupedValues = attributeValues
            .GroupBy(v => v.ProductAttributeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var combinations = new List<VariantCombinationDto>();

        var cartesianProduct = groupedValues.Values.CartesianProduct();

        foreach (var combination in cartesianProduct)
        {
            var displayValues = combination.ToDictionary(
                v => v.ProductAttribute!.Name,
                v => v.Name
            );

            var suggestedSku = GenerateVariantSku(request.SkuPattern, combination);

            var existingVariant = await _context.ProductVariants
                .Include(v => v.VariantAttributeValues)
                .FirstOrDefaultAsync(v =>
                    v.ProductId == request.ProductId &&
                    v.VariantAttributeValues.All(vav => request.AttributeValueIds.Contains(vav.ProductAttributeValueId)) &&
                    v.VariantAttributeValues.Count == combination.Count(),
                    cancellationToken);

            combinations.Add(new VariantCombinationDto(
                combination.Select(v => v.Id).ToList(),
                displayValues,
                suggestedSku,
                existingVariant?.Sku,
                existingVariant?.Price,
                existingVariant?.CompareAtPrice,
                existingVariant?.StockQuantity,
                existingVariant?.IsActive,
                existingVariant?.IsDefault,
                existingVariant != null,
                existingVariant?.Id
            ));
        }

        return combinations;
    }

    public async Task SaveGeneratedVariantsAsync(List<CreateProductVariantRequest> variants, CancellationToken cancellationToken = default)
    {
        foreach (var variant in variants)
        {
            try
            {
                await CreateVariantAsync(variant, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Skipping variant {Sku}: {Error}", variant.Sku, ex.Message);
            }
        }
    }

    public async Task BulkUpdateVariantsAsync(BulkUpdateVariantsRequest request, CancellationToken cancellationToken = default)
    {
        var variants = await _context.ProductVariants
            .Where(v => request.VariantIds.Contains(v.Id))
            .ToListAsync(cancellationToken);

        foreach (var variant in variants)
        {
            if (request.NewPrice.HasValue)
            {
                variant.Price = request.NewPrice.Value;
            }
            else if (request.PriceAdjustment.HasValue)
            {
                if (request.PriceAdjustmentIsPercentage)
                {
                    variant.Price = variant.Price * (1 + request.PriceAdjustment.Value / 100);
                }
                else
                {
                    variant.Price = variant.Price + request.PriceAdjustment.Value;
                }
            }

            if (request.NewStock.HasValue)
            {
                variant.StockQuantity = request.NewStock.Value;
            }
            else if (request.StockAdjustment.HasValue)
            {
                variant.StockQuantity = (variant.StockQuantity ?? 0) + request.StockAdjustment.Value;
            }

            if (request.IsActive.HasValue)
            {
                variant.IsActive = request.IsActive.Value;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await InvalidateHomePageCacheAsync(cancellationToken);
        await InvalidateVariationsCacheAsync(variants.Select(v => v.ProductId).Distinct().ToList(), cancellationToken);
        _logger.LogInformation("Bulk updated {Count} variants", variants.Count);
    }

    public async Task<StorefrontProductVariationsDto> GetStorefrontVariationsAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var key = $"product:{productId}:variations";
        var cached = await _cache.GetStringAsync(key, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
            return JsonSerializer.Deserialize<StorefrontProductVariationsDto>(cached)!;

        var variants = await _context.ProductVariants
            .Where(v => v.ProductId == productId && v.IsActive)
            .ToListAsync(cancellationToken);

        var variantIds = variants.Select(v => v.Id).ToList();

        var variantAttributeValueMap = await _context.ProductVariantAttributeValues
            .Include(vav => vav.AttributeValue)
            .Where(vav => variantIds.Contains(vav.ProductVariantId))
            .ToListAsync(cancellationToken)
            .ContinueWith(task => task.Result.GroupBy(vav => vav.ProductVariantId).ToDictionary(g => g.Key, g => g.ToList()), cancellationToken);

        var activeValues = await _context.ProductAttributeValues
            .Include(v => v.ProductAttribute)
            .Where(v => v.IsActive && v.ProductAttribute != null && v.ProductAttribute.IsActive && v.ProductAttribute.IsVariationAttribute)
            .ToListAsync(cancellationToken);

        var activeValueIds = activeValues.Select(v => v.Id).ToHashSet();

        var availableOptionGroups = activeValues
            .GroupBy(v => v.ProductAttribute!)
            .Select(g => new StorefrontVariationOptionDto(
                g.Key.Name,
                g.Key.Slug,
                g.Select(v => new StorefrontVariationOptionValueDto(
                    v.Id,
                    v.Name,
                    v.Slug,
                    v.DisplayValue,
                    v.HexColour,
                    v.ImageUrl,
                    variantIds.Any(id => variantAttributeValueMap.TryGetValue(id, out var vavs) && vavs.Any(vav => vav.ProductAttributeValueId == v.Id))
                )).ToList()
            )).ToList();

        var storefrontVariants = variants
            .Where(v => variantAttributeValueMap.TryGetValue(v.Id, out var vavs) && vavs.All(vav => activeValueIds.Contains(vav.ProductAttributeValueId)))
            .Select(v =>
            {
                var vavs = variantAttributeValueMap[v.Id];
                var attrNames = vavs.ToDictionary(
                    vav => vav.AttributeValue?.ProductAttribute?.Name ?? string.Empty,
                    vav => vav.AttributeValue?.Name ?? string.Empty
                );
                var attrSlugs = vavs.ToDictionary(
                    vav => vav.AttributeValue?.ProductAttribute?.Slug ?? string.Empty,
                    vav => (string?)vav.ProductAttributeValueId.ToString()
                );

                var availableStock = Math.Max(0, (v.StockQuantity ?? 0) - (v.ReservedStock ?? 0));

                return new StorefrontVariantDto(
                    v.Id,
                    v.Sku,
                    v.Price,
                    v.CompareAtPrice,
                    availableStock > 0,
                    availableStock,
                    v.ImageUrl,
                    attrNames,
                    attrSlugs,
                    v.IsDefault
                );
            }).ToList();

        var availableCombinations = variants
            .Where(v => variantAttributeValueMap.TryGetValue(v.Id, out var vavs))
            .Select(v =>
            {
                var vavs = variantAttributeValueMap[v.Id];
                var attrSlugs = vavs
                    .OrderBy(vav => vav.AttributeValue?.ProductAttribute?.DisplayOrder ?? 0)
                    .Select(vav => vav.AttributeValue?.ProductAttribute?.Slug ?? string.Empty)
                    .ToList();

                var valueSlugs = vavs
                    .OrderBy(vav => vav.AttributeValue?.ProductAttribute?.DisplayOrder ?? 0)
                    .Select(vav => vav.AttributeValue?.Slug ?? string.Empty)
                    .ToList();

                var availableStock = Math.Max(0, (v.StockQuantity ?? 0) - (v.ReservedStock ?? 0));

                return new VariantCombinationAvailabilityDto(
                    attrSlugs,
                    valueSlugs,
                    v.Id,
                    true,
                    availableStock > 0,
                    v.Price,
                    v.ImageUrl
                );
            }).ToList();

        var result = new StorefrontProductVariationsDto(
            productId,
            availableOptionGroups,
            storefrontVariants,
            availableCombinations
        );

        await _cache.SetStringAsync(key, JsonSerializer.Serialize(result), GetCacheOptions(), cancellationToken);
        return result;
    }

    public async Task<ProductVariantDto?> GetVariantByAttributeValuesAsync(Guid productId, List<Guid> attributeValueIds, CancellationToken cancellationToken = default)
    {
        var variants = await _context.ProductVariants
            .Where(v => v.ProductId == productId)
            .Include(v => v.VariantAttributeValues)
            .ToListAsync(cancellationToken);

        var matchingVariant = variants.FirstOrDefault(v =>
            v.VariantAttributeValues.Count == attributeValueIds.Count &&
            v.VariantAttributeValues.All(vav => attributeValueIds.Contains(vav.ProductAttributeValueId)));

        return matchingVariant != null ? ToDto(matchingVariant) : null;
    }

    public async Task<bool> IsSkuUniqueAsync(string sku, Guid? excludeVariantId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.ProductVariants.Where(v => v.Sku == sku);
        if (excludeVariantId.HasValue)
            query = query.Where(v => v.Id != excludeVariantId.Value);
        return !await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> HasDuplicateCombinationsAsync(Guid productId, List<Guid> attributeValueIds, Guid? excludeVariantId = null, CancellationToken cancellationToken = default)
    {
        var variants = await _context.ProductVariants
            .Where(v => v.ProductId == productId)
            .Include(v => v.VariantAttributeValues)
            .ToListAsync(cancellationToken);

        foreach (var variant in variants)
        {
            if (excludeVariantId.HasValue && variant.Id == excludeVariantId.Value)
                continue;

            var variantValueIds = variant.VariantAttributeValues.Select(vav => vav.ProductAttributeValueId).ToHashSet();
            if (variantValueIds.SetEquals(attributeValueIds))
                return true;
        }

        return false;
    }

    private ProductAttributeDto ToDto(ProductAttribute attribute) => new(
        attribute.Id,
        attribute.Name,
        attribute.Slug,
        attribute.DisplayType,
        attribute.IsVariationAttribute,
        attribute.IsActive,
        attribute.DisplayOrder,
        attribute.Description,
        attribute.Values.Where(v => v.IsActive).OrderBy(v => v.DisplayOrder).Select(ToDto).ToList(),
        attribute.CreatedAtUtc
    );

    private ProductAttributeValueDto ToDto(ProductAttributeValue value) => new(
        value.Id,
        value.ProductAttributeId,
        value.Name,
        value.Slug,
        value.DisplayValue,
        value.HexColour,
        value.ImageUrl,
        value.IsActive,
        value.DisplayOrder
    );

    private async Task<ProductVariantDto> ToDtoAsync(ProductVariant variant, CancellationToken cancellationToken)
    {
        var variantWithValues = await _context.ProductVariants
            .Where(v => v.Id == variant.Id)
            .Include(v => v.VariantAttributeValues)
                .ThenInclude(vav => vav.AttributeValue)
                    .ThenInclude(av => av!.ProductAttribute)
            .FirstOrDefaultAsync(cancellationToken);

        if (variantWithValues == null)
        {
            throw new InvalidOperationException($"Variant {variant.Id} not found");
        }

        var attributeValues = variantWithValues.VariantAttributeValues
            .Where(vav => vav.AttributeValue != null && vav.AttributeValue.ProductAttribute != null)
            .ToDictionary(
                vav => vav.AttributeValue!.ProductAttribute!.Name,
                vav => vav.AttributeValue!.Name
            );

        return new ProductVariantDto(
            variantWithValues.Id,
            variantWithValues.ProductId,
            variantWithValues.Sku,
            variantWithValues.Barcode,
            variantWithValues.Price,
            variantWithValues.CompareAtPrice,
            variantWithValues.CostPrice,
            variantWithValues.Weight,
            variantWithValues.IsDefault,
            variantWithValues.IsActive,
            variantWithValues.StockQuantity,
            variantWithValues.ReservedStock,
            variantWithValues.LowStockThreshold,
            variantWithValues.ImageUrl,
            variantWithValues.Notes,
            attributeValues,
            variantWithValues.CreatedAtUtc,
            variantWithValues.UpdatedAtUtc
        );
    }

    private ProductVariantDto ToDto(ProductVariant variant)
    {
        var attributeValues = variant.VariantAttributeValues
            .Where(vav => vav!.AttributeValue != null && vav.AttributeValue.ProductAttribute != null)
            .ToDictionary(
                vav => vav!.AttributeValue!.ProductAttribute!.Name,
                vav => vav!.AttributeValue!.Name
            );

        return new ProductVariantDto(
            variant.Id,
            variant.ProductId,
            variant.Sku,
            variant.Barcode,
            variant.Price,
            variant.CompareAtPrice,
            variant.CostPrice,
            variant.Weight,
            variant.IsDefault,
            variant.IsActive,
            variant.StockQuantity,
            variant.ReservedStock,
            variant.LowStockThreshold,
            variant.ImageUrl,
            variant.Notes,
            attributeValues,
            variant.CreatedAtUtc,
            variant.UpdatedAtUtc
        );
    }

    private string GenerateSlug(string name) => name.ToLowerInvariant()
        .Replace(" ", "-")
        .Replace("--", "-")
        .Trim('-');

    private string GenerateVariantSku(string pattern, IEnumerable<ProductAttributeValue> values)
    {
        var sku = pattern;
        foreach (var value in values)
        {
            sku = sku.Replace($"{{{value.ProductAttribute!.Slug}}}", value.Slug.ToUpperInvariant());
        }
        return sku.Replace("{}", string.Empty).Replace("--", "-").Trim('-');
    }

    private async Task SetNoDefaultVariantAsync(Guid productId, Guid? setAsDefaultVariantId, CancellationToken cancellationToken)
    {
        var variants = await _context.ProductVariants
            .Where(v => v.ProductId == productId && v.IsDefault)
            .ToListAsync(cancellationToken);

        foreach (var variant in variants)
        {
            variant.IsDefault = false;
        }

        if (setAsDefaultVariantId.HasValue)
        {
            var variant = await _context.ProductVariants.FindAsync(new object[] { setAsDefaultVariantId.Value }, cancellationToken);
            if (variant != null)
            {
                variant.IsDefault = true;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> IsAttributeSlugUniqueAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.ProductAttributes.Where(a => a.Slug == slug);
        if (excludeId.HasValue)
            query = query.Where(a => a.Id != excludeId.Value);
        return !await query.AnyAsync(cancellationToken);
    }

    private async Task<bool> IsAttributeValueSlugUniqueAsync(string slug, Guid attributeId, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.ProductAttributeValues.Where(v => v.Slug == slug && v.ProductAttributeId == attributeId);
        if (excludeId.HasValue)
            query = query.Where(v => v.Id != excludeId.Value);
        return !await query.AnyAsync(cancellationToken);
    }

    private static void ValidatePrice(decimal price, decimal? compareAtPrice, decimal? costPrice)
    {
        if (price < 0)
            throw new InvalidOperationException("Price cannot be negative");

        if (compareAtPrice.HasValue && compareAtPrice.Value < price)
            throw new InvalidOperationException("Compare at price must be greater than or equal to price");

        if (costPrice.HasValue && costPrice.Value < 0)
            throw new InvalidOperationException("Cost price cannot be negative");
    }

    private DistributedCacheEntryOptions GetCacheOptions() => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheSettings.AbsoluteExpirationMinutes)
    };

    private async Task InvalidateHomePageCacheAsync(CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(Application.Common.CacheKeys.HomePage, cancellationToken);
    }

    private async Task InvalidateVariationsCacheAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync($"product:{productId}:variations", cancellationToken);
    }

    private async Task InvalidateVariationsCacheAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default)
    {
        foreach (var productId in productIds.Distinct())
        {
            await _cache.RemoveAsync($"product:{productId}:variations", cancellationToken);
        }
    }
}

public static class EnumerableExtensions
{
    public static IEnumerable<IEnumerable<T>> CartesianProduct<T>(this IEnumerable<IEnumerable<T>> sequences)
    {
        IEnumerable<IEnumerable<T>> emptyProduct = new[] { Enumerable.Empty<T>() };
        return sequences.Aggregate(
            emptyProduct,
            (accumulator, sequence) =>
                from accseq in accumulator
                from item in sequence
                select accseq.Concat(new[] { item }));
    }
}
