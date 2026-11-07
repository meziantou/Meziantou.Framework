namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What a JPEG file holds besides its quantized coefficients, as the JPEG reconstruction data of a JPEG XL file describe
/// it (ISO/IEC 18181-2, annex A): the order of its markers, its segments, its quantization and Huffman tables, the
/// parameters of each scan, and the choices of the encoder that the coefficients do not determine.
/// <see cref="JpegBitstreamWriter"/> writes the file back, byte for byte.
/// </summary>
internal sealed class JpegBitstreamModel
{
    /// <summary>The marker that stands for data between two markers in <see cref="MarkerOrder"/>.</summary>
    public const byte InterMarkerData = 0xFF;

    /// <summary>Gets or sets the width of the image.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the height of the image.</summary>
    public int Height { get; set; }

    /// <summary>Gets the markers of the file after the start of image, in order, up to the end of image.</summary>
    public List<byte> MarkerOrder { get; } = [];

    /// <summary>Gets the application segments, in order: the second byte of the marker, the length and the payload.</summary>
    public List<byte[]> ApplicationSegments { get; } = [];

    /// <summary>Gets the comment segments, in order: the second byte of the marker, the length and the payload.</summary>
    public List<byte[]> CommentSegments { get; } = [];

    /// <summary>Gets the bytes that sit between two markers, in order.</summary>
    public List<byte[]> InterMarkerSegments { get; } = [];

    /// <summary>Gets or sets the bytes that follow the end of image.</summary>
    public byte[] TailData { get; set; } = [];

    /// <summary>Gets the quantization tables, in the order of the file.</summary>
    public List<JpegBitstreamQuantizationTable> QuantizationTables { get; } = [];

    /// <summary>Gets the components of the frame.</summary>
    public List<JpegBitstreamComponent> Components { get; } = [];

    /// <summary>Gets the Huffman tables, in the order of the file.</summary>
    public List<JpegBitstreamHuffmanTable> HuffmanTables { get; } = [];

    /// <summary>Gets the scans, in order.</summary>
    public List<JpegBitstreamScan> Scans { get; } = [];

    /// <summary>Gets or sets the restart interval, in minimum coded units; 0 without restart markers.</summary>
    public int RestartInterval { get; set; }

    /// <summary>
    /// Gets or sets the bits that pad the entropy-coded data to a byte boundary, one per element, when some of them are
    /// zero; <see langword="null"/> when every padding bit is one.
    /// </summary>
    public byte[]? PaddingBits { get; set; }

    /// <summary>Gets the marker of the frame header.</summary>
    public byte FrameMarker => MarkerOrder.Find(marker => marker is >= 0xC0 and <= 0xC2);
}
