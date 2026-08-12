
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
. Email notifications and background jobs
27. Content management and settings
28. Reports and dashboard
29. SEO, accessibility and performance
30. Security hardening
31. Automated tests
32. Deployment and production checklist

Do not skip phases.





Continue the FashionStore project using all rules from the master project prompt.

Implement only Phase 1: Solution Foundation.

## Objectives

Create the complete solution and project structure using:

* ASP.NET Core MVC
* .NET 10 LTS where available
* .NET 8 LTS when .NET 10 is unavailable
* SQL Server
* EF Core
* Clean modular monolith architecture

## Required projects

Create:

* FashionStore.Domain
* FashionStore.Application
* FashionStore.Infrastructure
* FashionStore.Web
* FashionStore.UnitTests
* FashionStore.IntegrationTests

Configure correct project references without circular dependencies.

## Required foundation features

Implement:

* Nullable reference types
* Central package management where appropriate
* Consistent warnings and analyzers
* Environment-specific appsettings
* Strongly typed configuration
* SQL Server DbContext foundation
* EF Core design-time factory
* Dependency injection extension methods for every layer
* Serilog configuration
* Global exception-handling middleware
* Standard error response model
* Development error page
* Production-safe error page
* Health check endpoint
* Request correlation ID
* UTC date handling conventions
* Basic audit entity base classes
* Pagination request and response models
* Result or error model for application operations
* Initial empty migration
* Database connection verification
* Swagger support for future API endpoints

## Base domain classes

Create suitable base types such as:

* Entity
* AuditedEntity
* SoftDeletableEntity where appropriate
* FullAuditedEntity where appropriate
* DomainException

Do not apply soft deletion to order or financial history by default.

## Configuration sections

Prepare strongly typed configuration for:

* Database
* Store information
* Email
* File storage
* Payments
* Invoice
* Security
* Background jobs

Do not add real credentials.

## Error handling

Create global handling for:

* Validation errors
* Not-found errors
* Unauthorized access
* Forbidden access
* Domain rule violations
* Unexpected errors

Log unexpected exceptions with the correlation ID.

Do not expose stack traces in production.

## Deliverables

Provide:

1. Final solution tree
2. Complete code for every created file
3. Package installation commands
4. Migration commands
5. Dependency direction explanation
6. Build verification instructions
7. Manual testing checklist
8. Completion status

Stop after Phase 1 and commit with phase details.



Continue the existing FashionStore project.

Implement only Phase 2: Tailwind CSS Design System.

Do not implement product or order features.

## Objectives

Configure a production-ready Tailwind CSS workflow and build a reusable mobile-first design system.

## Required setup

Configure:

* Tailwind CSS
* PostCSS
* Autoprefixer
* Production CSS minification
* Content scanning for Razor views
* Development watch command
* Production build command
* npm scripts
* Generated CSS output under wwwroot
* Static-file versioning support

Do not use the Tailwind CDN.

## Design tokens

Create reusable theme tokens for:

* Primary accent
* Secondary accent
* Background
* Surface
* Border
* Muted text
* Primary text
* Success
* Warning
* Danger
* Information
* Border radius
* Shadows
* Container widths
* Typography scale
* Spacing
* Transitions
* Z-index layers

Make the accent colour easy to change later.

## Reusable components

Create reusable Razor partials, Tag Helpers, or component classes for:

* Primary button
* Secondary button
* Outline button
* Danger button
* Icon button
* Input
* Select
* Textarea
* Checkbox
* Radio button
* Toggle
* Form validation message
* Card
* Product card shell
* Badge
* Status badge
* Alert
* Toast
* Modal
* Mobile bottom sheet
* Drawer
* Dropdown
* Breadcrumb
* Pagination
* Empty state
* Skeleton loader
* Loading spinner
* Responsive table shell
* Mobile list card
* Tabs
* Accordion
* Price display
* Quantity selector

## Accessibility

Ensure:

* Visible keyboard focus
* Sufficient colour contrast
* Semantic HTML
* ARIA labels where necessary
* Escape-key modal closing
* Focus trapping for modal and drawer components
* Reduced-motion support
* Screen-reader text utilities
* Touch targets of approximately 44px or larger

## Mobile app-like behaviour

Create foundational styles for:

* Sticky mobile app header
* Fixed bottom navigation
* Safe-area spacing
* Bottom sheets
* Full-screen mobile search
* Mobile filter drawer
* Sticky mobile action bar
* Toast area above bottom navigation
* Preventing content from being hidden behind fixed navigation

## Deliverables

Provide all code and configuration files.

Include:

* npm commands
* Development command
* Production build command
* Component preview page
* Responsive testing checklist
* Accessibility checklist
* Completion status

Stop after Phase 2 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 3: Identity, Roles and Permissions.

## Objectives

Implement secure customer and administrator authentication using ASP.NET Core Identity.

## User model

Create an ApplicationUser with appropriate properties such as:

* FirstName
* LastName
* DisplayName
* PhoneNumber
* ProfileImageUrl
* IsActive
* LastLoginAtUtc
* CreatedAtUtc
* UpdatedAtUtc

Do not put shipping addresses directly inside the Identity table.

## Authentication features

Implement:

* Customer registration
* Login
* Logout
* Email confirmation
* Forgot password
* Reset password
* Change password
* Account lockout
* Remember me
* Secure cookie settings
* External login-ready architecture
* Two-factor authentication-ready architecture
* Administrator login protection

## Roles

Seed:

* SuperAdmin
* Admin
* ProductManager
* OrderManager
* InventoryManager
* CustomerSupport
* ContentManager
* Customer

## Permission system

Implement policy-based permissions.

Create permissions such as:

* Dashboard.View
* Products.View
* Products.Create
* Products.Update
* Products.Delete
* Products.ManageInventory
* Categories.Manage
* Brands.Manage
* Orders.View
* Orders.UpdateStatus
* Orders.Cancel
* Orders.Refund
* Orders.PrintInvoice
* Customers.View
* Customers.Update
* Reviews.Manage
* Content.Manage
* Reports.View
* Settings.Manage
* Users.Manage
* Roles.Manage
* AuditLogs.View

Allow roles to receive multiple permissions.

Do not rely only on role checks inside controllers.

## Seed administrator

Provide a safe development-only method for creating the initial SuperAdmin.

Read the development administrator credentials from configuration or user secrets.

Never hardcode the password in source code.

## UI

Create mobile-friendly pages for:

* Login
* Registration
* Email confirmation result
* Forgot password
* Reset password
* Access denied
* Account locked

The mobile pages should look like a native shopping application.

## Security

Include:

* CSRF protection
* Rate limiting for login and password-reset endpoints
* Secure password rules
* Cookie security
* Generic login error messages
* Account enumeration protection
* Open redirect protection
* Audit events for administrative login and permission changes

## Testing

Add tests for:

* Registration validation
* Login
* Lockout
* Permission authorization
* Customer inability to access admin pages
* Deactivated user login
* Password reset token behaviour

