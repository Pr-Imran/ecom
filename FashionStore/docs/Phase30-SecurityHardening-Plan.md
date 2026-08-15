# Phase 30 - Security Hardening (work-in-progress)

Status snapshot for the Phase 30 security review. This document records the
survey completed so far, the confirmed vulnerabilities, and the plan to
complete the phase. It is a living plan: as fixes are implemented they move
from "pending" to "done".

## What has been done

A security survey of two of the four planned review areas completed:

1. **Authentication / account / user services** (AuthService, AccountController,
   AddressesController, Identity configuration, claims factory, password
   reset / email confirmation / lockout / rate limiting).
2. **Payment / checkout / orders / webhooks** (PaymentController,
   CheckoutController, OrderController, ReturnsController, PaymentService,
   OrderPlacementService, CustomerOrderService, CustomerReturnService,
   AdminReturnService, CheckoutCalculationService, DiscountService).

Areas still to be surveyed: the Web request pipeline (Program.cs middleware,
headers, CSRF, XSS, open redirects, file uploads, mass assignment, session,
error exposure, Swagger, Hangfire) and admin services / audit / data
protection / secrets. These are covered by the plan below.

## Confirmed findings (from completed surveys)

### High severity

| # | Finding | Location |
|---|---------|----------|
| H1 | `POST /payments/mock-hosted-checkout/process` is anonymous, has no anti-forgery token, and itself computes a valid HMAC from the configured WebhookSecret, delivering a signed `payment.succeeded` webhook that settles any order as Paid | `src/FashionStore.Web/Controllers/PaymentController.cs:93-139` |
| H2 | Webhook + guest-token secrets hardcoded in source-controlled base config (`WebhookSecret` for card/mfs/bank, `GuestAccessTokenSecret`) - anyone who reads the repo can forge webhooks / guest tickets | `src/FashionStore.Web/appsettings.json:80,94,101,155` |
| H3 | `GET /checkout/confirmation/{publicOrderNumber}` is open to anonymous callers for any guessable sequential order number, exposing PII (name, address, email, items) | `src/FashionStore.Web/Controllers/CheckoutController.cs:254-277`; `src/FashionStore.Web/Views/Checkout/Confirmation.cshtml` |
| H4 | `POST /api/Admin/seed-superadmin` is `[AllowAnonymous]` and creates a SuperAdmin with an arbitrary email/password, with no environment guard - unauthenticated privilege escalation in Production | `src/FashionStore.Web/Controllers/Admin/AdminController.cs:67-122` (also `seed-roles`, `seed-content` at :35-65) |

### Medium severity

| # | Finding | Location |
|---|---------|----------|
| M1 | `GET /checkout/confirmation/{orderNumber}/payment-status` has no ownership check - leaks payment state/tx id for any order number | `CheckoutController.cs:334-344` |
| M2 | Shipping refunds can be issued repeatedly, over-refunding up to the full captured payment | `src/FashionStore.Infrastructure/Services/AdminReturnService.cs:495-524,1010-1030` |
| M3 | Register POST and ResetPassword POST are unthrottled; `global` rate-limit policy is dead configuration | `AccountController.cs:103-106,210-213`; `Program.cs:100-130` |
| M4 | Unconfirmed-email login is counted as a failed attempt, allowing lockout DoS on legitimate unconfirmed accounts | `AuthService.cs:130-133` (missing `IsNotAllowed` handling) |
| M5 | Login responses disclose account state (deactivated/locked) and unknown-user branch has no timing delay - enumeration | `AuthService.cs:89-104` |
| M6 | `IsActive` checked only at login; suspension does not invalidate sessions (security stamp never rotated); `isActive` claim unused by any policy | `AuthService.cs:93-98`; `ApplicationClaimsPrincipalFactory.cs:24` |
| M7 | Guest payment initiation (`POST /checkout/pay`) requires no order ticket/email for anonymous callers | `CheckoutController.cs:294-305` |

### Low severity

