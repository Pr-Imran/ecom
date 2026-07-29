namespace FashionStore.Web.Models;

public sealed class ErrorViewModel
{
    public string? RequestId { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public string? CorrelationId { get; init; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