Stop after Phase 3 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 4: Shared Layouts and Navigation.

## Public desktop layout

Create:

* Announcement bar
* Main header
* Logo
* Product search field
* Category navigation
* Account menu
* Wishlist icon
* Cart icon and badge
* Optional promotional menu
* Main content container
* Newsletter section
* Footer
* Cookie consent area

## Mobile app layout

Create a native-app-inspired mobile interface with:

* Compact sticky top app bar
* Logo or page title
* Search action
* Cart action with badge
* Fixed bottom navigation
* Home tab
* Categories tab
* Search tab
* Wishlist tab
* Account tab
* Active-tab indicator
* Safe-area bottom spacing
* Proper page padding so content is never hidden
* Slide-out cart-ready container
* Full-screen search-ready container

The bottom navigation must not appear in the admin layout.

## Customer account layout

Create:

* Account header
* Profile summary
* Mobile account menu cards
* Desktop account sidebar
* Breadcrumbs
* Logout action

## Admin layout

Create:

* Responsive sidebar
* Collapsible desktop sidebar
* Mobile sidebar drawer
* Top header
* User menu
* Notification area
* Breadcrumb
* Page title section
* Main content region
* Responsive action buttons
* Mobile sticky action bar support

## Navigation architecture

Use reusable navigation models or services.

Do not hardcode duplicated navigation markup in several pages.

Hide admin links when the authenticated user lacks permission.

## States

Create consistent:

* Empty state
* Loading state
* Access-denied state
* Not-found state
* Error state
* Offline-style connection-error state

## Deliverables

Include a demonstration page for all layouts on desktop and mobile.

Stop after Phase 4 and commit with phase details.




Continue the FashionStore project.

Implement only Phase 5: Categories, Brands and Collections.

## Entities

Create production-ready entities for:

### Category

Include:

* Name
* Slug
* Description
* ParentCategoryId
* DisplayOrder
* ImageUrl
* IconUrl
* IsActive
* ShowInMainMenu
* SEO title
* SEO description
* Created and updated timestamps

Support unlimited hierarchy but prevent circular parent relationships.

### Brand

Include:

* Name
* Slug
* Description
* LogoUrl
* WebsiteUrl
* IsActive
* DisplayOrder
* SEO fields
* Audit timestamps

### Collection

Include:

* Name
* Slug
* Description
* BannerImageUrl
* StartAtUtc
* EndAtUtc
* IsActive
* DisplayOrder
* SEO fields
* Audit timestamps

## Administration

Create responsive CRUD management for:

* Categories
* Brands
* Collections

Include:

* Search
* Filters
* Pagination
* Sorting
* Create
* Edit
* Activate or deactivate
* Reordering
* Validation
* Slug generation
* Unique-name and unique-slug protection
* Image selection-ready fields

Do not hard-delete an item that is already referenced by products.

## Mobile admin UI

On mobile:

* Use cards instead of wide tables
* Use a floating or sticky Add button
* Use a full-screen or bottom-sheet filter panel
* Make actions accessible through a compact menu
* Keep save buttons sticky when forms are long

## Public services

Create read services for:

* Active menu categories
* Category hierarchy
* Active brands
* Active collections

Use caching where appropriate with proper cache invalidation.

## Testing

Test:

* Duplicate slug protection
* Circular category prevention
* Inactive item exclusion
* Reordering
* Referenced-category deletion prevention

Stop after Phase 5 and commit with phase details.


Continue the FashionStore project.

Implement only Phase 6: Core Product Catalogue.

Do not yet implement product variations. Product variations will be handled in the next phase.

## Product entity

Create a Product entity containing:

* Name
* Slug
* ShortDescription
* FullDescription
* CategoryId
* BrandId
* CollectionId
* ProductType
* Material
* Fabric
* CareInstructions
* Gender or target group
* CountryOfOrigin
* BaseSku
* Barcode
* BasePrice
* CompareAtPrice
* CostPrice
* TaxCategory
* Weight
* IsActive
* IsFeatured
* IsNewArrival
* IsBestSeller
* AllowReviews
* PublishedAtUtc
* SEO title
* SEO description
* Search keywords
* Audit timestamps
* Row version where useful

Use decimal precision for money and measurements.

## Related entities

Create:

* ProductTag
* ProductTagMapping
* RelatedProduct
* ProductSpecification
* ProductSizeGuideMapping

## Rich text

Provide a secure approach for product descriptions.

Do not allow unsafe HTML.

## Product administration

Create:

* Product list
* Search
* Filters
* Sorting
* Pagination
* Create form
* Edit form
* Draft and publish states
* Duplicate product action
* Archive or deactivate action
* SEO section
* Pricing section
* Details section
* Organisation section
* Search preview

Use tabbed sections on desktop and stacked accordion sections on mobile.

## Validation

Validate:

* Required fields
* Unique slug
* Valid price ranges
* Compare price not below selling price when used
* Cost price rules
* Valid relationships
* Slug normalization
* Maximum text lengths

## Product snapshots

Prepare the product model so order items can later save immutable product snapshots.

## Testing

Add tests for:

* Price validation
* Duplicate slug
* Publishing requirements
* Inactive category handling
* Product duplication
* Search query behaviour

Stop after Phase 6 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 7: Colours, Sizes, Attributes and Product Variations.

This is a critical phase. Design it carefully.

## Requirements

A fashion product may have variations such as:

* Black / Small
* Black / Medium
* Blue / Small
* Blue / Large

Each variation may have its own:

* SKU
* Barcode
* Price
* Compare-at price
* Cost price
* Stock
* Weight
* Image
* Active status

Do not store colours or sizes as comma-separated strings.

## Entities

Create:

### ProductAttribute

Examples:

* Colour
* Size
* Fit
* Material

Fields:

* Name
* Slug
* DisplayType
* IsVariationAttribute
* DisplayOrder
* IsActive

### ProductAttributeValue

Fields:

* ProductAttributeId
* Name
* Slug
* DisplayValue
* HexColour
* ImageUrl
* DisplayOrder
* IsActive

### ProductVariant

Fields:

* ProductId
* Sku
* Barcode
* Price
* CompareAtPrice
* CostPrice
* Weight
* IsDefault
* IsActive
* ImageUrl
* RowVersion
* Audit timestamps

### ProductVariantAttributeValue

Connect one variant to its attribute values.

Add a unique constraint that prevents duplicate variation combinations for the same product.

## Administration UI

Create an advanced variation builder.

Administrators should be able to:

1. Select variation attributes.
2. Select values such as colours and sizes.
3. Generate all valid combinations.
4. Remove unwanted combinations.
5. Enter SKU, price, stock, image, and status for each combination.
6. Perform bulk price changes.
7. Perform bulk stock changes.
8. Activate or deactivate combinations.
9. Set a default combination.

Desktop:

* Use a responsive variation matrix.

Mobile:

* Use collapsible cards per variation.
* Show colour and size clearly.
* Provide sticky save controls.
* Avoid wide unusable tables.

## Rules

