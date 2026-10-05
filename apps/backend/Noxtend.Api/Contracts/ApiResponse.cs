namespace Noxtend.Api.Contracts;

/// <summary>
/// Design Ref: §4.0 — every endpoint answers with the same envelope, success or not.
///
/// A fixed shape means the client parses one thing. §10.4 forbids serializing entities,
/// so what goes into <c>data</c> is always a response DTO.
/// </summary>
public sealed record ApiResponse<T>(T? Data, ApiError? Error)
{
    public static ApiResponse<T> Ok(T data) => new(data, null);

    public static ApiResponse<T> Fail(string code, string message, IReadOnlyDictionary<string, string>? fields = null)
        => new(default, new ApiError(code, message, fields));
}

/// <summary>
/// Design Ref: §4.0 · §6 — codes are SCREAMING_SNAKE and stable; messages are for humans.
/// Provider-originated text never reaches this type verbatim (§4.2 #13): a key can be
/// embedded in an upstream error message.
/// </summary>
public sealed record ApiError(string Code, string Message, IReadOnlyDictionary<string, string>? Fields = null);
