using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>An opaque pixel with 16-bit red, green and blue components, stored in native endianness.</summary>
/// <remarks>It holds a 16-bit color image without transparency in three quarters of the memory of <see cref="Rgba64"/>.</remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct Rgb48 : IEquatable<Rgb48>
{
    /// <summary>The red component.</summary>
    public ushort R;

    /// <summary>The green component.</summary>
    public ushort G;

    /// <summary>The blue component.</summary>
    public ushort B;

    /// <summary>Initializes a new instance of the <see cref="Rgb48"/> struct.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    public Rgb48(ushort r, ushort g, ushort b)
    {
        R = r;
        G = g;
        B = b;
    }

    /// <inheritdoc />
    public readonly bool Equals(Rgb48 other) => R == other.R && G == other.G && B == other.B;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is Rgb48 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(R, G, B);

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"Rgb48({R}, {G}, {B})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Rgb48 left, Rgb48 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Rgb48 left, Rgb48 right) => !left.Equals(right);
}
