# Phase 32 - Deployment and Production Readiness Deliverables

## Scope

This phase prepares the FashionStore application for production deployment. No
new features were added and no database schema changed, so **no EF migration is
required for this phase**. Deliverables cover the four deployment targets
(IIS, Docker, reverse-proxy hosting, cloud hosting), production configuration,
a safe database migration strategy, Docker build artifacts, IIS hosting
guidance, operational runbooks and a final production checklist.

## 1. Deployment targets

The application is a standard ASP.NET Core 8 web application (`net8.0`). It can
be hosted in any of the following ways; they are independent and do not need to
be deployed simultaneously.

### 1.1 IIS deployment

* Requires the **.NET 8 ASP.NET Core Hosting Bundle** on the Windows Server.
  The bundle registers the `AspNetCoreModuleV2` and installs the ASP.NET Core
  runtime. Install it before creating the site.
* A published copy of the web project (`dotnet publish -c Release -o ./publish`)
  is placed in the site physical path. The repository includes a ready
  `src/FashionStore.Web/web.config` that pins the module handler, runs the app
  in-process (`hostingModel="inprocess"`), sets `ASPNETCORE_ENVIRONMENT` to
  `Production` and redirects `stdout` logging.
* **Application pool**: create a dedicated pool (e.g. `FashionStorePool`) with
  `.NET CLR Version = No Managed Code` (the ASP.NET Core module runs its own
  runtime). Pool identity should be a dedicated service account or
  `ApplicationPoolIdentity` with read access to the publish folder and modify
  access to the uploads, logs and Data Protection key folders.
* **File permissions**: grant the pool identity `Read & execute` on the publish
  directory and `Modify` on `uploads/`, `Logs/` and the Data Protection key
  directory (`App_Data/DataProtection-Keys`). IIS's own `IUSR`/`IIS_IUSRS`
  access is not enough because in-process hosting runs under the pool identity.
* **HTTPS**: terminate TLS at IIS with a binding for port 443. Set
  `ForwardedHeaders__Enabled=true` and configure the load balancer (ARR) as a
  known proxy so `X-Forwarded-Proto` is trusted; otherwise the app cannot
  build absolute `https://` links (email, payment return URLs).
* **Logs**: keep `stdoutLogEnabled="false"` in production and rely on the
  file/serilog sinks configured under `Logs/`. Enable `stdoutLogEnabled` only
  temporarily when troubleshooting a startup crash.
* **App restart behaviour**: file changes under the publish folder trigger a
  graceful restart via the module. Scheduled restarts recycle the pool and
  reload configuration. Data Protection keys and uploads must live outside the
  publish folder so they survive restarts and redeploys.

### 1.2 Docker deployment

* See section 4 for the production `Dockerfile`, `.dockerignore` and
  `docker-compose.yml`.
* The container runs as a **non-root user** (`$APP_UID`), listens on
  `http://+:8080` and exposes the `/health/live` liveness probe for the
  `HEALTHCHECK` instruction and for orchestrators.
* Persistent state (uploads, Data Protection keys, logs) is written under
  `/app` sub-directories that must be mounted as volumes.

### 1.3 Reverse-proxy hosting

The app supports running behind Nginx, Caddy, HAProxy, ARR or a cloud load
balancer. Because the reverse proxy terminates TLS and forwards requests:

1. Set `ForwardedHeaders__Enabled=true`.
2. Add the proxy IP/CIDR to `ForwardedHeaders__KnownProxies` /
   `ForwardedHeaders__KnownNetworks`. Both are **cleared by default** so an
   unconfigured header is never trusted (no open-proxy spoofing).
3. The reverse proxy must forward `X-Forwarded-For` and `X-Forwarded-Proto`.

The `/health`, `/health/live` and `/health/ready` endpoints are designed to be
consumed by the load balancer / orchestrator.

### 1.4 Cloud hosting

Works on any container or managed .NET platform:

* **Azure App Service**: set the connection string as an App Setting, uploads
  should move to Azure Blob (`CloudFileStorage__Provider=AzureBlob`), and Data
  Protection keys to a Key Vault or file share.
* **AWS**: ECS/Fargate/EKS with the Docker image; S3 via `CloudFileStorage`
  (S3Provider) and Secrets Manager for secrets.
* **Azure Container Apps / AWS App Runner**: same image, environment variables
  for configuration, mounted volumes for uploads/keys/logs.

## 2. Production configuration

### 2.1 Appsettings structure

