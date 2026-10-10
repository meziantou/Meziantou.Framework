namespace Meziantou.Framework.Imaging;

/// <summary>The exception thrown when an operation would exceed a configured <see cref="ImageResourceLimits"/> value.</summary>
/// <remarks>Exceeding a limit never produces a silently truncated result.</remarks>
public class ImageResourceLimitException : ImageException
{
    /// <summary>Initializes a new instance of the <see cref="ImageResourceLimitException"/> class.</summary>
    public ImageResourceLimitException()
        : base("A resource limit was exceeded.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ImageResourceLimitException"/> class with a message.</summary>
    /// <param name="message">The error message.</param>
    public ImageResourceLimitException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ImageResourceLimitException"/> class with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public ImageResourceLimitException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ImageResourceLimitException"/> class with structured details.</summary>
    /// <param name="kind">The exceeded limit.</param>
    /// <param name="limit">The configured value of the limit.</param>
    /// <param name="requested">The value that was requested, if known.</param>
    public ImageResourceLimitException(ImageResourceLimitKind kind, long limit, long? requested)
        : base(CreateMessage(kind, limit, requested))
    {
        Kind = kind;
        Limit = limit;
        Requested = requested;
    }

    /// <summary>Gets the exceeded limit.</summary>
    public ImageResourceLimitKind Kind { get; }

    /// <summary>Gets the configured value of the exceeded limit, or 0 if not specified.</summary>
    public long Limit { get; }

    /// <summary>Gets the requested value that exceeded the limit, or <see langword="null"/> if unknown (for example when a cumulative counter crossed the limit).</summary>
    public long? Requested { get; }

    private static string CreateMessage(ImageResourceLimitKind kind, long limit, long? requested)
    {
        return requested is null
            ? string.Create(CultureInfo.InvariantCulture, $"The resource limit '{kind}' ({limit}) was exceeded.")
            : string.Create(CultureInfo.InvariantCulture, $"The resource limit '{kind}' ({limit}) was exceeded: {requested} requested.");
    }
}
