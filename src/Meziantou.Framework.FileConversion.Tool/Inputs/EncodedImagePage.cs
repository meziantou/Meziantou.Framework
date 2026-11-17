using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.FileConversionTool.Inputs;

/// <summary>A page that is an image file.</summary>
internal sealed class EncodedImagePage(int pageNumber, EncodedImage image) : ConversionPage(pageNumber)
{
    public override EncodedImage? GetEncodedImage() => image;

    public override Image RenderImage(CancellationToken cancellationToken) => Image.Load(image.Data.Span, options: null, cancellationToken);
}
