using FashionStore.Application.Email;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.BackgroundJobs;

/// <summary>
/// Emails administrators a digest of variants that are at or below their low-stock
/// threshold. A variant is flagged when the minimum warehouse threshold configured
/// for it is reached; variants without a threshold are ignored.
/// </summary>
public sealed class LowStockAlertJob
{
    private readonly AppDbContext _context;
    private readonly IEmailNotificationService _emails;
    private readonly ILogger<LowStockAlertJob> _logger;

    public LowStockAlertJob(AppDbContext context, IEmailNotificationService emails, ILogger<LowStockAlertJob> logger)
    {
        _context = context;
        _emails = emails;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .ToListAsync(cancellationToken);

        var stocks = await _context.WarehouseStocks
            .AsNoTracking()
            .Where(s => s.LowStockThreshold.HasValue)
            .ToListAsync(cancellationToken);

        var items = new List<LowStockAlertItem>();

        foreach (var variant in variants)
        {
            var variantStocks = stocks.Where(s => s.ProductVariantId == variant.Id).ToList();
            if (variantStocks.Count == 0)
            {
                continue;
            }

            var onHand = variantStocks.Sum(s => s.OnHandQuantity);
            var reserved = variantStocks.Sum(s => s.ReservedQuantity);
            var available = onHand - reserved;
            var threshold = variantStocks
                .Where(s => s.LowStockThreshold.HasValue)
                .Select(s => s.LowStockThreshold)
                .Min();

            if (threshold.HasValue && available <= threshold.Value)
            {
                items.Add(new LowStockAlertItem
                {
                    ProductName = variant.Product?.Name ?? "Unknown product",
                    Sku = variant.Sku,
                    Variant = variant.Sku,
                    Available = available,
                    Threshold = threshold
                });
            }
        }

        if (items.Count == 0)
        {
            return 0;
        }

        await _emails.SendLowStockAlertAsync(items, cancellationToken);
        _logger.LogInformation("Low-stock alert sent for {Count} variant(s)", items.Count);
        return items.Count;
    }
}
