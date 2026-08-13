using FashionStore.Application.Configuration;
using FashionStore.Application.Email;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Emails;

/// <summary>
/// Contract for a concrete email transport. This is the future API-provider
/// abstraction: SendGrid-style HTTP providers implement it the same way the
/// development sink and SMTP providers do, and the sender resolves whichever is
/// active without callers knowing the transport.
/// </summary>
public interface IEmailProvider
{
    /// <summary>Stable identifier, e.g. <c>Development</c>, <c>Smtp</c>, <c>Api</c>.</summary>
    string Name { get; }

    /// <summary>Whether the provider is configured and ready to send.</summary>
    bool IsAvailable { get; }

    Task<EmailSendResult> SendAsync(EmailOutboundMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes the message to the application log instead of a real server. The default
/// provider for development so no SMTP account is required to run the store, and
/// the inbox can be inspected in the log or a local MailHog instance.
/// </summary>
public sealed class DevelopmentEmailProvider : IEmailProvider
{
    public const string ProviderName = "Development";

    private readonly ILogger<DevelopmentEmailProvider> _logger;

    public DevelopmentEmailProvider(ILogger<DevelopmentEmailProvider> logger)
    {
        _logger = logger;
    }

    public string Name => ProviderName;

    public bool IsAvailable => true;

    public Task<EmailSendResult> SendAsync(EmailOutboundMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("[Email:Development] To: {To} | Subject: {Subject}", message.ToEmail, message.Subject);
        _logger.LogInformation("[Email:Development] Body: {Body}", message.BodyHtml);
        return Task.FromResult(new EmailSendResult(true));
    }
}

/// <summary>
/// Sends through SMTP using System.Net.Mail. The server is derived from the
/// configured provider preset (Gmail, Outlook, Hotmail, Yahoo) or a custom
/// host/port/TLS block for an own-domain mail server. Failures are returned as
/// sanitized messages that never include credentials.
/// </summary>
public sealed class SmtpEmailProvider : IEmailProvider
{
    public const string ProviderName = "Smtp";

    private readonly EmailSettings _settings;
    private readonly ILogger<SmtpEmailProvider> _logger;

    public SmtpEmailProvider(EmailSettings settings, ILogger<SmtpEmailProvider> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public string Name => ProviderName;

    public bool IsAvailable => !string.IsNullOrEmpty(ResolveServer(_settings).Host);

    public async Task<EmailSendResult> SendAsync(EmailOutboundMessage message, CancellationToken cancellationToken = default)
    {
        var (host, port, useSsl) = ResolveServer(_settings);

        if (string.IsNullOrWhiteSpace(host))
        {
            return new EmailSendResult(false, "No SMTP server is configured.");
        }

        try
        {
            using var client = new System.Net.Mail.SmtpClient(host, port);
            if (!string.IsNullOrEmpty(_settings.SmtpUsername) || !string.IsNullOrEmpty(_settings.SmtpPassword))
            {
                client.Credentials = new System.Net.NetworkCredential(_settings.SmtpUsername, _settings.SmtpPassword);
            }

            client.EnableSsl = useSsl;
            client.Timeout = 30000;

            var from = new System.Net.Mail.MailAddress(_settings.FromAddress, _settings.FromName);
            var to = new System.Net.Mail.MailAddress(message.ToEmail);

            using var mail = new System.Net.Mail.MailMessage(from, to)
            {
                Subject = message.Subject,
                Body = message.BodyHtml,
                IsBodyHtml = true
            };

            if (!string.IsNullOrEmpty(message.AttachmentFileName) && message.AttachmentBytes is { Length: > 0 })
            {
                mail.Attachments.Add(new System.Net.Mail.Attachment(
                    new MemoryStream(message.AttachmentBytes),
                    message.AttachmentFileName,
                    message.AttachmentContentType ?? "application/octet-stream"));
            }

            await client.SendMailAsync(mail, cancellationToken);
            _logger.LogInformation("Email sent to {To} via SMTP ({Host}:{Port})", message.ToEmail, host, port);
            return new EmailSendResult(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var sanitized = SanitizeError(ex.Message);
            _logger.LogWarning("SMTP delivery to {To} failed: {Error}", message.ToEmail, sanitized);
            return new EmailSendResult(false, sanitized);
        }
    }

    /// <summary>
    /// Resolves the SMTP endpoint for the active provider preset. Presets fill in
    /// the well-known server; <c>Custom</c> and the legacy blocks let the operator
    /// provide their own host, port and TLS settings.
    /// </summary>
    internal static (string Host, int Port, bool UseSsl) ResolveServer(EmailSettings settings)
    {
        switch (settings.Provider.Trim().ToLowerInvariant())
        {
            case "gmail":
                return ("smtp.gmail.com", 587, true);
            case "outlook":
                return ("smtp.office365.com", 587, true);
            case "hotmail":
                return ("smtp-mail.outlook.com", 587, true);
            case "yahoo":
                return ("smtp.mail.yahoo.com", 465, true);
            case "smtp":
            case "custom":
                if (!string.IsNullOrWhiteSpace(settings.SmtpHost))
                {
                    return (settings.SmtpHost, settings.SmtpPort, settings.UseSsl);
                }

                if (!string.IsNullOrWhiteSpace(settings.PrimarySmtpHost))
                {
                    return (settings.PrimarySmtpHost, settings.PrimarySmtpPort, settings.PrimaryUseSsl);
                }

                if (!string.IsNullOrWhiteSpace(settings.FallbackSmtpHost))
                {
                    return (settings.FallbackSmtpHost, settings.FallbackSmtpPort, settings.FallbackUseSsl);
                }

                return (string.Empty, settings.SmtpPort, settings.UseSsl);
            default:
                if (!string.IsNullOrWhiteSpace(settings.SmtpHost))
                {
                    return (settings.SmtpHost, settings.SmtpPort, settings.UseSsl);
                }

                if (!string.IsNullOrWhiteSpace(settings.PrimarySmtpHost))
                {
                    return (settings.PrimarySmtpHost, settings.PrimarySmtpPort, settings.PrimaryUseSsl);
                }

                if (!string.IsNullOrWhiteSpace(settings.FallbackSmtpHost))
                {
                    return (settings.FallbackSmtpHost, settings.FallbackSmtpPort, settings.FallbackUseSsl);
                }

                return (string.Empty, settings.SmtpPort, settings.UseSsl);
        }
    }

    private string SanitizeError(string message)
    {
        var result = message;
        if (!string.IsNullOrEmpty(_settings.SmtpUsername))
        {
            result = result.Replace(_settings.SmtpUsername, "***", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(_settings.SmtpPassword))
        {
            result = result.Replace(_settings.SmtpPassword, "***", StringComparison.OrdinalIgnoreCase);
        }

        return result.Length <= 1000 ? result : result[..1000];
    }
}

/// <summary>
/// Placeholder for a future HTTP-based API provider (SendGrid-style). The class
/// exists so the provider seam is exercised end to end; configuring
/// <c>Email:Provider=Api</c> reports a clear, safe failure until a concrete API
/// provider is implemented.
/// </summary>
public sealed class ApiEmailProvider : IEmailProvider
{
    public const string ProviderName = "Api";

    private readonly ILogger<ApiEmailProvider> _logger;

    public ApiEmailProvider(ILogger<ApiEmailProvider> logger)
    {
        _logger = logger;
    }

    public string Name => ProviderName;

    public bool IsAvailable => false;

    public Task<EmailSendResult> SendAsync(EmailOutboundMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogWarning("The Api email provider is not configured; message to {To} was not sent", message.ToEmail);
        return Task.FromResult(new EmailSendResult(false, "The API email provider is not configured."));
    }
}
