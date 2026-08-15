# Phase 31 - Automated Tests and Quality Verification

Phase 31 builds the automated test suite for FashionStore and runs the full set
of quality gates required by the phase prompt:

* Unit tests
* Integration tests
* UI-level (Playwright) preparation
* Build
* Static analysis
* Formatting
* Migration verification
* Dependency vulnerability review
* Nullability review
* Logging review

This document records the test project structure, the commands used, the test
coverage summary, the production defects the tests surfaced (and the fixes
applied), the known gaps, and the completion status.

## Test project structure

```
FashionStore/
  src/
    FashionStore.Domain        (entities, enums, domain rules)
    FashionStore.Application   (DTOs, service interfaces, permissions)
    FashionStore.Infrastructure(EF Core, Identity, payments, invoices, jobs)
    FashionStore.Web           (MVC controllers, views, middleware)
  tests/
    FashionStore.UnitTests/           610 tests
      DomainEntityTests.cs
      Emails/
        EmailOutboxTests.cs
        EmailSenderTests.cs
        SendQueuedEmailsJobTests.cs
        TransactionTimingTests.cs
      Reports/
        AdminDashboardServiceTests.cs
        AdminReportServiceTests.cs
        ReportDateRangeHelperTests.cs
        SqliteTestContext.cs
      Services/
        AddToCartServiceTests.cs
        AddressServiceTests.cs
        AdminReturnServiceTests.cs
        AdminReviewServiceTests.cs
        AuthServiceTests.cs
        CartServiceTests.cs
        CatalogServiceTests.cs
        CheckoutCalculationServiceTests.cs
        ContentManagementServiceTests.cs
        CouponServiceTests.cs
        CustomerOrderServiceTests.cs
        CustomerReturnServiceTests.cs
        DiscountServiceTests.cs
        Images/ (image processing, storage, validation)
        InventoryServiceTests.cs
        InvoiceServiceTests.cs
        OrderAdministrationServiceTests.cs
        OrderPlacementServiceTests.cs
        PaymentServiceTests.cs
        PermissionRulesTests.cs
        ProductServiceTests.cs
        ProductVariationServiceTests.cs
        ProfileServiceTests.cs
        ReviewServiceTests.cs
        RichContentSanitizerTests.cs
        ShippingCalculationServiceTests.cs
        ShippingServiceTests.cs
        SitemapServiceTests.cs
        SlugRedirectServiceTests.cs
        WebsiteSettingsServiceTests.cs
        WishlistServiceTests.cs
    FashionStore.IntegrationTests/     313 tests
      TestWebApplicationFactory.cs     (per-class InMemory DB host)
      SqliteReportTestFactory.cs       (SQLite report test host)
      AccountFlowTests.cs              Identity, registration, login
      AdminDashboardAndReportsTests.cs Admin authorization, reports
      AdminInventoryTests.cs           Inventory adjustment, thresholds,
                                       reservations, CSV export, warehouses
      AdminOrderPanelTests.cs          Admin order management
      AdminProductCrudTests.cs         Product CRUD + audit
      AdminReturnsPanelTests.cs        Admin returns/refunds
      AdminVariationCrudTests.cs       Attribute/variant CRUD
      CartTests.cs
      CheckoutTests.cs
      ConcurrentStockPurchaseTests.cs  Stock race
      ContentManagementPageTests.cs
      CustomerReturnsFlowTests.cs
      EmailAdminPageTests.cs
      EmailFlowTests.cs
      EmailTemplateRenderingTests.cs
      HealthCheckTests.cs
      InvoiceTests.cs                  Invoice access + ownership
      OrderPanelTests.cs               Customer order panel
      OrderTests.cs                    Order creation, cancellation
      PaymentWebhookTests.cs           HMAC webhook, idempotency
      ProductDetailsTests.cs
      ProductListingTests.cs
      ReviewFlowTests.cs
      SecurityHeadersTests.cs
      SeoAndPerformanceTests.cs
      ShippingQuoteTests.cs
      WishlistTests.cs
    Playwright/                       UI-level preparation (not executed here)
      package.json
      playwright.config.ts            desktop 1280x800 + mobile 390x844
      e2e/customer.spec.ts            storefront journeys
      e2e/admin.spec.ts               admin journeys
      README.md                       run instructions
```

