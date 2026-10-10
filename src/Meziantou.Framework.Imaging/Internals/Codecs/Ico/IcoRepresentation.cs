using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// One resolved representation of an icon or cursor directory: the payload range, the kind of payload, the geometry read
/// from the payload itself and, for cursors, the hotspot.
/// </summary>
/// <remarks>
/// The directory byte of a dimension cannot express more than 256 and real files routinely store 0 for a 256-pixel side,
/// so the <em>payload</em> is authoritative for the geometry and the sample layout; the directory values are only used to
/// locate the payload and are validated against it where they can be.
/// </remarks>
internal sealed class IcoRepresentation
{
    /// <summary>Gets the zero-based index of the representation in the directory.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the offset of the payload from the start of the file.</summary>
    public required long Offset { get; init; }

    /// <summary>Gets the declared length of the payload.</summary>
    public required long Length { get; init; }

    /// <summary>Gets the format of the payload: <see cref="ImageFormat.Png"/> or <see cref="ImageFormat.Bmp"/> (a bare DIB).</summary>
    public required ImageFormat PayloadFormat { get; init; }

    /// <summary>Gets the size of the representation, read from the payload.</summary>
    public required Size Size { get; init; }

    /// <summary>Gets the number of bits of one stored pixel, read from the payload (used to pick between representations of the same size).</summary>
    public required int BitsPerPixel { get; init; }

    /// <summary>Gets the lossless working representation of the payload.</summary>
    public required PixelFormat PixelFormat { get; init; }

    /// <summary>Gets the color model of the encoded samples.</summary>
    public required ImageColorModel ColorModel { get; init; }

    /// <summary>Gets the precision of one encoded component.</summary>
    public required int BitsPerComponent { get; init; }

    /// <summary>Gets a value indicating whether the representation can have transparent pixels.</summary>
    public required bool MayHaveTransparency { get; init; }

    /// <summary>Gets the hotspot of a cursor representation, or <see langword="null"/> for an icon.</summary>
    public Point? Hotspot { get; init; }

    /// <summary>Gets the parsed DIB header of a DIB-backed representation, or <see langword="null"/> for a PNG payload.</summary>
    public BmpInfoHeader? DibHeader { get; init; }

    /// <summary>Gets the offset of the DIB palette from the start of the payload.</summary>
    public int DibPaletteOffset { get; init; }

    /// <summary>Gets a value indicating whether the payload stores the 1-bit AND mask after the color data.</summary>
    public bool HasAndMask { get; init; }

    /// <summary>
    /// Gets a value indicating whether the alpha of a 32-bit <c>BI_RGB</c> payload is implicit: the DIB declares no alpha
    /// mask, but a 32-bit icon payload stores alpha in its fourth byte. When every alpha byte turns out to be zero the
    /// channel is unused and only the AND mask determines transparency.
    /// </summary>
    public bool HasImplicitAlpha { get; init; }

    /// <summary>Gets the metadata of the representation (an icon directory carries none of its own).</summary>
    public ImageMetadata Metadata { get; init; } = new();
}
