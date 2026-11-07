namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// The parameters of one channel of a gain map (ISO 21496-1, the <c>hdrgm</c> XMP namespace of Adobe and Ultra HDR): how
/// the samples of the gain map image become the gain that turns the base image into the alternate image.
/// </summary>
/// <remarks>
/// For a sample <c>g</c> of the gain map in [0, 1], the gain in stops is
/// <c>lerp(GainMapMin, GainMapMax, pow(g, 1 / Gamma))</c>, and an alternate color is
/// <c>(base + BaseOffset) * exp2(gain * weight) - AlternateOffset</c> in linear light, where the weight depends on the
/// headroom of the display (<see cref="GainMapMetadata.GetWeight(double)"/>).
/// </remarks>
public sealed class GainMapChannel : IEquatable<GainMapChannel>
{
    /// <summary>Initializes a new instance of the <see cref="GainMapChannel"/> class.</summary>
    /// <param name="gainMapMin">The gain of a sample of 0, in stops (log2 of the ratio of the alternate color to the base color).</param>
    /// <param name="gainMapMax">The gain of a sample of 1, in stops; at least <paramref name="gainMapMin"/>.</param>
    /// <param name="gamma">The exponent the samples were encoded with; greater than 0.</param>
    /// <param name="baseOffset">The offset added to the linear base color before the gain applies.</param>
    /// <param name="alternateOffset">The offset subtracted from the color once the gain applied.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is not finite, <paramref name="gamma"/> is not greater than 0, or <paramref name="gainMapMax"/> is less than <paramref name="gainMapMin"/>.</exception>
    public GainMapChannel(double gainMapMin, double gainMapMax, double gamma, double baseOffset, double alternateOffset)
    {
        EnsureFinite(gainMapMin, nameof(gainMapMin));
        EnsureFinite(gainMapMax, nameof(gainMapMax));
        EnsureFinite(gamma, nameof(gamma));
        EnsureFinite(baseOffset, nameof(baseOffset));
        EnsureFinite(alternateOffset, nameof(alternateOffset));
        if (gamma <= 0)
            throw new ArgumentOutOfRangeException(nameof(gamma), gamma, "The gamma must be greater than 0.");

        if (gainMapMax < gainMapMin)
            throw new ArgumentOutOfRangeException(nameof(gainMapMax), gainMapMax, "The maximum gain must be at least the minimum gain.");

        GainMapMin = gainMapMin;
        GainMapMax = gainMapMax;
        Gamma = gamma;
        BaseOffset = baseOffset;
        AlternateOffset = alternateOffset;
    }

    /// <summary>Gets the gain of a sample of 0, in stops (log2 of the ratio of the alternate color to the base color).</summary>
    public double GainMapMin { get; }

    /// <summary>Gets the gain of a sample of 1, in stops.</summary>
    public double GainMapMax { get; }

    /// <summary>Gets the exponent the samples were encoded with.</summary>
    public double Gamma { get; }

    /// <summary>Gets the offset added to the linear base color before the gain applies.</summary>
    public double BaseOffset { get; }

    /// <summary>Gets the offset subtracted from the color once the gain applied.</summary>
    public double AlternateOffset { get; }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] GainMapChannel? other)
    {
        return other is not null
            && GainMapMin.Equals(other.GainMapMin)
            && GainMapMax.Equals(other.GainMapMax)
            && Gamma.Equals(other.Gamma)
            && BaseOffset.Equals(other.BaseOffset)
            && AlternateOffset.Equals(other.AlternateOffset);
    }

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as GainMapChannel);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GainMapMin, GainMapMax, Gamma, BaseOffset, AlternateOffset);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"gain {GainMapMin} to {GainMapMax} stops, gamma {Gamma}, offsets {BaseOffset} and {AlternateOffset}");

    private static void EnsureFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be a finite number.");
    }
}