## Commands

```bash
# Build (warnings-as-errors, recommended analysers)
dotnet build FashionStore.sln -c Release

# Unit tests
dotnet test tests/FashionStore.UnitTests/FashionStore.UnitTests.csproj -c Release

# Integration tests (uses an in-memory EF Core database, no SQL Server needed)
dotnet test tests/FashionStore.IntegrationTests/FashionStore.IntegrationTests.csproj -c Release

# Formatting (whitespace) - now clean repo-wide
dotnet format whitespace --verify-no-changes --no-restore

# EF model / migration verification
dotnet ef migrations has-pending-model-changes \
  --project src/FashionStore.Infrastructure/FashionStore.Infrastructure.csproj \
  --startup-project src/FashionStore.Web/FashionStore.Web.csproj

# Dependency vulnerability audit (transitive included)
dotnet list package --vulnerable --include-transitive

# Playwright (requires a running, seeded application instance)
cd tests/Playwright
npm install
npx playwright install chromium
npm test                      # desktop + mobile projects
```

## Test coverage summary

| Suite | Count | Result |
|-------|-------|--------|
| Unit tests | 610 | Passed |
| Integration tests | 313 | Passed |
| Playwright | prepared | Requires a database-backed app instance |

Unit tests cover product validation, variation combinations, price
calculation, discounts, coupons, shipping, cart, checkout, inventory, stock
reservations, order creation, order status transitions, payment processing,
payment webhook idempotency, cancellation, returns, refunds, invoice totals,
invoice snapshot consistency, permission rules, review moderation, rich-content
sanitisation, image processing, slug redirects, sitemaps and email delivery.

Integration tests cover identity, registration, login, admin authorization,
product CRUD, variation CRUD, inventory adjustment, cart, checkout, order
creation, concurrent stock purchase, payment webhook, invoice access, customer
order ownership and return requests.

## Production defects found by the new tests and fixed

* **`ProductService.InvalidateCacheAsync` never cleared the per-product cache
  key.** It only removed the `products:featured` / `products:Home` /
  `products:Sitemap` collection keys, so a published/archived/edited product
  kept a stale `product:{id}` entry. The method now accepts an optional
  `productId` and removes that key from all five call sites (create, update,
  delete, publish, archive).
* **`InventoryService.SetStockThresholdsAsync` auto-created `WarehouseStock`
  rows for non-existent variants**, leaving orphan stock rows. It now rejects
  unknown variants.
* **`InventoryService.ApplyStockChangeAsync` had a read-modify-write race on
  stock.** Two concurrent reservations of the last unit both succeeded (both
  orders were assigned `ORD-2026-000001`). A process-wide static
  `SemaphoreSlim` now serializes the mutation plus `SaveChanges`, so the
  availability guard always sees committed state. `OrderPlacementService`
  already maps the resulting "Insufficient available stock" to a friendly
  stock error.
* **Test host database isolation.** `TestWebApplicationFactory` previously
  shared one InMemory database across all parallel test classes via
  `InMemoryDatabaseRoot` (`SharedRoot`), producing order/race-dependent flakes
  in baseline tests. It now uses a per-factory (per-class) InMemory database.
* **Role seeding granularity.** Full `IRoleSeeder` seeding had granted the
  bare `Admin` role in tests every permission claim, breaking 9
  permission-denial tests. Only the `Customer` role is now seeded (through
  `EnsureCustomerRole`).

## Quality gates

### Build and static analysis

`dotnet build -c Release` succeeds with **0 warnings, 0 errors**.
`TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended` and
`EnforceCodeStyleInBuild` are enabled solution-wide, so the recommended
analysers (CA/IDE rules) run as part of every build.

### Formatting

`dotnet format whitespace --verify-no-changes` now passes **repo-wide**. The
previous checkpoint recorded 62 pre-existing WHITESPACE findings across 10
baseline files (`AdminReturnService`, `CategoryService`, `ProductService`,
`ProductVariationService`, `IProductVariationService`, `AccountController`,
`AdminController`, `AdminPagesController`, `AppDbContext`,
`AuthServiceTests`). These were auto-fixed with `dotnet format whitespace`;
the diff is whitespace-only and the full suite still passes.

