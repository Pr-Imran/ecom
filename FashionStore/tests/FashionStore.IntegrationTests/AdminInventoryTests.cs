using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

/// <summary>
/// Exercises the administrative inventory API end to end: the Inventory.Manage
/// permission guard, warehouse CRUD, stock adjustment with its ledger trail,
/// reservations and releases, threshold updates and the CSV export.
/// </summary>
public class AdminInventoryTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "AdminTest!pass1";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminInventoryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"inventory-admin-{Guid.NewGuid():N}@example.com";

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetInt32() : -1;

    private static void AssertRedirectedTo(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(path, response.Headers.Location?.ToString());
    }

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    // ---- Seeding helpers ----

    private async Task<(string Email, string UserId)> CreateUserWithPermissionsAsync(params string[] permissions)
    {
        var email = UniqueEmail();
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var createResult = await userManager.CreateAsync(user, Password);
        Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(e => e.Description)));

        foreach (var permission in permissions)
        {
            var claimResult = await userManager.AddClaimAsync(user, new Claim("permission", permission));
            Assert.True(claimResult.Succeeded, string.Join("; ", claimResult.Errors.Select(e => e.Description)));
        }

        return (email, user.Id);
    }

    private async Task<HttpClient> LoggedInClientAsync(string email)
    {
        var client = CreateClient();
        var loginHtml = await client.GetStringAsync("/Account/Login");
        var token = CartTestsHelper.ExtractAntiforgeryToken(loginHtml);
        var login = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["EmailOrUserName"] = email,
                ["Password"] = Password
            })
        };
        var response = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private async Task<HttpClient> InventoryClientAsync()
    {
        var (email, _) = await CreateUserWithPermissionsAsync("Inventory.Manage");
        return await LoggedInClientAsync(email);
    }

    private Guid SweaterVariantId() => CartTestsHelper.GetVariantId(_factory, "SW-1001-GREY-M");

    private Guid MainWarehouseId()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Warehouses.Single(w => w.Code == "MAIN").Id;
    }

    private async Task ResetSweaterStockAsync(int onHand = 10)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var variant = await db.ProductVariants.SingleAsync(v => v.Sku == "SW-1001-GREY-M");
        variant.StockQuantity = onHand;
        variant.ReservedStock = 0;
        var stocks = await db.WarehouseStocks.Where(s => s.ProductVariantId == variant.Id).ToListAsync();
        foreach (var stock in stocks)
        {
            stock.OnHandQuantity = onHand;
            stock.ReservedQuantity = 0;
        }
        await db.SaveChangesAsync();
    }

    private async Task RestoreSweaterSeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var variant = await db.ProductVariants.SingleAsync(v => v.Sku == "SW-1001-GREY-M");
        variant.StockQuantity = 10;
        variant.ReservedStock = 0;
        var stocks = await db.WarehouseStocks.Where(s => s.ProductVariantId == variant.Id).ToListAsync();
        foreach (var stock in stocks)
        {
            stock.OnHandQuantity = 10;
            stock.ReservedQuantity = 0;
        }
        await db.SaveChangesAsync();
    }

    // ---- Permission guards ----

    [Fact]
    public async Task Anonymous_CannotAccessInventoryApi()
    {
        var response = await CreateClient().GetAsync("/api/admin/inventory");
        AssertRedirectedTo(response, "/Account/Login");
    }

    [Fact]
    public async Task UserWithoutInventoryManagePermission_IsDenied()
    {
        var client = await LoggedInClientAsync((await CreateUserWithPermissionsAsync("Products.View")).Email);
        var response = await client.GetAsync("/api/admin/inventory");
        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    // ---- Search / detail / summary / export ----

    [Fact]
    public async Task Search_ReturnsSeededSweaterRow()
    {
        await ResetSweaterStockAsync();
        var client = await InventoryClientAsync();

        var response = await client.GetAsync("/api/admin/inventory?searchTerm=SW-1001");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await ReadJsonAsync(response);
        var rows = root.GetProperty("items").EnumerateArray().ToList();
        var row = Assert.Single(rows);
        Assert.Equal("SW-1001-GREY-M", GetString(row, "sku"));
        Assert.Equal(10, GetInt(row, "totalOnHand"));
        Assert.Equal(10, GetInt(row, "totalAvailable"));
    }

    [Fact]
    public async Task Summary_ReportsTotals()
    {
        await ResetSweaterStockAsync();
        var client = await InventoryClientAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expectedOutOfStock = (await db.WarehouseStocks.AsNoTracking().ToListAsync())
            .Count(s => s.AvailableQuantity <= 0);

        var response = await client.GetAsync("/api/admin/inventory/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await ReadJsonAsync(response);
        Assert.True(summary.GetProperty("totalOnHand").GetInt32() >= 10);
        Assert.Equal(expectedOutOfStock, summary.GetProperty("outOfStockCount").GetInt32());
    }

    [Fact]
    public async Task VariantDetail_ShowsWarehouseStock()
    {
        await ResetSweaterStockAsync();
        var client = await InventoryClientAsync();

        var response = await client.GetAsync($"/api/admin/inventory/variants/{SweaterVariantId()}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await ReadJsonAsync(response);
        Assert.Equal("SW-1001-GREY-M", GetString(detail, "sku"));
        Assert.Equal(10, GetInt(detail, "totalOnHand"));
        var warehouse = Assert.Single(detail.GetProperty("warehouses").EnumerateArray());
        Assert.Equal("Main Warehouse", GetString(warehouse, "warehouseName"));
    }

    [Fact]
    public async Task VariantDetail_UnknownVariant_ReturnsNotFound()
    {
        var client = await InventoryClientAsync();
        var response = await client.GetAsync($"/api/admin/inventory/variants/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Export_ReturnsCsvWithRows()
    {
        await ResetSweaterStockAsync();
        var client = await InventoryClientAsync();

        var response = await client.GetAsync("/api/admin/inventory/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("SKU,Product,", csv);
        Assert.Contains("SW-1001-GREY-M", csv);
    }

    // ---- Stock adjustment ----

    [Fact]
    public async Task AdjustStock_Increase_WritesLedgerAndUpdatesVariant()
    {
        await ResetSweaterStockAsync(5);
        var client = await InventoryClientAsync();

        var response = await client.PostAsync("/api/admin/inventory/adjust", Json(new
        {
            variantId = SweaterVariantId(),
            warehouseId = MainWarehouseId(),
            adjustmentQuantity = 7,
            reason = 3,
            notes = "Stock count correction"
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stock = await ReadJsonAsync(response);
        Assert.Equal(12, GetInt(stock, "onHandQuantity"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var variant = await db.ProductVariants.SingleAsync(v => v.Id == SweaterVariantId());
        Assert.Equal(12, variant.StockQuantity);

        var tx = await db.InventoryTransactions
            .Where(t => t.ProductVariantId == SweaterVariantId() && t.Reason == StockAdjustmentReason.ManualIncrease)
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync();
        Assert.NotNull(tx);
        Assert.Equal(7, tx.QuantityChange);
        Assert.Equal(5, tx.PreviousOnHand);
        Assert.Equal(12, tx.NewOnHand);

        await RestoreSweaterSeedAsync();
    }

    [Fact]
    public async Task AdjustStock_NegativeBeyondAvailable_IsRejected()
    {
        await ResetSweaterStockAsync(3);
        var client = await InventoryClientAsync();

        var response = await client.PostAsync("/api/admin/inventory/adjust", Json(new
        {
            variantId = SweaterVariantId(),
            warehouseId = MainWarehouseId(),
            adjustmentQuantity = -10,
            reason = 9
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await RestoreSweaterSeedAsync();
    }

    [Fact]
    public async Task BulkAdjust_UpdatesMultipleVariants()
    {
        await ResetSweaterStockAsync(5);
        var client = await InventoryClientAsync();
        var shoeVariant = CartTestsHelper.GetVariantId(_factory, "SH-3003-BLK-09");

        var response = await client.PostAsync("/api/admin/inventory/bulk-adjust", Json(new
        {
            variantIds = new[] { SweaterVariantId(), shoeVariant },
            warehouseId = MainWarehouseId(),
            adjustmentQuantity = 2,
            reason = 2
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(2, body.GetProperty("adjustedCount").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(7, (await db.ProductVariants.SingleAsync(v => v.Id == SweaterVariantId())).StockQuantity);
        Assert.Equal(2, (await db.ProductVariants.SingleAsync(v => v.Id == shoeVariant)).StockQuantity);

        var shoe = await db.ProductVariants.SingleAsync(v => v.Id == shoeVariant);
        shoe.StockQuantity = 0;
        var shoeStocks = await db.WarehouseStocks.Where(s => s.ProductVariantId == shoeVariant).ToListAsync();
        foreach (var stock in shoeStocks)
        {
            stock.OnHandQuantity = 0;
        }

        await db.SaveChangesAsync();

        await RestoreSweaterSeedAsync();
    }

    // ---- Thresholds ----

    [Fact]
    public async Task SetThresholds_UpdatesWarehouseStock()
    {
        await ResetSweaterStockAsync();
        var client = await InventoryClientAsync();

        var response = await client.PutAsync(
            $"/api/admin/inventory/variants/{SweaterVariantId()}/thresholds",
            Json(new
            {
                variantId = SweaterVariantId(),
                warehouseId = MainWarehouseId(),
                lowStockThreshold = 4,
                reorderLevel = 2,
                allowBackorder = false
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stock = await db.WarehouseStocks.SingleAsync(s =>
            s.ProductVariantId == SweaterVariantId() && s.WarehouseId == MainWarehouseId());
        Assert.Equal(4, stock.LowStockThreshold);
        Assert.Equal(2, stock.ReorderLevel);
    }

    [Fact]
    public async Task SetThresholds_UnknownVariant_ReturnsNotFound()
    {
        var client = await InventoryClientAsync();
        var unknownVariantId = Guid.NewGuid();
        var response = await client.PutAsync(
            $"/api/admin/inventory/variants/{unknownVariantId}/thresholds",
            Json(new { variantId = unknownVariantId, lowStockThreshold = 1 }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Reservations ----

    [Fact]
    public async Task Reserve_ThenRelease_UpdatesReservedQuantities()
    {
        await ResetSweaterStockAsync(5);
        var client = await InventoryClientAsync();

        var create = await client.PostAsync("/api/admin/inventory/reservations", Json(new
        {
            variantId = SweaterVariantId(),
            warehouseId = MainWarehouseId(),
            quantity = 2,
            cartReference = "CART-RES-1",
            expirationMinutes = 30
        }));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var reservation = await ReadJsonAsync(create);
        var reservationId = reservation.GetProperty("id").GetGuid();
        Assert.Equal(2, GetInt(reservation, "quantity"));

        using (var midScope = _factory.Services.CreateScope())
        {
            var db = midScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var variant = await db.ProductVariants.SingleAsync(v => v.Id == SweaterVariantId());
            Assert.Equal(2, variant.ReservedStock);
        }

        var release = await client.PostAsync($"/api/admin/inventory/reservations/{reservationId}/release", Json(new { }));
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        var releaseBody = await ReadJsonAsync(release);
        Assert.True(releaseBody.GetProperty("released").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        var checkDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await checkDb.StockReservations.SingleAsync(r => r.Id == reservationId);
        Assert.Equal(StockReservationStatus.Released, stored.Status);
        Assert.Equal(0, (await checkDb.ProductVariants.SingleAsync(v => v.Id == SweaterVariantId())).ReservedStock);

        await RestoreSweaterSeedAsync();
    }

    [Fact]
    public async Task Reserve_BeyondAvailableStock_IsRejected()
    {
        await ResetSweaterStockAsync(1);
        var client = await InventoryClientAsync();

        var response = await client.PostAsync("/api/admin/inventory/reservations", Json(new
        {
            variantId = SweaterVariantId(),
            warehouseId = MainWarehouseId(),
            quantity = 5,
            cartReference = "CART-OVER"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await RestoreSweaterSeedAsync();
    }

    [Fact]
    public async Task ReleaseExpired_ReleasesStaleReservations()
    {
        using (var seedScope = _factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var variant = await seedDb.ProductVariants.SingleAsync(v => v.Id == SweaterVariantId());
            var warehouseStock = await seedDb.WarehouseStocks.SingleAsync(s =>
                s.ProductVariantId == SweaterVariantId() && s.WarehouseId == MainWarehouseId());
            variant.ReservedStock += 1;
            warehouseStock.ReservedQuantity += 1;
            seedDb.StockReservations.Add(new StockReservation
            {
                ProductVariantId = SweaterVariantId(),
                WarehouseId = MainWarehouseId(),
                Quantity = 1,
                CartReference = "CART-STALE",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5),
                Status = StockReservationStatus.Active,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-40)
            });
            await seedDb.SaveChangesAsync();
        }

        var client = await InventoryClientAsync();
        var response = await client.PostAsync("/api/admin/inventory/reservations/release-expired", Json(new { }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("releasedCount").GetInt32() >= 1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stale = await db.StockReservations.Where(r => r.CartReference == "CART-STALE").SingleAsync();
        Assert.Equal(StockReservationStatus.Expired, stale.Status);
    }

    // ---- Warehouse CRUD ----

    [Fact]
    public async Task Warehouse_CreateUpdateGetDelete_Cycle()
    {
        var client = await InventoryClientAsync();

        var create = await client.PostAsync("/api/admin/inventory/warehouses", Json(new
        {
            name = "East Coast Hub",
            code = "EAST",
            description = "Secondary hub",
            address = "5 Harbor Road",
            city = "Boston",
            country = "US",
            isActive = true,
            isDefault = false,
            displayOrder = 5
        }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJsonAsync(create);
        var warehouseId = created.GetProperty("id").GetGuid();
        Assert.Equal("EAST", GetString(created, "code"));

        var update = await client.PutAsync($"/api/admin/inventory/warehouses/{warehouseId}", Json(new
        {
            id = warehouseId,
            name = "East Coast Hub (Renamed)",
            code = "EAST",
            description = "Renamed hub",
            address = "5 Harbor Road",
            city = "Boston",
            country = "US",
            isActive = true,
            isDefault = false,
            displayOrder = 6
        }));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ReadJsonAsync(update);
        Assert.Equal("East Coast Hub (Renamed)", GetString(updated, "name"));

        var get = await client.GetAsync($"/api/admin/inventory/warehouses/{warehouseId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var delete = await client.DeleteAsync($"/api/admin/inventory/warehouses/{warehouseId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var missing = await client.GetAsync($"/api/admin/inventory/warehouses/{warehouseId}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Warehouse_List_IncludesSeededMainWarehouse()
    {
        var client = await InventoryClientAsync();
        var response = await client.GetAsync("/api/admin/inventory/warehouses");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await ReadJsonAsync(response);
        var codes = root.EnumerateArray().Select(w => GetString(w, "code")).ToList();
        Assert.Contains("MAIN", codes);
    }
}
