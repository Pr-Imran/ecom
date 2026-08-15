using System.Net;
using System.Net.Http.Json;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

/// <summary>
/// Phase 31 identity coverage: public registration, login, email confirmation
/// gating and admin authorization on a role-gated endpoint. These exercise the
/// real Identity pipeline through the MVC web host rather than only the service
/// layer, so cookie issuing, claim construction and policy evaluation all run.
/// </summary>
public class AccountFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "ValidPass123!";

    private readonly WebApplicationFactory<Program> _factory;

    public AccountFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail(string prefix = "account") => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private static void AssertRedirectedTo(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(path, response.Headers.Location?.ToString());
    }

    private async Task<HttpClient> RegisterAndConfirmAsync(string email)
    {
        var client = CreateClient();
        var registerPage = await client.GetStringAsync("/Account/Register");
        var token = CartTestsHelper.ExtractAntiforgeryToken(registerPage);

        var register = new HttpRequestMessage(HttpMethod.Post, "/Account/Register")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["FirstName"] = "Jane",
                ["LastName"] = "Doe",
                ["Email"] = email,
                ["PhoneNumber"] = "+15550100",
                ["Password"] = Password,
                ["AcceptTerms"] = "true"
            })
        };
        var registerResponse = await client.SendAsync(register);
        AssertRedirectedTo(registerResponse, "/Account/Login");

        var user = await FindUserAsync(email);
        Assert.NotNull(user);

        await ConfirmEmailAsync(user!.Id);

        return client;
    }

    private async Task<ApplicationUser?> FindUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.SingleOrDefaultAsync(u => u.Email == email);
    }

    private async Task ConfirmEmailAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user!);
        await userManager.ConfirmEmailAsync(user!, token);
    }

    private async Task<HttpClient> LoginAsync(string email, string password = Password)
    {
        var client = CreateClient();
        var loginPage = await client.GetStringAsync("/Account/Login");
        var token = CartTestsHelper.ExtractAntiforgeryToken(loginPage);

        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["EmailOrUserName"] = email,
                ["Password"] = password,
                ["RememberMe"] = "false"
            })
        };
        var response = await client.SendAsync(request);
        AssertRedirectedTo(response, "/");
        return client;
    }

    [Fact]
    public async Task Register_PublicPage_ReturnsOk()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/Account/Register");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_ValidSubmission_RedirectsToLoginAndCreatesUser()
    {
        var email = UniqueEmail();
        var client = CreateClient();
        var registerPage = await client.GetStringAsync("/Account/Register");
        var token = CartTestsHelper.ExtractAntiforgeryToken(registerPage);

        var register = new HttpRequestMessage(HttpMethod.Post, "/Account/Register")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["FirstName"] = "Jane",
                ["LastName"] = "Doe",
                ["Email"] = email,
                ["Password"] = Password,
                ["AcceptTerms"] = "true"
            })
        };

        var response = await client.SendAsync(register);

        AssertRedirectedTo(response, "/Account/Login");
        var user = await FindUserAsync(email);
        Assert.NotNull(user);
        Assert.Equal("Jane Doe", user!.DisplayName);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsGenericErrorPage()
    {
        var email = UniqueEmail();
        await RegisterAndConfirmAsync(email);
        var client = CreateClient();

        var registerPage = await client.GetStringAsync("/Account/Register");
        var token = CartTestsHelper.ExtractAntiforgeryToken(registerPage);

        var register = new HttpRequestMessage(HttpMethod.Post, "/Account/Register")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Email"] = email,
                ["Password"] = Password,
                ["AcceptTerms"] = "true"
            })
        };

        var response = await client.SendAsync(register);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        // Email enumeration must not leak; the generic message is shown.
        Assert.Contains("could not create your account", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("is already", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_WeakPassword_ShowsValidationErrors()
    {
        var client = CreateClient();
        var registerPage = await client.GetStringAsync("/Account/Register");
        var token = CartTestsHelper.ExtractAntiforgeryToken(registerPage);

        var register = new HttpRequestMessage(HttpMethod.Post, "/Account/Register")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Email"] = UniqueEmail(),
                ["Password"] = "short",
                ["AcceptTerms"] = "true"
            })
        };

        var response = await client.SendAsync(register);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Create Account", html);
        Assert.Contains("alert-danger", html);
    }

    [Fact]
    public async Task Login_UnconfirmedEmail_IsRefusedWithGenericMessage()
    {
        var email = UniqueEmail();
        var client = CreateClient();
        var registerPage = await client.GetStringAsync("/Account/Register");
        var token = CartTestsHelper.ExtractAntiforgeryToken(registerPage);

        var register = new HttpRequestMessage(HttpMethod.Post, "/Account/Register")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Email"] = email,
                ["Password"] = Password,
                ["AcceptTerms"] = "true"
            })
        };
        await client.SendAsync(register);

        var loginPage = await client.GetStringAsync("/Account/Login");
        token = CartTestsHelper.ExtractAntiforgeryToken(loginPage);
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid email or password", html);
    }

    [Fact]
    public async Task Login_ConfirmedUser_SignsInAndReachesAccountPanel()
    {
        var email = UniqueEmail();
        await RegisterAndConfirmAsync(email);
        var client = await LoginAsync(email);

        var response = await client.GetAsync("/Account");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(email, html);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsLoginPageWithError()
    {
        var email = UniqueEmail();
        await RegisterAndConfirmAsync(email);
        var client = CreateClient();

        var loginPage = await client.GetStringAsync("/Account/Login");
        var token = CartTestsHelper.ExtractAntiforgeryToken(loginPage);
        var login = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["EmailOrUserName"] = email,
                ["Password"] = "WrongPass123!"
            })
        };

        var response = await client.SendAsync(login);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid email or password", html);
    }

    [Fact]
    public async Task Admin_Anonymous_IsRedirectedToLogin()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/Account");

        AssertRedirectedTo(response, "/Account/Login");
    }

    [Fact]
    public async Task Admin_CustomerRole_CannotAccessAdminOrderPanel()
    {
        var email = UniqueEmail("customer");
        await RegisterAndConfirmAsync(email);
        var client = await LoginAsync(email);

        var response = await client.GetAsync("/admin/orders");

        // A customer is not an admin; expect a redirect (403/redirect to login).
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
