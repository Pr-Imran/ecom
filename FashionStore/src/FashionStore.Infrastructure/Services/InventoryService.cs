using System.Globalization;
using System.Text;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Inventory;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

public sealed class InventoryService : IInventoryService
{
    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly ILogger<InventoryService> _logger;
    private readonly InventorySettings _inventorySettings;

    public InventoryService(
        AppDbContext context,
        IDistributedCache cache,
        ILogger<InventoryService> logger,
        InventorySettings inventorySettings)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
        _inventorySettings = inventorySettings;
    }

    public async Task<IEnumerable<WarehouseDto>> GetWarehousesAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Warehouses.AsNoTracking();
        if (!includeInactive)
            query = query.Where(w => w.IsActive);

        var warehouses = await query
            .OrderBy(w => w.IsDefault ? 0 : 1)
            .ThenBy(w => w.DisplayOrder)
            .ThenBy(w => w.Name)
            .ToListAsync(cancellationToken);

        return warehouses.Select(ToDto);
    }

    public async Task<WarehouseDto?> GetWarehouseByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var warehouse = await _context.Warehouses.FindAsync(new object[] { id }, cancellationToken);
        return warehouse != null ? ToDto(warehouse) : null;
    }

    public async Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _context.Warehouses.AnyAsync(w => w.Code == code, cancellationToken))
            throw new InvalidOperationException($"Warehouse with code '{code}' already exists");

        if (request.IsDefault)
            await ClearDefaultWarehouseAsync(cancellationToken);

        var warehouse = new Warehouse
        {
            Name = request.Name.Trim(),
            Code = code,
            Description = request.Description,
            Address = request.Address,
            City = request.City,
            Country = request.Country,
            IsActive = request.IsActive,
            IsDefault = request.IsDefault,
            DisplayOrder = request.DisplayOrder,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created warehouse {WarehouseId} - {Code}", warehouse.Id, warehouse.Code);
        return ToDto(warehouse);
    }

    public async Task<WarehouseDto?> UpdateWarehouseAsync(UpdateWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        var warehouse = await _context.Warehouses.FindAsync(new object[] { request.Id }, cancellationToken);
        if (warehouse == null) return null;

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _context.Warehouses.AnyAsync(w => w.Code == code && w.Id != request.Id, cancellationToken))
            throw new InvalidOperationException($"Warehouse with code '{code}' already exists");

        if (request.IsDefault && !warehouse.IsDefault)
            await ClearDefaultWarehouseAsync(cancellationToken);

        warehouse.Name = request.Name.Trim();
        warehouse.Code = code;
        warehouse.Description = request.Description;
        warehouse.Address = request.Address;
        warehouse.City = request.City;
        warehouse.Country = request.Country;
        warehouse.IsActive = request.IsActive;
        warehouse.IsDefault = request.IsDefault;
        warehouse.DisplayOrder = request.DisplayOrder;
        warehouse.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated warehouse {WarehouseId}", request.Id);
        return ToDto(warehouse);
    }

    public async Task<bool> DeleteWarehouseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var warehouse = await _context.Warehouses
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (warehouse == null) return false;

        var hasStock = await _context.WarehouseStocks.AnyAsync(s => s.WarehouseId == id, cancellationToken);
        if (hasStock)
            throw new InvalidOperationException("Cannot delete a warehouse that still holds stock. Deactivate it instead.");

        var hasTransactions = await _context.InventoryTransactions.AnyAsync(t => t.WarehouseId == id, cancellationToken);
        if (hasTransactions)
            throw new InvalidOperationException("Cannot delete a warehouse with inventory history. Deactivate it instead.");

        var hasReservations = await _context.StockReservations.AnyAsync(r => r.WarehouseId == id && r.Status == StockReservationStatus.Active, cancellationToken);
        if (hasReservations)
            throw new InvalidOperationException("Cannot delete a warehouse with active stock reservations. Deactivate it instead.");

        _context.Warehouses.Remove(warehouse);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted warehouse {WarehouseId}", id);
        return true;
    }

    public async Task<Guid?> GetDefaultWarehouseIdAsync(CancellationToken cancellationToken = default)
    {
        var defaultId = await _context.Warehouses
            .AsNoTracking()
            .Where(w => w.IsActive && w.IsDefault)
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultId.HasValue)
            return defaultId;

        return await _context.Warehouses
            .AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.DisplayOrder)
            .ThenBy(w => w.Name)
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<InventorySearchResult> SearchInventoryAsync(InventorySearchRequest request, CancellationToken cancellationToken = default)
    {
#pragma warning disable CA1862
        var variantQuery = _context.ProductVariants.AsNoTracking();

        if (request.WarehouseId.HasValue)
            variantQuery = variantQuery.Where(v => _context.WarehouseStocks.Any(s => s.ProductVariantId == v.Id && s.WarehouseId == request.WarehouseId.Value));

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.ToLowerInvariant();
            variantQuery = variantQuery.Where(v =>
                v.Sku.ToLowerInvariant().Contains(term) ||
                (v.Product != null && (v.Product.Name.ToLowerInvariant().Contains(term) || v.Product.BaseSku.ToLowerInvariant().Contains(term))) ||
                v.VariantAttributeValues.Any(vav => vav.AttributeValue != null && vav.AttributeValue.Name.ToLowerInvariant().Contains(term)));
        }
