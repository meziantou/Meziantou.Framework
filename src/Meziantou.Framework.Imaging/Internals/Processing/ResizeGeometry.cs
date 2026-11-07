using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The output size and the exact per-axis source mapping of a resize. All size
/// arithmetic is integer/rational and overflow-free (64- and 128-bit intermediates); nothing is computed in floating
/// point here.
/// </summary>
/// <param name="OutputSize">The size of the result.</param>
/// <param name="X">The horizontal mapping.</param>
/// <param name="Y">The vertical mapping.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ResizeGeometry(Size OutputSize, ResampleAxis X, ResampleAxis Y)
{
    /// <summary>Gets a value indicating whether the resize keeps every pixel in place (the output size equals the source size).</summary>
    public bool IsIdentity => X.IsIdentity && Y.IsIdentity;

    /// <summary>
    /// Gets the canvas that the resized image is placed on by <see cref="ResizeMode.Pad"/> and <see cref="ResizeMode.BoxPad"/>,
    /// or <see langword="null"/> when the result is the resized image alone (its size is <see cref="OutputSize"/>).
    /// </summary>
    public ResizePadding? Padding { get; init; }

    /// <summary>Gets the size of the result: the padded canvas, or the resized image.</summary>
    public Size CanvasSize => Padding?.CanvasSize ?? OutputSize;

    /// <summary>Gets the output pixel that designates a source pixel after the resize (see <see cref="ResampleAxis.TryMapSourceIndex"/>).</summary>
    /// <param name="source">A pixel of the source canvas.</param>
    /// <param name="destination">The pixel of the result.</param>
    /// <returns><see langword="false"/> when the source pixel lies entirely outside the region a Cover resize keeps.</returns>
    public bool TryMapPoint(Point source, out Point destination)
    {
        if (X.TryMapSourceIndex(source.X, out var x) && Y.TryMapSourceIndex(source.Y, out var y))
        {
            var offset = Padding?.Offset ?? default;
            destination = new Point(x + offset.X, y + offset.Y);
            return true;
        }

        destination = default;
        return false;
    }

    /// <summary>Computes the geometry of resizing a <paramref name="source"/> canvas with <paramref name="options"/>.</summary>
    /// <exception cref="ArgumentException">
    /// Enlargement is required by <see cref="ResizeMode.Stretch"/> or <see cref="ResizeMode.Cover"/> while
    /// <see cref="ResizeOptions.AllowUpscaling"/> is <see langword="false"/>, or a reduction is required by a mode other
    /// than <see cref="ResizeMode.Contain"/> while <see cref="ResizeOptions.AllowDownscaling"/> is <see langword="false"/>.
    /// </exception>
    public static ResizeGeometry Compute(Size source, ResizeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sourceWidth = source.Width;
        var sourceHeight = source.Height;
        var targetWidth = options.Size.Width;
        var targetHeight = options.Size.Height;
        switch (options.Mode)
        {
            case ResizeMode.Stretch:
                if (!options.AllowUpscaling && (targetWidth > sourceWidth || targetHeight > sourceHeight))
                    throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Stretching the {sourceWidth}x{sourceHeight} image to {targetWidth}x{targetHeight} requires enlargement, but ResizeOptions.AllowUpscaling is false."), nameof(options));

                if (!options.AllowDownscaling && (targetWidth < sourceWidth || targetHeight < sourceHeight))
                    throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Stretching the {sourceWidth}x{sourceHeight} image to {targetWidth}x{targetHeight} requires a reduction, but ResizeOptions.AllowDownscaling is false."), nameof(options));

                return new(new Size(targetWidth, targetHeight), ResampleAxis.FullExtent(sourceWidth, targetWidth), ResampleAxis.FullExtent(sourceHeight, targetHeight));

            case ResizeMode.Contain or ResizeMode.Pad or ResizeMode.BoxPad:
            {
                // Scale factor s = min(tw / sw, th / sh), compared exactly with cross products
                var mode = options.Mode;
                var limitedByWidth = (long)targetWidth * sourceHeight <= (long)targetHeight * sourceWidth;
                long numerator = limitedByWidth ? targetWidth : targetHeight;
                long denominator = limitedByWidth ? sourceWidth : sourceHeight;
                if (numerator > denominator && (!options.AllowUpscaling || mode == ResizeMode.BoxPad))
                {
                    numerator = 1;
                    denominator = 1;
                }
                else if (numerator < denominator && !options.AllowDownscaling)
                {
                    // Contain may exceed the target; the padding modes cannot, since the canvas is the target
                    if (mode != ResizeMode.Contain)
                        throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Padding the {sourceWidth}x{sourceHeight} image to {targetWidth}x{targetHeight} requires a reduction, but ResizeOptions.AllowDownscaling is false."), nameof(options));

                    numerator = 1;
                    denominator = 1;
                }

                var width = limitedByWidth ? (numerator == denominator ? sourceWidth : targetWidth) : ScaleLength(sourceWidth, numerator, denominator);
                var height = limitedByWidth ? ScaleLength(sourceHeight, numerator, denominator) : (numerator == denominator ? sourceHeight : targetHeight);
                var geometry = new ResizeGeometry(new Size(width, height), ResampleAxis.FullExtent(sourceWidth, width), ResampleAxis.FullExtent(sourceHeight, height));
                if (mode == ResizeMode.Contain || (width == targetWidth && height == targetHeight))
                    return geometry;

                // The image fits in the target (s <= min(tw / sw, th / sh) and nearest rounding): the remainder is padding
                Debug.Assert(width <= targetWidth && height <= targetHeight);
                var (anchorX, anchorY) = GetAnchorPosition(options.Anchor);
                var offset = new Point((int)((long)(targetWidth - width) * anchorX / 2), (int)((long)(targetHeight - height) * anchorY / 2));
                return geometry with { Padding = new ResizePadding(new Size(targetWidth, targetHeight), offset) };
            }

            case ResizeMode.Cover:
            {
                // Scale factor s = max(tw / sw, th / sh); the overflowing axis is cropped at the anchor
                var limitedByWidth = (long)targetWidth * sourceHeight >= (long)targetHeight * sourceWidth;
                long numerator = limitedByWidth ? targetWidth : targetHeight;
                long denominator = limitedByWidth ? sourceWidth : sourceHeight;
                if (!options.AllowUpscaling && numerator > denominator)
                    throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Covering {targetWidth}x{targetHeight} with the {sourceWidth}x{sourceHeight} image requires enlargement, but ResizeOptions.AllowUpscaling is false."), nameof(options));

                if (!options.AllowDownscaling && numerator < denominator)
                    throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Covering {targetWidth}x{targetHeight} with the {sourceWidth}x{sourceHeight} image requires a reduction, but ResizeOptions.AllowDownscaling is false."), nameof(options));

                var (anchorX, anchorY) = GetAnchorPosition(options.Anchor);
                return new(
                    new Size(targetWidth, targetHeight),
                    ResampleAxis.Cover(sourceWidth, targetWidth, numerator, denominator, anchorX),
                    ResampleAxis.Cover(sourceHeight, targetHeight, numerator, denominator, anchorY));
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Mode, "The resize mode is not valid.");
        }
    }

    /// <summary>
    /// Scales a length by <c>numerator / denominator</c> with nearest rounding, ties upward, and a minimum of one pixel:
    /// <c>max(1, floor((2 * length * numerator + denominator) / (2 * denominator)))</c>.
    /// </summary>
    internal static int ScaleLength(int length, long numerator, long denominator)
    {
        var scaled = ((2 * (Int128)length * numerator) + denominator) / (2 * (Int128)denominator);
        return scaled < 1 ? 1 : checked((int)scaled);
    }

    /// <summary>Gets the anchor position on each axis, in halves of the overflow: 0 (start), 1 (center) or 2 (end).</summary>
    internal static (int X, int Y) GetAnchorPosition(ResizeAnchor anchor) => anchor switch
    {
        ResizeAnchor.TopLeft => (0, 0),
        ResizeAnchor.Top => (1, 0),
        ResizeAnchor.TopRight => (2, 0),
        ResizeAnchor.Left => (0, 1),
        ResizeAnchor.Center => (1, 1),
        ResizeAnchor.Right => (2, 1),
        ResizeAnchor.BottomLeft => (0, 2),
        ResizeAnchor.Bottom => (1, 2),
        ResizeAnchor.BottomRight => (2, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "The anchor is not valid."),
    };
}