| # | Finding | Location |
|---|---------|----------|
| L1 | `TokenExpiryHours` config knob is never wired to the Identity token provider (dead configuration) | `SecuritySettings.cs:13`; `DependencyInjection.cs:109` |
| L2 | `Email:BaseUrl` not set in appsettings - reset/confirmation links default to `https://localhost:5001` | `EmailSettings.cs:23`; `EmailNotificationService.cs:39` |
| L3 | Duplicate-email registration error surfaced verbatim - email enumeration | `AuthService.cs:78-79` |
| L4 | Login rate-limit queue (QueueLimit=10) weakens the 5/min window; `RetryAfter` hardcoded to 60s even for the 5-min passwordreset window | `Program.cs:108-127` |
| L5 | Identity cookie `SecurePolicy = SameAsRequest` | `DependencyInjection.cs:116` |
| L6 | Email-confirmation token not invalidated after first use (replayable within 1-day window; benign) | `AuthService.cs:143-150` |
| L7 | Refund idempotency key defaults to a random GUID - dedupe ineffective unless admin client supplies a key | `AdminReturnService.cs:1078-1086` |
| L8 | Guest coupon per-customer limit keyed by email; non-atomic usage count across app instances | `OrderPlacementService.cs:458`; `DiscountService.cs:33-37` |

## Not yet surveyed (to be completed before fixing)

- **Web request pipeline**: middleware order, HSTS/HTTPS, security headers
  (CSP, X-Content-Type-Options, Referrer-Policy, Permissions-Policy,
  X-Frame-Options), exception/error exposure, ForwardedHeaders, Data
  Protection key persistence, cookie policy.
- **CSRF**: audit every POST action for `[ValidateAntiForgeryToken]`.
- **XSS**: audit `Html.Raw(` usages on user/DB content in views.
- **SQL injection**: confirm all queries use parameterised LINQ/EF.
- **File uploads**: validate extension/content-type/size; path traversal.
- **Open redirects**: confirm `Url.IsLocalUrl` on all `returnUrl` handling.
- **Mass assignment**: list POST actions binding entities directly.
- **Session security**: session cookie options, `UseSession`.
- **Admin authorization + audit**: verify all admin controllers use
  `[Authorize]` policies; identify missing audit logging for admin login,
  permission changes, product deletion/deactivation, price changes, stock
  changes, order status changes, refunds, settings changes, account suspension.
- **Secrets / DB credentials / backup security**: scan appsettings for
  hardcoded secrets, connection strings in source control, backup flows.
- **Hangfire dashboard** authorization filter.
- **Swagger** production exposure guard.

## Plan to complete Phase 30

1. **Complete the remaining survey** (pipeline, admin/audit, secrets) and fold
   results into this document.
2. **Fix High severity**: secure/disable the mock hosted-checkout processor
   (H1), strip placeholder secrets from appsettings.json and require
   environment overrides with a startup validation guard (H2), add guest
   email/ticket verification to confirmation + payment-status + pay endpoints
   (H3, M1, M7), and gate admin seeding endpoints behind an environment check
   (H4).
3. **Fix Medium severity**: cap repeated shipping refunds (M2), add rate
   limiting to Register/ResetPassword and make `global` policy effective (M3),
   fix unconfirmed-email lockout DoS (M4), uniform login messages + timing
   delay (M5), rotate security stamp on suspension + enforce via security
   stamp validator (M6).
4. **Fix Low severity**: wire TokenExpiryHours (L1), require Email:BaseUrl
   (L2), generic duplicate-email message (L3), tighten rate-limit queue +
   RetryAfter (L4), SecurePolicy=Always (L5), confirm-email token rotation
   (L6), refund idempotency default key (L7), coupon usage constraints (L8).
5. **Add security headers middleware**: CSP, X-Content-Type-Options,
   Referrer-Policy, Permissions-Policy, X-Frame-Options, HSTS.
6. **Add audit logging** for the required action classes (admin login,
   permission changes, product delete/deactivate, price changes, stock
   changes, order status changes, refunds, settings changes, account
   suspension).
7. **Data Protection**: configure key persistence for production (e.g. a
   persisted key ring, not per-process).
8. **Deliverables**: documented identified risks + severity, fixed issues,
   remaining accepted risks, security testing checklist, production security
   configuration, completion status. Document at
   `docs/Phase30-SecurityHardening.md`.
9. Build the solution, run the full test suite, add security regression
   tests, then commit with phase details.
