using System.Runtime.InteropServices;

namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Represents ReplayGain loudness normalization values.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct ReplayGainInfo : IEquatable<ReplayGainInfo>
{
    /// <summary>Gets the track gain in dB.</summary>
    public double? TrackGain { get; init; }

    /// <summary>Gets the track peak value.</summary>
    public double? TrackPeak { get; init; }

    /// <summary>Gets the album gain in dB.</summary>
    public double? AlbumGain { get; init; }

    /// <summary>Gets the album peak value.</summary>
    public double? AlbumPeak { get; init; }

    /// <inheritdoc/>
    public bool Equals(ReplayGainInfo other)
        => TrackGain == other.TrackGain && TrackPeak == other.TrackPeak
        && AlbumGain == other.AlbumGain && AlbumPeak == other.AlbumPeak;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ReplayGainInfo other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(TrackGain, TrackPeak, AlbumGain, AlbumPeak);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(ReplayGainInfo left, ReplayGainInfo right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(ReplayGainInfo left, ReplayGainInfo right) => !left.Equals(right);
}
