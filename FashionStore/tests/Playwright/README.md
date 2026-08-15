# FashionStore Playwright UI-level verification

Phase 31 prepares Playwright-driven UI tests that exercise the storefront and
admin panel through a real browser on representative desktop and mobile
viewport sizes.

These specs are **preparation** for the UI-level verification checklist in the
Phase 31 prompt:

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

## Prerequisites

* Node.js 18+ (tested with Node 22)
* A running FashionStore instance backed by a real database, seeded with the
  sample catalogue, on `http://localhost:5000` (or override with
  `FASHIONSTORE_BASE_URL`).

The storefront is a server-rendered Razor application, so the specs navigate
real URLs and assert against the `data-*` attribute selectors and text that the
server-rendered views expose. They cannot run against the in-memory integration
test host, which is why they are prepared as a separate project here rather
than part of `FashionStore.IntegrationTests`.

## Install

```bash
cd tests/Playwright
npm install
npx playwright install chromium
```

## Run

The application must be running and seeded first:

```bash
# from FashionStore/src/FashionStore.Web
export ASPNETCORE_ENVIRONMENT=Development
dotnet run
```

Then, from `tests/Playwright`:

```bash
# default: run both desktop (1280x800) and mobile (390x844) projects
npm test

# run only the desktop or mobile project
npm run test:desktop
npm run test:mobile

# headed browser for debugging
npm run test:headed

# open the interactive HTML report after a run
npm run report
```

Admin specs authenticate with `FASHIONSTORE_ADMIN_EMAIL` /
`FASHIONSTORE_ADMIN_PASSWORD` (defaults `admin@fashionstore.local` /
`Admin123!`). Override them to match the administrator seeded into your local
database.

## Viewports

* `chromium-desktop`: 1280x800 (`Desktop Chrome`)
* `chromium-mobile`: 390x844 (`Pixel 7`, touch enabled)

## Structure

* `e2e/customer.spec.ts` - storefront journeys (navigation, search, filters,
  variation selection, add to cart, cart update, guest checkout, login,
  customer order view)
* `e2e/admin.spec.ts` - admin journeys (authorization guard, dashboard,
  product listing / add-product entry point, invoice view)
* `playwright.config.ts` - projects, viewports, base URL, reporters
