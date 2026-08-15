using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Images;
using FashionStore.Application.DTOs.Inventory;
using FashionStore.Application.DTOs.Returns;
using FashionStore.Application.Email;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Administrative return management. This service owns the single state machine for a
/// return's lifecycle: every transition is forward-only, recorded in the status
/// history with the acting administrator, and re-validated server-side. Inspection
/// captures each item's condition; only sellable inspected items are returned to
/// sellable stock. Refunds are idempotent (a supplied idempotency key is only ever
/// applied once) and route through the payment pipeline when the gateway is enabled,
/// while manual refunds never touch the gateway.
/// </summary>
public sealed class AdminReturnService : IAdminReturnService
{
    private readonly AppDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IPaymentService _paymentService;
    private readonly IOptions<ReturnSettings> _returnOptions;
    private readonly IEmailNotificationService _emailService;
    private readonly IAuditService _auditService;
    private readonly ILogger<AdminReturnService> _logger;

    public AdminReturnService(
        AppDbContext context,
        IInventoryService inventoryService,
        IPaymentService paymentService,
        IOptions<ReturnSettings> returnOptions,
        IEmailNotificationService emailService,
        IAuditService auditService,
        ILogger<AdminReturnService> logger)
    {
        _context = context;
        _inventoryService = inventoryService;
        _paymentService = paymentService;
        _returnOptions = returnOptions;
        _emailService = emailService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<AdminReturnListResultDto> GetReturnsAsync(
        AdminReturnQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        var baseQuery = _context.ReturnRequests
            .AsNoTracking()
            .Include(r => r.Order)
            .Include(r => r.Items)
            .AsQueryable();

        if (query.Status.HasValue)
        {
            baseQuery = baseQuery.Where(r => r.Status == query.Status.Value);
        }

        if (query.OrderId.HasValue)
        {
            baseQuery = baseQuery.Where(r => r.OrderId == query.OrderId.Value);
        }

        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{search.Replace("%", "[%]").Replace("_", "[_]")}%";
            baseQuery = baseQuery.Where(r =>
                EF.Functions.Like(r.ReturnNumber, pattern) ||
                EF.Functions.Like(r.Order!.PublicOrderNumber, pattern) ||
                (r.GuestEmail != null && EF.Functions.Like(r.GuestEmail, pattern)) ||
                (r.CustomerName != null && EF.Functions.Like(r.CustomerName, pattern)));
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var returns = await baseQuery
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = returns.Select(BuildListItem).ToList();

        return new AdminReturnListResultDto(
            items,
            totalCount,
            page,
            pageSize,
            page * pageSize < totalCount);
    }

    public async Task<ReturnDetailDto?> GetReturnDetailAsync(
        Guid returnId,
        CancellationToken cancellationToken = default)
    {
        var returnRequest = await LoadReturnAsync(returnId, cancellationToken);
        return returnRequest is null ? null : BuildDetail(returnRequest);
    }

    public async Task<ReturnTransitionResult> ReviewAsync(
        Guid returnId,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteTransitionAsync(
            returnId,
            actorId,
            from => from is ReturnStatus.Requested,
            ReturnStatus.UnderReview,
            null,
            string.IsNullOrWhiteSpace(note) ? "Return moved to review." : $"Return moved to review: {note.Trim()}",
            returnRequest => { },
            cancellationToken);
    }

    public async Task<ReturnTransitionResult> ApproveAsync(
        Guid returnId,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteTransitionAsync(
            returnId,
            actorId,
            from => from is ReturnStatus.Requested or ReturnStatus.UnderReview,
            ReturnStatus.AwaitingShipment,
            null,
            string.IsNullOrWhiteSpace(note)
                ? "Return approved; awaiting shipment from customer."
                : $"Return approved: {note.Trim()}",
            returnRequest =>
            {
                returnRequest.ApprovedAtUtc = DateTime.UtcNow;
            },
            cancellationToken,
            afterSave: (returnRequest, token) => _emailService.SendReturnApprovedAsync(returnRequest.Id, token));
    }

    public async Task<ReturnTransitionResult> RejectAsync(
        Guid returnId,
        string? reasonCode,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                var returnRequest = await _context.ReturnRequests
                    .FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);

                if (returnRequest is null)
                {
                    return new ReturnTransitionResult(false, null, null, "Return request not found.");
                }

                if (returnRequest.Status is not (
                    ReturnStatus.Requested or
                    ReturnStatus.UnderReview or
                    ReturnStatus.AwaitingShipment))
                {
                    return new ReturnTransitionResult(false, null, null, $"A return in {returnRequest.Status} cannot be rejected.");
                }

                var now = DateTime.UtcNow;
                var previous = returnRequest.Status;
                returnRequest.Status = ReturnStatus.Rejected;
                returnRequest.RejectedAtUtc = now;
                returnRequest.RejectionReasonCode = NormalizeOptional(reasonCode, 50);
                returnRequest.RejectionNote = NormalizeOptional(note, 1000);
                returnRequest.UpdatedAtUtc = now;

                _context.ReturnStatusHistories.Add(new ReturnStatusHistory
                {
                    ReturnRequestId = returnRequest.Id,
                    FromStatus = previous,
                    ToStatus = ReturnStatus.Rejected,
                    Note = string.IsNullOrWhiteSpace(note) ? "Return rejected." : $"Return rejected: {note.Trim()}",
                    CreatedBy = actorId,
                    CreatedAtUtc = now
                });

                await _context.SaveChangesAsync(cancellationToken);

                await _emailService.SendReturnRejectedAsync(returnRequest.Id, cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation("Return {ReturnNumber} rejected by {Actor}", returnRequest.ReturnNumber, actorId);

                return new ReturnTransitionResult(true, returnRequest.ReturnNumber, returnRequest.Status.ToString(), null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rejecting return {ReturnId} failed", returnId);
            return new ReturnTransitionResult(false, null, null, "We could not reject the return. Please try again.");
        }
    }

    public async Task<ReturnTransitionResult> MarkReceivedAsync(
        Guid returnId,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteTransitionAsync(
            returnId,
            actorId,
            from => from is ReturnStatus.InTransit,
            ReturnStatus.Received,
            "received",
            string.IsNullOrWhiteSpace(note) ? "Returned items received at warehouse." : $"Returned items received: {note.Trim()}",
            returnRequest =>
            {
                returnRequest.ReceivedAtUtc = DateTime.UtcNow;
            },
            cancellationToken);
    }

    public async Task<ReturnTransitionResult> InspectAsync(
        Guid returnId,
        InspectReturnRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                var returnRequest = await _context.ReturnRequests
                    .Include(r => r.Items)
                    .FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);

                if (returnRequest is null)
                {
                    return new ReturnTransitionResult(false, null, null, "Return request not found.");
                }

                if (returnRequest.Status is not ReturnStatus.Received)
                {
                    return new ReturnTransitionResult(false, null, null, "Only received returns can be inspected.");
                }

                if (request.Items is null || request.Items.Count != returnRequest.Items.Count)
                {
                    return new ReturnTransitionResult(false, null, null, "Record a condition for every returned item.");
                }

                if (!Enum.TryParse<ReturnResolution>(request.Resolution, ignoreCase: true, out var resolution) ||
                    resolution is not (ReturnResolution.Refund or ReturnResolution.Exchange))
                {
                    return new ReturnTransitionResult(false, null, null, "Choose whether this return is resolved with a refund or an exchange.");
                }

                var itemMap = returnRequest.Items.ToDictionary(i => i.Id);
                foreach (var itemRequest in request.Items)
                {
                    if (!itemMap.TryGetValue(itemRequest.ReturnItemId, out var item))
                    {
                        return new ReturnTransitionResult(false, null, null, "One of the inspected items does not belong to this return.");
                    }

                    if (!Enum.TryParse<ReturnItemCondition>(itemRequest.Condition, ignoreCase: true, out var condition) ||
                        condition is not (ReturnItemCondition.Sellable or ReturnItemCondition.Damaged))
                    {
                        return new ReturnTransitionResult(false, null, null, $"Choose a condition for \"{item.ProductName}\".");
                    }

                    item.Condition = condition;
                    item.InspectionNote = NormalizeOptional(itemRequest.Note, 1000);
                    item.InspectedAtUtc = DateTime.UtcNow;
                }

                var now = DateTime.UtcNow;
                var previous = returnRequest.Status;
                returnRequest.Status = ReturnStatus.Inspected;
                returnRequest.Resolution = resolution;
                returnRequest.InspectedAtUtc = now;
                returnRequest.UpdatedAtUtc = now;

                _context.ReturnStatusHistories.Add(new ReturnStatusHistory
                {
                    ReturnRequestId = returnRequest.Id,
                    FromStatus = previous,
                    ToStatus = ReturnStatus.Inspected,
                    Note = $"{returnRequest.Items.Count} item(s) inspected; resolved as {(resolution == ReturnResolution.Refund ? "refund" : "exchange")}.",
                    CreatedBy = actorId,
                    CreatedAtUtc = now
                });

                await _context.SaveChangesAsync(cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation("Return {ReturnNumber} inspected by {Actor} (resolution {Resolution})", returnRequest.ReturnNumber, actorId, resolution);

                return new ReturnTransitionResult(true, returnRequest.ReturnNumber, returnRequest.Status.ToString(), null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inspecting return {ReturnId} failed", returnId);
            return new ReturnTransitionResult(false, null, null, "We could not save the inspection. Please try again.");
        }
    }

    public async Task<ReturnTransitionResult> RestockItemAsync(
        Guid returnId,
        RestockReturnItemRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                var returnRequest = await _context.ReturnRequests
                    .Include(r => r.Items)
                    .FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);

                if (returnRequest is null)
                {
                    return new ReturnTransitionResult(false, null, null, "Return request not found.");
                }

                if (returnRequest.Status is not ReturnStatus.Inspected)
                {
                    return new ReturnTransitionResult(false, null, null, "Items can only be restocked after inspection.");
                }

                var item = returnRequest.Items.FirstOrDefault(i => i.Id == request.ReturnItemId);
                if (item is null)
                {
                    return new ReturnTransitionResult(false, null, null, "The item does not belong to this return.");
                }

                if (item.IsRestocked)
                {
                    return new ReturnTransitionResult(false, null, null, $"\"{item.ProductName}\" has already been restocked.");
                }

                if (item.Condition is not ReturnItemCondition.Sellable)
                {
                    return new ReturnTransitionResult(false, null, null, $"\"{item.ProductName}\" was marked damaged and cannot be restocked.");
                }

                if (!item.ProductVariantId.HasValue)
                {
                    return new ReturnTransitionResult(false, null, null, $"\"{item.ProductName}\" no longer has a catalogue variant to restock.");
                }

                await _inventoryService.AdjustStockAsync(new AdjustStockRequest(
                    item.ProductVariantId.Value,
                    request.WarehouseId,
                    item.Quantity,
                    StockAdjustmentReason.CustomerReturn,
                    NormalizeOptional(request.Note, 500) ?? $"Sellable item returned under {returnRequest.ReturnNumber}.",
                    actorId,
                    InventoryReferenceType.Return,
                    returnRequest.ReturnNumber), cancellationToken);

                item.IsRestocked = true;
                item.RestockedAtUtc = DateTime.UtcNow;
                returnRequest.UpdatedAtUtc = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "Return {ReturnNumber} item {Product} restocked ({Quantity}) by {Actor}",
                    returnRequest.ReturnNumber,
                    item.ProductName,
                    item.Quantity,
                    actorId);

