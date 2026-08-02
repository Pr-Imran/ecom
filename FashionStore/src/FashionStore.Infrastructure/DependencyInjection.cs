using FashionStore.Application.Configuration;
using FashionStore.Application.Interfaces;
using FashionStore.Infrastructure.Data;
using FashionStore.Infrastructure.Services;
using FashionStore.Infrastructure.Services.Images;
using FashionStore.Infrastructure.Services.Storage;
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

        services.AddSingleton(cacheSettings);
        services.AddSingleton(fileStorageSettings);
        services.AddSingleton(imageSettings);
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
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IImageValidationService, ImageValidationService>();
        services.AddScoped<IImageProcessingService, ImageProcessingService>();
        services.AddScoped<IImageService, ImageService>();
        services.AddSingleton<IImageProcessingDispatcher, ImageProcessingDispatcher>();
        services.AddHostedService<ImageProcessingBackgroundService>();
        services.AddAuthorization();
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database");

        return services;
    }
}