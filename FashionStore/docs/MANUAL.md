# FashionStore - Setup, Migration, Docker, Publish and Run Manual

This manual is the operational guide for the FashionStore e-commerce
application (ASP.NET Core 8, EF Core 8, SQL Server or PostgreSQL). It covers
everything that must be done by hand after pulling the repository:

* prerequisites and building
* configuration (database provider switch, connection strings, secrets)
* database migration for **SQL Server** and **PostgreSQL** (EF Core and raw SQL)
* Docker setup (compose with SQL Server or PostgreSQL)
* running the app locally and in a container
* publishing (folder, Docker image, IIS)
* seeding roles/content/super-admin (the only way to get data in)
* background jobs (Hangfire), email, uploads, Data Protection keys
* troubleshooting (ties back to the fixes in the latest commit)

All commands assume you are in the repository root: `FashionStore/`
(the folder containing `FashionStore.sln`).

---

## 1. Architecture in one screen

```
FashionStore.sln
├─ src/
│  ├─ FashionStore.Domain          (entities, enums, domain rules)
│  ├─ FashionStore.Application     (DTOs, service interfaces, config models)
│  ├─ FashionStore.Infrastructure  (EF DbContext, migrations, Hangfire, email,
│  │                                 payments, invoice PDF, file storage)
│  └─ FashionStore.Web             (MVC controllers, Razor views, Program.cs)
├─ tests/
│  ├─ FashionStore.UnitTests
│  ├─ FashionStore.IntegrationTests
│  └─ Playwright                    (optional e2e)
├─ Dockerfile
├─ docker-compose.yml
└─ docs/
```

Key runtime facts:

* **Two EF Core contexts live in `FashionStore.Infrastructure`:**
  * `AppDbContext` - the runtime context. The provider is selected by the
    `Database:Provider` setting (`SqlServer` or `PostgreSql`). Both the EF
    connection and the Hangfire storage connection follow that setting.
  * `PostgreSqlAppDbContext` - a thin migration-host context that keeps the
    **PostgreSQL** migration chain and model snapshot in their own folder,
    parallel to the SQL Server chain.
* **Migrations are NOT applied automatically.** `Program.cs` does not call
  `Database.Migrate()`. You must apply them yourself before first run
  (section 5).
* **Design-time factories** tell EF tools how to connect:
  * `Data/DesignTimeDbContextFactory.cs` - SQL Server `AppDbContext`, fixed
    local connection string.
  * `Data/PostgreSqlAppDbContextFactory.cs` - `PostgreSqlAppDbContext`, reads
    the `Database__ConnectionString` environment variable (falls back to
    `localhost` / `postgres` / `postgres`).
* The app runs only on .NET 8 (`net8.0`). Ports: local dev `5188`, Docker
  SQL stack `8080`, Docker Postgres stack `8081`.

---

## 2. Prerequisites

| Tool | Version | Needed for |
|------|---------|------------|
| .NET SDK | 8.x (SDK) | build, run, `dotnet ef`, tests |
| EF Core tools | 8.0.x (`dotnet-ef`) | generating/applying migrations |
| Node.js + npm | 20.x | Tailwind CSS bundle (`npm ci`, `css:build`) |
| Docker + Compose | any recent | `docker compose` stack |
| SQL Server | 2022 (or any supported) | SQL Server provider |
| PostgreSQL | 14/15/16 | Postgres provider |
| sqlcmd / psql | - | applying raw SQL migration scripts |

Install the EF tools (global tool):

```bash
dotnet tool install --global dotnet-ef --version 8.*
# or update if already present
dotnet tool update --global dotnet-ef --version 8.*
```

Verify:

```bash
dotnet --version
dotnet ef --version
node --version
docker compose version
```

---

## 3. Build the project

Restore .NET packages:

```bash
dotnet restore FashionStore.sln
```

Build the Tailwind stylesheet (required before build - the Web project
references `wwwroot/css/site.min.css`):

```bash
# from the Web project folder
cd src/FashionStore.Web
npm ci
npm run css:build
cd ../..
```

Compile the whole solution. Note that `Directory.Build.props` sets
`TreatWarningsAsErrors=true`, so warnings fail the build:

