namespace Meziantou.Framework;

/// <summary>
/// Specifies options for controlling which operations are allowed on a <see cref="RestrictedStream"/>.
/// </summary>
public sealed record RestrictedStreamOptions
{
    /// <summary>Gets or sets a value indicating whether synchronous operations are allowed on the stream.</summary>
    public bool AllowSynchronousCalls { get; set; }

    /// <summary>Gets or sets a value indicating whether asynchronous operations are allowed on the stream.</summary>
    public bool AllowAsynchronousCalls { get; set; }

    /// <summary>Gets or sets a value indicating whether reading operations are allowed on the stream.</summary>
    public bool AllowReading { get; set; }

    /// <summary>Gets or sets a value indicating whether writing operations are allowed on the stream.</summary>
    public bool AllowWriting { get; set; }

    /// <summary>Gets or sets a value indicating whether seeking operations are allowed on the stream.</summary>
    public bool AllowSeeking { get; set; }
}
