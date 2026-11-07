using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>An 8-bit alpha (coverage) value without color, for alpha masks.</summary>
/// <remarks>
/// The pixel has no color: it reads as black with its alpha (<c>(0, 0, 0, A)</c>) in every conversion to another pixel
/// format, and a conversion to <see cref="A8"/> keeps the alpha of the source and discards its color.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Pixel structs expose their exact storage layout as fields.")]
public struct A8 : IEquatable<A8>
{
    /// <summary>The alpha component (0 is fully transparent, 255 is fully opaque).</summary>
    public byte A;

    /// <summary>Initializes a new instance of the <see cref="A8"/> struct.</summary>
    /// <param name="a">The alpha component.</param>
    public A8(byte a) => A = a;

    /// <inheritdoc />
    public readonly bool Equals(A8 other) => A == other.A;

    /// <inheritdoc />
    public override readonly bool Equals([NotNullWhen(true)] object? obj) => obj is A8 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => A;

    /// <inheritdoc />
    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture, $"A8({A})");

    /// <summary>Compares two pixels for equality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(A8 left, A8 right) => left.Equals(right);

    /// <summary>Compares two pixels for inequality.</summary>
    /// <param name="left">The first pixel.</param>
    /// <param name="right">The second pixel.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(A8 left, A8 right) => !left.Equals(right);
}
