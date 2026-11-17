using Meziantou.Framework.FileConversionTool.Inputs;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.FileConversionTool.Outputs;

/// <summary>Writes the pages of the inputs to a multi-page TIFF file.</summary>
internal static class TiffOutputWriter
{
    public static void Write(FullPath outputPath, IEnumerable<ConversionPage> pages, ConversionSettings settings, CancellationToken cancellationToken)
    {
        using var collection = ImageCollection.Create(ImageCollectionKind.Pages);
        foreach (var page in pages)
        {
            using var image = page.RenderImage(cancellationToken);
            settings.ResizeImage(image, cancellationToken);
            collection.Add(image);
        }

        if (collection.Count is 0)
            throw new InvalidOperationException("The inputs have no page to write");

        var encoder = settings.CreateEncoder(ImageFormat.Tiff, outputPath.Extension);
        ArchiveFiles.WriteAtomically(outputPath, stream => collection.Save(stream, encoder));
    }
}