```bash
dotnet build FashionStore.sln --no-restore
```

> If you see `CA1862`-style warnings, the code already wraps the affected
> lines in `#pragma warning disable CA1862`; do not re-introduce
> `StringComparison` overloads in LINQ-to-Entities queries (see section 11).

---

## 4. Configuration

Configuration follows the ASP.NET Core `Section__Key` environment-variable
convention and is layered as:

```
appsettings.json  <-  appsettings.{Environment}.json  <-  env vars  <-  CLI args
```

Environment variables always win.

### 4.1 Database provider switch

The single setting that chooses the database engine is:

```
Database__Provider = SqlServer | PostgreSql
```

* Default (and `appsettings.json`): `SqlServer`
* Runtime selection happens in `FashionStore.Infrastructure/DependencyInjection.cs`:
  Npgsql is used when the value equals `PostgreSql` (case-insensitive),
  otherwise SQL Server. **The same setting also selects the Hangfire storage
  provider.**

### 4.2 Connection strings

* **SQL Server (default):**

  ```
  Server=localhost;Database=FashionStore_Dev;Trusted_Connection=True;TrustServerCertificate=True;
  ```

  For a SQL Server container / Linux login:

  ```
  Server=localhost,1433;Database=FashionStore_Dev;User Id=sa;Password=...;TrustServerCertificate=True;
  ```

* **PostgreSQL:**

  ```
  Host=localhost;Port=5432;Database=fashionstore_dev;Username=postgres;Password=...
  ```

* Set them either in `appsettings.{Environment}.json` under `"Database"` or as
  environment variables:

  ```bash
  export Database__Provider=PostgreSql
  export Database__ConnectionString="Host=localhost;Port=5432;Database=fashionstore_dev;Username=postgres;Password=..."
  ```

### 4.3 Secrets (production only)

The app **refuses to start** in `Production` unless these are set (fail-fast
check in `Program.cs`):

```
Payments__Providers__0__WebhookSecret   (and any other enabled provider)
Order__GuestAccessTokenSecret
Checkout__ContinuationTokenSecret
Email__BaseUrl                          (must not start with https://localhost)
```

In Development these may be the `dev-*` placeholders already present in
`appsettings.Development.json`.

---

## 5. Database migrations

Two parallel migration chains exist:

| Provider | Context | Migration folder | Design-time factory |
|----------|---------|------------------|---------------------|
| SQL Server | `AppDbContext` | `src/FashionStore.Infrastructure/Data/Migrations/` | `DesignTimeDbContextFactory` |
| PostgreSQL | `PostgreSqlAppDbContext` | `src/FashionStore.Infrastructure/Data/Migrations/PostgreSql/` | `PostgreSqlAppDbContextFactory` |

> Because two contexts live in one assembly, **always pass `--context`** to
> `dotnet ef`. Always run with `--project src/FashionStore.Infrastructure`
> (where migrations live) and `--startup-project src/FashionStore.Web`
> (the executable project).

### 5.1 SQL Server - apply migrations

Local Windows / SQL Server with integrated auth:

```bash
dotnet ef database update --context AppDbContext \
  --project src/FashionStore.Infrastructure \
  --startup-project src/FashionStore.Web
```

SQL Server container / SQL login (override the connection with `--connection`,
EF Core 8 `dotnet ef database update` supports this):

```bash
dotnet ef database update --context AppDbContext \
  --project src/FashionStore.Infrastructure \
  --startup-project src/FashionStore.Web \
  --connection "Server=localhost,1433;Database=FashionStore_Dev;User Id=sa;Password=YOUR_SA_PASSWORD;TrustServerCertificate=True;"
```

### 5.2 PostgreSQL - apply migrations

The Postgres design-time factory reads the `Database__ConnectionString`
environment variable:

```bash
export Database__ConnectionString="Host=localhost;Port=5432;Database=fashionstore_dev;Username=postgres;Password=YOUR_PASSWORD"

dotnet ef database update --context PostgreSqlAppDbContext \
  --project src/FashionStore.Infrastructure \
  --startup-project src/FashionStore.Web
```

