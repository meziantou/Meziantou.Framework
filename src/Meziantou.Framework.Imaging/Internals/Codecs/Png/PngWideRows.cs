namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Rows of pixels in the wide format of <see cref="PngOptimizedLayout"/> (<see cref="PixelFormat.Rgba32"/> or
/// <see cref="PixelFormat.Rgba64"/>): the rows of a frame converted on demand (<see cref="PngFrameRows"/>), or the private
/// canvas copies of the APNG encoder (<see cref="PngCanvas"/>).
/// </summary>
internal abstract class PngWideRows
{
    /// <summary>Gets the width in pixels.</summary>
    public abstract int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public abstract int Height { get; }

    /// <summary>Gets a row of wide pixels; the span is valid until the next call.</summary>
    /// <param name="y">The row.</param>
    /// <returns>The row, <c>Width</c> wide pixels.</returns>
    public abstract ReadOnlySpan<byte> GetRow(int y);
}
