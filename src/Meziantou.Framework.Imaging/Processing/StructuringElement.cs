namespace Meziantou.Framework.Imaging;

/// <summary>
/// An immutable structuring element: the neighborhood of a pixel that the morphological filters read
/// (<see cref="ImageProcessingExtensions.Erode(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/>,
/// <see cref="ImageProcessingExtensions.Dilate(Image, StructuringElement, ConvolutionEdgeMode, CancellationToken)"/> and the
/// operations made of them). It is flat: every pixel of the neighborhood counts the same.
/// </summary>
/// <remarks>
/// The neighborhood is centered on the pixel and symmetric: it holds the offset <c>(dx, dy)</c> when it holds
/// <c>(-dx, -dy)</c>. A rectangle (<see cref="CreateRectangle"/>) is filtered in two passes, a row then a column, in a time
/// per pixel that does not depend on its size; an ellipse (<see cref="CreateEllipse"/>), which rounds the corners, takes a
/// time per pixel that grows with its height.
/// </remarks>
public sealed class StructuringElement
{
    /// <summary>The largest radius of a structuring element.</summary>
    internal const int MaxRadius = 1000;

    private readonly int[] _halfWidths;

    private StructuringElement(int radiusX, int radiusY, bool isRectangle, int[] halfWidths)
    {
        RadiusX = radiusX;
        RadiusY = radiusY;
        IsRectangle = isRectangle;
        _halfWidths = halfWidths;
    }

    /// <summary>Gets the number of pixels on each side of the center, horizontally.</summary>
    public int RadiusX { get; }

    /// <summary>Gets the number of pixels on each side of the center, vertically.</summary>
    public int RadiusY { get; }

    /// <summary>Gets the number of pixels of a row of the bounding box, <c>2 RadiusX + 1</c>.</summary>
    public int Width => (2 * RadiusX) + 1;

    /// <summary>Gets the number of pixels of a column of the bounding box, <c>2 RadiusY + 1</c>.</summary>
    public int Height => (2 * RadiusY) + 1;

    /// <summary>Gets a value indicating whether the element is the whole rectangle of its bounding box (<see cref="CreateRectangle"/>).</summary>
    internal bool IsRectangle { get; }

    /// <summary>
    /// Creates a rectangle of <c>2 radiusX + 1</c> by <c>2 radiusY + 1</c> pixels: the neighborhood of
    /// <see cref="ImageProcessingExtensions.MedianBlur(Image, int, ConvolutionEdgeMode, CancellationToken)"/> when both radii
    /// are equal, and the rectangle of OpenCV's <c>MORPH_RECT</c> and of SciPy's <c>size</c> parameter.
    /// </summary>
    /// <param name="radiusX">The number of pixels on each side of the center, horizontally, from 0 to 1000.</param>
    /// <param name="radiusY">The number of pixels on each side of the center, vertically, from 0 to 1000.</param>
    /// <returns>The structuring element.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A radius is negative or exceeds 1000.</exception>
    public static StructuringElement CreateRectangle(int radiusX, int radiusY)
    {
        ValidateRadius(radiusX, nameof(radiusX));
        ValidateRadius(radiusY, nameof(radiusY));
        var halfWidths = new int[(2 * radiusY) + 1];
        Array.Fill(halfWidths, radiusX);
        return new StructuringElement(radiusX, radiusY, isRectangle: true, halfWidths);
    }

    /// <summary>
    /// Creates an ellipse: the offsets <c>(dx, dy)</c> such that <c>(dx / radiusX)^2 + (dy / radiusY)^2 &lt;= 1</c>,
    /// computed exactly in integers. With equal radii, it is the disk of scikit-image's <c>morphology.disk(radius)</c>. A
    /// radius of 0 gives a line along the other axis.
    /// </summary>
    /// <param name="radiusX">The number of pixels on each side of the center, horizontally, from 0 to 1000.</param>
    /// <param name="radiusY">The number of pixels on each side of the center, vertically, from 0 to 1000.</param>
    /// <returns>The structuring element.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A radius is negative or exceeds 1000.</exception>
    public static StructuringElement CreateEllipse(int radiusX, int radiusY)
    {
        ValidateRadius(radiusX, nameof(radiusX));
        ValidateRadius(radiusY, nameof(radiusY));
        if (radiusX == 0 || radiusY == 0)
            return CreateRectangle(radiusX, radiusY);

        // The largest dx with dx^2 ry^2 + dy^2 rx^2 <= rx^2 ry^2, in exact integer arithmetic
        var halfWidths = new int[(2 * radiusY) + 1];
        long rx2 = (long)radiusX * radiusX;
        long ry2 = (long)radiusY * radiusY;
        var isRectangle = true;
        for (var dy = -radiusY; dy <= radiusY; dy++)
        {
            var limit = (rx2 * ry2) - ((long)dy * dy * rx2);
            var dx = radiusX;
            while ((long)dx * dx * ry2 > limit)
            {
                dx--;
            }

            halfWidths[dy + radiusY] = dx;
            isRectangle &= dx == radiusX;
        }

        return new StructuringElement(radiusX, radiusY, isRectangle, halfWidths);
    }

    /// <summary>Gets a value indicating whether the element holds an offset from its center.</summary>
    /// <param name="dx">The horizontal offset, positive to the right.</param>
    /// <param name="dy">The vertical offset, positive downward.</param>
    /// <returns><see langword="true"/> when the pixel at this offset is part of the neighborhood.</returns>
    public bool Contains(int dx, int dy) => Math.Abs(dy) <= RadiusY && Math.Abs(dx) <= _halfWidths[dy + RadiusY];

    /// <summary>Gets the number of pixels on each side of the center of the row at offset <paramref name="dy"/>, from <c>-RadiusY</c> to <c>RadiusY</c>.</summary>
    internal int GetHalfWidth(int dy) => _halfWidths[dy + RadiusY];

    private static void ValidateRadius(int radius, string paramName)
    {
        if (radius is < 0 or > MaxRadius)
            throw new ArgumentOutOfRangeException(paramName, radius, string.Create(CultureInfo.InvariantCulture, $"The radius must be between 0 and {MaxRadius}."));
    }
}
