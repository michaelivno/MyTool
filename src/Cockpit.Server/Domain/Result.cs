using Cockpit.Shared;

namespace Cockpit.Server.Domain;

/// <summary>Outcome of a service operation: a value, or an ApiError with the HTTP status to use.</summary>
public sealed class Result<T>
{
    public T? Value { get; private init; }
    public ApiError? Error { get; private init; }
    public int StatusCode { get; private init; } = 200;
    public bool Ok => Error is null;

    public static Result<T> Success(T value, int status = 200) => new() { Value = value, StatusCode = status };
    public static Result<T> Fail(ApiError error, int status) => new() { Error = error, StatusCode = status };
    public static Result<T> NotFound(string what = "Item") => Fail(new ApiError { Code = ApiError.NotFound, Message = $"{what} not found." }, 404);
    public static Result<T> Invalid(string message) => Fail(new ApiError { Code = ApiError.Validation, Message = message }, 400);

    public Result<TOut> Cast<TOut>() => Result<TOut>.Fail(Error!, StatusCode);
}
