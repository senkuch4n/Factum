namespace Factum.Backend.Common;

public sealed record Result<T>
{
    public T? Value { get; }
    public string? Error { get; }
    public bool IsSuccess { get; }

    private Result(T value) { Value = value; IsSuccess = true; }
    private Result(string error) { Error = error; IsSuccess = false; }

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(string error) => new(error);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value!) : onFailure(Error!);
}

public static class Result
{
    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);
    public static Result<T> Fail<T>(string error) => Result<T>.Fail(error);
}
