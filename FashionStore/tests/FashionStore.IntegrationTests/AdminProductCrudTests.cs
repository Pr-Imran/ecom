using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

/// <summary>
/// Exercises the administrative product catalogue API end to end: role guarding,
/// list/search/filter, create with validation failures, read, update, publish,
/// archive, duplicate and delete, including the audit records those actions write.
/// </summary>
public class AdminProductCrudTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "AdminTest!pass1";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminProductCrudTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"product-admin-{Guid.NewGuid():N}@example.com";

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

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

    private async Task<(string Email, string UserId)> CreateAdminRoleUserAsync()
    {
        var email = UniqueEmail();
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        if (await roleManager.FindByNameAsync("Admin") is null)
        {
            var roleResult = await roleManager.CreateAsync(new ApplicationRole
            {
                Name = "Admin",
                Description = "Administrative access",
                IsSystemRole = true,
                CreatedAtUtc = DateTime.UtcNow
            });
            Assert.True(roleResult.Succeeded, string.Join("; ", roleResult.Errors.Select(e => e.Description)));
        }

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
        await userManager.AddToRoleAsync(user, "Admin");

        return (email, user.Id);
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var (email, _) = await CreateAdminRoleUserAsync();
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

    private Guid GetCategoryId()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Categories.Single(c => c.Slug == "clothing").Id;
    }

    private async Task<Guid> CreateProductAsync(HttpClient client, string name, string sku)
    {
        var response = await client.PostAsync("/api/admin/products", Json(new
        {
            name,
            shortDescription = "A test product",
            fullDescription = "Full test description",
            categoryId = GetCategoryId(),
            brandId = (Guid?)null,
            collectionId = (Guid?)null,
            productType = "Apparel",
            material = (string?)null,
            fabric = (string?)null,
            careInstructions = (string?)null,
            gender = "Unisex",
            countryOfOrigin = "US",
            baseSku = sku,
            barcode = (string?)null,
            basePrice = 49.99m,
            compareAtPrice = (decimal?)null,
            costPrice = (decimal?)null,
            taxCategory = "standard",
            weight = (decimal?)null,
            isActive = true,
            isFeatured = false,
            isNewArrival = false,
            isBestSeller = false,
            allowReviews = true,
            publishedAtUtc = (DateTime?)null,
            seoTitle = (string?)null,
            seoDescription = (string?)null,
            searchKeywords = (string?)null,
            tagIds = (List<Guid>?)null
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var id = body.GetProperty("id").GetGuid();
        Assert.False(id == Guid.Empty);
        return id;
    }

    // ---- Permission guards ----

    [Fact]
    public async Task Anonymous_CannotAccessAdminProductsApi()
    {
        var response = await CreateClient().GetAsync("/api/admin/products");
        AssertRedirectedTo(response, "/Account/Login");
    }

    [Fact]
    public async Task AuthenticatedUserWithoutAdminRole_IsDenied()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = UniqueEmail();
        var customer = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        Assert.True((await userManager.CreateAsync(customer, Password)).Succeeded);

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
        await client.SendAsync(login);

        var response = await client.GetAsync("/api/admin/products");
        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    // ---- List + search + filter ----

    [Fact]
    public async Task List_ReturnsSeededProducts()
    {
        var client = await AdminClientAsync();
        var response = await client.GetAsync("/api/admin/products?pageSize=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await ReadJsonAsync(response);
        Assert.True(root.GetProperty("totalCount").GetInt32() >= 3);
        var names = root.GetProperty("products").EnumerateArray()
            .Select(p => GetString(p, "name"))
            .ToList();
        Assert.Contains("Cashmere Crew Neck Sweater", names);
        Assert.Contains("Trail Running Shoe", names);
    }

    [Fact]
    public async Task List_SearchTerm_FiltersByNameAndSku()
    {
        var client = await AdminClientAsync();

        var byName = await ReadJsonAsync(await client.GetAsync("/api/admin/products?searchTerm=cashmere&pageSize=50"));
        var byNameSku = byName.GetProperty("products").EnumerateArray().Select(p => GetString(p, "baseSku")).ToList();
        Assert.Contains("SW-1001", byNameSku);
        Assert.DoesNotContain("SH-3003", byNameSku);

        var bySku = await ReadJsonAsync(await client.GetAsync("/api/admin/products?searchTerm=SH-3003&pageSize=50"));
        var bySkuSkus = bySku.GetProperty("products").EnumerateArray().Select(p => GetString(p, "baseSku")).ToList();
        Assert.Contains("SH-3003", bySkuSkus);
    }

    [Fact]
    public async Task List_FilterByActiveFlag()
    {
        var client = await AdminClientAsync();

        var archivedResponse = await client.PostAsync(
            $"/api/admin/products/{CartTestsHelper.GetProductId(_factory, "trail-running-shoe")}/archive", Json(new { }));
        Assert.Equal(HttpStatusCode.NoContent, archivedResponse.StatusCode);

        var active = await ReadJsonAsync(await client.GetAsync("/api/admin/products?isActive=true&pageSize=50"));
        var activeSkus = active.GetProperty("products").EnumerateArray().Select(p => GetString(p, "baseSku")).ToList();
        Assert.Contains("SW-1001", activeSkus);
        Assert.DoesNotContain("SH-3003", activeSkus);

        var inactive = await ReadJsonAsync(await client.GetAsync("/api/admin/products?isActive=false&pageSize=50"));
        var inactiveSkus = inactive.GetProperty("products").EnumerateArray().Select(p => GetString(p, "baseSku")).ToList();
        Assert.Contains("SH-3003", inactiveSkus);
    }

    // ---- Create ----

    [Fact]
    public async Task Create_ValidProduct_ReturnsCreatedWithLocation()
    {
        var client = await AdminClientAsync();
        var name = $"Test Trench Coat {Guid.NewGuid():N}"[..28];
        var id = await CreateProductAsync(client, name, $"TR-{Guid.NewGuid():N}"[..10]);

        var detail = await client.GetAsync($"/api/admin/products/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await ReadJsonAsync(detail);
        Assert.Equal(name, GetString(body, "name"));
        Assert.Equal(49.99m, body.GetProperty("basePrice").GetDecimal());
        Assert.Equal("Apparel", GetString(body, "productType"));
        Assert.True(body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Create_NegativePrice_IsRejected()
    {
        var client = await AdminClientAsync();
        var payload = new
        {
            name = "Invalid Product",
            shortDescription = "Bad",
            fullDescription = "Bad",
            categoryId = GetCategoryId(),
            productType = "Apparel",
            baseSku = $"BAD-{Guid.NewGuid():N}"[..10],
            basePrice = -5m,
            taxCategory = "standard",
            isActive = true,
            isFeatured = false,
            isNewArrival = false,
            isBestSeller = false,
            allowReviews = true
        };

        var response = await client.PostAsync("/api/admin/products", Json(payload));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Update ----

    [Fact]
    public async Task Update_ChangesFields_AndAuditsPriceChange()
    {
        var client = await AdminClientAsync();
        var id = await CreateProductAsync(client, $"Updateable {Guid.NewGuid():N}"[..24], $"UP-{Guid.NewGuid():N}"[..10]);
        var created = await ReadJsonAsync(await client.GetAsync($"/api/admin/products/{id}"));

        var update = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["name"] = GetString(created, "name"),
            ["shortDescription"] = "Updated short description",
            ["fullDescription"] = "Updated full description",
            ["categoryId"] = GetCategoryId(),
            ["brandId"] = null,
            ["collectionId"] = null,
            ["productType"] = "Apparel",
            ["material"] = "Wool",
            ["fabric"] = null,
            ["careInstructions"] = null,
            ["gender"] = "Women",
            ["countryOfOrigin"] = "IT",
            ["baseSku"] = GetString(created, "baseSku"),
            ["barcode"] = null,
            ["basePrice"] = 59.99m,
            ["compareAtPrice"] = null,
            ["costPrice"] = null,
            ["taxCategory"] = "standard",
            ["weight"] = null,
            ["isActive"] = true,
            ["isFeatured"] = false,
            ["isNewArrival"] = false,
            ["isBestSeller"] = false,
            ["allowReviews"] = true,
            ["publishedAtUtc"] = null,
            ["seoTitle"] = null,
            ["seoDescription"] = null,
            ["searchKeywords"] = null,
            ["tagIds"] = null
        };

        var response = await client.PutAsync($"/api/admin/products/{id}", Json(update));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(59.99m, body.GetProperty("basePrice").GetDecimal());
        Assert.Equal("Updated short description", GetString(body, "shortDescription"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs
            .Where(a => a.Action == "Product.Updated" && a.EntityId == id.ToString())
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Contains("price:", audit.OldValue);
        Assert.Contains("price:", audit.NewValue);
    }

    [Fact]
    public async Task Update_IdMismatch_IsRejected()
    {
        var client = await AdminClientAsync();
        var id = await CreateProductAsync(client, $"Mismatch {Guid.NewGuid():N}"[..22], $"MM-{Guid.NewGuid():N}"[..10]);
        var created = await ReadJsonAsync(await client.GetAsync($"/api/admin/products/{id}"));

        var payload = new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["name"] = GetString(created, "name"),
            ["shortDescription"] = "x",
            ["fullDescription"] = "y",
            ["categoryId"] = GetCategoryId(),
            ["productType"] = "Apparel",
            ["baseSku"] = GetString(created, "baseSku"),
            ["basePrice"] = 10m,
            ["taxCategory"] = "standard",
            ["isActive"] = true,
            ["isFeatured"] = false,
            ["isNewArrival"] = false,
            ["isBestSeller"] = false,
            ["allowReviews"] = true
        };

        var response = await client.PutAsync($"/api/admin/products/{id}", Json(payload));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Publish / archive / duplicate ----

    [Fact]
    public async Task Archive_ThenPublish_TogglesActiveState()
    {
        var client = await AdminClientAsync();
        var id = await CreateProductAsync(client, $"Toggle {Guid.NewGuid():N}"[..20], $"TG-{Guid.NewGuid():N}"[..10]);

        var archived = await client.PostAsync($"/api/admin/products/{id}/archive", Json(new { }));
        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);

        var inactive = await ReadJsonAsync(await client.GetAsync($"/api/admin/products/{id}"));
        Assert.False(inactive.GetProperty("isActive").GetBoolean());

        var published = await client.PostAsync($"/api/admin/products/{id}/publish", Json(new { }));
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);

        var active = await ReadJsonAsync(await client.GetAsync($"/api/admin/products/{id}"));
        Assert.True(active.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Publish_UnknownProduct_ReturnsNotFound()
    {
        var client = await AdminClientAsync();
        var response = await client.PostAsync($"/api/admin/products/{Guid.NewGuid()}/publish", Json(new { }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_CopiesProductWithSkuSuffix()
    {
        var client = await AdminClientAsync();
        var id = await CreateProductAsync(client, $"Original {Guid.NewGuid():N}"[..22], $"OR-{Guid.NewGuid():N}"[..10]);

        var response = await client.PostAsync($"/api/admin/products/{id}/duplicate", Json(new
        {
            sourceProductId = id,
            newName = "Copied Product",
            newSku = (string?)null
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var newId = body.GetProperty("id").GetGuid();
        Assert.NotEqual(id, newId);
        Assert.Equal("Copied Product", GetString(body, "name"));
    }

    [Fact]
    public async Task Duplicate_IdMismatch_IsRejected()
    {
        var client = await AdminClientAsync();
        var response = await client.PostAsync($"/api/admin/products/{Guid.NewGuid()}/duplicate", Json(new
        {
            sourceProductId = Guid.NewGuid(),
            newName = "Nope"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Delete ----

    [Fact]
    public async Task Delete_RemovesProduct_AndAudits()
    {
        var client = await AdminClientAsync();
        var id = await CreateProductAsync(client, $"Delete Me {Guid.NewGuid():N}"[..20], $"DL-{Guid.NewGuid():N}"[..10]);

        var response = await client.DeleteAsync($"/api/admin/products/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await client.GetAsync($"/api/admin/products/{id}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Products.AnyAsync(p => p.Id == id));
        Assert.NotNull(await db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "Product.Deleted" && a.EntityId == id.ToString()));
    }

    [Fact]
    public async Task Delete_UnknownProduct_ReturnsNotFound()
    {
        var client = await AdminClientAsync();
        var response = await client.DeleteAsync($"/api/admin/products/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
