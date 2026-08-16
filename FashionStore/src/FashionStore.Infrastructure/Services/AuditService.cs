using System.Security.Claims;
using FashionStore.Application.Interfaces;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Writes immutable audit records for sensitive administrative actions. The actor
/// is resolved from the current request (authenticated user ID, IP address, user
/// agent) when an HTTP context is available, otherwise from the supplied actor id.
/// Values are truncated to the column limits so an oversized change can never fail
/// the audit write. System-initiated actions that run without an authenticated
/// user (e.g. seeding) are logged as warnings rather than persisted, because the
/// audit trail always references a real user.
/// </summary>
public sealed class AuditService : IAuditService
{
    private const int ActionMaxLength = 100;
    private const int EntityTypeMaxLength = 100;
    private const int EntityIdMaxLength = 100;
    private const int ValueMaxLength = 1000;

    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        AppDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditService> logger)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task RecordAsync(
        string action,
        string entityType,
        string? entityId = null,
        string? oldValue = null,
        string? newValue = null,
        string? actorId = null,
        CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var resolvedActor = actorId ?? httpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(resolvedActor))
        {
            _logger.LogWarning(
                "Skipped audit record for {Action} on {EntityType} {EntityId}: no authenticated user",
                action,
                entityType,
                entityId);
            return;
        }

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = resolvedActor,
            Action = Truncate(action, ActionMaxLength),
            EntityType = Truncate(entityType, EntityTypeMaxLength),
            EntityId = Truncate(entityId, EntityIdMaxLength),
            OldValue = Truncate(oldValue, ValueMaxLength),
            NewValue = Truncate(newValue, ValueMaxLength),
            IpAddress = httpContext?.Connection?.RemoteIpAddress?.ToString() ?? string.Empty,
            UserAgent = httpContext?.Request?.Headers.UserAgent.ToString() ?? string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[..Math.Min(value.Length, maxLength)];
}
