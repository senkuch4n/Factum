namespace Factum.Backend.Common;

/// <summary>
/// Tipo de error de un <see cref="Result{T}"/>, para que los controladores mapeen el código
/// HTTP sin mirar el texto del mensaje. <see cref="Failure"/> es el default de
/// <c>Result.Fail(string)</c> (usuarios previos de Result, sin cambios).
/// </summary>
public enum ErrorKind { Failure, Validation, NotFound, Forbidden, Conflict }

public sealed record Result<T>
{
    public T? Value { get; }
    public string? Error { get; }
    public bool IsSuccess { get; }
    public ErrorKind Kind { get; }
    /// <summary>Claves de obligatorios faltantes (solo en errores de validación).</summary>
    public IReadOnlyList<string>? Missing { get; }

    private Result(T value) { Value = value; IsSuccess = true; }
    private Result(ErrorKind kind, string error, IReadOnlyList<string>? missing)
    {
        Kind = kind;
        Error = error;
        Missing = missing;
        IsSuccess = false;
    }

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(string error) => new(ErrorKind.Failure, error, null);
    public static Result<T> Fail(ErrorKind kind, string error, IReadOnlyList<string>? missing = null) =>
        new(kind, error, missing);

    /// <summary>Propaga el error a un Result de otro tipo.</summary>
    public Result<TOut> Cast<TOut>() => Result<TOut>.Fail(Kind, Error ?? "", Missing);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value!) : onFailure(Error!);
}

public static class Result
{
    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);
    public static Result<T> Fail<T>(string error) => Result<T>.Fail(error);
    public static Result<T> Fail<T>(ErrorKind kind, string error, IReadOnlyList<string>? missing = null) =>
        Result<T>.Fail(kind, error, missing);
    public static Result<T> NotFound<T>(string error = "Caso no encontrado") =>
        Result<T>.Fail(ErrorKind.NotFound, error);
    public static Result<T> Forbidden<T>(string error = "Acceso denegado") =>
        Result<T>.Fail(ErrorKind.Forbidden, error);
    public static Result<T> Conflict<T>(string error) => Result<T>.Fail(ErrorKind.Conflict, error);
    public static Result<T> Invalid<T>(string error, IReadOnlyList<string>? missing = null) =>
        Result<T>.Fail(ErrorKind.Validation, error, missing);
}
