using System.Net;
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
/// Exercises the administrative variation catalogue API end to end: role guards,
/// product-attribute CRUD, attribute-value CRUD, variant CRUD, SKU uniqueness and
/// the storefront-facing variation endpoints.
/// </summary>
public class AdminVariationCrudTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "AdminTest!pass1";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminVariationCrudTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"variation-admin-{Guid.NewGuid():N}@example.com";

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

    private async Task<HttpClient> AdminClientAsync()
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

    private Guid SweaterProductId() => CartTestsHelper.GetProductId(_factory, "cashmere-crew-neck-sweater");

    private Guid SeedAttributeValueId(string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.ProductAttributeValues.Single(v => v.Slug == slug).Id;
    }

    // ---- Permission guards ----

    [Fact]
    public async Task Anonymous_CannotAccessAdminAttributesApi()
    {
        var response = await CreateClient().GetAsync("/api/admin/attributes");
        AssertRedirectedTo(response, "/Account/Login");
    }

    [Fact]
    public async Task StorefrontVariationEndpoints_AreAnonymous()
    {
        var client = CreateClient();
        var response = await client.GetAsync($"/api/admin/products/{SweaterProductId()}/storefront-variations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Attribute CRUD ----

    [Fact]
    public async Task Attribute_CreateGetListUpdateDelete_Cycle()
    {
        var client = await AdminClientAsync();
        var name = $"Fabric {Guid.NewGuid():N}"[..20];

        var create = await client.PostAsync("/api/admin/attributes", Json(new
        {
            name,
            displayType = "Text",
            isVariationAttribute = true,
            displayOrder = 10,
            description = "Fabric type"
        }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJsonAsync(create);
        var attributeId = created.GetProperty("id").GetGuid();
        Assert.Equal(name, GetString(created, "name"));

        var get = await client.GetAsync($"/api/admin/attributes/{attributeId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(name, GetString(await ReadJsonAsync(get), "name"));

        var list = await ReadJsonAsync(await client.GetAsync("/api/admin/attributes?includeInactive=true"));
        var names = list.EnumerateArray().Select(a => GetString(a, "name")).ToList();
        Assert.Contains("Colour", names);
        Assert.Contains(name, names);

        var update = await client.PutAsync($"/api/admin/attributes/{attributeId}", Json(new
        {
            id = attributeId,
            name = name + " 2",
            displayType = "Text",
            isVariationAttribute = true,
            displayOrder = 11,
            description = "Updated"
        }));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(name + " 2", GetString(await ReadJsonAsync(update), "name"));

        var delete = await client.DeleteAsync($"/api/admin/attributes/{attributeId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var missing = await client.GetAsync($"/api/admin/attributes/{attributeId}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Attribute_CreateDuplicateSlug_IsRejected()
    {
        var client = await AdminClientAsync();
        var response = await client.PostAsync("/api/admin/attributes", Json(new
        {
            name = "Colour",
            displayType = "Swatch",
            isVariationAttribute = true,
            displayOrder = 99,
            description = "Duplicate"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Attribute value CRUD ----

    [Fact]
    public async Task AttributeValue_CreateUpdateDelete_Cycle()
    {
        var client = await AdminClientAsync();
        var attributeId = await CreateAttributeIdAsync(client, $"Fabric {Guid.NewGuid():N}"[..20]);

        var create = await client.PostAsync("/api/admin/attribute-values", Json(new
        {
            productAttributeId = attributeId,
            name = "Wool",
            displayValue = "Wool",
            hexColour = (string?)null,
            imageUrl = (string?)null,
            displayOrder = 1
        }));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await ReadJsonAsync(create);
        var valueId = created.GetProperty("id").GetGuid();
        Assert.Equal("Wool", GetString(created, "name"));

        var update = await client.PutAsync($"/api/admin/attribute-values/{valueId}", Json(new
        {
            id = valueId,
            name = "Merino Wool",
            displayValue = "Merino Wool",
            hexColour = (string?)null,
            imageUrl = (string?)null,
            displayOrder = 1
        }));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Merino Wool", GetString(await ReadJsonAsync(update), "name"));

        var delete = await client.DeleteAsync($"/api/admin/attribute-values/{valueId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task AttributeValue_CreateDuplicateSlug_IsRejected()
    {
        var client = await AdminClientAsync();
        var attributeId = await CreateAttributeIdAsync(client, $"Fabric {Guid.NewGuid():N}"[..20]);

        var first = await client.PostAsync("/api/admin/attribute-values", Json(new
        {
            productAttributeId = attributeId,
            name = "Cotton",
            displayValue = "Cotton",
            displayOrder = 1
        }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var duplicate = await client.PostAsync("/api/admin/attribute-values", Json(new
        {
            productAttributeId = attributeId,
            name = "Cotton",
            displayValue = "Cotton",
            displayOrder = 2
        }));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    private static async Task<Guid> CreateAttributeIdAsync(HttpClient client, string name)
    {
        var response = await client.PostAsync("/api/admin/attributes", Json(new
        {
            name,
            displayType = "Text",
            isVariationAttribute = true,
            displayOrder = 20,
            description = (string?)null
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    // ---- Variant CRUD ----

    [Fact]
    public async Task Variant_CreateGetListUpdateDelete_Cycle()
    {
        var client = await AdminClientAsync();
        var productId = SweaterProductId();
        var sku = $"SW-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var sizeL = SeedAttributeValueId("m");

        var create = await client.PostAsync("/api/admin/variants", Json(new
        {
            productId,
            sku,
            barcode = (string?)null,
            price = 138.00m,
            compareAtPrice = (decimal?)null,
            costPrice = (decimal?)null,
            weight = (decimal?)null,
            isDefault = false,
            isActive = true,
            stockQuantity = 5,
            imageUrl = (string?)null,
            notes = "Test variant",
            attributeValueIds = new[] { sizeL }
        }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJsonAsync(create);
        var variantId = created.GetProperty("id").GetGuid();
        Assert.Equal(sku, GetString(created, "sku"));
        Assert.Equal(138.00m, created.GetProperty("price").GetDecimal());

        var list = await ReadJsonAsync(await client.GetAsync($"/api/admin/products/{productId}/variants"));
        var skus = list.EnumerateArray().Select(v => GetString(v, "sku")).ToList();
        Assert.Contains(sku, skus);

        var get = await client.GetAsync($"/api/admin/variants/{variantId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var fetched = await ReadJsonAsync(get);
        Assert.Equal(sku, GetString(fetched, "sku"));
        Assert.Equal(5, fetched.GetProperty("stockQuantity").GetInt32());

        var update = await client.PutAsync($"/api/admin/variants/{variantId}", Json(new
        {
            id = variantId,
            productId,
            sku,
            barcode = (string?)null,
            price = 148.00m,
            compareAtPrice = (decimal?)null,
            costPrice = (decimal?)null,
            weight = (decimal?)null,
            isDefault = false,
            isActive = true,
            stockQuantity = 8,
            imageUrl = (string?)null,
            notes = "Updated variant",
            attributeValueIds = new[] { sizeL }
        }));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ReadJsonAsync(update);
        Assert.Equal(148.00m, updated.GetProperty("price").GetDecimal());

        var delete = await client.DeleteAsync($"/api/admin/variants/{variantId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var missing = await client.GetAsync($"/api/admin/variants/{variantId}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Variant_DuplicateSku_IsRejected()
    {
        var client = await AdminClientAsync();
        var response = await client.PostAsync("/api/admin/variants", Json(new
        {
            productId = SweaterProductId(),
            sku = "SW-1001-GREY-M",
            price = 10m,
            isDefault = false,
            isActive = true,
            attributeValueIds = Array.Empty<Guid>()
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SkuUniqueness_ReportsExistingAndNewSku()
    {
        var client = await AdminClientAsync();

        var existing = await ReadJsonAsync(await client.GetAsync("/api/admin/variants/sku-unique?sku=SW-1001-GREY-M"));
        Assert.False(existing.GetProperty("isUnique").GetBoolean());

        var newSku = $"NEW-{Guid.NewGuid():N}"[..20];
        var unique = await ReadJsonAsync(await client.GetAsync($"/api/admin/variants/sku-unique?sku={newSku}"));
        Assert.True(unique.GetProperty("isUnique").GetBoolean());
    }

    [Fact]
    public async Task VariantByValues_MatchesSeededCombination()
    {
        var client = CreateClient();
        var grey = SeedAttributeValueId("heather-grey");
        var medium = SeedAttributeValueId("m");

        var response = await client.GetAsync(
            $"/api/admin/products/{SweaterProductId()}/variant-by-values?attributeValueIds={grey}&attributeValueIds={medium}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var variant = await ReadJsonAsync(response);
        Assert.Equal("SW-1001-GREY-M", GetString(variant, "sku"));
    }
}
