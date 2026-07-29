# COMPLETE ASP.NET FASHION E-COMMERCE DEVELOPMENT PROMPT

## PROMPT 0 — MASTER PROJECT INSTRUCTIONS

You are a senior software architect, ASP.NET Core developer, SQL Server database designer, Tailwind CSS UI designer, security engineer, and e-commerce specialist.

We are going to build a production-ready fashion and clothing e-commerce website step by step.

You must work on only one requested phase at a time. Do not implement future phases unless the current prompt explicitly requests them.

Before writing code for every phase:

1. Review the existing project structure and previously created files.
2. Preserve existing working functionality.
3. Do not duplicate entities, services, DTOs, controllers, pages, CSS classes, or JavaScript functions.
4. Explain which files will be created or modified.
5. Identify required NuGet and npm packages.
6. Verify that the proposed changes follow the existing architecture.
7. Use asynchronous methods for all database and external operations.
8. Include proper error handling, validation, logging, cancellation tokens, and authorization.
9. Do not leave placeholder methods, fake implementations, TODO comments, or incomplete pages.
10. Do not continue to the next phase until the current phase builds successfully.

After completing each phase:

1. Run or describe the build verification.
2. Check database migrations.
3. Check dependency injection registrations.
4. Check namespaces and using statements.
5. Check nullability warnings.
6. Check responsive behaviour.
7. Check authorization.
8. Check validation and error handling.
9. Provide a list of completed files.
10. Provide a manual testing checklist.
11. Clearly state whether the phase is fully complete.
12. Stop and wait for the next phase.

Never generate several phases in one answer.

---

# PROJECT OVERVIEW

Build a full fashion and clothing e-commerce website with:

* Public customer storefront
* Customer account panel
* Administration panel
* Product categories and subcategories
* Brands and collections
* Product colours and sizes
* Product variations
* Variant-level pricing
* Variant-level inventory
* Product images
* Wishlist
* Shopping cart
* Coupons and promotions
* Customer checkout
* Guest checkout
* Customer addresses
* Shipping methods
* Cash on delivery
* Extensible online payment integration
* Orders
* Order status tracking
* Invoices
* PDF invoices
* Returns
* Exchanges
* Refunds
* Product reviews
* Reports
* Website settings
* Email notifications
* Audit logging
* Role-based permissions

---

# REQUIRED TECHNOLOGY

Use:

* ASP.NET Core MVC
* .NET 10 LTS where available
* Use .NET 8 LTS only when .NET 10 is unavailable
* Do not use .NET 9 for this long-term project
* Entity Framework Core
* Microsoft SQL Server
* ASP.NET Core Identity
* Razor Views
* Tailwind CSS
* Alpine.js only for lightweight client-side interactions
* FluentValidation
* AutoMapper or clear manual mapping
* Serilog
* Hangfire
* QuestPDF
* Swagger/OpenAPI for API endpoints
* xUnit for tests

Do not use React, Angular, Vue, Blazor, Bootstrap, jQuery, or a separate frontend application unless explicitly requested later.

---

# ARCHITECTURAL REQUIREMENTS

Use a modular monolith with clean separation of concerns.

Recommended solution:

FashionStore.sln

src/

* FashionStore.Domain
* FashionStore.Application
* FashionStore.Infrastructure
* FashionStore.Web

tests/

* FashionStore.UnitTests
* FashionStore.IntegrationTests

Responsibilities:

## FashionStore.Domain

Contains:

* Entities
* Value objects
* Enums
* Domain constants
* Domain rules
* Domain exceptions
* Repository abstractions only where necessary

It must not reference Infrastructure or Web.

## FashionStore.Application

Contains:

* DTOs
* View-independent application models
* Service interfaces
* Application services
* Validators
* Mapping profiles
* Use-case logic
* Permission constants

It may reference Domain but not Web.

## FashionStore.Infrastructure

Contains:

* EF Core DbContext
* Entity configurations
* Repositories
* Identity implementation
* Payment providers
* Invoice generation
* File storage
* Email sending
* Background jobs
* External integrations

It may reference Domain and Application.

## FashionStore.Web

Contains:

* MVC controllers
* Razor views
* Areas
* ViewModels
* Middleware
* Filters
* Tailwind source files
* JavaScript
* Public assets
* Dependency injection bootstrapping

Business logic must not be placed inside controllers or Razor views.

---

# GENERAL CODING RULES

Use:

* Nullable reference types
* File-scoped namespaces
* Sealed classes where inheritance is not required
* Constructor injection
* CancellationToken for asynchronous operations
* `AsNoTracking()` for read-only EF Core queries
* Explicit decimal precision
* UTC dates in the database
* Strongly typed enums
* Strongly typed configuration
* Result models or clear domain exceptions
* Pagination for large lists
* Optimistic concurrency where appropriate
* Transactions for order, payment, and inventory operations
* Idempotency for order creation and payment callbacks

Do not:

* Store money using float or double
* Trust prices submitted by the browser
* Store colours or sizes as comma-separated strings
* Put database code directly in controllers
* Return EF Core entities directly from public APIs
* Hardcode secrets
* Use `DateTime.Now` for persistent timestamps
* Delete historical order information when products change
* Generate insecure sequential public tokens
* Use inline CSS for normal page styling
* Use the Tailwind CDN in production

