using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering;
using Meziantou.Framework.Pdf.Rendering.Images;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// An image painted by the content of a page (<see cref="PdfPage.GetImages"/>): an image XObject painted by <c>Do</c>,
/// directly or by a form, or an inline image (ISO 32000-2, 8.9). An image XObject painted several times is an
/// occurrence each time, with the same <see cref="Reference"/>.
/// </summary>
public sealed class PdfImageOccurrence
{
    private readonly PdfStream? _stream;
    private readonly PdfDictionary? _inlineDictionary;
    private readonly PdfDictionary? _resources;

    private PdfImageOccurrence(PdfPage page, PdfMatrix matrix, PdfDictionary dictionary, ReadOnlyMemory<byte> encodedData, PdfDictionary? resources, PdfStream? stream, PdfDictionary? inlineDictionary)
    {
        _stream = stream;
        _inlineDictionary = inlineDictionary;
        Page = page;
        Matrix = matrix;
        Dictionary = dictionary;
        EncodedData = encodedData;
        _resources = resources;
    }

    /// <summary>Gets the page.</summary>
    public PdfPage Page { get; }

    /// <summary>Gets the matrix from the unit square of the image to the default user space of the page: the transformation matrix when the image is painted (8.9.4).</summary>
    public PdfMatrix Matrix { get; }

    /// <summary>Gets the bounding box of the image on the page, in default user space: the unit square transformed by <see cref="Matrix"/>.</summary>
    public PdfRectangle Bounds
    {
        get
        {
            var corners = new[] { Matrix.Transform(0, 0), Matrix.Transform(1, 0), Matrix.Transform(0, 1), Matrix.Transform(1, 1) };
            return new PdfRectangle(corners.Min(point => point.X), corners.Min(point => point.Y), corners.Max(point => point.X), corners.Max(point => point.Y));
        }
    }

    /// <summary>Gets a value indicating whether the image is an inline image (<c>BI</c> ... <c>ID</c> ... <c>EI</c>).</summary>
    public bool IsInline => _inlineDictionary is not null;

    /// <summary>Gets the name of the image XObject in the resources it was painted from, or <see langword="null"/> for an inline image.</summary>
    public string? Name { get; private init; }

    /// <summary>Gets the reference to the image XObject, or <see langword="null"/> for an inline image or a direct stream.</summary>
    public PdfReference? Reference { get; private init; }

    /// <summary>Gets the image dictionary: the dictionary of the stream, or the dictionary of an inline image with its abbreviated keys expanded (<c>/W</c> is <c>/Width</c>...). Its color space can be the name of a resource.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the data of the image, encoded with its filters (<see cref="Filters"/>).</summary>
    public ReadOnlyMemory<byte> EncodedData { get; }

    /// <summary>Gets the filters of the data, such as <c>DCTDecode</c> for a JPEG image.</summary>
    public IReadOnlyList<PdfName> Filters => Dictionary.GetResolved(PdfNames.Filter) switch
    {
        PdfName name => [name],
        PdfArray array => [.. array.Select(PdfReference.ResolveIfReference).OfType<PdfName>()],
        _ => [],
    };

    /// <summary>Gets the width of the image, in samples.</summary>
    public int Width => (int)Math.Clamp(Dictionary.GetInteger(PdfNames.Width, 0), 0, int.MaxValue);

    /// <summary>Gets the height of the image, in samples.</summary>
    public int Height => (int)Math.Clamp(Dictionary.GetInteger(PdfNames.Height, 0), 0, int.MaxValue);

    /// <summary>Gets the number of bits of each component of a sample, or 0 when the filter of the image gives it (JPXDecode).</summary>
    public int BitsPerComponent => (int)Math.Clamp(Dictionary.GetInteger(PdfNames.BitsPerComponent, Dictionary.GetBoolean(PdfNames.ImageMask) ? 1 : 0), 0, 32);

    /// <summary>Gets a value indicating whether the image is a stencil mask, painted with the fill color (8.9.6.2).</summary>
    public bool IsImageMask => Dictionary.GetBoolean(PdfNames.ImageMask);

