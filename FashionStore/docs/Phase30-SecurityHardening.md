# Phase 30 - Security Hardening Deliverables

Phase 30 performed a complete security review of the FashionStore application
across authentication, authorization, permissions, ownership validation, admin
route protection, CSRF, XSS, SQL injection, file uploads, open redirects, mass
assignment, session/cookie security, password reset, email confirmation, login
rate limiting, account lockout, payment webhook security, replay attacks,
idempotency, sensitive logging, error exposure, HTTP headers, CSP, HTTPS, secret
storage, database credentials, backup security, Hangfire dashboard security and
Swagger production exposure.

This document records the identified risks and their severity, the fixes applied,
the remaining accepted risks, the security testing checklist, the production
security configuration, and the completion status.

## Identified risks

All findings from the review are tabulated with severity in
`docs/Phase30-SecurityHardening-Plan.md`. Summary:

| Severity | Count | Fixed |
|----------|-------|-------|
| High | 4 | 4 |
| Medium | 7 | 7 |
| Low | 8 | 8 |
| Pipeline / Data Protection | 2 | 2 |

### High severity

| Finding | Location |
|---------|----------|
| Anonymous, token-less mock payment processor that settles any order via a self-computed signed webhook | `PaymentController.cs:93-139` |
| Webhook + guest-token secrets hardcoded in source-controlled base config | `appsettings.json` |
| Anonymous order-confirmation page exposing PII for guessable order numbers | `CheckoutController.cs:254-277` |
| `[AllowAnonymous]` `seed-superadmin` privilege escalation in Production | `AdminController.cs:67-122` |

### Medium severity

| Finding | Location |
|---------|----------|
| Payment-status endpoint leaks payment state for any order number | `CheckoutController.cs:334-344` |
| Repeated shipping refunds over-refund up to the captured amount | `AdminReturnService.cs:495-524,1010-1030` |
| Register/ResetPassword unthrottled; `global` rate-limit policy dead | `AccountController.cs`; `Program.cs:100-130` |
| Unconfirmed-email login counted as failed attempt (lockout DoS) | `AuthService.cs:130-133` |
| Login responses disclose account state; no timing delay on unknown user | `AuthService.cs:89-104` |
| Suspension does not invalidate sessions; `isActive` claim unused | `AuthService.cs:93-98`; `ApplicationClaimsPrincipalFactory.cs:24` |
| Guest payment initiation requires no order ticket for anonymous callers | `CheckoutController.cs:294-305` |

### Low severity

| Finding | Location |
|---------|----------|
| `TokenExpiryHours` never wired to Identity token provider | `SecuritySettings.cs:13` |
| `Email:BaseUrl` unset - links default to localhost | `EmailSettings.cs:23` |
| Duplicate-email error surfaced verbatim (enumeration) | `AuthService.cs:78-79` |
| Login rate-limit queue weakens window; `RetryAfter` hardcoded | `Program.cs:108-127` |
| Identity cookie `SecurePolicy = SameAsRequest` | `DependencyInjection.cs:116` |
| Email-confirmation token replayable after first use | `AuthService.cs:143-150` |
| Refund idempotency key defaulted to random GUID | `AdminReturnService.cs:1078-1086` |
| Guest coupon usage count non-atomic across app instances | `OrderPlacementService.cs:458` |

### Request pipeline / Data Protection

| Finding | Location |
|---------|----------|
| No CSP or security headers on responses | `Program.cs` middleware |
| Data Protection keys never persisted (restart invalidates cookies/tokens) | Infrastructure DI |

## Fixed issues

### Authentication and account hardening

- **Uniform login failure**: all failure branches (unknown user, inactive,
  locked-out, wrong password, unconfirmed email) return the same generic message
  and the unknown-user branch applies the same randomized timing delay as the
  password branch, removing account enumeration and timing side channels.
- **No lockout DoS for unconfirmed email**: an unconfirmed-email login is no
  longer counted as a failed password attempt, so a user who has not confirmed
  their email cannot be driven into lockout by an attacker.
- **Session invalidation on suspension**: suspending or reactivating an account
  rotates the identity security stamp; a cookie validator
  (`OnValidatePrincipal`) rejects any session whose `isActive` claim is not
  `true`, and the security-stamp validation interval is reduced to 1 minute so a
  rotated stamp invalidates existing sessions promptly.
- **Generic duplicate-email registration** error removes email enumeration on the
  register endpoint.
- **`TokenExpiryHours` wired** into the Identity `DataProtectionTokenProvider`,
  so the configured password-reset / email-confirmation token lifetime is
  actually enforced.
- **Identity cookie `SecurePolicy = Always`**, so the auth cookie is only ever
  sent over HTTPS.
- **Security stamp rotated after `ConfirmEmailAsync`**, invalidating any
  email-confirmation token issued for the same user after first use.

### Rate limiting

- **`register` policy** (5/min per connection) applied to `Register` POST.
- **`passwordreset` policy** (3 per 5 min per connection) applied to
  `ResetPassword` POST.