* `appsettings.json` — full defaults, committed.
* `appsettings.Production.json` — production overrides, committed, but with
  **all secrets empty** (connection string, SMTP credentials, webhook secrets,
  guest/continuation-token secrets). Actual values are supplied through
  environment variables only (never in the repository).
* `ASPNETCORE_ENVIRONMENT=Production` selects the file. The app **fails fast on
  startup** (Phase 30 hardening) when required production secrets are missing.

### 2.2 Environment variables

Configuration uses the standard `Section__Key` convention, e.g.:

```text
ASPNETCORE_ENVIRONMENT=Production
Database__ConnectionString=Server=...;Database=FashionStore;User Id=...;Password=...
Email__SmtpHost=mail.example.com
Email__SmtpPort=587
Email__SmtpUsername=appuser
Email__SmtpPassword=<secret>
Email__UseSsl=true
Email__BaseUrl=https://shop.example.com
Email__AdminAlertRecipients=admin@example.com
Order__GuestAccessTokenSecret=<random-64-hex>
Checkout__ContinuationTokenSecret=<random-64-hex>
Payments__Providers__0__WebhookSecret=<cod-secret>
Payments__Providers__1__WebhookSecret=<card-secret>
DataProtection__KeysDirectory=/path/to/dataprotection-keys
```

Generate token secrets with:

```bash
openssl rand -hex 32
```

### 2.3 Secret management

* Local production (self-hosted): environment variables / a `.env` file loaded
  by the process manager (systemd, Docker Compose). Never commit real secrets.
* Cloud: platform secret stores (Azure Key Vault, AWS Secrets Manager) injected
  as environment variables by the runtime.
* The committed `appsettings.Production.json` intentionally carries empty
  values so a fresh clone can never start production with a default secret.

### 2.4 Database provider (SQL Server or PostgreSQL)

The store supports **SQL Server** (default) and **PostgreSQL**. The active
provider is chosen with `Database:Provider` (`"SqlServer"` or `"PostgreSql"`);
it configures both EF Core and the Hangfire job-storage backend.

`Database__ConnectionString` must match the chosen provider:

```text
# SQL Server
Database__Provider=SqlServer
Database__ConnectionString=Server=...;Database=FashionStore;User Id=...;Password=...;TrustServerCertificate=True;

# PostgreSQL
Database__Provider=PostgreSql
Database__ConnectionString=Host=...;Port=5432;Database=FashionStore;Username=...;Password=...
```

The dev `Trusted_Connection` SQL Server default is replaced in production by a
SQL login / managed identity; PostgreSQL uses the standard `Host=...;Database=...`
form. Command timeout and EF retry are raised in the production file (60s / 5
attempts).

#### 2.4.1 Migrations

Each provider owns its own migration history:

* SQL Server - the existing chain in `Data/Migrations` (apply with
  `dotnet ef database update --context AppDbContext`).
* PostgreSQL - a separate chain in `Data/Migrations/PostgreSql` rooted at the
  `InitialPostgres` migration, generated through the `PostgreSqlAppDbContext`
  design-time host. The shared model automatically remaps SQL Server `datetime2`
  columns to Postgres `timestamp with time zone`, so the Postgres schema is valid
  while SQL Server migrations stay untouched.

```bash
# Add a new PostgreSQL migration (after a model change)
dotnet ef migrations add <Name> \
  --project src/FashionStore.Infrastructure \
  --startup-project src/FashionStore.Web \
  --context PostgreSqlAppDbContext \
  --output-dir Data/Migrations/PostgreSql

# Apply PostgreSQL migrations to a target database
dotnet ef database update --context PostgreSqlAppDbContext \
  --connection "<postgres-connection-string>"
```

Verify a model change is captured for the provider you touched:

```bash
dotnet ef migrations has-pending-model-changes --context AppDbContext
dotnet ef migrations has-pending-model-changes --context PostgreSqlAppDbContext
```

Note: there is no runtime auto-migrate; schema is applied as part of the deploy
step. Do not point both providers at the same database.

#### 2.4.2 Hangfire storage

Hangfire uses the same provider: `SqlServerStorage` for SQL Server and
`PostgreSqlStorage` for PostgreSQL. The PostgreSQL storage is configured through
the `UsePostgreSqlStorage(Action<PostgreSqlBootstrapperOptions>,
PostgreSqlStorageOptions)` overload with schema preparation enabled; the legacy
string-based overload is obsolete and must not be used.

#### 2.4.3 Local development (Docker Compose)

`docker-compose.yml` provides both stacks:

