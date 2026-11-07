using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>An opaque 8-bit grayscale pixel.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct Gray8 : IEquatable<Gray8>
{
    /// <summary>The gray level (0 is black, 255 is white), in the encoded (not linear) scale.</summary>
    public byte Value;

    /// <summary>Initializes a new instance of the <see cref="Gray8"/> struct.</summary>
    /// <param name="value">The gray level.</param>
    public Gray8(byte value) => Value = value;

    /// <inheritdoc />
    public readonly bool Equals(Gray8 other) => Value == other.Value;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is Gray8 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => Value;

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"Gray8({Value})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Gray8 left, Gray8 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Gray8 left, Gray8 right) => !left.Equals(right);
}
