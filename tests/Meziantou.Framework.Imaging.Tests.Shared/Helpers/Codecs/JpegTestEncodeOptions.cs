namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>Options of <see cref="JpegTestImage.Encode"/>.</summary>
internal sealed class JpegTestEncodeOptions
{
    /// <summary>Gets the restart interval in MCUs (0: none).</summary>
    public int RestartInterval { get; init; }

    /// <summary>Gets the scans as groups of component indices (default: one scan with every component).</summary>
    public int[][]? Scans { get; init; }

    public JpegTestHuffmanStyle Huffman { get; init; }

    public int HuffmanSeed { get; init; } = 1;

    /// <summary>Gets the frame marker: 0xC0 (baseline) or 0xC1 (extended sequential); progressive encodings always write 0xC2.</summary>
    public byte FrameMarker { get; init; } = 0xC0;

    /// <summary>
    /// Gets the scans of a progressive encoding (SOF2), in order, or <see langword="null"/> for a sequential encoding. The
    /// scans are written as given, even when they do not form a valid progression (for defect tests).
    /// </summary>
    public IReadOnlyList<JpegTestProgressiveScan>? Progression { get; init; }

    /// <summary>Gets the longest end-of-band run of progressive AC scans (at most 32,767).</summary>
    public int MaxEndOfBandRun { get; init; } = 0x7FFF;

    /// <summary>Gets a value indicating whether the DHT segments are written again before every scan.</summary>
    public bool TablesBeforeEachScan { get; init; }

    /// <summary>Gets complete marker segments written after APP0/APP14 (before the tables and the frame header).</summary>
    public IReadOnlyList<byte[]> SegmentsBeforeFrame { get; init; } = [];

    /// <summary>Gets complete marker segments written after the last scan (before EOI).</summary>
    public IReadOnlyList<byte[]> SegmentsAfterScans { get; init; } = [];
}
