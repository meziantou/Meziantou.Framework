using Meziantou.Framework.FileConversionTool.Archives;
using Meziantou.Framework.FileConversionTool.Inputs;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.FileConversionTool.Outputs;

/// <summary>Writes the pages of the inputs to a comic book archive (CBZ, CB7, CBT): one image per page, named by the number of the page.</summary>
internal static class ComicOutputWriter
{
    /// <summary>Writes pages to a comic book archive.</summary>
    /// <param name="outputPath">The path of the archive.</param>
    /// <param name="outputFormat">The comic book format.</param>
    /// <param name="pages">The pages.</param>
    /// <param name="pageCount">The number of pages, when it is known, to pad the names of the entries.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static void Write(FullPath outputPath, FileFormat outputFormat, IEnumerable<ConversionPage> pages, int? pageCount, ConversionSettings settings, CancellationToken cancellationToken)
    {
        // The names of the entries are padded to at least 4 digits, or to the number of pages, so that they sort in order
        var padding = Math.Max(pageCount?.ToString(CultureInfo.InvariantCulture).Length ?? 0, 4);

        var index = 0;
        ArchiveFiles.WriteAtomically(outputPath, stream =>
        {
            // The images are already compressed: they are stored unless the command line sets a compression
            using var writer = ArchiveWriter.Create(stream, outputFormat.ArchiveType, settings, storeByDefault: true);
            foreach (var page in pages)
            {
                index++;
                var (data, format) = GetPageImage(page, settings, cancellationToken);
                var name = index.ToString(CultureInfo.InvariantCulture).PadLeft(padding, '0') + FileFormat.FromImageFormat(format).DefaultExtension;
                using var content = new MemoryStream(data.ToArray(), writable: false);
                writer.AddFile(name, content, lastWriteTime: null, unixFileMode: null);
            }

            if (index is 0)
                throw new InvalidOperationException("The inputs have no page to write");
        });
    }

    private static (ReadOnlyMemory<byte> Data, ImageFormat Format) GetPageImage(ConversionPage page, ConversionSettings settings, CancellationToken cancellationToken)
    {
        var targetFormat = settings.ComicImageFormat switch
        {
            ComicImageFormat.Jpeg => ImageFormat.Jpeg,
            ComicImageFormat.Png => ImageFormat.Png,
            ComicImageFormat.WebP => ImageFormat.WebP,
            _ => ImageFormat.Unknown,
        };

        if (!settings.ResizesImages && page.GetEncodedImage() is { } encoded)
        {
            // The formats comic book readers support are copied as they are, unless the options of the encoder of the format
            // ask for another encoding (--jpeg-quality, --webp-lossless...)
            var keep = targetFormat is ImageFormat.Unknown
                ? encoded.Format is ImageFormat.Jpeg or ImageFormat.Png or ImageFormat.Gif or ImageFormat.WebP
                : encoded.Format == targetFormat && !settings.HasEncoderOptions(targetFormat);

            if (keep)
                return (encoded.Data, encoded.Format);
        }

        if (targetFormat is ImageFormat.Unknown)
        {
            targetFormat = ImageFormat.Png;
        }

        using var image = page.RenderImage(cancellationToken);
        image.AutoOrient(cancellationToken);
        settings.ResizeImage(image, cancellationToken);
        using var output = new MemoryStream();
        image.Save(output, settings.CreateEncoder(targetFormat, extension: null));
        return (output.ToArray(), targetFormat);
    }
}