#pragma warning restore CA1862

        var variants = await variantQuery
            .Include(v => v.Product)
            .Include(v => v.VariantAttributeValues).ThenInclude(vav => vav.AttributeValue).ThenInclude(av => av!.ProductAttribute)
            .ToListAsync(cancellationToken);

        var variantIds = variants.Select(v => v.Id).ToList();

        var stocks = await _context.WarehouseStocks
            .AsNoTracking()
            .Include(s => s.Warehouse)
            .Where(s => variantIds.Contains(s.ProductVariantId))
            .ToListAsync(cancellationToken);

        var rows = new List<InventoryRowDto>();
        foreach (var variant in variants)
        {
            var variantStocks = stocks.Where(s => s.ProductVariantId == variant.Id).ToList();
            var onHand = variantStocks.Sum(s => s.OnHandQuantity);
            var reserved = variantStocks.Sum(s => s.ReservedQuantity);
            var available = onHand - reserved;
            var minThreshold = variantStocks
                .Where(s => s.LowStockThreshold.HasValue)
                .Select(s => s.LowStockThreshold)
                .Min();
            var anyBackorder = variantStocks.Any(s => s.AllowBackorder);

            rows.Add(new InventoryRowDto(
                variant.Id,
                variant.Sku,
                variant.Product?.Name ?? "Unknown",
                variant.Product?.Slug,
                variant.Price,
                null,
                BuildAttributeValues(variant),
                variantStocks.Count,
                onHand,
                reserved,
                available,
                minThreshold,
                anyBackorder,
                variantStocks.Count > 0
                    ? variantStocks.Max(s => s.UpdatedAtUtc) ?? variant.CreatedAtUtc
                    : variant.CreatedAtUtc));
        }

        if (request.LowStockOnly == true)
            rows = rows.Where(r => r.LowStockThreshold.HasValue && r.TotalAvailable <= r.LowStockThreshold.Value).ToList();

        if (request.OutOfStockOnly == true)
            rows = rows.Where(r => r.TotalAvailable <= 0).ToList();

        if (request.BackorderOnly == true)
            rows = rows.Where(r => r.AllowBackorder).ToList();

        rows = request.SortBy?.ToLowerInvariant() switch
        {
            "sku" => request.SortDescending ? rows.OrderByDescending(r => r.Sku).ToList() : rows.OrderBy(r => r.Sku).ToList(),
            "name" => request.SortDescending ? rows.OrderByDescending(r => r.ProductName).ToList() : rows.OrderBy(r => r.ProductName).ToList(),
            "price" => request.SortDescending ? rows.OrderByDescending(r => r.Price).ToList() : rows.OrderBy(r => r.Price).ToList(),
            "available" => request.SortDescending ? rows.OrderByDescending(r => r.TotalAvailable).ToList() : rows.OrderBy(r => r.TotalAvailable).ToList(),
            "onhand" => request.SortDescending ? rows.OrderByDescending(r => r.TotalOnHand).ToList() : rows.OrderBy(r => r.TotalOnHand).ToList(),
            "updated" => request.SortDescending ? rows.OrderByDescending(r => r.UpdatedAtUtc).ToList() : rows.OrderBy(r => r.UpdatedAtUtc).ToList(),
            _ => rows.OrderByDescending(r => r.UpdatedAtUtc).ToList()
        };

        var totalCount = rows.Count;
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var items = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return new InventorySearchResult(items, totalCount, page, pageSize, totalPages);
    }

    public async Task<VariantInventoryDetailDto?> GetVariantInventoryAsync(Guid variantId, CancellationToken cancellationToken = default)
    {
        var variant = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Include(v => v.VariantAttributeValues).ThenInclude(vav => vav.AttributeValue).ThenInclude(av => av!.ProductAttribute)
            .FirstOrDefaultAsync(v => v.Id == variantId, cancellationToken);

        if (variant == null) return null;

        var stocks = await _context.WarehouseStocks
            .AsNoTracking()
            .Include(s => s.Warehouse)
            .Where(s => s.ProductVariantId == variantId)
            .ToListAsync(cancellationToken);

        var warehouseDtos = stocks.Select(ToDto).ToList();
        var updatedAt = stocks.Count > 0 ? stocks.Max(s => s.UpdatedAtUtc) ?? variant.CreatedAtUtc : variant.CreatedAtUtc;

        return new VariantInventoryDetailDto(
            variant.Id,
            variant.Sku,
            variant.Product?.Name ?? "Unknown",
            variant.Price,
            BuildAttributeValues(variant),
            warehouseDtos,
            warehouseDtos.Sum(w => w.OnHandQuantity),
            warehouseDtos.Sum(w => w.ReservedQuantity),
            warehouseDtos.Sum(w => w.AvailableQuantity),
            warehouseDtos.Any(w => w.AllowBackorder),
            updatedAt);
    }

    public async Task<InventorySummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var stocks = await _context.WarehouseStocks.AsNoTracking().ToListAsync(cancellationToken);

        var totalOnHand = stocks.Sum(s => s.OnHandQuantity);
        var totalReserved = stocks.Sum(s => s.ReservedQuantity);
        var totalAvailable = stocks.Sum(s => s.AvailableQuantity);

        var lowStockCount = stocks.Count(s => s.LowStockThreshold.HasValue && s.AvailableQuantity <= s.LowStockThreshold.Value);
        var outOfStockCount = stocks.Count(s => s.AvailableQuantity <= 0);
        var backorderCount = stocks.Count(s => s.AllowBackorder);

        var totalVariants = await _context.ProductVariants.CountAsync(cancellationToken);
        var activeReservations = await _context.StockReservations
            .CountAsync(r => r.Status == StockReservationStatus.Active, cancellationToken);

        return new InventorySummaryDto(
            totalVariants,
            lowStockCount,
            outOfStockCount,
            backorderCount,
            totalOnHand,
            totalReserved,
            totalAvailable,
            activeReservations);
    }

    public async Task<IEnumerable<InventoryTransactionDto>> GetTransactionHistoryAsync(
        Guid variantId,
        Guid? warehouseId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        var query = _context.InventoryTransactions
            .AsNoTracking()
            .Include(t => t.Warehouse)
            .Include(t => t.Variant)
            .Where(t => t.ProductVariantId == variantId);

        if (warehouseId.HasValue)
            query = query.Where(t => t.WarehouseId == warehouseId.Value);

        var transactions = await query
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return transactions.Select(ToTransactionDto);
    }

    public async Task<WarehouseStockDto> AdjustStockAsync(AdjustStockRequest request, CancellationToken cancellationToken = default)
    {
        if (request.AdjustmentQuantity == 0)
            throw new InvalidOperationException("Stock adjustment quantity cannot be zero");

        var variant = await _context.ProductVariants.FindAsync(new object[] { request.VariantId }, cancellationToken);
        if (variant == null)
            throw new InvalidOperationException($"Variant {request.VariantId} not found");

        var warehouseId = await ResolveWarehouseIdAsync(request.WarehouseId, cancellationToken);

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;

            await ApplyStockChangeAsync(
                request.VariantId,
                warehouseId,
                request.AdjustmentQuantity,
                0,
                request.Reason,
                request.ReferenceType,
                request.ReferenceId,
                request.Notes,
                request.AdministratorId,
                cancellationToken);

            if (tx != null)
                await tx.CommitAsync(cancellationToken);
        });

        _logger.LogInformation(
            "Adjusted stock of variant {VariantId} by {Quantity} ({Reason}) in warehouse {WarehouseId}",
            request.VariantId, request.AdjustmentQuantity, request.Reason, warehouseId);

        await InvalidateVariantCacheAsync(variant.ProductId, cancellationToken);
        return await GetStockDtoAsync(request.VariantId, warehouseId, cancellationToken);
    }

    public async Task<int> BulkAdjustStockAsync(BulkAdjustStockRequest request, CancellationToken cancellationToken = default)
    {
        if (request.AdjustmentQuantity == 0)
            return 0;

        var warehouseId = await ResolveWarehouseIdAsync(request.WarehouseId, cancellationToken);
        var applied = 0;

        foreach (var variantId in request.VariantIds)
        {
            try
            {
                await ApplyStockChangeAsync(
                    variantId,
                    warehouseId,
                    request.AdjustmentQuantity,
                    0,
                    request.Reason,
                    InventoryReferenceType.Adjustment,
                    null,
                    request.Notes,
                    request.AdministratorId,
                    cancellationToken);
                applied++;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Bulk adjustment skipped variant {VariantId}: {Message}", variantId, ex.Message);
            }
        }

        _logger.LogInformation("Bulk adjusted stock for {Applied} of {Total} variants", applied, request.VariantIds.Count);
        return applied;
    }

    public async Task<WarehouseStockDto> SetStockThresholdsAsync(SetStockThresholdsRequest request, CancellationToken cancellationToken = default)
    {
        if (request.LowStockThreshold.HasValue && request.LowStockThreshold.Value < 0)
            throw new InvalidOperationException("Low stock threshold cannot be negative");

        if (request.ReorderLevel.HasValue && request.ReorderLevel.Value < 0)
            throw new InvalidOperationException("Reorder level cannot be negative");

        var warehouseId = await ResolveWarehouseIdAsync(request.WarehouseId, cancellationToken);

        var stock = await GetOrCreateStockAsync(request.VariantId, warehouseId, cancellationToken);
        stock.LowStockThreshold = request.LowStockThreshold;
        stock.ReorderLevel = request.ReorderLevel;
        if (request.AllowBackorder.HasValue)
            stock.AllowBackorder = request.AllowBackorder.Value;
        stock.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated stock thresholds for variant {VariantId} in warehouse {WarehouseId}", request.VariantId, warehouseId);
        return await GetStockDtoAsync(request.VariantId, warehouseId, cancellationToken);
    }

    public async Task<StockReservationDto> ReserveStockAsync(CreateStockReservationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
            throw new InvalidOperationException("Reservation quantity must be greater than zero");

        var variant = await _context.ProductVariants.FindAsync(new object[] { request.VariantId }, cancellationToken);
        if (variant == null)
            throw new InvalidOperationException($"Variant {request.VariantId} not found");

        var warehouseId = await ResolveWarehouseIdAsync(request.WarehouseId, cancellationToken);

        var reservation = new StockReservation
        {
            ProductVariantId = request.VariantId,
            WarehouseId = warehouseId,
            Quantity = request.Quantity,
            CartReference = request.CartReference,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(request.ExpirationMinutes > 0
                ? request.ExpirationMinutes
                : _inventorySettings.DefaultReservationExpirationMinutes),
            Status = StockReservationStatus.Active,
            ReferenceId = request.ReferenceId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.StockReservations.Add(reservation);

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;

            await ApplyStockChangeAsync(
                request.VariantId,
                warehouseId,
                0,
                request.Quantity,
                StockAdjustmentReason.OrderReservation,
                request.ReferenceType,
                request.ReferenceId,
                $"Reservation {reservation.Id} - {request.CartReference}",
                null,
                cancellationToken);

            if (tx != null)
                await tx.CommitAsync(cancellationToken);
        });

        _logger.LogInformation(
            "Reserved {Quantity} units of variant {VariantId} for cart {CartReference} in warehouse {WarehouseId}",
            request.Quantity, request.VariantId, request.CartReference, warehouseId);

        await InvalidateVariantCacheAsync(variant.ProductId, cancellationToken);
        return await ToReservationDtoAsync(reservation, cancellationToken);
    }

    public async Task<bool> ReleaseReservationAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        var reservation = await _context.StockReservations.FindAsync(new object[] { reservationId }, cancellationToken);
        if (reservation == null) return false;

        if (reservation.Status != StockReservationStatus.Active)
            return true;

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;

            reservation.Status = StockReservationStatus.Released;
            reservation.ReleasedAtUtc = DateTime.UtcNow;

            if (reservation.WarehouseId.HasValue)
            {
                await ApplyStockChangeAsync(
                    reservation.ProductVariantId,
                    reservation.WarehouseId.Value,
                    0,
                    -reservation.Quantity,
                    StockAdjustmentReason.ReservationRelease,
                    InventoryReferenceType.Cart,
                    reservation.ReferenceId ?? reservation.Id.ToString(),
                    $"Release reservation {reservation.Id}",
                    null,
                    cancellationToken);
            }

            if (tx != null)
                await tx.CommitAsync(cancellationToken);
        });

        var variant = await _context.ProductVariants.FindAsync(new object[] { reservation.ProductVariantId }, cancellationToken);
        if (variant != null)
            await InvalidateVariantCacheAsync(variant.ProductId, cancellationToken);

        return true;
    }

    public async Task<int> ReleaseExpiredReservationsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expired = await _context.StockReservations
            .Include(r => r.Variant)
            .Where(r => r.Status == StockReservationStatus.Active && r.ExpiresAtUtc < now)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
            return 0;

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;

            foreach (var reservation in expired)
            {
                reservation.Status = StockReservationStatus.Expired;
                reservation.ReleasedAtUtc = now;

                if (reservation.WarehouseId.HasValue)
                {
                    await ApplyStockChangeAsync(
                        reservation.ProductVariantId,
                        reservation.WarehouseId.Value,
                        0,
                        -reservation.Quantity,
                        StockAdjustmentReason.ReservationRelease,
                        InventoryReferenceType.Cart,
                        reservation.ReferenceId ?? reservation.Id.ToString(),
                        $"Auto-release expired reservation {reservation.Id}",
                        null,
                        cancellationToken);
                }
            }

            if (tx != null)
                await tx.CommitAsync(cancellationToken);
        });

        foreach (var reservation in expired.Where(r => r.Variant != null))
        {
            await InvalidateVariantCacheAsync(reservation.Variant!.ProductId, cancellationToken);
        }

        _logger.LogInformation("Released {Count} expired stock reservations", expired.Count);
        return expired.Count;
    }

    public async Task<string> ExportInventoryCsvAsync(InventorySearchRequest request, CancellationToken cancellationToken = default)
    {
        var exportRequest = new InventorySearchRequest(
            request.SearchTerm,
            request.LowStockOnly,
            request.OutOfStockOnly,
            request.BackorderOnly,
            request.WarehouseId,
            request.SortBy,
            request.SortDescending,
            1,
            10000);

        var result = await SearchInventoryAsync(exportRequest, cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine("SKU,Product,Colour,Size,OnHand,Reserved,Available,LowStockThreshold,AllowBackorder,UpdatedAtUtc");

        foreach (var item in result.Items)
        {
            item.AttributeValues.TryGetValue("Colour", out var colour);
            item.AttributeValues.TryGetValue("Size", out var size);

            builder.AppendLine(string.Join(",",
                EscapeCsv(item.Sku),
                EscapeCsv(item.ProductName),
                EscapeCsv(colour ?? string.Empty),
                EscapeCsv(size ?? string.Empty),
                item.TotalOnHand.ToString(CultureInfo.InvariantCulture),
                item.TotalReserved.ToString(CultureInfo.InvariantCulture),
                item.TotalAvailable.ToString(CultureInfo.InvariantCulture),
                item.LowStockThreshold?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                item.AllowBackorder ? "true" : "false",
                item.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)));
        }

        return builder.ToString();
    }

    private async Task<WarehouseStock> ApplyStockChangeAsync(
        Guid variantId,
        Guid warehouseId,
        int onHandDelta,
        int reservedDelta,
        StockAdjustmentReason reason,
        InventoryReferenceType referenceType,
        string? referenceId,
        string? notes,
        string? administratorId,
        CancellationToken cancellationToken)
    {
        var stock = await GetOrCreateStockAsync(variantId, warehouseId, cancellationToken);

        var newOnHand = stock.OnHandQuantity + onHandDelta;
        var newReserved = stock.ReservedQuantity + reservedDelta;

        if (newOnHand < 0)
            throw new InvalidOperationException("Stock cannot be reduced below zero for this variant.");

        if (newReserved < 0)
            throw new InvalidOperationException("Reserved stock cannot be reduced below zero.");

        if (reservedDelta > 0 && !stock.AllowBackorder && newReserved > newOnHand)
            throw new InvalidOperationException("Insufficient available stock for this reservation.");

        if (onHandDelta < 0 && newOnHand < stock.ReservedQuantity && !stock.AllowBackorder)
            throw new InvalidOperationException("Stock cannot be reduced below the quantity currently reserved for this variant.");

        var previousOnHand = stock.OnHandQuantity;
        var previousReserved = stock.ReservedQuantity;

        stock.OnHandQuantity = newOnHand;
        stock.ReservedQuantity = newReserved;
        stock.UpdatedAtUtc = DateTime.UtcNow;

        _context.InventoryTransactions.Add(new InventoryTransaction
        {
            WarehouseId = warehouseId,
            ProductVariantId = variantId,
            QuantityChange = onHandDelta,
            PreviousOnHand = previousOnHand,
            NewOnHand = newOnHand,
            ReservedQuantityChange = reservedDelta,
            PreviousReserved = previousReserved,
            NewReserved = newReserved,
            Reason = reason,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Notes = notes,
            AdministratorId = administratorId,
            CreatedAtUtc = DateTime.UtcNow
        });

        await SyncVariantTotalsAsync(variantId, cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new InvalidOperationException(
                "Stock was modified by another operation. Please review the current quantity and retry.",
                ex);
        }

        return stock;
    }

    private async Task<WarehouseStock> GetOrCreateStockAsync(Guid variantId, Guid warehouseId, CancellationToken cancellationToken)
    {
        var stock = await _context.WarehouseStocks
            .FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.ProductVariantId == variantId, cancellationToken);

        if (stock != null)
            return stock;

        stock = new WarehouseStock
        {
            WarehouseId = warehouseId,
            ProductVariantId = variantId,
            OnHandQuantity = 0,
            ReservedQuantity = 0,
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.WarehouseStocks.Add(stock);
        return stock;
    }

    private async Task SyncVariantTotalsAsync(Guid variantId, CancellationToken cancellationToken)
    {
        var variant = await _context.ProductVariants.FindAsync(new object[] { variantId }, cancellationToken);
        if (variant == null)
            return;

        var stocks = await _context.WarehouseStocks
            .Where(s => s.ProductVariantId == variantId)
            .ToListAsync(cancellationToken);

        var presentIds = stocks.Select(s => s.Id).ToHashSet();
        foreach (var local in _context.WarehouseStocks.Local
            .Where(s => s.ProductVariantId == variantId
                && _context.Entry(s).State == EntityState.Added
                && !presentIds.Contains(s.Id)))
        {
            stocks.Add(local);
        }

        variant.StockQuantity = stocks.Sum(s => s.OnHandQuantity);
        variant.ReservedStock = stocks.Sum(s => s.ReservedQuantity);
    }

    private async Task<Guid> ResolveWarehouseIdAsync(Guid? requestedWarehouseId, CancellationToken cancellationToken)
    {
        var warehouseId = requestedWarehouseId ?? await GetDefaultWarehouseIdAsync(cancellationToken);
        if (!warehouseId.HasValue)
            throw new InvalidOperationException("No active warehouse is configured. Create a warehouse before managing stock.");

        var active = await _context.Warehouses
            .AnyAsync(w => w.Id == warehouseId.Value && w.IsActive, cancellationToken);
        if (!active)
            throw new InvalidOperationException($"Active warehouse {warehouseId.Value} not found.");

        return warehouseId.Value;
    }

    private async Task ClearDefaultWarehouseAsync(CancellationToken cancellationToken)
    {
        var warehouses = await _context.Warehouses.Where(w => w.IsDefault).ToListAsync(cancellationToken);
        foreach (var warehouse in warehouses)
        {
            warehouse.IsDefault = false;
        }
    }

    private async Task InvalidateVariantCacheAsync(Guid productId, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.RemoveAsync($"product:{productId}:variations", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate variation cache for product {ProductId}", productId);
        }
    }

    private async Task<WarehouseStockDto> GetStockDtoAsync(Guid variantId, Guid warehouseId, CancellationToken cancellationToken)
    {
        var stock = await _context.WarehouseStocks
            .AsNoTracking()
            .Include(s => s.Warehouse)
            .FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.ProductVariantId == variantId, cancellationToken);

        if (stock == null || stock.Warehouse == null)
            throw new InvalidOperationException("Stock record not found after the operation.");

        return ToDto(stock);
    }

    private async Task<StockReservationDto> ToReservationDtoAsync(StockReservation reservation, CancellationToken cancellationToken)
    {
        var sku = await _context.ProductVariants
            .AsNoTracking()
            .Where(v => v.Id == reservation.ProductVariantId)
            .Select(v => v.Sku)
            .FirstOrDefaultAsync(cancellationToken) ?? reservation.ProductVariantId.ToString();

        return new StockReservationDto(
            reservation.Id,
            reservation.ProductVariantId,
            sku,
            reservation.WarehouseId,
            reservation.Quantity,
            reservation.CartReference,
            reservation.ExpiresAtUtc,
            reservation.Status,
            reservation.CreatedAtUtc,
            reservation.ReleasedAtUtc);
    }

    private static Dictionary<string, string> BuildAttributeValues(ProductVariant variant)
    {
        return variant.VariantAttributeValues
            .Where(vav => vav.AttributeValue != null && vav.AttributeValue.ProductAttribute != null)
            .ToDictionary(
                vav => vav.AttributeValue!.ProductAttribute!.Name,
                vav => vav.AttributeValue!.Name);
    }

    private static WarehouseDto ToDto(Warehouse warehouse) => new(
        warehouse.Id,
        warehouse.Name,
        warehouse.Code,
        warehouse.Description,
        warehouse.Address,
        warehouse.City,
        warehouse.Country,
        warehouse.IsActive,
        warehouse.IsDefault,
        warehouse.DisplayOrder);

    private static WarehouseStockDto ToDto(WarehouseStock stock) => new(
        stock.Id,
        stock.WarehouseId,
        stock.Warehouse?.Name ?? string.Empty,
        stock.ProductVariantId,
        string.Empty,
        stock.OnHandQuantity,
        stock.ReservedQuantity,
        stock.AvailableQuantity,
        stock.LowStockThreshold,
        stock.ReorderLevel,
        stock.AllowBackorder,
        stock.UpdatedAtUtc ?? stock.CreatedAtUtc);

    private static InventoryTransactionDto ToTransactionDto(InventoryTransaction transaction) => new(
        transaction.Id,
        transaction.WarehouseId,
        transaction.Warehouse?.Name ?? string.Empty,
        transaction.ProductVariantId,
        transaction.Variant?.Sku ?? string.Empty,
        transaction.QuantityChange,
        transaction.PreviousOnHand,
        transaction.NewOnHand,
        transaction.ReservedQuantityChange,
        transaction.PreviousReserved,
        transaction.NewReserved,
        transaction.Reason,
        transaction.ReferenceType,
        transaction.ReferenceId,
        transaction.Notes,
        transaction.AdministratorId,
        transaction.CreatedAtUtc);

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuotes)
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
