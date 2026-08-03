using FashionStore.Domain.Enums;

namespace FashionStore.Application.DTOs.Inventory;

public sealed record WarehouseDto(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    string? Address,
    string? City,
    string? Country,
    bool IsActive,
    bool IsDefault,
    int DisplayOrder
);

public sealed record CreateWarehouseRequest(
    string Name,
    string Code,
    string? Description,
    string? Address,
    string? City,
    string? Country,
    bool IsActive,
    bool IsDefault,
    int DisplayOrder
);

public sealed record UpdateWarehouseRequest(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    string? Address,
    string? City,
    string? Country,
    bool IsActive,
    bool IsDefault,
    int DisplayOrder
);

public sealed record WarehouseStockDto(
    Guid Id,
    Guid WarehouseId,
    string WarehouseName,
    Guid VariantId,
    string Sku,
    int OnHandQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    int? LowStockThreshold,
    int? ReorderLevel,
    bool AllowBackorder,
    DateTime UpdatedAtUtc
);

public sealed record InventoryRowDto(
    Guid VariantId,
    string Sku,
    string ProductName,
    string? ProductSlug,
    decimal Price,
    string? VariantImageUrl,
    Dictionary<string, string> AttributeValues,
    int WarehouseCount,
    int TotalOnHand,
    int TotalReserved,
    int TotalAvailable,
    int? LowStockThreshold,
    bool AllowBackorder,
    DateTime UpdatedAtUtc
);

public sealed record InventorySearchRequest(
    string? SearchTerm,
    bool? LowStockOnly,
    bool? OutOfStockOnly,
    bool? BackorderOnly,
    Guid? WarehouseId,
    string? SortBy,
    bool SortDescending,
    int Page = 1,
    int PageSize = 20
);

public sealed record InventorySearchResult(
    IReadOnlyList<InventoryRowDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);

public sealed record AdjustStockRequest(
    Guid VariantId,
    Guid? WarehouseId,
    int AdjustmentQuantity,
    StockAdjustmentReason Reason,
    string? Notes,
    string? AdministratorId,
    InventoryReferenceType ReferenceType = InventoryReferenceType.None,
    string? ReferenceId = null
);

public sealed record BulkAdjustStockRequest(
    List<Guid> VariantIds,
    Guid? WarehouseId,
    int AdjustmentQuantity,
    StockAdjustmentReason Reason,
    string? Notes,
    string? AdministratorId
);

public sealed record SetStockThresholdsRequest(
    Guid VariantId,
    Guid? WarehouseId,
    int? LowStockThreshold,
    int? ReorderLevel,
    bool? AllowBackorder
);

public sealed record StockReservationDto(
    Guid Id,
    Guid VariantId,
    string Sku,
    Guid? WarehouseId,
    int Quantity,
    string CartReference,
    DateTime ExpiresAtUtc,
    StockReservationStatus Status,
    DateTime CreatedAtUtc,
    DateTime? ReleasedAtUtc
);

public sealed record CreateStockReservationRequest(
    Guid VariantId,
    Guid? WarehouseId,
    int Quantity,
    string CartReference,
    int ExpirationMinutes = 30,
    InventoryReferenceType ReferenceType = InventoryReferenceType.Cart,
    string? ReferenceId = null
);

public sealed record InventoryTransactionDto(
    Guid Id,
    Guid WarehouseId,
    string WarehouseName,
    Guid VariantId,
    string Sku,
    int QuantityChange,
    int PreviousOnHand,
    int NewOnHand,
    int ReservedQuantityChange,
    int PreviousReserved,
    int NewReserved,
    StockAdjustmentReason Reason,
    InventoryReferenceType ReferenceType,
    string? ReferenceId,
    string? Notes,
    string? AdministratorId,
    DateTime CreatedAtUtc
);

public sealed record VariantInventoryDetailDto(
    Guid VariantId,
    string Sku,
    string ProductName,
    decimal Price,
    Dictionary<string, string> AttributeValues,
    IReadOnlyList<WarehouseStockDto> Warehouses,
    int TotalOnHand,
    int TotalReserved,
    int TotalAvailable,
    bool AllowBackorder,
    DateTime UpdatedAtUtc
);

public sealed record InventorySummaryDto(
    int TotalVariantStocks,
    int LowStockCount,
    int OutOfStockCount,
    int BackorderEnabledCount,
    int TotalOnHand,
    int TotalReserved,
    int TotalAvailable,
    int ActiveReservationCount
);
