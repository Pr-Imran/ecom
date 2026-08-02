namespace FashionStore.Application.Interfaces;

public interface IImageProcessingService
{
    Task ProcessDerivativesAsync(string originalRelativePath, CancellationToken cancellationToken = default);

    Task DeleteDerivativesAsync(string originalRelativePath, CancellationToken cancellationToken = default);
}
