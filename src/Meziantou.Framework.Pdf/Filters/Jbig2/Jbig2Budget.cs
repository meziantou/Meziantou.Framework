namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>
/// Bounds the work of a JBIG2 stream: the bytes of its bitmaps (the page, the symbols, the patterns, the intermediate
/// regions), the pixels it decodes and the bytes it places. A few bytes of data can declare huge bitmaps and many instances.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic decoder decodes any number of decisions past the end of its data (files strip the trailing bytes it
/// would read as 1 bits), so a few bytes can code a huge region: the work is bounded instead, a pixel decoded being one
/// unit and a byte placed one unit.
/// </para>
/// <para>
/// A page needs about one unit per pixel for each region that covers it: decoded, placed, refined. The work of a stream
/// is at most three times the pixels of its page, and 16 million units for its dictionaries and small pages; it is also
/// at most 6 units per byte of the limit of the bitmaps.
/// </para>
/// </remarks>
internal sealed class Jbig2Budget(long maxMemory)
{
    // The units of work (pixels decoded, bytes placed) per byte of the limit of the bitmaps
    private const long WorkPerByte = 6;

    // The units of work per pixel of the page, and the units every stream has (its dictionaries, the pages of a few pixels)
    private const long WorkPerPagePixel = 3;
    private const long BaseWork = 1 << 24;

    private long _memory;
    private long _work;
    private long _pageArea;

    /// <summary>Gets the memory charged: the bytes of the bitmaps.</summary>
    public long Memory => _memory;

    /// <summary>Gets the work charged.</summary>
    public long Work => _work;

    public void ChargeMemory(long bytes)
    {
        _memory += bytes;
        if (bytes < 0 || _memory > maxMemory)
            throw new PdfResourceLimitException($"The JBIG2 bitmaps are larger than the limit of {maxMemory} bytes");
    }

    /// <summary>Charges the work of a bitmap: its pixels, and one more per row, so that rows of no pixel are work too.</summary>
    public void ChargeWork(long width, long height) => ChargeWork((Math.Max(width, 0) + 1) * Math.Max(height, 1));

    /// <summary>Charges the placement of a bitmap in another one: its bytes, and one more per row (<see cref="Jbig2Bitmap.Combine"/> works on bytes).</summary>
    public void ChargePlacement(Jbig2Bitmap bitmap) => ChargeWork(((long)bitmap.Stride + 1) * Math.Max(bitmap.Height, 1));

    /// <summary>Sets the pixels of the page, which the work is proportional to; the page of unknown height grows.</summary>
    public void SetPageArea(long pixels) => _pageArea = Math.Max(_pageArea, pixels);

    public void ChargeWork(long pixels)
    {
        _work += Math.Max(pixels, 1);
        if (_work > Math.Min(maxMemory * WorkPerByte, BaseWork + (WorkPerPagePixel * _pageArea)))
            throw new PdfResourceLimitException("The JBIG2 data decodes more pixels than its page needs, or than the limit allows");
    }
}
