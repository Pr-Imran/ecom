using FashionStore.Application.DTOs.Reviews;

namespace FashionStore.Web.Models;

/// <summary>
/// The public reviews page for one product: the rating summary, the sortable /
/// filterable review list and the signed-in customer's eligibility context so the
/// view can render the right write-review call to action.
/// </summary>
public sealed class ReviewPageViewModel
{
    public ReviewProductDto Product { get; set; } = null!;
    public ReviewListResultDto Reviews { get; set; } = null!;
    public ReviewQueryRequest Query { get; set; } = new();
    public bool IsAuthenticated { get; set; }
    public ReviewEligibilityDto? Eligibility { get; set; }
}

/// <summary>Context for the mobile full-screen write-review form.</summary>
public sealed class WriteReviewViewModel
{
    public ReviewProductDto Product { get; set; } = null!;
    public ReviewEligibilityDto Eligibility { get; set; } = null!;
}

/// <summary>Form payload for the write-review form.</summary>
public sealed class SubmitReviewForm
{
    public Guid ProductId { get; set; }
    public string ProductSlug { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public Guid? OrderItemId { get; set; }
    public IFormFileCollection Photos { get; set; } = new FormFileCollection();
}
