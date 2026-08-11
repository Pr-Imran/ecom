using FashionStore.Application.DTOs.Returns;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Administrative return management. Every lifecycle change flows through this
/// service so a single central state machine owns the transition rules: statuses
/// advance forward only, every transition is recorded in the return's status history
/// with the acting administrator, inventory is restored only for sellable inspected
/// items, and refunds are idempotent and run through the payment pipeline when the
/// gateway is enabled.
/// </summary>
public interface IAdminReturnService
{
    Task<AdminReturnListResultDto> GetReturnsAsync(
        AdminReturnQueryRequest query,
        CancellationToken cancellationToken = default);

    Task<ReturnDetailDto?> GetReturnDetailAsync(
        Guid returnId,
        CancellationToken cancellationToken = default);

    Task<ReturnTransitionResult> ReviewAsync(
        Guid returnId,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default);

    Task<ReturnTransitionResult> ApproveAsync(
        Guid returnId,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default);

    Task<ReturnTransitionResult> RejectAsync(
        Guid returnId,
        string? reasonCode,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default);

    Task<ReturnTransitionResult> MarkReceivedAsync(
        Guid returnId,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default);

    Task<ReturnTransitionResult> InspectAsync(
        Guid returnId,
        InspectReturnRequest request,
        string actorId,
        CancellationToken cancellationToken = default);

    Task<ReturnTransitionResult> RestockItemAsync(
        Guid returnId,
        RestockReturnItemRequest request,
        string actorId,
        CancellationToken cancellationToken = default);

    Task<ReturnTransitionResult> RefundAsync(
        Guid returnId,
        RefundReturnRequest request,
        string actorId,
        CancellationToken cancellationToken = default);

    Task<ReturnTransitionResult> ExchangeAsync(
        Guid returnId,
        ExchangeReturnRequest request,
        string actorId,
        CancellationToken cancellationToken = default);

    Task<ReturnTransitionResult> CompleteAsync(
        Guid returnId,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default);

    Task<ReturnTransitionResult> UpdateNotesAsync(
        Guid returnId,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default);
}
