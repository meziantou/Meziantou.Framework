namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>
/// The results of decoded global segments (<c>/JBIG2Globals</c>), shared by the images that use them: the symbol and
/// pattern dictionaries, the tables and the intermediate regions, with the memory and the work they cost.
/// </summary>
/// <remarks>Instances are immutable: the decoders that start with them copy the dictionary of results, and never change the bitmaps.</remarks>
internal sealed class Jbig2Globals(IReadOnlyDictionary<uint, Jbig2Decoder.SegmentResult> results, long memory, long work, int segmentCount)
{
    public IReadOnlyDictionary<uint, Jbig2Decoder.SegmentResult> Results { get; } = results;

    public long Memory { get; } = memory;

    public long Work { get; } = work;

    public int SegmentCount { get; } = segmentCount;
}
