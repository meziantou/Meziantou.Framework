using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>An opaque grayscale pixel with a half-precision floating-point gray level, in 2 bytes per pixel.</summary>
/// <remarks>
/// The values have the meaning of those of <see cref="GrayFloat"/>, with the range and the precision of <see cref="Half"/>
/// (11 significant bits, finite values up to 65,504). Components are stored in native endianness.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct GrayHalf : IEquatable<GrayHalf>
{
    /// <summary>The gray level.</summary>
    public Half Value;

    /// <summary>Initializes a new instance of the <see cref="GrayHalf"/> struct.</summary>
    /// <param name="value">The gray level.</param>
    public GrayHalf(Half value) => Value = value;

    /// <inheritdoc />
    /// <remarks>Values are compared with <see cref="Half.Equals(Half)"/>: a value that is not a number equals itself.</remarks>
    public readonly bool Equals(GrayHalf other) => Value.Equals(other.Value);

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is GrayHalf other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"GrayHalf({Value})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(GrayHalf left, GrayHalf right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(GrayHalf left, GrayHalf right) => !left.Equals(right);
}
