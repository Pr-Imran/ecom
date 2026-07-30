using FashionStore.Application.Configuration;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

public interface IEmailService
{
    Task<bool> SendEmailAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default);
    Task SendConfirmationEmailAsync(string email, string userId, string token, CancellationToken cancellationToken = default);
    Task SendPasswordResetEmailAsync(string email, string token, CancellationToken cancellationToken = default);
    Task SendWelcomeEmailAsync(string email, string name, CancellationToken cancellationToken = default);
}

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(EmailSettings settings, ILogger<EmailService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<bool> SendEmailAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail) || !toEmail.Contains("@"))
            return false;

        // Try primary SMTP first
        if (!string.IsNullOrEmpty(_settings.PrimarySmtpHost))
        {
            var primaryConfig = new SmtpConfig(
                _settings.PrimarySmtpHost,
                _settings.PrimarySmtpPort,
                _settings.PrimarySmtpUsername,
                _settings.PrimarySmtpPassword,
                _settings.PrimaryUseSsl,
                _settings.FromAddress,
                _settings.FromName
            );

            try
            {
                await SendViaSmtpAsync(primaryConfig, toEmail, subject, body, cancellationToken);
                _logger.LogInformation("Email sent to {Email} via primary SMTP", toEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Primary SMTP failed for {Email}, trying fallback", toEmail);
            }
        }

        // Try fallback SMTP
        if (!string.IsNullOrEmpty(_settings.FallbackSmtpHost))
        {
            var fallbackConfig = new SmtpConfig(
                _settings.FallbackSmtpHost,
                _settings.FallbackSmtpPort,
                _settings.FallbackSmtpUsername,
                _settings.FallbackSmtpPassword,
                _settings.FallbackUseSsl,
                _settings.FromAddress,
                _settings.FromName
            );

            try
            {
                await SendViaSmtpAsync(fallbackConfig, toEmail, subject, body, cancellationToken);
                _logger.LogInformation("Email sent to {Email} via fallback SMTP", toEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback SMTP failed for {Email}", toEmail);
            }
        }

        // Legacy single SMTP as last resort
        if (!string.IsNullOrEmpty(_settings.SmtpHost))
        {
            var legacyConfig = new SmtpConfig(
                _settings.SmtpHost,
                _settings.SmtpPort,
                _settings.SmtpUsername,
                _settings.SmtpPassword,
                _settings.UseSsl,
                _settings.FromAddress,
                _settings.FromName
            );

            try
            {
                await SendViaSmtpAsync(legacyConfig, toEmail, subject, body, cancellationToken);
                _logger.LogInformation("Email sent to {Email} via legacy SMTP", toEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "All SMTP providers failed for {Email}", toEmail);
            }
        }
        else
        {
            _logger.LogError("No SMTP configuration available");
        }

        return false;
    }

    private async Task SendViaSmtpAsync(SmtpConfig config, string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        using var client = new System.Net.Mail.SmtpClient(config.Host, config.Port);
        client.Credentials = new System.Net.NetworkCredential(config.Username, config.Password);
        client.EnableSsl = config.UseSsl;
        client.Timeout = 30000;

        var fromAddress = new System.Net.Mail.MailAddress(config.FromAddress, config.FromName);
        var toAddress = new System.Net.Mail.MailAddress(toEmail);
        var message = new System.Net.Mail.MailMessage(fromAddress, toAddress)
        {
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };

        await client.SendMailAsync(message, cancellationToken);
    }

    public async Task SendConfirmationEmailAsync(string email, string userId, string token, CancellationToken cancellationToken = default)
    {
        var confirmationLink = $"/Account/ConfirmEmail?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";
        var body = $@"
            <h2>Welcome to FashionStore!</h2>
            <p>Thank you for registering. Please confirm your email by clicking the button below:</p>
            <p><a href='{confirmationLink}' style='display: inline-block; padding: 12px 24px; background-color: #8B4513; color: white; text-decoration: none; border-radius: 4px;'>Confirm Email</a></p>
            <p>Or copy and paste this link:<br/><a href='{confirmationLink}'>{confirmationLink}</a></p>
            <p>If you didn't create an account, please ignore this email.</p>
            <p><strong>Link expires in 24 hours.</strong></p>
        ";

        await SendEmailAsync(email, "Confirm Your FashionStore Account", body, cancellationToken);
    }

    public async Task SendPasswordResetEmailAsync(string email, string token, CancellationToken cancellationToken = default)
    {
        var resetLink = $"/Account/ResetPassword?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
        var body = $@"
            <h2>Password Reset Request</h2>
            <p>You requested to reset your password. Click the button below to proceed:</p>
            <p><a href='{resetLink}' style='display: inline-block; padding: 12px 24px; background-color: #8B4513; color: white; text-decoration: none; border-radius: 4px;'>Reset Password</a></p>
            <p>Or copy and paste this link:<br/><a href='{resetLink}'>{resetLink}</a></p>
            <p>If you didn't request this, please ignore this email and your password will remain unchanged.</p>
            <p><strong>Link expires in 1 hour.</strong></p>
        ";

        await SendEmailAsync(email, "Reset Your Password - FashionStore", body, cancellationToken);
    }

    public async Task SendWelcomeEmailAsync(string email, string name, CancellationToken cancellationToken = default)
    {
        var body = $@"
            <h2>Welcome to FashionStore, {name}!</h2>
            <p>Thank you for joining FashionStore. We're excited to have you on board!</p>
            <p>Explore our latest collections, enjoy exclusive offers, and experience fashion like never before.</p>
            <p><a href='/' style='display: inline-block; padding: 12px 24px; background-color: #8B4513; color: white; text-decoration: none; border-radius: 4px;'>Start Shopping</a></p>
        ";

        await SendEmailAsync(email, "Welcome to FashionStore!", body, cancellationToken);
    }
}

internal sealed record SmtpConfig(
    string Host,
    int Port,
    string Username,
    string Password,
    bool UseSsl,
    string FromAddress,
    string FromName
);
