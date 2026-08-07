namespace FashionStore.Domain.Enums;

/// <summary>
/// The kind of delivery a shipping method represents. Local pickup does not
/// require a shipping address and shows pickup instructions instead of an
/// estimated courier window.
/// </summary>
public enum ShippingMethodType
{
    Standard = 0,
    Express = 1,
    FreeDelivery = 2,
    LocalPickup = 3
}

/// <summary>
/// How a shipping rate is priced. <see cref="Flat"/> charges a fixed amount while
/// <see cref="PerUnitWeight"/> scales the amount by the total package weight.
/// </summary>
public enum ShippingRateType
{
    Flat = 0,
    PerUnitWeight = 1
}
