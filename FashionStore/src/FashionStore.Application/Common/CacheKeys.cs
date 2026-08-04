namespace FashionStore.Application.Common;

/// <summary>
/// Central registry of distributed cache keys used across services so cache
/// keys stay consistent and cache invalidation remains reliable.
/// </summary>
public static class CacheKeys
{
    public const string HomePage = "homepage:data";
}
