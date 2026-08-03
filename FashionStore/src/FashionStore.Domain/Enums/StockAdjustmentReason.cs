namespace FashionStore.Domain.Enums;

public enum StockAdjustmentReason
{
    InitialStock = 1,
    PurchaseReceipt = 2,
    ManualIncrease = 3,
    ManualDecrease = 4,
    OrderReservation = 5,
    ReservationRelease = 6,
    OrderFulfilment = 7,
    CustomerReturn = 8,
    DamagedStock = 9,
    LostStock = 10,
    Correction = 11
}
