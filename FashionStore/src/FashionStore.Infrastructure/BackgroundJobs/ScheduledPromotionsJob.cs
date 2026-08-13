using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.BackgroundJobs;

/// <summary>
/// Applies the promotion schedule: activates promotions whose scheduled window has
/// started and deactivates ones whose end date has passed. Runs periodically so
/// the storefront never serves stale or premature promotions.
/// </summary>
public sealed class ScheduledPromotionsJob
{
    private readonly AppDbContext _context;
    private readonly ILogger<ScheduledPromotionsJob> _logger;

    public ScheduledPromotionsJob(AppDbContext context, ILogger<ScheduledPromotionsJob> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var activated = 0;
        var deactivated = 0;

        var dueToStart = await _context.Promotions
            .Where(p => !p.IsActive
                && p.StartAtUtc != null
                && p.StartAtUtc <= now
                && (p.EndAtUtc == null || p.EndAtUtc > now))
            .ToListAsync(cancellationToken);

        foreach (var promotion in dueToStart)
        {
            cancellationToken.ThrowIfCancellationRequested();
            promotion.IsActive = true;
            promotion.UpdatedAtUtc = now;
            activated++;
        }

        var dueToEnd = await _context.Promotions
            .Where(p => p.IsActive && p.EndAtUtc != null && p.EndAtUtc <= now)
            .ToListAsync(cancellationToken);

        foreach (var promotion in dueToEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();
            promotion.IsActive = false;
            promotion.UpdatedAtUtc = now;
            deactivated++;
        }

        if (activated > 0 || deactivated > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        if (activated > 0 || deactivated > 0)
        {
            _logger.LogInformation("Promotion schedule applied: {Activated} activated, {Deactivated} deactivated", activated, deactivated);
        }

        return activated + deactivated;
    }
}
