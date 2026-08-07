using FashionStore.Application.DTOs.Account;
using FashionStore.Application.Interfaces;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FashionStore.UnitTests.Services;

public class AddressServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"addresses-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static AddressService CreateService(AppDbContext context)
    {
        var validation = new AddressValidationService(
            new ICountryAddressValidator[]
            {
                new DefaultCountryAddressValidator(),
                new UnitedStatesAddressValidator(),
                new UnitedKingdomAddressValidator()
            });

        return new AddressService(context, validation, NullLogger<AddressService>.Instance);
    }

    private static SaveAddressRequest ValidRequest(string label = "Home")
    {
        return new SaveAddressRequest(
            Label: label,
            RecipientName: "Jane Doe",
            Phone: "555-0100",
            AddressLine1: "123 Main Street",
            AddressLine2: "Apt 4",
            Area: "Downtown",
            City: "Austin",
            Region: "TX",
            PostalCode: "78701",
            CountryCode: "US",
            DeliveryInstructions: "Leave at the door");
    }

    private static async Task<CustomerAddress> SeedAddressAsync(
        AppDbContext context,
        string userId,
        string label = "Home",
        bool defaultShipping = false,
        bool defaultBilling = false)
    {
        var request = ValidRequest(label);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = label,
            RecipientName = request.RecipientName,
            Phone = request.Phone,
            AddressLine1 = request.AddressLine1,
            AddressLine2 = request.AddressLine2,
            Area = request.Area,
            City = request.City,
            Region = request.Region,
            PostalCode = request.PostalCode,
            CountryCode = request.CountryCode,
            DeliveryInstructions = request.DeliveryInstructions,
            IsDefaultShipping = defaultShipping,
            IsDefaultBilling = defaultBilling,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        context.CustomerAddresses.Add(address);
        await context.SaveChangesAsync();
        return address;
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_CreatesAddress()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.CreateAsync(UserA, ValidRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Address);
        Assert.Equal("Jane Doe", result.Address!.RecipientName);
        Assert.Equal(1, await context.CustomerAddresses.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_FirstAddress_BecomesDefaultShippingAndBilling()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.CreateAsync(UserA, ValidRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Address!.IsDefaultShipping);
        Assert.True(result.Address.IsDefaultBilling);
    }

    [Fact]
    public async Task CreateAsync_SubsequentAddress_WithDefaultFlag_ClearsPreviousDefault()
    {
        var context = CreateContext();
        var service = CreateService(context);
        await service.CreateAsync(UserA, ValidRequest("Home"), CancellationToken.None);

        var result = await service.CreateAsync(
            UserA,
            ValidRequest("Office") with { IsDefaultShipping = true },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Address!.IsDefaultShipping);
        Assert.False(result.Address.IsDefaultBilling);

        var home = await context.CustomerAddresses.SingleAsync(a => a.Label == "Home");
        Assert.False(home.IsDefaultShipping);
        Assert.True(home.IsDefaultBilling);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidCountry_ReturnsError()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.CreateAsync(
            UserA,
            ValidRequest() with { CountryCode = "XX" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Empty(await context.CustomerAddresses.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_MissingRequiredFields_ReturnsValidationErrors()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.CreateAsync(
            UserA,
            ValidRequest() with { RecipientName = "", AddressLine1 = "", City = "", PostalCode = "", Region = "" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Recipient name", result.ErrorMessage);
        Assert.Contains("Address line 1", result.ErrorMessage);
        Assert.Contains("City", result.ErrorMessage);
        Assert.Contains("Postal code", result.ErrorMessage);
        Assert.Contains("State", result.ErrorMessage);
        Assert.Empty(await context.CustomerAddresses.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_InvalidUsZip_ReturnsError()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.CreateAsync(
            UserA,
            ValidRequest() with { PostalCode = "12AB" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("US ZIP", result.ErrorMessage!);
    }

    [Fact]
    public async Task CreateAsync_InvalidUkPostcode_ReturnsError()
    {
        var context = CreateContext();
        var service = CreateService(context);

        var result = await service.CreateAsync(
            UserA,
            ValidRequest() with { CountryCode = "GB", Region = "", PostalCode = "12345" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("UK postcode", result.ErrorMessage!);
    }

    [Fact]
    public async Task GetByIdAsync_WithForeignAddress_ReturnsNull()
    {
        var context = CreateContext();
        var service = CreateService(context);
        var address = await SeedAddressAsync(context, UserB);

        var result = await service.GetByIdAsync(UserA, address.Id, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_WithOwnedAddress_ReturnsDto()
    {
        var context = CreateContext();
        var service = CreateService(context);
        var address = await SeedAddressAsync(context, UserA);

        var result = await service.GetByIdAsync(UserA, address.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(address.Id, result!.Id);
        Assert.Equal("Jane Doe", result.RecipientName);
    }

    [Fact]
    public async Task UpdateAsync_WithForeignAddress_ReturnsNotFound()
    {
        var context = CreateContext();
        var service = CreateService(context);
        var address = await SeedAddressAsync(context, UserB);

        var result = await service.UpdateAsync(UserA, address.Id, ValidRequest(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage!);
    }

    [Fact]
    public async Task UpdateAsync_WithOwnedAddress_AppliesChanges()
    {
        var context = CreateContext();
        var service = CreateService(context);
        var address = await SeedAddressAsync(context, UserA);

        var result = await service.UpdateAsync(
            UserA,
            address.Id,
            ValidRequest("Work") with { RecipientName = "John Smith" },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("John Smith", result.Address!.RecipientName);
        Assert.Equal("Work", result.Address.Label);
        var stored = await context.CustomerAddresses.SingleAsync(a => a.Id == address.Id);
        Assert.Equal("John Smith", stored.RecipientName);
    }

    [Fact]
    public async Task DeleteAsync_WithForeignAddress_ReturnsNotFoundAndKeepsRow()
    {
        var context = CreateContext();
        var service = CreateService(context);
        var address = await SeedAddressAsync(context, UserB);

        var result = await service.DeleteAsync(UserA, address.Id, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, await context.CustomerAddresses.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_WithOwnedAddress_RemovesRow()
    {
        var context = CreateContext();
        var service = CreateService(context);
        var address = await SeedAddressAsync(context, UserA);

        var result = await service.DeleteAsync(UserA, address.Id, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(await context.CustomerAddresses.ToListAsync());
    }

    [Fact]
    public async Task DeleteAsync_DefaultAddress_LeavesNoDefault()
    {
        var context = CreateContext();
        var service = CreateService(context);
        var address = await SeedAddressAsync(context, UserA, defaultShipping: true, defaultBilling: true);

        await service.DeleteAsync(UserA, address.Id, CancellationToken.None);

        Assert.Empty(await context.CustomerAddresses.ToListAsync());
    }

    [Fact]
    public async Task SetDefaultAsync_SwapsDefaultShippingBetweenAddresses()
    {
        var context = CreateContext();
        var service = CreateService(context);
        var home = await SeedAddressAsync(context, UserA, "Home", defaultShipping: true);
        var office = await SeedAddressAsync(context, UserA, "Office");

        var result = await service.SetDefaultAsync(UserA, office.Id, asShipping: true, asBilling: false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Address!.IsDefaultShipping);

        var homeReloaded = await context.CustomerAddresses.SingleAsync(a => a.Id == home.Id);
        var officeReloaded = await context.CustomerAddresses.SingleAsync(a => a.Id == office.Id);
        Assert.False(homeReloaded.IsDefaultShipping);
        Assert.True(officeReloaded.IsDefaultShipping);
    }

    [Fact]
    public async Task SetDefaultAsync_WithForeignAddress_ReturnsNotFound()
    {
        var context = CreateContext();
        var service = CreateService(context);
        var address = await SeedAddressAsync(context, UserB);

        var result = await service.SetDefaultAsync(UserA, address.Id, asShipping: true, asBilling: false, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage!);
    }

    [Fact]
    public async Task GetSnapshotAsync_WithOwnedAddress_ReturnsSnapshot()
    {
        var context = CreateContext();
        var service = CreateService(context);
        var address = await SeedAddressAsync(context, UserA);

        var snapshot = await service.GetSnapshotAsync(UserA, address.Id, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal("Jane Doe", snapshot!.RecipientName);
        Assert.Equal("123 Main Street", snapshot.AddressLine1);
        Assert.Equal("78701", snapshot.PostalCode);
    }

    [Fact]
    public async Task GetSnapshotAsync_WithForeignAddress_ReturnsNull()
    {
        var context = CreateContext();
        var service = CreateService(context);
        var address = await SeedAddressAsync(context, UserB);

        var snapshot = await service.GetSnapshotAsync(UserA, address.Id, CancellationToken.None);

        Assert.Null(snapshot);
    }

    [Fact]
    public void Snapshot_IsImmutableAfterSourceAddressEdit()
    {
        var request = ValidRequest();
        var address = new CustomerAddress
        {
            UserId = UserA,
            Label = "Home",
            RecipientName = request.RecipientName,
            Phone = request.Phone,
            AddressLine1 = request.AddressLine1,
            AddressLine2 = request.AddressLine2,
            Area = request.Area,
            City = request.City,
            Region = request.Region,
            PostalCode = request.PostalCode,
            CountryCode = request.CountryCode,
            DeliveryInstructions = request.DeliveryInstructions
        };

        var snapshot = address.CreateSnapshot();

        address.RecipientName = "Changed";
        address.AddressLine1 = "999 New Street";
        address.PostalCode = "00000";

        Assert.Equal("Jane Doe", snapshot.RecipientName);
        Assert.Equal("123 Main Street", snapshot.AddressLine1);
        Assert.Equal("78701", snapshot.PostalCode);
    }

    [Fact]
    public async Task GetAddressBookAsync_OrdersDefaultsFirst()
    {
        var context = CreateContext();
        var service = CreateService(context);
        var defaultAddress = await SeedAddressAsync(context, UserA, "Office", defaultShipping: true);
        var other = await SeedAddressAsync(context, UserA, "Home");

        var viewData = await service.GetAddressBookAsync(UserA, CancellationToken.None);

        Assert.Equal(2, viewData.Addresses.Count);
        Assert.Equal(defaultAddress.Id, viewData.Addresses[0].Id);
        Assert.Equal(other.Id, viewData.Addresses[1].Id);
        Assert.True(viewData.HasDefaultShipping);
        Assert.False(viewData.HasDefaultBilling);
        Assert.NotEmpty(viewData.Countries);
    }
}
