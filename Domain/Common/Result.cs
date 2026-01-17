namespace Domain.Common;

public record Result<TData> : Result
{
    public TData? Data { get; init; }

    public static Result<TData> Ok(TData data, string message = "")
        => new()
        {
            IsSuccess = true,
            Data = data,
            Message = message
        };

    public static Result<TData> Fail(string message)
        => new()
        {
            IsSuccess = false,
            Message = message
        };
}

public record Result
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;

    public static Result Ok()
        => new()
        {
            IsSuccess = true,
        };

    public static Result Fail(string message)
        => new()
        {
            IsSuccess = false,
            Message = message
        };
}