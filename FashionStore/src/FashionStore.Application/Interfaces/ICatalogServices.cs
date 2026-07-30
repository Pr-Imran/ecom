using FashionStore.Application.DTOs.Catalog;

namespace FashionStore.Application.Interfaces;

public interface ICategoryService
{
    Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<CategoryDto>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<IEnumerable<CategoryHierarchyDto>> GetHierarchyAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<CategoryDto>> GetMenuCategoriesAsync(CancellationToken cancellationToken = default);
    Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);
    Task<CategoryDto?> UpdateAsync(UpdateCategoryRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> IsSlugUniqueAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<bool> HasCircularReferenceAsync(Guid id, Guid? parentId, CancellationToken cancellationToken = default);
    Task<bool> HasProductsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<CategoryDto>> SearchAsync(string searchTerm, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task ReorderAsync(IEnumerable<(Guid Id, int Order)> items, CancellationToken cancellationToken = default);
}

public interface IBrandService
{
    Task<BrandDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<BrandDto>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<IEnumerable<BrandDto>> GetActiveBrandsAsync(CancellationToken cancellationToken = default);
    Task<BrandDto> CreateAsync(CreateBrandRequest request, CancellationToken cancellationToken = default);
    Task<BrandDto?> UpdateAsync(UpdateBrandRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> IsSlugUniqueAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<bool> HasProductsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<BrandDto>> SearchAsync(string searchTerm, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task ReorderAsync(IEnumerable<(Guid Id, int Order)> items, CancellationToken cancellationToken = default);
}

public interface ICollectionService
{
    Task<CollectionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<CollectionDto>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<IEnumerable<CollectionDto>> GetActiveCollectionsAsync(CancellationToken cancellationToken = default);
    Task<CollectionDto> CreateAsync(CreateCollectionRequest request, CancellationToken cancellationToken = default);
    Task<CollectionDto?> UpdateAsync(UpdateCollectionRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> IsSlugUniqueAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<bool> HasProductsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<CollectionDto>> SearchAsync(string searchTerm, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task ReorderAsync(IEnumerable<(Guid Id, int Order)> items, CancellationToken cancellationToken = default);
}
