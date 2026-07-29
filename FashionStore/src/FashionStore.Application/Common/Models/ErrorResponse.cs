namespace FashionStore.Application.Common.Models;

public sealed class ErrorResponse
{
    public string ErrorCode { get; }
    public string Message { get; }
    public string? CorrelationId { get; }
    public string? Details { get; }
    public DateTime OccurredAtUtc { get; }

    public ErrorResponse(string errorCode, string message, string? correlationId = null, string? details = null)
    {
        ErrorCode = errorCode;
        Message = message;
        CorrelationId = correlationId;
        Details = details;
        OccurredAtUtc = DateTime.UtcNow;
    }
}