* Every variant SKU must be unique.
* A product cannot contain duplicate attribute combinations.
* Only one default variant is allowed.
* Inactive attribute values cannot be selected for new variations.
* Existing order history must not be affected if an attribute value is renamed.
* Prevent deleting attribute values used by variants unless safely handled.
* Validate price and compare-price relationships.
* Support products without variations where necessary.

## Storefront preparation

Create DTOs suitable for showing:

* Available colours
* Available sizes
* Valid colour-size combinations
* Variant price
* Stock availability
* Variant image
* Disabled combinations

## Testing

Include tests for:

* Duplicate combinations
* Duplicate SKU
* Default variation enforcement
* Combination generation
* Invalid attribute deletion
* Price validation
* Variant lookup by selected values

Stop after Phase 7 and commit with phase details.




Continue the FashionStore project.

Implement only Phase 8: Product Image Management.

## Requirements

Support:

* Main product image
* Product gallery images
* Variant-specific images
* Category images
* Brand logos
* Collection banners
* Image ordering
* Alternative text
* Image captions
* Responsive image versions
* WebP or AVIF optimisation where supported
* Image removal
* Image replacement

## Storage architecture

Create an abstraction such as:

* IFileStorageService
* LocalFileStorageService
* CloudFileStorage-ready implementation contract

Use local storage during development.

Do not tightly couple controllers to physical file paths.

## Upload security

Validate:

* File extension
* MIME type
* File signature
* Maximum file size
* Image dimensions
* Image count per product
* Safe generated filename
* Storage path
* Authorization

Prevent executable or unsafe file uploads.

## Image processing

Implement:

* Original image preservation when appropriate
* Thumbnail generation
* Product-card size
* Product-detail size
* Gallery size
* Image compression
* Orientation correction
* Safe removal of generated versions

Do not block HTTP requests with unnecessary heavy processing. Prepare background processing where appropriate.

## Administration UI

Desktop:

* Drag-and-drop upload
* Image reordering
* Main-image selection
* Alternative-text editing
* Variant assignment

Mobile:

* Camera or gallery upload support
* Large touch upload area
* Reorder controls that work without precision dragging
* Preview cards
* Upload progress
* Retry option

## Storefront

Use:

* Responsive `srcset`
* Lazy loading
* Explicit image dimensions
* Appropriate aspect ratio
* Fallback image
* Accessible alternative text

## Testing

Test:

* Invalid file rejection
* File signature validation
* Duplicate filename safety
* Image ordering
* Main image selection
* Storage deletion
* Authorization

Stop after Phase 8 and commit with phase details.




Continue the FashionStore project.

Implement only Phase 9: Variant-Level Inventory Management.

## Inventory model

Create:

* Warehouse
* WarehouseStock
* InventoryTransaction
* StockReservation
* StockAdjustmentReason

Support one default warehouse initially, but design for multiple warehouses.

## Stock values

Track:

* OnHandQuantity
* ReservedQuantity
* AvailableQuantity
* LowStockThreshold
* ReorderLevel
* AllowBackorder

Available quantity must be calculated safely.

Do not store inconsistent duplicated totals without clear synchronisation rules.

## Inventory transactions

Support transaction types:

* Initial stock
* Purchase receipt
* Manual increase
* Manual decrease
* Order reservation
* Reservation release
* Order fulfilment
* Customer return
* Damaged stock
* Lost stock
* Correction

Save:

* Product variant
* Warehouse
* Quantity change
* Previous quantity
* New quantity
* Reason
* Reference type
* Reference ID
* Administrator
* Timestamp
* Notes

## Administration

Create:

* Inventory overview
* Search by product, variation, SKU, colour, and size
* Low-stock filter
* Out-of-stock filter
* Stock-adjustment form
* Inventory history
* Variant inventory details
* CSV export-ready service
* Permission checks

Mobile admin:

* Inventory cards
* Visible SKU
* Colour and size badges
* Quick adjustment bottom sheet
* Large increase and decrease controls
* Confirmation before stock reduction

## Concurrency

Use safe concurrency handling.

Prevent two simultaneous operations from selling or adjusting the same stock incorrectly.

Use:

* Transactions
* Row version
* Atomic database updates
* Retry or conflict messages where appropriate

## Stock reservation

Prepare stock reservations for later checkout.

A reservation must have:

* Variant
* Quantity
* Session or cart reference
* Expiration
* Status
* Created timestamp
* Released timestamp

## Background process

Prepare a Hangfire job for releasing expired reservations.

## Testing

Include concurrency tests and tests for:

* Stock increases
* Stock decreases
* Preventing negative stock
* Reservation
* Reservation release
* Low stock
* Multi-warehouse totals
* Duplicate transaction prevention

Stop after Phase 9 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 10: Public Storefront Homepage.

## Homepage sections

Create configurable sections for:

* Announcement bar
* Hero banner
* Promotional banners
* Shop by category
* New arrivals
* Featured products
* Best sellers
* Current collections
* Sale products
* Shop by brand
* Benefits such as delivery, returns, and secure payment
* Newsletter
* Social or lookbook section
* Footer

## Mobile design

The mobile homepage must feel like a shopping application.

Include:

* Sticky compact app header
* Search entry
* Cart badge
* Bottom navigation
* Horizontally scrollable category shortcuts
* Swipe-friendly promotional cards
* Two-column product grid
* Compact product cards
* Wishlist buttons
* Native-looking skeleton loaders
* Safe spacing above the bottom navigation
* No accidental horizontal page overflow

## Product card

Show:

* Main image
* Product name
* Brand where available
* Current price
* Compare price
* Discount badge
* New badge
* Stock state
* Wishlist action
* Available colour preview
* Product link

Do not place Add to Cart directly on cards when variation selection is required unless a variation-selection sheet is shown.

## Performance

Use:

* Efficient queries
* Projection to DTOs
* Caching
* Responsive images
* Lazy loading below the fold
* Limited initial item counts
* No N+1 database queries

## Administration preparation

Homepage section content must be configurable later rather than permanently hardcoded.

## Testing

Test:

* Inactive product exclusion
* Out-of-date collection exclusion
* Product ordering
* Cache invalidation
* Mobile layout
* Empty-section behaviour

Stop after Phase 10 and commit with phase details.

Continue the FashionStore project.

Implement only Phase 11: Product Listing, Search, Sorting and Filters.

## Listing pages

Create:

* All products
* Category products
* Brand products
* Collection products
* Search results
* Sale products
* New arrivals
* Best sellers

## Filters

Support:

* Category
* Subcategory
* Brand
* Collection
* Colour
* Size
* Price range
* Availability
* Discount
* Rating when reviews exist
* Product tags
* Material
* Gender or target group

## Sorting

Support:

* Relevance
* Newest
* Oldest
* Price low to high
* Price high to low
* Popularity
* Best selling
* Highest rated
* Discount

## URL behaviour

Use query-string parameters.

Requirements:

