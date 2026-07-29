namespace FashionStore.Application.Common.Models;

public sealed class PaginatedRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    public int Skip => (Page - 1) * PageSize;

    public PaginatedRequest()
    {
    }

    public PaginatedRequest(int page, int pageSize)
    {
        Page = page < 1 ? 1 : page;
        PageSize = pageSize < 1 ? 20 : pageSize > 100 ? 100 : pageSize;
    }
}