                return new ReturnTransitionResult(true, returnRequest.ReturnNumber, returnRequest.Status.ToString(), null);
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Restocking return {ReturnId} item failed due to stock rules", returnId);
            return new ReturnTransitionResult(false, null, null, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restocking return {ReturnId} item failed", returnId);
            return new ReturnTransitionResult(false, null, null, "We could not restock the item. Please try again.");
        }
    }

    public async Task<ReturnTransitionResult> RefundAsync(
        Guid returnId,
        RefundReturnRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                var returnRequest = await _context.ReturnRequests
                    .Include(r => r.Items)
                    .Include(r => r.Refunds).ThenInclude(rf => rf.Transactions)
                    .FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);

                if (returnRequest is null)
                {
                    return new ReturnTransitionResult(false, null, null, "Return request not found.");
                }

                if (returnRequest.Resolution is not ReturnResolution.Refund)
                {
                    return new ReturnTransitionResult(false, null, null, "This return was resolved as an exchange, not a refund.");
                }

                if (returnRequest.Status is not (ReturnStatus.Inspected or ReturnStatus.RefundPending or ReturnStatus.Refunded))
                {
                    return new ReturnTransitionResult(false, null, null, $"A refund cannot be issued from status {returnRequest.Status}.");
                }

                if (!Enum.TryParse<RefundType>(request.RefundType, ignoreCase: true, out var refundType) ||
                    refundType is not (
                        RefundType.Full or
                        RefundType.Partial or
                        RefundType.Item or
                        RefundType.Shipping or
                        RefundType.Manual))
                {
                    return new ReturnTransitionResult(false, null, null, "Choose a valid refund type.");
                }

                if (request.Manual && !_returnOptions.Value.AllowManualRefund)
                {
                    return new ReturnTransitionResult(false, null, null, "Manual refunds are disabled.");
                }

                if (!request.Manual && !_returnOptions.Value.AllowGatewayRefund)
                {
                    return new ReturnTransitionResult(false, null, null, "Gateway refunds are disabled.");
                }

                var idempotencyKey = ResolveIdempotencyKey(returnRequest, request);
                var existing = returnRequest.Refunds.FirstOrDefault(rf =>
                    rf.IdempotencyKey == idempotencyKey && rf.Status != RefundStatus.Voided);
                if (existing is not null)
                {
                    return new ReturnTransitionResult(true, returnRequest.ReturnNumber, returnRequest.Status.ToString(), null);
                }

                var settings = _returnOptions.Value;
                var order = await _context.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == returnRequest.OrderId, cancellationToken);
                if (order is null)
                {
                    return new ReturnTransitionResult(false, null, null, "The order for this return no longer exists.");
                }

                // The refund cap is the item refundable total plus the shipping
                // charge exactly once, then everything already refunded is
                // subtracted. Adding the shipping charge to the remaining total
                // on every shipping refund would let a refunded shipping charge
                // be refunded repeatedly.
                var shippingReason = await _context.ReturnReasons
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Code == returnRequest.ReasonCode.ToString(), cancellationToken);
                var shippingEligible = settings.AllowShippingRefund &&
                                       (shippingReason?.AllowShippingRefund ?? true) &&
                                       CoversEntireOrder(returnRequest, order);
                var refundCap = Math.Round(
                    returnRequest.RefundableAmount + (shippingEligible ? order.ShippingCharge : 0m),
                    2);

                var alreadyRefunded = returnRequest.Refunds
                    .Where(rf => rf.Status == RefundStatus.Succeeded)
                    .Sum(rf => rf.Amount);
                var remaining = Math.Max(0m, refundCap - alreadyRefunded);

                var (amount, reason) = await ComputeRefundAsync(
                    returnRequest,
                    order,
                    refundType,
                    request,
                    settings,
                    cancellationToken);

                if (amount <= 0m)
                {
                    return new ReturnTransitionResult(false, null, null, "The refund amount must be positive.");
                }

                if (amount > remaining)
                {
                    return new ReturnTransitionResult(false, null, null, $"The refund amount exceeds the amount still refundable ({remaining:0.00} {returnRequest.Currency}).");
                }

                var now = DateTime.UtcNow;
                var refund = new Refund
                {
                    ReturnRequestId = returnRequest.Id,
                    OrderId = returnRequest.OrderId,
                    ReferenceNumber = await GenerateUniqueRefundNumberAsync(cancellationToken),
                    Type = refundType,
                    Status = RefundStatus.Pending,
                    Amount = amount,
                    Currency = returnRequest.Currency,
                    IsGatewayRefund = !request.Manual,
                    IdempotencyKey = idempotencyKey,
                    Reason = reason,
                    InitiatedBy = actorId,
                    CreatedAtUtc = now,
                    CreatedBy = actorId
                };

                refund.Transactions.Add(new RefundTransaction
                {
                    Type = "Created",
                    Succeeded = false,
                    ResultMessage = $"Refund of {amount:0.00} {returnRequest.Currency} created ({(request.Manual ? "manual" : "gateway")}).",
                    CreatedBy = actorId,
                    CreatedAtUtc = now
                });

                if (request.Manual)
                {
                    CompleteRefund(refund, null, now, actorId);
                }
                else
                {
                    var payment = await FindRefundablePaymentAsync(order.Id, cancellationToken);
                    if (payment is null)
                    {
                        refund.Status = RefundStatus.Failed;
                        refund.FailureCode = "no-payment";
                        refund.FailureReason = "No captured payment found for the order.";
                        refund.Transactions.Add(new RefundTransaction
                        {
                            Type = "Failed",
                            Succeeded = false,
                            ResultCode = "no-payment",
                            ResultMessage = "No captured payment found for the order.",
                            CreatedBy = actorId,
                            CreatedAtUtc = now
                        });

                        _context.Refunds.Add(refund);
                        await _context.SaveChangesAsync(cancellationToken);

                        if (tx != null)
                        {
                            await tx.CommitAsync(cancellationToken);
                        }

                        return new ReturnTransitionResult(false, null, null, "No captured payment was found for this order, so the gateway refund could not be issued. Use a manual refund instead.");
                    }

                    var gatewayResult = await _paymentService.RefundAsync(payment.Id, amount, actorId, cancellationToken);
                    if (!gatewayResult.Success)
                    {
                        refund.Status = RefundStatus.Failed;
                        refund.FailureCode = gatewayResult.FailureCode;
                        refund.FailureReason = gatewayResult.FailureReason;
                        refund.Transactions.Add(new RefundTransaction
                        {
                            Type = "Failed",
                            Succeeded = false,
                            ResultCode = gatewayResult.FailureCode,
                            ResultMessage = gatewayResult.FailureReason,
                            CreatedBy = actorId,
                            CreatedAtUtc = DateTime.UtcNow
                        });

                        _context.Refunds.Add(refund);
                        await _context.SaveChangesAsync(cancellationToken);

                        if (tx != null)
                        {
                            await tx.CommitAsync(cancellationToken);
                        }

                        return new ReturnTransitionResult(false, null, null, $"The payment provider declined the refund: {gatewayResult.FailureReason ?? gatewayResult.FailureCode}");
                    }

                    refund.ProviderRefundId = gatewayResult.ProviderRefundId;
                    CompleteRefund(refund, gatewayResult.ProviderRefundId, now, actorId);
                }

                _context.Refunds.Add(refund);
                await _context.SaveChangesAsync(cancellationToken);

                var priorRefunded = returnRequest.Refunds
                    .Where(rf => rf.Id != refund.Id && rf.Status == RefundStatus.Succeeded)
                    .Sum(rf => rf.Amount);
                var refundedTotal = priorRefunded + (refund.Status == RefundStatus.Succeeded ? refund.Amount : 0m);
                returnRequest.RefundedAmount = Math.Round(refundedTotal, 2);
                returnRequest.Status = ReturnStatus.Refunded;
                returnRequest.RefundedAtUtc = now;
                returnRequest.UpdatedAtUtc = now;

                if (!request.Manual)
                {
                    order.RefundedAmount = Math.Round(order.RefundedAmount + amount, 2);
                }

                _context.ReturnStatusHistories.Add(new ReturnStatusHistory
                {
                    ReturnRequestId = returnRequest.Id,
                    FromStatus = ReturnStatus.Inspected,
                    ToStatus = ReturnStatus.Refunded,
                    Note = $"Refund of {amount:0.00} {returnRequest.Currency} issued ({(request.Manual ? "manual" : "gateway")}) — {refund.ReferenceNumber}.",
                    CreatedBy = actorId,
                    CreatedAtUtc = now
                });

                await _context.SaveChangesAsync(cancellationToken);

                await _emailService.SendRefundCompletedAsync(returnRequest.OrderId, cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "Refund {ReferenceNumber} of {Amount} {Currency} issued for return {ReturnNumber} by {Actor} (manual: {Manual})",
                    refund.ReferenceNumber,
                    amount,
                    returnRequest.Currency,
                    returnRequest.ReturnNumber,
                    actorId,
                    request.Manual);

                await _auditService.RecordAsync(
                    "Refund.Issued",
                    "ReturnRequest",
                    returnRequest.Id.ToString(),
                    oldValue: null,
                    newValue: $"{amount:0.00} {returnRequest.Currency} ({refund.ReferenceNumber}, manual: {request.Manual})",
                    actorId: actorId,
                    cancellationToken: cancellationToken);

                return new ReturnTransitionResult(true, returnRequest.ReturnNumber, returnRequest.Status.ToString(), null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refunding return {ReturnId} failed", returnId);
            return new ReturnTransitionResult(false, null, null, "We could not issue the refund. Please try again.");
        }
    }

    public async Task<ReturnTransitionResult> ExchangeAsync(
        Guid returnId,
        ExchangeReturnRequest request,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                var returnRequest = await _context.ReturnRequests
                    .Include(r => r.ExchangeRequests)
                    .FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);

                if (returnRequest is null)
                {
                    return new ReturnTransitionResult(false, null, null, "Return request not found.");
                }

                if (returnRequest.Resolution is not ReturnResolution.Exchange)
                {
                    return new ReturnTransitionResult(false, null, null, "This return was resolved as a refund, not an exchange.");
                }

                if (returnRequest.Status is not (ReturnStatus.Inspected or ReturnStatus.Exchanged))
                {
                    return new ReturnTransitionResult(false, null, null, $"An exchange cannot be arranged from status {returnRequest.Status}.");
                }

                if (request.Quantity < 1)
                {
                    return new ReturnTransitionResult(false, null, null, "The exchange quantity must be at least one.");
                }

                var variant = await _context.ProductVariants
                    .AsNoTracking()
                    .Include(v => v.Product)
                    .FirstOrDefaultAsync(v => v.Id == request.ProductVariantId, cancellationToken);

                if (variant is null || !variant.IsActive || variant.Product is null || !variant.Product.IsActive)
                {
                    return new ReturnTransitionResult(false, null, null, "The replacement item is no longer available.");
                }

                var now = DateTime.UtcNow;
                var previous = returnRequest.Status;

                foreach (var pending in returnRequest.ExchangeRequests.Where(e => e.Status == ExchangeStatus.Pending))
                {
                    pending.Status = ExchangeStatus.Cancelled;
                    pending.CompletedAtUtc = now;
                    pending.CompletedBy = actorId;
                }

                var exchange = new ExchangeRequest
                {
                    ReturnRequestId = returnRequest.Id,
                    OrderId = returnRequest.OrderId,
                    ProductVariantId = variant.Id,
                    ProductName = variant.Product.Name,
                    Sku = variant.Sku,
                    Quantity = request.Quantity,
                    UnitPrice = variant.Price,
                    Status = ExchangeStatus.Pending,
                    Notes = NormalizeOptional(request.Note, 1000),
                    CreatedAtUtc = now,
                    CreatedBy = actorId
                };

                _context.ExchangeRequests.Add(exchange);
                returnRequest.Status = ReturnStatus.Exchanged;
                returnRequest.UpdatedAtUtc = now;

                _context.ReturnStatusHistories.Add(new ReturnStatusHistory
                {
                    ReturnRequestId = returnRequest.Id,
                    FromStatus = previous,
                    ToStatus = ReturnStatus.Exchanged,
                    Note = $"Exchange arranged: {variant.Product.Name} × {request.Quantity}.",
                    CreatedBy = actorId,
                    CreatedAtUtc = now
                });

                await _context.SaveChangesAsync(cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "Exchange {ExchangeId} arranged for return {ReturnNumber} by {Actor}",
                    exchange.Id,
                    returnRequest.ReturnNumber,
                    actorId);

                return new ReturnTransitionResult(true, returnRequest.ReturnNumber, returnRequest.Status.ToString(), null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Arranging exchange for return {ReturnId} failed", returnId);
            return new ReturnTransitionResult(false, null, null, "We could not arrange the exchange. Please try again.");
        }
    }

    public async Task<ReturnTransitionResult> CompleteAsync(
        Guid returnId,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                var returnRequest = await _context.ReturnRequests
                    .Include(r => r.ExchangeRequests)
                    .FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);

                if (returnRequest is null)
                {
                    return new ReturnTransitionResult(false, null, null, "Return request not found.");
                }

                if (returnRequest.Status is not (ReturnStatus.Refunded or ReturnStatus.Exchanged))
                {
                    return new ReturnTransitionResult(false, null, null, "Only refunded or exchanged returns can be closed.");
                }

                var now = DateTime.UtcNow;
                var previous = returnRequest.Status;
                returnRequest.Status = ReturnStatus.Closed;
                returnRequest.CompletedAtUtc = now;
                returnRequest.UpdatedAtUtc = now;

                foreach (var pending in returnRequest.ExchangeRequests.Where(e => e.Status == ExchangeStatus.Pending))
                {
                    pending.Status = ExchangeStatus.Completed;
                    pending.CompletedAtUtc = now;
                    pending.CompletedBy = actorId;
                }

                _context.ReturnStatusHistories.Add(new ReturnStatusHistory
                {
                    ReturnRequestId = returnRequest.Id,
                    FromStatus = previous,
                    ToStatus = ReturnStatus.Closed,
                    Note = string.IsNullOrWhiteSpace(note) ? "Return closed." : $"Return closed: {note.Trim()}",
                    CreatedBy = actorId,
                    CreatedAtUtc = now
                });

                await _context.SaveChangesAsync(cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation("Return {ReturnNumber} closed by {Actor}", returnRequest.ReturnNumber, actorId);

                return new ReturnTransitionResult(true, returnRequest.ReturnNumber, returnRequest.Status.ToString(), null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Closing return {ReturnId} failed", returnId);
            return new ReturnTransitionResult(false, null, null, "We could not close the return. Please try again.");
        }
    }

    public async Task<ReturnTransitionResult> UpdateNotesAsync(
        Guid returnId,
        string? note,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var returnRequest = await _context.ReturnRequests
                .FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);

            if (returnRequest is null)
            {
                return new ReturnTransitionResult(false, null, null, "Return request not found.");
            }

            if (returnRequest.Status is ReturnStatus.Rejected or ReturnStatus.Closed)
            {
                return new ReturnTransitionResult(false, null, null, "Closed returns cannot be edited.");
            }

            if (string.IsNullOrWhiteSpace(note))
            {
                return new ReturnTransitionResult(false, null, null, "Enter a note.");
            }

            var now = DateTime.UtcNow;
            var prefix = string.IsNullOrWhiteSpace(returnRequest.AdminNotes)
                ? $"[{now:yyyy-MM-dd HH:mm}] {note.Trim()}"
                : $"{returnRequest.AdminNotes}\n[{now:yyyy-MM-dd HH:mm}] {note.Trim()}";
            returnRequest.AdminNotes = prefix.Length > 1000 ? prefix[..1000] : prefix;
            returnRequest.UpdatedAtUtc = now;

            await _context.SaveChangesAsync(cancellationToken);

            return new ReturnTransitionResult(true, returnRequest.ReturnNumber, returnRequest.Status.ToString(), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Updating notes for return {ReturnId} failed", returnId);
            return new ReturnTransitionResult(false, null, null, "We could not save the note. Please try again.");
        }
    }

    // ---- Helpers ----

    private async Task<ReturnTransitionResult> ExecuteTransitionAsync(
        Guid returnId,
        string actorId,
        Func<ReturnStatus, bool> allowedFrom,
        ReturnStatus toStatus,
        string? errorVerb,
        string historyNote,
        Action<ReturnRequest> apply,
        CancellationToken cancellationToken,
        Func<ReturnRequest, CancellationToken, Task>? afterSave = null)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                var returnRequest = await _context.ReturnRequests
                    .FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);

                if (returnRequest is null)
                {
                    return new ReturnTransitionResult(false, null, null, "Return request not found.");
                }

                if (!allowedFrom(returnRequest.Status))
                {
                    var verb = errorVerb ?? toStatus.ToString().ToLowerInvariant();
                    return new ReturnTransitionResult(false, null, null, $"A return in {returnRequest.Status} cannot be {verb}.");
                }

                var now = DateTime.UtcNow;
                var previous = returnRequest.Status;
                apply(returnRequest);
                returnRequest.Status = toStatus;
                returnRequest.UpdatedAtUtc = now;

                _context.ReturnStatusHistories.Add(new ReturnStatusHistory
                {
                    ReturnRequestId = returnRequest.Id,
                    FromStatus = previous,
                    ToStatus = toStatus,
                    Note = historyNote,
                    CreatedBy = actorId,
                    CreatedAtUtc = now
                });

                await _context.SaveChangesAsync(cancellationToken);

                if (afterSave is not null)
                {
                    await afterSave(returnRequest, cancellationToken);
                }

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation("Return {ReturnNumber} moved to {Status} by {Actor}", returnRequest.ReturnNumber, toStatus, actorId);

                return new ReturnTransitionResult(true, returnRequest.ReturnNumber, returnRequest.Status.ToString(), null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transitioning return {ReturnId} to {Status} failed", returnId, toStatus);
            return new ReturnTransitionResult(false, null, null, "We could not update the return. Please try again.");
        }
    }

    private async Task<(decimal Amount, string Reason)> ComputeRefundAsync(
        ReturnRequest returnRequest,
        Order order,
        RefundType refundType,
        RefundReturnRequest request,
        ReturnSettings settings,
        CancellationToken cancellationToken)
    {
        switch (refundType)
        {
            case RefundType.Full:
            {
                var amount = returnRequest.Items.Sum(i => i.RefundableAmount);
                return (Math.Round(amount, 2), $"Full refund for {returnRequest.ReturnNumber}.");
            }

            case RefundType.Item:
            {
                if (request.ReturnItemIds is null || request.ReturnItemIds.Count == 0)
                {
                    return (0m, string.Empty);
                }

                var items = returnRequest.Items
                    .Where(i => request.ReturnItemIds.Contains(i.Id))
                    .ToList();

                if (items.Count != request.ReturnItemIds.Count)
                {
                    return (0m, string.Empty);
                }

                var amount = items.Sum(i => i.RefundableAmount);
                return (Math.Round(amount, 2), $"Item refund for {returnRequest.ReturnNumber} ({items.Count} line(s)).");
            }

            case RefundType.Shipping:
            {
                var reason = await _context.ReturnReasons
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Code == returnRequest.ReasonCode.ToString(), cancellationToken);

                var shippingAllowed = settings.AllowShippingRefund &&
                                      (reason?.AllowShippingRefund ?? true);

                if (!shippingAllowed)
                {
                    return (0m, string.Empty);
                }

                if (!CoversEntireOrder(returnRequest, order))
                {
                    return (0m, string.Empty);
                }

                return (Math.Round(order.ShippingCharge, 2), $"Shipping charge refund for {returnRequest.ReturnNumber}.");
            }

            case RefundType.Partial:
            case RefundType.Manual:
            default:
            {
                if (!request.Amount.HasValue || request.Amount.Value <= 0m)
                {
                    return (0m, string.Empty);
                }

                return (Math.Round(request.Amount.Value, 2), $"{(refundType == RefundType.Manual ? "Manual" : "Partial")} refund for {returnRequest.ReturnNumber}.");
            }
        }
    }

    private static bool CoversEntireOrder(ReturnRequest returnRequest, Order order)
    {
        var returned = returnRequest.Items
            .GroupBy(i => i.OrderItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        return order.Items.All(oi => returned.TryGetValue(oi.Id, out var qty) && qty >= oi.Quantity);
    }

    private async Task<Payment?> FindRefundablePaymentAsync(Guid orderId, CancellationToken cancellationToken) =>
        await _context.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == orderId &&
                        (p.State == PaymentState.Paid || p.State == PaymentState.PartiallyRefunded))
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private static void CompleteRefund(Refund refund, string? providerRefundId, DateTime now, string actorId)
    {
        refund.Status = RefundStatus.Succeeded;
        refund.CompletedAtUtc = now;
        refund.ProviderRefundId = providerRefundId;
        refund.Transactions.Add(new RefundTransaction
        {
            Type = "Succeeded",
            Succeeded = true,
            ResultMessage = "Refund completed.",
            CreatedBy = actorId,
            CreatedAtUtc = now
        });
    }

    private static string ResolveIdempotencyKey(ReturnRequest returnRequest, RefundReturnRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return $"{returnRequest.ReturnNumber}:{request.IdempotencyKey.Trim()}";
        }

        // Deterministic default: the same refund request replayed produces the same
        // key so a retried/duplicated submission cannot double-refund. The amount,
        // selected items and type fully describe the refund intent.
        var itemIds = request.ReturnItemIds is null || request.ReturnItemIds.Count == 0
            ? string.Empty
            : string.Join(",", request.ReturnItemIds.OrderBy(id => id).Select(id => id.ToString("N")));

        return $"{returnRequest.ReturnNumber}:{request.RefundType}:{itemIds}:{request.Amount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty}";
    }

    private async Task<string> GenerateUniqueRefundNumberAsync(CancellationToken cancellationToken)
    {
        var settings = _returnOptions.Value;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = $"{settings.RefundNumberPrefix}-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
            var exists = await _context.Refunds
                .AsNoTracking()
                .AnyAsync(r => r.ReferenceNumber == candidate, cancellationToken);
            if (!exists)
            {
                return candidate;
            }
        }

        return $"{settings.RefundNumberPrefix}-{Guid.NewGuid():N}".ToUpperInvariant();
    }

    private async Task<ReturnRequest?> LoadReturnAsync(Guid returnId, CancellationToken cancellationToken) =>
        await _context.ReturnRequests
            .AsNoTracking()
            .Include(r => r.Order)
            .Include(r => r.Items)
            .Include(r => r.StatusHistory)
            .Include(r => r.Attachments)
            .Include(r => r.ExchangeRequests)
            .Include(r => r.Refunds).ThenInclude(rf => rf.Transactions)
            .FirstOrDefaultAsync(r => r.Id == returnId, cancellationToken);

    private AdminReturnListItemDto BuildListItem(ReturnRequest returnRequest)
    {
        var orderNumber = returnRequest.Order?.PublicOrderNumber ?? string.Empty;
        var first = returnRequest.Items.OrderBy(i => i.Id).FirstOrDefault();

        return new AdminReturnListItemDto(
            returnRequest.Id,
            returnRequest.ReturnNumber,
            orderNumber,
            string.IsNullOrEmpty(returnRequest.UserId),
            returnRequest.CustomerName,
            returnRequest.GuestEmail,
            returnRequest.Currency,
            returnRequest.Status.ToString(),
            returnRequest.RefundableAmount,
            returnRequest.RefundedAmount,
            returnRequest.Items.Sum(i => i.Quantity),
            returnRequest.ReasonCode.ToString(),
            returnRequest.IsExchange,
            returnRequest.CreatedAtUtc,
            returnRequest.ReceivedAtUtc,
            returnRequest.CompletedAtUtc);
    }

    private ReturnDetailDto BuildDetail(ReturnRequest returnRequest)
    {
        var timeline = returnRequest.StatusHistory
            .OrderBy(h => h.CreatedAtUtc)
            .ThenBy(h => h.Id.ToString())
            .Select((h, index) => new ReturnTimelineEntryDto(
                index + 1,
                h.FromStatus?.ToString() ?? "—",
                h.ToStatus.ToString(),
                h.Note,
                h.CreatedBy,
                h.CreatedAtUtc))
            .ToList();

        return new ReturnDetailDto(
            returnRequest.Id,
            returnRequest.ReturnNumber,
            returnRequest.OrderId,
            returnRequest.Order?.PublicOrderNumber ?? string.Empty,
            string.IsNullOrEmpty(returnRequest.UserId),
            returnRequest.CustomerName,
            returnRequest.GuestEmail,
            returnRequest.Currency,
            returnRequest.Status.ToString(),
            returnRequest.ReasonCode.ToString(),
            returnRequest.CustomerNotes,
            returnRequest.IsExchange,
            returnRequest.RefundableAmount,
            returnRequest.RefundedAmount,
            returnRequest.TrackingNumber,
            returnRequest.CarrierCode,
            returnRequest.AdminNotes,
            returnRequest.CreatedAtUtc,
            returnRequest.ApprovedAtUtc,
            returnRequest.RejectedAtUtc,
            returnRequest.ReceivedAtUtc,
            returnRequest.InspectedAtUtc,
            returnRequest.RefundedAtUtc,
            returnRequest.CompletedAtUtc,
            returnRequest.RejectionNote,
            returnRequest.Resolution.ToString(),
            returnRequest.Items
                .OrderBy(i => i.Id)
                .Select(i => new ReturnItemDto(
                    i.Id,
                    i.OrderItemId,
                    i.ProductId,
                    i.ProductVariantId,
                    i.ProductName,
                    i.Sku,
                    i.ColourName,
                    i.ColourValue,
                    i.SizeName,
                    i.ImageUrl,
                    i.UnitPrice,
                    i.Discount,
                    i.Tax,
                    i.Quantity,
                    i.PurchasedQuantity,
                    i.RefundableAmount,
                    i.Condition.ToString(),
                    i.IsRestocked))
                .ToList(),
            returnRequest.Attachments
                .OrderBy(a => a.CreatedAtUtc)
                .Select(a => new ReturnAttachmentDto(
                    a.Id,
                    a.FileName,
                    a.OriginalFileName,
                    a.StoragePath,
                    a.ContentType,
                    a.SizeBytes,
                    a.CreatedAtUtc))
                .ToList(),
            timeline,
            returnRequest.ExchangeRequests
                .OrderBy(e => e.CreatedAtUtc)
                .Select(e => new ExchangeRequestDto(
                    e.Id,
                    e.ProductVariantId,
                    e.ProductName,
                    e.Sku,
                    e.Quantity,
                    e.UnitPrice,
                    e.Status.ToString(),
                    e.Notes,
                    e.CreatedAtUtc,
                    e.CompletedAtUtc))
                .ToList(),
            returnRequest.Refunds
                .OrderBy(rf => rf.CreatedAtUtc)
                .Select(rf => new RefundDto(
                    rf.Id,
                    rf.ReferenceNumber,
                    rf.Type.ToString(),
                    rf.Status.ToString(),
                    rf.Amount,
                    rf.Currency,
                    rf.IsGatewayRefund,
                    rf.ProviderRefundId,
                    rf.FailureReason,
                    rf.Reason,
                    rf.InitiatedBy,
                    rf.CreatedAtUtc,
                    rf.CompletedAtUtc,
                    rf.Transactions
                        .OrderBy(t => t.CreatedAtUtc)
                        .Select(t => new RefundTransactionDto(
                            t.Type,
                            t.Succeeded,
                            t.ResultCode,
                            t.ResultMessage,
                            t.CreatedAtUtc))
                        .ToList()))
                .ToList());
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
