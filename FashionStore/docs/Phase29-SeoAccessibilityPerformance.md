# Phase 29 - SEO, Accessibility and Performance Optimisation

Checklists and verification procedures for the Phase 29 work. Run these against a
local instance (frontend dev server proxying `/api` to the backend) and, where
noted, against the production build.

## Lighthouse checklist

Run Lighthouse against the home page, a product details page, the product
listing and a content page. Target 90+ across all categories on the storefront.

- [ ] Home page: Performance 90+, Accessibility 95+, Best Practices 100, SEO 100
- [ ] Product details: Performance 85+, Accessibility 95+, SEO 100
- [ ] Product listing: Performance 90+, Accessibility 95+, SEO 100
- [ ] Content page: Performance 90+, Accessibility 95+, SEO 100
- [ ] No render-blocking resources beyond the single inlined entry CSS
- [ ] No oversized / scaled-down images reported
- [ ] No layout-shift warnings on category or listing pages
- [ ] LCP on home page under 2.5s on a simulated 4G connection
- [ ] CLS below 0.1 on every storefront page

## Accessibility checklist (WCAG 2.2 AA)

- [ ] Every page starts with a skip-to-content link that moves focus to `main`
- [ ] Page headings form a single, descending hierarchy (one `h1` per page)
- [ ] Focus is visible on all interactive elements (custom `:focus-visible`)
- [ ] Tab order follows the visual order; no focus traps outside modals/drawers
- [ ] Modals trap focus while open, restore focus on close, and close on `Esc`
- [ ] Drawers and off-canvas panels are keyboard- and screen-reader-accessible
- [ ] All form fields have associated `<label>` elements
- [ ] Validation failures are announced and summarised near the form
- [ ] Colour contrast meets 4.5:1 for normal text and 3:1 for large text/UI
- [ ] Every image carries `alt` text or `aria-hidden="true"` when decorative
- [ ] Touch targets are at least 44x44px
- [ ] `prefers-reduced-motion` disables decorative animation
- [ ] Status and error messages are announced (role/live region where needed)
- [ ] Account, admin, cart and checkout pages are excluded from the index
- [ ] Screen-reader labels provided for icon-only buttons (aria-label)

## SEO checklist

- [ ] Every public page has a unique, dynamic title and meta description
- [ ] Canonical URL emitted on product listing, product details, and content pages
- [ ] Open Graph and Twitter card metadata present on public pages
- [ ] Product structured data (`Product` + `BreadcrumbList`) on product pages
- [ ] `Organization` + `WebSite` structured data on the home page
- [ ] XML sitemap at `/sitemap.xml` lists live, indexable URLs only
- [ ] No duplicate URLs in the sitemap
- [ ] `/robots.txt` disallows account, admin, cart, checkout and other private areas
- [ ] `/robots.txt` references the sitemap URL
- [ ] Pagination emits `prev`/`next` and noindexes page 2+
- [ ] Changed slugs 301-redirect to the new slug instead of 404ing
- [ ] Private pages render `<meta name="robots" content="noindex, nofollow">`
- [ ] Slug format is short, hyphenated, and human readable

## Performance checklist

- [ ] Responses compressed with Brotli/Gzip
- [ ] Static assets served with long-lived cache headers
- [ ] CSS and JavaScript minified in production
- [ ] Images lazy-loaded below the fold; critical images eager
- [ ] Images served responsively and with explicit dimensions
- [ ] Critical assets preloaded where they block first paint
- [ ] Fonts subset and self-hosted; `font-display: swap`
- [ ] Database indexes added for the SEO/slug lookups
- [ ] Listing and sitemap queries use projection / no-tracking
- [ ] Output caching applied where safe (robots.txt, sitemap.xml, public pages)
- [ ] Cache invalidated when catalogue, content or redirect data changes
- [ ] No N+1 query patterns in the queries exercised by public pages
- [ ] Minimal initial JavaScript on mobile storefront
- [ ] Stable bottom navigation; no late layout shifts

## Broken-link verification

- [ ] `/sitemap.xml` URLs are unique and each returns 200
- [ ] Every footer link resolves (new arrivals, sale, policies, accessibility)
- [ ] Navigation menu links (home, products, categories, brands, about, contact) return 200
- [ ] Redirect targets resolve to a 200 page, not a redirect loop
- [ ] No internal links to `/pages/{slug}` 404 (only published pages linked)

## Structured-data verification

- [ ] Validate home page schema in Google Rich Results Test (Organization, WebSite)
- [ ] Validate product schema on `/products/{slug}` (Product, BreadcrumbList)
- [ ] Schema uses absolute URLs matching the canonical host
- [ ] `application/ld+json` blocks parse without errors
- [ ] Breadcrumb items match the visible breadcrumb trail

## Automated coverage

- `tests/FashionStore.UnitTests/Services/SlugRedirectServiceTests.cs`
- `tests/FashionStore.UnitTests/Services/SitemapServiceTests.cs`
- `tests/FashionStore.IntegrationTests/SeoAndPerformanceTests.cs`

Run with:

```bash
dotnet test tests/FashionStore.UnitTests
dotnet test tests/FashionStore.IntegrationTests
```
