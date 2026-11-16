using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.FileConversionTool.Inputs;

/// <summary>An image file: one page (with its animation frames), or the pages of a TIFF document.</summary>
internal sealed class ImageInputDocument : InputDocument
{
    private readonly byte[] _data;
    private readonly ImageCollection? _pages;

    private ImageInputDocument(FullPath path, FileFormat format, byte[] data, ImageCollection? pages)
        : base(path, format)
    {
        _data = data;
        _pages = pages;
    }

    public override int? PageCount => _pages?.Count ?? 1;

    public static ImageInputDocument Open(FullPath path, FileFormat format)
    {
        var data = File.ReadAllBytes(path);
        ImageCollection? pages = null;
        if (format.ImageFormat is ImageFormat.Tiff)
        {
            pages = ImageCollection.Load(data);
            if (pages.Count <= 1)
            {
                pages.Dispose();
                pages = null;
            }
        }

        return new ImageInputDocument(path, format, data, pages);
    }

    public override IEnumerable<ConversionPage> GetPages(CancellationToken cancellationToken)
    {
        if (_pages is null)
        {
            yield return new EncodedImagePage(1, new EncodedImage(_data, Format.ImageFormat));
            yield break;
        }

        for (var i = 0; i < _pages.Count; i++)
        {
            yield return new CollectionEntryPage(i + 1, _pages[i]);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pages?.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class CollectionEntryPage(int pageNumber, ImageCollectionEntry entry) : ConversionPage(pageNumber)
    {
        public override Image RenderImage(CancellationToken cancellationToken) => entry.Decode(cancellationToken: cancellationToken);
    }
}
