namespace FashionStore.Domain.Enums;

/// <summary>
/// The lifecycle state of an order. The state machine is forward-only in normal
/// operation; cancellation is only allowed from the placed / confirmed states and
/// each transition is recorded in the order's status history.
/// </summary>
public enum OrderStatus
{
    Placed = 0,
    Confirmed = 1,
    Processing = 2,
    Shipped = 3,
    Delivered = 4,
    Completed = 5,
    Cancelled = 6
}

/// <summary>
/// The money collected against an order. <see cref="Unpaid"/> is the starting state
/// for cash on delivery and for online payments that have not yet been captured.
/// </summary>
public enum PaymentStatus
{
    Unpaid = 0,
    Paid = 1,
    PartiallyPaid = 2,
    Refunded = 3,
    Failed = 4
}

/// <summary>
/// How far an order has progressed through the warehouse. Starts at
/// <see cref="Unfulfilled"/> and advances as stock is picked, packed and shipped.
/// </summary>
public enum FulfilmentStatus
{
    Unfulfilled = 0,
    PartiallyFulfilled = 1,
    Fulfilled = 2
}

/// <summary>
/// Distinguishes the shipping snapshot from the billing snapshot stored on an order.
/// Both are immutable copies captured at placement time.
/// </summary>
public enum OrderAddressType
{
    Shipping = 0,
    Billing = 1
}

/// <summary>
/// The reason a customer asked to cancel an order. The code is stored on the order
/// and a human-readable note is appended to the status history so cancellations are
/// auditable without trusting free-form input.
/// </summary>
public enum OrderCancellationReason
{
    CustomerRequested = 0,
    ChangedMind = 1,
    FoundCheaperElsewhere = 2,
    DeliveryTooLate = 3,
    DuplicateOrder = 4,
    PaymentIssue = 5,
    Other = 6
}
