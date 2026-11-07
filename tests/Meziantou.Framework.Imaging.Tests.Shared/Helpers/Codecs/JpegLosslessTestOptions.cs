namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>Options of <see cref="JpegLosslessTestImage.Encode"/>.</summary>
internal sealed class JpegLosslessTestOptions
{
    /// <summary>Gets the predictor of the scans (<c>Ss</c>), when <see cref="Scans"/> is not set.</summary>
    public int Predictor { get; init; } = 1;

    /// <summary>Gets the point transform of the scans (<c>Al</c>), when <see cref="Scans"/> is not set.</summary>
    public int PointTransform { get; init; }

    /// <summary>Gets the scans (default: one scan with every component).</summary>
    public IReadOnlyList<JpegLosslessTestScan>? Scans { get; init; }

    /// <summary>Gets the restart interval in MCUs (0: none). T.81 H.1.1 requires a whole number of MCU rows; the value is written as given.</summary>
    public int RestartInterval { get; init; }

    /// <summary>Gets the restart interval in MCU rows of each scan (0: none), which replaces <see cref="RestartInterval"/>: a DRI segment is written before each scan.</summary>
    public int RestartRows { get; init; }

    public JpegTestHuffmanStyle Huffman { get; init; }

    public int HuffmanSeed { get; init; } = 1;

    /// <summary>Gets a value indicating whether each component has a table of its own (up to four) instead of one for the first component and one for the others.</summary>
    public bool TablePerComponent { get; init; }

    /// <summary>
    /// Gets the seed of the samples that complete the MCUs of an interleaved scan (T.81 A.2.4), or <see langword="null"/> to
    /// replicate the last column and the last line. A decoder removes them, whatever they are.
    /// </summary>
    public int? PaddingSeed { get; init; }

    /// <summary>Gets the frame marker: 0xC3 (lossless, Huffman coding) unless a test needs another one.</summary>
    public byte FrameMarker { get; init; } = 0xC3;

    /// <summary>Gets the values of the scan parameters that have no meaning in a lossless scan and shall be zero: <c>Se</c> and <c>Ah</c>.</summary>
    public (int End, int High) UnusedScanParameters { get; init; }

    /// <summary>Gets complete marker segments written before the frame header.</summary>
    public IReadOnlyList<byte[]> SegmentsBeforeFrame { get; init; } = [];
}
