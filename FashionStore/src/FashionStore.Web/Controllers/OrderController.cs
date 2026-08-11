using System.Security.Claims;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Orders;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Enums;
using FashionStore.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FashionStore.Web.Controllers;

/// <summary>
/// The customer order panel. Signed-in customers reach <c>/orders</c> and see only
/// their own orders; guests reach <c>/orders/track</c> and prove access with the
/// order number plus the email used at checkout, after which a short-lived signed
/// ticket unlocks the order. Every cancellation and buy-again action re-checks
/// ownership (user id or valid ticket) before it is performed.
/// </summary>
[Route("orders")]
public class OrderController : Controller
{
    private readonly ICustomerOrderService _customerOrderService;
    private readonly IPaymentService _paymentService;
    private readonly IInvoiceService _invoiceService;
    private readonly IOptions<InvoiceSettings> _invoiceOptions;
    private readonly ILogger<OrderController> _logger;

    public OrderController(
        ICustomerOrderService customerOrderService,
        IPaymentService paymentService,
        IInvoiceService invoiceService,
        IOptions<InvoiceSettings> invoiceOptions,
        ILogger<OrderController> logger)
    {
        _customerOrderService = customerOrderService;
        _paymentService = paymentService;
        _invoiceService = invoiceService;
        _invoiceOptions = invoiceOptions;
        _logger = logger;
    }

    /// <summary>
    /// Signed-in order list with search (order number or product name) and lifecycle
    /// status filter, paginated newest first.
    /// </summary>
    [Authorize]
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? search,
        [FromQuery] OrderStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated user has no name identifier claim.");

        var result = await _customerOrderService.GetCustomerOrdersAsync(
            userId,
            new CustomerOrderQueryRequest(search, status, page, pageSize),
            cancellationToken);