```bash
docker compose up --build                     # SQL Server on :1433, app on :8080
docker compose --profile postgres up --build  # Postgres on :5432, app on :8081
```

### 2.5 Data Protection key storage

`DataProtection__ApplicationName` and `DataProtection__KeysDirectory` persist
antiforgery, auth cookies and email-confirmation tokens across restarts. In
production the keys directory **must** be a persistent, per-instance store
(shared volume on a single instance; consider a shared file share or Key Vault
when running multiple instances behind a load balancer). Losing the keys
invalidates all issued cookies and tokens.

### 2.6 HTTPS

* Terminate TLS at the reverse proxy / IIS / platform load balancer.
* Keep `ForwardedHeaders__Enabled=true` and trust only the proxy address so the
  app sees `https` and generates secure absolute URLs (payment returns, emails).
* The security headers middleware (Phase 30) emits `HSTS`, `Content-Security-Policy`
  and related headers; HSTS is sent over HTTPS and forces secure redirects.

### 2.7 Forwarded headers

`ForwardedHeaders__Enabled` (default `false`). When enabled, `KnownProxies` and
`KnownNetworks` are **cleared first**, then populated only from the configured
entries. See section 1.3.

### 2.8 Logging

* Serilog file sink writes under `Logs/` (path is environment-configurable via
  `Serilog__...` overrides). Log retention is covered in section 6.9.
* In containers the file sink also captures to stdout (captured by
  `docker logs` / orchestrator log agents).

### 2.9 Health checks

* `/health` — alias of readiness (kept for compatibility).
* `/health/live` — liveness; reports healthy as long as the process responds
  (no external dependency checks). Used by orchestrator probes and the Docker
  `HEALTHCHECK`.
* `/health/ready` — readiness; runs all dependency checks including the EF
  database check and the new **storage writability check** (see below).

A new `StorageHealthCheck` (Infrastructure, `Services/Storage/`) verifies the
local storage directory exists and is writable (probe file), so a full/read-only
upload volume stops traffic before product images or review attachments fail.
Non-local providers (Azure Blob / S3) report healthy without a probe.

### 2.10 Background jobs

`BackgroundJobs__Enabled`, `WorkerCount`, `Queues`, `ServerName`, `RetentionDays`
control the Hangfire server. The Hangfire dashboard is gated behind
`Hangfire__DashboardEnabled` (default `true`, set to `false` in production) and
`Hangfire__DashboardRole` (default `SuperAdmin`); the authorization filter now
requires the configured role.

### 2.11 Email

`Email__Provider` (`Development` writes to log; `Custom`/presets use SMTP),
`Email__SmtpHost/Port/Username/Password/UseSsl`, `Email__BaseUrl` (absolute
links in templates), `Email__AdminAlertRecipients` (low-stock/alert digests).
SMTP password is loaded from environment; missing credentials fail fast in
production when a custom SMTP provider is selected.

### 2.12 File storage

`FileStorage__Provider` — `Local` (default; `BasePath`/`PublicUrlBase`) or
`CloudFileStorage` (Azure Blob `ConnectionString`/`ContainerName`, or the S3
block). Production should use a persistent volume or object storage; the
storage health check guards local storage.

### 2.13 Payment configuration

`Payments__Providers__N__WebhookSecret` per enabled provider. Webhook
signatures are verified against these secrets (Phase 30). `Payments__ReturnUrl` /
`Payments__CancelUrl` are checkout return URLs (set to the public HTTPS base
URL in production).

### 2.14 Invoice configuration

`Invoice__CompanyName/Address/Email/Phone`, `TaxId`, `RegistrationNumber`,
`InvoicePrefix`, `YearAware`. No secrets. The invoice PDF renderer uses the
configuration at request time; only the data matters for correctness.

## 3. Database deployment (safe migration strategy)

* **No schema change in this phase**: no EF migration is generated.
* **Backup before migration**: take a full backup of the production database
  (`BACKUP DATABASE FashionStore TO DISK = N'/backup/FashionStore.bak' WITH
  INIT, COMPRESSION`) before applying any future migration.
* **Controlled migration process**: ship EF migrations with the application and
  apply them with `dotnet ef database update` (or a migration bundle)
  **before** the new app version starts serving traffic, or rely on
  `MigrateOnStartup` with idempotent migrations in a maintenance window. Use a
  migration bundle for controlled execution:
  ```bash
  dotnet ef migrations bundle -p src/FashionStore.Infrastructure -s src/FashionStore.Web -o ./efbundle
  ./efbundle --connection "Server=...;Database=FashionStore;..."
  ```
