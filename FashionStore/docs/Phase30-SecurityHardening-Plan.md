# Phase 30 - Security Hardening (complete)

Status snapshot for the Phase 30 security review. All four review areas
(authentication/account, payment/checkout/orders/webhooks, the Web request
pipeline, and admin services / audit / data protection / secrets) have been
surveyed. Every confirmed finding has been fixed and the fixes verified by the
build and the full test suite. The formal deliverables report lives at
`docs/Phase30-SecurityHardening.md`.

## Survey coverage

1. **Authentication / account / user services** (AuthService, AccountController,
   AddressesController, Identity configuration, claims factory, password
   reset / email confirmation / lockout / rate limiting).
2. **Payment / checkout / orders / webhooks** (PaymentController,
   CheckoutController, OrderController, ReturnsController, PaymentService,
   OrderPlacementService, CustomerOrderService, CustomerReturnService,
   AdminReturnService, CheckoutCalculationService, DiscountService).
3. **Web request pipeline** (Program.cs middleware order, HSTS/HTTPS, security
   headers, exception/error exposure, ForwardedHeaders, Data Protection key
   persistence, cookie policy).
4. **Admin services / audit / data protection / secrets** (all Admin controllers,
   AuditService, seeding endpoints, Hangfire dashboard, Swagger gating,
   hardcoded secrets in checked-in configuration).

## Confirmed findings (fixed)

### High severity

| # | Finding | Location | Fix |
|---|---------|----------|-----|
| H1 | `POST /payments/mock-hosted-checkout/process` is anonymous, has no anti-forgery token, and itself computes a valid HMAC from the configured WebhookSecret, delivering a signed `payment.succeeded` webhook that settles any order as Paid | `src/FashionStore.Web/Controllers/PaymentController.cs:93-139` | Development-only, anti-forgery token added, ownership verified before signed webhook delivery |
| H2 | Webhook + guest-token secrets hardcoded in source-controlled base config (`WebhookSecret` for card/mfs/bank, `GuestAccessTokenSecret`) - anyone who reads the repo can forge webhooks / guest tickets | `src/FashionStore.Web/appsettings.json:80,94,101,155` | Placeholder secrets stripped from base config; fail-fast startup guard in non-Development when secrets are missing or dev-prefixed |
| H3 | `GET /checkout/confirmation/{publicOrderNumber}` is open to anonymous callers for any guessable sequential order number, exposing PII (name, address, email, items) | `src/FashionStore.Web/Controllers/CheckoutController.cs:254-277`; `src/FashionStore.Web/Views/Checkout/Confirmation.cshtml` | Guest order ticket gating added (covered by OrderTests guest-ticket tests) |
| H4 | `POST /api/Admin/seed-superadmin` is `[AllowAnonymous]` and creates a SuperAdmin with an arbitrary email/password, with no environment guard - unauthenticated privilege escalation in Production | `src/FashionStore.Web/Controllers/Admin/AdminController.cs:67-122` (also `seed-roles`, `seed-content` at :35-65) | `seed-roles`/`seed-content`/`seed-superadmin` return 404 outside Development |

### Medium severity

| # | Finding | Location | Fix |
|---|---------|----------|-----|
| M1 | `GET /checkout/confirmation/{orderNumber}/payment-status` has no ownership check - leaks payment state/tx id for any order number | `CheckoutController.cs:334-344` | Guest ticket verification added |
| M2 | Shipping refunds can be issued repeatedly, over-refunding up to the full captured payment | `src/FashionStore.Infrastructure/Services/AdminReturnService.cs:495-524,1010-1030` | Shipping refund capped by refundable amount + one shipping charge (no repeated shipping refunds) |
| M3 | Register POST and ResetPassword POST are unthrottled; `global` rate-limit policy is dead configuration | `AccountController.cs:103-106,210-213`; `Program.cs:100-130` | `register` and `passwordreset` rate-limit policies applied; global policy now effective via GlobalLimiter |
| M4 | Unconfirmed-email login is counted as a failed attempt, allowing lockout DoS on legitimate unconfirmed accounts | `AuthService.cs:130-133` (missing `IsNotAllowed` handling) | `IsNotAllowed` handled - unconfirmed email no longer counts toward lockout |
| M5 | Login responses disclose account state (deactivated/locked) and unknown-user branch has no timing delay - enumeration | `AuthService.cs:89-104` | Uniform login failure message + timing delay |
| M6 | `IsActive` checked only at login; suspension does not invalidate sessions (security stamp never rotated); `isActive` claim unused by any policy | `AuthService.cs:93-98`; `ApplicationClaimsPrincipalFactory.cs:24` | Security stamp rotated on suspension and after ConfirmEmailAsync; `OnValidatePrincipal` rejects sessions when `isActive` claim is false; stamp validator interval 1 min |
| M7 | Guest payment initiation (`POST /checkout/pay`) requires no order ticket/email for anonymous callers | `CheckoutController.cs:294-305` | Guest ticket gating added |