        ViewData["ActiveOrderNav"] = "index";
        return View(result);
    }

    /// <summary>
    /// Guest lookup: enter the public order number and the email used at checkout.
    /// The order number alone is never enough — a signed ticket is only issued when
    /// the email matches.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("track")]
    public IActionResult Track(CancellationToken cancellationToken = default)
    {
        ViewData["ActiveOrderNav"] = "track";
        return View();
    }

    [AllowAnonymous]
    [HttpPost("track")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Track(
        [FromForm] string? publicOrderNumber,
        [FromForm] string? email,
        CancellationToken cancellationToken = default)
    {
        var result = await _customerOrderService.VerifyGuestLookupAsync(
            publicOrderNumber ?? string.Empty,
            email ?? string.Empty,
            cancellationToken);

        if (!result.Success)
        {
            ViewData["ActiveOrderNav"] = "track";
            ViewData["LookupError"] = result.ErrorMessage;
            ViewData["LookupOrderNumber"] = publicOrderNumber;
            ViewData["LookupEmail"] = email;
            return View();
        }

        return RedirectToAction(nameof(Details), new { publicOrderNumber = result.OrderNumber, t = result.Token });
    }

    /// <summary>
    /// Full order detail for the owner: signed-in customers pass their identity;
    /// guests must present a valid signed ticket (<c>t</c>) issued for that order
    /// number. The current payment status is attached for the payment section.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{publicOrderNumber}")]
    public async Task<IActionResult> Details(
        string publicOrderNumber,
        [FromQuery] string? t,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        OrderDetailDto? order;
        string? accessToken = null;

        if (string.IsNullOrEmpty(userId))
        {
            if (string.IsNullOrWhiteSpace(t) ||
                _customerOrderService.ValidateGuestToken(t, publicOrderNumber) is null)
            {
                // No identity and no valid ticket: the caller is not entitled to
                // view this order. Present the lookup screen instead of guessing.
                ViewData["ActiveOrderNav"] = "track";
                return RedirectToAction(nameof(Track));
            }

            accessToken = t;
            order = await _customerOrderService.GetGuestOrderDetailAsync(publicOrderNumber, cancellationToken);
        }
        else
        {
            order = await _customerOrderService.GetOrderDetailAsync(userId, publicOrderNumber, cancellationToken);
        }

        if (order is null)
        {
            return NotFound();
        }

        order = order with
        {
            Payment = await _paymentService.GetStatusByOrderNumberAsync(publicOrderNumber, cancellationToken)
        };

        ViewData["ActiveOrderNav"] = "details";
        ViewData["GuestAccessToken"] = accessToken;
        return View(order);
    }

    /// <summary>
    /// Customer-initiated cancellation. Business rules live in the service; here we
    /// only confirm ownership (signed-in user id or a valid guest ticket) and the
    /// actor details recorded in the status history.
    /// </summary>
    [HttpPost("{publicOrderNumber}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        string publicOrderNumber,
        [FromForm] string? reason,
        [FromForm] string? t,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actorId = userId ?? t ?? string.Empty;
        var actorName = string.IsNullOrEmpty(userId)
            ? null
            : User.Identity?.Name ?? userId;

        if (string.IsNullOrEmpty(userId))
        {
            if (string.IsNullOrWhiteSpace(t) ||
                _customerOrderService.ValidateGuestToken(t, publicOrderNumber) is null)
            {
                return RedirectToAction(nameof(Track));
            }
        }
        else
        {
            var ownsOrder = await _customerOrderService.GetOrderDetailAsync(userId, publicOrderNumber, cancellationToken);
            if (ownsOrder is null)
            {
                return NotFound();
            }
        }

        var parsed = Enum.TryParse<OrderCancellationReason>(reason, true, out var parsedReason)
            ? parsedReason
            : OrderCancellationReason.Other;

        var result = await _customerOrderService.CancelAsync(
            publicOrderNumber,
            parsed,
            actorId,
            actorName,
            cancellationToken);

        if (!result.Success)
        {
            if (!string.IsNullOrEmpty(userId))
            {
                TempData["OrderError"] = result.Message;
                return RedirectToAction(nameof(Details), new { publicOrderNumber });
            }

            TempData["OrderError"] = result.Message;
            return RedirectToAction(nameof(Details), new { publicOrderNumber, t });
        }

        TempData["OrderMessage"] = result.Message;
        if (string.IsNullOrEmpty(userId))
        {
            return RedirectToAction(nameof(Details), new { publicOrderNumber, t });
        }

        return RedirectToAction(nameof(Details), new { publicOrderNumber });
    }

    /// <summary>
    /// Buy-again availability for every order line, resolved against the live
    /// catalogue. The view re-renders the add-to-cart affordances from this list.
    /// </summary>
    [HttpPost("{publicOrderNumber}/buy-again")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BuyAgain(
        string publicOrderNumber,
        [FromForm] string? t,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            if (string.IsNullOrWhiteSpace(t) ||
                _customerOrderService.ValidateGuestToken(t, publicOrderNumber) is null)
            {
                return Unauthorized(new { success = false, message = "Your order access has expired. Please look up your order again." });
            }
        }
        else
        {
            var ownsOrder = await _customerOrderService.GetOrderDetailAsync(userId, publicOrderNumber, cancellationToken);
            if (ownsOrder is null)
            {
                return NotFound(new { success = false, message = "Order not found." });
            }
        }

        var items = await _customerOrderService.GetBuyAgainAsync(publicOrderNumber, cancellationToken);
        return Ok(new { success = true, items });
    }

    /// <summary>
    /// Customer invoice view. Ownership is always verified: signed-in customers must
    /// own the order, guests must present a valid access ticket bound to the order
    /// number. The invoice is generated on first open (the number is assigned by the
    /// concurrency-safe sequence) and is rendered entirely from order snapshots.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("invoice/{publicOrderNumber}")]
    public async Task<IActionResult> Invoice(
        string publicOrderNumber,
        [FromQuery] string? t,
        CancellationToken cancellationToken = default)
    {
        var order = await VerifyAccessAsync(publicOrderNumber, t, cancellationToken);
        if (order is null)
        {
            return RedirectToAction(nameof(Track));
        }

        var invoice = await _invoiceService.EnsureForOrderAsync(order.OrderId, cancellationToken);

        ViewData["ActiveOrderNav"] = "details";
        ViewData["GuestAccessToken"] = t;

        return View(new InvoiceViewModel
        {
            Invoice = invoice,
            Branding = _invoiceOptions.Value,
            IsAdminView = false,
            GuestAccessToken = t
        });
    }

    /// <summary>
    /// Customer PDF download for an invoice. Ownership is verified exactly as for the
    /// HTML view before the deterministic A4 PDF is produced.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("invoice/{publicOrderNumber}/pdf")]
    public async Task<IActionResult> DownloadInvoicePdf(
        string publicOrderNumber,
        [FromQuery] string? t,
        CancellationToken cancellationToken = default)
    {
        var order = await VerifyAccessAsync(publicOrderNumber, t, cancellationToken);
        if (order is null)
        {
            return RedirectToAction(nameof(Track));
        }

        var invoice = await _invoiceService.EnsureForOrderAsync(order.OrderId, cancellationToken);
        var pdf = await _invoiceService.BuildPdfAsync(invoice, cancellationToken);
        var fileName = $"invoice-{invoice.InvoiceNumber}.pdf";

        return File(pdf, "application/pdf", fileName);
    }

    /// <summary>
    /// Verifies that the caller may view the given order: a signed-in owner, or a
    /// guest holding a valid access ticket for the order number. Returns null when
    /// the caller has no access (the caller then redirects to the guest track screen).
    /// </summary>
    private async Task<OrderDetailDto?> VerifyAccessAsync(
        string publicOrderNumber,
        string? t,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            if (string.IsNullOrWhiteSpace(t) ||
                _customerOrderService.ValidateGuestToken(t, publicOrderNumber) is null)
            {
                return null;
            }

            return await _customerOrderService.GetGuestOrderDetailAsync(publicOrderNumber, cancellationToken);
        }

        return await _customerOrderService.GetOrderDetailAsync(userId, publicOrderNumber, cancellationToken);
    }
}
