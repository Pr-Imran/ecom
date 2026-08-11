using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Invoices;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Invoicing;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionStore.Infrastructure.Invoicing;

/// <summary>
/// Generates, regenerates and delivers invoices. All content is read from the
/// order's immutable snapshots (<see cref="OrderItem"/>, <see cref="OrderAddress"/>
/// and the order's financial fields) — the live catalogue is never touched, so the
/// invoice stays accurate after products are renamed or removed. Invoice numbers
/// are allocated by <see cref="InvoiceNumberSequence"/> in the same save that
/// persists the invoice; the unique invoice-number index is the concurrency guard,
/// and a conflict forces a retry that can never duplicate a number.
/// </summary>
public sealed class InvoiceService : IInvoiceService
{
    private const int MaxNumberingAttempts = 5;

    private readonly AppDbContext _context;
    private readonly IInvoicePdfGenerator _pdfGenerator;
    private readonly IEmailService _emailService;
    private readonly IOptions<InvoiceSettings> _invoiceOptions;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(
        AppDbContext context,
        IInvoicePdfGenerator pdfGenerator,
        IEmailService emailService,
        IOptions<InvoiceSettings> invoiceOptions,
        ILogger<InvoiceService> logger)
    {
        _context = context;
        _pdfGenerator = pdfGenerator;
        _emailService = emailService;
        _invoiceOptions = invoiceOptions;
        _logger = logger;
    }

