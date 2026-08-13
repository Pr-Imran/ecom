using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Images;
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
/// The customer-facing return panel. Order reads are always scoped to the caller's
/// identity: signed-in customers see only their own orders and a verified guest
/// access ticket (validated against the order number by the controller) is required
/// for guest returns. Return creation is re-validated server-side against the order
/// snapshot and catalogue rules — return window, product-level restrictions, quantity
/// caps and duplicate completed-return prevention — and every lifecycle transition is
/// recorded in the return's status history.
/// </summary>
public sealed class CustomerReturnService : ICustomerReturnService
{
    private readonly AppDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly IOptions<ReturnSettings> _returnOptions;
    private readonly IEmailNotificationService _emailService;
    private readonly ILogger<CustomerReturnService> _logger;

    public CustomerReturnService(
        AppDbContext context,
        IFileStorageService fileStorage,
        IOptions<ReturnSettings> returnOptions,
        IEmailNotificationService emailService,
        ILogger<CustomerReturnService> logger)
    {
        _context = context;
        _fileStorage = fileStorage;
        _returnOptions = returnOptions;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<CustomerReturnListResultDto> GetCustomerReturnsAsync(
        string userId,
        CustomerReturnQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 10 : query.PageSize, 1, 50);

        var baseQuery = _context.ReturnRequests
            .AsNoTracking()
            .Where(r => r.UserId == userId);

        if (query.Status.HasValue)
        {
            baseQuery = baseQuery.Where(r => r.Status == query.Status.Value);
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var returns = await baseQuery
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(r => r.Order)
            .Include(r => r.Items)
            .ToListAsync(cancellationToken);

        var items = returns.Select(BuildListItem).ToList();

        return new CustomerReturnListResultDto(
            items,
            totalCount,
            page,
            pageSize,
            page * pageSize < totalCount);
    }

    public async Task<ReturnDetailDto?> GetReturnDetailAsync(
        string userId,
        string returnNumber,
        CancellationToken cancellationToken = default)
    {
        var returnRequest = await LoadReturnAsync(returnNumber, cancellationToken);
        if (returnRequest is null ||
            !string.Equals(returnRequest.UserId, userId, StringComparison.Ordinal))
        {
            return null;
        }

        return BuildDetail(returnRequest);
    }

    public async Task<ReturnDetailDto?> GetGuestReturnDetailAsync(
        string publicOrderNumber,
        string returnNumber,
        CancellationToken cancellationToken = default)
    {
        var returnRequest = await LoadReturnAsync(returnNumber, cancellationToken);
        if (returnRequest is null ||
            !string.IsNullOrEmpty(returnRequest.UserId) ||
            !string.Equals(returnRequest.Order?.PublicOrderNumber, publicOrderNumber, StringComparison.Ordinal))
        {
            return null;
        }

        return BuildDetail(returnRequest);
    }

    public async Task<CustomerReturnListResultDto> GetGuestReturnsAsync(
        string publicOrderNumber,
        CustomerReturnQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 10 : query.PageSize, 1, 50);

        var baseQuery = _context.ReturnRequests
            .AsNoTracking()
            .Where(r => r.Order!.PublicOrderNumber == publicOrderNumber &&
                        string.IsNullOrEmpty(r.UserId));

        if (query.Status.HasValue)
        {
            baseQuery = baseQuery.Where(r => r.Status == query.Status.Value);
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var returns = await baseQuery
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(r => r.Items)
            .ToListAsync(cancellationToken);

        var items = returns.Select(BuildListItem).ToList();

        return new CustomerReturnListResultDto(
            items,
            totalCount,
            page,
            pageSize,
            page * pageSize < totalCount);
    }

    public async Task<IReadOnlyList<ReturnReasonOptionDto>> GetReturnReasonsAsync(
        CancellationToken cancellationToken = default)
    {
        var reasons = await _context.ReturnReasons
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder)
            .ToListAsync(cancellationToken);

        return reasons.Select(r => new ReturnReasonOptionDto(
            r.Code.ToString(),
            r.Label,
            r.Description,
            r.RequiresPhoto,
            r.AllowShippingRefund)).ToList();
    }

