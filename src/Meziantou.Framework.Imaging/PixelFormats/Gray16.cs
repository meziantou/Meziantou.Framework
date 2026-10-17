using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>An opaque 16-bit grayscale pixel, stored in native endianness.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct Gray16 : IEquatable<Gray16>
{
    /// <summary>The gray level (0 is black, 65535 is white), in the encoded (not linear) scale.</summary>
    public ushort Value;

    /// <summary>Initializes a new instance of the <see cref="Gray16"/> struct.</summary>
    /// <param name="value">The gray level.</param>
    public Gray16(ushort value) => Value = value;

    /// <inheritdoc />
    public readonly bool Equals(Gray16 other) => Value == other.Value;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is Gray16 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => Value;

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"Gray16({Value})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Gray16 left, Gray16 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Gray16 left, Gray16 right) => !left.Equals(right);
}
