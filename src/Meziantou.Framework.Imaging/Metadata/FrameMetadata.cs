namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>Mutable per-frame settings of a displayed frame.</summary>
public sealed class FrameMetadata
{
    /// <summary>Gets or sets how long the frame is displayed. Defaults to <see cref="FrameDuration.Zero"/>.</summary>
    /// <remarks>
    /// The exact source timing is preserved on import. Encoders convert it to their own representation according to
    /// their <see cref="Formats.FrameDurationRounding"/> setting.
    /// </remarks>
    public FrameDuration Duration { get; set; }

    /// <summary>Creates an independent copy of these settings.</summary>
    /// <returns>A new instance with the same values.</returns>
    public FrameMetadata Clone() => new() { Duration = Duration };
}