    public async Task<InvoiceDto> EnsureForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxNumberingAttempts; attempt++)
        {
            try
            {
                return await EnsureForOrderCoreAsync(orderId, cancellationToken);
            }
            catch (DbUpdateException ex) when (attempt < MaxNumberingAttempts - 1 && IsUniqueConstraintViolation(ex))
            {
                _logger.LogWarning(ex, "Invoice number conflict while generating an invoice for order {OrderId}; retrying", orderId);
                _context.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique invoice number for the order.");
    }

    private async Task<InvoiceDto> EnsureForOrderCoreAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var invoice = await _context.Invoices
            .FirstOrDefaultAsync(i => i.OrderId == orderId, cancellationToken);

        if (invoice is not null)
        {
            return (await BuildDtoAsync(orderId, cancellationToken))!;
        }

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new InvalidOperationException($"Order {orderId} was not found while generating an invoice.");
        }

        var now = DateTime.UtcNow;
        var number = await GenerateInvoiceNumberAsync(cancellationToken);
        var (status, outstanding) = DeriveFinancialState(order);

        invoice = new Invoice
        {
            InvoiceNumber = number,
            OrderId = order.Id,
            IssueDateUtc = now,
            Currency = order.Currency,
            Subtotal = order.Subtotal,
            ProductDiscount = order.ProductDiscount,
            CouponDiscount = order.CouponDiscount,
            ShippingCharge = order.ShippingCharge,
            Tax = order.Tax,
            GrandTotal = order.GrandTotal,
            PaidAmount = order.PaidAmount,
            OutstandingAmount = outstanding,
            RefundedAmount = order.RefundedAmount,
            Status = status,
            GeneratedAtUtc = now,
            Version = 1
        };

        _context.Invoices.Add(invoice);
        order.InvoiceNumber = number;

        await _context.SaveChangesAsync(cancellationToken);

        return (await BuildDtoAsync(orderId, cancellationToken))!;
    }

    public async Task<InvoiceDto?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var exists = await _context.Invoices.AsNoTracking().AnyAsync(i => i.OrderId == orderId, cancellationToken);
        return exists ? await BuildDtoAsync(orderId, cancellationToken) : null;
    }

    public async Task<InvoiceDto?> GetByOrderNumberAsync(string publicOrderNumber, CancellationToken cancellationToken = default)
    {
        var orderId = await _context.Orders.AsNoTracking()
            .Where(o => o.PublicOrderNumber == publicOrderNumber)
            .Select(o => o.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (orderId == Guid.Empty)
        {
            return null;
        }

        var exists = await _context.Invoices.AsNoTracking().AnyAsync(i => i.OrderId == orderId, cancellationToken);
        return exists ? await BuildDtoAsync(orderId, cancellationToken) : null;
    }

    public async Task<InvoiceDto> RegenerateAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.OrderId == orderId, cancellationToken);
        if (invoice is null)
        {
            return await EnsureForOrderAsync(orderId, cancellationToken);
        }

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new InvalidOperationException($"Order {orderId} was not found while regenerating its invoice.");
        }

        var (status, outstanding) = DeriveFinancialState(order);

        invoice.Subtotal = order.Subtotal;
        invoice.ProductDiscount = order.ProductDiscount;
        invoice.CouponDiscount = order.CouponDiscount;
        invoice.ShippingCharge = order.ShippingCharge;
        invoice.Tax = order.Tax;
        invoice.GrandTotal = order.GrandTotal;
        invoice.PaidAmount = order.PaidAmount;
        invoice.OutstandingAmount = outstanding;
        invoice.RefundedAmount = order.RefundedAmount;
        invoice.Status = status;
        invoice.GeneratedAtUtc = DateTime.UtcNow;
        invoice.Version += 1;

        await _context.SaveChangesAsync(cancellationToken);

        return (await BuildDtoAsync(orderId, cancellationToken))!;
    }

    public async Task<InvoiceEmailResult> EmailPdfAsync(Guid orderId, string? initiatedBy, CancellationToken cancellationToken = default)
    {
        var invoice = await EnsureForOrderAsync(orderId, cancellationToken);

        if (string.IsNullOrWhiteSpace(invoice.GuestEmail))
        {
            return new InvoiceEmailResult(false, "The order has no customer email to send the invoice to.", null, null);
        }

        var pdf = await BuildPdfAsync(invoice, cancellationToken);
        var subject = $"Invoice {invoice.InvoiceNumber} for order {invoice.PublicOrderNumber}";
        var recipientName = string.IsNullOrWhiteSpace(invoice.CustomerName) ? "customer" : invoice.CustomerName;
        var body = $"<p>Dear {recipientName},</p><p>Please find attached invoice <strong>{invoice.InvoiceNumber}</strong> for order <strong>{invoice.PublicOrderNumber}</strong>.</p><p>Thank you for shopping with us.</p>";

        var sent = await _emailService.SendEmailWithAttachmentAsync(
            invoice.GuestEmail,
            subject,
            body,
            $"invoice-{invoice.InvoiceNumber}.pdf",
            pdf,
            cancellationToken);

        var now = DateTime.UtcNow;
        var log = new InvoiceSendLog
        {
            InvoiceId = invoice.InvoiceId,
            SentTo = invoice.GuestEmail,
            Subject = subject,
            SentBy = initiatedBy,
            Succeeded = sent,
            ErrorMessage = sent ? null : "SMTP delivery failed.",
            SentAtUtc = now
        };
        _context.InvoiceSendLogs.Add(log);

        var persisted = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == invoice.InvoiceId, cancellationToken);
        if (persisted is not null && sent)
        {
            persisted.SentAtUtc = now;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new InvoiceEmailResult(
            sent,
            sent ? null : "The email provider could not deliver the invoice.",
            log.Id,
            invoice.GuestEmail);
    }

    public async Task<IReadOnlyList<InvoiceSendLogDto>> GetSendHistoryAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var invoiceId = await _context.Invoices.AsNoTracking()
            .Where(i => i.OrderId == orderId)
            .Select(i => i.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (invoiceId == Guid.Empty)
        {
            return Array.Empty<InvoiceSendLogDto>();
        }

        return await _context.InvoiceSendLogs.AsNoTracking()
            .Where(l => l.InvoiceId == invoiceId)
            .OrderByDescending(l => l.SentAtUtc)
            .Select(l => new InvoiceSendLogDto(
                l.Id,
                l.SentTo,
                l.Subject,
                l.Succeeded,
                l.ErrorMessage,
                l.SentAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<byte[]> BuildPdfAsync(InvoiceDto invoice, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_pdfGenerator.Generate(invoice));
    }

    // ---- Numbering ----

    private async Task<string> GenerateInvoiceNumberAsync(CancellationToken cancellationToken)
    {
        var prefix = _invoiceOptions.Value.InvoicePrefix;
        var year = _invoiceOptions.Value.YearAware ? DateTime.UtcNow.Year : 0;

        var sequence = await _context.InvoiceNumberSequences
            .FirstOrDefaultAsync(s => s.Prefix == prefix && s.Year == year, cancellationToken);

        if (sequence is null)
        {
            sequence = new InvoiceNumberSequence
            {
                Prefix = prefix,
                Year = year,
                LastNumber = 0
            };
            _context.InvoiceNumberSequences.Add(sequence);
        }

        sequence.LastNumber++;

        var number = year > 0
            ? $"{prefix}{year}-{sequence.LastNumber:D6}"
            : $"{prefix}{sequence.LastNumber:D6}";

        return number.Length > 50 ? number[..50] : number;
    }

    // ---- DTO mapping ----

    private async Task<InvoiceDto?> BuildDtoAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var invoice = await _context.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.OrderId == orderId, cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var refunds = await (from refund in _context.PaymentRefundRecords.AsNoTracking()
                             join payment in _context.Payments.AsNoTracking() on refund.PaymentId equals payment.Id
                             where payment.OrderId == orderId && refund.Succeeded
                             select refund)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var notes = order.Notes
            .Where(n => !n.IsInternal)
            .OrderBy(n => n.CreatedAtUtc)
            .Select(n => n.Note)
            .ToList();

        return new InvoiceDto(
            invoice.Id,
            order.Id,
            invoice.InvoiceNumber,
            order.PublicOrderNumber,
            invoice.Version,
            invoice.IssueDateUtc,
            invoice.Currency,
            invoice.Subtotal,
            invoice.ProductDiscount,
            invoice.CouponDiscount,
            invoice.ShippingCharge,
            invoice.Tax,
            invoice.GrandTotal,
            invoice.PaidAmount,
            invoice.OutstandingAmount,
            invoice.RefundedAmount,
            invoice.Status.ToString(),
            invoice.GeneratedAtUtc,
            invoice.SentAtUtc,
            string.IsNullOrEmpty(order.UserId),
            order.CustomerName,
            order.GuestEmail,
            order.GuestPhone,
            order.PaymentMethodCode,
            order.PaymentStatus.ToString(),
            order.ShippingMethodName,
            order.TrackingNumber,
            order.CarrierCode,
            order.TrackingUrl,
            order.Items
                .OrderBy(i => i.Id.ToString())
                .Select(i => new InvoiceItemDto(
                    i.Id,
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
                    i.LineTotal))
                .ToList(),
            order.BillingAddress is null ? null : ToAddressDto(order.BillingAddress),
            order.ShippingAddress is null ? null : ToAddressDto(order.ShippingAddress),
            notes,
            refunds
                .Select(r => new InvoiceRefundReferenceDto(
                    r.ProviderRefundId ?? string.Empty,
                    r.Currency,
                    r.Amount,
                    r.CreatedAtUtc))
                .ToList());
    }

    private static InvoiceAddressDto ToAddressDto(OrderAddress address) =>
        new(
            address.RecipientName,
            address.Phone,
            address.AddressLine1,
            address.AddressLine2,
            address.Area,
            address.City,
            address.Region,
            address.PostalCode,
            address.CountryCode,
            address.DeliveryInstructions);

    private static (InvoiceStatus Status, decimal Outstanding) DeriveFinancialState(Order order)
    {
        var outstanding = Math.Max(0, order.GrandTotal - order.PaidAmount - order.RefundedAmount);

        InvoiceStatus status;
        if (order.GrandTotal > 0 && order.RefundedAmount >= order.GrandTotal)
        {
            status = InvoiceStatus.Refunded;
        }
        else if (order.GrandTotal > 0 && order.PaidAmount >= order.GrandTotal)
        {
            status = InvoiceStatus.Paid;
        }
        else if (order.PaidAmount > 0)
        {
            status = InvoiceStatus.PartiallyPaid;
        }
        else
        {
            status = InvoiceStatus.Issued;
        }

        return (status, outstanding);
    }

    private async Task<Order?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.ShippingAddress)
            .Include(o => o.BillingAddress)
            .Include(o => o.Notes)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (var current = (Exception?)exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().Name.Contains("UniqueConstraint", StringComparison.Ordinal))
            {
                return true;
            }

            if (current is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }
}
