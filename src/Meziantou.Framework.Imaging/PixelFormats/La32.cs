using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>A pixel with a 16-bit gray level and a 16-bit straight (non-premultiplied) alpha component, stored in native endianness.</summary>
/// <remarks>It holds a 16-bit grayscale image with transparency in half the memory of <see cref="Rgba64"/>.</remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct La32 : IEquatable<La32>
{
    /// <summary>The gray level (0 is black, 65535 is white), in the encoded (not linear) scale.</summary>
    public ushort L;

    /// <summary>The straight alpha component (0 is fully transparent, 65535 is fully opaque).</summary>
    public ushort A;

    /// <summary>Initializes a new instance of the <see cref="La32"/> struct.</summary>
    /// <param name="l">The gray level.</param>
    /// <param name="a">The straight alpha component. Defaults to fully opaque.</param>
    public La32(ushort l, ushort a = ushort.MaxValue)
    {
        L = l;
        A = a;
    }

    /// <inheritdoc />
    public readonly bool Equals(La32 other) => L == other.L && A == other.A;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is La32 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => L | (A << 16);

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"La32({L}, {A})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(La32 left, La32 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(La32 left, La32 right) => !left.Equals(right);
}
