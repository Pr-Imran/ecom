using FashionStore.Application.DTOs.Images;

namespace FashionStore.Application.Interfaces;

public interface IImageValidationService
{
    Task<ImageValidationResult> ValidateAsync(
        UploadedFileInput file,
        CancellationToken cancellationToken = default);
}
