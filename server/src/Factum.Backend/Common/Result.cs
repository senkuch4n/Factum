namespace Factum.Backend.Common;

/// <summary>
/// Tipo de error de un <see cref="Result{T}"/>, para que los controladores mapeen el código
/// HTTP sin mirar el texto del mensaje. <see cref="Failure"/> es el default de
/// <c>Result.Fail(string)</c> (usuarios previos de Result, sin cambios). Los tres últimos
/// (subida-archivos-grandes §5.4) van al final para no cambiar los valores existentes.
/// </summary>
public enum ErrorKind { Failure, Validation, NotFound, Forbidden, Conflict, PayloadTooLarge, InsufficientStorage, ServerError }

public sealed record Result<T>
{
    public T? Value { get; }
    public string? Error { get; }
    public bool IsSuccess { get; }
    public ErrorKind Kind { get; }
    /// <summary>Claves de obligatorios faltantes (solo en errores de validación).</summary>
    public IReadOnlyList<string>? Missing { get; }
    /// <summary>
    /// Campos extra del cuerpo de error (claves ya en snake_case literal, p. ej. <c>code</c>,
    /// <c>size</c>). <c>null</c> en todos los errores previos: su JSON no cambia.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Details { get; }

    private Result(T value) { Value = value; IsSuccess = true; }
    private Result(ErrorKind kind, string error, IReadOnlyList<string>? missing,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        Kind = kind;
        Error = error;
        Missing = missing;
        Details = details;
        IsSuccess = false;
    }

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(string error) => new(ErrorKind.Failure, error, null);
    public static Result<T> Fail(ErrorKind kind, string error, IReadOnlyList<string>? missing = null) =>
        new(kind, error, missing);
    public static Result<T> Fail(ErrorKind kind, string error, IReadOnlyDictionary<string, object?> details) =>
        new(kind, error, null, details);

    /// <summary>Propaga el error (incluidos <see cref="Missing"/> y <see cref="Details"/>) a un Result de otro tipo.</summary>
    public Result<TOut> Cast<TOut>() => Result<TOut>.FailWith(Kind, Error ?? "", Missing, Details);

    internal static Result<T> FailWith(ErrorKind kind, string error, IReadOnlyList<string>? missing,
        IReadOnlyDictionary<string, object?>? details) => new(kind, error, missing, details);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value!) : onFailure(Error!);
}

public static class Result
{
    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);
    public static Result<T> Fail<T>(string error) => Result<T>.Fail(error);
    public static Result<T> Fail<T>(ErrorKind kind, string error, IReadOnlyList<string>? missing = null) =>
        Result<T>.Fail(kind, error, missing);
    public static Result<T> Fail<T>(ErrorKind kind, string error, IReadOnlyDictionary<string, object?> details) =>
        Result<T>.Fail(kind, error, details);
    public static Result<T> NotFound<T>(string error = "Caso no encontrado") =>
        Result<T>.Fail(ErrorKind.NotFound, error);
    public static Result<T> Forbidden<T>(string error = "Acceso denegado") =>
        Result<T>.Fail(ErrorKind.Forbidden, error);
    public static Result<T> Conflict<T>(string error) => Result<T>.Fail(ErrorKind.Conflict, error);
    public static Result<T> Invalid<T>(string error, IReadOnlyList<string>? missing = null) =>
        Result<T>.Fail(ErrorKind.Validation, error, missing);
}