* Shareable URLs
* Back-button support
* Filter persistence
* Search-engine-friendly canonical behaviour
* No invalid filter crashes
* Pagination preservation
* Sorting preservation

## Mobile UI

Use:

* Sticky result header
* Filter button
* Sort button
* Full-screen filter sheet
* Bottom-sheet sorting
* Selected-filter chips
* Clear-all action
* Two-column product grid
* Optional one-column list mode
* Bottom navigation safe area
* Loading skeletons

## Desktop UI

Use:

* Sidebar filters or filter drawer
* Product count
* Sorting dropdown
* Grid controls
* Pagination
* Applied-filter chips

## Search

Search:

* Product name
* SKU
* Variant SKU
* Brand
* Category
* Tags
* Search keywords

Normalize search safely.

Use SQL-friendly and indexed queries.

Prepare an abstraction for a future external search engine without requiring one now.

## Performance

Prevent N+1 queries.

Use projection, indexes, pagination, and query limits.

## Testing

Test:

* Combined filters
* Invalid filter values
* Search
* Sorting
* Pagination
* Inactive product exclusion
* Inactive variation handling
* Empty results
* SEO URL behaviour

Stop after Phase 11 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 12: Product Details Page.

## Product information

Show:

* Product name
* Brand
* Category
* Product images
* Product video-ready area
* Price
* Compare price
* Discount
* Description
* Material
* Fabric
* Care instructions
* Size guide
* Delivery information
* Return information
* Stock availability
* SKU
* Colour options
* Size options
* Related products
* Recently viewed products
* Reviews placeholder
* Wishlist action

## Variation selection

The customer must select a valid combination.

Requirements:

* Selecting a colour updates available sizes.
* Selecting a size updates available colours.
* Invalid combinations are disabled.
* Selected variant updates price.
* Selected variant updates image.
* Selected variant updates SKU.
* Selected variant updates stock.
* Out-of-stock combinations cannot be purchased.
* The server must verify the variation again when adding to cart.

## Mobile UI

Create a native-app-inspired experience:

* Swipeable image gallery
* Sticky compact app bar
* Back button
* Wishlist button
* Cart badge
* Bottom sticky purchase bar
* Price and Add to Cart controls
* Variation selection in a slide-up bottom sheet
* Large colour swatches
* Large size buttons
* Expandable details
* Full-screen size guide modal
* Safe-area support
* Content spacing above the bottom purchase bar

## Desktop UI

Use:

* Image gallery
* Sticky product information column
* Colour swatches
* Size buttons
* Quantity selector
* Add to Cart
* Wishlist
* Product accordions

## Add-to-cart preparation

Send only:

* Product ID
* Variant ID
* Quantity

Do not trust client-supplied product price, colour name, size name, or total.

## SEO

Implement:

* Canonical URL
* Product metadata
* Open Graph metadata
* Structured product data
* Breadcrumb structured data

## Testing

Test:

* Invalid combinations
* Out-of-stock selection
* Price changes
* Default variation
* Disabled variation
* Server validation
* Structured data

Stop after Phase 12 and commit with phase details.


Continue the FashionStore project.

Implement only Phase 13: Wishlist and Recently Viewed Products.

## Wishlist

Support:

* Authenticated customer wishlist
* Optional anonymous temporary wishlist
* Merge anonymous wishlist after login
* Add product
* Remove product
* Move item to cart
* Wishlist item count
* Prevent duplicates
* Product availability information
* Variation selection when required

## Recently viewed

Track:

* Product
* Customer or anonymous session
* Last viewed timestamp
* Maximum retained item count
* Deduplication
* Expiration rules

Do not create an excessive database write on every repeated page refresh.

## Mobile UI

Wishlist:

* App-like header
* Product cards
* Colour and size information where a variation is saved
* Remove action
* Select variation action
* Move to cart
* Empty-state illustration area
* Bottom navigation

Recently viewed:

* Horizontal product carousel
* Clear history action
* Privacy-friendly behaviour

## Security and privacy

Ensure one customer cannot access another customer’s wishlist or browsing history.

## Testing

Test:

* Duplicate prevention
* Anonymous merge
* Authorization
* Inactive product handling
* Deleted product handling
* Recently viewed ordering
* Maximum retention

Stop after Phase 13 and commit with phase details.




Continue the FashionStore project.

Implement only Phase 14: Shopping Cart.

## Cart architecture

Support:

* Anonymous cart
* Authenticated cart
* Anonymous-to-customer merge
* Variant-level items
* Quantity changes
* Item removal
* Clear cart
* Cart count
* Mini-cart
* Cart drawer
* Full cart page
* Stock validation
* Price recalculation
* Cart expiration

## Cart item

A cart item must reference:

* Product
* Product variant
* Quantity

Displayed information may include:

* Product name
* Product image
* Variant SKU
* Colour
* Size
* Current unit price
* Compare price
* Discount
* Quantity
* Line total
* Stock state

Do not permanently store browser-submitted prices as trusted values.

## Server calculation

Every cart read and update must verify:

* Product active state
* Variant active state
* Current price
* Current promotion
* Available stock
* Maximum quantity
* Current tax rules where applicable

## Mobile UI

Create:

* Slide-out or full-screen cart
* Large product cards
* Visible colour and size
* Touch-friendly quantity controls
* Swipe-style removal preparation
* Sticky order-summary section
* Sticky checkout button
* Safe-area padding
* Clear unavailable-item warnings

## Desktop UI

Create:

* Responsive cart list
* Order summary sidebar
* Coupon placeholder
* Shipping estimate placeholder
* Continue shopping
* Checkout button

## Merge rules

When merging:

* Combine identical product variants.
* Respect maximum stock.
* Avoid duplicate items.
* Preserve the most recent valid quantity.
* Report unavailable items clearly.

## Concurrency and security

Protect cart ownership.

Use server-generated cart identifiers.

Do not expose another user’s cart.

## Testing

Test:

* Anonymous cart
* Login merge
* Quantity update
* Stock limit
* Price changes
* Inactive variant
* Duplicate merge
* Cart ownership

Stop after Phase 14 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 15: Coupons and Promotions.

## Coupon model

Support:

* Coupon code
* Percentage discount
* Fixed discount
* Minimum order value
* Maximum discount
* Start and end date
* Total usage limit
* Per-customer usage limit
* First-order-only rule
* Customer-specific coupon
* Category restriction
* Brand restriction
* Product restriction
* Excluded products
* Active status
* Automatic or manually entered coupon
* Free-shipping coupon

## Promotion model

Support:

* Product sale
* Category sale
* Brand sale
* Collection sale
* Quantity-based discount preparation
* Time-limited promotion
* Priority
* Stackable or non-stackable rules

## Calculation engine

Build one central pricing and discount service.

It must:

* Recalculate on the server
* Return discount breakdown
* Explain rejected coupon reasons
* Prevent negative totals
* Respect currency precision
* Avoid duplicate application
* Handle promotion priority
* Handle coupon stacking rules
* Be deterministic

## Administration

Create responsive management for:

