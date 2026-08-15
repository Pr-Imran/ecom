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

    // Enables provider-specific derived contexts (e.g. the PostgreSqlAppDbContext
    // used as the design-time migration host for PostgreSQL) to reuse the shared
    // model while carrying their own DbContextOptions type.
    protected AppDbContext(DbContextOptions options) : base(options)
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
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductVariantAttributeValue> ProductVariantAttributeValues => Set<ProductVariantAttributeValue>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<WarehouseStock> WarehouseStocks => Set<WarehouseStock>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
    public DbSet<ReviewImage> ReviewImages => Set<ReviewImage>();
    public DbSet<ReviewHelpfulVote> ReviewHelpfulVotes => Set<ReviewHelpfulVote>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<CartCoupon> CartCoupons => Set<CartCoupon>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<CouponUsage> CouponUsages => Set<CouponUsage>();
    public DbSet<CouponCategory> CouponCategories => Set<CouponCategory>();
    public DbSet<CouponBrand> CouponBrands => Set<CouponBrand>();
    public DbSet<CouponProduct> CouponProducts => Set<CouponProduct>();
    public DbSet<CouponExcludedProduct> CouponExcludedProducts => Set<CouponExcludedProduct>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();
    public DbSet<ShippingZone> ShippingZones => Set<ShippingZone>();
    public DbSet<ShippingZoneCountry> ShippingZoneCountries => Set<ShippingZoneCountry>();
    public DbSet<ShippingZoneCity> ShippingZoneCities => Set<ShippingZoneCity>();
    public DbSet<ShippingRate> ShippingRates => Set<ShippingRate>();
    public DbSet<ShippingMethodProduct> ShippingMethodProducts => Set<ShippingMethodProduct>();
    public DbSet<ShippingMethodCategory> ShippingMethodCategories => Set<ShippingMethodCategory>();
    public DbSet<DeliveryBlackout> DeliveryBlackouts => Set<DeliveryBlackout>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderAddress> OrderAddresses => Set<OrderAddress>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<OrderNote> OrderNotes => Set<OrderNote>();
    public DbSet<OrderNumberSequence> OrderNumberSequences => Set<OrderNumberSequence>();
    public DbSet<OrderIdempotencyRecord> OrderIdempotencyRecords => Set<OrderIdempotencyRecord>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PaymentWebhookLog> PaymentWebhookLogs => Set<PaymentWebhookLog>();
    public DbSet<PaymentRefundRecord> PaymentRefundRecords => Set<PaymentRefundRecord>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceSendLog> InvoiceSendLogs => Set<InvoiceSendLog>();
    public DbSet<InvoiceNumberSequence> InvoiceNumberSequences => Set<InvoiceNumberSequence>();
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();
    public DbSet<ReturnItem> ReturnItems => Set<ReturnItem>();
    public DbSet<ReturnStatusHistory> ReturnStatusHistories => Set<ReturnStatusHistory>();
    public DbSet<ExchangeRequest> ExchangeRequests => Set<ExchangeRequest>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<RefundTransaction> RefundTransactions => Set<RefundTransaction>();
    public DbSet<ReturnReason> ReturnReasons => Set<ReturnReason>();
    public DbSet<ReturnAttachment> ReturnAttachments => Set<ReturnAttachment>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<ContentPage> ContentPages => Set<ContentPage>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<HomepageSection> HomepageSections => Set<HomepageSection>();
    public DbSet<NavigationMenu> NavigationMenus => Set<NavigationMenu>();
    public DbSet<NavigationItem> NavigationItems => Set<NavigationItem>();
    public DbSet<FaqItem> FaqItems => Set<FaqItem>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<PolicyDocument> PolicyDocuments => Set<PolicyDocument>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<SlugRedirect> SlugRedirects => Set<SlugRedirect>();

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
            entity.HasKey(e => new { e.UserId, e.RoleId });
            entity.HasOne(e => e.User)
                  .WithMany(u => u.UserRoles)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Role)
                  .WithMany(r => r.UserRoles)
                  .HasForeignKey(e => e.RoleId)
                  .OnDelete(DeleteBehavior.Cascade);
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
            entity.Property(e => e.AverageRating).HasPrecision(3, 2);

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

        modelBuilder.Entity<ProductAttribute>(entity =>
        {
            entity.ToTable("ProductAttributes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasIndex(e => e.IsActive);
        });

        modelBuilder.Entity<ProductAttributeValue>(entity =>
        {
            entity.ToTable("ProductAttributeValues");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => new { e.ProductAttributeId, e.Slug }).IsUnique();
            entity.HasIndex(e => e.IsActive);
            entity.HasOne(e => e.ProductAttribute).WithMany(a => a.Values).HasForeignKey(e => e.ProductAttributeId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.ToTable("ProductVariants");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Sku).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => e.Sku).IsUnique();
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.IsDefault);
            entity.HasOne(e => e.Product).WithMany(p => p.Variants).HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductReview>(entity =>
        {
            entity.ToTable("ProductReviews");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(450);
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Body).HasMaxLength(4000);
            entity.Property(e => e.ModerationNotes).HasMaxLength(2000);
            entity.HasIndex(e => new { e.ProductId, e.Status });
            entity.HasIndex(e => new { e.UserId, e.ProductId });
            entity.HasIndex(e => e.Rating);
            entity.HasIndex(e => e.IsVerifiedPurchase);
            entity.HasOne(e => e.Product).WithMany(p => p.Reviews).HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReviewImage>(entity =>
        {
            entity.ToTable("ReviewImages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.OriginalFileName).HasMaxLength(255);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.StoragePath).IsRequired().HasMaxLength(500);
            entity.HasIndex(e => e.ReviewId);
            entity.HasOne(e => e.Review).WithMany(r => r.Images).HasForeignKey(e => e.ReviewId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReviewHelpfulVote>(entity =>
        {
            entity.ToTable("ReviewHelpfulVotes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(450);
            entity.HasIndex(e => new { e.ReviewId, e.UserId }).IsUnique();
            entity.HasOne(e => e.Review).WithMany(r => r.HelpfulVotes).HasForeignKey(e => e.ReviewId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductVariantAttributeValue>(entity =>
        {
            entity.ToTable("ProductVariantAttributeValues");
            entity.HasKey(e => new { e.ProductVariantId, e.ProductAttributeValueId });
            entity.HasOne(e => e.Variant).WithMany(v => v.VariantAttributeValues).HasForeignKey(e => e.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AttributeValue).WithMany(a => a.VariantAttributeValues).HasForeignKey(e => e.ProductAttributeValueId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.ProductVariantId, e.ProductAttributeValueId }).IsUnique();
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.ToTable("ProductImages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.OriginalFileName).HasMaxLength(255);
            entity.Property(e => e.AltText).HasMaxLength(500);
            entity.Property(e => e.Caption).HasMaxLength(500);
            entity.Property(e => e.ImageFormat).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ProcessingStatus).IsRequired().HasMaxLength(20);
            entity.HasIndex(e => new { e.ProductId, e.DisplayOrder });
            entity.HasIndex(e => new { e.ProductId, e.IsMain });
            entity.HasIndex(e => e.ProductVariantId);
            entity.HasOne(e => e.Product).WithMany(p => p.Images).HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Variant).WithMany().HasForeignKey(e => e.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.ToTable("Warehouses");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.IsDefault);
        });

        modelBuilder.Entity<WarehouseStock>(entity =>
        {
            entity.ToTable("WarehouseStocks");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.WarehouseId, e.ProductVariantId }).IsUnique();
            entity.HasIndex(e => e.ProductVariantId);
            entity.HasIndex(e => e.OnHandQuantity);
            entity.HasIndex(e => e.LowStockThreshold);
            entity.HasOne(e => e.Warehouse).WithMany(w => w.StockItems).HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Variant).WithMany().HasForeignKey(e => e.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InventoryTransaction>(entity =>
        {
            entity.ToTable("InventoryTransactions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ReferenceId).HasMaxLength(100);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.AdministratorId).HasMaxLength(450);
            entity.HasIndex(e => e.ProductVariantId);
            entity.HasIndex(e => e.WarehouseId);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => e.Reason);
            entity.HasIndex(e => e.ReferenceType);
            entity.HasOne(e => e.Warehouse).WithMany(w => w.Transactions).HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Variant).WithMany().HasForeignKey(e => e.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockReservation>(entity =>
        {
            entity.ToTable("StockReservations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CartReference).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ReferenceId).HasMaxLength(100);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ExpiresAtUtc);
            entity.HasIndex(e => e.CartReference);
            entity.HasIndex(e => e.ProductVariantId);
            entity.HasOne(e => e.Variant).WithMany().HasForeignKey(e => e.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Warehouse).WithMany(w => w.Reservations).HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WishlistItem>(entity =>
        {
            entity.ToTable("WishlistItems");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(450);
            entity.HasIndex(e => new { e.UserId, e.ProductId, e.ProductVariantId }).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Variant).WithMany().HasForeignKey(e => e.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("CartItems");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(450);
            entity.HasIndex(e => new { e.UserId, e.ProductId, e.ProductVariantId }).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => e.ProductVariantId);
            entity.HasIndex(e => e.UpdatedAtUtc);
            entity.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Variant).WithMany().HasForeignKey(e => e.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CartCoupon>(entity =>
        {
            entity.ToTable("CartCoupons");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(450);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasIndex(e => e.CouponId);
            entity.HasOne(e => e.Coupon).WithMany().HasForeignKey(e => e.CouponId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Coupon>(entity =>
        {
            entity.ToTable("Coupons");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.Property(e => e.NormalizedCode).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CustomerId).HasMaxLength(450);
            entity.HasIndex(e => e.NormalizedCode).IsUnique();
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.EndAtUtc);
        });

        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.ToTable("Promotions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.EndAtUtc);
            entity.HasIndex(e => e.Priority);
            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => e.CategoryId);
            entity.HasIndex(e => e.BrandId);
            entity.HasIndex(e => e.CollectionId);
            entity.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Collection).WithMany().HasForeignKey(e => e.CollectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CouponUsage>(entity =>
        {
            entity.ToTable("CouponUsages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(450);
            entity.Property(e => e.OrderId).HasMaxLength(100);
            entity.HasIndex(e => e.CouponId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.UsedAtUtc);
            entity.HasOne(e => e.Coupon).WithMany(c => c.CouponUsages).HasForeignKey(e => e.CouponId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CouponCategory>(entity =>
        {
            entity.ToTable("CouponCategories");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CouponId, e.CategoryId }).IsUnique();
            entity.HasIndex(e => e.CategoryId);
            entity.HasOne(e => e.Coupon).WithMany(c => c.CouponCategories).HasForeignKey(e => e.CouponId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CouponBrand>(entity =>
        {
            entity.ToTable("CouponBrands");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CouponId, e.BrandId }).IsUnique();
            entity.HasIndex(e => e.BrandId);
            entity.HasOne(e => e.Coupon).WithMany(c => c.CouponBrands).HasForeignKey(e => e.CouponId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CouponProduct>(entity =>
        {
            entity.ToTable("CouponProducts");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CouponId, e.ProductId }).IsUnique();
            entity.HasIndex(e => e.ProductId);
            entity.HasOne(e => e.Coupon).WithMany(c => c.CouponProducts).HasForeignKey(e => e.CouponId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CouponExcludedProduct>(entity =>
        {
            entity.ToTable("CouponExcludedProducts");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CouponId, e.ProductId }).IsUnique();
            entity.HasIndex(e => e.ProductId);
            entity.HasOne(e => e.Coupon).WithMany(c => c.CouponExcludedProducts).HasForeignKey(e => e.CouponId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerAddress>(entity =>
        {
            entity.ToTable("CustomerAddresses");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(450);
            entity.Property(e => e.Label).IsRequired().HasMaxLength(50);
            entity.Property(e => e.RecipientName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Phone).HasMaxLength(30);
            entity.Property(e => e.AddressLine1).IsRequired().HasMaxLength(200);
            entity.Property(e => e.AddressLine2).HasMaxLength(200);
            entity.Property(e => e.Area).HasMaxLength(100);
            entity.Property(e => e.City).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Region).HasMaxLength(100);
            entity.Property(e => e.PostalCode).IsRequired().HasMaxLength(20);
            entity.Property(e => e.CountryCode).IsRequired().HasMaxLength(2);
            entity.Property(e => e.DeliveryInstructions).HasMaxLength(500);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.IsDefaultShipping });
            entity.HasIndex(e => new { e.UserId, e.IsDefaultBilling });
            entity.HasIndex(e => e.CreatedAtUtc);
        });

        modelBuilder.Entity<ShippingMethod>(entity =>
        {
            entity.ToTable("ShippingMethods");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.PickupInstructions).HasMaxLength(1000);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.DisplayOrder);
            entity.HasIndex(e => e.Type);
        });

        modelBuilder.Entity<ShippingZone>(entity =>
        {
            entity.ToTable("ShippingZones");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.DisplayOrder);
        });

        modelBuilder.Entity<ShippingZoneCountry>(entity =>
        {
            entity.ToTable("ShippingZoneCountries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CountryCode).IsRequired().HasMaxLength(2);
            entity.HasIndex(e => new { e.ShippingZoneId, e.CountryCode }).IsUnique();
            entity.HasIndex(e => e.CountryCode);
            entity.HasOne(e => e.ShippingZone).WithMany(z => z.Countries).HasForeignKey(e => e.ShippingZoneId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShippingZoneCity>(entity =>
        {
            entity.ToTable("ShippingZoneCities");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CityName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.NormalizedCityName).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => new { e.ShippingZoneId, e.NormalizedCityName }).IsUnique();
            entity.HasIndex(e => e.NormalizedCityName);
            entity.HasOne(e => e.ShippingZone).WithMany(z => z.Cities).HasForeignKey(e => e.ShippingZoneId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShippingRate>(entity =>
        {
            entity.ToTable("ShippingRates");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CityName).HasMaxLength(100);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.ShippingMethodId);
            entity.HasIndex(e => e.ShippingZoneId);
            entity.HasIndex(e => e.RateType);
            entity.HasOne(e => e.ShippingMethod).WithMany(m => m.Rates).HasForeignKey(e => e.ShippingMethodId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ShippingZone).WithMany().HasForeignKey(e => e.ShippingZoneId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ShippingMethodProduct>(entity =>
        {
            entity.ToTable("ShippingMethodProducts");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ShippingMethodId, e.ProductId, e.IsExclusion }).IsUnique();
            entity.HasIndex(e => e.ProductId);
            entity.HasOne(e => e.ShippingMethod).WithMany(m => m.ProductRestrictions).HasForeignKey(e => e.ShippingMethodId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShippingMethodCategory>(entity =>
        {
            entity.ToTable("ShippingMethodCategories");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ShippingMethodId, e.CategoryId, e.IsExclusion }).IsUnique();
            entity.HasIndex(e => e.CategoryId);
            entity.HasOne(e => e.ShippingMethod).WithMany(m => m.CategoryRestrictions).HasForeignKey(e => e.ShippingMethodId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeliveryBlackout>(entity =>
        {
            entity.ToTable("DeliveryBlackouts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.HasIndex(e => e.ShippingMethodId);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.StartAtUtc);
            entity.HasOne(e => e.ShippingMethod).WithMany(m => m.Blackouts).HasForeignKey(e => e.ShippingMethodId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PublicOrderNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.InvoiceNumber).HasMaxLength(50);
            entity.Property(e => e.UserId).HasMaxLength(450);
            entity.Property(e => e.GuestEmail).HasMaxLength(254);
            entity.Property(e => e.GuestPhone).HasMaxLength(30);
            entity.Property(e => e.CustomerName).HasMaxLength(200);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(3);
            entity.Property(e => e.PaymentMethodCode).HasMaxLength(50);
            entity.Property(e => e.ShippingMethodCode).HasMaxLength(50);
            entity.Property(e => e.ShippingMethodName).HasMaxLength(200);
            entity.HasIndex(e => e.PublicOrderNumber).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.GuestEmail);
            entity.HasIndex(e => e.OrderStatus);
            entity.HasIndex(e => e.PaymentStatus);
            entity.HasIndex(e => e.FulfilmentStatus);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasOne(e => e.ShippingAddress).WithMany().HasForeignKey(e => e.ShippingAddressId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.BillingAddress).WithMany().HasForeignKey(e => e.BillingAddressId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ProductSlug).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Sku).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ColourName).HasMaxLength(100);
            entity.Property(e => e.ColourValue).HasMaxLength(50);
            entity.Property(e => e.SizeName).HasMaxLength(100);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => e.ProductVariantId);
            entity.HasOne(e => e.Order).WithMany(o => o.Items).HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderAddress>(entity =>
        {
            entity.ToTable("OrderAddresses");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RecipientName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Phone).HasMaxLength(30);
            entity.Property(e => e.AddressLine1).IsRequired().HasMaxLength(200);
            entity.Property(e => e.AddressLine2).HasMaxLength(200);
            entity.Property(e => e.Area).HasMaxLength(100);
            entity.Property(e => e.City).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Region).HasMaxLength(100);
            entity.Property(e => e.PostalCode).IsRequired().HasMaxLength(20);
            entity.Property(e => e.CountryCode).IsRequired().HasMaxLength(2);
            entity.Property(e => e.DeliveryInstructions).HasMaxLength(500);
        });

        modelBuilder.Entity<OrderStatusHistory>(entity =>
        {
            entity.ToTable("OrderStatusHistories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.CreatedBy).HasMaxLength(450);
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasOne(e => e.Order).WithMany(o => o.StatusHistory).HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderNote>(entity =>
        {
            entity.ToTable("OrderNotes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Note).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.CreatedBy).HasMaxLength(450);
            entity.HasIndex(e => e.OrderId);
            entity.HasOne(e => e.Order).WithMany(o => o.Notes).HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderNumberSequence>(entity =>
        {
            entity.ToTable("OrderNumberSequences");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Prefix).IsRequired().HasMaxLength(20);
            entity.HasIndex(e => new { e.Prefix, e.Year }).IsUnique();
        });

        modelBuilder.Entity<OrderIdempotencyRecord>(entity =>
        {
            entity.ToTable("OrderIdempotencyRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(128);
            entity.Property(e => e.UserId).HasMaxLength(450);
            entity.HasIndex(e => e.IdempotencyKey).IsUnique();
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.CreatedAtUtc);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProviderCode).IsRequired().HasMaxLength(50);
            entity.Property(e => e.PaymentMethodCode).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ProviderTransactionId).HasMaxLength(128);
            entity.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(3);
            entity.Property(e => e.FailureCode).HasMaxLength(50);
            entity.Property(e => e.FailureReason).HasMaxLength(500);
            entity.HasIndex(e => e.OrderId).IsUnique();
            entity.HasIndex(e => e.ProviderTransactionId);
            entity.HasIndex(e => e.IdempotencyKey).IsUnique();
            entity.HasIndex(e => e.State);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => e.ExpiresAtUtc);
            entity.HasOne(e => e.Order).WithOne().HasForeignKey<Payment>(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentAttempt>(entity =>
        {
            entity.ToTable("PaymentAttempts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProviderTransactionId).HasMaxLength(128);
            entity.Property(e => e.FailureReason).HasMaxLength(500);
            entity.HasIndex(e => e.PaymentId);
            entity.HasIndex(e => new { e.PaymentId, e.AttemptNumber });
            entity.HasOne(e => e.Payment).WithMany(p => p.Attempts).HasForeignKey(e => e.PaymentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.ToTable("PaymentTransactions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProviderCode).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ProviderTransactionId).HasMaxLength(128);
            entity.Property(e => e.ResultCode).HasMaxLength(50);
            entity.Property(e => e.ResultMessage).HasMaxLength(500);
            entity.HasIndex(e => e.PaymentId);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => e.Type);
            entity.HasOne(e => e.Payment).WithMany(p => p.Transactions).HasForeignKey(e => e.PaymentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentWebhookLog>(entity =>
        {
            entity.ToTable("PaymentWebhookLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProviderCode).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ProviderEventId).HasMaxLength(128);
            entity.Property(e => e.Signature).HasMaxLength(1024);
            entity.Property(e => e.FailureReason).HasMaxLength(500);
            entity.HasIndex(e => new { e.ProviderCode, e.ProviderEventId }).IsUnique();
            entity.HasIndex(e => e.PaymentId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ReceivedAtUtc);
            entity.HasOne(e => e.Payment).WithMany().HasForeignKey(e => e.PaymentId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PaymentRefundRecord>(entity =>
        {
            entity.ToTable("PaymentRefundRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(3);
            entity.Property(e => e.ProviderRefundId).HasMaxLength(128);
            entity.Property(e => e.FailureCode).HasMaxLength(50);
            entity.Property(e => e.FailureReason).HasMaxLength(500);
            entity.Property(e => e.InitiatedBy).HasMaxLength(200);
            entity.HasIndex(e => e.PaymentId);
            entity.HasIndex(e => e.ProviderRefundId);
            entity.HasOne(e => e.Payment).WithMany(p => p.Refunds).HasForeignKey(e => e.PaymentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("Invoices");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.InvoiceNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(3);
            entity.HasIndex(e => e.InvoiceNumber).IsUnique();
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.IssueDateUtc);
            entity.HasOne(e => e.Order).WithOne().HasForeignKey<Invoice>(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceSendLog>(entity =>
        {
            entity.ToTable("InvoiceSendLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SentTo).IsRequired().HasMaxLength(254);
            entity.Property(e => e.Subject).HasMaxLength(200);
            entity.Property(e => e.SentBy).HasMaxLength(200);
            entity.Property(e => e.ErrorMessage).HasMaxLength(500);
            entity.HasIndex(e => e.InvoiceId);
            entity.HasIndex(e => e.SentAtUtc);
            entity.HasOne(e => e.Invoice).WithMany(i => i.SendLogs).HasForeignKey(e => e.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceNumberSequence>(entity =>
        {
            entity.ToTable("InvoiceNumberSequences");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Prefix).IsRequired().HasMaxLength(20);
            entity.HasIndex(e => new { e.Prefix, e.Year }).IsUnique();
        });

        modelBuilder.Entity<ReturnRequest>(entity =>
        {
            entity.ToTable("ReturnRequests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ReturnNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.UserId).HasMaxLength(450);
            entity.Property(e => e.GuestEmail).HasMaxLength(254);
            entity.Property(e => e.CustomerName).HasMaxLength(200);
            entity.Property(e => e.GuestPhone).HasMaxLength(30);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(3);
            entity.Property(e => e.CustomerNotes).HasMaxLength(2000);
            entity.Property(e => e.TrackingNumber).HasMaxLength(100);
            entity.Property(e => e.CarrierCode).HasMaxLength(50);
            entity.Property(e => e.AdminNotes).HasMaxLength(1000);
            entity.Property(e => e.RejectionReasonCode).HasMaxLength(50);
            entity.Property(e => e.RejectionNote).HasMaxLength(1000);
            entity.HasIndex(e => e.ReturnNumber).IsUnique();
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.GuestEmail);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasOne(e => e.Order).WithMany(o => o.ReturnRequests).HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReturnItem>(entity =>
        {
            entity.ToTable("ReturnItems");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Sku).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ColourName).HasMaxLength(100);
            entity.Property(e => e.ColourValue).HasMaxLength(50);
            entity.Property(e => e.SizeName).HasMaxLength(100);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.Property(e => e.InspectionNote).HasMaxLength(1000);
            entity.HasIndex(e => e.ReturnRequestId);
            entity.HasIndex(e => e.OrderItemId);
            entity.HasIndex(e => e.ProductVariantId);
            entity.HasOne(e => e.ReturnRequest).WithMany(r => r.Items).HasForeignKey(e => e.ReturnRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReturnStatusHistory>(entity =>
        {
            entity.ToTable("ReturnStatusHistories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.CreatedBy).HasMaxLength(450);
            entity.HasIndex(e => e.ReturnRequestId);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasOne(e => e.ReturnRequest).WithMany(r => r.StatusHistory).HasForeignKey(e => e.ReturnRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExchangeRequest>(entity =>
        {
            entity.ToTable("ExchangeRequests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Sku).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.CompletedBy).HasMaxLength(450);
            entity.HasIndex(e => e.ReturnRequestId);
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.ProductVariantId);
            entity.HasIndex(e => e.Status);
            entity.HasOne(e => e.ReturnRequest).WithMany(r => r.ExchangeRequests).HasForeignKey(e => e.ReturnRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Refund>(entity =>
        {
            entity.ToTable("Refunds");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ReferenceNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(3);
            entity.Property(e => e.ProviderRefundId).HasMaxLength(128);
            entity.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(128);
            entity.Property(e => e.FailureCode).HasMaxLength(50);
            entity.Property(e => e.FailureReason).HasMaxLength(500);
            entity.Property(e => e.Reason).HasMaxLength(1000);
            entity.Property(e => e.InitiatedBy).HasMaxLength(450);
            entity.HasIndex(e => e.ReferenceNumber).IsUnique();
            entity.HasIndex(e => e.IdempotencyKey).IsUnique();
            entity.HasIndex(e => e.ReturnRequestId);
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ProviderRefundId);
            entity.HasOne(e => e.ReturnRequest).WithMany(r => r.Refunds).HasForeignKey(e => e.ReturnRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefundTransaction>(entity =>
        {
            entity.ToTable("RefundTransactions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ResultCode).HasMaxLength(50);
            entity.Property(e => e.ResultMessage).HasMaxLength(500);
            entity.Property(e => e.CreatedBy).HasMaxLength(450);
            entity.HasIndex(e => e.RefundId);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasOne(e => e.Refund).WithMany(r => r.Transactions).HasForeignKey(e => e.RefundId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReturnReason>(entity =>
        {
            entity.ToTable("ReturnReasons");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Label).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.SortOrder);
        });

        modelBuilder.Entity<ReturnAttachment>(entity =>
        {
            entity.ToTable("ReturnAttachments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.OriginalFileName).HasMaxLength(255);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.StoragePath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.UploadedBy).HasMaxLength(450);
            entity.HasIndex(e => e.ReturnRequestId);
            entity.HasOne(e => e.ReturnRequest).WithMany(r => r.Attachments).HasForeignKey(e => e.ReturnRequestId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailMessage>(entity =>
        {
            entity.ToTable("EmailMessages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ToEmail).IsRequired().HasMaxLength(254);
            entity.Property(e => e.RecipientName).HasMaxLength(200);
            entity.Property(e => e.Subject).HasMaxLength(500);
            entity.Property(e => e.BodyHtml).IsRequired();
            entity.Property(e => e.TemplateName).HasMaxLength(100);
            entity.Property(e => e.AttachmentKind).HasMaxLength(50);
            entity.Property(e => e.LastError).HasMaxLength(1000);
            entity.Property(e => e.DeduplicationKey).HasMaxLength(450);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.Status, e.NextAttemptAtUtc });
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => e.ToEmail);
            // Duplicate prevention backstop: at most one queued email per stable key.
            entity.HasIndex(e => e.DeduplicationKey)
                .IsUnique()
                .HasFilter("[DeduplicationKey] IS NOT NULL");
        });

        modelBuilder.Entity<ContentPage>(entity =>
        {
            entity.ToTable("ContentPages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Summary).HasMaxLength(500);
            entity.Property(e => e.MetaTitle).HasMaxLength(200);
            entity.Property(e => e.MetaDescription).HasMaxLength(500);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<Banner>(entity =>
        {
            entity.ToTable("Banners");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Title).HasMaxLength(300);
            entity.Property(e => e.Subtitle).HasMaxLength(500);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.Property(e => e.LinkUrl).HasMaxLength(500);
            entity.Property(e => e.LinkText).HasMaxLength(100);
            entity.Property(e => e.Style).HasMaxLength(50);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Placement);
        });

        modelBuilder.Entity<HomepageSection>(entity =>
        {
            entity.ToTable("HomepageSections");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SectionType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Subtitle).HasMaxLength(500);
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<NavigationMenu>(entity =>
        {
            entity.ToTable("NavigationMenus");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(300);
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<NavigationItem>(entity =>
        {
            entity.ToTable("NavigationItems");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Label).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Url).HasMaxLength(500);
            entity.Property(e => e.Target).HasMaxLength(10);
            entity.HasIndex(e => e.MenuId);
            entity.HasIndex(e => e.ParentId);
            entity.HasOne(e => e.Menu)
                .WithMany(m => m.Items)
                .HasForeignKey(e => e.MenuId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Parent)
                .WithMany(e => e.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FaqItem>(entity =>
        {
            entity.ToTable("FaqItems");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Question).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Answer).HasMaxLength(4000);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.HasIndex(e => e.IsActive);
        });

        modelBuilder.Entity<BlogPost>(entity =>
        {
            entity.ToTable("BlogPosts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Excerpt).HasMaxLength(500);
            entity.Property(e => e.CoverImageUrl).HasMaxLength(500);
            entity.Property(e => e.AuthorName).HasMaxLength(200);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<PolicyDocument>(entity =>
        {
            entity.ToTable("PolicyDocuments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Summary).HasMaxLength(500);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<SiteSetting>(entity =>
        {
            entity.ToTable("SiteSettings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Value).IsRequired();
            entity.Property(e => e.ValueType).HasMaxLength(50);
            entity.Property(e => e.Group).HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.HasIndex(e => e.Key).IsUnique();
        });

        modelBuilder.Entity<SlugRedirect>(entity =>
        {
            entity.ToTable("SlugRedirects");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OldSlug).IsRequired().HasMaxLength(200);
            entity.Property(e => e.NewSlug).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => new { e.EntityType, e.OldSlug }).IsUnique();
            entity.HasIndex(e => e.CreatedAtUtc);
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

        // PostgreSQL does not understand SQL Server's "datetime2" column type.
        // The SQL Server mapping stays untouched (DateTime maps to datetime2 by
        // default there), but when the model targets Npgsql every explicitly
        // configured datetime2 column is remapped to Postgres "timestamp with
        // time zone" so the generated Postgres schema is valid.
        if (Database.IsNpgsql())
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.GetColumnType() == "datetime2")
                    {
                        property.SetColumnType("timestamp with time zone");
                    }
                }
            }
        }
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
