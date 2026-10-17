namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>Mutable animation-wide settings of an animated image.</summary>
/// <remarks>
/// Animation settings are structural data, not optional metadata: stripping metadata on save never removes them.
/// Per-frame timing is stored in <see cref="FrameMetadata.Duration"/>.
/// </remarks>
public sealed class AnimationMetadata
{
    /// <summary>
    /// Gets or sets the total number of times the animation is played, including the first play, or <see langword="null"/>
    /// for an infinite loop. Defaults to <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Codecs translate this value to their own conventions: APNG <c>num_plays</c> stores it directly (0 meaning infinite),
    /// while the GIF NETSCAPE2.0 extension stores a number of <em>repetitions</em> after the first play.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public int? TotalPlays
    {
        get;
        set
        {
            if (value is not null)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value.Value);
            }

            field = value;
        }
    }

    /// <summary>Creates an independent copy of these settings.</summary>
    /// <returns>A new instance with the same values.</returns>
    public AnimationMetadata Clone() => new() { TotalPlays = TotalPlays };
}
