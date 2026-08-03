using FashionStore.Application.DTOs.Inventory;

namespace FashionStore.Application.Interfaces;

public interface IInventoryService
{
    Task<IEnumerable<WarehouseDto>> GetWarehousesAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<WarehouseDto?> GetWarehouseByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken cancellationToken = default);
    Task<WarehouseDto?> UpdateWarehouseAsync(UpdateWarehouseRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteWarehouseAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guid?> GetDefaultWarehouseIdAsync(CancellationToken cancellationToken = default);

    Task<InventorySearchResult> SearchInventoryAsync(InventorySearchRequest request, CancellationToken cancellationToken = default);
    Task<VariantInventoryDetailDto?> GetVariantInventoryAsync(Guid variantId, CancellationToken cancellationToken = default);
    Task<InventorySummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<InventoryTransactionDto>> GetTransactionHistoryAsync(Guid variantId, Guid? warehouseId = null, int limit = 50, CancellationToken cancellationToken = default);

    Task<WarehouseStockDto> AdjustStockAsync(AdjustStockRequest request, CancellationToken cancellationToken = default);
    Task<int> BulkAdjustStockAsync(BulkAdjustStockRequest request, CancellationToken cancellationToken = default);
    Task<WarehouseStockDto> SetStockThresholdsAsync(SetStockThresholdsRequest request, CancellationToken cancellationToken = default);

    Task<StockReservationDto> ReserveStockAsync(CreateStockReservationRequest request, CancellationToken cancellationToken = default);
    Task<bool> ReleaseReservationAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task<int> ReleaseExpiredReservationsAsync(CancellationToken cancellationToken = default);

    Task<string> ExportInventoryCsvAsync(InventorySearchRequest request, CancellationToken cancellationToken = default);
}
