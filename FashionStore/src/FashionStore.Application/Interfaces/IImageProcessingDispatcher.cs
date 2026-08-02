namespace FashionStore.Application.Interfaces;

public sealed record ImageProcessingJob(
    Guid ImageId,
    string OriginalRelativePath
);

public interface IImageProcessingDispatcher
{
    ValueTask EnqueueAsync(ImageProcessingJob job, CancellationToken cancellationToken = default);

    ValueTask<ImageProcessingJob> DequeueAsync(CancellationToken cancellationToken = default);
}