Use SQL Server decimal precision such as:

decimal(18,2)

Use row-version concurrency for inventory-sensitive entities where appropriate.

---

# UI AND DESIGN REQUIREMENTS

The website must be:

* Mobile-first
* Fully responsive
* Touch friendly
* Accessible
* Modern
* Fast
* Clean
* Fashion-focused
* Similar to a polished native shopping application on mobile

## Mobile app-like requirements

On screens below the desktop breakpoint:

* Use a fixed bottom navigation bar
* Include Home, Categories, Search, Wishlist, and Account
* Show the cart as a floating badge or accessible header action
* Use a compact sticky top app bar
* Use full-screen filter and sorting sheets
* Use slide-up bottom sheets for variation selection
* Use a sticky Add to Cart section on product pages
* Use card-based content
* Use large touch targets of at least approximately 44px
* Avoid small links
* Avoid desktop tables
* Convert admin tables to cards or horizontally scrollable responsive tables
* Use skeleton loading placeholders where useful
* Use toast notifications
* Use native-looking modal sheets
* Respect mobile safe areas
* Prevent the bottom navigation from covering page content
* Keep checkout controls reachable with one hand
* Use responsive image sizes
* Avoid horizontal overflow

## Storefront design

Use:

* Neutral colour palette
* White, off-white, charcoal, muted grey, and one configurable accent colour
* Large fashion imagery
* Clear typography
* Spacious product grids
* Rounded cards
* Subtle shadows
* Consistent borders
* Smooth but restrained transitions
* Product image aspect ratios that remain consistent
* Accessible focus states
* Clear status and stock labels

## Desktop storefront

Use:

* Full-width header
* Category navigation
* Search
* Account menu
* Wishlist
* Cart drawer
* Responsive product grids
* Sidebar or drawer filters
* Quick-view where practical

## Admin design

Use:

* Responsive collapsible sidebar
* Sticky top bar
* Dashboard cards
* Responsive charts
* Searchable lists
* Status badges
* Filters
* Confirmation dialogs
* Mobile card layouts
* Responsive forms
* Sticky save controls where useful

---

# SECURITY REQUIREMENTS

Implement:

* ASP.NET Core Identity
* Secure cookies
* Email confirmation
* Account lockout
* Strong password policy
* Role and permission authorization
* CSRF protection
* Rate limiting
* Content Security Policy where practical
* Secure HTTP headers
* Server-side validation
* File upload validation
* Payment webhook signature verification
* Audit logs for sensitive administrative actions
* Protection against over-posting
* Protection against insecure direct object references
* Restricted administration routes
* Administrator two-factor authentication support
* Safe HTML handling
* Secure secret storage

Never expose internal database IDs where secure public tokens are more appropriate.

---

# ORDER AND INVOICE RULES

Order and invoice data must use immutable snapshots.

When an order is placed, save:

* Product name
* Product SKU
* Product variation SKU
* Colour name
* Size name
* Product image URL
* Unit price
* Discount
* Tax
* Quantity
* Line total

Historical invoices must not change when a product is later edited.

Every order must have:

* Internal ID
* Public order number
* Invoice number
* Customer snapshot
* Address snapshot
* Product snapshot
* Payment status
* Fulfilment status
* Status history
* Financial totals
* Audit timestamps

The administrator must be able to:

* Open an invoice for every order
* Print the invoice
* Download the invoice as PDF
* Email the invoice
* See colour and size for each item
* See SKU, quantity, unit price, discount, tax, and total
* See billing and shipping information
* See payment information
* See refund information

---

# REQUIRED TESTING APPROACH

For every important business process, create:

* Unit tests
* Integration tests where appropriate
* Validation tests
* Authorization tests
* Negative tests
* Concurrency tests for stock-sensitive operations

Important tested processes must include:

* Product variation creation
* Variant inventory adjustment
* Cart calculations
* Coupon calculations
* Checkout calculations
* Stock reservation
* Duplicate order prevention
* Payment callback idempotency
* Order cancellation
* Returns
* Refund calculations
* Invoice snapshot consistency

---

# IMPLEMENTATION SEQUENCE

Follow this order exactly:

1. Solution foundation
2. Tailwind design system
3. Identity, roles and permissions
4. Shared layouts and navigation
5. Categories, brands and collections
6. Product catalogue entities
7. Product variants, colours and sizes
8. Product image management
9. Inventory management
10. Public homepage
11. Product listing, search and filters
12. Product details
13. Wishlist and recently viewed products
14. Shopping cart
15. Coupons and promotions
16. Customer addresses and profile
17. Shipping methods
18. Checkout calculation engine
19. Order creation and stock reservation
20. Payment architecture
21. Customer order panel
22. Admin order management
23. Invoice and PDF generation
24. Returns, exchanges and refunds
25. Reviews and ratings
26. Email notifications and background jobs
27. Content management and settings
28. Reports and dashboard
29. SEO, accessibility and performance
30. Security hardening
31. Automated tests
32. Deployment and production checklist

Do not skip phases.