* Coupons
* Promotions
* Usage history
* Active and expired filters
* Customer usage
* Duplication
* Activation and deactivation

## Mobile customer UI

Provide:

* Coupon field
* Apply button
* Applied coupon card
* Remove action
* Validation message
* Discount breakdown
* Native-looking success toast

## Tests

Add comprehensive tests for:

* Date validity
* Minimum order
* Maximum discount
* Usage limit
* Customer limit
* Product restrictions
* Category restrictions
* Stacking
* Expired coupon
* Case-insensitive code
* Concurrent usage

Stop after Phase 15 and commit with phase details.




Continue the FashionStore project.

Implement only Phase 16: Customer Profile and Address Book.

## Customer profile

Support:

* First name
* Last name
* Display name
* Email
* Phone
* Profile image
* Date of birth as optional
* Marketing preference
* Notification preferences
* Password change
* Account deactivation request preparation

## Address entity

Support:

* Label such as Home or Office
* Recipient name
* Phone
* Address line 1
* Address line 2
* Area
* City
* Region or state
* Postal code
* Country
* Delivery instructions
* IsDefaultShipping
* IsDefaultBilling
* Latitude and longitude only as optional future fields
* Audit timestamps

## Rules

* A customer can have several addresses.
* Only one default shipping address.
* Only one default billing address.
* Order address snapshots must remain even if an address is later edited.
* Customers cannot access other customers’ addresses.
* Validate country-specific required fields through an extensible design.

## Mobile UI

Create:

* Native-looking account dashboard
* Profile card
* Address cards
* Add-address bottom sheet or full-screen form
* Edit address
* Delete confirmation
* Default badges
* Sticky save button
* Bottom navigation

## Testing

Test:

* Ownership
* Default address enforcement
* Address validation
* Deletion
* Order snapshot independence
* Profile update security

Stop after Phase 16 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 17: Shipping Methods and Delivery Rules.

## Shipping methods

Support:

* Standard delivery
* Express delivery
* Free delivery
* Local pickup preparation
* Cash-on-delivery availability
* Estimated delivery days
* Active or inactive state
* Display order

## Shipping rules

Support:

* Flat rate
* Free above order amount
* Weight-based rate
* Region-based rate
* City-based rate
* Product restriction
* Category restriction
* Maximum package weight
* Delivery blackout preparation

## Architecture

Create an extensible shipping calculation service.

The checkout must not trust shipping costs sent by the browser.

## Administration

Create responsive management for:

* Shipping methods
* Shipping zones
* Shipping rates
* Free-shipping thresholds
* Estimated days
* COD support
* Activation
* Ordering

## Customer UI

Show:

* Available delivery methods
* Delivery price
* Estimated delivery time
* Disabled method reason
* Selected method
* Free-shipping progress where appropriate

Mobile:

* Use selectable app-like cards
* Large radio targets
* Sticky continue button

## Testing

Test:

* Region matching
* Weight rules
* Free-shipping threshold
* Inactive method
* Unsupported address
* Product restrictions
* Calculation consistency

Stop after Phase 17 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 18: Checkout Calculation and Validation Engine.

Do not create final orders yet.

## Objectives

Build a central server-side checkout calculation service.

## Inputs

Accept:

* Cart reference
* Customer or guest information
* Shipping address
* Billing address
* Shipping method
* Coupon code
* Payment method identifier

Do not accept trusted prices or totals from the browser.

## Calculations

Calculate:

* Item unit prices
* Item discounts
* Subtotal
* Coupon discount
* Promotion discount
* Shipping
* Tax
* Grand total
* Amount payable
* Currency
* Warnings

## Validation

Validate:

* Product availability
* Variant availability
* Stock
* Quantity
* Address
* Shipping method
* Coupon
* Payment-method eligibility
* Minimum and maximum order rules
* Price changes
* Cart ownership
* Guest email and phone
* Terms acceptance

## Checkout result

Return:

* Normalized line items
* Product snapshot data
* Variation snapshot data
* Price breakdown
* Shipping breakdown
* Discount breakdown
* Tax breakdown
* Validation errors
* Warnings
* Checkout calculation token or version for safe continuation

## Mobile UI

Build a multi-step app-like checkout:

1. Contact
2. Address
3. Delivery
4. Payment
5. Review

Requirements:

* Progress indicator
* Back navigation
* Sticky Continue button
* Sticky Place Order button on final step
* Summary bottom sheet
* Address cards
* Delivery cards
* Payment cards
* Inline validation
* Safe-area support
* Prevention of accidental duplicate submission

## Guest checkout

Support checkout without creating an account.

Offer account creation after order placement later.

## Testing

Add extensive calculation and validation tests.

Stop after Phase 18 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 19: Order Creation and Stock Reservation.

This phase must be transactional and idempotent.

## Order entities

Create:

* Order
* OrderItem
* OrderAddress
* OrderStatusHistory
* OrderNote
* OrderNumberSequence or safe number generator
* OrderIdempotencyRecord

## Order fields

Include:

* Public order number
* Invoice number placeholder
* Customer ID where logged in
* Guest email
* Guest phone
* Customer name snapshot
* Currency
* Subtotal
* Product discount
* Coupon discount
* Shipping charge
* Tax
* Grand total
* Paid amount
* Refunded amount
* Order status
* Payment status
* Fulfilment status
* CreatedAtUtc
* UpdatedAtUtc
* PaidAtUtc
* ShippedAtUtc
* DeliveredAtUtc
* CancelledAtUtc

## Order item snapshot

Save:

* Product ID where available
* Product variant ID where available
* Product name
* Product slug
* SKU
* Colour name
* Colour value
* Size name
* Image URL
* Unit price
* Compare price
* Discount
* Tax
* Quantity
* Line total

The order must remain readable if the original product is later renamed, deactivated, or removed.

## Order address snapshot

Save complete billing and shipping snapshots.

Do not reference only the customer’s editable address ID.

## Transaction flow

Inside one safe transaction:

1. Validate idempotency key.
2. Recalculate checkout.
3. Verify stock.
4. Create order.
5. Create order items.
6. Create address snapshots.
7. Reserve or deduct stock according to payment method.
8. Record inventory transactions.
9. Record coupon usage reservation.
10. Add status history.
11. Commit.
12. Trigger background notifications after commit.

## Duplicate prevention

Prevent duplicate orders caused by:

* Double click
* Mobile retry
* Browser refresh
* Slow connection
* Network timeout
* Repeated API request

## Stock rules

For cash on delivery, choose and document a clear reservation or deduction policy.

For online payments, reserve stock for a limited period.

Release stock when payment expires or fails.

## Mobile result

Create:

* Order processing state
* Success screen
* Order number
* Summary
* Track order action
* Continue shopping action
* Guest account creation suggestion
* Safe retry behaviour

## Testing

Include:

* Duplicate request
* Concurrent last-item checkout
* Transaction rollback
* Price change
* Stock change
* Invalid coupon
* Address snapshot
* Product snapshot
* Reservation expiration

