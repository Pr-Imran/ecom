using FashionStore.Application.DTOs.Account;
using FashionStore.Domain.Entities;

namespace FashionStore.Application.Interfaces;

/// <summary>
/// Customer address book operations. Every read and mutation is scoped to the
/// customer id resolved from the authenticated principal; one customer can never
/// access another customer's addresses. At most one default shipping and one
/// default billing address can exist per customer; setting a default clears the
/// previous one.
/// </summary>
public interface IAddressService
{
    /// <summary>
    /// Loads the customer's address book together with default-state flags and the
    /// selectable country list.
    /// </summary>
    Task<AddressBookViewData> GetAddressBookAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single address belonging to the customer, or null when the address
    /// does not exist or belongs to another customer.
    /// </summary>
    Task<AddressDto?> GetByIdAsync(string userId, Guid addressId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an address for the customer after country-specific validation.
    /// Setting the first address or a default flag promotes the address to the
    /// corresponding default. Returns the persisted address.
    /// </summary>
    Task<AddressMutationResult> CreateAsync(
        string userId,
        SaveAddressRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an address belonging to the customer. Ownership is enforced; a
    /// foreign address is reported as not found. Default flags are re-balanced so
    /// only one default shipping and billing address remain.
    /// </summary>
    Task<AddressMutationResult> UpdateAsync(
        string userId,
        Guid addressId,
        SaveAddressRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an address belonging to the customer. Deleting a default address
    /// leaves the customer without a default of that type; no automatic fallback
    /// is created.
    /// </summary>
    Task<AddressMutationResult> DeleteAsync(
        string userId,
        Guid addressId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an address as the default shipping and/or billing address, clearing
    /// the previous default of the same type. Ownership is enforced.
    /// </summary>
    Task<AddressMutationResult> SetDefaultAsync(
        string userId,
        Guid addressId,
        bool asShipping,
        bool asBilling,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces an immutable snapshot of an address belonging to the customer.
    /// Orders persist this snapshot at creation time so later edits to the address
    /// book never change the values recorded on an already-placed order.
    /// </summary>
    Task<AddressSnapshot?> GetSnapshotAsync(string userId, Guid addressId, CancellationToken cancellationToken = default);
}
