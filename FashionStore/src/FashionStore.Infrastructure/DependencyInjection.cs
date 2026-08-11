using FashionStore.Application.Authorization;
using FashionStore.Application.Configuration;
using FashionStore.Application.Interfaces;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Invoicing;
using FashionStore.Infrastructure.Payments;
using FashionStore.Infrastructure.Services;
using FashionStore.Infrastructure.Services.Images;
using FashionStore.Infrastructure.Services.Storage;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FashionStore.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var dbSettings = configuration
            .GetSection(DatabaseSettings.SectionName)
            .Get<DatabaseSettings>() ?? new DatabaseSettings();

        var securitySettings = configuration
            .GetSection(SecuritySettings.SectionName)
            .Get<SecuritySettings>() ?? new SecuritySettings();

        var cacheSettings = configuration
            .GetSection(CacheSettings.SectionName)
            .Get<CacheSettings>() ?? new CacheSettings();

        var fileStorageSettings = configuration
            .GetSection(FileStorageSettings.SectionName)
            .Get<FileStorageSettings>() ?? new FileStorageSettings();

        var imageSettings = configuration
            .GetSection(ImageSettings.SectionName)
            .Get<ImageSettings>() ?? new ImageSettings();

        var backgroundJobSettings = configuration
            .GetSection(BackgroundJobSettings.SectionName)
            .Get<BackgroundJobSettings>() ?? new BackgroundJobSettings();

        var inventorySettings = configuration
            .GetSection(InventorySettings.SectionName)
            .Get<InventorySettings>() ?? new InventorySettings();

        var homePageSettings = configuration
            .GetSection(HomePageSettings.SectionName)
            .Get<HomePageSettings>() ?? new HomePageSettings();

        services.AddSingleton(cacheSettings);
        services.AddSingleton(fileStorageSettings);
        services.AddSingleton(imageSettings);
        services.AddSingleton(backgroundJobSettings);
        services.AddSingleton(inventorySettings);
        services.AddSingleton(homePageSettings);
        services.AddDistributedMemoryCache();

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(
                dbSettings.ConnectionString,
                sqlOptions =>
                {
                    sqlOptions.CommandTimeout(dbSettings.CommandTimeoutSeconds);
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: dbSettings.MaxRetryCount,
                        maxRetryDelay: TimeSpan.FromSeconds(dbSettings.MaxRetryDelaySeconds),
                        errorNumbersToAdd: null);
                });
        });

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
        {
            options.Password.RequireDigit = securitySettings.PasswordRequireDigit;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = securitySettings.PasswordRequireUppercase;
            options.Password.RequireNonAlphanumeric = securitySettings.PasswordRequireNonAlphanumeric;
            options.Password.RequiredLength = securitySettings.PasswordRequiredLength;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(securitySettings.LockoutDurationMinutes);
            options.Lockout.MaxFailedAccessAttempts = securitySettings.MaxFailedLoginAttempts;
            options.Lockout.AllowedForNewUsers = true;
            options.User.RequireUniqueEmail = true;
            options.SignIn.RequireConfirmedEmail = securitySettings.RequireEmailConfirmation;
            options.SignIn.RequireConfirmedPhoneNumber = false;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders()
        .AddClaimsPrincipalFactory<ApplicationClaimsPrincipalFactory>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = ".FashionStore.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(1);
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";
        });

        services.AddScoped<IRoleSeeder, RoleSeeder>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<FashionStore.Application.Services.INavigationService, NavigationService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IProductVariationService, ProductVariationService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IHomePageService, HomePageService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IProductDetailsService, ProductDetailsService>();
        services.AddScoped<IAddToCartService, AddToCartService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IDiscountService, DiscountService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<IShippingService, ShippingService>();
        services.AddScoped<IShippingCalculationService, ShippingCalculationService>();
        services.AddScoped<ICheckoutCalculationService, CheckoutCalculationService>();
        services.AddScoped<IOrderService, OrderPlacementService>();
        services.AddScoped<ICustomerOrderService, CustomerOrderService>();
        services.AddScoped<IOrderAdministrationService, OrderAdministrationService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IInvoicePdfGenerator, QuestPdfInvoiceGenerator>();
        services.AddScoped<ICustomerReturnService, CustomerReturnService>();
        services.AddScoped<IAdminReturnService, AdminReturnService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IAdminReviewService, AdminReviewService>();
        services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IAddressService, AddressService>();
        services.AddScoped<IAddressValidationService, AddressValidationService>();
        services.AddScoped<ICountryAddressValidator, DefaultCountryAddressValidator>();
        services.AddScoped<ICountryAddressValidator, UnitedStatesAddressValidator>();
        services.AddScoped<ICountryAddressValidator, UnitedKingdomAddressValidator>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IImageValidationService, ImageValidationService>();
        services.AddScoped<IImageProcessingService, ImageProcessingService>();
        services.AddScoped<IImageService, ImageService>();
        services.AddSingleton<IImageProcessingDispatcher, ImageProcessingDispatcher>();
        services.AddHostedService<ImageProcessingBackgroundService>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(InventoryPolicies.InventoryManage, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Products.ManageInventory));
            options.AddPolicy(ShippingPolicies.ShippingManage, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Shipping.Manage));
            options.AddPolicy(OrderPolicies.OrdersView, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Orders.View));
            options.AddPolicy(OrderPolicies.OrdersUpdateStatus, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Orders.UpdateStatus));
            options.AddPolicy(OrderPolicies.OrdersCancel, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Orders.Cancel));
            options.AddPolicy(OrderPolicies.OrdersAddNote, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Orders.AddNote));
            options.AddPolicy(OrderPolicies.OrdersPrintInvoice, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Orders.PrintInvoice));
            options.AddPolicy(ReturnPolicies.ReturnsView, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Returns.View));
            options.AddPolicy(ReturnPolicies.ReturnsReview, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Returns.Review));
            options.AddPolicy(ReturnPolicies.ReturnsInspect, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Returns.Inspect));
            options.AddPolicy(ReturnPolicies.ReturnsRestock, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Returns.Restock));
            options.AddPolicy(ReturnPolicies.ReturnsRefund, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Returns.Refund));
            options.AddPolicy(ReturnPolicies.ReturnsExchange, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Returns.Exchange));
            options.AddPolicy(ReturnPolicies.ReturnsComplete, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Returns.Complete));
            options.AddPolicy(ReviewPolicies.ReviewsManage, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim("permission", ApplicationPermissions.Reviews.Manage));
        });
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(dbSettings.ConnectionString, new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.FromSeconds(15),
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true
            }));

        if (backgroundJobSettings.Enabled)
        {
            services.AddHangfireServer(options =>
            {
                options.ServerName = backgroundJobSettings.ServerName;
                options.WorkerCount = backgroundJobSettings.WorkerCount;
                options.Queues = backgroundJobSettings.Queues;
            });
        }

        return services;
    }
}