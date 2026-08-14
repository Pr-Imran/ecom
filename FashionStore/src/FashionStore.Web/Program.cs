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
    options.AddFixedWindowLimiter("global", options =>
    {
        options.PermitLimit = 100;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 10;
    });

    options.AddFixedWindowLimiter("login", options =>
    {
        options.PermitLimit = 5;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 10;
    });

    options.AddFixedWindowLimiter("passwordreset", options =>
    {
        options.PermitLimit = 3;
        options.Window = TimeSpan.FromMinutes(5);
        options.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 5;
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests. Please try again later.", retryAfterSeconds = 60 }, token);
    };
});

var app = builder.Build();

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

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthorization();

app.UseMiddleware<MaintenanceModeMiddleware>();

app.UseStaticFiles();

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
