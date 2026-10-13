namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// An immutable pixel aspect ratio (the <c>pasp</c> box of ISO/IEC 14496-12): the relative width and height of a pixel,
/// for images whose pixels are not square.
/// </summary>
/// <remarks>
/// <para>
/// A pixel is <see cref="HorizontalSpacing"/> units wide for <see cref="VerticalSpacing"/> units high: a ratio of 2:1
/// describes pixels twice as wide as they are high. The two terms are kept as they are given, not reduced, so that a
/// description round-trips unchanged.
/// </para>
/// <para>
/// This is a label: no operation resamples the pixels to make them square. An operation that swaps the axes of the image
/// (a rotation by a quarter turn, <c>AutoOrient</c> for the orientations that transpose) swaps the two terms.
/// </para>
/// </remarks>
public sealed class PixelAspectRatio : IEquatable<PixelAspectRatio>
{
    /// <summary>Initializes a new instance of the <see cref="PixelAspectRatio"/> class.</summary>
    /// <param name="horizontalSpacing">The relative width of a pixel. Must be positive.</param>
    /// <param name="verticalSpacing">The relative height of a pixel. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">A term is zero.</exception>
    public PixelAspectRatio(uint horizontalSpacing, uint verticalSpacing)
    {
        ArgumentOutOfRangeException.ThrowIfZero(horizontalSpacing);
        ArgumentOutOfRangeException.ThrowIfZero(verticalSpacing);

        HorizontalSpacing = horizontalSpacing;
        VerticalSpacing = verticalSpacing;
    }

    /// <summary>Gets the relative width of a pixel.</summary>
    public uint HorizontalSpacing { get; }

    /// <summary>Gets the relative height of a pixel.</summary>
    public uint VerticalSpacing { get; }

    /// <summary>Gets the width of a pixel divided by its height.</summary>
    public double Ratio => (double)HorizontalSpacing / VerticalSpacing;

    /// <summary>Gets a value indicating whether the pixels are square (the two terms are equal).</summary>
    public bool IsSquare => HorizontalSpacing == VerticalSpacing;

    /// <summary>Returns the ratio of the pixels of the image once its axes are swapped.</summary>
    internal PixelAspectRatio Transpose() => IsSquare ? this : new PixelAspectRatio(VerticalSpacing, HorizontalSpacing);

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] PixelAspectRatio? other) => other is not null && HorizontalSpacing == other.HorizontalSpacing && VerticalSpacing == other.VerticalSpacing;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as PixelAspectRatio);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(HorizontalSpacing, VerticalSpacing);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{HorizontalSpacing}:{VerticalSpacing}");
}