Stop after Phase 19 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 20: Payment Architecture.

Do not hardcode a single payment gateway.

## Payment abstraction

Create an interface such as:

* IPaymentProvider
* IPaymentProviderFactory
* IPaymentService

Support:

* Cash on delivery
* Online payment provider placeholder
* Mobile financial service provider-ready implementation
* Card provider-ready implementation
* Bank payment-ready implementation

## Payment entities

Create:

* Payment
* PaymentTransaction
* PaymentAttempt
* PaymentWebhookLog
* PaymentRefundRecord

Track:

* Order
* Provider
* Provider transaction ID
* Internal transaction ID
* Amount
* Currency
* Status
* Failure reason
* Request metadata
* Response metadata
* Created, completed, and failed timestamps
* Idempotency key

Never store raw card information.

## Statuses

Support:

* Pending
* Initiated
* Authorised
* Paid
* Failed
* Cancelled
* Expired
* PartiallyRefunded
* Refunded

## Webhooks

Implement secure webhook architecture with:

* Signature verification
* Timestamp verification
* Replay protection
* Idempotency
* Raw payload logging with sensitive-data filtering
* Unknown transaction handling
* Amount and currency verification
* Order-state verification

## Payment workflow

Implement:

* Initiate payment
* Redirect or hosted-checkout preparation
* Callback handling
* Webhook handling
* Payment-status check
* Payment success
* Payment failure
* Stock release
* Order status update

Do not mark an order paid based only on a browser redirect.

## Mobile UI

Create payment method cards with:

* Provider logo
* Name
* Description
* Availability
* Fee when applicable
* Selected state
* Secure-payment notice
* Sticky Pay button

## Testing

Test:

* Duplicate webhook
* Invalid signature
* Wrong amount
* Wrong currency
* Expired payment
* Successful payment
* Failed payment
* Stock release
* Browser callback without webhook
* Concurrent webhook delivery

Stop after Phase 20 and commit with phase details.




Continue the FashionStore project.

Implement only Phase 21: Customer Order Panel.

## Features

Create:

* Order list
* Order search
* Status filter
* Order details
* Order timeline
* Payment status
* Shipping status
* Product items
* Colour and size
* Quantity
* Prices
* Address snapshots
* Payment information
* Delivery information
* Invoice link placeholder
* Cancellation action when allowed
* Return request placeholder
* Buy again
* Contact support action

## Guest order access

Create secure guest order lookup using:

* Public order number
* Verified email, phone, signed token, or secure lookup mechanism

Do not expose orders through predictable IDs alone.

## Mobile UI

Create an app-like order experience:

* Order cards
* Clear status badge
* Product thumbnails
* Horizontal or vertical status timeline
* Sticky support action
* Expandable payment summary
* Native-looking empty state
* Bottom navigation
* Pull-to-refresh-style visual readiness without requiring a SPA

## Cancellation

Allow cancellation only when business rules permit.

Use a service, not controller logic.

Record:

* Who cancelled
* Reason
* Time
* Previous status
* New status
* Inventory release
* Coupon release where applicable

## Tests

Test:

* Ownership
* Guest lookup
* Status timeline
* Cancellation rules
* Inventory release
* Buy-again unavailable variants
* Order snapshot rendering

Stop after Phase 21 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 22: Administration Order Management.

## Order list

Provide:

* Search by order number, customer, email, phone, and transaction
* Date filter
* Order-status filter
* Payment-status filter
* Fulfilment-status filter
* Shipping-method filter
* Payment-method filter
* Amount range
* Pagination
* Sorting
* Export-ready service

## Order details

Show:

* Order number
* Customer
* Guest state
* Contact information
* Billing address
* Shipping address
* Product items
* Product images
* SKU
* Colour
* Size
* Quantity
* Unit price
* Discounts
* Tax
* Totals
* Payment information
* Payment transaction history
* Shipping information
* Status history
* Inventory history
* Internal notes
* Customer-visible notes
* Invoice action
* Refund placeholder
* Return placeholder

## Actions

Support permission-controlled:

* Update order status
* Update fulfilment status
* Add note
* Cancel order
* Mark as processing
* Mark as packed
* Mark as shipped
* Add tracking information
* Mark as delivered
* Resend confirmation
* Open invoice
* Print packing slip
* Print shipping summary

## Rules

Use a central state-transition service.

Do not allow arbitrary invalid status jumps.

Record every transition.

Prevent changing financial states without corresponding payment operations.

## Mobile admin UI

Use:

* Order cards
* Filter bottom sheet
* Sticky order actions
* Collapsible sections
* Colour and size badges
* Timeline
* Confirmation sheets
* No unusable wide tables

## Tests

Test:

* Permissions
* Valid status transitions
* Invalid status transitions
* Cancellation
* Shipment tracking
* Notes
* Audit logs
* Inventory effects

Stop after Phase 22 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 23: Order Invoice and PDF Invoice Generation.

Use QuestPDF or another production-appropriate server-side PDF library approved by the project.

## Invoice entity

Create an Invoice entity containing:

* Invoice number
* Order ID
* Issue date
* Currency
* Subtotal
* Discounts
* Shipping
* Tax
* Total
* Paid amount
* Outstanding amount
* Refunded amount
* Status
* GeneratedAtUtc
* SentAtUtc
* Optional version

## Invoice numbering

Create a safe invoice-number generator.

Requirements:

* Unique
* Concurrency safe
* Configurable prefix
* Year-aware when configured
* Not dependent on browser input
* Not duplicated during retries

## Invoice content

Display:

* Store logo
* Store name
* Store address
* Business registration or tax information
* Invoice number
* Order number
* Invoice date
* Customer name
* Customer email and phone
* Billing address
* Shipping address
* Payment method
* Payment status
* Delivery method
* Product name
* Product image optionally
* SKU
* Colour
* Size
* Unit price
* Quantity
* Discount
* Tax
* Line total
* Subtotal
* Coupon discount
* Product discount
* Shipping charge
* Tax total
* Grand total
* Paid amount
* Outstanding amount
* Refunded amount
* Notes
* Return or refund references where applicable

## Critical snapshot requirement

Invoice data must come from order snapshots.

Do not load current product names, prices, colours, sizes, or SKUs as the historical invoice source.

## Admin features

Allow authorised administrators to:

* Open invoice
* Print HTML invoice
* Download PDF
* Email PDF
* Regenerate an identical invoice
* See email-send history

## Customer features

Allow customers to:

* Open their invoice
* Download PDF
* Print invoice

Verify order ownership.

## Mobile UI

The HTML invoice view should:

* Fit small screens
* Use a card-based item layout on mobile
* Preserve a professional print layout
* Provide sticky Download and Print actions
* Display colour and size prominently

## PDF requirements

* A4
* Page numbers
* Repeating header when needed
* Clear totals
* Avoid item-row splitting where possible
* Support long product names
* Support several pages
* Correct currency formatting
* Correct date formatting
* Company branding
* Deterministic output

## Tests

Test:

