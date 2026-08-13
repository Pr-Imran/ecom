using FashionStore.Application.Configuration;
using FashionStore.Application.Email;
using FashionStore.Infrastructure.Emails;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FashionStore.UnitTests.Emails;

public class EmailSenderTests
{
    private static EmailSender CreateSender(
        EmailSettings settings,
        IEmailProvider[]? providers = null)
    {
        var all = providers ?? new IEmailProvider[]
        {
            new DevelopmentEmailProvider(NullLogger<DevelopmentEmailProvider>.Instance),
            new SmtpEmailProvider(settings, NullLogger<SmtpEmailProvider>.Instance),
            new ApiEmailProvider(NullLogger<ApiEmailProvider>.Instance)
        };

        return new EmailSender(all, settings, NullLogger<EmailSender>.Instance);
    }

    private static EmailOutboundMessage Message() =>
        new("jane@example.com", "Subject", "<html/>");

    [Fact]
    public async Task Send_DevelopmentProvider_Default_Succeeds()
    {
        var sender = CreateSender(new EmailSettings { Provider = "Development" });
        var result = await sender.SendAsync(Message(), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Null(result.SanitizedError);
    }

    [Fact]
    public async Task Send_UnknownProvider_FallsBackToDevelopment()
    {
        var sender = CreateSender(new EmailSettings { Provider = "SomeFutureProvider" });
        var result = await sender.SendAsync(Message(), CancellationToken.None);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Send_ApiProvider_ReportsNotConfigured()
    {
        var sender = CreateSender(new EmailSettings { Provider = "Api" });
        var result = await sender.SendAsync(Message(), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("not configured", result.SanitizedError);
    }

    [Fact]
    public async Task Send_CustomProviderWithoutHost_ReportsNotConfigured()
    {
        var settings = new EmailSettings { Provider = "Custom", SmtpHost = "" };
        var sender = CreateSender(settings);
        var result = await sender.SendAsync(Message(), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("not configured", result.SanitizedError);
    }

    [Fact]
    public async Task Send_SmtpProviderWithoutHost_ReportsNotConfigured()
    {
        var settings = new EmailSettings { Provider = "smtp", SmtpHost = "" };
        var sender = CreateSender(settings);
        var result = await sender.SendAsync(Message(), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("not configured", result.SanitizedError);
    }

    [Fact]
    public async Task Send_NoProvidersRegistered_ReportsNoProvider()
    {
        var sender = CreateSender(new EmailSettings { Provider = "Development" }, Array.Empty<IEmailProvider>());
        var result = await sender.SendAsync(Message(), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("No email provider", result.SanitizedError);
    }
}