> If the environment variable is missing the factory falls back to
> `Host=localhost;Port=5432;Database=fashionstore;Username=postgres;Password=postgres`.

### 5.3 Generating SQL scripts (manual SQL migration)

For environments where you want to review or run the SQL yourself, generate an
**idempotent** script and apply it with `sqlcmd` / `psql`. This is the
recommended production approach (review the script, back up first, apply once).

SQL Server:

```bash
dotnet ef migrations script --context AppDbContext --idempotent \
  --project src/FashionStore.Infrastructure \
  --startup-project src/FashionStore.Web \
  --output ./migration-sqlserver.sql
```

Apply:

```bash
sqlcmd -S localhost -U sa -P 'YOUR_SA_PASSWORD' -d FashionStore_Dev -i migration-sqlserver.sql
```

PostgreSQL:

```bash
export Database__ConnectionString="Host=localhost;Port=5432;Database=fashionstore_dev;Username=postgres;Password=YOUR_PASSWORD"

dotnet ef migrations script --context PostgreSqlAppDbContext --idempotent \
  --project src/FashionStore.Infrastructure \
  --startup-project src/FashionStore.Web \
  --output ./migration-postgres.sql
```

Apply:

```bash
psql "Host=localhost;Port=5432;Database=fashionstore_dev;Username=postgres" -f migration-postgres.sql
```

Idempotent scripts check `__EFMigrationsHistory` and only apply pending
migrations, so they are safe to re-run. A fresh database receives the full
schema; an existing database receives only what is missing.

### 5.4 Adding a NEW migration

After you change the EF model:

SQL Server (default output folder is `Data/Migrations/`):

```bash
dotnet ef migrations add NameOfChange --context AppDbContext \
  --project src/FashionStore.Infrastructure \
  --startup-project src/FashionStore.Web
```

PostgreSQL (must keep the Postgres chain in its own folder, otherwise it would
pollute the SQL Server chain):

```bash
dotnet ef migrations add NameOfChange --context PostgreSqlAppDbContext \
  --project src/FashionStore.Infrastructure \
  --startup-project src/FashionStore.Web \
  --output-dir Data/Migrations/PostgreSql
```

Then apply with 5.1 / 5.2 and commit the generated files.

### 5.5 Reverting / going to a specific migration

```bash
# SQL Server - roll back to a specific migration
dotnet ef database update InitialIdentity --context AppDbContext \
  --project src/FashionStore.Infrastructure --startup-project src/FashionStore.Web
```

> Downgrading is only safe for schema changes that have SQL `Down()` bodies.
> Back up before downgrading in any shared environment.

---

## 6. Docker setup

Two compose stacks are provided (`docker-compose.yml`). Both build the same
image from `Dockerfile` (multi-stage: Tailwind CSS via Node, `dotnet publish`,
non-root runtime image).

| Profile | Containers | App port | DB |
|---------|-----------|----------|-----|
| (default) | `db` + `app` | http://localhost:8080 | SQL Server 2022 |
| `postgres` | `db-postgres` + `app-postgres` | http://localhost:8081 | PostgreSQL 16 |

Named volumes survive container recreation: `mssql-data`, `postgres-data`,
`app-uploads`, `app-dataprotection`, `app-logs`.

### 6.1 SQL Server stack (default)

```bash
docker compose up --build
```

* Web app: http://localhost:8080
* SQL Server: localhost:1433 (sa / `FashionStore_dev_2026!`, DB `FashionStore_Dev`)
* Health check: app `/health/live`, db via `sqlcmd` - compose waits for a
  healthy db before starting the app.

### 6.2 PostgreSQL stack

```bash
docker compose --profile postgres up --build
```

* Web app: http://localhost:8081
* PostgreSQL: localhost:5432 (postgres / `FashionStore_dev_2026!`, DB `fashionstore_dev`)

### 6.3 Apply migrations to the compose-managed database

Compose starts the app, but **does not migrate** the database. Before first
browsing, apply migrations from the host (the container ports are published to
localhost):

SQL Server container:

