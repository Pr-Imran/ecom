using FashionStore.Application.DTOs.Products;

namespace FashionStore.Application.Interfaces;

public interface IProductVariationService
{
    Task<IEnumerable<ProductAttributeDto>> GetVariationAttributesAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<ProductAttributeDto?> GetAttributeByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductAttributeDto> CreateAttributeAsync(CreateProductAttributeRequest request, CancellationToken cancellationToken = default);
    Task<ProductAttributeDto?> UpdateAttributeAsync(UpdateProductAttributeRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAttributeAsync(Guid id, CancellationToken cancellationToken = default);
    
    Task<ProductAttributeValueDto?> GetAttributeValueByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductAttributeValueDto> CreateAttributeValueAsync(CreateProductAttributeValueRequest request, CancellationToken cancellationToken = default);
    Task<ProductAttributeValueDto?> UpdateAttributeValueAsync(UpdateProductAttributeValueRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAttributeValueAsync(Guid id, CancellationToken cancellationToken = default);
    
    Task<IEnumerable<ProductVariantDto>> GetVariantsByProductAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<ProductVariantDto?> GetVariantByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductVariantDto?> GetVariantBySkuAsync(string sku, CancellationToken cancellationToken = default);
    Task<ProductVariantDto> CreateVariantAsync(CreateProductVariantRequest request, CancellationToken cancellationToken = default);
    Task<ProductVariantDto?> UpdateVariantAsync(UpdateProductVariantRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteVariantAsync(Guid id, CancellationToken cancellationToken = default);
    
    Task<List<VariantCombinationDto>> GenerateCombinationsAsync(GenerateVariantsRequest request, CancellationToken cancellationToken = default);
    Task SaveGeneratedVariantsAsync(List<CreateProductVariantRequest> variants, CancellationToken cancellationToken = default);
    Task BulkUpdateVariantsAsync(BulkUpdateVariantsRequest request, CancellationToken cancellationToken = default);
    
    Task<StorefrontProductVariationsDto> GetStorefrontVariationsAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<ProductVariantDto?> GetVariantByAttributeValuesAsync(Guid productId, List<Guid> attributeValueIds, CancellationToken cancellationToken = default);
    
    Task<bool> IsSkuUniqueAsync(string sku, Guid? excludeVariantId = null, CancellationToken cancellationToken = default);
    Task<bool> HasDuplicateCombinationsAsync(Guid productId, List<Guid> attributeValueIds, Guid? excludeVariantId = null, CancellationToken cancellationToken = default);
}
