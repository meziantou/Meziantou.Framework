namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Lists the images a DNG file stores next to its raw image (DNG specification 1.7.1, chapter 3, NewSubFileType): the
/// previews (1, a thumbnail when IFD 0 is one), the alternate previews (10001.H), the transparency masks (4, 5), the depth
/// maps (8, 9), the enhanced image (16) and the semantic masks (10004.H). Each is a TIFF image file directory, decoded as
/// a TIFF page (<see cref="DngDecoder"/>, with <see cref="ImageDecodeRequest.DngDirectoryIndex"/>).
/// </summary>
internal static class DngAuxiliaryScanner
{
    public static List<AuxiliaryImage> Scan(AuxiliaryImageCollection collection, byte[] data, ImageConfiguration configuration, CancellationToken cancellationToken)
    {
        var result = new List<AuxiliaryImage>();
        var context = new ImageCodecContext(configuration, "AuxiliaryImages", cancellationToken);
        using var input = RandomAccessInput.Create(data);
        var source = new RandomAccessSource(input, context, ImageFormat.Dng, chargeReads: false);
        DngDocument document;
        try
        {
            document = DngDocument.Read(source, context);
        }
        catch (InvalidImageContentException)
        {
            return result;
        }
        catch (UnsupportedImageFeatureException)
        {
            return result;
        }

        foreach (var directory in document.Directories)
        {
            if (directory.SubfileType == DngFormat.SubfileMain)
                continue;

            var kind = directory.SubfileType switch
            {
                DngFormat.SubfilePreview => AuxiliaryImageKind.Thumbnail,
                DngFormat.SubfileDepth or DngFormat.SubfileReducedDepth => AuxiliaryImageKind.Depth,
                DngFormat.SubfileSemanticMask => AuxiliaryImageKind.Matte,
                _ => AuxiliaryImageKind.Other,
            };

            if (!directory.Directory.TryGetUnsigned(TiffFormat.ImageWidthTag, out var width) || !directory.Directory.TryGetUnsigned(TiffFormat.ImageLengthTag, out var height)
                || width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
            {
                continue;
            }

            result.Add(new AuxiliaryImage(collection, kind, auxiliaryType: null, new Size((int)width, (int)height)) { DngDirectoryIndex = directory.Index });
        }

        return result;
    }
}