* **Rollback planning**: keep the previous release's binaries and the pre-migration
  backup. If a migration fails or is wrong, restore the backup and redeploy the
  previous version. Prefer additive migrations (new columns/tables) over
  destructive ones; destructive steps are delayed to a follow-up migration.
* **Seed-data rules**: seeding only inserts reference data when tables are empty
  (idempotent, `ISeed`/`EnsureSeedData` pattern). It never resets existing data.
* **Administrator creation**: created through the seeded admin bootstrap flow on
  first run; the account must be secured with a strong password and can be
  promoted/created via the admin panel. A startup-level check prevents duplicate
  creation.
* **No destructive automatic production reset**: `EnsureCreated`/`Drop`-style
  operations are never used in production; only migrations run.

## 4. Docker

### 4.1 Production Dockerfile (`FashionStore/Dockerfile`)

Multi-stage:

1. `css` — `node:20-alpine`, `npm ci` + `npm run css:build` (Tailwind → minified
   `site.min.css`).
2. `build` — `mcr.microsoft.com/dotnet/sdk:8.0`, copies `Directory.Build.props` /
   `Directory.Packages.props` (central package management) and `src/`, copies the
   Tailwind output from `css`, `dotnet restore` then `dotnet publish -c Release`.
3. `final` — `mcr.microsoft.com/dotnet/aspnet:8.0`, installs `curl` for the
   health probe, runs as `$APP_UID` (non-root), `EXPOSE 8080`,
   `HEALTHCHECK` against `/health/live`, and sets production defaults
   (`ASPNETCORE_ENVIRONMENT=Production`, `FileStorage__BasePath=/app/uploads`,
   `DataProtection__KeysDirectory=/app/App_Data/DataProtection-Keys`).

Build and run:

```bash
docker build -t fashionstore:latest .
docker run --rm -p 8080:8080 \
  -e Database__ConnectionString='Server=...;Database=FashionStore;...' \
  -e Email__SmtpHost='...' -e Email__SmtpUsername='...' -e Email__SmtpPassword='...' \
  -v fashionstore-uploads:/app/uploads \
  -v fashionstore-keys:/app/App_Data/DataProtection-Keys \
  fashionstore:latest
```

### 4.2 `.dockerignore`

Excludes `bin/`, `obj/`, `node_modules/`, `Logs/`, `uploads/`, `App_Data/`,
`TestResults/`, `playwright/`, `test-results/`, `.git/`, docs and Docker files so
the build context stays small.

### 4.3 Persistent storage guidance

Mount volumes for the three stateful paths:

* `/app/uploads` — product images and review attachments.
* `/app/App_Data/DataProtection-Keys` — cookie/token keys.
* `/app/Logs` — Serilog files.

### 4.4 Docker Compose (development)

`docker-compose.yml` at the repository root starts the app container plus a SQL
Server 2022 container (`fashionstore-db`, `MSSQL_PID=Developer`, named volume),
with the app health-checked and the connection string pointing at `db`. It is
intended for **local development** of the app against a real SQL Server; the app
healthcheck and volume wiring mirror the production image. Compose is not the
production deployment mechanism (use the standalone image + orchestrator).

```bash
docker compose up --build
# App on http://localhost:8080
```

## 5. IIS

See section 1.1 for the full checklist. Summary of the documented IIS items:

* **Hosting bundle** — .NET 8 ASP.NET Core Hosting Bundle (v8.x).
* **Application pool** — dedicated pool, `No Managed Code`, in-process.
* **File permissions** — pool identity read on publish, modify on
  uploads/logs/keys.
* **Environment variables** — set at the site level or machine level (never in
  `web.config`, which is committed).
* **Logs** — Serilog file sink under `Logs/`; stdout disabled in production.
* **HTTPS** — TLS binding + ForwardedHeaders.
* **Static files** — served by the ASP.NET Core static file middleware
  (`wwwroot`); IIS URL authorization is disabled for static content.
* **Data Protection** — persistent key directory outside the publish folder.
* **App restart behaviour** — graceful recycle on publish overwrite / pool
  recycle; keys/uploads survive.

## 6. Operations runbook

### 6.1 Database backups

Automated full backups nightly + transaction log backups (e.g. every 15 min)
via SQL Agent or an external backup tool. Retain at least 7 daily, 4 weekly,
3 monthly copies.

### 6.2 File backups

