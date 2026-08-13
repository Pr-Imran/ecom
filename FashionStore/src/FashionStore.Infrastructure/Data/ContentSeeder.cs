using FashionStore.Domain.Entities;
using FashionStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Data;

public interface IContentSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Seeds the default content a fresh storefront needs: system pages (About,
/// Contact, Size Guide), legal/informational policy documents with stable codes,
/// and the main storefront navigation menu. Existing records are matched by
/// stable keys and left untouched, so edits made by administrators survive
/// subsequent seeding runs.
/// </summary>
public class ContentSeeder : IContentSeeder
{
    private readonly AppDbContext _context;
    private readonly ILogger<ContentSeeder> _logger;

    public ContentSeeder(AppDbContext context, ILogger<ContentSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedSystemPagesAsync(cancellationToken);
        await SeedPolicyDocumentsAsync(cancellationToken);
        await SeedMainNavigationAsync(cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Content seeding completed");
    }

    private async Task SeedSystemPagesAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var pages = new[]
        {
            new ContentPage
            {
                Title = "About Us",
                Slug = "about",
                Summary = "Learn more about our story and what we stand for.",
                BodyHtml = "<p>Tell your customers who you are and what makes your store special.</p>",
                Template = ContentPageTemplate.Default,
                Status = ContentStatus.Published,
                IsSystem = true,
                PublishedAtUtc = now,
                MetaTitle = "About Us",
                MetaDescription = "Learn more about our store and what we stand for.",
                CreatedAtUtc = now,
                CreatedBy = "system"
            },
            new ContentPage
            {
                Title = "Contact",
                Slug = "contact",
                Summary = "Get in touch with our team.",
                BodyHtml = "<p>Contact page placeholder.</p>",
                Template = ContentPageTemplate.Default,
                Status = ContentStatus.Published,
                IsSystem = true,
                PublishedAtUtc = now,
                MetaTitle = "Contact Us",
                MetaDescription = "Get in touch with our team.",
                CreatedAtUtc = now,
                CreatedBy = "system"
            },
            new ContentPage
            {
                Title = "Size Guide",
                Slug = "size-guide",
                Summary = "Find the right fit with our size guide.",
                BodyHtml = "<p>Add your size guide tables here.</p>",
                Template = ContentPageTemplate.Default,
                Status = ContentStatus.Published,
                IsSystem = true,
                PublishedAtUtc = now,
                MetaTitle = "Size Guide",
                MetaDescription = "Find the right fit with our size guide.",
                CreatedAtUtc = now,
                CreatedBy = "system"
            }
        };

        foreach (var page in pages)
        {
            var existing = await _context.ContentPages
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Slug == page.Slug, cancellationToken);
            if (existing is null)
            {
                _context.ContentPages.Add(page);
            }
        }
    }

    private async Task SeedPolicyDocumentsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var documents = new[]
        {
            new PolicyDocument
            {
                Code = "delivery-policy",
                Title = "Delivery Policy",
                Summary = "Shipping times, rates and international delivery information.",
                BodyHtml = "<p>Describe your shipping methods, delivery times and any carrier partners here.</p>",
                Status = ContentStatus.Published,
                PublishedAtUtc = now,
                CreatedAtUtc = now,
                CreatedBy = "system"
            },
            new PolicyDocument
            {
                Code = "return-policy",
                Title = "Return Policy",
                Summary = "How returns and exchanges work within the return window.",
                BodyHtml = "<p>Explain your return window, condition requirements and the return process here.</p>",
                Status = ContentStatus.Published,
                PublishedAtUtc = now,
                CreatedAtUtc = now,
                CreatedBy = "system"
            },
            new PolicyDocument
            {
                Code = "privacy-policy",
                Title = "Privacy Policy",
                Summary = "How we collect, use and protect your personal information.",
                BodyHtml = "<p>Describe how customer data is collected, stored and shared here.</p>",
                Status = ContentStatus.Published,
                PublishedAtUtc = now,
                CreatedAtUtc = now,
                CreatedBy = "system"
            },
            new PolicyDocument
            {
                Code = "terms",
                Title = "Terms and Conditions",
                Summary = "The terms that govern use of this website and purchases.",
                BodyHtml = "<p>Set out the legal terms for using the site and placing orders here.</p>",
                Status = ContentStatus.Published,
                PublishedAtUtc = now,
                CreatedAtUtc = now,
                CreatedBy = "system"
            }
        };

        foreach (var document in documents)
        {
            var existing = await _context.PolicyDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Code == document.Code, cancellationToken);
            if (existing is null)
            {
                _context.PolicyDocuments.Add(document);
            }
        }
    }

    private async Task SeedMainNavigationAsync(CancellationToken cancellationToken)
    {
        var menu = await _context.NavigationMenus
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.Code == "main", cancellationToken);

        if (menu is null)
        {
            var now = DateTime.UtcNow;
            var items = new[]
            {
                new NavigationItem { Label = "Home", Url = "/", DisplayOrder = 0, IsActive = true },
                new NavigationItem { Label = "Products", Url = "/products", DisplayOrder = 1, IsActive = true },
                new NavigationItem { Label = "Categories", Url = "/categories", DisplayOrder = 2, IsActive = true },
                new NavigationItem { Label = "Brands", Url = "/brands", DisplayOrder = 3, IsActive = true },
                new NavigationItem { Label = "About Us", Url = "/about", DisplayOrder = 4, IsActive = true },
                new NavigationItem { Label = "Contact", Url = "/contact", DisplayOrder = 5, IsActive = true }
            };

            foreach (var item in items)
            {
                item.CreatedAtUtc = now;
                item.CreatedBy = "system";
            }

            _context.NavigationMenus.Add(new NavigationMenu
            {
                Name = "Main Menu",
                Code = "main",
                Description = "Primary storefront navigation",
                IsActive = true,
                Items = items,
                CreatedAtUtc = now,
                CreatedBy = "system"
            });
        }
    }
}