* Snapshot consistency after product edit
* Multi-page invoice
* Colour and size display
* Unique invoice number
* Ownership
* Permissions
* Totals
* Partial refund display
* PDF generation

Stop after Phase 23 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 24: Returns, Exchanges and Refunds.

## Entities

Create:

* ReturnRequest
* ReturnItem
* ReturnStatusHistory
* ExchangeRequest
* Refund
* RefundTransaction
* ReturnReason
* ReturnAttachment

## Return workflow

Support:

* Customer return request
* Item selection
* Quantity selection
* Reason
* Notes
* Photo attachment
* Approval
* Rejection
* Return shipping state
* Received state
* Inspection
* Refund decision
* Exchange decision
* Completion

## Return statuses

Include:

* Requested
* UnderReview
* Approved
* Rejected
* AwaitingShipment
* InTransit
* Received
* Inspected
* RefundPending
* Refunded
* Exchanged
* Closed

## Rules

* Enforce return window.
* Prevent returning more than purchased.
* Prevent duplicate completed returns.
* Support partial returns.
* Support product-level return restrictions.
* Record all decisions.
* Restore inventory only according to inspection outcome.
* Distinguish sellable and damaged returned stock.
* Do not change original invoice history incorrectly.
* Generate adjustment or credit-note-ready records.

## Refunds

Support:

* Full refund
* Partial refund
* Item refund
* Shipping refund when allowed
* Manual refund
* Gateway refund-ready interface
* Refund idempotency

## Mobile customer UI

Use:

* Step-by-step return flow
* Product cards
* Quantity selector
* Reason cards
* Photo upload
* Status timeline
* Sticky submit action

## Mobile admin UI

Use:

* Return cards
* Inspection form
* Image preview
* Approval and rejection sheets
* Refund summary
* Inventory-restock options

## Testing

Test:

* Return window
* Quantity
* Partial return
* Duplicate return
* Refund calculation
* Inventory restoration
* Damaged stock
* Permissions
* Ownership
* Status transitions

Stop after Phase 24 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 25: Product Reviews and Ratings.

## Review model

Support:

* Product
* Customer
* Order item
* Rating from 1 to 5
* Title
* Review
* Images
* Verified purchase
* Approval status
* Helpful count
* Created and updated timestamps

## Rules

* Only authenticated customers may submit.
* Mark verified purchase only after checking delivered orders.
* Limit duplicate reviews according to a documented rule.
* Prevent review ownership violations.
* Moderate unsafe or spam content.
* Do not allow raw unsafe HTML.
* Recalculate product ratings safely.

## Administration

Support:

* Pending review list
* Approve
* Reject
* Hide
* Delete where policy permits
* View product and customer
* Filter by rating
* Filter by verified purchase
* Moderation notes

## Customer UI

Create:

* Rating summary
* Rating distribution
* Review list
* Sort and filter
* Write-review action
* Mobile full-screen review form
* Photo upload
* Helpful action
* Verified badge

## Testing

Test:

* Verified purchase
* Duplicate review
* Ownership
* Rating aggregation
* Moderation
* Unsafe content
* Inactive product

Stop after Phase 25 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 26: Email Notifications and Background Jobs.

## Email architecture

Create:

* IEmailSender
* Email template renderer
* Email queue
* Email log
* Retry strategy
* Development email provider
* SMTP-ready provider
* Future API-provider abstraction
* optiton for adding gmail,outlook, hotmail etc provider add option
* option for custom provider add option like own domain mail with receive and send emal from application with host, port ect what need 

## Email templates

Create responsive templates for:

* Email confirmation
* Password reset
* Welcome
* Order placed
* Payment received
* Payment failed
* Order processing
* Order shipped
* Order delivered
* Order cancelled
* Invoice
* Return requested
* Return approved
* Return rejected
* Refund completed
* Low-stock administrator alert

## Background jobs

Configure Hangfire for:

* Sending queued emails
* Releasing expired stock reservations
* Expiring unpaid orders
* Processing scheduled promotions
* Low-stock alerts
* Cleaning temporary uploads
* Generating heavy invoice documents where appropriate
* Retry of transient failures

## Reliability

Requirements:

* Do not send important email before the database transaction commits.
* Prevent duplicate emails.
* Store email status.
* Store attempt count.
* Store sanitized error information.
* Use retry with limits.
* Support cancellation.
* Avoid logging secrets.

## Administration

Create:

* Email log page
* Search and filters
* Status
* Attempts
* Resend action
* Template preview
* Background-job dashboard restricted to authorised administrators

## Testing

Test:

* Duplicate prevention
* Retry
* Failed sending
* Template rendering
* Transaction timing
* Authorization
* Reservation-release job

Stop after Phase 26 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 27: Content Management and Website Settings.

## Content entities

Create:

* Page
* Banner
* HomepageSection
* NavigationMenu
* NavigationItem
* FAQ
* BlogPost preparation
* PolicyDocument

## Pages

Support:

* About
* Contact
* Delivery policy
* Return policy
* Privacy policy
* Terms
* Size guide
* Custom pages

## Website settings

Create strongly typed settings for:

* Store name
* Store logo
* Favicon
* Contact email
* Contact phone
* Address
* Business registration information
* Currency
* Timezone
* Accent colour
* Social links
* Checkout settings
* Order settings
* Return window
* Invoice prefix
* Low-stock defaults
* SEO defaults
* Maintenance mode
* Guest checkout
* Review settings

## Administration

Create mobile-friendly management for:

* Pages
* Banners
* Homepage sections
* Navigation
* FAQs
* Store settings

## Safety

* Sanitize rich content.
* Validate URLs.
* Restrict file uploads.
* Audit settings changes.
* Protect critical settings with permissions.
* Cache settings safely.
* Invalidate cache after updates.

## Mobile UI

The content-management forms must use:

* Stacked sections
* Preview cards
* Sticky save action
* Image previews
* Reordering controls
* Clear publish state

## Testing

Test:

* Settings cache
* Sanitization
* Navigation hierarchy
* Scheduling
* Permissions
* Maintenance mode

Stop after Phase 27 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 28: Administration Dashboard and Reports.

## Dashboard metrics

Show:

* Sales today
* Sales this month
* Orders today
* Pending orders
* Paid orders
* Failed payments
* Low-stock variants
* Out-of-stock variants
* Pending returns
* Pending refunds
* New customers
* Average order value
* Top-selling products
* Top categories
* Top brands
* Recent orders
* Sales trend

## Reports

Create:

* Sales report
* Order report
* Product sales report
* Variation sales report
* Category report
* Brand report
* Customer report
* Coupon usage report
* Inventory report
* Return report
* Refund report
* Payment report

## Filters

Support:

* Date range
* Status
* Category
* Brand
* Product
* Payment method
* Shipping method
* Customer
* Currency where applicable

## Performance

Use:

* Aggregated queries
* Projection
* Appropriate indexes
* Caching
* Date limits
* Pagination
* Background export preparation

Do not load all orders into memory to calculate reports.

## Mobile admin UI

