using System.Net;
using System.Security.Claims;
using FashionStore.Application.Configuration;
using FashionStore.Application.DTOs.Settings;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FashionStore.IntegrationTests;

public class ContentManagementPageTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private const string Password = "AdminTest!pass1";

    public ContentManagementPageTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"content-admin-{Guid.NewGuid():N}@example.com";

    private static void AssertRedirectedTo(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(path, response.Headers.Location?.ToString());
    }

    private async Task<(string Email, string UserId)> CreateAdminRoleUserAsync(params string[] permissions)
    {
        var email = UniqueEmail();
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        if (await roleManager.FindByNameAsync("Admin") is null)
        {
            await roleManager.CreateAsync(new ApplicationRole
            {
                Name = "Admin",
                Description = "Administrative access",
                IsSystemRole = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var result = await userManager.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, "Admin");

        foreach (var permission in permissions)
        {
            await userManager.AddClaimAsync(user, new Claim("permission", permission));
        }

        return (email, user.Id);
    }

    private async Task<HttpClient> LoggedInClientAsync(string email)
    {
        var client = CreateClient();
        var loginHtml = await client.GetStringAsync("/Account/Login");
        var token = CartTestsHelper.ExtractAntiforgeryToken(loginHtml);
        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["EmailOrUserName"] = email,
                ["Password"] = Password
            })
        };
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private async Task SetMaintenanceModeAsync(bool enabled, bool superAdmin = true)
    {
        using var scope = _factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<FashionStore.Application.Interfaces.IWebsiteSettingsService>();
        var result = await settings.UpdateSettingsAsync(
            new UpdateWebsiteSettingsRequest(
                Store: null,
                Branding: null,
                Contact: null,
                Commerce: null,
                Checkout: null,
                Orders: null,
                Seo: null,
                Maintenance: new MaintenanceSection(enabled, enabled ? "We'll be back soon." : string.Empty),
                Reviews: null),
            "integration-test",
            superAdmin,
            CancellationToken.None);
        Assert.True(result.Success, result.ErrorMessage);
    }

    private async Task EnsurePublishedAboutPageAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!await db.ContentPages.AnyAsync(p => p.Slug == "about"))
        {
            db.ContentPages.Add(new ContentPage
            {
                Title = "About Us",
                Slug = "about",
                Summary = "About page",
                BodyHtml = "<p>Hello from the about page.</p>",
                Template = ContentPageTemplate.Default,
                Status = ContentStatus.Published,
                IsSystem = true,
                PublishedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = "integration-test"
            });
            await db.SaveChangesAsync();
        }
    }

    // ---- Permissions: content pages ----

    [Fact]
    public async Task AdminPages_RequiresContentPermission()
    {
        var (email, _) = await CreateAdminRoleUserAsync();
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/admin/content/pages");

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task AdminPages_AllowsContentPermission()
    {
        var (email, _) = await CreateAdminRoleUserAsync(ApplicationPermissions.Content.Manage);
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/admin/content/pages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Permissions: website settings ----

    [Fact]
    public async Task AdminSettings_RequiresSettingsPermission()
    {
        var (email, _) = await CreateAdminRoleUserAsync();
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/admin/settings");

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task AdminSettings_AllowsSettingsPermission()
    {
        var (email, _) = await CreateAdminRoleUserAsync(ApplicationPermissions.Settings.Manage);
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/admin/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Permissions: content API ----

    [Fact]
    public async Task ContentApi_RequiresPermission()
    {
        var (email, _) = await CreateAdminRoleUserAsync();
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/api/admin/content/pages");

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task ContentApi_AllowsPermission()
    {
        var (email, _) = await CreateAdminRoleUserAsync(ApplicationPermissions.Content.Manage);
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/api/admin/content/pages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SettingsApi_RequiresPermission()
    {
        var (email, _) = await CreateAdminRoleUserAsync();
        var client = await LoggedInClientAsync(email);

        var response = await client.GetAsync("/api/admin/settings");

        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    // ---- Storefront content ----

    [Fact]
    public async Task AboutPage_RendersWhenPublished()
    {
        await EnsurePublishedAboutPageAsync();
        await SetMaintenanceModeAsync(false);
        var client = CreateClient();

        var response = await client.GetAsync("/about");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("About Us", html);
    }

    // ---- Maintenance mode ----

    [Fact]
    public async Task MaintenanceMode_Returns503ForAnonymousVisitors()
    {
        await SetMaintenanceModeAsync(true);
        try
        {
            var client = CreateClient();
            var response = await client.GetAsync("/");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("Under Maintenance", html);
        }
        finally
        {
            await SetMaintenanceModeAsync(false);
        }
    }

    [Fact]
    public async Task MaintenanceMode_BypassedForAdminUsers()
    {
        var (email, _) = await CreateAdminRoleUserAsync(ApplicationPermissions.Dashboard.View);
        var client = await LoggedInClientAsync(email);

        await SetMaintenanceModeAsync(true);
        try
        {
            var response = await client.GetAsync("/");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await SetMaintenanceModeAsync(false);
        }
    }

    [Fact]
    public async Task MaintenanceMode_BypassedForAdminArea()
    {
        var (email, _) = await CreateAdminRoleUserAsync(ApplicationPermissions.Dashboard.View);
        var client = await LoggedInClientAsync(email);

        await SetMaintenanceModeAsync(true);
        try
        {
            var response = await client.GetAsync("/admin");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await SetMaintenanceModeAsync(false);
        }
    }
}