- **Global limiter now effective**: every request shares a generous per-IP
  budget via `GlobalLimiter`, and the previously-dead named `global` policy is
  retained for explicit use.
- **`QueueLimit = 0` on every policy**, so requests are rejected immediately
  rather than queuing past the window.
- **`RetryAfter` reflects the actual limiter window** from the lease metadata
  instead of a hardcoded 60s.

### Payment / webhook / refund hardening

- **Mock hosted-checkout processor** is Development-only (404 outside
  Development), requires the anti-forgery token, verifies order ownership before
  delivering a signed `payment.succeeded` webhook, and signs the webhook with the
  configured secret rather than computing it from attacker-controlled input.
- **Shipping refunds capped**: a shipping refund is limited to the remaining
  refundable amount and can be issued at most once per order (no repeated
  shipping refunds), preventing over-refunding.
- **Deterministic refund idempotency default**: when the admin client does not
  supply an idempotency key, the refund uses a deterministic key derived from the
  order/refund identity so the payment provider dedupe is effective.
- **Coupon usage count atomic across instances**: coupon usage increments are
  serialized with a SQL Server application lock (`sp_getapplock`), so the
  per-customer usage limit cannot be exceeded by concurrent requests from
  different app instances.

### Seeding and admin surface

- **`seed-roles`, `seed-content`, `seed-superadmin`** return 404 outside the
  Development environment, removing the unauthenticated privilege-escalation
  path from Production.

### Secrets and startup validation

- **Placeholder secrets stripped** from the checked-in base `appsettings.json`
  (`WebhookSecret` entries, `GuestAccessTokenSecret`, `ContinuationTokenSecret`).
  Development overrides remain in `appsettings.Development.json` with clearly
  dev-prefixed values.
- **Fail-fast startup guard**: outside Development the application refuses to
  start if any enabled payment provider lacks a webhook secret, if the guest or
  continuation secrets are missing/dev-prefixed, or if `Email:BaseUrl` is
  missing/localhost.

### Security headers

A `SecurityHeadersMiddleware` (registered first in the request pipeline) now
emits on every response:

- `Content-Security-Policy`:
  `default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; frame-src 'self';`
  (`'unsafe-inline'` for script/style is required by the server-rendered Razor
  views and is flagged as an accepted risk below).
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `X-Frame-Options: DENY`
- `Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=(), battery=(), accelerometer=(), gyroscope=()`
- `Strict-Transport-Security: max-age=31536000; includeSubDomains; preload`
  outside Development when the request is HTTPS (`UseHsts()` also applies).

### Data Protection key persistence

Data Protection is now configured through a `DataProtection` configuration
section:

```json
"DataProtection": {
  "ApplicationName": "FashionStore",
  "KeysDirectory": ""
}
```

- `ApplicationName` is recorded in the key ring and must be identical across all
  instances sharing the ring.
- When `KeysDirectory` is set, keys are persisted to that filesystem directory
  (`PersistKeysToFileSystem`); when empty (default), the built-in per-process key
  ring is used, which is acceptable for Development only.
- Development overrides use `App_Data/DataProtection-Keys`.

#### Production key-persistence plan

1. **Choose a durable store.** For a single web server, persist to a directory
   outside the app folder that survives redeploys and app-pool recycling, e.g.
   `%ProgramData%\FashionStore\DataProtection-Keys` (IIS) or
   `/var/lib/fashionstore/dataprotection` (Linux container volume).
2. **For multiple instances**, all instances must read the same key ring. The
   recommended options are, in order of preference:
   - **SQL Server key storage** via
     `PersistKeysToSqlServer(connectionString, "DataProtectionKeys")`
     (requires the `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`
     package) - shared automatically, transactional, and backed up with the
     database.
   - **Azure Key Vault / AWS-managed key storage** when running on those
     platforms.
   - **A shared network filesystem** when instance count is small and the FS is
     reliable.
3. **Protect keys at rest.** Keys written to disk are protected at rest by DPAPI
   when running as a Windows service/worker; on Linux use
   `ProtectKeysWithCertificate` with a certificate held in the platform secret
   store. At a minimum ensure the key directory ACL allows only the app account.
4. **Backup.** Include the key ring (or the SQL table holding it) in the standard
   database/file backup, so encrypted cookies and tokens survive a restore.
5. **Rollover.** Keys are rotated automatically by the framework; a rolling key
   ring means old cookies/tokens remain decryptable until expiry.

### Audit logging

A dedicated `IAuditService`/`AuditService` records immutable audit entries
(action, entity type/id, old/new value, actor, IP, user agent, timestamp) for:

| Action class | Recorded by |
|--------------|-------------|
| Admin login / logout | `AuthService` (`User.Login`, `User.Logout`) |
| Permission / role changes | `AdminController` (`Role.Seeding`, `User.SuperAdminCreated`) |
| Product deletion / deactivation / archiving | `ProductsController` |
| Product price changes | `ProductsController` |
| Inventory stock adjustments / thresholds | `InventoryController` |
| Order status / fulfilment changes | `OrderAdministrationService` |
| Refunds | `AdminReturnService` |
| Website settings changes | `WebsiteSettingsService` (`Settings.Update`) |
| Account suspension / reactivation | `AdminController` |