```bash
dotnet ef database update --context AppDbContext \
  --project src/FashionStore.Infrastructure \
  --startup-project src/FashionStore.Web \
  --connection "Server=localhost,1433;Database=FashionStore_Dev;User Id=sa;Password=FashionStore_dev_2026!;TrustServerCertificate=True;"
```

PostgreSQL container:

```bash
export Database__ConnectionString="Host=localhost;Port=5432;Database=fashionstore_dev;Username=postgres;Password=FashionStore_dev_2026!"

dotnet ef database update --context PostgreSqlAppDbContext \
  --project src/FashionStore.Infrastructure \
  --startup-project src/FashionStore.Web
```

> Note: the SQL Server design-time factory does not read environment
> variables, which is why the `--connection` override is required there. The
> Postgres factory reads `Database__ConnectionString`.

### 6.4 Stopping the stack

```bash
# stop (keeps volumes and data)
docker compose stop
docker compose --profile postgres stop

# stop and remove containers/networks (data in named volumes is kept)
docker compose down
docker compose --profile postgres down

# stop, remove containers and DESTROY database volumes (irreversible)
docker compose down -v
docker compose --profile postgres down -v
```

### 6.5 Building/running the image standalone

```bash
docker build -t fashionstore:latest .

docker run --rm -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e Database__Provider=SqlServer \
  -e Database__ConnectionString="Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;" \
  -e Order__GuestAccessTokenSecret=... \
  -e Checkout__ContinuationTokenSecret=... \
  -e Payments__Providers__0__WebhookSecret=... \
  -e Email__BaseUrl=https://shop.example.com \
  -v fashionstore-uploads:/app/uploads \
  -v fashionstore-dp:/app/App_Data/DataProtection-Keys \
  -v fashionstore-logs:/app/Logs \
  fashionstore:latest
```

The image listens on `:8080`, runs as a non-root user, and requires the three
volume paths to be mounted for persistence (uploads, Data Protection keys,
logs).

---

## 7. Run locally (no Docker)

The default launch profile (`Properties/launchSettings.json`) uses
`http://localhost:5188` with `ASPNETCORE_ENVIRONMENT=Development`.

### 7.1 SQL Server

Point the connection string at a reachable SQL Server, then:

```bash
export Database__Provider=SqlServer
export Database__ConnectionString="Server=localhost;Database=FashionStore_Dev;Trusted_Connection=True;TrustServerCertificate=True;"

dotnet run --project src/FashionStore.Web
```

Swagger is available at http://localhost:5188/swagger (Development only).

### 7.2 PostgreSQL

```bash
export Database__Provider=PostgreSql
export Database__ConnectionString="Host=localhost;Port=5432;Database=fashionstore_dev;Username=postgres;Password=..."

dotnet run --project src/FashionStore.Web
```

### 7.3 Common environment variables

```bash
ASPNETCORE_ENVIRONMENT=Development|Production
ASPNETCORE_URLS=http://+:5188
Database__Provider=SqlServer|PostgreSql
Database__ConnectionString=...
Email__Provider=Development|Custom|Gmail|Outlook|Hotmail|Yahoo|Api
Email__SmtpHost=...
Email__SmtpPort=587
Email__SmtpUsername=...
Email__SmtpPassword=...
Email__UseSsl=true
Email__BaseUrl=https://shop.example.com
FileStorage__Provider=Local|AzureBlob
FileStorage__BasePath=uploads
FileStorage__PublicUrlBase=/uploads
DataProtection__KeysDirectory=App_Data/DataProtection-Keys
Order__GuestAccessTokenSecret=...
Checkout__ContinuationTokenSecret=...
Hangfire__DashboardEnabled=true
Hangfire__DashboardRole=SuperAdmin
BackgroundJobs__Enabled=true
```

---

## 8. Seeding (manual steps - required for a usable store)

Migrations create the schema but **no data**. Roles, permissions, content and
the administrator are created through development-only seed endpoints
(`Controllers/Admin/AdminController.cs`). These endpoints are `AllowAnonymous`
but **only respond in the Development environment** (they return 404 otherwise).

First-time setup order:

1. Start the app (section 7 or 6), then:

