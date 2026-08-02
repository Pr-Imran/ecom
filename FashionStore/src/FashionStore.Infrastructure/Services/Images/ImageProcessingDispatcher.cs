using System.Threading.Channels;
using FashionStore.Application.Interfaces;

namespace FashionStore.Infrastructure.Services.Images;

public sealed class ImageProcessingDispatcher : IImageProcessingDispatcher
{
    private readonly Channel<ImageProcessingJob> _channel = Channel.CreateUnbounded<ImageProcessingJob>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

    public ValueTask EnqueueAsync(ImageProcessingJob job, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(job, cancellationToken);

    public ValueTask<ImageProcessingJob> DequeueAsync(CancellationToken cancellationToken = default)
        => _channel.Reader.ReadAsync(cancellationToken);
}
