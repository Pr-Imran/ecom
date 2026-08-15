using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FashionStore.IntegrationTests;

public class SecurityHeadersTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SecurityHeadersTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HomePage_EmitsSecurityHeaders()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.NotNull(response.Headers.GetValues("Content-Security-Policy").SingleOrDefault());
        Assert.NotNull(response.Headers.GetValues("Permissions-Policy").SingleOrDefault());
    }

    [Fact]
    public async Task ApiEndpoint_EmitsSecurityHeaders()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.NotNull(response.Headers.GetValues("Content-Security-Policy").SingleOrDefault());
    }
}
