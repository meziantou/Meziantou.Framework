namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>
/// Bounds the work of a JBIG2 stream: the bytes of its bitmaps (the page, the symbols, the patterns, the intermediate
/// regions) and the pixels it decodes or places. A few bytes of data can declare huge bitmaps and many instances.
/// </summary>
internal sealed class Jbig2Budget(long maxMemory)
{
    // The pixels decoded and placed, as a multiple of the bytes of the bitmaps the stream may hold
    private const long WorkPerByte = 512;

    private long _memory;
    private long _work;

    public void ChargeMemory(long bytes)
    {
        _memory += bytes;
        if (bytes < 0 || _memory > maxMemory)
            throw new PdfResourceLimitException($"The JBIG2 bitmaps are larger than the limit of {maxMemory} bytes");
    }

    /// <summary>Charges the work of a bitmap: its pixels, and one more per row, so that rows of no pixel are work too.</summary>
    public void ChargeWork(long width, long height) => ChargeWork((Math.Max(width, 0) + 1) * Math.Max(height, 1));

    public void ChargeWork(long pixels)
    {
        _work += Math.Max(pixels, 1);
        if (_work > maxMemory * WorkPerByte)
            throw new PdfResourceLimitException("The JBIG2 data decodes more pixels than the limit");
    }
}
