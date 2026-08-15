using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using FashionStore.Infrastructure.Data;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

public class EmailAdminPageTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private const string Password = "AdminTest!pass1";

    public EmailAdminPageTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"email-admin-{Guid.NewGuid():N}@example.com";

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

    private async Task<(string Email, string UserId)> CreatePermissionUserAsync(params string[] permissions)
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
        var result = await userManager.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));

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

    private async Task<Guid> SeedEmailAsync(string toEmail, EmailStatus status = EmailStatus.Pending, int attempts = 0, string subject = "Test subject")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = new EmailMessage
        {
            ToEmail = toEmail,
            Subject = subject,
            BodyHtml = "<html><body>Hello</body></html>",
            Status = status,
            AttemptCount = attempts,
            MaxAttempts = 5,
            NextAttemptAtUtc = status == EmailStatus.Pending ? DateTime.UtcNow.AddMinutes(-1) : null,
            LastError = status == EmailStatus.Failed ? "permanent failure" : null,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.EmailMessages.Add(email);
        await db.SaveChangesAsync();
        return email.Id;
    }

    private static JsonElement ReadJson(HttpResponseMessage response)
    {
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static int? GetCount(JsonElement root)
    {
        return root.TryGetProperty("totalCount", out var value) ? value.GetInt32() : null;
    }

    // ---- HTML page authorization ----

    [Fact]
    public async Task EmailsPage_Anonymous_RedirectsToLogin()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/admin/emails");
        AssertRedirectedTo(response, "/Account/Login");
    }

    [Fact]
    public async Task EmailsPage_NonAdminWithPermission_RedirectsToAccessDenied()
    {
        var (email, _) = await CreatePermissionUserAsync("Emails.Manage");
        var client = await LoggedInClientAsync(email);
        var response = await client.GetAsync("/admin/emails");
        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task EmailsPage_AdminWithoutPermission_RedirectsToAccessDenied()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Orders.View");
        var client = await LoggedInClientAsync(email);
        var response = await client.GetAsync("/admin/emails");
        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task EmailsPage_AdminWithPermission_ReturnsOk()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Emails.Manage");
        var client = await LoggedInClientAsync(email);
        var response = await client.GetAsync("/admin/emails");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Emails", html);
    }

    [Fact]
    public async Task EmailTemplatesPage_AdminWithPermission_RendersAllTemplateNames()
    {
        var (email, _) = await CreateAdminRoleUserAsync("Emails.Manage");
        var client = await LoggedInClientAsync(email);
        var response = await client.GetAsync("/admin/emails/templates");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("OrderShipped", html);
        Assert.Contains("LowStockAlert", html);
        Assert.Contains("ReturnRequested", html);
        Assert.Contains("ConfirmEmail", html);
    }

    // ---- API ----

    [Fact]
    public async Task Api_Anonymous_RedirectsToLogin()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/admin/emails");
        AssertRedirectedTo(response, "/Account/Login");
    }

    [Fact]
    public async Task Api_UserWithoutPermission_RedirectsToAccessDenied()
    {
        var (email, _) = await CreatePermissionUserAsync("Orders.View");
        var client = await LoggedInClientAsync(email);
        var response = await client.GetAsync("/api/admin/emails");
        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    [Fact]
    public async Task Api_UserWithPermissionClaim_CanListEmails()
    {
        var (email, _) = await CreatePermissionUserAsync("Emails.Manage");
        var toEmail = UniqueEmail();
        await SeedEmailAsync(toEmail);

        var client = await LoggedInClientAsync(email);
        var response = await client.GetAsync("/api/admin/emails?pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = ReadJson(response);
        Assert.True(GetCount(root) > 0);
        Assert.Contains(root.GetProperty("items").EnumerateArray(), i =>
            i.TryGetProperty("toEmail", out var t) && t.GetString() == toEmail);
    }

    [Fact]
    public async Task Api_Search_FiltersByRecipient()
    {
        var (email, _) = await CreatePermissionUserAsync("Emails.Manage");
        var target = UniqueEmail();
        await SeedEmailAsync(target, subject: "Order shipped");
        await SeedEmailAsync(UniqueEmail(), subject: "Welcome");

        var client = await LoggedInClientAsync(email);
        var response = await client.GetAsync($"/api/admin/emails?search={Uri.EscapeDataString(target)}");

        var root = ReadJson(response);
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(target, items[0].GetProperty("toEmail").GetString());
    }

    [Fact]
    public async Task Api_StatusFilter_ReturnsOnlyMatchingStatus()
    {
        var (email, _) = await CreatePermissionUserAsync("Emails.Manage");
        await SeedEmailAsync(UniqueEmail(), status: EmailStatus.Sent, subject: "sent one");
        await SeedEmailAsync(UniqueEmail(), status: EmailStatus.Pending, subject: "pending one");

        var client = await LoggedInClientAsync(email);
        var response = await client.GetAsync("/api/admin/emails?status=Sent");

        var root = ReadJson(response);
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.All(items, i => Assert.Equal("Sent", i.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Api_Detail_ReturnsEmail()
    {
        var (email, _) = await CreatePermissionUserAsync("Emails.Manage");
        var id = await SeedEmailAsync(UniqueEmail());

        var client = await LoggedInClientAsync(email);
        var response = await client.GetAsync($"/api/admin/emails/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = ReadJson(response);
        Assert.Equal(id, root.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Api_Detail_UnknownId_ReturnsNotFound()
    {
        var (email, _) = await CreatePermissionUserAsync("Emails.Manage");
        var client = await LoggedInClientAsync(email);
        var response = await client.GetAsync($"/api/admin/emails/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Api_Resend_FailedEmail_RequeuesForDelivery()
    {
        var (email, _) = await CreatePermissionUserAsync("Emails.Manage");
        var id = await SeedEmailAsync(UniqueEmail(), status: EmailStatus.Failed, attempts: 5);

        var client = await LoggedInClientAsync(email);
        var response = await client.PostAsync($"/api/admin/emails/{id}/resend", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.EmailMessages.SingleAsync(e => e.Id == id);
        Assert.Equal(EmailStatus.Pending, row.Status);
        Assert.Equal(0, row.AttemptCount);
        Assert.Null(row.LastError);
        Assert.NotNull(row.NextAttemptAtUtc);
    }

    [Fact]
    public async Task Api_Resend_UnknownId_ReturnsBadRequest()
    {
        var (email, _) = await CreatePermissionUserAsync("Emails.Manage");
        var client = await LoggedInClientAsync(email);
        var response = await client.PostAsync($"/api/admin/emails/{Guid.NewGuid()}/resend", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Hangfire dashboard authorization ----

    private bool AuthorizeDashboard(ClaimsPrincipal? user, string requiredRole = "SuperAdmin")
    {
        var httpContext = new DefaultHttpContext
        {
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity()),
            RequestServices = _factory.Services
        };
        var storage = new SqlServerStorage("Server=localhost;Database=HangfireTest;Trusted_Connection=True;");
        var context = new AspNetCoreDashboardContext(storage, new DashboardOptions(), httpContext);
        return new FashionStore.Web.Middleware.HangfireDashboardAuthorizationFilter(requiredRole).Authorize(context);
    }

    [Fact]
    public void HangfireDashboard_Anonymous_NotAuthorized() =>
        Assert.False(AuthorizeDashboard(null));

    [Fact]
    public void HangfireDashboard_AuthenticatedNonAdmin_NotAuthorized()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "jane") }, "test"));
        Assert.False(AuthorizeDashboard(user));
    }

    [Fact]
    public void HangfireDashboard_AdminRole_NotAuthorizedForDefaultRole()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }, "test"));
        Assert.False(AuthorizeDashboard(user));
    }

    [Fact]
    public void HangfireDashboard_AdminRole_AuthorizedWhenConfigured()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }, "test"));
        Assert.True(AuthorizeDashboard(user, "Admin"));
    }

    [Fact]
    public void HangfireDashboard_SuperAdminRole_Authorized()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "SuperAdmin") }, "test"));
        Assert.True(AuthorizeDashboard(user));
    }
}
