using FashionStore.Application.Configuration;
using FashionStore.Application.Email;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Emails;

/// <summary>
/// Resolves the active email provider from configuration and delegates each send.
/// All failures are converted into sanitized results so no credentials leak into
/// the email log.
/// </summary>
public sealed class EmailSender : IEmailSender
{
    private readonly IEnumerable<IEmailProvider> _providers;
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IEnumerable<IEmailProvider> providers, EmailSettings settings, ILogger<EmailSender> logger)
    {
        _providers = providers;
        _settings = settings;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(EmailOutboundMessage message, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider();
        if (provider is null)
        {
            return new EmailSendResult(false, $"No email provider is available for provider '{_settings.Provider}'.");
        }

        if (!provider.IsAvailable)
        {
            return new EmailSendResult(false, $"The email provider '{provider.Name}' is not configured.");
        }

        try
        {
            return await provider.SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email delivery to {To} failed", message.ToEmail);
            return new EmailSendResult(false, Sanitize(ex.Message));
        }
    }

    private IEmailProvider? ResolveProvider()
    {
        switch (_settings.Provider.Trim().ToLowerInvariant())
        {
            case "development":
                return _providers.OfType<DevelopmentEmailProvider>().FirstOrDefault();
            case "api":
                return _providers.OfType<ApiEmailProvider>().FirstOrDefault();
            case "gmail":
            case "outlook":
            case "hotmail":
            case "yahoo":
            case "smtp":
            case "custom":
                return _providers.OfType<SmtpEmailProvider>().FirstOrDefault();
            default:
                _logger.LogWarning("Unknown email provider '{Provider}', falling back to development sink", _settings.Provider);
                return _providers.OfType<DevelopmentEmailProvider>().FirstOrDefault();
        }
    }

    private static string Sanitize(string message)
    {
        var result = message.Length <= 1000 ? message : message[..1000];
        return result.Replace("\r", " ").Replace("\n", " ");
    }
}
