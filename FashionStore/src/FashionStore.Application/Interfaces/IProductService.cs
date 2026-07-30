using FashionStore.Application.DTOs.Products;

namespace FashionStore.Application.Interfaces;

public interface IProductService
{
    Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductSearchResult> SearchAsync(ProductSearchRequest request, CancellationToken cancellationToken = default);
    Task<IEnumerable<ProductListDto>> GetFeaturedProductsAsync(int count = 10, CancellationToken cancellationToken = default);
    Task<IEnumerable<ProductListDto>> GetNewArrivalsAsync(int count = 10, CancellationToken cancellationToken = default);
    Task<IEnumerable<ProductListDto>> GetBestSellersAsync(int count = 10, CancellationToken cancellationToken = default);
    Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default);
    Task<ProductDto?> UpdateAsync(UpdateProductRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductDto> DuplicateAsync(DuplicateProductRequest request, CancellationToken cancellationToken = default);
    Task<bool> PublishAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ArchiveAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> IsSlugUniqueAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
