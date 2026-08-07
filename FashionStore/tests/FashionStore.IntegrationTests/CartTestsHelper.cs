using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.IntegrationTests;

public static class CartTestsHelper
{
    public static Guid GetProductId(WebApplicationFactory<Program> factory, string productSlug)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Products.Single(p => p.Slug == productSlug).Id;
    }

    public static Guid GetVariantId(WebApplicationFactory<Program> factory, string variantSku)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.ProductVariants.Single(v => v.Sku == variantSku).Id;
    }

    public static string ExtractAntiforgeryToken(string html)
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
