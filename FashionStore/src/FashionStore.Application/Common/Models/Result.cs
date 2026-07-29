namespace FashionStore.Application.Common.Models;

public sealed class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    private Result(bool isSuccess, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static Result Success() => new(true, null, null);
    public static Result Failure(string errorCode, string errorMessage) => new(false, errorCode, errorMessage);

    public static Result Failure(string errorMessage) => new(false, "ERROR", errorMessage);
}

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    private Result(T? value, bool isSuccess, string? errorCode, string? errorMessage)
    {
        Value = value;
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static Result<T> Success(T value) => new(value, true, null, null);
    public static Result<T> Failure(string errorCode, string errorMessage) => new(default, false, errorCode, errorMessage);
    public static Result<T> Failure(string errorMessage) => new(default, false, "ERROR", errorMessage);
}
