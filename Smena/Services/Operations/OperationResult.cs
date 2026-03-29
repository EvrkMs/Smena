namespace Host.Services.Operations;

public readonly record struct OperationResult(bool Success, string Message)
{
    public static OperationResult Ok(string message = "") => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}
