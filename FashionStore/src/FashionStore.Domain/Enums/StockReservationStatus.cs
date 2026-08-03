namespace FashionStore.Domain.Enums;

public enum StockReservationStatus
{
    Active = 1,
    Released = 2,
    Expired = 3,
    Consumed = 4,
    Cancelled = 5
}

public enum InventoryReferenceType
{
    None = 0,
    Order = 1,
    Cart = 2,
    PurchaseOrder = 3,
    Return = 4,
    Adjustment = 5
}
