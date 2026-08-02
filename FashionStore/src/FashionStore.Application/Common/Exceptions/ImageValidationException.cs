namespace FashionStore.Application.Common.Exceptions;

public sealed class ImageValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ImageValidationException(IEnumerable<string> errors)
        : base(string.Join("; ", errors))
    {
        Errors = errors.ToList();
    }

    public ImageValidationException(string message)
        : base(message)
    {
        Errors = new[] { message };
    }
}
