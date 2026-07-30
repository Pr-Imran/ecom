namespace FashionStore.Application.Configuration;

public sealed class CacheSettings
{
    public const string SectionName = "Cache";
    public int AbsoluteExpirationMinutes { get; init; } = 30;
    public int SlidingExpirationMinutes { get; init; } = 10;
}
