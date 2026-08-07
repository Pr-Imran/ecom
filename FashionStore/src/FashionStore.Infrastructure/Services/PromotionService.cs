using FashionStore.Application.DTOs.Promotions;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Administrative promotion management. Promotions are auto-applied catalog
/// discounts scoped to a product, category, brand or collection with an optional
/// minimum quantity trigger, a priority ordering (lowest value applied first) and
/// a stackable flag that governs whether further promotions may combine on a line.
/// </summary>
public sealed class PromotionService : IPromotionService
{
    private readonly AppDbContext _context;
    private readonly ILogger<PromotionService> _logger;

    public PromotionService(AppDbContext context, ILogger<PromotionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PromotionDto>> GetAllAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Promotions.AsNoTracking();

        if (!includeInactive)
        {
            var now = DateTime.UtcNow;
            query = query.Where(p => p.IsActive &&
                (p.StartAtUtc == null || p.StartAtUtc <= now) &&
                (p.EndAtUtc == null || p.EndAtUtc >= now));
        }

        var promotions = await query
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return promotions.Select(ToDto).ToList();
    }

    public async Task<PromotionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var promotion = await _context.Promotions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return promotion is null ? null : ToDto(promotion);
    }

    public async Task<PromotionDto> CreateAsync(CreatePromotionRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request.Name, request.DiscountValue, request.MinQuantity, request.StartAtUtc, request.EndAtUtc);

        var promotion = new Promotion
        {
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            DiscountType = request.DiscountType,
            DiscountValue = request.DiscountValue,
            MaxDiscountAmount = request.MaxDiscountAmount,
            MinQuantity = request.MinQuantity,
            Priority = request.Priority,
            IsStackable = request.IsStackable,
            IsActive = true,
            StartAtUtc = request.StartAtUtc,
            EndAtUtc = request.EndAtUtc,
            ProductId = request.ProductId,
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            CollectionId = request.CollectionId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.Promotions.Add(promotion);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created promotion {PromotionId} - {Name}", promotion.Id, promotion.Name);
        return ToDto(promotion);
    }

    public async Task<PromotionDto?> UpdateAsync(Guid id, UpdatePromotionRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request.Name, request.DiscountValue, request.MinQuantity, request.StartAtUtc, request.EndAtUtc);

        var promotion = await _context.Promotions.FindAsync(new object[] { id }, cancellationToken);
        if (promotion is null)
        {
            return null;
        }

        promotion.Name = request.Name.Trim();
        promotion.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        promotion.DiscountType = request.DiscountType;
        promotion.DiscountValue = request.DiscountValue;
        promotion.MaxDiscountAmount = request.MaxDiscountAmount;
        promotion.MinQuantity = request.MinQuantity;
        promotion.Priority = request.Priority;
        promotion.IsStackable = request.IsStackable;
        promotion.StartAtUtc = request.StartAtUtc;
        promotion.EndAtUtc = request.EndAtUtc;
        promotion.ProductId = request.ProductId;
        promotion.CategoryId = request.CategoryId;
        promotion.BrandId = request.BrandId;
        promotion.CollectionId = request.CollectionId;
        promotion.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated promotion {PromotionId}", promotion.Id);
        return ToDto(promotion);
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var promotion = await _context.Promotions.FindAsync(new object[] { id }, cancellationToken);
        if (promotion is null)
        {
            return false;
        }

        promotion.IsActive = isActive;
        promotion.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("{(State)} promotion {PromotionId}", isActive ? "Activated" : "Deactivated", promotion.Id);
        return true;
    }

    public async Task<PromotionDto?> DuplicateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = await _context.Promotions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (source is null)
        {
            return null;
        }

        var copy = new Promotion
        {
            Name = $"{source.Name} (Copy)",
            Description = source.Description,
            DiscountType = source.DiscountType,
            DiscountValue = source.DiscountValue,
            MaxDiscountAmount = source.MaxDiscountAmount,
            MinQuantity = source.MinQuantity,
            Priority = source.Priority,
            IsStackable = source.IsStackable,
            IsActive = false,
            StartAtUtc = source.StartAtUtc,
            EndAtUtc = source.EndAtUtc,
            ProductId = source.ProductId,
            CategoryId = source.CategoryId,
            BrandId = source.BrandId,
            CollectionId = source.CollectionId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.Promotions.Add(copy);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Duplicated promotion {SourceId} into {PromotionId}", source.Id, copy.Id);
        return ToDto(copy);
    }

    private static PromotionDto ToDto(Promotion p)
    {
        return new PromotionDto(
            p.Id,
            p.Name,
            p.Description,
            p.DiscountType,
            p.DiscountValue,
            p.MaxDiscountAmount,
            p.MinQuantity,
            p.Priority,
            p.IsStackable,
            p.IsActive,
            p.StartAtUtc,
            p.EndAtUtc,
            p.ProductId,
            p.CategoryId,
            p.BrandId,
            p.CollectionId,
            p.CreatedAtUtc);
    }

    private static void ValidateRequest(
        string name,
        decimal discountValue,
        int minQuantity,
        DateTime? startAtUtc,
        DateTime? endAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Promotion name is required.");
        }

        if (discountValue <= 0)
        {
            throw new InvalidOperationException("Discount value must be greater than zero.");
        }

        if (minQuantity < 1)
        {
            throw new InvalidOperationException("Minimum quantity must be at least 1.");
        }

        if (startAtUtc.HasValue && endAtUtc.HasValue && endAtUtc.Value < startAtUtc.Value)
        {
            throw new InvalidOperationException("End date must be after the start date.");
        }
    }
}
