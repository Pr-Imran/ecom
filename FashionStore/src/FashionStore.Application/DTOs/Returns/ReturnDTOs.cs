using FashionStore.Domain.Enums;

namespace FashionStore.Application.DTOs.Returns;

/// <summary>
/// A returnable order line shown on the customer's product card. The quantity
/// available for return is the purchased quantity minus any quantity already claimed
/// by active or completed returns, and the refundable amount is computed server-side
/// from the order snapshot.
/// </summary>
public sealed record ReturnableItemDto(
    Guid OrderItemId,
    Guid? ProductId,
    Guid? ProductVariantId,
    string ProductName,
    string Sku,
    string? ColourName,
    string? ColourValue,
    string? SizeName,
    string? ImageUrl,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    int Quantity,
    int QuantityAvailable,
    bool IsReturnable,
    string? RestrictionReason,
    decimal RefundableAmount);

/// <summary>One line of the customer's item selection when creating a return.</summary>
public sealed record ReturnItemSelectionDto(Guid OrderItemId, int Quantity);

/// <summary>The customer's return request payload. The browser supplies item/quantity
/// selections plus reason and notes; the service re-validates everything against the
/// order snapshot and catalogue rules.</summary>
public sealed record CreateReturnRequest(
    string ReasonCode,
    string? Notes,
    bool IsExchange,
    IReadOnlyList<ReturnItemSelectionDto> Items);

public sealed record CreateReturnResult(bool Success, string? ReturnNumber, string? ErrorMessage);

/// <summary>Reason catalogue option rendered as a selectable reason card.</summary>
public sealed record ReturnReasonOptionDto(
    string Code,
    string Label,
    string? Description,
    bool RequiresPhoto,
    bool AllowShippingRefund);

/// <summary>Customer order lookup that verifies which items are still returnable.</summary>
public sealed record ReturnOrderLookupDto(
    string PublicOrderNumber,
    string? OrderStatus,
    bool WithinWindow,
    string? WindowErrorMessage,
    IReadOnlyList<ReturnableItemDto> Items);

public sealed record CustomerReturnQueryRequest(
    int Page = 1,
    int PageSize = 10,
    ReturnStatus? Status = null);

public sealed record CustomerReturnListItemDto(
    Guid ReturnId,
    string ReturnNumber,
    string OrderNumber,
    string Status,
    decimal RefundableAmount,
    decimal RefundedAmount,
    string Currency,
    int ItemCount,
    string? ThumbnailUrl,
    string FirstItemName,
    bool IsExchange,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record CustomerReturnListResultDto(
    IReadOnlyList<CustomerReturnListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasMore);

public sealed record ReturnItemDto(
    Guid Id,
    Guid OrderItemId,
    Guid? ProductId,
    Guid? ProductVariantId,
    string ProductName,
    string Sku,
    string? ColourName,
    string? ColourValue,
    string? SizeName,
    string? ImageUrl,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    int Quantity,
    int PurchasedQuantity,
    decimal RefundableAmount,
    string Condition,
    bool IsRestocked);

public sealed record ReturnAttachmentDto(
    Guid Id,
    string FileName,
    string? OriginalFileName,
    string Url,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc);

public sealed record ReturnTimelineEntryDto(
    int Sequence,
    string FromStatus,
    string ToStatus,
    string? Note,
    string? Actor,
    DateTime CreatedAtUtc);

public sealed record ExchangeRequestDto(
    Guid Id,
    Guid ProductVariantId,
    string ProductName,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    string Status,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record RefundTransactionDto(
    string Type,
    bool Succeeded,
    string? ResultCode,
    string? ResultMessage,
    DateTime CreatedAtUtc);

public sealed record RefundDto(
    Guid Id,
    string ReferenceNumber,
    string Type,
    string Status,
    decimal Amount,
    string Currency,
    bool IsGatewayRefund,
    string? ProviderRefundId,
    string? FailureReason,
    string? Reason,
    string? InitiatedBy,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<RefundTransactionDto> Transactions);

public sealed record ReturnDetailDto(
    Guid ReturnId,
    string ReturnNumber,
    Guid OrderId,
    string OrderNumber,
    bool IsGuest,
    string? CustomerName,
    string? GuestEmail,
    string Currency,
    string Status,
    string ReasonCode,
    string? CustomerNotes,
    bool IsExchange,
    decimal RefundableAmount,
    decimal RefundedAmount,
    string? TrackingNumber,
    string? CarrierCode,
    string? AdminNotes,
    DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? RejectedAtUtc,
    DateTime? ReceivedAtUtc,
    DateTime? InspectedAtUtc,
    DateTime? RefundedAtUtc,
    DateTime? CompletedAtUtc,
    string? RejectionNote,
    string Resolution,
    IReadOnlyList<ReturnItemDto> Items,
    IReadOnlyList<ReturnAttachmentDto> Attachments,
    IReadOnlyList<ReturnTimelineEntryDto> Timeline,
    IReadOnlyList<ExchangeRequestDto> Exchanges,
    IReadOnlyList<RefundDto> Refunds);

/// <summary>Outcome of uploading a return photo.</summary>
public sealed record ReturnAttachmentUploadResult(bool Success, Guid? AttachmentId, string? Url, string? ErrorMessage);

/// <summary>Outcome of a return workflow transition (customer or admin).</summary>
public sealed record ReturnTransitionResult(bool Success, string? ReturnNumber, string? Status, string? ErrorMessage);

// ---- Administrative DTOs ----

public sealed record AdminReturnQueryRequest(
    int Page = 1,
    int PageSize = 20,
    ReturnStatus? Status = null,
    string? Search = null,
    Guid? OrderId = null);

public sealed record AdminReturnListItemDto(
    Guid ReturnId,
    string ReturnNumber,
    string OrderNumber,
    bool IsGuest,
    string? CustomerName,
    string? GuestEmail,
    string Currency,
    string Status,
    decimal RefundableAmount,
    decimal RefundedAmount,
    int ItemCount,
    string ReasonCode,
    bool IsExchange,
    DateTime CreatedAtUtc,
    DateTime? ReceivedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record AdminReturnListResultDto(
    IReadOnlyList<AdminReturnListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasMore);

public sealed record ApproveReturnRequest(string? Note);

public sealed record RejectReturnRequest(string? ReasonCode, string? Note);

public sealed record MarkReceivedRequest(string? Note);

/// <summary>Per-item condition recorded during inspection.</summary>
public sealed record InspectReturnItemRequest(Guid ReturnItemId, string Condition, string? Note);

/// <summary>
/// The inspection result. <paramref name="Resolution"/> is Refund or Exchange and
/// records the decision; each item's condition (Sellable/Damaged) is captured for
/// the later inventory-restock decision.
/// </summary>
public sealed record InspectReturnRequest(string Resolution, IReadOnlyList<InspectReturnItemRequest> Items, string? Note);

public sealed record RestockReturnItemRequest(Guid ReturnItemId, Guid? WarehouseId, string? Note);

public sealed record RefundReturnRequest(
    string RefundType,
    decimal? Amount,
    IReadOnlyList<Guid>? ReturnItemIds,
    bool RefundShipping,
    string? Note,
    bool Manual,
    string? IdempotencyKey);

public sealed record ExchangeReturnRequest(Guid ProductVariantId, int Quantity, string? Note);

public sealed record CompleteReturnRequest(string? Note);

public sealed record UpdateReturnNotesRequest(string? Note);
