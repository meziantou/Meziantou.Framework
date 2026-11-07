using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// The Butteraugli distance between two frames (<see cref="ImageSimilarity.ComputeButteraugliDistance"/>): the two values
/// that libjxl's <c>butteraugli_main</c> prints.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct ButteraugliDistance : IEquatable<ButteraugliDistance>
{
    /// <summary>Initializes a new instance of the <see cref="ButteraugliDistance"/> struct.</summary>
    /// <param name="maxDistance">The largest distance of the map.</param>
    /// <param name="pNorm3">The 3-norm of the map.</param>
    public ButteraugliDistance(double maxDistance, double pNorm3)
    {
        MaxDistance = maxDistance;
        PNorm3 = pNorm3;
    }

    /// <summary>
    /// Gets the largest distance of the map, the value usually called "the Butteraugli distance": <c>0</c> for identical
    /// pixels, below about <c>1</c> when the difference is not visible, and larger values for more visible differences.
    /// </summary>
    /// <remarks>It is the distance of the worst pixel, so a difference confined to a small area weighs fully.</remarks>
    public double MaxDistance { get; }

    /// <summary>
    /// Gets the 3-norm of the map (the mean of its 3-, 6- and 12-norms): a summary of the whole frame, smaller than
    /// <see cref="MaxDistance"/>, that a localized difference raises less.
    /// </summary>
    public double PNorm3 { get; }

    /// <inheritdoc />
    public bool Equals(ButteraugliDistance other) => MaxDistance.Equals(other.MaxDistance) && PNorm3.Equals(other.PNorm3);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ButteraugliDistance other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(MaxDistance, PNorm3);

    /// <summary>Returns the distance formatted as <c>{MaxDistance} (3-norm: {PNorm3})</c>.</summary>
    /// <returns>A string representation of the distance.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{MaxDistance} (3-norm: {PNorm3})");

    /// <summary>Compares two distances for equality.</summary>
    /// <param name="left">The first distance.</param>
    /// <param name="right">The second distance.</param>
    /// <returns><see langword="true"/> if both distances are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(ButteraugliDistance left, ButteraugliDistance right) => left.Equals(right);

    /// <summary>Compares two distances for inequality.</summary>
    /// <param name="left">The first distance.</param>
    /// <param name="right">The second distance.</param>
    /// <returns><see langword="true"/> if the distances differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(ButteraugliDistance left, ButteraugliDistance right) => !left.Equals(right);
}
