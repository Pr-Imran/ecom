using FashionStore.Application.DTOs.Returns;

namespace FashionStore.Web.Models;

/// <summary>
/// View model for the customer's return-start wizard. Carries the order's
/// returnable lines (with quantity caps and refundable amounts), the selectable
/// reason cards, the current step state and the guest access token when the order
/// is being returned by a guest.
/// </summary>
public sealed class ReturnStartViewModel
{
    public required ReturnOrderLookupDto Order { get; init; }
    public required IReadOnlyList<ReturnReasonOptionDto> Reasons { get; init; }
    public required bool IsExchange { get; init; }
    public string? GuestAccessToken { get; init; }
}

/// <summary>
/// Form model for creating a return. The browser supplies only item/quantity
/// selections plus reason and notes; every business rule (window, product-level
/// restrictions, quantity caps, duplicate prevention) is re-validated server-side.
/// </summary>
public sealed class CreateReturnFormModel
{
    public string PublicOrderNumber { get; set; } = string.Empty;
    public string? T { get; set; }
    public string? ReasonCode { get; set; }
    public string? Notes { get; set; }
    public bool IsExchange { get; set; }
    public List<ReturnItemSelectionForm> Items { get; set; } = new();
    public IFormFile[]? Photos { get; set; }
}

public sealed class ReturnItemSelectionForm
{
    public Guid OrderItemId { get; set; }
    public int Quantity { get; set; }
}
