using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Represents the result of a media tag operation that does not return a value.
/// </summary>
public readonly struct MediaTagResult
{
    private MediaTagResult(bool isSuccess, MediaTagError? error, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorMessage = errorMessage;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    /// <summary>Gets the error that occurred, if the operation failed.</summary>
    public MediaTagError? Error { get; }

    /// <summary>Gets a human-readable error message, if the operation failed.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Creates a successful result.</summary>
    public static MediaTagResult Success() => new(isSuccess: true, error: null, errorMessage: null);

    /// <summary>Creates a failure result with the specified error.</summary>
    public static MediaTagResult Failure(MediaTagError error, string? message = null) => new(isSuccess: false, error: error, errorMessage: message);
}

/// <summary>
/// Represents the result of a media tag operation that returns a value of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of the result value.</typeparam>
public readonly struct MediaTagResult<T>
{
    private readonly T? _value;

    private MediaTagResult(bool isSuccess, T? value, MediaTagError? error, string? errorMessage)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
        ErrorMessage = errorMessage;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    /// <summary>Gets the result value. Throws if the operation failed.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Cannot access Value on a failed result. Error: " + ErrorMessage);

    /// <summary>Gets the error that occurred, if the operation failed.</summary>
    public MediaTagError? Error { get; }

    /// <summary>Gets a human-readable error message, if the operation failed.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Creates a successful result with the specified value.</summary>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types")]
    public static MediaTagResult<T> Success(T value) => new(isSuccess: true, value: value, error: null, errorMessage: null);

    /// <summary>Creates a failure result with the specified error.</summary>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types")]
    public static MediaTagResult<T> Failure(MediaTagError error, string? message = null) => new(isSuccess: false, value: default, error: error, errorMessage: message);
}
