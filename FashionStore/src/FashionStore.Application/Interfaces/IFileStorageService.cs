using FashionStore.Application.DTOs.Images;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Abstraction over physical file storage so that controllers and application
/// services never depend on concrete file paths.
///
/// A cloud implementation (Azure Blob, S3, GCS) implements this same contract;
/// only the registered provider changes, no callers do.
/// </summary>
public interface IFileStorageService
{
    string ProviderName { get; }

    Task<StoredFileResult> SaveAsync(
        string relativePath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string relativePath, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    string ResolveUrl(string relativePath);
}
