using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// A pixel with single-precision floating-point red, green, blue and straight (non-premultiplied) alpha components, for
/// scene-referred and high dynamic range data.
/// </summary>
/// <remarks>
/// <para>
/// The color components are not limited to [0, 1]: 1 is the reference white of a standard dynamic range, values above it
/// are brighter, and negative values are colors outside the gamut of the primaries. Alpha is a coverage from 0 (fully
/// transparent) to 1 (fully opaque). A new image of this pixel type is labeled as linear light
/// (<see cref="Metadata.ColorTransferFunction.Linear"/>); a conversion keeps the label of its source, since it never
/// converts colors.
/// </para>
/// <para>
/// Converting to an integer pixel format rejects samples that are not numbers, and samples outside [0, 1] unless
/// <see cref="PixelConversionOptions.OutOfRangeSamples"/> or <see cref="PixelConversionOptions.ToneMapping"/> says how to
/// bring them into range.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct RgbaFloat : IEquatable<RgbaFloat>
{
    /// <summary>The red component.</summary>
    public float R;

    /// <summary>The green component.</summary>
    public float G;

    /// <summary>The blue component.</summary>
    public float B;

    /// <summary>The straight alpha component (0 is fully transparent, 1 is fully opaque).</summary>
    public float A;

    /// <summary>Initializes a new instance of the <see cref="RgbaFloat"/> struct.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The straight alpha component. Defaults to fully opaque.</param>
    public RgbaFloat(float r, float g, float b, float a = 1f)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <inheritdoc />
    /// <remarks>Components are compared with <see cref="float.Equals(float)"/>: a component that is not a number equals itself.</remarks>
    public readonly bool Equals(RgbaFloat other) => R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is RgbaFloat other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(R, G, B, A);

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"RgbaFloat({R}, {G}, {B}, {A})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(RgbaFloat left, RgbaFloat right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(RgbaFloat left, RgbaFloat right) => !left.Equals(right);
}
