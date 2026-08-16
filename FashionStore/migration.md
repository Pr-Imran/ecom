In Package Manager Console (default project: FashionStore.Infrastructure, startup: FashionStore.Web):

SQL Server (AppDbContext):


Add-Migration YourChangeName -Context AppDbContext -Project FashionStore.Infrastructure -StartupProject FashionStore.Web

Update-Database -Context AppDbContext -Project FashionStore.Infrastructure -StartupProject FashionStore.Web




PostgreSQL (PostgreSqlAppDbContext — keep -OutputDir so the Postgres chain stays in its own folder):


Add-Migration YourChangeName -Context PostgreSqlAppDbContext -Project FashionStore.Infrastructure -StartupProject FashionStore.Web -OutputDir Data/Migrations/PostgreSql

Update-Database -Context PostgreSqlAppDbContext -Project FashionStore.Infrastructure -StartupProject FashionStore.Web -Connection "Host=localhost;Port=5432;Database=fashionstore_dev;Username=postgres;Password=YOUR_PASSWORD"



Notes:

Set the default project dropdown to FashionStore.Infrastructure first; -Project/-StartupProject make it explicit anyway.
-Connection (EF Core 8) lets you target a specific server, e.g. the Docker containers:
SQL: -Connection "Server=localhost,1433;Database=FashionStore_Dev;User Id=sa;Password=...;TrustServerCertificate=True;"
To generate an idempotent SQL script instead: Script-Migration -Context AppDbContext -Idempotent -Project FashionStore.Infrastructure -StartupProject FashionStore.Web.






Commands to delete + regenerate (PowerShell, from the FashionStore folder)
1. Delete the broken SQL Server migration chain (20 migrations + Designers):


Remove-Item src\FashionStore.Infrastructure\Data\Migrations\*.cs
This only removes files directly in Data\Migrations\ — it does not touch the PostgreSql subfolder. Keep that; it's a separate context.

2. Delete the stray root migration + the AppDbContext snapshot:


Remove-Item src\FashionStore.Infrastructure\Migrations\*.cs
This removes 20260729151513_InitialCreate.cs/.Designer.cs (an empty leftover) and AppDbContextModelSnapshot.cs.

Deleting the snapshot is required — if you keep it, Add-Migration would produce an empty migration (model already matches snapshot).

3. Regenerate one fresh full-schema migration:


Add-Migration InitialCreate -Context AppDbContext -Project FashionStore.Infrastructure -StartupProject FashionStore.Web -OutputDir Data/Migrations
4. Apply it:


Update-Database -Context AppDbContext -Project FashionStore.Infrastructure -StartupProject FashionStore.Web
Caveats
Only do this while the DB is fresh (yours is — only the empty InitialCreate applied). If the DB had data, deleting migrations would break the upgrade path.
This resets the SQL Server history to a single migration. The Postgres chain (PostgreSqlAppDbContext + Data/Migrations/PostgreSql/) is untouched and still valid.
After regenerating, review the new migration's UserRoles block — it must show only UserId/RoleId (no 1 suffixes).
Alternative (keeps history) — edit 20260730054130_InitialIdentity.cs to remove the UserId1/RoleId1 columns, FKs and indexes, and strip the corresponding drop-lines from 20260811175237_AddReviewSchema.cs. More surgi