```bash
# 1. Roles + permissions
curl -X POST http://localhost:5188/api/admin/seed-roles

# 2. Default content (system pages, policy documents, main navigation)
curl -X POST http://localhost:5188/api/admin/seed-content

# 3. SuperAdmin account
curl -X POST http://localhost:5188/api/admin/seed-superadmin \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@example.com","firstName":"Store","lastName":"Admin","password":"StrongPass!123"}'
```

For the Docker stacks use port `8080` (SQL) or `8081` (Postgres) instead of
`5188`.

2. Log in at `/Account/Login`. The admin area is `/admin` (or via the header);
   permission claims are assigned to roles by the role seeder, so the seeded
   SuperAdmin can access everything.

3. The seeded content makes `/about`, `/contact`, `/size-guide`,
   `/delivery-policy`, `/return-policy`, `/privacy-policy`, `/terms`
   resolvable. Until content is seeded those routes return 404 by design.

> These endpoints are development-only; in Production they are 404. Do not
> weaken that guard.

---

## 9. Publish

### 9.1 Publish a runnable folder

Rebuild CSS, then publish:

```bash
cd src/FashionStore.Web
npm ci
npm run css:build
cd ../..

dotnet publish src/FashionStore.Web/FashionStore.Web.csproj \
  --configuration Release \
  --output ./publish
```

Run the published app:

```bash
ASPNETCORE_ENVIRONMENT=Production \
Database__Provider=SqlServer \
Database__ConnectionString="Server=...;Database=FashionStore;User Id=...;Password=...;TrustServerCertificate=True;" \
Order__GuestAccessTokenSecret=... \
Checkout__ContinuationTokenSecret=... \
Payments__Providers__0__WebhookSecret=... \
Email__BaseUrl=https://shop.example.com \
dotnet publish/FashionStore.Web.dll
```

### 9.2 Docker image

```bash
docker build -t fashionstore:latest .
docker push <registry>/fashionstore:latest   # if using a registry
```

### 9.3 IIS / Windows Server

* Install the **.NET 8 ASP.NET Core Hosting Bundle**.
* Publish with `dotnet publish -c Release -o ./publish`; the repo ships a
  ready `src/FashionStore.Web/web.config` (in-process hosting,
  `ASPNETCORE_ENVIRONMENT=Production`).
* Application pool: dedicated pool, `.NET CLR Version = No Managed Code`.
* Grant the pool identity read/execute on the publish folder and modify on
  `uploads/`, `Logs/`, `App_Data/DataProtection-Keys`.
* Terminate TLS at IIS/ARR and set `ForwardedHeaders__Enabled=true` with the
  ARR/proxy as a known proxy so absolute `https://` links are built.

---

## 10. Operations notes

### 10.1 Background jobs (Hangfire)

* Hangfire runs **in-process** (a `BackgroundJobServer` is started when
  `BackgroundJobs:Enabled=true`). Recurring jobs are registered at startup:
  inventory reservation release, email outbox, expire unpaid orders, scheduled
  promotions, low-stock alerts, temp-upload cleanup.
* Dashboard: `/hangfire` (Development default). It is protected by the
  `Hangfire:DashboardRole` (default `SuperAdmin`); in Production the dashboard
  is disabled by default (`Hangfire__DashboardEnabled=false`).
* Hangfire storage follows `Database__Provider` (SQL Server or Postgres) using
  the same connection string.

### 10.2 Email

* Default provider is `Development`: messages are written to the log (no SMTP
  needed).
* For a local inbox, run MailHog and point SMTP at it:

```bash
docker run -d -p 1025:1025 -p 8025:8025 --name mailhog mailhog/mailhog
```

```bash
export Email__Provider=Custom
export Email__SmtpHost=localhost
export Email__SmtpPort=1025
export Email__UseSsl=false
export Email__FromAddress=noreply@fashionstore.example.com
```

Inspect the inbox at http://localhost:8025. The compose files already point the
app at `host.docker.internal:1025` for this purpose.
For real delivery use a preset (`Gmail`, `Outlook`, `Hotmail`, `Yahoo`) or
`Custom` with your own SMTP and `Email__BaseUrl` set to the production origin.

