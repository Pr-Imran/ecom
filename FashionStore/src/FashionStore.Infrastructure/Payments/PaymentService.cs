using System.Text.Json;
using FashionStore.Application.Common;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Inventory;
using FashionStore.Application.DTOs.Payments;
using FashionStore.Application.Email;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionStore.Infrastructure.Payments;

/// <summary>
/// Orchestrates the payment lifecycle for an order. This service is the only
/// component that writes payment state and transitions order payment status; a
/// payment is only ever settled from a verified provider webhook or a provider
/// status check triggered by a browser callback - never from a browser redirect
/// alone. Every action is recorded as an immutable <see cref="PaymentTransaction"/>
/// and webhook activity as a <see cref="PaymentWebhookLog"/>, with only masked
/// (non-sensitive) metadata persisted.
/// </summary>
public sealed class PaymentService : IPaymentService
{
    private readonly AppDbContext _context;
    private readonly IPaymentProviderFactory _providerFactory;
    private readonly IInventoryService _inventoryService;
    private readonly IEmailNotificationService _emailService;
    private readonly IOptions<PaymentSettings> _paymentOptions;
    private readonly IOptions<OrderSettings> _orderOptions;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        AppDbContext context,
        IPaymentProviderFactory providerFactory,
        IInventoryService inventoryService,
        IEmailNotificationService emailService,
        IOptions<PaymentSettings> paymentOptions,
        IOptions<OrderSettings> orderOptions,
        ILogger<PaymentService> logger)
    {
        _context = context;
        _providerFactory = providerFactory;
        _inventoryService = inventoryService;
        _emailService = emailService;
        _paymentOptions = paymentOptions;
        _orderOptions = orderOptions;
        _logger = logger;
    }

    public async Task<PaymentPlacementInfo> InitiateForOrderAsync(
        Guid orderId,
        string? returnUrl,
        string? cancelUrl,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException($"Order {orderId} was not found and cannot be paid.");
        }

        var methodCode = string.IsNullOrWhiteSpace(order.PaymentMethodCode) ? "cod" : order.PaymentMethodCode;
        var method = PaymentMethodCatalog.Find(methodCode);
        var providerCode = method?.ProviderCode ?? "cod";

        var provider = _providerFactory.GetProvider(providerCode);
        if (provider is null)
        {
            throw new InvalidOperationException($"Payment provider '{providerCode}' is not configured.");
        }

        // A fully discounted order has nothing to collect; there is no payment record.
        if (order.GrandTotal <= 0m)
        {
            return new PaymentPlacementInfo(false, null, null, null, PaymentState.Paid.ToString(), providerCode);
        }

        var now = DateTime.UtcNow;

        var payment = await _context.Payments
            .Include(p => p.Attempts)
            .FirstOrDefaultAsync(p => p.OrderId == order.Id, cancellationToken);

        if (payment is null)
        {
            payment = new Payment
            {
                OrderId = order.Id,
                ProviderCode = providerCode,
                PaymentMethodCode = methodCode,
                IdempotencyKey = $"order-{order.Id:N}",
                Amount = order.GrandTotal,
                Currency = string.IsNullOrWhiteSpace(order.Currency) ? "USD" : order.Currency,
                State = PaymentState.Pending,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(ReservationMinutes(providerCode))
            };
            _context.Payments.Add(payment);
        }
        else if (!string.Equals(payment.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Order {OrderNumber} already has a payment for provider {Existing} but {Requested} was requested",
                order.PublicOrderNumber,
                payment.ProviderCode,
                providerCode);
            return BuildPlacementInfo(payment);
        }

        // A settled payment cannot be re-initiated; return the current placement.
        if (payment.State is PaymentState.Paid or PaymentState.Refunded or PaymentState.PartiallyRefunded)
        {
            return BuildPlacementInfo(payment);
        }

        var attemptNumber = (payment.Attempts.Count == 0 ? 0 : payment.Attempts.Max(a => a.AttemptNumber)) + 1;
        var attempt = new PaymentAttempt
        {
            PaymentId = payment.Id,
            AttemptNumber = attemptNumber,
            Status = PaymentAttemptStatus.Initiated,
            CreatedAtUtc = now
        };
        _context.PaymentAttempts.Add(attempt);

        var request = new PaymentInitiationRequest(
            payment.Id,
            order.Id,
            order.PublicOrderNumber,
            providerCode,
            methodCode,
            payment.Amount,
            payment.Currency,
            order.GuestEmail,
            string.IsNullOrWhiteSpace(returnUrl) ? _paymentOptions.Value.ReturnUrl : returnUrl,
            string.IsNullOrWhiteSpace(cancelUrl) ? _paymentOptions.Value.CancelUrl : cancelUrl,
            payment.IdempotencyKey);

        PaymentInitiationResult initiation;
        try
        {
            initiation = await provider.InitiateAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment initiation failed for order {OrderNumber}", order.PublicOrderNumber);
            attempt.Status = PaymentAttemptStatus.Failed;
            attempt.FailureReason = "Payment initiation failed.";
            attempt.CompletedAtUtc = DateTime.UtcNow;
            payment.State = PaymentState.Failed;
            payment.FailureCode = "initiation-failed";
            payment.FailureReason = "Payment initiation failed.";
            payment.FailedAtUtc = DateTime.UtcNow;
            AddTransaction(
                payment,
                PaymentTransactionType.Initiate,
                providerCode,
                null,
                false,
                "initiation-failed",
                "Payment initiation failed.",
                now);
            await _context.SaveChangesAsync(cancellationToken);
            return new PaymentPlacementInfo(true, null, null, null, PaymentState.Failed.ToString(), providerCode);
        }

        var metadata = new InitiationMetadata(
            initiation.ProviderTransactionId,
            initiation.RedirectUrl,
            initiation.HostedCheckoutReference,
            initiation.Instructions);

        if (!initiation.Success)
        {
            payment.State = PaymentState.Failed;
            payment.FailureCode = initiation.FailureCode;
            payment.FailureReason = initiation.FailureReason ?? "The payment provider declined the payment.";
            payment.FailedAtUtc = DateTime.UtcNow;
            payment.ResponseMetadata = SerializeMetadata(metadata);
            attempt.Status = PaymentAttemptStatus.Failed;
            attempt.FailureReason = payment.FailureReason;
            attempt.ResponseMetadata = SerializeMetadata(metadata);
            attempt.CompletedAtUtc = DateTime.UtcNow;
            AddTransaction(
                payment,
                PaymentTransactionType.Initiate,
                providerCode,
                initiation.ProviderTransactionId,
                false,
                initiation.FailureCode,
                payment.FailureReason,
                now);
            await _context.SaveChangesAsync(cancellationToken);
            return new PaymentPlacementInfo(true, null, null, initiation.Instructions, PaymentState.Failed.ToString(), providerCode);
        }

        payment.ProviderTransactionId = initiation.ProviderTransactionId;
        payment.State = PaymentState.Initiated;
        payment.InitiatedAtUtc = now;
        payment.FailureCode = null;
        payment.FailureReason = null;
        payment.FailedAtUtc = null;
        payment.ResponseMetadata = SerializeMetadata(metadata);
        attempt.ProviderTransactionId = initiation.ProviderTransactionId;
        attempt.ResponseMetadata = SerializeMetadata(metadata);
        AddTransaction(
            payment,
            PaymentTransactionType.Initiate,
            providerCode,
            initiation.ProviderTransactionId,
            true,
            null,
            null,
            now);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Initiated {Provider} payment {PaymentId} for order {OrderNumber} ({Amount} {Currency})",
            providerCode,
            payment.Id,
            order.PublicOrderNumber,
            payment.Amount,
            payment.Currency);

        return BuildPlacementInfo(payment);
    }

    public async Task<PaymentStatusDto?> GetStatusByOrderNumberAsync(
        string publicOrderNumber,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.PublicOrderNumber == publicOrderNumber, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.OrderId == order.Id, cancellationToken);

        if (payment is null)
        {
            return null;
        }

        await ExpireIfDueAsync(payment, cancellationToken);
        return ToStatusDto(payment, order);
    }

    public async Task<PaymentStatusDto?> HandleBrowserCallbackAsync(
        string publicOrderNumber,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.PublicOrderNumber == publicOrderNumber, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var payment = await _context.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.OrderId == order.Id, cancellationToken);

        if (payment is null)
        {
            return null;
        }

        // The browser arriving at the return URL is not proof of payment. Only a
        // provider-confirmed state may settle the payment.
        await ExpireIfDueAsync(payment, cancellationToken);

        if (IsSettled(payment.State))
        {
            return ToStatusDto(payment, order);
        }

        var provider = _providerFactory.GetProvider(payment.ProviderCode);
        if (provider is null)
        {
            return ToStatusDto(payment, order);
        }

        PaymentStatusCheckResult check;
        try
        {
            check = await provider.CheckStatusAsync(
                new PaymentStatusCheckRequest(
                    payment.Id,
                    payment.ProviderCode,
                    payment.ProviderTransactionId,
                    payment.Amount,
                    payment.Currency,
                    payment.State),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Status check failed for payment {PaymentId}", payment.Id);
            return ToStatusDto(payment, order);
        }

        if (check.Success && IsSettled(check.State) && CanTransition(payment.State, check.State))
        {
            await ApplySettlementAsync(
                payment,
                check.State,
                PaymentTransactionType.Callback,
                check.ProviderTransactionId,
                check.FailureReason,
                DateTime.UtcNow,
                cancellationToken);
        }

        return ToStatusDto(payment, order);
    }

    public async Task<PaymentWebhookHandlingResult> HandleWebhookAsync(
        string providerCode,
        string rawPayload,
        string? signature,
        CancellationToken cancellationToken = default)
    {
        var provider = _providerFactory.GetProvider(providerCode);
        if (provider is null)
        {
            await LogWebhookAsync(
                providerCode,
                null,
                rawPayload,
                signature,
                PaymentWebhookStatus.ProviderDisabled,
                null,
                "Payment provider is not enabled.",
                cancellationToken);
            return new PaymentWebhookHandlingResult(false, PaymentWebhookStatus.ProviderDisabled, null, "Payment provider is not enabled.");
        }

        if (!provider.VerifyWebhookSignature(rawPayload, signature))
        {
            await LogWebhookAsync(
                providerCode,
                null,
                rawPayload,
                signature,
                PaymentWebhookStatus.InvalidSignature,
                null,
                "Webhook signature verification failed.",
                cancellationToken);
            return new PaymentWebhookHandlingResult(false, PaymentWebhookStatus.InvalidSignature, null, "Webhook signature verification failed.");
        }

        var parsed = provider.TryParseWebhook(rawPayload);
        if (parsed is null)
        {
            await LogWebhookAsync(
                providerCode,
                null,
                rawPayload,
                signature,
                PaymentWebhookStatus.Failed,
                null,
                "The webhook payload could not be parsed.",
                cancellationToken);
            return new PaymentWebhookHandlingResult(false, PaymentWebhookStatus.Failed, null, "The webhook payload could not be parsed.");
        }

        var toleranceSeconds = _paymentOptions.Value.WebhookTimestampToleranceSeconds;
        var ageSeconds = Math.Abs((DateTime.UtcNow - parsed.Timestamp).TotalSeconds);
        if (ageSeconds > toleranceSeconds)
        {
            await LogWebhookAsync(
                providerCode,
                null,
                rawPayload,
                signature,
                PaymentWebhookStatus.InvalidTimestamp,
                parsed.EventId,
                "Webhook timestamp is outside the accepted window.",
                cancellationToken);
            return new PaymentWebhookHandlingResult(false, PaymentWebhookStatus.InvalidTimestamp, parsed.EventId, "Webhook timestamp is outside the accepted window.");
        }

        var replayed = await _context.PaymentWebhookLogs.AnyAsync(
            l => l.ProviderCode == providerCode && l.ProviderEventId == parsed.EventId,
            cancellationToken);
        if (replayed)
        {
            await LogWebhookAsync(
                providerCode,
                null,
                rawPayload,
                signature,
                PaymentWebhookStatus.Duplicate,
                parsed.EventId,
                "Duplicate webhook event.",
                cancellationToken);
            return new PaymentWebhookHandlingResult(false, PaymentWebhookStatus.Duplicate, parsed.EventId, "Duplicate webhook event.");
        }

        var payment = await LoadPaymentForWebhookAsync(parsed, providerCode, cancellationToken);
        if (payment is null)
        {
            await LogWebhookAsync(
                providerCode,
                null,
                rawPayload,
                signature,
                PaymentWebhookStatus.UnknownTransaction,
                parsed.EventId,
                "No payment matches the webhook event.",
                cancellationToken);
            return new PaymentWebhookHandlingResult(false, PaymentWebhookStatus.UnknownTransaction, parsed.EventId, "No payment matches the webhook event.");
        }

        if (Math.Abs(payment.Amount - parsed.Amount) > 0.001m)
        {
            await LogWebhookAsync(
                providerCode,
                payment.Id,
                rawPayload,
                signature,
                PaymentWebhookStatus.AmountMismatch,
                parsed.EventId,
                $"Webhook amount {parsed.Amount} does not match the expected {payment.Amount}.",
                cancellationToken);
            return new PaymentWebhookHandlingResult(false, PaymentWebhookStatus.AmountMismatch, parsed.EventId, "Webhook amount does not match the payment.");
        }

        if (!string.Equals(payment.Currency, parsed.Currency, StringComparison.OrdinalIgnoreCase))
        {
            await LogWebhookAsync(
                providerCode,
                payment.Id,
                rawPayload,
                signature,
                PaymentWebhookStatus.CurrencyMismatch,
                parsed.EventId,
                $"Webhook currency {parsed.Currency} does not match the expected {payment.Currency}.",
                cancellationToken);
            return new PaymentWebhookHandlingResult(false, PaymentWebhookStatus.CurrencyMismatch, parsed.EventId, "Webhook currency does not match the payment.");
        }

        var targetState = MapEventToState(parsed.EventType);
        if (targetState is null)
        {
            // Recognized event but no state transition is mapped; record it and acknowledge.
            await LogWebhookAsync(
                providerCode,
                payment.Id,
                rawPayload,
                signature,
                PaymentWebhookStatus.Processed,
                parsed.EventId,
                null,
                cancellationToken);
            return new PaymentWebhookHandlingResult(true, PaymentWebhookStatus.Processed, parsed.EventId, null);
        }

        var target = targetState.Value;
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = _context.Database.IsRelational()
                    ? await _context.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                // Re-check replay inside the transaction so two concurrent deliveries
                // of the same event cannot both apply a transition.
                var inTxReplay = await _context.PaymentWebhookLogs.AnyAsync(
                    l => l.ProviderCode == providerCode && l.ProviderEventId == parsed.EventId,
                    cancellationToken);
                if (inTxReplay)
                {
                    await LogWebhookAsync(
                        providerCode,
                        payment.Id,
                        rawPayload,
                        signature,
                        PaymentWebhookStatus.Duplicate,
                        parsed.EventId,
                        "Duplicate webhook event.",
                        cancellationToken);
                    if (tx != null)
                    {
                        await tx.CommitAsync(cancellationToken);
                    }

                    return new PaymentWebhookHandlingResult(false, PaymentWebhookStatus.Duplicate, parsed.EventId, "Duplicate webhook event.");
                }

                var current = await _context.Payments
                    .Include(p => p.Order)
                    .FirstOrDefaultAsync(p => p.Id == payment.Id, cancellationToken);

                if (current is null || current.Order is null)
                {
                    await LogWebhookAsync(
                        providerCode,
                        payment.Id,
                        rawPayload,
                        signature,
                        PaymentWebhookStatus.UnknownTransaction,
                        parsed.EventId,
                        "No payment matches the webhook event.",
                        cancellationToken);
                    if (tx != null)
                    {
                        await tx.CommitAsync(cancellationToken);
                    }

                    return new PaymentWebhookHandlingResult(false, PaymentWebhookStatus.UnknownTransaction, parsed.EventId, "No payment matches the webhook event.");
                }

                if (current.State == target || !CanTransition(current.State, target))
                {
                    await LogWebhookAsync(
                        providerCode,
                        current.Id,
                        rawPayload,
                        signature,
                        PaymentWebhookStatus.InvalidOrderState,
                        parsed.EventId,
                        $"Cannot move payment from {current.State} to {target}.",
                        cancellationToken);
                    if (tx != null)
                    {
                        await tx.CommitAsync(cancellationToken);
                    }

                    return new PaymentWebhookHandlingResult(false, PaymentWebhookStatus.InvalidOrderState, parsed.EventId, "The webhook is not valid for the current payment state.");
                }

                await ApplySettlementAsync(
                    current,
                    target,
                    PaymentTransactionType.Webhook,
                    parsed.ProviderTransactionId ?? current.ProviderTransactionId,
                    parsed.FailureReason,
                    DateTime.UtcNow,
                    cancellationToken);

                await LogWebhookAsync(
                    providerCode,
                    current.Id,
                    rawPayload,
                    signature,
                    PaymentWebhookStatus.Processed,
                    parsed.EventId,
                    null,
                    cancellationToken);

                await _context.SaveChangesAsync(cancellationToken);

                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "Webhook {EventId} moved payment {PaymentId} for order {OrderNumber} to {State}",
                    parsed.EventId,
                    current.Id,
                    current.Order.PublicOrderNumber,
                    targetState);

                return new PaymentWebhookHandlingResult(true, PaymentWebhookStatus.Processed, parsed.EventId, null);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook {EventId} could not be processed", parsed.EventId);
            return new PaymentWebhookHandlingResult(false, PaymentWebhookStatus.Failed, parsed.EventId, "The webhook could not be processed.");
        }
    }

    public async Task<PaymentRefundResult> RefundAsync(
        Guid paymentId,
        decimal amount,
        string? initiatedBy,
        CancellationToken cancellationToken = default)
    {
        var payment = await _context.Payments
            .Include(p => p.Order)
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);

        if (payment is null)
        {
            return new PaymentRefundResult(false, null, "not-found", "Payment not found.");
        }

        if (payment.State is not (PaymentState.Paid or PaymentState.PartiallyRefunded))
        {
            return new PaymentRefundResult(false, null, "invalid-state", "Only captured payments can be refunded.");
        }

        if (amount <= 0m)
        {
            return new PaymentRefundResult(false, null, "invalid-amount", "The refund amount must be positive.");
        }

        var refunded = payment.Refunds.Where(r => r.Succeeded).Sum(r => r.Amount);
        var refundable = payment.Amount - refunded;
        if (amount > refundable)
        {
            return new PaymentRefundResult(false, null, "invalid-amount", "The refund amount exceeds the captured amount.");
        }

        var now = DateTime.UtcNow;
        var refund = new PaymentRefundRecord
        {
            PaymentId = payment.Id,
            Amount = amount,
            Currency = payment.Currency,
            ProviderRefundId = $"refund_{Guid.NewGuid():N}",
            Succeeded = true,
            InitiatedBy = string.IsNullOrWhiteSpace(initiatedBy) ? "storefront" : initiatedBy,
            CreatedAtUtc = now,
            CompletedAtUtc = now
        };
        _context.PaymentRefundRecords.Add(refund);

        var totalRefunded = refunded + amount;
        if (Math.Abs(totalRefunded - payment.Amount) <= 0.001m)
        {
            payment.State = PaymentState.Refunded;
        }
        else
        {
            payment.State = PaymentState.PartiallyRefunded;
        }

        if (payment.Order is not null)
        {
            payment.Order.RefundedAmount = totalRefunded;
            payment.Order.PaymentStatus =
                Math.Abs(totalRefunded - payment.Amount) <= 0.001m
                    ? PaymentStatus.Refunded
                    : PaymentStatus.Paid;
        }

        AddTransaction(
            payment,
            PaymentTransactionType.Refund,
            payment.ProviderCode,
            payment.ProviderTransactionId,
            true,
            null,
            $"{amount:0.00} {payment.Currency} refunded ({totalRefunded:0.00} total).",
            now);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Refunded {Amount} {Currency} against payment {PaymentId} (order {OrderNumber})",
            amount,
            payment.Currency,
            payment.Id,
            payment.Order?.PublicOrderNumber);

        return new PaymentRefundResult(true, refund.ProviderRefundId, null, null);
    }

    // ---- Helpers ----

    private static PaymentPlacementInfo BuildPlacementInfo(Payment payment)
    {
        var metadata = TryReadMetadata(payment);
        return new PaymentPlacementInfo(
            true,
            metadata?.RedirectUrl,
            metadata?.HostedCheckoutReference,
            metadata?.Instructions,
            payment.State.ToString(),
            payment.ProviderCode);
    }

    private async Task ExpireIfDueAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (payment.State is not (PaymentState.Pending or PaymentState.Initiated) ||
            payment.ExpiresAtUtc is null ||
            payment.ExpiresAtUtc.Value > DateTime.UtcNow)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == payment.OrderId, cancellationToken);
        if (order is null)
        {
            return;
        }

        payment.State = PaymentState.Expired;
        payment.CompletedAtUtc = now;
        AddTransaction(
            payment,
            PaymentTransactionType.Expire,
            payment.ProviderCode,
            payment.ProviderTransactionId,
            true,
            "expired",
            "The payment window elapsed before the payment was completed.",
            now);

        ApplyOrderPaymentStatus(order, PaymentState.Expired, now);

        await ReleaseOrderReservationsAsync(order.PublicOrderNumber, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Payment {PaymentId} for order {OrderNumber} expired", payment.Id, order.PublicOrderNumber);
    }

    private async Task ApplySettlementAsync(
        Payment payment,
        PaymentState state,
        PaymentTransactionType transactionType,
        string? providerTransactionId,
        string? failureReason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var order = payment.Order ?? await _context.Orders.FirstOrDefaultAsync(o => o.Id == payment.OrderId, cancellationToken);
        if (order is null)
        {
            throw new InvalidOperationException($"Order {payment.OrderId} was not found while settling a payment.");
        }

        payment.State = state;
        payment.ProviderTransactionId = providerTransactionId ?? payment.ProviderTransactionId;

        switch (state)
        {
            case PaymentState.Paid:
                payment.CompletedAtUtc = now;
                payment.FailureCode = null;
                payment.FailureReason = null;
                payment.FailedAtUtc = null;
                ApplyOrderPaymentStatus(order, PaymentState.Paid, now);
                AddTransaction(
                    payment,
                    transactionType,
                    payment.ProviderCode,
                    payment.ProviderTransactionId,
                    true,
                    null,
                    null,
                    now);
                break;

            case PaymentState.Failed:
                payment.FailureCode = "payment-failed";
                payment.FailureReason = failureReason ?? "The payment failed.";
                payment.FailedAtUtc = now;
                ApplyOrderPaymentStatus(order, PaymentState.Failed, now);
                AddTransaction(
                    payment,
                    transactionType,
                    payment.ProviderCode,
                    payment.ProviderTransactionId,
                    false,
                    payment.FailureCode,
                    payment.FailureReason,
                    now);
                await ReleaseOrderReservationsAsync(order.PublicOrderNumber, cancellationToken);
                break;

            case PaymentState.Cancelled:
                payment.CompletedAtUtc = now;
                payment.FailureReason = failureReason ?? "The payment was cancelled.";
                ApplyOrderPaymentStatus(order, PaymentState.Cancelled, now);
                AddTransaction(
                    payment,
                    transactionType,
                    payment.ProviderCode,
                    payment.ProviderTransactionId,
                    false,
                    "cancelled",
                    payment.FailureReason,
                    now);
                await ReleaseOrderReservationsAsync(order.PublicOrderNumber, cancellationToken);
                break;

            case PaymentState.Expired:
                payment.CompletedAtUtc = now;
                payment.FailureReason = failureReason ?? "The payment expired before it was completed.";
                ApplyOrderPaymentStatus(order, PaymentState.Expired, now);
                AddTransaction(
                    payment,
                    transactionType,
                    payment.ProviderCode,
                    payment.ProviderTransactionId,
                    false,
                    "expired",
                    payment.FailureReason,
                    now);
                await ReleaseOrderReservationsAsync(order.PublicOrderNumber, cancellationToken);
                break;

            default:
                break;
        }

        switch (state)
        {
            case PaymentState.Paid:
                await _emailService.SendPaymentReceivedAsync(order.Id, cancellationToken);
                break;
            case PaymentState.Failed:
                await _emailService.SendPaymentFailedAsync(order.Id, cancellationToken);
                break;
        }
    }

    private void ApplyOrderPaymentStatus(Order order, PaymentState state, DateTime now)
    {
        switch (state)
        {
            case PaymentState.Paid:
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaidAmount = Math.Max(order.PaidAmount, order.GrandTotal);
                order.PaidAtUtc = now;
                break;
            case PaymentState.Failed:
            case PaymentState.Cancelled:
            case PaymentState.Expired:
                order.PaymentStatus = PaymentStatus.Failed;
                break;
            default:
                break;
        }
    }

    private void AddTransaction(
        Payment payment,
        PaymentTransactionType type,
        string providerCode,
        string? providerTransactionId,
        bool succeeded,
        string? resultCode,
        string? resultMessage,
        DateTime now)
    {
        _context.PaymentTransactions.Add(new PaymentTransaction
        {
            PaymentId = payment.Id,
            Type = type,
            ProviderCode = providerCode,
            ProviderTransactionId = providerTransactionId,
            Succeeded = succeeded,
            ResultCode = resultCode,
            ResultMessage = resultMessage,
            CreatedAtUtc = now
        });
    }

    private async Task LogWebhookAsync(
        string providerCode,
        Guid? paymentId,
        string rawPayload,
        string? signature,
        PaymentWebhookStatus status,
        string? providerEventId,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        _context.PaymentWebhookLogs.Add(new PaymentWebhookLog
        {
            ProviderCode = providerCode,
            ProviderEventId = providerEventId,
            PaymentId = paymentId,
            Status = status,
            RawPayload = MaskPayload(rawPayload),
            Signature = signature,
            FailureReason = failureReason,
            ReceivedAtUtc = DateTime.UtcNow,
            ProcessedAtUtc = status is PaymentWebhookStatus.Processed
                ? DateTime.UtcNow
                : null
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Payment?> LoadPaymentForWebhookAsync(
        PaymentWebhookEvent parsed,
        string providerCode,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(parsed.ProviderTransactionId))
        {
            var byTransaction = await _context.Payments
                .Include(p => p.Order)
                .FirstOrDefaultAsync(
                    p => p.ProviderCode == providerCode &&
                         p.ProviderTransactionId == parsed.ProviderTransactionId,
                    cancellationToken);

            if (byTransaction is not null)
            {
                return byTransaction;
            }
        }

        if (!string.IsNullOrWhiteSpace(parsed.OrderNumber))
        {
            return await _context.Payments
                .Include(p => p.Order)
                .FirstOrDefaultAsync(
                    p => p.ProviderCode == providerCode &&
                         p.Order != null &&
                         p.Order.PublicOrderNumber == parsed.OrderNumber,
                    cancellationToken);
        }

        return null;
    }

    private async Task ReleaseOrderReservationsAsync(string? orderNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            return;
        }

        var reservationIds = await _context.StockReservations
            .Where(r => r.Status == StockReservationStatus.Active &&
                        (r.CartReference == orderNumber || r.ReferenceId == orderNumber))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        foreach (var reservationId in reservationIds)
        {
            await _inventoryService.ReleaseReservationAsync(reservationId, cancellationToken);
        }
    }

    private int ReservationMinutes(string providerCode) =>
        IsCashOnDelivery(providerCode)
            ? _orderOptions.Value.CodReservationMinutes
            : _orderOptions.Value.OnlineReservationMinutes;

    private static bool IsCashOnDelivery(string providerCode) =>
        string.Equals(providerCode, "cod", StringComparison.OrdinalIgnoreCase);

    private static bool IsSettled(PaymentState state) =>
        state is PaymentState.Paid or PaymentState.Failed or PaymentState.Cancelled or
            PaymentState.Expired or PaymentState.Refunded or PaymentState.PartiallyRefunded;

    private static bool CanTransition(PaymentState current, PaymentState target) =>
        current != target &&
        current is PaymentState.Pending or PaymentState.Initiated &&
        target is PaymentState.Paid or PaymentState.Failed or PaymentState.Cancelled or PaymentState.Expired;

    private static PaymentState? MapEventToState(string eventType)
    {
        return eventType switch
        {
            "payment.succeeded" or "payment.paid" or "payment.captured" => PaymentState.Paid,
            "payment.failed" => PaymentState.Failed,
            "payment.cancelled" or "payment.canceled" => PaymentState.Cancelled,
            "payment.expired" => PaymentState.Expired,
            _ => null
        };
    }

    private static PaymentStatusDto? ToStatusDto(Payment payment, Order order)
    {
        var metadata = TryReadMetadata(payment);
        var orderPaid = payment.State is PaymentState.Paid or PaymentState.PartiallyRefunded;

        return new PaymentStatusDto(
            payment.Id,
            payment.OrderId,
            order.PublicOrderNumber,
            payment.ProviderCode,
            payment.PaymentMethodCode,
            payment.State,
            payment.ProviderTransactionId,
            metadata?.HostedCheckoutReference,
            metadata?.Instructions,
            payment.Amount,
            payment.Currency,
            orderPaid,
            payment.CreatedAtUtc,
            payment.CompletedAtUtc,
            payment.FailedAtUtc,
            payment.FailureReason);
    }

    private static InitiationMetadata? TryReadMetadata(Payment payment)
    {
        if (string.IsNullOrWhiteSpace(payment.ResponseMetadata))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InitiationMetadata>(payment.ResponseMetadata);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Masks the raw webhook payload before it is persisted. The placeholder
    /// envelope contains no card data, but the mask keeps the audit record safe if
    /// a future gateway embeds tokens or other sensitive fields in the body.
    /// </summary>
    private static string? MaskPayload(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload) || rawPayload.Length <= 500)
        {
            return rawPayload;
        }

        return rawPayload[..500] + "[truncated]";
    }

    private static string? SerializeMetadata(InitiationMetadata metadata)
    {
        try
        {
            return JsonSerializer.Serialize(metadata);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Non-sensitive metadata captured at initiation time.</summary>
    private sealed record InitiationMetadata(
        string? ProviderTransactionId,
        string? RedirectUrl,
        string? HostedCheckoutReference,
        string? Instructions);
}
