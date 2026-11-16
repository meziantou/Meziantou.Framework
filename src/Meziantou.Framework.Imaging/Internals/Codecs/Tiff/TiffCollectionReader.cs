namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The <see cref="ImageCollectionReader"/> of a TIFF or BigTIFF document: the pages are resolved from the directory chain
/// when the collection is loaded, and the pixels of a page are read only when that page is decoded.
/// </summary>
internal sealed class TiffCollectionReader : ImageCollectionReader
{
    private readonly TiffPage[] _pages;

    private TiffCollectionReader(RandomAccessInput input, ImageConfiguration configuration, TiffPage[] pages)
        : base(input, configuration)
        => _pages = pages;

    public override ImageFormat Format => ImageFormat.Tiff;

    /// <summary>Gets the kind: the directories of a TIFF are the pages of a document, never animation frames.</summary>
    public override ImageCollectionKind Kind => ImageCollectionKind.Pages;

    public override int Count => _pages.Length;

    /// <summary>Gets a value indicating that a page decodes only the strips or the tiles that an area meets (<see cref="ImageDecodeRequest.Region"/>).</summary>
    public override bool SupportsRegionDecoding => true;

    /// <summary>Reads the header and resolves every page of a document.</summary>
    /// <param name="input">The random-access input; the reader takes ownership.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The reader.</returns>
    public static TiffCollectionReader Create(RandomAccessInput input, ImageConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(configuration);
        var context = new ImageCodecContext(configuration, "TiffDocument", CancellationToken.None);
        var source = new RandomAccessSource(input, context, ImageFormat.Tiff);
        var header = TiffHeader.Read(source);
        var first = TiffDirectory.Read(source, header, header.FirstDirectoryOffset);
        if (DngDocument.IsDng(first))
            throw new UnsupportedImageFeatureException("DNG stores a single raw image, not a collection of pages: use Image.Load to render it, and AuxiliaryImageCollection for its previews.", ImageFormat.Dng, "Image collection");

        // The first directory is read once: its bytes count toward the encoded-byte limit once
        var pages = new List<TiffPage>();
        foreach (var directory in TiffDirectoryChain.Enumerate(source, header, configuration.Limits, first))
        {
            pages.Add(TiffPage.Resolve(directory, pages.Count, context));
        }

        return new TiffCollectionReader(input, configuration, [.. pages]);
    }

    public override ImageCollectionEntryDescriptor GetDescriptor(int index)
    {
        var page = _pages[index];
        return new ImageCollectionEntryDescriptor(
            page.Size,
            page.DefaultPixelFormat,
            page.ColorModel,
            page.BitsPerSample,
            page.HasAlpha,
            PayloadFormat: ImageFormat.Tiff,
            Hotspot: null,
            page.Metadata);
    }

    protected override Image DecodeCore(int index, RandomAccessSource source, ImageDecodeRequest request, ImageCodecContext context)
        => TiffPageDecoder.Decode(source, _pages[index], request, context);
}