### Migration verification

`dotnet ef migrations has-pending-model-changes` reports **"No changes have
been made to the model since the last migration."** No new migration is
required.

### Dependency vulnerability review

`dotnet list package --vulnerable --include-transitive` is clean across all
six projects. During this phase three transitive advisories were remediated:

| Package | Advisory | Resolved |
|---------|----------|----------|
| Newtonsoft.Json (via Hangfire.Core) | GHSA-5crp-9r3c-p9vr (High) | pinned 13.0.3 |
| System.Security.Cryptography.Xml (Data Protection) | CVE-2026-33116 / CVE-2026-50648 | pinned 8.0.4 |
| SQLitePCLRaw.lib.e_sqlite3 (test projects) | GHSA-2m69-gcr7-jv3q (High, CVE-2025-6965) | bundle pinned 3.0.5 |

The SQLitePCLRaw advisory affects every 2.1.x bundle (no patched 2.1 release),
so the transitive `SQLitePCLRaw.bundle_e_sqlite3` is overridden to 3.0.5 in the
two test projects; `Microsoft.EntityFrameworkCore.Sqlite` remains on 8.0.11
and the full test suite passes with the newer native SQLite.

### Nullability review

The codebase builds with `Nullable` enabled and warnings-as-errors, so any
nullable-reference-type violation fails the build; the review therefore
concentrated on runtime-only risks not caught by the compiler:

* `.First()` / `.Single()` without a fallback: only found in `SitemapService`
  where it is applied inside `GroupBy(...).Select(g => g.First())` - group keys
  always have at least one member, so this is safe.
* Unboxed `[0]` indexing in `QuestPdfInvoiceGenerator.Humanize` operates on
  `Split(..., RemoveEmptyEntries)` output - never an empty string.
* Nullable `.Value` accesses in `PaymentService` are all guarded by a preceding
  `is null` / `HasValue` / state check.

No unresolved nullability findings.

### Logging review

Searched every `ILogger` call site in `src/` for sensitive data:

* No passwords, reset tokens, confirmation tokens, card numbers, CVV/PAN,
  security codes, OTPs or full payment payloads are logged.
* No whole-object structured logging (`{@...}` template tokens) is used, so a
  request or entity can never be serialized verbatim into a log line.
* Webhook payloads are persisted only after `PaymentService.MaskPayload`
  truncates them at 500 characters.
* Payment logs record order numbers, payment IDs, provider codes, amounts and
  event IDs - identifiers, not credentials.

No sensitive-data logging findings.

### Playwright (UI-level) preparation

Prepared under `tests/Playwright` with a desktop project (1280x800) and a
mobile project (390x844, Pixel 7). `e2e/customer.spec.ts` covers mobile
navigation, product search, filters, variation selection, add to cart, cart
update, guest checkout, login and the customer order view. `e2e/admin.spec.ts`
covers the admin authorization guard, dashboard, product listing /
add-product entry point and the admin invoice view. The specs are type-checked
(`tsc --noEmit`) and documented in `tests/Playwright/README.md`.

These specs require a running, database-backed, seeded application instance
(the in-memory integration host is intentionally not a valid Playwright
target), so they are prepared but not executed in this environment.

## Known gaps

* **Playwright execution requires a real app instance.** The specs are written,
  type-checked and documented but not run here because they need a
  database-backed host with seeded catalogue data and an admin account. They
  are ready to run against a local or CI-hosted instance.
* **Admin product creation is API-driven.** The admin product listing page
  currently exposes an "Add Product" entry point backed by the
  `api/admin/products` API (covered by `AdminProductCrudTests`); the admin spec
  asserts the entry point renders. The create form is delivered by the API
  surface rather than a full browser form.

## Completion status

Phase 31 automated test and quality verification is **complete**:

* 610 unit tests and 313 integration tests pass (verified repeatedly after the
  test-isolation and SQLitePCLRaw changes).
* The Release build is clean under warnings-as-errors.
* Formatting is clean repo-wide.
* No pending EF model changes.
* The NuGet vulnerability audit is clean (three transitive High advisories
  remediated this phase).
* Nullability and logging reviews completed with no findings.
* Playwright UI-level preparation is in place for desktop and mobile viewports.

Verified 2026-08-15.
