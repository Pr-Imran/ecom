using System.Text.Json;
using FashionStore.Application.Common;
using FashionStore.Application.Common.Models;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Seo;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Resolves and manages permanent slug redirects. The full redirect table is
/// cached in the distributed cache and read on every unmatched public slug so a
/// renamed slug issues a 301 instead of a 404. Any create/update/delete
/// invalidates the cached table.
/// </summary>
public sealed class SlugRedirectService : ISlugRedirectService
{
    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly CacheSettings _cacheSettings;
    private readonly ILogger<SlugRedirectService> _logger;

    public SlugRedirectService(
        AppDbContext context,
        IDistributedCache cache,
        CacheSettings cacheSettings,
        ILogger<SlugRedirectService> logger)
    {
        _context = context;
        _cache = cache;
        _cacheSettings = cacheSettings;
        _logger = logger;
    }

    public async Task<string?> ResolveAsync(SlugEntityType entityType, string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        try
        {
            var redirects = await GetCachedAsync(cancellationToken);
            var match = redirects.FirstOrDefault(r => r.EntityType == entityType &&
                string.Equals(r.OldSlug, slug, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(match?.NewSlug) ? null : match!.NewSlug;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve slug redirect for {EntityType} '{Slug}'", entityType, slug);
            return null;
        }
    }

    public async Task<SlugRedirectDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.SlugRedirects.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<IReadOnlyList<SlugRedirectDto>> GetAllAsync(CancellationToken cancellationToken = default)
        => await GetCachedAsync(cancellationToken);

    public async Task<Result<Guid>> AddOrUpdateAsync(SlugRedirectRequest request, CancellationToken cancellationToken = default)
    {
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return Result<Guid>.Failure("Redirect.Validation", validationError);
        }

        var entityType = request.EntityType;
        var oldSlug = request.OldSlug.Trim();
        var newSlug = request.NewSlug.Trim();

        var existing = await _context.SlugRedirects
            .FirstOrDefaultAsync(r => r.EntityType == entityType &&
                string.Equals(r.OldSlug, oldSlug, StringComparison.OrdinalIgnoreCase), cancellationToken);

        var now = DateTime.UtcNow;
        if (existing is not null)
        {
            existing.NewSlug = newSlug;
            existing.UpdatedAtUtc = now;
        }
        else
        {
            _context.SlugRedirects.Add(new SlugRedirect
            {
                EntityType = entityType,
                OldSlug = oldSlug,
                NewSlug = newSlug,
                CreatedAtUtc = now
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.SlugRedirects, cancellationToken);

        return Result<Guid>.Success(existing?.Id ?? _context.SlugRedirects.Local.Last().Id);
    }

    public async Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.SlugRedirects.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (entity is null)
        {
            return Result.Failure("Redirect.NotFound", "Redirect not found.");
        }

        _context.SlugRedirects.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.SlugRedirects, cancellationToken);

        return Result.Success();
    }

    private async Task<IReadOnlyList<SlugRedirectDto>> GetCachedAsync(CancellationToken cancellationToken)
    {
        var cached = await _cache.GetStringAsync(CacheKeys.SlugRedirects, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            return JsonSerializer.Deserialize<List<SlugRedirectDto>>(cached) ?? [];
        }

        var items = await _context.SlugRedirects.AsNoTracking()
            .OrderBy(r => r.EntityType)
            .ThenBy(r => r.OldSlug)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(ToDto).ToList();
        await _cache.SetStringAsync(CacheKeys.SlugRedirects, JsonSerializer.Serialize(dtos), GetCacheOptions(), cancellationToken);
        return dtos;
    }

    private DistributedCacheEntryOptions GetCacheOptions() => new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheSettings.AbsoluteExpirationMinutes),
        SlidingExpiration = TimeSpan.FromMinutes(_cacheSettings.SlidingExpirationMinutes)
    };

    private static SlugRedirectDto ToDto(SlugRedirect entity) => new(
        entity.Id,
        entity.EntityType,
        entity.OldSlug,
        entity.NewSlug,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc);

    private static string? Validate(SlugRedirectRequest request)
    {
        if (request is null)
        {
            return "No redirect supplied.";
        }

        if (string.IsNullOrWhiteSpace(request.OldSlug))
        {
            return "The old slug is required.";
        }

        if (request.OldSlug.Trim().Length > 200)
        {
            return "The old slug must be 200 characters or fewer.";
        }

        if (request.OldSlug.Trim().Contains('/') || request.OldSlug.Trim().Contains(' ') || request.OldSlug.Trim().Contains('\\'))
        {
            return "The old slug must be a plain URL slug without slashes or spaces.";
        }

        if (string.IsNullOrWhiteSpace(request.NewSlug))
        {
            return "The redirect target slug is required.";
        }

        if (request.NewSlug.Trim().Length > 200)
        {
            return "The redirect target slug must be 200 characters or fewer.";
        }

        if (request.NewSlug.Trim().Contains('/') || request.NewSlug.Trim().Contains(' ') || request.NewSlug.Trim().Contains('\\'))
        {
            return "The redirect target slug must be a plain URL slug without slashes or spaces.";
        }

        return null;
    }
}