    public async Task<ReturnOrderLookupDto> GetReturnableItemsAsync(
        string publicOrderNumber,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderForReturnAsync(publicOrderNumber, cancellationToken);
        if (order is null)
        {
            return new ReturnOrderLookupDto(publicOrderNumber, null, false, "Order not found.", Array.Empty<ReturnableItemDto>());
        }

        if (!CanAccessOrder(order, userId))
        {
            return new ReturnOrderLookupDto(publicOrderNumber, order.OrderStatus.ToString(), false, "You do not have access to this order.", Array.Empty<ReturnableItemDto>());
        }

        var settings = _returnOptions.Value;
        var reference = order.DeliveredAtUtc ?? order.CreatedAtUtc;
        var now = DateTime.UtcNow;
        var globalDeadline = reference.AddDays(settings.ReturnWindowDays);
        var withinWindow = now <= globalDeadline;

        var products = await LoadProductsAsync(order.Items, cancellationToken);
        var claimed = await LoadClaimedQuantitiesAsync(order.Items, cancellationToken);

        var items = new List<ReturnableItemDto>(order.Items.Count);
        foreach (var item in order.Items.OrderBy(i => i.Id))
        {
            var available = Math.Max(0, item.Quantity - (claimed.TryGetValue(item.Id, out var c) ? c : 0));
            var (isReturnable, restrictionReason) = EvaluateItemEligibility(item, products, reference, now, settings);

            var unitRefundable = PerUnitRefundable(item);
            items.Add(new ReturnableItemDto(
                item.Id,
                item.ProductId,
                item.ProductVariantId,
                item.ProductName,
                item.Sku,
                item.ColourName,
                item.ColourValue,
                item.SizeName,
                item.ImageUrl,
                item.UnitPrice,
                item.Discount,
                item.Tax,
                item.Quantity,
                Math.Max(0, available),
                isReturnable && available > 0,
                isReturnable ? (available > 0 ? null : "This item is already fully returned.") : restrictionReason,
                available > 0 && isReturnable ? Math.Round(unitRefundable * available, 2) : 0m));
        }

        var windowMessage = withinWindow
            ? null
            : $"Returns are accepted within {settings.ReturnWindowDays} days of delivery. Your order fell outside this window.";

        return new ReturnOrderLookupDto(
            order.PublicOrderNumber,
            order.OrderStatus.ToString(),
            withinWindow,
            windowMessage,
            items);
    }

    public async Task<CreateReturnResult> CreateReturnAsync(
        string publicOrderNumber,
        CreateReturnRequest request,
        string? userId,
        string? actorName,
        CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return new CreateReturnResult(false, null, "Select at least one item to return.");
        }

        var settings = _returnOptions.Value;

