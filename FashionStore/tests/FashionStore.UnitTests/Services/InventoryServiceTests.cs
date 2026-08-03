using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Inventory;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FashionStore.UnitTests.Services;

public class InventoryServiceTests
{
    private readonly IDistributedCache _cache = new MemoryDistributedCache(new OptionsWrapper<MemoryDistributedCacheOptions>(new MemoryDistributedCacheOptions()));

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"fashionstore-inventory-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private InventoryService CreateService(AppDbContext context)
    {
        return new InventoryService(
            context,
            _cache,
            NullLogger<InventoryService>.Instance,
            new InventorySettings { DefaultReservationExpirationMinutes = 30 });
    }

    private static async Task<(Warehouse warehouse, Product product, ProductVariant variant)> SeedAsync(AppDbContext context)
    {
        var category = new Category { Name = "Test Category", Slug = "test-category" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "Test Product",
            Slug = "test-product",
            CategoryId = category.Id,
            BaseSku = "TP-001",
            BasePrice = 49.99m
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            Name = "Main Warehouse",
            Code = "WH-01",
            City = "London",
            Country = "UK",
            IsActive = true,
            IsDefault = true
        };
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = "TP-001-S",
            Price = 49.99m,
            IsActive = true,
            IsDefault = true,
            StockQuantity = 0,
            ReservedStock = 0
        };
        context.ProductVariants.Add(variant);
        await context.SaveChangesAsync();

