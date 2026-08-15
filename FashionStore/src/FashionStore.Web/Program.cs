using FashionStore.Application;
using FashionStore.Application.Configuration;
using FashionStore.Application.Interfaces;
using FashionStore.Infrastructure;
using FashionStore.Infrastructure.BackgroundJobs;
using FashionStore.Web.Infrastructure;
using FashionStore.Web.Middleware;
using Hangfire;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
        .WriteTo.File(
            path: "Logs/fashionstore-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {CorrelationId} {Message:lj} {Properties:j}{NewLine}{Exception}");
});

builder.Services.Configure<DatabaseSettings>(
    builder.Configuration.GetSection(DatabaseSettings.SectionName));
builder.Services.Configure<StoreSettings>(
    builder.Configuration.GetSection(StoreSettings.SectionName));
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection(EmailSettings.SectionName));
builder.Services.AddSingleton(
    builder.Configuration.GetSection(EmailSettings.SectionName).Get<EmailSettings>() ?? new EmailSettings());
builder.Services.Configure<FileStorageSettings>(
    builder.Configuration.GetSection(FileStorageSettings.SectionName));
builder.Services.Configure<ImageSettings>(
    builder.Configuration.GetSection(ImageSettings.SectionName));
builder.Services.Configure<CloudFileStorageSettings>(
    builder.Configuration.GetSection(CloudFileStorageSettings.SectionName));
builder.Services.Configure<PaymentSettings>(
    builder.Configuration.GetSection(PaymentSettings.SectionName));
builder.Services.Configure<InvoiceSettings>(
    builder.Configuration.GetSection(InvoiceSettings.SectionName));
builder.Services.Configure<CheckoutSettings>(
    builder.Configuration.GetSection(CheckoutSettings.SectionName));
builder.Services.Configure<TaxSettings>(
    builder.Configuration.GetSection(TaxSettings.SectionName));
builder.Services.Configure<OrderSettings>(
    builder.Configuration.GetSection(OrderSettings.SectionName));
builder.Services.Configure<ReturnSettings>(
    builder.Configuration.GetSection(ReturnSettings.SectionName));
builder.Services.Configure<ReviewSettings>(
    builder.Configuration.GetSection(ReviewSettings.SectionName));
builder.Services.Configure<SecuritySettings>(
    builder.Configuration.GetSection(SecuritySettings.SectionName));
builder.Services.Configure<BackgroundJobSettings>(
    builder.Configuration.GetSection(BackgroundJobSettings.SectionName));
builder.Services.Configure<HomePageSettings>(
    builder.Configuration.GetSection(HomePageSettings.SectionName));
builder.Services.Configure<AdminReportSettings>(
    builder.Configuration.GetSection(AdminReportSettings.SectionName));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllersWithViews();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});

builder.Services.AddOutputCache();

builder.Services.Configure<RazorViewEngineOptions>(options =>
{
    options.ViewLocationExpanders.Add(new AdminPagesViewLocationExpander());
});

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FashionStore API",
        Version = "v1",
        Description = "FashionStore E-Commerce API"
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // A real global limiter (not a dead named policy): every request shares a
    // generous per-connection budget so a single runaway client cannot saturate
    // the server, while normal browsing is unaffected.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddFixedWindowLimiter("global", options =>
    {
        options.PermitLimit = 100;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("login", options =>
    {
        options.PermitLimit = 5;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("register", options =>
    {
        options.PermitLimit = 5;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("passwordreset", options =>
    {
        options.PermitLimit = 3;
        options.Window = TimeSpan.FromMinutes(5);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 0;
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        // Honour the limiter's actual retry-after window instead of a hardcoded 60s.
        int retryAfterSeconds = 60;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
        }

        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests. Please try again later.", retryAfterSeconds }, token);
    };
});

var app = builder.Build();

// Fail fast on missing security configuration outside Development. Placeholder
// secrets are stripped from the checked-in configuration, so any secret left
// unset here means webhook forgery or guest-ticket forgery is possible; the app
// must not start until the operator provides real values.
if (!app.Environment.IsDevelopment())
{
    var securityIssues = new List<string>();

    var paymentSettings = app.Configuration.GetSection(PaymentSettings.SectionName).Get<PaymentSettings>();
    if (paymentSettings is not null)
    {
        foreach (var provider in paymentSettings.Providers.Where(p => p.IsEnabled))
        {
            if (string.IsNullOrWhiteSpace(provider.WebhookSecret) ||
                provider.WebhookSecret.StartsWith("dev-", StringComparison.OrdinalIgnoreCase))
            {
                securityIssues.Add($"Payments provider '{provider.ProviderCode}' is enabled but has no webhook secret configured (set Payments:Providers:{provider.ProviderCode}:WebhookSecret).");
            }
        }
    }

    var guestSecret = app.Configuration["Order:GuestAccessTokenSecret"];
    if (string.IsNullOrWhiteSpace(guestSecret) || guestSecret.StartsWith("dev-", StringComparison.OrdinalIgnoreCase))
    {
        securityIssues.Add("Order:GuestAccessTokenSecret is not configured (set it to a strong random value).");
    }

    var continuationSecret = app.Configuration["Checkout:ContinuationTokenSecret"];
    if (string.IsNullOrWhiteSpace(continuationSecret) || continuationSecret.StartsWith("dev-", StringComparison.OrdinalIgnoreCase))
    {
        securityIssues.Add("Checkout:ContinuationTokenSecret is not configured (set it to a strong random value).");
    }

    var emailBaseUrl = app.Configuration["Email:BaseUrl"];
    if (string.IsNullOrWhiteSpace(emailBaseUrl) ||
        emailBaseUrl.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase))
    {
        securityIssues.Add("Email:BaseUrl is not configured (set it to the production site origin so account links are correct).");
    }

    if (securityIssues.Count > 0)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogCritical("Security configuration is incomplete; refusing to start. {Issues}", string.Join(Environment.NewLine, securityIssues));
        throw new InvalidOperationException(
            "Security configuration is incomplete. Fix the following before starting:\n" +
            string.Join("\n", securityIssues));
    }
}

app.UseRequestCorrelation();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "FashionStore API v1");
    });
}

