namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Controls how an encoder converts exact <see cref="FrameDuration"/> values to the timing representation of its format.</summary>
/// <remarks>Durations outside the range representable by the format always throw; they are never clamped.</remarks>
public enum FrameDurationRounding
{
    /// <summary>Throws an <see cref="UnsupportedImageFeatureException"/> if a duration cannot be represented exactly.</summary>
    RequireExact = 0,

    /// <summary>
    /// Rounds each duration to the nearest representable value (ties round up): the nearest hundredth for GIF, the nearest
    /// millisecond for WebP, the nearest sixtieth of a second for ANI, the nearest fraction with 16-bit numerator and
    /// denominator for APNG.
    /// </summary>
    RoundToNearest = 1,
}
