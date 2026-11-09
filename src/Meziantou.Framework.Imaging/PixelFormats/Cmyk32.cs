using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>An opaque pixel with 8-bit cyan, magenta, yellow and black ink amounts.</summary>
/// <remarks>
/// <para>
/// Each component is the amount of an ink: 0 is no ink and 255 is full coverage (paper white is <c>(0, 0, 0, 0)</c>).
/// Files that store the complement (Adobe CMYK JPEG files) are complemented when they are read and written.
/// </para>
/// <para>
/// The colors of the inks depend on the printing condition, described by the CMYK ICC profile of the image
/// (<see cref="Metadata.ImageMetadata.IccProfile"/>). Converting CMYK pixels to another pixel format converts their colors,
/// which needs <see cref="PixelConversionOptions.CmykConversion"/>.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct Cmyk32 : IEquatable<Cmyk32>
{
    /// <summary>The cyan ink.</summary>
    public byte C;

    /// <summary>The magenta ink.</summary>
    public byte M;

    /// <summary>The yellow ink.</summary>
    public byte Y;

    /// <summary>The black ink.</summary>
    public byte K;

    /// <summary>Initializes a new instance of the <see cref="Cmyk32"/> struct.</summary>
    /// <param name="c">The cyan ink.</param>
    /// <param name="m">The magenta ink.</param>
    /// <param name="y">The yellow ink.</param>
    /// <param name="k">The black ink.</param>
    public Cmyk32(byte c, byte m, byte y, byte k)
    {
        C = c;
        M = m;
        Y = y;
        K = k;
    }

    /// <inheritdoc />
    public readonly bool Equals(Cmyk32 other) => C == other.C && M == other.M && Y == other.Y && K == other.K;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is Cmyk32 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(C, M, Y, K);

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"Cmyk32({C}, {M}, {Y}, {K})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Cmyk32 left, Cmyk32 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Cmyk32 left, Cmyk32 right) => !left.Equals(right);
}
