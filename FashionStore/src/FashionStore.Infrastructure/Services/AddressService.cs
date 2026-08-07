using FashionStore.Application.Common;
using FashionStore.Application.DTOs.Account;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Customer address book implementation. Every read and mutation is scoped to the
/// customer id supplied by the caller; a foreign address is treated as not found
/// so ownership is never disclosed. Country-specific validation is delegated to
/// the extensible validator registry. Setting a default shipping/billing address
/// clears the previous default of the same type.
/// </summary>
public sealed class AddressService : IAddressService
{
    private readonly AppDbContext _context;
    private readonly IAddressValidationService _validation;
    private readonly ILogger<AddressService> _logger;

    public AddressService(
        AppDbContext context,
        IAddressValidationService validation,
        ILogger<AddressService> logger)
    {
        _context = context;
        _validation = validation;
        _logger = logger;
    }

    public async Task<AddressBookViewData> GetAddressBookAsync(string userId, CancellationToken cancellationToken = default)
    {
        var addresses = await _context.CustomerAddresses
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.IsDefaultShipping ? 0 : 1)
            .ThenBy(a => a.IsDefaultBilling ? 0 : 1)
            .ThenByDescending(a => a.UpdatedAtUtc)
            .Select(a => ToDto(a))
            .ToListAsync(cancellationToken);

