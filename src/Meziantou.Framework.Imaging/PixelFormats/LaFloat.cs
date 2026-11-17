using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>A pixel with a single-precision floating-point gray level and a straight (non-premultiplied) alpha component, in 8 bytes per pixel.</summary>
/// <remarks>
/// <para>
/// The gray level has the meaning of the colors of <see cref="RgbaFloat"/>: linear light where 1 is the reference white,
/// with values outside [0, 1] allowed (highlights above 1). Alpha is a coverage from 0 (fully transparent) to 1 (fully opaque). A new image of this pixel type is labeled as linear
/// light (<see cref="Metadata.ColorTransferFunction.Linear"/>), and the components are stored in native endianness.
/// </para>
/// <para>
/// Converting a color to it gives the Rec. 709 luma of the color, <c>0.2126 R + 0.7152 G + 0.0722 B</c> (the luma of a
/// gray color is that gray), and converting it to a color format repeats the gray level in the three colors. Converting it
/// to an integer pixel format follows the rules of <see cref="RgbaFloat"/>.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct LaFloat : IEquatable<LaFloat>
{
    /// <summary>The gray level.</summary>
    public float L;

    /// <summary>The straight alpha component (0 is fully transparent, 1 is fully opaque).</summary>
    public float A;

    /// <summary>Initializes a new instance of the <see cref="LaFloat"/> struct with a fully opaque alpha.</summary>
    /// <param name="l">The gray level.</param>
    public LaFloat(float l)
        : this(l, 1f)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="LaFloat"/> struct.</summary>
    /// <param name="l">The gray level.</param>
    /// <param name="a">The straight alpha component.</param>
    public LaFloat(float l, float a)
    {
        L = l;
        A = a;
    }

    /// <inheritdoc />
    /// <remarks>Components are compared with <see cref="float.Equals(float)"/>: a component that is not a number equals itself.</remarks>
    public readonly bool Equals(LaFloat other) => L.Equals(other.L) && A.Equals(other.A);

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is LaFloat other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(L, A);

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"LaFloat({L}, {A})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if all components are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(LaFloat left, LaFloat right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if any component differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(LaFloat left, LaFloat right) => !left.Equals(right);
}
