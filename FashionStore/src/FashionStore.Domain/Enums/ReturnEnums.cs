namespace FashionStore.Domain.Enums;

/// <summary>
/// The lifecycle state of a return request. The state machine is forward-only:
/// Requested → (UnderReview) → Approved → AwaitingShipment → InTransit → Received →
/// Inspected → RefundPending → Refunded → Closed, or Inspected → Exchanged → Closed.
/// Rejected is terminal. Every transition is recorded in the return's status history.
/// </summary>
public enum ReturnStatus
{
    Requested = 0,
    UnderReview = 1,
    Approved = 2,
    Rejected = 3,
    AwaitingShipment = 4,
    InTransit = 5,
    Received = 6,
    Inspected = 7,
    RefundPending = 8,
    Refunded = 9,
    Exchanged = 10,
    Closed = 11
}

/// <summary>
/// The customer-selected reason for a return. The code is stored on the return
/// request and a human-readable label is resolved from the reason catalogue so the
/// reason stays readable even if the catalogue row is later edited.
/// </summary>
public enum ReturnReasonCode
{
    ChangedMind = 0,
    WrongSize = 1,
    NotAsDescribed = 2,
    Damaged = 3,
    Defective = 4,
    Unwanted = 5,
    DuplicateOrder = 6,
    Other = 7
}

/// <summary>
/// The condition a returned item was found in at inspection. Sellable items are
/// returned to sellable stock; damaged items are written off and are never
/// restocked. This drives the inventory restoration decision.
/// </summary>
public enum ReturnItemCondition
{
    Undetermined = 0,
    Sellable = 1,
    Damaged = 2
}

/// <summary>
/// The decision recorded at the end of inspection: money is returned to the
/// customer (Refund), a replacement item is arranged (Exchange), or nothing is done.
/// </summary>
public enum ReturnResolution
{
    None = 0,
    Refund = 1,
    Exchange = 2
}

/// <summary>
/// The kind of refund issued against a return. Full and Item refunds cover selected
/// return lines, Shipping covers the order's shipping charge when allowed, Partial
/// covers an arbitrary amount, and Manual is an operator-entered refund that never
/// touches the payment gateway.
/// </summary>
public enum RefundType
{
    Full = 0,
    Partial = 1,
    Item = 2,
    Shipping = 3,
    Manual = 4
}

/// <summary>
/// The lifecycle of a single refund. A refund starts Pending, settles into
/// Succeeded or Failed once the gateway (or the manual operation) completes, and may
/// be Voided by an operator when it must not be honoured.
/// </summary>
public enum RefundStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Voided = 3
}

/// <summary>
/// The lifecycle of an exchange arranged against a return.
/// </summary>
public enum ExchangeStatus
{
    Pending = 0,
    Completed = 1,
    Cancelled = 2
}