        return new AddressBookViewData(
            addresses,
            HasDefaultShipping: addresses.Any(a => a.IsDefaultShipping),
            HasDefaultBilling: addresses.Any(a => a.IsDefaultBilling),
            Countries: CountryCatalog.All);
    }

    public async Task<AddressDto?> GetByIdAsync(string userId, Guid addressId, CancellationToken cancellationToken = default)
    {
        var address = await _context.CustomerAddresses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == addressId && a.UserId == userId, cancellationToken);

        return address == null ? null : ToDto(address);
    }

    public async Task<AddressMutationResult> CreateAsync(
        string userId,
        SaveAddressRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = _validation.Validate(request);
        if (errors.Count > 0)
        {
            return new AddressMutationResult(false, string.Join(" ", errors));
        }

        var isFirst = !await _context.CustomerAddresses.AnyAsync(a => a.UserId == userId, cancellationToken);
        var makeDefaultShipping = isFirst || request.IsDefaultShipping;
        var makeDefaultBilling = isFirst || request.IsDefaultBilling;

        if (makeDefaultShipping)
        {
            await ClearDefaultAsync(userId, isShipping: true, isBilling: false, cancellationToken);
        }

        if (makeDefaultBilling)
        {
            await ClearDefaultAsync(userId, isShipping: false, isBilling: true, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = Normalize(request.Label) ?? "Home",
            RecipientName = request.RecipientName.Trim(),
            Phone = TrimToNull(request.Phone),
            AddressLine1 = request.AddressLine1.Trim(),
            AddressLine2 = TrimToNull(request.AddressLine2),
            Area = TrimToNull(request.Area),
            City = request.City.Trim(),
            Region = TrimToNull(request.Region),
            PostalCode = request.PostalCode.Trim(),
            CountryCode = request.CountryCode.Trim().ToUpperInvariant(),
            DeliveryInstructions = TrimToNull(request.DeliveryInstructions),
            IsDefaultShipping = makeDefaultShipping,
            IsDefaultBilling = makeDefaultBilling,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _context.CustomerAddresses.Add(address);
        await _context.SaveChangesAsync(cancellationToken);

        return new AddressMutationResult(true, null, ToDto(address));
    }

    public async Task<AddressMutationResult> UpdateAsync(
        string userId,
        Guid addressId,
        SaveAddressRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = _validation.Validate(request);
        if (errors.Count > 0)
        {
            return new AddressMutationResult(false, string.Join(" ", errors));
        }

        var address = await FindOwnedAsync(userId, addressId, cancellationToken);
        if (address == null)
        {
            return new AddressMutationResult(false, "Address not found.");
        }

        if (request.IsDefaultShipping)
        {
            await ClearDefaultAsync(userId, isShipping: true, isBilling: false, cancellationToken);
        }

        if (request.IsDefaultBilling)
        {
            await ClearDefaultAsync(userId, isShipping: false, isBilling: true, cancellationToken);
        }

        address.Label = Normalize(request.Label) ?? address.Label;
        address.RecipientName = request.RecipientName.Trim();
        address.Phone = TrimToNull(request.Phone);
        address.AddressLine1 = request.AddressLine1.Trim();
        address.AddressLine2 = TrimToNull(request.AddressLine2);
        address.Area = TrimToNull(request.Area);
        address.City = request.City.Trim();
        address.Region = TrimToNull(request.Region);
        address.PostalCode = request.PostalCode.Trim();
        address.CountryCode = request.CountryCode.Trim().ToUpperInvariant();
        address.DeliveryInstructions = TrimToNull(request.DeliveryInstructions);
        address.IsDefaultShipping = request.IsDefaultShipping;
        address.IsDefaultBilling = request.IsDefaultBilling;
        address.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new AddressMutationResult(true, null, ToDto(address));
    }

    public async Task<AddressMutationResult> DeleteAsync(
        string userId,
        Guid addressId,
        CancellationToken cancellationToken = default)
    {
        var address = await FindOwnedAsync(userId, addressId, cancellationToken);
        if (address == null)
        {
            return new AddressMutationResult(false, "Address not found.");
        }

        _context.CustomerAddresses.Remove(address);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted address {AddressId} for user {UserId}", addressId, userId);
        return new AddressMutationResult(true, null);
    }

    public async Task<AddressMutationResult> SetDefaultAsync(
        string userId,
        Guid addressId,
        bool asShipping,
        bool asBilling,
        CancellationToken cancellationToken = default)
    {
        var address = await FindOwnedAsync(userId, addressId, cancellationToken);
        if (address == null)
        {
            return new AddressMutationResult(false, "Address not found.");
        }

        if (asShipping)
        {
            await ClearDefaultAsync(userId, isShipping: true, isBilling: false, cancellationToken);
            address.IsDefaultShipping = true;
        }

        if (asBilling)
        {
            await ClearDefaultAsync(userId, isShipping: false, isBilling: true, cancellationToken);
            address.IsDefaultBilling = true;
        }

        address.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return new AddressMutationResult(true, null, ToDto(address));
    }

    public async Task<AddressSnapshot?> GetSnapshotAsync(string userId, Guid addressId, CancellationToken cancellationToken = default)
    {
        var address = await _context.CustomerAddresses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == addressId && a.UserId == userId, cancellationToken);

        return address?.CreateSnapshot();
    }

    private async Task<CustomerAddress?> FindOwnedAsync(string userId, Guid addressId, CancellationToken cancellationToken)
    {
        return await _context.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.UserId == userId, cancellationToken);
    }

    private async Task ClearDefaultAsync(string userId, bool isShipping, bool isBilling, CancellationToken cancellationToken)
    {
        var query = _context.CustomerAddresses.Where(a => a.UserId == userId);

        if (isShipping)
        {
            query = query.Where(a => a.IsDefaultShipping);
        }
        else
        {
            query = query.Where(a => a.IsDefaultBilling);
        }

        var current = await query.ToListAsync(cancellationToken);
        foreach (var item in current)
        {
            if (isShipping)
            {
                item.IsDefaultShipping = false;
            }

            if (isBilling)
            {
                item.IsDefaultBilling = false;
            }

            item.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private static AddressDto ToDto(CustomerAddress a) => new(
        a.Id,
        a.Label,
        a.RecipientName,
        a.Phone,
        a.AddressLine1,
        a.AddressLine2,
        a.Area,
        a.City,
        a.Region,
        a.PostalCode,
        a.CountryCode,
        a.DeliveryInstructions,
        a.IsDefaultShipping,
        a.IsDefaultBilling,
        a.CreatedAtUtc,
        a.UpdatedAtUtc);

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