app.UseGlobalExceptionHandling();

app.UseResponseCompression();

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseOutputCache();
app.UseAuthorization();

app.UseMiddleware<MaintenanceModeMiddleware>();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = static context =>
    {
        var path = context.Context.Request.Path.Value ?? string.Empty;
        var hasVersionHash = context.Context.Request.QueryString.HasValue;
        var cacheControl = hasVersionHash
            ? "public, max-age=31536000, immutable"
            : path.StartsWith("/uploads", StringComparison.OrdinalIgnoreCase)
                ? "public, max-age=86400"
                : "public, max-age=3600";
        context.Context.Response.Headers.CacheControl = cacheControl;
    }
});

var uploadsBasePath = Path.Combine(app.Environment.ContentRootPath, builder.Configuration["FileStorage:BasePath"] ?? "uploads");
Directory.CreateDirectory(uploadsBasePath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsBasePath),
    RequestPath = builder.Configuration["FileStorage:PublicUrlBase"] ?? "/uploads"
});

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireDashboardAuthorizationFilter() },
    StatsPollingInterval = 5000
});

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var inventorySettings = scope.ServiceProvider.GetRequiredService<InventorySettings>();
    var backgroundJobSettings = scope.ServiceProvider.GetRequiredService<BackgroundJobSettings>();
    try
    {
        RecurringJob.AddOrUpdate<IInventoryService>(
            "inventory-release-expired-reservations",
            service => service.ReleaseExpiredReservationsAsync(CancellationToken.None),
            inventorySettings.ExpiredReservationReleaseCron);

#pragma warning disable CS0618
        RecurringJob.AddOrUpdate<SendQueuedEmailsJob>(
            "email-process-outbox",
            job => job.ExecuteAsync(CancellationToken.None),
            backgroundJobSettings.EmailQueueCron,
            new RecurringJobOptions { QueueName = "email" });

        RecurringJob.AddOrUpdate<ExpireUnpaidOrdersJob>(
            "email-expire-unpaid-orders",
            job => job.ExecuteAsync(CancellationToken.None),
            backgroundJobSettings.ExpireUnpaidOrdersCron,
            new RecurringJobOptions { QueueName = "email" });

        RecurringJob.AddOrUpdate<ScheduledPromotionsJob>(
            "promotions-apply-schedule",
            job => job.ExecuteAsync(CancellationToken.None),
            backgroundJobSettings.ScheduledPromotionsCron,
            new RecurringJobOptions { QueueName = "default" });

        RecurringJob.AddOrUpdate<LowStockAlertJob>(
            "inventory-low-stock-alert",
            job => job.ExecuteAsync(CancellationToken.None),
            backgroundJobSettings.LowStockAlertCron,
            new RecurringJobOptions { QueueName = "email" });

        RecurringJob.AddOrUpdate<CleanupTemporaryUploadsJob>(
            "storage-cleanup-temporary-uploads",
            job => job.ExecuteAsync(CancellationToken.None),
            backgroundJobSettings.CleanupTemporaryUploadsCron,
            new RecurringJobOptions { QueueName = "cleanup" });
#pragma warning restore CS0618
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Hangfire recurring job could not be registered. Background storage may be unavailable.");
    }
}

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