        if (!Enum.TryParse<ReturnReasonCode>(request.ReasonCode, ignoreCase: true, out var reasonCode))
        {
            return new CreateReturnResult(false, null, "Please choose a valid return reason.");
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                var order = await LoadOrderForReturnAsync(publicOrderNumber, cancellationToken);
                if (order is null)
                {
                    return new CreateReturnResult(false, null, "Order not found.");
                }

                if (!CanAccessOrder(order, userId))
                {
                    return new CreateReturnResult(false, null, "You do not have access to this order.");
                }

                var reference = order.DeliveredAtUtc ?? order.CreatedAtUtc;
                var now = DateTime.UtcNow;
                if (now > reference.AddDays(settings.ReturnWindowDays))
                {
                    return new CreateReturnResult(false, null, $"Returns are accepted within {settings.ReturnWindowDays} days of delivery. This order fell outside the window.");
                }

                var products = await LoadProductsAsync(order.Items, cancellationToken);
                var claimed = await LoadClaimedQuantitiesAsync(order.Items, cancellationToken);

                var validationError = ValidateSelection(order, request.Items, claimed, products, reference, now, settings);
                if (validationError is not null)
                {
                    return new CreateReturnResult(false, null, validationError);
                }

                var returnNumber = await GenerateUniqueReturnNumberAsync(cancellationToken);
                var returnRequest = new ReturnRequest
                {
                    ReturnNumber = returnNumber,
                    OrderId = order.Id,
                    UserId = order.UserId,
                    GuestEmail = order.GuestEmail,
                    CustomerName = order.CustomerName,
                    GuestPhone = order.GuestPhone,
                    Currency = order.Currency,
                    Status = ReturnStatus.Requested,
                    ReasonCode = reasonCode,
                    CustomerNotes = NormalizeOptional(request.Notes, 2000),
                    IsExchange = request.IsExchange,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                var orderItems = order.Items.ToDictionary(i => i.Id);
                decimal refundableTotal = 0m;
                foreach (var selection in request.Items)
                {
                    var orderItem = orderItems[selection.OrderItemId];
                    var unitRefundable = PerUnitRefundable(orderItem);
                    var lineRefundable = Math.Round(unitRefundable * selection.Quantity, 2);
                    refundableTotal += lineRefundable;

                    returnRequest.Items.Add(new ReturnItem
                    {
                        OrderItemId = orderItem.Id,
                        ProductId = orderItem.ProductId,
                        ProductVariantId = orderItem.ProductVariantId,
                        ProductName = orderItem.ProductName,
                        Sku = orderItem.Sku,
                        ColourName = orderItem.ColourName,
                        ColourValue = orderItem.ColourValue,
                        SizeName = orderItem.SizeName,
                        ImageUrl = orderItem.ImageUrl,
                        UnitPrice = orderItem.UnitPrice,
                        Discount = orderItem.Discount,
                        Tax = orderItem.Tax,
                        Quantity = selection.Quantity,
                        PurchasedQuantity = orderItem.Quantity,
                        RefundableAmount = lineRefundable,
                        Condition = ReturnItemCondition.Undetermined
                    });
                }

                returnRequest.RefundableAmount = Math.Round(refundableTotal, 2);
                returnRequest.StatusHistory.Add(new ReturnStatusHistory
                {
                    FromStatus = null,
                    ToStatus = ReturnStatus.Requested,
                    Note = request.IsExchange
                        ? $"Return request created as an exchange ({reasonCode})."
                        : $"Return request created ({reasonCode}).",
                    CreatedBy = actorName ?? "customer",
                    CreatedAtUtc = now
                });

                _context.ReturnRequests.Add(returnRequest);
                await _context.SaveChangesAsync(cancellationToken);

                await _emailService.SendReturnRequestedAsync(returnRequest.Id, cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "Return {ReturnNumber} created against order {OrderNumber} for {ItemCount} line(s), refundable {Amount} {Currency}",
                    returnNumber,
                    publicOrderNumber,
                    returnRequest.Items.Count,
                    returnRequest.RefundableAmount,
                    returnRequest.Currency);

                return new CreateReturnResult(true, returnNumber, null);
            });
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("Duplicate return number generated for order {OrderNumber}; retrying", publicOrderNumber);
            return new CreateReturnResult(false, null, "We could not create your return right now. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Return creation failed for order {OrderNumber}", publicOrderNumber);
            return new CreateReturnResult(false, null, "We could not create your return right now. Please try again or contact support.");
        }
    }

    public async Task<ReturnAttachmentUploadResult> UploadAttachmentsAsync(
        string returnNumber,
        string? userId,
        string? actorName,
        IReadOnlyList<ReturnAttachmentInput> files,
        CancellationToken cancellationToken = default)
    {
        var returnRequest = await _context.ReturnRequests
            .Include(r => r.Attachments)
            .FirstOrDefaultAsync(r => r.ReturnNumber == returnNumber, cancellationToken);

        if (returnRequest is null)
        {
            return new ReturnAttachmentUploadResult(false, null, null, "Return request not found.");
        }

        if (!OwnsReturn(returnRequest, userId))
        {
            return new ReturnAttachmentUploadResult(false, null, null, "You do not have access to this return.");
        }

        if (returnRequest.Status is ReturnStatus.Rejected or ReturnStatus.Closed)
        {
            return new ReturnAttachmentUploadResult(false, null, null, "This return can no longer accept attachments.");
        }

        var settings = _returnOptions.Value;
        if (files is null || files.Count == 0)
        {
            return new ReturnAttachmentUploadResult(false, null, null, "Choose at least one photo.");
        }

        if (returnRequest.Attachments.Count + files.Count > settings.MaxAttachments)
        {
            return new ReturnAttachmentUploadResult(false, null, null, $"You can attach up to {settings.MaxAttachments} photos.");
        }

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!settings.AllowedAttachmentExtensions.Contains(extension))
            {
                return new ReturnAttachmentUploadResult(false, null, null, $"Only {string.Join(", ", settings.AllowedAttachmentExtensions)} photos are accepted.");
            }

            if (file.SizeBytes <= 0 || file.SizeBytes > settings.MaxAttachmentBytes)
            {
                return new ReturnAttachmentUploadResult(false, null, null, "Each photo must be smaller than 5 MB.");
            }
        }

        var now = DateTime.UtcNow;
        var saved = new List<(ReturnAttachment attachment, string relativePath)>();

        try
        {
            foreach (var file in files)
            {
                var safeName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
                var relativePath = $"returns/{returnRequest.Id}/{safeName}";
                var stored = await _fileStorage.SaveAsync(
                    relativePath,
                    file.Content,
                    string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType,
                    cancellationToken);

                saved.Add((new ReturnAttachment
                {
                    ReturnRequestId = returnRequest.Id,
                    FileName = safeName,
                    OriginalFileName = Path.GetFileName(file.FileName),
                    ContentType = file.ContentType,
                    SizeBytes = stored.SizeBytes,
                    StoragePath = stored.RelativePath,
                    UploadedBy = actorName ?? "customer",
                    CreatedAtUtc = now
                }, stored.RelativePath));
            }

            foreach (var (attachment, _) in saved)
            {
                _context.ReturnAttachments.Add(attachment);
            }

            await _context.SaveChangesAsync(cancellationToken);

            var first = saved[0].attachment;
            var url = _fileStorage.ResolveUrl(first.StoragePath);
            _logger.LogInformation(
                "Uploaded {Count} attachment(s) for return {ReturnNumber}",
                saved.Count,
                returnNumber);

            return new ReturnAttachmentUploadResult(true, first.Id, url, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attachment upload failed for return {ReturnNumber}", returnNumber);
            return new ReturnAttachmentUploadResult(false, null, null, "We could not upload the photos. Please try again.");
        }
    }

    public async Task<ReturnTransitionResult> MarkShippedAsync(
        string returnNumber,
        string? carrierCode,
        string? trackingNumber,
        string? userId,
        string? actorName,
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
                    .FirstOrDefaultAsync(r => r.ReturnNumber == returnNumber, cancellationToken);

                if (returnRequest is null)
                {
                    return new ReturnTransitionResult(false, null, null, "Return request not found.");
                }

                if (!OwnsReturn(returnRequest, userId))
                {
                    return new ReturnTransitionResult(false, null, null, "You do not have access to this return.");
                }

                if (returnRequest.Status is not (ReturnStatus.Approved or ReturnStatus.AwaitingShipment))
                {
                    return new ReturnTransitionResult(false, null, null, "You can only add tracking details once the return is approved.");
                }

                if (string.IsNullOrWhiteSpace(trackingNumber))
                {
                    return new ReturnTransitionResult(false, null, null, "Enter the courier tracking number.");
                }

                var now = DateTime.UtcNow;
                var previous = returnRequest.Status;
                returnRequest.Status = ReturnStatus.InTransit;
                returnRequest.TrackingNumber = trackingNumber.Trim();
                returnRequest.CarrierCode = NormalizeOptional(carrierCode, 50);
                returnRequest.UpdatedAtUtc = now;

                _context.ReturnStatusHistories.Add(new ReturnStatusHistory
                {
                    ReturnRequestId = returnRequest.Id,
                    FromStatus = previous,
                    ToStatus = ReturnStatus.InTransit,
                    Note = $"Return shipped back to us ({trackingNumber.Trim()}).",
                    CreatedBy = actorName ?? "customer",
                    CreatedAtUtc = now
                });

                await _context.SaveChangesAsync(cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation("Return {ReturnNumber} marked in-transit with tracking {Tracking}", returnNumber, trackingNumber);

                return new ReturnTransitionResult(true, returnRequest.ReturnNumber, returnRequest.Status.ToString(), null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Marking return {ReturnNumber} as shipped failed", returnNumber);
            return new ReturnTransitionResult(false, null, null, "We could not record the tracking details. Please try again.");
        }
    }

    public async Task<ReturnTransitionResult> CancelAsync(
        string returnNumber,
        string? userId,
        string? actorName,
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
                    .FirstOrDefaultAsync(r => r.ReturnNumber == returnNumber, cancellationToken);

                if (returnRequest is null)
                {
                    return new ReturnTransitionResult(false, null, null, "Return request not found.");
                }

                if (!OwnsReturn(returnRequest, userId))
                {
                    return new ReturnTransitionResult(false, null, null, "You do not have access to this return.");
                }

                if (returnRequest.Status is not (
                    ReturnStatus.Requested or
                    ReturnStatus.UnderReview or
                    ReturnStatus.Approved or
                    ReturnStatus.AwaitingShipment))
                {
                    return new ReturnTransitionResult(false, null, null, "This return can no longer be withdrawn.");
                }

                var now = DateTime.UtcNow;
                var previous = returnRequest.Status;
                returnRequest.Status = ReturnStatus.Closed;
                returnRequest.IsWithdrawn = true;
                returnRequest.CompletedAtUtc = now;
                returnRequest.UpdatedAtUtc = now;

                _context.ReturnStatusHistories.Add(new ReturnStatusHistory
                {
                    ReturnRequestId = returnRequest.Id,
                    FromStatus = previous,
                    ToStatus = ReturnStatus.Closed,
                    Note = "Return withdrawn by customer.",
                    CreatedBy = actorName ?? "customer",
                    CreatedAtUtc = now
                });

                await _context.SaveChangesAsync(cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation("Return {ReturnNumber} withdrawn by customer", returnNumber);

                return new ReturnTransitionResult(true, returnRequest.ReturnNumber, returnRequest.Status.ToString(), null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Withdrawing return {ReturnNumber} failed", returnNumber);
            return new ReturnTransitionResult(false, null, null, "We could not withdraw the return. Please try again.");
        }
    }

    // ---- Helpers ----

    private async Task<Order?> LoadOrderForReturnAsync(string publicOrderNumber, CancellationToken cancellationToken) =>
        await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.PublicOrderNumber == publicOrderNumber, cancellationToken);

    private async Task<ReturnRequest?> LoadReturnAsync(string returnNumber, CancellationToken cancellationToken) =>
        await _context.ReturnRequests
            .AsNoTracking()
            .Include(r => r.Order)
            .Include(r => r.Items)
            .Include(r => r.StatusHistory)
            .Include(r => r.Attachments)
            .Include(r => r.ExchangeRequests)
            .Include(r => r.Refunds).ThenInclude(rf => rf.Transactions)
            .FirstOrDefaultAsync(r => r.ReturnNumber == returnNumber, cancellationToken);

    private static bool CanAccessOrder(Order order, string? userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return string.IsNullOrEmpty(order.UserId);
        }

        return string.Equals(order.UserId, userId, StringComparison.Ordinal);
    }

    private static bool OwnsReturn(ReturnRequest returnRequest, string? userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return string.IsNullOrEmpty(returnRequest.UserId);
        }

        return string.Equals(returnRequest.UserId, userId, StringComparison.Ordinal);
    }

    private async Task<Dictionary<Guid, Product>> LoadProductsAsync(
        IEnumerable<OrderItem> items,
        CancellationToken cancellationToken)
    {
        var productIds = items
            .Where(i => i.ProductId.HasValue)
            .Select(i => i.ProductId!.Value)
            .Distinct()
            .ToList();

        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, Product>();
        }

        return await _context.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }

    private async Task<Dictionary<Guid, int>> LoadClaimedQuantitiesAsync(
        IEnumerable<OrderItem> items,
        CancellationToken cancellationToken)
    {
        var orderItemIds = items.Select(i => i.Id).ToList();
        if (orderItemIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var claims = await _context.ReturnItems
            .AsNoTracking()
            .Where(ri => orderItemIds.Contains(ri.OrderItemId) &&
                         !ri.ReturnRequest!.IsWithdrawn &&
                         ri.ReturnRequest.Status != ReturnStatus.Rejected)
            .Select(ri => new { ri.OrderItemId, ri.Quantity })
            .ToListAsync(cancellationToken);

        return claims
            .GroupBy(c => c.OrderItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
    }

    private static (bool IsReturnable, string? RestrictionReason) EvaluateItemEligibility(
        OrderItem item,
        IReadOnlyDictionary<Guid, Product> products,
        DateTime reference,
        DateTime now,
        ReturnSettings settings)
    {
        if (!item.ProductId.HasValue || !products.TryGetValue(item.ProductId.Value, out var product))
        {
            return (false, "This item is no longer available in our catalogue.");
        }

        if (!product.IsReturnable)
        {
            return (false, "This product is not eligible for returns.");
        }

        var windowDays = product.ReturnWindowDays ?? settings.ReturnWindowDays;
        if (now > reference.AddDays(windowDays))
        {
            return (false, $"This item can only be returned within {windowDays} days of delivery.");
        }

        return (true, null);
    }

    private static string? ValidateSelection(
        Order order,
        IReadOnlyList<ReturnItemSelectionDto> selections,
        Dictionary<Guid, int> claimed,
        IReadOnlyDictionary<Guid, Product> products,
        DateTime reference,
        DateTime now,
        ReturnSettings settings)
    {
        var orderItems = order.Items.ToDictionary(i => i.Id);

        foreach (var selection in selections)
        {
            if (selection.Quantity < 1)
            {
                return "Each returned quantity must be at least one.";
            }

            if (!orderItems.TryGetValue(selection.OrderItemId, out var orderItem))
            {
                return "One of the selected items is no longer part of this order.";
            }

            var available = orderItem.Quantity - (claimed.TryGetValue(orderItem.Id, out var c) ? c : 0);
            if (selection.Quantity > available)
            {
                return $"You can return up to {available} of \"{orderItem.ProductName}\".";
            }

            var (isReturnable, restrictionReason) = EvaluateItemEligibility(orderItem, products, reference, now, settings);
            if (!isReturnable)
            {
                return restrictionReason ?? $"\"{orderItem.ProductName}\" cannot be returned.";
            }
        }

        return null;
    }

    private static decimal PerUnitRefundable(OrderItem item)
    {
        if (item.Quantity <= 0)
        {
            return 0m;
        }

        var linePaid = (item.UnitPrice * item.Quantity) - item.Discount + item.Tax;
        return linePaid / item.Quantity;
    }

    private async Task<string> GenerateUniqueReturnNumberAsync(CancellationToken cancellationToken)
    {
        var settings = _returnOptions.Value;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = $"{settings.ReturnNumberPrefix}-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
            var exists = await _context.ReturnRequests
                .AsNoTracking()
                .AnyAsync(r => r.ReturnNumber == candidate, cancellationToken);
            if (!exists)
            {
                return candidate;
            }
        }

        return $"{settings.ReturnNumberPrefix}-{Guid.NewGuid():N}".ToUpperInvariant();
    }

    private CustomerReturnListItemDto BuildListItem(ReturnRequest returnRequest)
    {
        var first = returnRequest.Items.OrderBy(i => i.Id).FirstOrDefault();

        return new CustomerReturnListItemDto(
            returnRequest.Id,
            returnRequest.ReturnNumber,
            returnRequest.Order?.PublicOrderNumber ?? string.Empty,
            returnRequest.Status.ToString(),
            returnRequest.RefundableAmount,
            returnRequest.RefundedAmount,
            returnRequest.Currency,
            returnRequest.Items.Sum(i => i.Quantity),
            first?.ImageUrl,
            first?.ProductName ?? "Return",
            returnRequest.IsExchange,
            returnRequest.CreatedAtUtc,
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

        var attachments = returnRequest.Attachments
            .OrderBy(a => a.CreatedAtUtc)
            .Select(a => new ReturnAttachmentDto(
                a.Id,
                a.FileName,
                a.OriginalFileName,
                _fileStorage.ResolveUrl(a.StoragePath),
                a.ContentType,
                a.SizeBytes,
                a.CreatedAtUtc))
            .ToList();

        var exchanges = returnRequest.ExchangeRequests
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
            .ToList();

        var refunds = returnRequest.Refunds
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
            attachments,
            timeline,
            exchanges,
            refunds);
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
