using System.Net;
using System.Text;
using System.Text.Json;
using FashionStore.Domain.Entities;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FashionStore.Web.Models;

namespace FashionStore.IntegrationTests;

public class WishlistTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WishlistTests(TestWebApplicationFactory factory)
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

    [Fact]
    public async Task Wishlist_Anonymous_EmptyStateRenders()
    {
        var client = CreateClient();
        var html = await client.GetStringAsync("/wishlist");

        Assert.Contains("wishlist", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wishlist_Anonymous_AddAppendsCookieAndShowsItem()
    {
        var client = CreateClient();
        var html = await client.GetStringAsync("/wishlist");
        var token = ExtractAntiforgeryToken(html);

        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");
        var payload = JsonSerializer.Serialize(new { productId, variantId });
        var request = new HttpRequestMessage(HttpMethod.Post, "/wishlist/add")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());

        var cookie = response.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.Contains("fashionstore_wishlist"));
        Assert.NotNull(cookie);

        var page = await client.GetStringAsync("/wishlist");
        Assert.Contains("Cashmere Crew Neck Sweater", page);
    }

    [Fact]
    public async Task Wishlist_Anonymous_AddThenRemoveClearsItem()
    {
        var client = CreateClient();
        var html = await client.GetStringAsync("/wishlist");
        var token = ExtractAntiforgeryToken(html);

        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");

        var addPayload = JsonSerializer.Serialize(new { productId, variantId });
        var addRequest = new HttpRequestMessage(HttpMethod.Post, "/wishlist/add")
        {
            Content = new StringContent(addPayload, Encoding.UTF8, "application/json")
        };
        addRequest.Headers.Add("RequestVerificationToken", token);
        await client.SendAsync(addRequest);

        var removePayload = JsonSerializer.Serialize(new { productId, variantId });
        var removeRequest = new HttpRequestMessage(HttpMethod.Post, "/wishlist/remove")
        {
            Content = new StringContent(removePayload, Encoding.UTF8, "application/json")
        };
        removeRequest.Headers.Add("RequestVerificationToken", token);
        var response = await client.SendAsync(removeRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Wishlist_Anonymous_RemoveByIdRequiresSignIn()
    {
        var client = CreateClient();
        var html = await client.GetStringAsync("/wishlist");
        var token = ExtractAntiforgeryToken(html);

        var payload = JsonSerializer.Serialize(new { wishlistItemId = Guid.NewGuid() });
        var request = new HttpRequestMessage(HttpMethod.Post, "/wishlist/remove-by-id")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RecentlyViewed_DetailsSetsCookieAndPageRenders()
    {
        var client = CreateClient();
        await client.GetAsync("/products/cashmere-crew-neck-sweater");

        var html = await client.GetStringAsync("/products/recently-viewed");

        Assert.Contains("Cashmere Crew Neck Sweater", html);
        Assert.Contains("recently", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecentlyViewed_PageAllowsClearingHistory()
    {
        var client = CreateClient();
        await client.GetAsync("/products/cashmere-crew-neck-sweater");
        var html = await client.GetStringAsync("/products/recently-viewed");
        var token = ExtractAntiforgeryToken(html);

        var request = new HttpRequestMessage(HttpMethod.Post, "/products/recently-viewed/clear");
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.Contains("fashionstore_recently_viewed"));
        Assert.NotNull(cookie);
    }

    [Fact]
    public void RecentlyViewedCookie_DedupesAndOrdersMostRecentFirst()
    {
        var cookie = new RecentViewedCookieTestHarness();
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();
        var three = Guid.NewGuid();

        cookie.Append(one);
        cookie.Append(two);
        cookie.Append(three);
        cookie.Append(two);

        var ids = cookie.Read();

        Assert.Equal(two, ids[0]);
        Assert.Equal(three, ids[1]);
        Assert.Equal(one, ids[2]);
    }

    [Fact]
    public void RecentlyViewedCookie_RetainsOnlyMostRecentTwelve()
    {
        var cookie = new RecentViewedCookieTestHarness();
        var ids = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids)
        {
            cookie.Append(id);
        }

        var result = cookie.Read();

        Assert.Equal(12, result.Count);
        Assert.Equal(ids[^1], result[0]);
        Assert.Equal(ids[^12], result[^1]);
    }

    [Fact]
    public void RecentlyViewedCookie_ClearEmptiesHistory()
    {
        var cookie = new RecentViewedCookieTestHarness();
        cookie.Append(Guid.NewGuid());
        cookie.Append(Guid.NewGuid());

        cookie.Clear();

        Assert.Empty(cookie.Read());
    }

    private sealed class RecentViewedCookieTestHarness
    {
        private readonly DefaultHttpContext _context = new();
        private string? _cookieValue;

        public void Append(Guid productId)
        {
            SyncRequestCookie();
            RecentlyViewedCookie.Append(_context, productId);
            CaptureCookie();
        }

        public IReadOnlyList<Guid> Read()
        {
            SyncRequestCookie();
            return RecentlyViewedCookie.Read(_context);
        }

        public void Clear()
        {
            RecentlyViewedCookie.Clear(_context);
            _cookieValue = null;
            _context.Request.Headers.Remove("Cookie");
        }

        private void SyncRequestCookie()
        {
            if (!string.IsNullOrEmpty(_cookieValue))
            {
                _context.Request.Headers["Cookie"] = $"{RecentlyViewedCookie.CookieName}={_cookieValue}";
            }
        }

        private void CaptureCookie()
        {
            var setCookie = _context.Response.Headers["Set-Cookie"].LastOrDefault();
            if (setCookie is null)
            {
                _cookieValue = null;
                return;
            }

            var pair = setCookie.Split(';').First().Trim();
            var nameValue = pair.Split('=', 2);
            _cookieValue = nameValue.Length == 2 ? nameValue[1] : null;
        }
    }

    [Fact]
    public async Task Wishlist_AnonymousItem_MergedAfterLogin()
    {
        var client = CreateClient();

        var (productId, variantId) = GetIds("cashmere-crew-neck-sweater", "SW-1001-GREY-M");

        var pageHtml = await client.GetStringAsync("/wishlist");
        var pageToken = ExtractAntiforgeryToken(pageHtml);
        var addPayload = JsonSerializer.Serialize(new { productId, variantId });
        var addRequest = new HttpRequestMessage(HttpMethod.Post, "/wishlist/add")
        {
            Content = new StringContent(addPayload, Encoding.UTF8, "application/json")
        };
        addRequest.Headers.Add("RequestVerificationToken", pageToken);
        var addResponse = await client.SendAsync(addRequest);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var email = $"merge-{Guid.NewGuid():N}@example.com";
        var password = "Merg3Test!pass";
        await CreateConfirmedUserAsync(email, password);

        var loginHtml = await client.GetStringAsync("/Account/Login");
        var loginToken = ExtractAntiforgeryToken(loginHtml);
        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = loginToken,
                ["EmailOrUserName"] = email,
                ["Password"] = password
            })
        };
        var loginResponse = await client.SendAsync(loginRequest);

        if (loginResponse.StatusCode != HttpStatusCode.Redirect)
        {
            var body = await loginResponse.Content.ReadAsStringAsync();
            Assert.Fail($"Login failed with {loginResponse.StatusCode}: {body}");
        }

        var wishlistHtml = await client.GetStringAsync("/wishlist");
        Assert.Contains("Cashmere Crew Neck Sweater", wishlistHtml);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        var persisted = await db.WishlistItems.Where(w => w.UserId == user.Id).ToListAsync();
        Assert.Single(persisted);
        Assert.Equal(productId, persisted[0].ProductId);
        Assert.Equal(variantId, persisted[0].ProductVariantId);
    }

    private async Task CreateConfirmedUserAsync(string email, string password)
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
}