Backup the uploads volume (object storage snapshots or `rsync`/`azcopy` of the
mounted volume) on the same cadence as database backups.

### 6.3 Restore procedure

1. Stop the app or take it out of the load balancer.
2. Restore the database from the latest backup (or point-in-time for
   transaction logs).
3. Restore the uploads/keys/logs volumes if required (keys only on total loss).
4. Start the app and run the smoke-test checklist (section 7.5).

### 6.4 Health monitoring

Probe `/health/live` (liveness) and `/health/ready` (readiness) every 30s from
the orchestrator/load balancer; alert when readiness fails. The Docker
`HEALTHCHECK` and compose `healthcheck` both use `/health/live`.

### 6.5 Error alerting

Serilog errors are written to the file sink. Forward error-level events to a
central aggregator (Seq, Elastic/OpenSearch, Azure Log Analytics) or configure
the SMTP sink to an admin mailbox. Alert on 5xx rate and on
`Hangfire` server outage.

### 6.6 Disk monitoring

Monitor disk usage of the database, uploads, logs and Data Protection volumes;
alert at 80% and 90%. The storage health check stops traffic to `/health/ready`
when local storage is not writable.

### 6.7 Email failure monitoring

`Email__AdminAlertRecipients` receives alert digests. Check the background job
(`email` queue) failure counts in Hangfire for undelivered email jobs, and alert
when the failed-jobs counter grows.

### 6.8 Payment webhook monitoring

Watch Hangfire/webhook processing logs for signature failures
(webhook-secret mismatch) and alert on repeated failures. Confirm webhook
endpoint `/api/payments/webhook` is reachable and returns `200` for valid
requests from provider IPs only.

### 6.9 Hangfire monitoring

The dashboard is disabled in production by default (`Hangfire__DashboardEnabled
=false`). Enable it behind the `SuperAdmin` role only when needed, and monitor
the `default`/`email`/`invoice`/`cleanup` queues, failed jobs and storage
connection from the dashboard or a metrics endpoint.

### 6.10 Log retention

Retain Serilog JSON files for 30 days (daily rolling). Configure retention via
Serilog file rolling settings (`Serilog__WriteTo__...`); the Docker volume
should be sized for 30 days of logs.

### 6.11 Audit-log retention

Audit entries are stored in the database (Phase 30). Retain audit data for at
least 12 months; archive quarterly and purge older than the policy. Exports can
be produced from the admin panel / SQL.

### 6.12 Security update process

* Patch the base images (`mcr.microsoft.com/dotnet/aspnet`, `sdk`, `node`) and
  re-run `docker build` on a schedule; subscribe to .NET security advisories.
* Run `dotnet list package --vulnerable` in CI and fail on known-vulnerable
  transitive packages.
* Update the hosting bundle on IIS hosts within the same window.
* Re-verify the Phase 30 security checklist (headers, rate limiting, secrets)
  after each major upgrade.

## 7. Final verification

### 7.1 Final production checklist

- [ ] `ASPNETCORE_ENVIRONMENT=Production` set.
- [ ] Real secrets supplied via environment (DB, SMTP, webhooks, tokens); committed config has empty values.
- [ ] `Database__ConnectionString` points to production SQL Server; app fails fast if missing.
- [ ] `DataProtection__KeysDirectory` is persistent (volume / disk) and writable by the runtime identity.
- [ ] Forwarded headers enabled with the proxy address in `KnownProxies`/`KnownNetworks` when behind TLS-terminating proxy/IIS.
- [ ] HTTPS enforced; HSTS/CSP/security headers present on responses.
- [ ] `/health/live` and `/health/ready` reachable and wired into the orchestrator/load balancer.
- [ ] `Hangfire__DashboardEnabled=false` (or dashboard protected by role).
- [ ] Uploads volume writable; storage health check passing.
- [ ] Email SMTP credentials valid; test email sent.
- [ ] Payment providers enabled with correct webhook secrets; test webhook accepted.
- [ ] Invoice data (company details) correct on generated PDFs.
- [ ] Background jobs running (Hangfire server online; queues processed).
- [ ] File and database backups scheduled and verified with a test restore.
- [ ] Logging and audit-log retention configured.
- [ ] Security updates process in place (dependency + image scanning).

### 7.2 Deployment commands

```bash
# Docker
docker build -t fashionstore:latest .
docker push <registry>/fashionstore:latest

# IIS / manual publish
dotnet publish src/FashionStore.Web/FashionStore.Web.csproj -c Release -o ./publish

# EF migration bundle (when a schema migration ships)
dotnet ef migrations bundle -p src/FashionStore.Infrastructure -s src/FashionStore.Web -o ./efbundle
```

