namespace FashionStore.Application.DTOs.Navigation;

public sealed record CategoryItem(string Name, string Url);

public sealed record HeaderViewModel(
    string? UserDisplayName = null,
    string? UserEmail = null,
    int CartItemCount = 0,
    string CartTotal = "$0.00",
    bool IsAdmin = false,
    IEnumerable<CategoryItem>? Categories = null,
    IEnumerable<Announcement>? Announcements = null
);