### Low severity

| # | Finding | Location | Fix |
|---|---------|----------|-----|
| L1 | `TokenExpiryHours` config knob is never wired to the Identity token provider (dead configuration) | `SecuritySettings.cs:13`; `DependencyInjection.cs:109` | Wired into `DataProtectionTokenProviderOptions.TokenLifespan` |
| L2 | `Email:BaseUrl` not set in appsettings - reset/confirmation links default to `https://localhost:5001` | `EmailSettings.cs:23`; `EmailNotificationService.cs:39` | Development override present; startup guard requires a production value outside Development |
| L3 | Duplicate-email registration error surfaced verbatim - email enumeration | `AuthService.cs:78-79` | Generic duplicate-email error returned |
| L4 | Login rate-limit queue (QueueLimit=10) weakens the 5/min window; `RetryAfter` hardcoded to 60s even for the 5-min passwordreset window | `Program.cs:108-127` | `QueueLimit=0` everywhere; `RetryAfter` taken from the actual limiter window |
| L5 | Identity cookie `SecurePolicy = SameAsRequest` | `DependencyInjection.cs:116` | `SecurePolicy = Always` |
| L6 | Email-confirmation token not invalidated after first use (replayable within 1-day window; benign) | `AuthService.cs:143-150` | Security stamp rotated after `ConfirmEmailAsync` |
| L7 | Refund idempotency key defaults to a random GUID - dedupe ineffective unless admin client supplies a key | `AdminReturnService.cs:1078-1086` | Deterministic idempotency default key |
| L8 | Guest coupon per-customer limit keyed by email; non-atomic usage count across app instances | `OrderPlacementService.cs:458`; `DiscountService.cs:33-37` | SQL Server `sp_getapplock` makes coupon usage count atomic across app instances (guest email keying already present) |

## Request pipeline findings (fixed in this batch)

| # | Finding | Fix |
|---|---------|-----|
| P1 | No Content-Security-Policy or security headers on responses | `SecurityHeadersMiddleware` adds CSP, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, `X-Frame-Options` and (outside Development, over HTTPS) HSTS |
| P2 | Data Protection keys never persisted - cookies/tokens invalidated on every restart | `DataProtectionSettings` (`KeysDirectory`) wired to `PersistKeysToFileSystem`; see `docs/Phase30-SecurityHardening.md` for the production key-persistence plan |

## Audit logging coverage

| Action class | Where recorded |
|--------------|----------------|
| Admin login | `User.Login` in `AuthService` |
| Permission / role changes | `Role.Seeding`, `User.SuperAdminCreated` in `AdminController` seeding endpoints |
| Product delete / deactivate | `ProductsController` |
| Price changes | `ProductsController` |
| Stock changes | `InventoryController` |
| Order status / fulfilment changes | `OrderAdministrationService` |
| Refunds | `AdminReturnService` |
| Settings changes | `WebsiteSettingsService` (`Settings.Update`) via `IAuditService` |
| Account suspension / reactivation | `AdminController` |

## Plan to complete Phase 30

1. Build the solution and run the full test suite (warnings-as-errors). **done**
2. Add security headers middleware (CSP, X-Content-Type-Options, Referrer-Policy,
   Permissions-Policy, X-Frame-Options, HSTS). **done**
3. Add audit logging for the required action classes (roles/permissions,
   settings). **done**
4. Configure Data Protection key persistence for production. **done**
5. Write `docs/Phase30-SecurityHardening.md` deliverables report. **done**
6. Re-run the full test suite, verify no EF migration is needed, then commit. **done**
