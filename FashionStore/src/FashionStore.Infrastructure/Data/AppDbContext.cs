using FashionStore.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FashionStore.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string,
    IdentityUserClaim<string>, ApplicationUserRole, IdentityUserLogin<string>,
    IdentityRoleClaim<string>, IdentityUserToken<string>>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductTag> ProductTags => Set<ProductTag>();
    public DbSet<RelatedProduct> RelatedProducts => Set<RelatedProduct>();
    public DbSet<ProductSpecification> ProductSpecifications => Set<ProductSpecification>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.ForeignKeyPropertiesMappedToUnrelatedTables));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Custom table names
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.UserName).IsUnique();
        });

        modelBuilder.Entity<ApplicationRole>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasIndex(e => e.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<ApplicationRoleClaim>(entity =>
        {
            entity.ToTable("RoleClaims");
            entity.HasOne(e => e.Role)
                  .WithMany(r => r.Claims)
                  .HasForeignKey(e => e.RoleId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationUserRole>(entity =>
        {
            entity.ToTable("UserRoles");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.User)
                  .WithMany(u => u.AuditLogs)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => e.Action);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasOne(e => e.ParentCategory).WithMany(c => c.Children).HasForeignKey(e => e.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Brand>(entity =>
        {
            entity.ToTable("Brands");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.Slug).IsUnique();
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.ToTable("Collections");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.Slug).IsUnique();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasIndex(e => e.CategoryId);
            entity.HasIndex(e => e.BrandId);
            entity.HasIndex(e => e.CollectionId);
            entity.HasIndex(e => e.BaseSku).IsUnique();
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.IsFeatured);
            entity.HasIndex(e => e.CreatedAtUtc);
            
            entity.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Collection).WithMany().HasForeignKey(e => e.CollectionId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ProductTag>(entity =>
        {
            entity.ToTable("ProductTags");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Slug).IsUnique();
        });

        modelBuilder.Entity<ProductTagMapping>(entity =>
        {
            entity.ToTable("ProductTagMappings");
            entity.HasKey(e => new { e.ProductId, e.ProductTagId });
            entity.HasOne(e => e.Product).WithMany(p => p.ProductTagMappings).HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ProductTag).WithMany(p => p.ProductTagMappings).HasForeignKey(e => e.ProductTagId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RelatedProduct>(entity =>
        {
            entity.ToTable("RelatedProducts");
            entity.HasKey(e => new { e.ProductId, e.RelatedProductId });
            entity.HasOne(e => e.Product).WithMany(p => p.RelatedProducts).HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.RelatedProductEntity).WithMany(p => p.RelatedToProducts).HasForeignKey(e => e.RelatedProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductSpecification>(entity =>
        {
            entity.ToTable("ProductSpecifications");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Value).IsRequired().HasMaxLength(500);
            entity.HasOne(e => e.Product).WithMany(p => p.Specifications).HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductSizeGuideMapping>(entity =>
        {
            entity.ToTable("ProductSizeGuideMappings");
            entity.HasKey(e => new { e.ProductId, e.SizeGuideId });
            entity.HasOne(e => e.Product).WithMany(p => p.SizeGuideMappings).HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        // Apply soft delete filter
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(SoftDeletableEntity).IsAssignableFrom(entityType.ClrType))
            {
                entityType.AddSoftDeleteQueryFilter();
            }
        }

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override int SaveChanges()
    {
        UpdateAuditFields();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateAuditFields()
    {
        var entries = ChangeTracker.Entries<IdentityUser>();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity is ApplicationUser appUser:
                    appUser.CreatedAtUtc = DateTime.UtcNow;
                    break;
                case EntityState.Modified when entry.Entity is ApplicationUser modifiedUser:
                    modifiedUser.UpdatedAtUtc = DateTime.UtcNow;
                    break;
            }
        }
    }
}
