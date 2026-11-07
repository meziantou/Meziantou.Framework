namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Represents the result of a media tag operation that does not return a value.
/// </summary>
/// <remarks>
/// The default value of this type is a failure carrying <see cref="MediaTagError.UnsupportedFormat"/>, so
/// <see cref="Error"/> is never <see langword="null"/> when <see cref="IsSuccess"/> is <see langword="false"/>.
/// </remarks>
public readonly struct MediaTagResult
{
    // Stored as a non-nullable field so that a default-initialized result is a valid failure rather than a
    // failure with no error, which would contradict the MemberNotNullWhen annotation on IsSuccess.
    private readonly MediaTagError _error;

    private MediaTagResult(bool isSuccess, MediaTagError error, string? errorMessage)
    {
        IsSuccess = isSuccess;
        _error = error;
        ErrorMessage = errorMessage;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    /// <summary>Gets the error that occurred, or <see langword="null"/> if the operation succeeded.</summary>
    public MediaTagError? Error => IsSuccess ? null : _error;

    /// <summary>Gets a human-readable error message, if the operation failed.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Creates a successful result.</summary>
    public static MediaTagResult Success() => new(isSuccess: true, error: default, errorMessage: null);

    /// <summary>Creates a failure result with the specified error.</summary>
    public static MediaTagResult Failure(MediaTagError error, string? message = null) => new(isSuccess: false, error: error, errorMessage: message);
}

/// <summary>
/// Represents the result of a media tag operation that returns a value of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of the result value.</typeparam>
/// <remarks>
/// The default value of this type is a failure carrying <see cref="MediaTagError.UnsupportedFormat"/>, so
/// <see cref="Error"/> is never <see langword="null"/> when <see cref="IsSuccess"/> is <see langword="false"/>.
/// </remarks>
public readonly struct MediaTagResult<T>
{
    private readonly T? _value;
    private readonly MediaTagError _error;

    private MediaTagResult(bool isSuccess, T? value, MediaTagError error, string? errorMessage)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
        ErrorMessage = errorMessage;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    /// <summary>Gets the result value. Throws if the operation failed.</summary>
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Cannot access Value on a failed result. Error: " + (ErrorMessage ?? _error.ToString()));

    /// <summary>Gets the error that occurred, or <see langword="null"/> if the operation succeeded.</summary>
    public MediaTagError? Error => IsSuccess ? null : _error;

    /// <summary>Gets a human-readable error message, if the operation failed.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Creates a successful result with the specified value.</summary>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types")]
    public static MediaTagResult<T> Success(T value) => new(isSuccess: true, value: value, error: default, errorMessage: null);

    /// <summary>Creates a failure result with the specified error.</summary>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types")]
    public static MediaTagResult<T> Failure(MediaTagError error, string? message = null) => new(isSuccess: false, value: default, error: error, errorMessage: message);
}
