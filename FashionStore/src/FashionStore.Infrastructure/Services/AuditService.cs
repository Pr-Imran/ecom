using FashionStore.Application.Interfaces;
using FashionStore.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace FashionStore.Infrastructure.Services;

/// <summary>
/// Writes immutable audit records for sensitive administrative actions. The actor
/// is resolved from the current request (authenticated user, IP address, user
/// agent) when an HTTP context is available, otherwise from the supplied actor id.
/// Values are truncated to the column limits so an oversized change can never fail
/// the audit write.
/// </summary>
public sealed class AuditService : IAuditService
{
    private const int ActionMaxLength = 100;
    private const int EntityTypeMaxLength = 100;
    private const int EntityIdMaxLength = 100;
    private const int ValueMaxLength = 1000;

    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditService(
        AppDbContext context,
        IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
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
        var resolvedActor = actorId ?? httpContext?.User.Identity?.Name ?? "system";

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
