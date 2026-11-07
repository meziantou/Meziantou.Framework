namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The scanlines of one PNG image, for the datastreams of <see cref="PngImageDataEncoder.StartSource"/>.</summary>
internal abstract class PngScanlineSource
{
    /// <summary>Gets the image width.</summary>
    public abstract int Width { get; }

    /// <summary>Gets the image height.</summary>
    public abstract int Height { get; }

    /// <summary>Gets the byte distance of the filters (the bytes per complete pixel, at least one).</summary>
    public abstract int FilterDistance { get; }

    /// <summary>Gets the length of the scanline of a row of pixels, without its filter type byte.</summary>
    /// <param name="width">The number of pixels.</param>
    /// <returns>The length in bytes.</returns>
    public abstract int GetScanlineLength(int width);

    /// <summary>Writes the scanline of pixels <c>(x0 + i * dx, y)</c> of the image, for <c>i</c> below <paramref name="count"/>.</summary>
    /// <param name="y">The row of the image.</param>
    /// <param name="x0">The first column.</param>
    /// <param name="dx">The column step (1, or the step of an Adam7 pass).</param>
    /// <param name="count">The number of pixels.</param>
    /// <param name="scanline">The scanline, <see cref="GetScanlineLength"/> bytes.</param>
    public abstract void WriteScanline(int y, int x0, int dx, int count, Span<byte> scanline);
}
