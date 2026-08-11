using FashionStore.Application.DTOs.Returns;

namespace FashionStore.Application.Interfaces;

/// <summary>A photo file to attach to a return.</summary>
public sealed record ReturnAttachmentInput(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes);

/// <summary>
/// The customer-facing return panel. Order reads are always scoped to the caller's
/// identity: signed-in customers see returns for their own orders and a verified
/// guest access ticket (validated against the order number) is required for guest
/// returns. Return creation enforces the return window, product-level restrictions,
/// quantity caps and duplicate prevention; every rule is re-validated server-side.
/// </summary>
public interface ICustomerReturnService
{
    /// <summary>Paged list of a signed-in customer's returns, newest first.</summary>
    Task<CustomerReturnListResultDto> GetCustomerReturnsAsync(
        string userId,
        CustomerReturnQueryRequest query,
        CancellationToken cancellationToken = default);

    /// <summary>Detail of a return owned by the given user.</summary>
    Task<ReturnDetailDto?> GetReturnDetailAsync(
        string userId,
        string returnNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Detail of a guest return on the given order (caller access is enforced by the controller via the guest ticket).</summary>
    Task<ReturnDetailDto?> GetGuestReturnDetailAsync(
        string publicOrderNumber,
        string returnNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Paged list of returns on a guest order.</summary>
    Task<CustomerReturnListResultDto> GetGuestReturnsAsync(
        string publicOrderNumber,
        CustomerReturnQueryRequest query,
        CancellationToken cancellationToken = default);

    /// <summary>Return reason catalogue for the reason cards.</summary>
    Task<IReadOnlyList<ReturnReasonOptionDto>> GetReturnReasonsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returnable lines for an order, with quantity caps and refundable amounts.
    /// <paramref name="userId"/> is null for guest orders (the controller must have
    /// validated the guest ticket); for signed-in customers it must match the order
    /// owner or an empty result is returned.
    /// </summary>
    Task<ReturnOrderLookupDto> GetReturnableItemsAsync(
        string publicOrderNumber,
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a return request. <paramref name="userId"/> is null for guest orders
    /// (the order must then be a guest order); for signed-in customers it must match
    /// the order owner. Returns are rejected outside the return window, for
    /// non-returnable products, when quantities exceed what was purchased or when a
    /// completed return already covers the requested lines.
    /// </summary>
    Task<CreateReturnResult> CreateReturnAsync(
        string publicOrderNumber,
        CreateReturnRequest request,
        string? userId,
        string? actorName,
        CancellationToken cancellationToken = default);

    /// <summary>Uploads photo attachments for a return the caller owns.</summary>
    Task<ReturnAttachmentUploadResult> UploadAttachmentsAsync(
        string returnNumber,
        string? userId,
        string? actorName,
        IReadOnlyList<ReturnAttachmentInput> files,
        CancellationToken cancellationToken = default);

    /// <summary>Customer marks the return as shipped with an optional carrier/tracking number.</summary>
    Task<ReturnTransitionResult> MarkShippedAsync(
        string returnNumber,
        string? carrierCode,
        string? trackingNumber,
        string? userId,
        string? actorName,
        CancellationToken cancellationToken = default);

    /// <summary>Customer withdraws a return that is still in the Requested / UnderReview state.</summary>
    Task<ReturnTransitionResult> CancelAsync(
        string returnNumber,
        string? userId,
        string? actorName,
        CancellationToken cancellationToken = default);
}