Create:

* Swipe-friendly metric cards
* Responsive single-column charts
* Compact report cards
* Full-screen filters
* Mobile date range selector
* Export action
* No unreadable desktop charts

## Accuracy

Document whether cancelled, failed, refunded, or partially refunded orders are included in every metric.

## Testing

Test:

* Date boundaries
* Refund adjustments
* Cancelled-order exclusion
* Timezone conversion
* Aggregation
* Permission checks
* Large-data query behaviour

Stop after Phase 28 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 29: SEO, Accessibility and Performance Optimisation.

## SEO

Implement:

* Dynamic page titles
* Meta descriptions
* Canonical URLs
* Open Graph metadata
* Social preview metadata
* Product structured data
* Breadcrumb structured data
* Organisation structured data
* Website search structured data
* XML sitemap
* Robots.txt
* SEO-friendly slugs
* Pagination metadata
* No-index rules for cart, checkout, account, and admin
* Redirect handling for changed slugs

## Accessibility

Review and improve:

* Keyboard navigation
* Focus order
* Focus visibility
* Form labels
* Validation announcements
* Modal focus trapping
* Drawer accessibility
* Colour contrast
* Screen-reader labels
* Alternative text
* Heading hierarchy
* Touch target size
* Reduced motion
* Status announcements
* Error summaries

Target WCAG 2.2 AA where practical.

## Performance

Implement:

* Response compression
* Static asset caching
* CSS and JavaScript minification
* Responsive images
* Lazy loading
* Preloading critical assets
* Font optimisation
* Database indexes
* Query projection
* Pagination
* Caching
* Output caching where safe
* Cache invalidation
* Reduced layout shifts
* Explicit image dimensions
* Avoidance of N+1 queries

## Mobile performance

Optimise for:

* Slow mobile networks
* Mid-range Android devices
* Small screens
* Touch input
* Minimal initial JavaScript
* Fast first render
* Stable bottom navigation
* Low layout shift

## Testing

Provide:

* Lighthouse checklist
* Accessibility checklist
* SEO checklist
* Performance checklist
* Broken-link verification
* Structured-data verification

Stop after Phase 29 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 30: Security Hardening.

Perform a complete security review.

## Review areas

Check:

* Authentication
* Authorization
* Permissions
* Customer ownership validation
* Admin route protection
* CSRF
* XSS
* SQL injection
* File uploads
* Open redirects
* Mass assignment
* Insecure direct object references
* Session security
* Cookie security
* Password reset
* Email confirmation
* Login rate limiting
* Account lockout
* Payment webhook security
* Replay attacks
* Idempotency
* Sensitive logging
* Error exposure
* HTTP headers
* Content Security Policy
* HTTPS
* Secret storage
* Database credentials
* Backup security
* Hangfire dashboard security
* Swagger production exposure

## Security headers

Configure as appropriate:

* Content-Security-Policy
* X-Content-Type-Options
* Referrer-Policy
* Permissions-Policy
* Frame restrictions
* Strict-Transport-Security

## Audit logging

Ensure audit records for:

* Admin login
* Permission changes
* Product deletion or deactivation
* Price changes
* Stock changes
* Order status changes
* Refunds
* Settings changes
* Customer account suspension

## Data protection

Use ASP.NET Core Data Protection correctly.

Plan key persistence for production.

## Deliverables

Provide:

* Identified risks
* Severity
* Fixed issues
* Remaining accepted risks
* Security testing checklist
* Production security configuration
* Completion status

Stop after Phase 30 and commit with phase details.



Continue the FashionStore project.

Implement only Phase 31: Automated Tests and Quality Verification.

## Unit tests

Ensure tests for:

* Product validation
* Variation combinations
* Price calculation
* Discounts
* Coupons
* Shipping
* Cart
* Checkout
* Inventory
* Stock reservations
* Order creation
* Order status transitions
* Payment processing
* Payment webhook idempotency
* Cancellation
* Returns
* Refunds
* Invoice totals
* Invoice snapshot consistency
* Permission rules

## Integration tests

Create tests using an appropriate test database approach.

Cover:

* Identity
* Registration
* Login
* Admin authorization
* Product CRUD
* Variation CRUD
* Inventory adjustment
* Cart
* Checkout
* Order creation
* Concurrent stock purchase
* Payment webhook
* Invoice access
* Customer order ownership
* Return request

## UI-level verification

Provide Playwright test preparation for:

* Mobile navigation
* Product search
* Filters
* Variation selection
* Add to cart
* Cart update
* Checkout
* Login
* Customer order view
* Admin product creation
* Admin invoice view

Use representative mobile and desktop viewport sizes.

## Quality checks

Run:

* Build
* Unit tests
* Integration tests
* Static analysis
* Formatting
* Migration verification
* Dependency vulnerability review
* Nullability review
* Logging review

Do not ignore failing tests.

Provide:

* Test project structure
* Commands
* Test coverage summary
* Known gaps
* Completion status

Stop after Phase 31 and commit with phase details.


Continue the FashionStore project.

Implement only Phase 32: Deployment and Production Readiness.

## Deployment targets

Prepare for:

* IIS deployment
* Docker deployment
* Reverse-proxy hosting
* Cloud hosting

Do not require all targets to be deployed simultaneously, but document each.

## Production configuration

Prepare:

* Production appsettings structure
* Environment variables
* Secret management
* SQL Server connection
* Data Protection key storage
* HTTPS
* Forwarded headers
* Logging
* Health checks
* Background jobs
* Email
* File storage
* Payment configuration
* Invoice configuration

## Database deployment

Provide a safe migration strategy.

Include:

* Backup before migration
* Migration bundle or controlled migration process
* Rollback planning
* Seed-data rules
* Administrator creation
* No destructive automatic production reset

## Docker

Create:

* Production Dockerfile
* Multi-stage build
* Tailwind build step
* Non-root runtime where practical
* Health check
* Environment configuration
* Persistent storage guidance
* Docker Compose for application and SQL Server development only where useful

## IIS

Document:

* Hosting bundle
* Application pool
* File permissions
* Environment variables
* Logs
* HTTPS
* Static files
* Data Protection
* App restart behaviour

## Operations

Prepare:

* Database backups
* File backups
* Restore procedure
* Health monitoring
* Error alerting
* Disk monitoring
* Email failure monitoring
* Payment webhook monitoring
* Hangfire monitoring
* Log retention
* Audit-log retention
* Security update process

## Final verification

Perform a complete system review:

* Customer registration
* Product browsing
* Variation selection
* Cart
* Coupon
* Checkout
* Payment
* Order
* Inventory
* Customer panel
* Admin panel
* Invoice
* PDF
* Return
* Refund
* Mobile experience
* Desktop experience
* Security
* Performance
* Accessibility
* SEO
* Backups

Provide:

1. Final production checklist
2. Deployment commands
3. Required environment variables
4. Rollback steps
5. Smoke-test checklist
6. Known limitations
7. Final completion status

Stop after Phase 32 and commit with phase details.