    /// <summary>Gets the color space of the image (<c>/ColorSpace</c>), resolved in the resources when it is a name, or <see langword="null"/> (an image mask, a JPXDecode image).</summary>
    public PdfObject? ColorSpace
    {
        get
        {
            var value = Dictionary.GetResolved(PdfNames.ColorSpace);
            if (value is PdfName name && _resources?.Get<PdfDictionary>(PdfNames.ColorSpace)?.GetResolved(name) is { } resource)
                return resource;

            return value;
        }
    }

    /// <summary>Gets the soft mask of the image (<c>/SMask</c>, 11.6.5.3), or <see langword="null"/>.</summary>
    public PdfStream? SoftMask => Dictionary.Get<PdfStream>(PdfNames.SMask);

    /// <summary>
    /// Decodes the image to RGBA pixels: its color space converted to sRGB, its decode array, its mask or soft mask applied
    /// as the alpha channel, within the limits of the read options of the document. A stencil mask is black where it paints.
    /// </summary>
    /// <returns>The image, which the caller disposes, or <see langword="null"/> when the image has no pixels.</returns>
    /// <exception cref="InvalidPdfContentException">The data of the image cannot be decoded.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The image uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The image exceeds a limit of the read options.</exception>
    public Image<Rgba32>? Decode()
    {
        using var cache = new RenderCache(new PdfRenderOptions { UseSystemFonts = false });
        var decoded = _stream is not null
            ? PdfImageDecoder.Decode(_stream, _resources, cache)
            : PdfImageDecoder.DecodeInline(_inlineDictionary!, EncodedData, _resources, cache);
        // The images the decoder creates belong to the cache: the caller gets a copy
        return decoded?.Image.CloneAs<Rgba32>();
    }

    internal static PdfImageOccurrence FromXObject(PdfPage page, PdfMatrix matrix, PdfStream stream, string name, PdfReference? reference, PdfDictionary? resources)
    {
        return new PdfImageOccurrence(page, matrix, stream.Dictionary, stream.EncodedData, resources, stream, inlineDictionary: null)
        {
            Name = name,
            Reference = reference,
        };
    }

    internal static PdfImageOccurrence FromInline(PdfPage page, PdfMatrix matrix, PdfDictionary inline, ReadOnlyMemory<byte> data, PdfDictionary? resources)
    {
        // The abbreviations of inline images (8.9.7, table 91 and 92)
        var dictionary = new PdfDictionary();
        foreach (var (key, value) in inline)
        {
            var name = key.Value switch
            {
                "W" => "Width",
                "H" => "Height",
                "BPC" => "BitsPerComponent",
                "CS" => "ColorSpace",
                "D" => "Decode",
                "DP" => "DecodeParms",
                "F" => "Filter",
                "IM" => "ImageMask",
                "I" => "Interpolate",
                _ => key.Value,
            };
            var expanded = PdfReference.ResolveIfReference(value) switch
            {
                PdfName { Value: "G" } => PdfNames.DeviceGray,
                PdfName { Value: "RGB" } => PdfNames.DeviceRGB,
                PdfName { Value: "CMYK" } => PdfNames.DeviceCMYK,
                PdfName { Value: "I" } when name == "ColorSpace" => PdfName.Create("Indexed"),
                PdfName { Value: "AHx" } => PdfName.Create("ASCIIHexDecode"),
                PdfName { Value: "A85" } => PdfName.Create("ASCII85Decode"),
                PdfName { Value: "LZW" } => PdfName.Create("LZWDecode"),
                PdfName { Value: "Fl" } => PdfNames.FlateDecode,
                PdfName { Value: "RL" } => PdfName.Create("RunLengthDecode"),
                PdfName { Value: "CCF" } => PdfName.Create("CCITTFaxDecode"),
                PdfName { Value: "DCT" } => PdfName.Create("DCTDecode"),
                var other => other,
            };
            dictionary[PdfName.Create(name)] = expanded;
        }

        return new PdfImageOccurrence(page, matrix, dictionary, data, resources, stream: null, inline);
    }
}
