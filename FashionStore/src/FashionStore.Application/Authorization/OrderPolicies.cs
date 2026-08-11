namespace FashionStore.Application.Authorization;

/// <summary>
/// Permission policies for the administrative order management. Each action maps
/// to a distinct permission so roles like OrderManager, CustomerSupport and Admin
/// can be granted different subsets of order capabilities.
/// </summary>
public static class OrderPolicies
{
    public const string OrdersView = "Orders.View";
    public const string OrdersUpdateStatus = "Orders.UpdateStatus";
    public const string OrdersCancel = "Orders.Cancel";
    public const string OrdersAddNote = "Orders.AddNote";
    public const string OrdersPrintInvoice = "Orders.PrintInvoice";
}
