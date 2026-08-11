using System.Security.Claims;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Returns;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Enums;
using FashionStore.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FashionStore.Web.Controllers;

/// <summary>
/// The customer return panel. Signed-in customers reach <c>/returns</c> and see
/// only their own returns; guests start a return from their order detail screen and
/// prove access with the order's signed guest ticket (<c>t</c>), which is validated
/// against the order number before any read or lifecycle action. Creation is
/// re-validated server-side by <see cref="ICustomerReturnService"/> (return window,
/// product restrictions, quantity caps, duplicate prevention), and the step wizard
/// only posts item/quantity selections, reason and notes — never prices.
/// </summary>
[Route("returns")]
public class ReturnsController : Controller
{
    private readonly ICustomerReturnService _returnService;
    private readonly ICustomerOrderService _orderService;
    private readonly IOptions<ReturnSettings> _returnOptions;
    private readonly ILogger<ReturnsController> _logger;

    public ReturnsController(
        ICustomerReturnService returnService,
        ICustomerOrderService orderService,
        IOptions<ReturnSettings> returnOptions,
        ILogger<ReturnsController> logger)
    {
        _returnService = returnService;
        _orderService = orderService;
        _returnOptions = returnOptions;
        _logger = logger;
    }

    /// <summary>
    /// Signed-in customer return list with an optional status filter, paginated
    /// newest first. Guests use the order-scoped flow instead.
    /// </summary>
    [Authorize]
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] ReturnStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated user has no name identifier claim.");

        var result = await _returnService.GetCustomerReturnsAsync(
            userId,
            new CustomerReturnQueryRequest(page, pageSize, status),
            cancellationToken);

        ViewData["ActiveReturnNav"] = "index";
        ViewData["AppliedStatus"] = status;
        return View(result);
    }

    /// <summary>
    /// The return-start wizard for one order. Signed-in customers must own the
    /// order; guests must present a valid signed ticket bound to the order number.
    /// The returned lines carry server-computed quantity caps and refundable amounts.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("create/{publicOrderNumber}")]
    public async Task<IActionResult> Start(
        string publicOrderNumber,
        [FromQuery] string? t,
        [FromQuery] bool exchange = false,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? guestToken = null;

        if (string.IsNullOrEmpty(userId))
        {
            if (string.IsNullOrWhiteSpace(t) ||
                _orderService.ValidateGuestToken(t, publicOrderNumber) is null)
            {
                return RedirectToAction("Track", "Order");
            }

            guestToken = t;
        }

        var order = await _returnService.GetReturnableItemsAsync(publicOrderNumber, userId, cancellationToken);
        var reasons = await _returnService.GetReturnReasonsAsync(cancellationToken);

        ViewData["ActiveReturnNav"] = "start";
        return View(new ReturnStartViewModel
        {
            Order = order,
            Reasons = reasons,
            IsExchange = exchange,
            GuestAccessToken = guestToken
        });
    }

    /// <summary>
    /// Creates the return request. Ownership is verified exactly as for the wizard
    /// (signed-in owner or validated guest ticket) and the service re-validates every
    /// business rule against the order snapshot before anything is persisted.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromForm] CreateReturnFormModel form,
        CancellationToken cancellationToken = default)
    {
        var publicOrderNumber = form.PublicOrderNumber;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? guestToken = null;

        if (string.IsNullOrEmpty(userId))
        {
            if (string.IsNullOrWhiteSpace(form.T) ||
                _orderService.ValidateGuestToken(form.T, publicOrderNumber) is null)
            {
                return RedirectToAction("Track", "Order");
            }

            guestToken = form.T;
        }

        var request = new CreateReturnRequest(
            form.ReasonCode ?? string.Empty,
            form.Notes,
            form.IsExchange,
            form.Items
                .Where(i => i.Quantity > 0)
                .Select(i => new ReturnItemSelectionDto(i.OrderItemId, i.Quantity))
                .ToList());

        var result = await _returnService.CreateReturnAsync(
            publicOrderNumber,
            request,
            userId,
            User.Identity?.Name,
            cancellationToken);

        if (!result.Success)
        {
            TempData["ReturnError"] = result.ErrorMessage;
            return RedirectToAction(nameof(Start), new { publicOrderNumber, t = guestToken, exchange = form.IsExchange });
        }

        if (form.Photos is { Length: > 0 } && result.ReturnNumber is not null)
        {
            var attachments = form.Photos
                .Select(f => new ReturnAttachmentInput(
                    f.OpenReadStream(),
                    f.FileName,
                    string.IsNullOrWhiteSpace(f.ContentType) ? "image/jpeg" : f.ContentType,
                    f.Length))
                .ToList();

            await _returnService.UploadAttachmentsAsync(
                result.ReturnNumber,
                userId,
                User.Identity?.Name,
                attachments,
                cancellationToken);
        }

        TempData["ReturnMessage"] = "Your return request has been submitted. Our team will review it shortly.";
        return RedirectToAction(nameof(Details), new { returnNumber = result.ReturnNumber, t = guestToken });
    }

    /// <summary>
    /// Return detail with the item lines, status timeline, attachments, refunds and
    /// exchanges. Signed-in customers must own the return; guests must present a
    /// valid ticket for the order the return belongs to.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{returnNumber}")]
    public async Task<IActionResult> Details(
        string returnNumber,
        [FromQuery] string? t,
        [FromQuery] string? order,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? guestToken = null;

        ReturnDetailDto? detail;
        if (string.IsNullOrEmpty(userId))
        {
            if (string.IsNullOrWhiteSpace(order) ||
                string.IsNullOrWhiteSpace(t) ||
                _orderService.ValidateGuestToken(t, order) is null)
            {
                return RedirectToAction("Track", "Order");
            }

            guestToken = t;
            detail = await _returnService.GetGuestReturnDetailAsync(order, returnNumber, cancellationToken);
        }
        else
        {
            detail = await _returnService.GetReturnDetailAsync(userId, returnNumber, cancellationToken);
        }

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["ActiveReturnNav"] = "details";
        ViewData["GuestAccessToken"] = guestToken;
        ViewData["GuestOrderNumber"] = string.IsNullOrEmpty(userId) ? detail.OrderNumber : null;
        return View(detail);
    }

    /// <summary>
    /// Uploads return photos. Ownership is verified (signed-in owner or guest ticket
    /// for the return's order); the service validates extension, size and count.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("{returnNumber}/attachments")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAttachments(
        string returnNumber,
        [FromForm] string? t,
        [FromForm] string? order,
        IFormFileCollection files,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            if (string.IsNullOrWhiteSpace(order) ||
                string.IsNullOrWhiteSpace(t) ||
                _orderService.ValidateGuestToken(t, order) is null)
            {
                return Unauthorized(new { success = false, message = "Your order access has expired. Please look up your order again." });
            }
        }
        else
        {
            var owned = await _returnService.GetReturnDetailAsync(userId, returnNumber, cancellationToken);
            if (owned is null)
            {
                return NotFound(new { success = false, message = "Return not found." });
            }
        }

        var attachments = files
            .Select(f => new ReturnAttachmentInput(
                f.OpenReadStream(),
                f.FileName,
                string.IsNullOrWhiteSpace(f.ContentType) ? "image/jpeg" : f.ContentType,
                f.Length))
            .ToList();

        var result = await _returnService.UploadAttachmentsAsync(
            returnNumber,
            userId,
            User.Identity?.Name,
            attachments,
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { success = false, message = result.ErrorMessage });
        }

        return Ok(new { success = true, attachmentId = result.AttachmentId, url = result.Url });
    }

    /// <summary>
    /// Customer marks the return as shipped back with an optional carrier/tracking
    /// number. Ownership is verified; the state machine then moves the return to
    /// in-transit.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("{returnNumber}/ship")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ship(
        string returnNumber,
        [FromForm] string? t,
        [FromForm] string? order,
        [FromForm] string? carrierCode,
        [FromForm] string? trackingNumber,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            if (string.IsNullOrWhiteSpace(order) ||
                string.IsNullOrWhiteSpace(t) ||
                _orderService.ValidateGuestToken(t, order) is null)
            {
                return RedirectToAction("Track", "Order");
            }
        }
        else
        {
            var owned = await _returnService.GetReturnDetailAsync(userId, returnNumber, cancellationToken);
            if (owned is null)
            {
                return NotFound();
            }
        }

        var result = await _returnService.MarkShippedAsync(
            returnNumber,
            carrierCode,
            trackingNumber,
            userId,
            User.Identity?.Name,
            cancellationToken);

        TempData[result.Success ? "ReturnMessage" : "ReturnError"] = result.Success
            ? "Thanks! Your return is now on its way back to us."
            : result.ErrorMessage;

        return RedirectToAction(nameof(Details), new { returnNumber, t, order });
    }

    /// <summary>
    /// Customer withdraws a return that has not yet progressed. Ownership is
    /// verified; the return is closed and its claimed quantity released.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("{returnNumber}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        string returnNumber,
        [FromForm] string? t,
        [FromForm] string? order,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            if (string.IsNullOrWhiteSpace(order) ||
                string.IsNullOrWhiteSpace(t) ||
                _orderService.ValidateGuestToken(t, order) is null)
            {
                return RedirectToAction("Track", "Order");
            }
        }
        else
        {
            var owned = await _returnService.GetReturnDetailAsync(userId, returnNumber, cancellationToken);
            if (owned is null)
            {
                return NotFound();
            }
        }

        var result = await _returnService.CancelAsync(returnNumber, userId, User.Identity?.Name, cancellationToken);

        TempData[result.Success ? "ReturnMessage" : "ReturnError"] = result.Success
            ? "Your return request was withdrawn."
            : result.ErrorMessage;

        return RedirectToAction(nameof(Details), new { returnNumber, t, order });
    }
}
