using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>A pixel with an 8-bit gray level and an 8-bit straight (non-premultiplied) alpha component, stored in that byte order.</summary>
/// <remarks>It holds a grayscale image with transparency in half the memory of <see cref="Rgba32"/>.</remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct La16 : IEquatable<La16>
{
    /// <summary>The gray level (0 is black, 255 is white), in the encoded (not linear) scale.</summary>
    public byte L;

    /// <summary>The straight alpha component (0 is fully transparent, 255 is fully opaque).</summary>
    public byte A;

    /// <summary>Initializes a new instance of the <see cref="La16"/> struct.</summary>
    /// <param name="l">The gray level.</param>
    /// <param name="a">The straight alpha component. Defaults to fully opaque.</param>
    public La16(byte l, byte a = byte.MaxValue)
    {
        L = l;
        A = a;
    }

    /// <inheritdoc />
    public readonly bool Equals(La16 other) => L == other.L && A == other.A;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is La16 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => L | (A << 8);

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"La16({L}, {A})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(La16 left, La16 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(La16 left, La16 right) => !left.Equals(right);
}
