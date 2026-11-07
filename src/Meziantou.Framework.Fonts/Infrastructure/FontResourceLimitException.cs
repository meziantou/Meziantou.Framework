namespace Meziantou.Framework.Fonts;

/// <summary>The exception thrown when an operation would exceed a limit of <see cref="FontResourceLimits"/> or of a <see cref="FontAllocator"/>.</summary>
#pragma warning disable CA1032 // The exceptions are created by the font code only
public sealed class FontResourceLimitException : FontException
{
    internal FontResourceLimitException(FontResourceLimitKind kind, long limit, long? requested)
        : base(requested is null
            ? string.Create(CultureInfo.InvariantCulture, $"The resource limit '{kind}' ({limit}) was exceeded.")
            : string.Create(CultureInfo.InvariantCulture, $"The resource limit '{kind}' ({limit}) was exceeded: {requested} requested."))
    {
        Kind = kind;
        Limit = limit;
        Requested = requested;
    }

    /// <summary>Gets the exceeded limit.</summary>
    public FontResourceLimitKind Kind { get; }

    /// <summary>Gets the value of the exceeded limit.</summary>
    public long Limit { get; }

    /// <summary>Gets the value that exceeded the limit, or <see langword="null"/> if unknown.</summary>
    public long? Requested { get; }
}
#pragma warning restore CA1032
