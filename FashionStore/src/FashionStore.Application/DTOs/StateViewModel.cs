namespace FashionStore.Application.DTOs;

public sealed record StateViewModel(
    string Type = "empty",
    string Title = "No Data",
    string Message = "There's nothing to show here yet.",
    string? Details = null,
    ActionLink? PrimaryAction = null,
    ActionLink? SecondaryAction = null
);

public sealed record ActionLink(
    string Label,
    string Url
);