Settings-change audit now flows through `IAuditService` so actor IP and user
agent are captured, matching the other audit classes.

## Remaining accepted risks

| Risk | Severity | Rationale / mitigation |
|------|----------|------------------------|
| CSP allows `'unsafe-inline'` for `script-src` and `style-src` | Medium | The server-rendered Razor views and admin JS run inline script/style blocks. XSS is mitigated in depth by Razor HTML-encoding, the rich-content sanitizer, and a reviewed `Html.Raw(` surface; migration to nonce-based CSP is recommended (see below). |
| Permissions-policy is not universally honored (older browsers) | Low | Non-enforcement is a browser limitation; `X-Frame-Options: DENY` and CSP `frame-ancestors 'none'` cover the clickjacking vector regardless. |
| Email sending uses localhost SMTP in development | Low | Development only; production requires real SMTP credentials via environment variables. |
| `AllowedHosts: *` in base config | Low | Operated behind a reverse proxy with host filtering; tighten to the production hostname. |
| No CSP nonce/`strict-dynamic` yet | Medium | See accepted-risk row 1. Recommended follow-up: generate a per-request nonce, add `nonce` to inline/script tags in views, then drop `'unsafe-inline'`. |

### Recommended follow-up (not blocking)

- Migrate to a nonce-based CSP and remove `'unsafe-inline'`.
- Set `AllowedHosts` to the production hostname in the production appsettings.
- Consider `PersistKeysToSqlServer` for multi-instance production (documented
  above).

## Security testing checklist

### Build and static verification

- [x] `dotnet build FashionStore.sln` succeeds with 0 warnings and 0 errors
      (`TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`)
- [x] `dotnet test FashionStore.sln` passes (unit + integration suites)

### Authentication and account

- [x] Registration rejects duplicate email with a generic message
- [x] Unconfirmed-email login does not increment the failed-attempt counter
- [x] Login failure is uniform across unknown-user / inactive / locked / bad
      password / unconfirmed branches (unit: `AuthServiceTests`)
- [x] Register and ResetPassword are rate limited (5/min and 3/5-min policies)
- [x] Suspension rotates the security stamp and rejects existing sessions
- [x] Reactivation rotates the stamp and requires a fresh sign-in
- [x] Email-confirmation token is invalidated after first use

### Payment / webhook / refund

- [x] Mock hosted-checkout processor returns 404 outside Development
- [x] Mock processor requires anti-forgery token and order ownership
- [x] Shipping refund cannot be issued more than once and is capped
- [x] Refund idempotency uses a deterministic default key
- [x] Coupon usage-count increment is atomic across instances

### Admin surface / audit

- [x] `seed-roles` / `seed-content` / `seed-superadmin` return 404 outside
      Development
- [x] All admin controllers enforce `[Authorize]` policies
- [x] Hangfire dashboard behind `HangfireDashboardAuthorizationFilter`
- [x] Swagger only enabled in Development
- [x] Audit records exist for login, role/permission changes, product
      delete/deactivate/price, stock, order status, refunds, settings, suspension

### Request pipeline / headers

- [x] Every response carries CSP, `X-Content-Type-Options`, `Referrer-Policy`,
      `Permissions-Policy`, `X-Frame-Options` (integration: `SecurityHeadersTests`)
- [x] HSTS emitted over HTTPS outside Development
- [x] Non-Development startup refuses to run with missing/dev secrets
- [x] Data Protection keys persisted when `DataProtection:KeysDirectory` set

## Production security configuration

1. Set environment variables (never in the base appsettings):
   - `Database__ConnectionString` (SQL Server with a least-privilege login)
   - `Payments__Providers__*__WebhookSecret` for every enabled provider
   - `Order__GuestAccessTokenSecret` and `Checkout__ContinuationTokenSecret`
     (strong random values)
   - `Email__BaseUrl`, `Email__SmtpHost`, `Email__SmtpUsername`,
     `Email__SmtpPassword`
   - `DataProtection__KeysDirectory` (or use `PersistKeysToSqlServer`)
2. Run behind HTTPS (reverse proxy terminating TLS or IIS with the hosted
   bundle); `UseHttpsRedirection` + HSTS are active outside Development.
3. Set `AllowedHosts` to the production hostname.
4. Keep `DataProtection:ApplicationName` identical across all app instances.
5. Back up the Data Protection key ring together with the database.
6. Configure `appsettings.Production.json` for real SMTP, storage, payments.

## Completion status

Phase 30 security hardening is **complete**. The full solution builds cleanly
(warnings-as-errors), the complete unit + integration test suite passes, no EF
migration is required (no entity model changes), and the deliverables above are
documented.

Verified 2026-08-15.
