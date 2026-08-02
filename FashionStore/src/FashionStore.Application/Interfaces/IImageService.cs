using FashionStore.Application.DTOs.Images;

namespace FashionStore.Application.Interfaces;

public interface IImageService
{
    Task<IEnumerable<ProductImageDto>> GetProductImagesAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<IEnumerable<ProductImageDto>> GetVariantImagesAsync(Guid variantId, CancellationToken cancellationToken = default);

    Task<ProductImageDto> UploadProductImageAsync(
        Guid productId,
        UploadedFileInput file,
        ProductImageUploadRequest request,
        CancellationToken cancellationToken = default);

    Task<ProductImageDto> UploadVariantImageAsync(
        Guid variantId,
        UploadedFileInput file,
        ProductImageUploadRequest request,
        CancellationToken cancellationToken = default);

    Task<string> UploadCategoryImageAsync(Guid categoryId, UploadedFileInput file, CancellationToken cancellationToken = default);

    Task<string> UploadBrandImageAsync(Guid brandId, UploadedFileInput file, CancellationToken cancellationToken = default);

    Task<string> UploadCollectionImageAsync(Guid collectionId, UploadedFileInput file, CancellationToken cancellationToken = default);

    Task<ProductImageDto> UpdateAltTextAsync(Guid imageId, string? altText, CancellationToken cancellationToken = default);

    Task<ProductImageDto> UpdateCaptionAsync(Guid imageId, string? caption, CancellationToken cancellationToken = default);

    Task<ProductImageDto> SetMainImageAsync(Guid imageId, CancellationToken cancellationToken = default);

    Task<ProductImageDto> AssignVariantAsync(Guid imageId, Guid? variantId, CancellationToken cancellationToken = default);

    Task ReorderProductImagesAsync(Guid productId, IReadOnlyList<ImageOrderItem> items, CancellationToken cancellationToken = default);

    Task<ProductImageDto> ReplaceImageAsync(Guid imageId, UploadedFileInput file, CancellationToken cancellationToken = default);

    Task<bool> DeleteImageAsync(Guid imageId, CancellationToken cancellationToken = default);

    Task<bool> DeleteCategoryImageAsync(Guid categoryId, CancellationToken cancellationToken = default);

    Task<bool> DeleteBrandImageAsync(Guid brandId, CancellationToken cancellationToken = default);

    Task<bool> DeleteCollectionImageAsync(Guid collectionId, CancellationToken cancellationToken = default);

    Task<int> GetProductImageCountAsync(Guid productId, CancellationToken cancellationToken = default);
}
