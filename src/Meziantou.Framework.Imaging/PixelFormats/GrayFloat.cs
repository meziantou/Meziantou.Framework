using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>An opaque grayscale pixel with a single-precision floating-point gray level, in 4 bytes per pixel.</summary>
/// <remarks>
/// <para>
/// The gray level has the meaning of the colors of <see cref="RgbaFloat"/>: linear light where 1 is the reference white,
/// with values outside [0, 1] allowed (highlights above 1). A new image of this pixel type is labeled as linear
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
public struct GrayFloat : IEquatable<GrayFloat>
{
    /// <summary>The gray level.</summary>
    public float Value;

    /// <summary>Initializes a new instance of the <see cref="GrayFloat"/> struct.</summary>
    /// <param name="value">The gray level.</param>
    public GrayFloat(float value) => Value = value;

    /// <inheritdoc />
    /// <remarks>Values are compared with <see cref="float.Equals(float)"/>: a value that is not a number equals itself.</remarks>
    public readonly bool Equals(GrayFloat other) => Value.Equals(other.Value);

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is GrayFloat other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"GrayFloat({Value})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(GrayFloat left, GrayFloat right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(GrayFloat left, GrayFloat right) => !left.Equals(right);
}
