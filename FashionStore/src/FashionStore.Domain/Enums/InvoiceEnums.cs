namespace FashionStore.Domain.Enums;

/// <summary>
/// The financial state of an invoice as derived from the immutable order snapshot.
/// The value is recomputed whenever the invoice is generated or regenerated so it
/// always reflects the latest paid / refunded amounts captured on the order.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Issued but nothing has been paid yet (for example cash on delivery).</summary>
    Issued = 0,

    /// <summary>Part of the total has been collected.</summary>
    PartiallyPaid = 1,

    /// <summary>The full total has been collected.</summary>
    Paid = 2,

    /// <summary>Money has been returned to the customer.</summary>
    Refunded = 3
}
