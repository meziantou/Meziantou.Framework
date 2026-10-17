using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>An opaque pixel with 8-bit red, green and blue components, stored in RGB byte order.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct Rgb24 : IEquatable<Rgb24>
{
    /// <summary>The red component.</summary>
    public byte R;

    /// <summary>The green component.</summary>
    public byte G;

    /// <summary>The blue component.</summary>
    public byte B;

    /// <summary>Initializes a new instance of the <see cref="Rgb24"/> struct.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    public Rgb24(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    /// <inheritdoc />
    public readonly bool Equals(Rgb24 other) => R == other.R && G == other.G && B == other.B;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is Rgb24 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(R, G, B);

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"Rgb24({R}, {G}, {B})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Rgb24 left, Rgb24 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Rgb24 left, Rgb24 right) => !left.Equals(right);
}
