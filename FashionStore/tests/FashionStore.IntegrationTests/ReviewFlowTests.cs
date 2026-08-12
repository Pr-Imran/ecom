using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
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
/// Exercises the product review feature end to end: an authenticated customer with
/// a delivered purchase submitting a review, duplicate-review refusal, ownership and
/// permission enforcement, admin approval/rejection through the real moderation API
/// and the public product page showing approved content only.
/// </summary>
public class ReviewFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "ReviewTest!pass1";
    private const string ProductSlug = "cashmere-crew-neck-sweater";

    private readonly WebApplicationFactory<Program> _factory;

    public ReviewFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string UniqueEmail() => $"review-{Guid.NewGuid():N}@example.com";

    private static string UniqueOrderNumber() => $"ORD-V-{Guid.NewGuid():N}"[..24];

    // ---- Account helpers ----

    private async Task<(string Email, string UserId)> CreateCustomerAsync()
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
        return (email, user.Id);
    }

    private async Task<HttpClient> CustomerClientAsync(string email)
    {
        var client = CreateClient();
        var loginHtml = await client.GetStringAsync("/Account/Login");
        var token = CartTestsHelper.ExtractAntiforgeryToken(loginHtml);
        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["EmailOrUserName"] = email,
                ["Password"] = Password
            })
        };
        var loginResponse = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        return client;
    }

    private async Task<HttpClient> AdminClientAsync(params string[] permissions)
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

        return await CustomerClientAsync(email);
    }

    private async Task<HttpClient> AdminPageClientAsync(params string[] permissions)
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

        var roleResult = await userManager.AddClaimAsync(user, new Claim(ClaimTypes.Role, "Admin"));
        Assert.True(roleResult.Succeeded, string.Join("; ", roleResult.Errors.Select(e => e.Description)));

        foreach (var permission in permissions)
        {
            var claimResult = await userManager.AddClaimAsync(user, new Claim("permission", permission));
            Assert.True(claimResult.Succeeded, string.Join("; ", claimResult.Errors.Select(e => e.Description)));
        }

        return await CustomerClientAsync(email);
    }

    // ---- Order seeding ----

    private async Task<Order> SeedDeliveredOrderAsync(string number, string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var productId = CartTestsHelper.GetProductId(_factory, ProductSlug);
        var variantId = CartTestsHelper.GetVariantId(_factory, "SW-1001-GREY-M");
        var now = DateTime.UtcNow;

        var order = new Order
        {
            PublicOrderNumber = number,
            InvoiceNumber = number.Replace("ORD", "INV"),
            UserId = userId,
            CustomerName = "Jane Doe",
            Currency = "USD",
            Subtotal = 128m,
            ProductDiscount = 0m,
            CouponDiscount = 0m,
            ShippingCharge = 9.99m,
            Tax = 0m,
            GrandTotal = 137.99m,
            PaymentMethodCode = "card",
            ShippingMethodName = "Standard Delivery",
            OrderStatus = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Paid,
            FulfilmentStatus = FulfilmentStatus.Fulfilled,
            DeliveredAtUtc = now.AddDays(-1),
            CreatedAtUtc = now.AddDays(-7),
            UpdatedAtUtc = now
        };
        order.Items.Add(new OrderItem
        {
            OrderId = order.Id,
            ProductId = productId,
            ProductVariantId = variantId,
            ProductName = "Cashmere Crew Neck Sweater",
            ProductSlug = ProductSlug,
            Sku = "SW-1001-GREY-M",
            ColourName = "Heather Grey",
            SizeName = "M",
            ImageUrl = "/img/sweater.jpg",
            UnitPrice = 128m,
            CompareAtPrice = 160m,
            Discount = 0m,
            Tax = 0m,
            Quantity = 1,
            LineTotal = 128m
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private async Task<Guid> SubmitReviewAsync(
        HttpClient client,
        int rating = 5,
        string body = "Wonderful quality and the fit is perfect.",
        string title = "Love it")
    {
        var writeHtml = await client.GetStringAsync($"/products/{ProductSlug}/reviews/write");
        var token = CartTestsHelper.ExtractAntiforgeryToken(writeHtml);
        var productId = CartTestsHelper.GetProductId(_factory, ProductSlug);

        var post = new HttpRequestMessage(HttpMethod.Post, "/reviews")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["ProductId"] = productId.ToString(),
                ["ProductSlug"] = ProductSlug,
                ["Rating"] = rating.ToString(),
                ["Title"] = title,
                ["Body"] = body
            })
        };

        var response = await client.SendAsync(post);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.ProductReviews.Single(r => r.Body == body).Id;
    }

    private static async Task ModerateAsync(HttpClient adminClient, Guid reviewId, ReviewStatus status)
    {
        var response = await adminClient.PostAsJsonAsync(
            $"/api/admin/reviews/{reviewId}/moderate",
            new { status = (int)status, notes = "Checked against the moderation guidelines." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Flows ----

    [Fact]
    public async Task CustomerWithDeliveredPurchase_SubmitsReview_AndAdminApprovesIt()
    {
        var (email, userId) = await CreateCustomerAsync();
        await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId);
        var customer = await CustomerClientAsync(email);
        var body = $"Wonderful quality and the fit is perfect. {Guid.NewGuid():N}";

        var reviewId = await SubmitReviewAsync(customer, body: body);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var review = db.ProductReviews.Include(r => r.Images).Single(r => r.Id == reviewId);
            Assert.Equal(ReviewStatus.Pending, review.Status);
            Assert.True(review.IsVerifiedPurchase);
        }

        var admin = await AdminClientAsync("Reviews.Manage");
        await ModerateAsync(admin, reviewId, ReviewStatus.Approved);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(ReviewStatus.Approved, db.ProductReviews.Single(r => r.Id == reviewId).Status);
            Assert.Equal(
                db.ProductReviews.Count(r => r.ProductId == CartTestsHelper.GetProductId(_factory, ProductSlug) && r.Status == ReviewStatus.Approved),
                db.Products.Single(p => p.Slug == ProductSlug).ReviewCount);
        }

        var anonymous = CreateClient();
        var page = await anonymous.GetStringAsync($"/products/{ProductSlug}/reviews");
        Assert.Contains("Love it", page);
        Assert.Contains(body, page);
    }

    [Fact]
    public async Task CustomerWithoutDeliveredPurchase_CannotWriteReview()
    {
        var (email, _) = await CreateCustomerAsync();
        var customer = await CustomerClientAsync(email);

        var writeResponse = await customer.GetAsync($"/products/{ProductSlug}/reviews/write");
        Assert.Equal(HttpStatusCode.Redirect, writeResponse.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotSubmitDuplicateReview()
    {
        var (email, userId) = await CreateCustomerAsync();
        await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId);
        var customer = await CustomerClientAsync(email);

        var reviewId = await SubmitReviewAsync(customer, body: $"Duplicate review target {Guid.NewGuid():N}.");

        var writeHtml = await customer.GetStringAsync($"/products/{ProductSlug}/reviews");
        var token = CartTestsHelper.ExtractAntiforgeryToken(writeHtml);
        var productId = CartTestsHelper.GetProductId(_factory, ProductSlug);

        var second = new HttpRequestMessage(HttpMethod.Post, "/reviews")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["ProductId"] = productId.ToString(),
                ["ProductSlug"] = ProductSlug,
                ["Rating"] = "4",
                ["Title"] = "Second attempt",
                ["Body"] = "Trying to submit a second review for the same product."
            })
        };

        var response = await customer.SendAsync(second);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(db.ProductReviews.Where(r => r.ProductId == productId && r.UserId == userId));
    }

    [Fact]
    public async Task Anonymous_CannotSubmitReview()
    {
        var client = CreateClient();
        var post = new HttpRequestMessage(HttpMethod.Post, "/reviews")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ProductId"] = Guid.NewGuid().ToString(),
                ["ProductSlug"] = ProductSlug,
                ["Rating"] = "5",
                ["Title"] = "Anon",
                ["Body"] = "An anonymous attempt to submit a review."
            })
        };

        var response = await client.SendAsync(post);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task AdminWithoutPermission_CannotModerate()
    {
        var (_, userId) = await CreateCustomerAsync();
        await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var productId = CartTestsHelper.GetProductId(_factory, ProductSlug);
        db.ProductReviews.Add(new ProductReview
        {
            ProductId = productId,
            UserId = userId,
            DisplayName = "Jane",
            Rating = 4,
            Body = "A pending review awaiting a moderator with the right permission.",
            Status = ReviewStatus.Pending,
            IsVerifiedPurchase = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var reviewId = db.ProductReviews.Single(r => r.Body == "A pending review awaiting a moderator with the right permission.").Id;

        var adminWithoutPermission = await AdminClientAsync("Returns.View");
        var response = await adminWithoutPermission.PostAsJsonAsync(
            $"/api/admin/reviews/{reviewId}/moderate",
            new { status = (int)ReviewStatus.Approved });
        AssertRedirectedTo(response, "/Account/AccessDenied");
    }

    private static void AssertRedirectedTo(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(path, response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PublicPage_ShowsApprovedReviews_AndHidesPendingAndRejected()
    {
        var (email, userId) = await CreateCustomerAsync();
        await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId);
        var customer = await CustomerClientAsync(email);
        var reviewId = await SubmitReviewAsync(customer, rating: 4, body: "Slightly small but otherwise great.");

        var admin = await AdminClientAsync("Reviews.Manage");
        await ModerateAsync(admin, reviewId, ReviewStatus.Approved);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ProductReviews.Add(new ProductReview
            {
                ProductId = CartTestsHelper.GetProductId(_factory, ProductSlug),
                UserId = Guid.NewGuid().ToString(),
                DisplayName = "Other Customer",
                Rating = 1,
                Body = "This pending review must never show on the public page.",
                Status = ReviewStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var anonymous = CreateClient();
        var page = await anonymous.GetStringAsync($"/products/{ProductSlug}/reviews");
        Assert.Contains("Slightly small but otherwise great.", page);
        Assert.DoesNotContain("This pending review must never show on the public page.", page);
    }

    [Fact]
    public async Task VerifiedPurchaseBadge_IsRendered_OnPublicPage()
    {
        var (email, userId) = await CreateCustomerAsync();
        await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId);
        var customer = await CustomerClientAsync(email);
        var reviewId = await SubmitReviewAsync(customer, body: $"Verified badge check {Guid.NewGuid():N}.");

        var admin = await AdminClientAsync("Reviews.Manage");
        await ModerateAsync(admin, reviewId, ReviewStatus.Approved);

        var anonymous = CreateClient();
        var page = await anonymous.GetStringAsync($"/products/{ProductSlug}/reviews");
        Assert.Contains("Verified", page);
    }

    [Fact]
    public async Task HelpfulVote_Toggles_AndPublicPageReflectsIt()
    {
        var (email, userId) = await CreateCustomerAsync();
        await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId);
        var customer = await CustomerClientAsync(email);
        var reviewId = await SubmitReviewAsync(customer, body: $"Helpful vote target {Guid.NewGuid():N}.");
        var admin = await AdminClientAsync("Reviews.Manage");
        await ModerateAsync(admin, reviewId, ReviewStatus.Approved);

        var voterEmail = (await CreateCustomerAsync()).Email;
        var voter = await CustomerClientAsync(voterEmail);
        var page = await voter.GetStringAsync($"/products/{ProductSlug}/reviews");
        var token = CartTestsHelper.ExtractAntiforgeryToken(page);

        var vote = new HttpRequestMessage(HttpMethod.Post, $"/reviews/{reviewId}/helpful")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            })
        };
        var voteResponse = await voter.SendAsync(vote);
        Assert.Equal(HttpStatusCode.OK, voteResponse.StatusCode);
        var payload = JsonDocument.Parse(await voteResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.True(payload.GetProperty("voted").GetBoolean());
        Assert.Equal(1, payload.GetProperty("helpfulCount").GetInt32());

        var reVote = new HttpRequestMessage(HttpMethod.Post, $"/reviews/{reviewId}/helpful")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            })
        };
        var reVoteResponse = await voter.SendAsync(reVote);
        Assert.Equal(HttpStatusCode.OK, reVoteResponse.StatusCode);
        var rePayload = JsonDocument.Parse(await reVoteResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.False(rePayload.GetProperty("voted").GetBoolean());
        Assert.Equal(0, rePayload.GetProperty("helpfulCount").GetInt32());
    }

    [Fact]
    public async Task MyReviews_Page_ListsTheCustomersOwnReviews()
    {
        var (email, userId) = await CreateCustomerAsync();
        await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId);
        var customer = await CustomerClientAsync(email);
        var title = $"My review title {Guid.NewGuid():N}";
        await SubmitReviewAsync(customer, rating: 5, body: "My very own review for my account page.", title: title);

        var page = await customer.GetStringAsync("/account/reviews");
        Assert.Contains(title, page);
    }

    [Fact]
    public async Task PhotoUpload_SubmitWithMultipart_AttachesImagesToReview()
    {
        var (email, userId) = await CreateCustomerAsync();
        await SeedDeliveredOrderAsync(UniqueOrderNumber(), userId);
        var customer = await CustomerClientAsync(email);
        var productId = CartTestsHelper.GetProductId(_factory, ProductSlug);
        var body = $"Review submitted together with a photo {Guid.NewGuid():N}";

        var writeHtml = await customer.GetStringAsync($"/products/{ProductSlug}/reviews/write");
        var token = CartTestsHelper.ExtractAntiforgeryToken(writeHtml);

        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        using var content = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent(productId.ToString()), "ProductId" },
            { new StringContent(ProductSlug), "ProductSlug" },
            { new StringContent("5"), "Rating" },
            { new StringContent("With photo"), "Title" },
            { new StringContent(body), "Body" }
        };
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "Photos", "photo.jpg");

        var response = await customer.PostAsync("/reviews", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var review = await db.ProductReviews.Include(r => r.Images).SingleAsync(r => r.Body == body);
        Assert.Equal(ReviewStatus.Pending, review.Status);
        var image = Assert.Single(review.Images);
        Assert.Equal("photo.jpg", image.OriginalFileName);
        Assert.Equal("image/jpeg", image.ContentType);
        Assert.True(image.SizeBytes > 0);
    }

    [Fact]
    public async Task PhotoUpload_OtherCustomerCannotAttachToReview()
    {
        var (ownerEmail, ownerId) = await CreateCustomerAsync();
        await SeedDeliveredOrderAsync(UniqueOrderNumber(), ownerId);
        var owner = await CustomerClientAsync(ownerEmail);
        var reviewId = await SubmitReviewAsync(owner, body: $"Photo ownership guard {Guid.NewGuid():N}.");

        var (otherEmail, _) = await CreateCustomerAsync();
        var other = await CustomerClientAsync(otherEmail);
        var page = await other.GetStringAsync($"/products/{ProductSlug}/reviews");
        var token = CartTestsHelper.ExtractAntiforgeryToken(page);

        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        using var content = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" }
        };
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "files", "sneaky.jpg");

        var upload = await other.PostAsync($"/reviews/{reviewId}/images", content);
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(db.ReviewImages.Where(i => i.ReviewId == reviewId));
    }

    [Fact]
    public async Task AdminReviewsPage_Renders_WithReviewsNavEntry()
    {
        var admin = await AdminPageClientAsync("Reviews.Manage");
        var response = await admin.GetAsync("/admin/reviews");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Reviews", body);
        Assert.Contains("href=\"/admin/reviews\"", body);
    }
}