### 7.3 Required environment variables

See section 2.2. The minimum set to start in production:

```text
Database__ConnectionString
Email__SmtpHost, Email__SmtpUsername, Email__SmtpPassword
Order__GuestAccessTokenSecret
Checkout__ContinuationTokenSecret
```

Optional but recommended: `Payments__Providers__*__WebhookSecret`, `Email__BaseUrl`,
`Email__AdminAlertRecipients`, `ForwardedHeaders__*`, `DataProtection__KeysDirectory`.

### 7.4 Rollback steps

1. Restore the pre-migration database backup (if a migration was applied).
2. Redeploy the previous app version/image.
3. Restore the uploads volume if files were removed.
4. Run the smoke-test checklist; keep the rollback in place until verified.

### 7.5 Smoke-test checklist

- [ ] Customer registration, email confirmation and sign-in.
- [ ] Product browsing, search/filter, variation selection, product images load.
- [ ] Cart add/update/remove, coupon apply.
- [ ] Checkout (registered + guest), address capture, order placement.
- [ ] Payment flow per enabled provider; webhook processes payment; order confirmed.
- [ ] Order detail + guest order tracking (token link).
- [ ] Inventory reservation and release on abandon/expiry.
- [ ] Customer panel (orders, returns, reviews) works.
- [ ] Admin panel (products, inventory, orders, customers, settings).
- [ ] Invoice generated and PDF downloads correctly.
- [ ] Return request + refund workflow (manual/gateway).
- [ ] Mobile experience: pages responsive at 360px width.
- [ ] Desktop experience: pages correct at 1280px+.
- [ ] Security: HTTPS redirect, security headers present, admin area requires roles, webhooks reject bad signatures.
- [ ] Performance: home/product/checkout pages respond in reasonable time; no N+1 regressions.
- [ ] Accessibility: forms have labels; images have alt text; keyboard navigation works.
- [ ] SEO: sitemap/robots reachable; product pages have meta title/description.
- [ ] Backups: verify a backup exists and a restore was tested.

### 7.6 Known limitations

* **Single-instance Data Protection**: keys directory is per-instance. Multiple
  app instances behind a load balancer require a shared key store or sticky
  sessions.
* **Local file storage**: the `Local` provider serves uploads from the app
  process; for horizontal scale-out, switch to Azure Blob / S3
  (`CloudFileStorage__Provider`).
* **SQL Server only**: EF migrations target SQL Server; other providers are not
  supported.
* **Hangfire dashboard disabled by default** in production; operational dashboards
  rely on logs/metrics.
* **HTTPS termination expected upstream**: the container itself serves HTTP on
  8080 and relies on the reverse proxy/orchestrator for TLS.
* **No destructive production reset**: seed/admin bootstrap is idempotent and
  never drops data; manual SQL is required for destructive migrations.

### 7.7 Final completion status

All Phase 32 deliverables are complete and the full solution builds green with
zero warnings (warnings are treated as errors). The production image, compose
stack, IIS config and this runbook are committed. No EF migration was required.
Manual verification against a live deployment remains a deployment-site
responsibility using the smoke-test checklist above.

## Completed files

* `FashionStore/Dockerfile` — multi-stage production image (Tailwind + publish + non-root runtime + healthcheck).
* `FashionStore/.dockerignore` — build-context exclusions.
* `FashionStore/docker-compose.yml` — development app + SQL Server stack.
* `FashionStore/src/FashionStore.Web/web.config` — IIS in-process hosting.
* `FashionStore/src/FashionStore.Web/appsettings.Production.json` — production overrides (secrets empty).
* `FashionStore/src/FashionStore.Web/appsettings.json` — added `ForwardedHeaders` and `Hangfire` sections.
* `FashionStore/src/FashionStore.Web/Program.cs` — ForwardedHeaders (opt-in, trusted-proxy lists), `/health/live` + `/health/ready`, config-gated Hangfire dashboard.
* `FashionStore/src/FashionStore.Infrastructure/Services/Storage/StorageHealthCheck.cs` — storage readiness check.
* `FashionStore/src/FashionStore.Infrastructure/DependencyInjection.cs` — storage health check registration.
* `FashionStore/src/FashionStore.Web/Middleware/HangfireDashboardAuthorizationFilter.cs` — configurable required role.
* `docs/Phase32-DeploymentProduction.md` — this document.