        return (warehouse, product, variant);
    }

    [Fact]
    public async Task CreateWarehouseAsync_NormalizesCodeAndCreates()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var warehouse = await service.CreateWarehouseAsync(
            new CreateWarehouseRequest("North Hub", "  wh-02 ", null, null, "Leeds", "UK", true, false, 2),
            CancellationToken.None);

        Assert.Equal("WH-02", warehouse.Code);
        Assert.Equal("North Hub", warehouse.Name);
        Assert.False(warehouse.IsDefault);
    }

    [Fact]
    public async Task CreateWarehouseAsync_DuplicateCode_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        await SeedAsync(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateWarehouseAsync(new CreateWarehouseRequest("Duplicate", "wh-01", null, null, null, null, true, false, 0), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteWarehouseAsync_WithStock_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (warehouse, _, variant) = await SeedAsync(context);

        context.WarehouseStocks.Add(new WarehouseStock
        {
            WarehouseId = warehouse.Id,
            ProductVariantId = variant.Id,
            OnHandQuantity = 5
        });
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteWarehouseAsync(warehouse.Id, CancellationToken.None));
        Assert.Contains("stock", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdjustStockAsync_IncreasesStockAndSyncsVariant()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (warehouse, _, variant) = await SeedAsync(context);

        var stock = await service.AdjustStockAsync(
            new AdjustStockRequest(variant.Id, warehouse.Id, 25, StockAdjustmentReason.PurchaseReceipt, "Received order", "admin-1"),
            CancellationToken.None);

        Assert.Equal(25, stock.OnHandQuantity);
        Assert.Equal(25, stock.AvailableQuantity);

        var refreshedVariant = await context.ProductVariants.FindAsync(variant.Id);
        Assert.Equal(25, refreshedVariant!.StockQuantity);
        Assert.Equal(0, refreshedVariant.ReservedStock);

        var transactions = await service.GetTransactionHistoryAsync(variant.Id, warehouse.Id, 10, CancellationToken.None);
        var transaction = Assert.Single(transactions);
        Assert.Equal(25, transaction.QuantityChange);
        Assert.Equal(StockAdjustmentReason.PurchaseReceipt, transaction.Reason);
    }

    [Fact]
    public async Task AdjustStockAsync_DecreaseBelowZero_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (warehouse, _, variant) = await SeedAsync(context);

        await service.AdjustStockAsync(
            new AdjustStockRequest(variant.Id, warehouse.Id, 10, StockAdjustmentReason.ManualIncrease, null, null),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AdjustStockAsync(new AdjustStockRequest(variant.Id, warehouse.Id, -11, StockAdjustmentReason.Correction, null, null), CancellationToken.None));
        Assert.Contains("zero", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReserveStockAsync_ReservesAndReducesAvailable()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (warehouse, _, variant) = await SeedAsync(context);

        await service.AdjustStockAsync(
            new AdjustStockRequest(variant.Id, warehouse.Id, 10, StockAdjustmentReason.ManualIncrease, null, null),
            CancellationToken.None);

        var reservation = await service.ReserveStockAsync(
            new CreateStockReservationRequest(variant.Id, warehouse.Id, 4, "cart-123", 30),
            CancellationToken.None);

        Assert.Equal(StockReservationStatus.Active, reservation.Status);
        Assert.True(reservation.ExpiresAtUtc > DateTime.UtcNow);

        var detail = await service.GetVariantInventoryAsync(variant.Id, CancellationToken.None);
        Assert.Equal(10, detail!.TotalOnHand);
        Assert.Equal(4, detail.TotalReserved);
        Assert.Equal(6, detail.TotalAvailable);
    }

    [Fact]
    public async Task ReserveStockAsync_InsufficientStock_Throws()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (warehouse, _, variant) = await SeedAsync(context);

        await service.AdjustStockAsync(
            new AdjustStockRequest(variant.Id, warehouse.Id, 5, StockAdjustmentReason.ManualIncrease, null, null),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReserveStockAsync(new CreateStockReservationRequest(variant.Id, warehouse.Id, 6, "cart-456", 30), CancellationToken.None));
    }

    [Fact]
    public async Task ReserveStockAsync_AllowBackorder_AllowsOverbooking()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (warehouse, _, variant) = await SeedAsync(context);

        await service.SetStockThresholdsAsync(
            new SetStockThresholdsRequest(variant.Id, warehouse.Id, 5, 10, true),
            CancellationToken.None);

        var reservation = await service.ReserveStockAsync(
            new CreateStockReservationRequest(variant.Id, warehouse.Id, 8, "cart-backorder", 30),
            CancellationToken.None);

        Assert.Equal(StockReservationStatus.Active, reservation.Status);

        var detail = await service.GetVariantInventoryAsync(variant.Id, CancellationToken.None);
        Assert.Equal(0, detail!.TotalOnHand);
        Assert.Equal(8, detail.TotalReserved);
    }

    [Fact]
    public async Task ReleaseReservationAsync_ReturnsStockToAvailable()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (warehouse, _, variant) = await SeedAsync(context);

        await service.AdjustStockAsync(
            new AdjustStockRequest(variant.Id, warehouse.Id, 10, StockAdjustmentReason.ManualIncrease, null, null),
            CancellationToken.None);
        var reservation = await service.ReserveStockAsync(
            new CreateStockReservationRequest(variant.Id, warehouse.Id, 4, "cart-release", 30),
            CancellationToken.None);

        var released = await service.ReleaseReservationAsync(reservation.Id, CancellationToken.None);

        Assert.True(released);
        var detail = await service.GetVariantInventoryAsync(variant.Id, CancellationToken.None);
        Assert.Equal(10, detail!.TotalOnHand);
        Assert.Equal(0, detail.TotalReserved);
        Assert.Equal(10, detail.TotalAvailable);
    }

    [Fact]
    public async Task ReleaseExpiredReservationsAsync_ReleasesOnlyExpired()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (warehouse, _, variant) = await SeedAsync(context);

        await service.AdjustStockAsync(
            new AdjustStockRequest(variant.Id, warehouse.Id, 10, StockAdjustmentReason.ManualIncrease, null, null),
            CancellationToken.None);

        var active = await service.ReserveStockAsync(
            new CreateStockReservationRequest(variant.Id, warehouse.Id, 3, "cart-active", 30),
            CancellationToken.None);
        var expired = await service.ReserveStockAsync(
            new CreateStockReservationRequest(variant.Id, warehouse.Id, 3, "cart-expired", 30),
            CancellationToken.None);

        var expiredEntity = await context.StockReservations.FindAsync(expired.Id);
        expiredEntity!.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        var releasedCount = await service.ReleaseExpiredReservationsAsync(CancellationToken.None);

        Assert.Equal(1, releasedCount);

        var detail = await service.GetVariantInventoryAsync(variant.Id, CancellationToken.None);
        Assert.Equal(10, detail!.TotalOnHand);
        Assert.Equal(3, detail.TotalReserved);

        var activeEntity = await context.StockReservations.FindAsync(active.Id);
        Assert.Equal(StockReservationStatus.Active, activeEntity!.Status);
        var expiredRefresh = await context.StockReservations.FindAsync(expired.Id);
        Assert.Equal(StockReservationStatus.Expired, expiredRefresh!.Status);
    }

    [Fact]
    public async Task SetStockThresholdsAsync_UpdatesThresholdsAndBackorder()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (warehouse, _, variant) = await SeedAsync(context);

        var stock = await service.SetStockThresholdsAsync(
            new SetStockThresholdsRequest(variant.Id, warehouse.Id, 5, 10, true),
            CancellationToken.None);

        Assert.Equal(5, stock.LowStockThreshold);
        Assert.Equal(10, stock.ReorderLevel);
        Assert.True(stock.AllowBackorder);
    }

    [Fact]
    public async Task SearchInventoryAsync_WithLowStockFilter_ReturnsMatchingRows()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (warehouse, _, variant) = await SeedAsync(context);

        await service.AdjustStockAsync(
            new AdjustStockRequest(variant.Id, warehouse.Id, 3, StockAdjustmentReason.ManualIncrease, null, null),
            CancellationToken.None);
        await service.SetStockThresholdsAsync(
            new SetStockThresholdsRequest(variant.Id, warehouse.Id, 5, 10, false),
            CancellationToken.None);

        var result = await service.SearchInventoryAsync(
            new InventorySearchRequest(null, true, null, null, null, null, false, 1, 20),
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(variant.Id, result.Items[0].VariantId);
        Assert.Equal(3, result.Items[0].TotalAvailable);
    }

    [Fact]
    public async Task AdjustStockAsync_MultipleWarehouses_TotalsSumAcrossWarehouses()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (warehouse, _, variant) = await SeedAsync(context);

        var secondWarehouse = new Warehouse
        {
            Name = "East Hub",
            Code = "WH-02",
            City = "Paris",
            Country = "FR",
            IsActive = true,
            IsDefault = false
        };
        context.Warehouses.Add(secondWarehouse);
        await context.SaveChangesAsync();

        await service.AdjustStockAsync(
            new AdjustStockRequest(variant.Id, warehouse.Id, 10, StockAdjustmentReason.PurchaseReceipt, null, null),
            CancellationToken.None);
        await service.AdjustStockAsync(
            new AdjustStockRequest(variant.Id, secondWarehouse.Id, 7, StockAdjustmentReason.PurchaseReceipt, null, null),
            CancellationToken.None);

        var detail = await service.GetVariantInventoryAsync(variant.Id, CancellationToken.None);

        Assert.Equal(2, detail!.Warehouses.Count);
        Assert.Equal(17, detail.TotalOnHand);
        Assert.Equal(0, detail.TotalReserved);
        Assert.Equal(17, detail.TotalAvailable);

        var refreshedVariant = await context.ProductVariants.FindAsync(variant.Id);
        Assert.Equal(17, refreshedVariant!.StockQuantity);
    }

    [Fact]
    public async Task ReleaseReservationAsync_Twice_DoesNotReleaseStockAgain()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (warehouse, _, variant) = await SeedAsync(context);

        await service.AdjustStockAsync(
            new AdjustStockRequest(variant.Id, warehouse.Id, 10, StockAdjustmentReason.ManualIncrease, null, null),
            CancellationToken.None);
        var reservation = await service.ReserveStockAsync(
            new CreateStockReservationRequest(variant.Id, warehouse.Id, 4, "cart-double-release", 30),
            CancellationToken.None);

        Assert.True(await service.ReleaseReservationAsync(reservation.Id, CancellationToken.None));
        Assert.True(await service.ReleaseReservationAsync(reservation.Id, CancellationToken.None));

        var transactions = (await service.GetTransactionHistoryAsync(variant.Id, warehouse.Id, 50, CancellationToken.None))
            .Where(t => t.Reason == StockAdjustmentReason.ReservationRelease)
            .ToList();
        Assert.Single(transactions);

        var detail = await service.GetVariantInventoryAsync(variant.Id, CancellationToken.None);
        Assert.Equal(10, detail!.TotalOnHand);
        Assert.Equal(0, detail.TotalReserved);
        Assert.Equal(10, detail.TotalAvailable);
    }

    [Fact]
    public async Task ExportInventoryCsvAsync_IncludesHeaderAndRows()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var (warehouse, _, variant) = await SeedAsync(context);

        await service.AdjustStockAsync(
            new AdjustStockRequest(variant.Id, warehouse.Id, 7, StockAdjustmentReason.ManualIncrease, null, null),
            CancellationToken.None);

        var csv = await service.ExportInventoryCsvAsync(new InventorySearchRequest(null, null, null, null, null, null, false, 1, 20), CancellationToken.None);

        Assert.StartsWith("SKU,Product,", csv, StringComparison.Ordinal);
        Assert.Contains(variant.Sku, csv, StringComparison.Ordinal);
    }
}
