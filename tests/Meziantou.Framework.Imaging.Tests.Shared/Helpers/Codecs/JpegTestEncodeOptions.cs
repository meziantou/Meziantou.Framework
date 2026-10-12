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

    /// <summary>
    /// Gets the frame marker: 0xC0 (baseline) or 0xC1 (extended sequential); <see langword="null"/> is the baseline frame for
    /// 8-bit samples and the extended sequential one for 12-bit samples. Progressive encodings always write 0xC2, and
    /// arithmetic-coded ones 0xC9 or 0xCA.
    /// </summary>
    public byte? FrameMarker { get; init; }

    /// <summary>
    /// Gets the scans of a progressive encoding (SOF2), in order, or <see langword="null"/> for a sequential encoding. The
    /// scans are written as given, even when they do not form a valid progression (for defect tests).
    /// </summary>
    public IReadOnlyList<JpegTestProgressiveScan>? Progression { get; init; }

    /// <summary>Gets the longest end-of-band run of progressive AC scans (at most 32,767).</summary>
    public int MaxEndOfBandRun { get; init; } = 0x7FFF;

    /// <summary>Gets a value indicating whether the DHT segments are written again before every scan.</summary>
    public bool TablesBeforeEachScan { get; init; }

    /// <summary>
    /// Gets a value indicating whether the scans are arithmetic coded (ITU-T T.81 sections F.1.4 and G.1.3): the frame is
    /// SOF9, or SOF10 with a <see cref="Progression"/>, and no Huffman table is written.
    /// </summary>
    public bool Arithmetic { get; init; }

    /// <summary>
    /// Gets the bounds L and U of the DC conditioning of the tables 0 and 1 of an arithmetic-coded image, written in a DAC
    /// segment, or <see langword="null"/> for the default bounds (0 and 1), which need no segment.
    /// </summary>
    public (int Lower, int Upper)[]? DcConditioning { get; init; }

    /// <summary>
    /// Gets the threshold Kx of the AC conditioning of the tables 0 and 1 of an arithmetic-coded image, written in a DAC
    /// segment, or <see langword="null"/> for the default threshold (5), which needs no segment.
    /// </summary>
    public int[]? AcConditioning { get; init; }

    /// <summary>Gets a value indicating whether the zero bytes that end an arithmetic-coded segment are omitted (T.81 D.1.8), as encoders usually do.</summary>
    public bool DiscardFinalZeros { get; init; }

    /// <summary>Gets complete marker segments written after APP0/APP14 (before the tables and the frame header).</summary>
    public IReadOnlyList<byte[]> SegmentsBeforeFrame { get; init; } = [];

    /// <summary>Gets complete marker segments written after the last scan (before EOI).</summary>
    public IReadOnlyList<byte[]> SegmentsAfterScans { get; init; } = [];
}
