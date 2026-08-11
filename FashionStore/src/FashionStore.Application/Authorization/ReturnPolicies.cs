namespace FashionStore.Application.Authorization;

/// <summary>
/// Permission policies for the administrative return and refund workflow. Each action
/// maps to a distinct permission so roles like OrderManager, CustomerSupport and
/// Admin receive different subsets of return capabilities.
/// </summary>
public static class ReturnPolicies
{
    public const string ReturnsView = "Returns.View";
    public const string ReturnsReview = "Returns.Review";
    public const string ReturnsInspect = "Returns.Inspect";
    public const string ReturnsRestock = "Returns.Restock";
    public const string ReturnsRefund = "Returns.Refund";
    public const string ReturnsExchange = "Returns.Exchange";
    public const string ReturnsComplete = "Returns.Complete";
}
