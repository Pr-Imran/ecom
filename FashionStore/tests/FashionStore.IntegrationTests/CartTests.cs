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

public class CartTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CartTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private (Guid productId, Guid variantId) GetIds(string productSlug, string variantSku)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = db.Products.Single(p => p.Slug == productSlug);
        var variant = db.ProductVariants.Single(v => v.Sku == variantSku);
        return (product.Id, variant.Id);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        if (!match.Success)
        {
            match = System.Text.RegularExpressions.Regex.Match(
                html,
                @"value=""([^""]+)""[^>]*name=""__RequestVerificationToken""");
        }
        Assert.True(match.Success, "Antiforgery token not found in HTML");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task Cart_Anonymous_EmptyStateRenders()
    {
        var client = CreateClient();
        var html = await client.GetStringAsync("/cart");

        Assert.Contains("My Cart", html);
        Assert.Contains("Your cart is empty", html);
        Assert.Contains("Start shopping", html);
        Assert.Contains("data-cart", html);
    }

    [Fact]
    public async Task Cart_Anonymous_AddSetsCookieAndShowsItem()
    {
        var client = CreateClient();
        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");

        var pageHtml = await client.GetStringAsync("/cart");
        var token = ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { productId, variantId, quantity = 2 });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());

        var cookie = response.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.Contains("fashionstore_cart"));
        Assert.NotNull(cookie);

        var page = await client.GetStringAsync("/cart");
        Assert.Contains("Cashmere Crew Neck Sweater", page);
        Assert.Contains("Heather Grey", page);
        Assert.Contains("SW-1001-GREY-M", page);
    }

    [Fact]
    public async Task Cart_Anonymous_CountReflectsCookie()
    {
        var client = CreateClient();
        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");

        var pageHtml = await client.GetStringAsync("/cart");
        var token = ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { productId, variantId, quantity = 3 });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        await client.SendAsync(request);

        var countBody = await client.GetStringAsync("/cart/count");
        using var doc = JsonDocument.Parse(countBody);
        Assert.Equal(3, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Cart_Anonymous_AddOutOfStockVariantFails()
    {
        var client = CreateClient();
        var (productId, variantId) = GetIds("trail-running-shoe", "SH-3003-BLK-09");

        var pageHtml = await client.GetStringAsync("/cart");
        var token = ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { productId, variantId, quantity = 1 });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Cart_Anonymous_UpdateAndRemove()
    {
        var client = CreateClient();
        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");

        var pageHtml = await client.GetStringAsync("/cart");
        var token = ExtractAntiforgeryToken(pageHtml);

        var add = JsonSerializer.Serialize(new { productId, variantId, quantity = 2 });
        var addRequest = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(add, Encoding.UTF8, "application/json")
        };
        addRequest.Headers.Add("RequestVerificationToken", token);
        await client.SendAsync(addRequest);

        var update = JsonSerializer.Serialize(new { productId, variantId, quantity = 5 });
        var updateRequest = new HttpRequestMessage(HttpMethod.Post, "/cart/update")
        {
            Content = new StringContent(update, Encoding.UTF8, "application/json")
        };
        updateRequest.Headers.Add("RequestVerificationToken", token);
        var updateResponse = await client.SendAsync(updateRequest);

        var updateBody = await updateResponse.Content.ReadAsStringAsync();
        using var updateDoc = JsonDocument.Parse(updateBody);
        Assert.True(updateDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(5, updateDoc.RootElement.GetProperty("count").GetInt32());

        var remove = JsonSerializer.Serialize(new { productId, variantId });
        var removeRequest = new HttpRequestMessage(HttpMethod.Post, "/cart/remove")
        {
            Content = new StringContent(remove, Encoding.UTF8, "application/json")
        };
        removeRequest.Headers.Add("RequestVerificationToken", token);
        var removeResponse = await client.SendAsync(removeRequest);

        var removeBody = await removeResponse.Content.ReadAsStringAsync();
        using var removeDoc = JsonDocument.Parse(removeBody);
        Assert.True(removeDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, removeDoc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Cart_Anonymous_MiniCartRenders()
    {
        var client = CreateClient();
        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");

        var pageHtml = await client.GetStringAsync("/cart");
        var token = ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { productId, variantId, quantity = 1 });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        await client.SendAsync(request);

        var html = await client.GetStringAsync("/cart/mini");

        Assert.Contains("Cashmere Crew Neck Sweater", html);
        Assert.Contains("View Cart", html);
        Assert.Contains("Checkout", html);
    }

    [Fact]
    public async Task Cart_Authenticated_AddPersistsToDatabase()
    {
        var client = CreateClient();
        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");

        var email = $"cart-{Guid.NewGuid():N}@example.com";
        var password = "CartT3st!pass";
        var userId = await CreateConfirmedUserAsync(email, password);
        await LoginAsync(client, email, password);

        var pageHtml = await client.GetStringAsync("/cart");
        var token = ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { productId, variantId, quantity = 2 });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var line = await db.CartItems.SingleAsync(c => c.UserId == userId);
        Assert.Equal(productId, line.ProductId);
        Assert.Equal(variantId, line.ProductVariantId);
        Assert.Equal(2, line.Quantity);
    }

    [Fact]
    public async Task Cart_Authenticated_CountReflectsDatabase()
    {
        var client = CreateClient();
        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");

        var email = $"cart-{Guid.NewGuid():N}@example.com";
        var password = "CartT3st!pass";
        var userId = await CreateConfirmedUserAsync(email, password);
        await LoginAsync(client, email, password);

        var pageHtml = await client.GetStringAsync("/cart");
        var token = ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { productId, variantId, quantity = 4 });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        await client.SendAsync(request);

        var countBody = await client.GetStringAsync("/cart/count");
        using var doc = JsonDocument.Parse(countBody);
        Assert.Equal(4, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Cart_Authenticated_OwnershipIsIsolated()
    {
        var client = CreateClient();
        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");

        var emailA = $"cart-a-{Guid.NewGuid():N}@example.com";
        var emailB = $"cart-b-{Guid.NewGuid():N}@example.com";
        var password = "CartT3st!pass";
        var userIdA = await CreateConfirmedUserAsync(emailA, password);
        await CreateConfirmedUserAsync(emailB, password);

        await LoginAsync(client, emailA, password);
        var pageHtml = await client.GetStringAsync("/cart");
        var token = ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { productId, variantId, quantity = 2 });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        await client.SendAsync(request);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userB = await db.Users.SingleAsync(u => u.Email == emailB);
        var linesForB = await db.CartItems.Where(c => c.UserId == userB.Id).ToListAsync();
        Assert.Empty(linesForB);

        var linesForA = await db.CartItems.Where(c => c.UserId == userIdA).ToListAsync();
        Assert.Single(linesForA);
    }

    [Fact]
    public async Task Cart_Anonymous_MergedAfterLogin()
    {
        var client = CreateClient();
        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");

        var pageHtml = await client.GetStringAsync("/cart");
        var token = ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { productId, variantId, quantity = 2 });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        await client.SendAsync(request);

        var email = $"cart-merge-{Guid.NewGuid():N}@example.com";
        var password = "Merg3Test!pass";
        var userId = await CreateConfirmedUserAsync(email, password);
        await LoginAsync(client, email, password);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lines = await db.CartItems.Where(c => c.UserId == userId).ToListAsync();
        Assert.Single(lines);
        Assert.Equal(2, lines[0].Quantity);
    }

    [Fact]
    public async Task Cart_Anonymous_InvalidRequestIsRejected()
    {
        var client = CreateClient();
        var pageHtml = await client.GetStringAsync("/cart");
        var token = ExtractAntiforgeryToken(pageHtml);

        var payload = JsonSerializer.Serialize(new { productId = Guid.NewGuid(), variantId = Guid.Empty, quantity = 1 });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cart_Authenticated_StockLimitIsEnforced()
    {
        var client = CreateClient();
        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");

        var email = $"cart-{Guid.NewGuid():N}@example.com";
        var password = "CartT3st!pass";
        await CreateConfirmedUserAsync(email, password);
        await LoginAsync(client, email, password);

        var pageHtml = await client.GetStringAsync("/cart");
        var token = ExtractAntiforgeryToken(pageHtml);

        var addPayload = JsonSerializer.Serialize(new { productId, variantId, quantity = 1 });
        var addRequest = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(addPayload, Encoding.UTF8, "application/json")
        };
        addRequest.Headers.Add("RequestVerificationToken", token);
        await client.SendAsync(addRequest);

        // Stock is 10; ask for 12.
        var payload = JsonSerializer.Serialize(new { productId, variantId, quantity = 12 });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/update")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        var response = await client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("stock", doc.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cart_Authenticated_InactiveVariantIsFlaggedOnRead()
    {
        var client = CreateClient();

        Guid productId;
        Guid variantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = await db.Categories.FirstAsync();
            var product = new Product
            {
                Name = "Inactive Test Tee",
                Slug = $"inactive-test-tee-{Guid.NewGuid():N}",
                CategoryId = category.Id,
                BaseSku = "IT-9001",
                BasePrice = 19.99m,
                IsActive = true,
                PublishedAtUtc = DateTime.UtcNow.AddDays(-1)
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            var variant = new ProductVariant
            {
                ProductId = product.Id,
                Sku = "IT-9001-M",
                Price = 19.99m,
                IsActive = true,
                StockQuantity = 5,
                ReservedStock = 0
            };
            db.ProductVariants.Add(variant);
            await db.SaveChangesAsync();
            productId = product.Id;
            variantId = variant.Id;
        }

        var email = $"cart-{Guid.NewGuid():N}@example.com";
        var password = "CartT3st!pass";
        await CreateConfirmedUserAsync(email, password);
        await LoginAsync(client, email, password);

        var pageHtml = await client.GetStringAsync("/cart");
        var token = ExtractAntiforgeryToken(pageHtml);
        var payload = JsonSerializer.Serialize(new { productId, variantId, quantity = 1 });
        var request = new HttpRequestMessage(HttpMethod.Post, "/cart/add")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        await client.SendAsync(request);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var variant = await db.ProductVariants.SingleAsync(v => v.Id == variantId);
            variant.IsActive = false;
            await db.SaveChangesAsync();
        }

        var html = await client.GetStringAsync("/cart");
        Assert.Contains("unavailable", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-cart-item", html);
    }

    private async Task<string> CreateConfirmedUserAsync(string email, string password)
    {
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
        var result = await userManager.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return user.Id;
    }

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var loginHtml = await client.GetStringAsync("/Account/Login");
        var token = ExtractAntiforgeryToken(loginHtml);
        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["EmailOrUserName"] = email,
                ["Password"] = password
            })
        };
        var response = await client.SendAsync(request);

        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Login failed with {response.StatusCode}: {body}");
        }
    }
}