### 10.3 Uploads / file storage

* Local storage is the default (`FileStorage__Provider=Local`,
  `FileStorage__BasePath=uploads`, public at `/uploads`). In Docker the path
  is `/app/uploads` and must be a mounted volume.
* For scalable object storage switch to
  `CloudFileStorage__Provider=AzureBlob` (Azure) or S3 and keep files out of
  the container filesystem.

### 10.4 Data Protection keys

* Identity cookies and data-protected tokens need a persistent key store.
  Configure `DataProtection__KeysDirectory` (Development default:
  `App_Data/DataProtection-Keys`). In Docker this is `/app/App_Data/DataProtection-Keys`
  and must be a mounted volume, otherwise keys are regenerated on every
  container restart and cookies/tokens stop validating.

### 10.5 Health checks

* `/health` - summary
* `/health/live` - liveness (always healthy when the process is up)
* `/health/ready` - readiness (checks database + storage)

### 10.6 Tests

```bash
dotnet test FashionStore.sln
```

Unit and integration suites use SQLite/InMemory and do not require a real
database. Playwright e2e tests (optional) are under `tests/Playwright`.

---

## 11. Troubleshooting (including the latest fixes)

The most recent commit (`d2446a0` "fix(postgres): resolve runtime errors and
document remaining manual steps") fixed runtime issues that you may otherwise
hit again. Summary and guard-rails:

| Symptom | Cause / fix |
|---------|-------------|
| `/products` 500 on Postgres | LINQ queries used `string.Equals(x, y, StringComparison.OrdinalIgnoreCase)` which does not translate on Npgsql. Use `.ToLower() ==` (with `#pragma warning disable CA1862`) in in-query comparisons. |
| `/LayoutsDemo` 500 "view not found" | Views live in `Views/Demo/` but the controller is named `LayoutsDemo`. `Infrastructure/LayoutsDemoViewLocationExpander.cs` (registered in `Program.cs`) bridges the names. |
| `/LayoutsDemo/StatesDemo` 500 | Missing `Views/Shared/_State.cshtml` partial referenced by `States.cshtml`. The file now exists. |
| `/api/admin/seed-roles` 500 | `AuditService` wrote a username/email into `AuditLog.UserId` (GUID FK). The actor is now resolved from the `NameIdentifier` claim and the audit row is skipped with a warning when no authenticated user exists. |
| 404 on `/about`, `/contact`, policy pages | Content-driven routes; seed via `POST /api/admin/seed-content` (section 8). |
| Build fails with warnings | `TreatWarningsAsErrors=true`; keep `NoWarn`/`#pragma` consistent with the codebase. |
| App refuses to start in Production | Missing secrets - see section 4.3. |
| Cookies/tokens invalid after container restart | Data Protection keys directory not mounted as a volume (section 10.4). |
| Compose app never becomes healthy | DB not migrated yet, or DB container still starting; run migrations from section 6.3 and check `docker compose ps`. |

### Rule of thumb for future EF queries

Only use patterns that translate on **both** SQL Server and Npgsql. Avoid
`StringComparison` overloads and client-evaluated string casing in predicates;
prefer `EF.Functions.ILike`/`.ToLower()` equivalents that Npgsql supports.

---

## 12. Production checklist (manual)

1. Apply SQL Server or PostgreSQL migrations (section 5) and verify
   `__EFMigrationsHistory`.
2. Set all production secrets as environment variables (section 4.3).
3. Set `Email__BaseUrl` to the public site origin and configure a real email
   provider (section 10.2).
4. Mount volumes for uploads, Data Protection keys and logs (sections 6/10).
5. Set `ForwardedHeaders__Enabled=true` and known proxies when behind a reverse
   proxy / load balancer.
6. Terminate TLS at the proxy and force HTTPS (the app already redirects).
7. Keep `Hangfire__DashboardEnabled=false` in Production.
8. Run the automated tests before deploying:
   `dotnet test FashionStore.sln`.
9. Health-check after deploy: `/health/live` and `/health/ready`.
10. Never run the development seed endpoints in Production (they 404 there).
