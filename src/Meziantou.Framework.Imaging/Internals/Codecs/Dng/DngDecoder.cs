using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Identifies and decodes DNG files, which the TIFF codec recognizes by the <c>DNGVersion</c> tag of their first image
/// file directory (<see cref="TiffCodec"/>): the raw image is rendered by <see cref="DngPipeline"/> to scene-referred
/// linear light (<see cref="PixelFormat.RgbaFloat"/>, the primaries of ITU-R BT.709 and the D65 white point, labeled
/// <see cref="ColorTransferFunction.Linear"/>), the size of its default crop.
/// </summary>
/// <remarks>
/// The orientation of IFD 0 is metadata, never applied, as for TIFF. The exceptions of the TIFF reader that the raw image
/// raises are reported for the DNG format.
/// </remarks>
internal static class DngDecoder
{
    /// <summary>Identifies a DNG file.</summary>
    public static ImageInfo Identify(RandomAccessSource source, TiffHeader header, TiffDirectory ifd0, ImageIdentifyMode mode, ImageCodecContext context)
        => Translate(() =>
        {
            var document = DngDocument.Read(source, header, ifd0, context);
            using var raw = DngRawDescriptor.Resolve(document.RawImage, context);
            _ = DngColorProfile.Read(document.Ifd0, raw.Planes).GetCameraToOutput();
            var metadata = ReadMetadata(document, context);
            if (mode == ImageIdentifyMode.FullScan)
            {
                context.Tracker.ChargeScannedFrame();
            }

            return new ImageInfo(
                ImageFormat.Dng,
                raw.DefaultCrop.Size,
                DefaultPixelFormats.ForDng(raw.Planes == 1),
                raw.Planes == 1 ? ImageColorModel.Grayscale : ImageColorModel.Rgb,
                bitsPerComponent: 32,
                frameCount: 1,
                isAnimated: false,
                hasPosterFrame: false,
                mayHaveTransparency: false,
                animation: null,
                metadata,
                mode,
                collectionEntryCount: null);
        });

    /// <summary>Decodes the raw image of a DNG file.</summary>
    public static Image Decode(RandomAccessSource source, TiffHeader header, TiffDirectory ifd0, ImageDecodeRequest request, ImageCodecContext context)
        => Translate(() =>
        {
            var document = DngDocument.Read(source, header, ifd0, context);
            if (request.DngDirectoryIndex is { } index)
                return DecodeDirectory(source, document, index, request, context);

            request = request.ForFullResolution(ImageFormat.Dng, supportsRegion: true);
            using var raw = DngRawDescriptor.Resolve(document.RawImage, context);
            var matrix = DngColorProfile.Read(document.Ifd0, raw.Planes).GetCameraToOutput();
            var metadata = ReadMetadata(document, context);
            var area = request.GetRegion(raw.DefaultCrop.Size, ImageFormat.Dng);
            using var sink = DecodedFrameSink.Create(context, request, ImageFormat.Dng, area.Size, DefaultPixelFormats.ForDng(raw.Planes == 1), PixelFormat.RgbaFloat, iccProfile: null);
            using var stage3 = Render(source, raw, context);
            Write(sink, stage3, raw.DefaultCrop, area, matrix, context);
            return sink.Build(metadata, animation: null);
        });

    /// <summary>Decodes an image file directory of the SubIFD tree other than the raw image (a preview) as a TIFF page.</summary>
    private static Image DecodeDirectory(RandomAccessSource source, DngDocument document, int index, ImageDecodeRequest request, ImageCodecContext context)
    {
        if (index < 0 || index >= document.Directories.Count)
            throw new InvalidOperationException("The image file directory of the DNG file does not exist.");

        var directory = document.Directories[index];
        var page = TiffPage.Resolve(directory.Directory, index, context);
        var pageRequest = request with { DngDirectoryIndex = null };
        return TiffPageDecoder.Decode(source, page, pageRequest.ForFullResolution(ImageFormat.Tiff, supportsRegion: true), context);
    }

    /// <summary>Runs stages 1 to 3 of the pipeline.</summary>
    public static DngPlanes Render(RandomAccessSource source, DngRawDescriptor raw, ImageCodecContext context)
    {
        DngPlanes stage2;
        using (var stage1 = DngPipeline.ReadStage1(source, raw, context))
        {
            stage2 = DngPipeline.Linearize(stage1, raw, context);
        }

        try
        {
            var stage3 = DngPipeline.Demosaic(stage2, raw, context);
            if (!ReferenceEquals(stage3, stage2))
            {
                stage2.Dispose();
            }

            return stage3;
        }
        catch
        {
            stage2.Dispose();
            throw;
        }
    }

    /// <summary>Reads the metadata of a DNG file: its orientation, XMP packet, EXIF data and DNG properties, and the linear label of the rendering.</summary>
    public static ImageMetadata ReadMetadata(DngDocument document, ImageCodecContext context)
    {
        var ifd0 = document.Ifd0;
        var builder = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Dng);
        if (ifd0.TryGetUnsigned(TiffFormat.OrientationTag, out var orientation) && orientation is >= 1 and <= 8)
        {
            builder.Metadata.Orientation = (ExifOrientation)orientation;
        }

        if (ifd0.Find(TiffFormat.XmpTag) is { } xmpField && xmpField.Count <= (ulong)context.Limits.MaxMetadataBytes && ifd0.TryReadBytes(TiffFormat.XmpTag, context.Limits.MaxMetadataBytes) is { } xmp)
        {
            builder.TryAdoptXmp(xmp);
        }

        if (DngExifExtractor.TryExtract(document, context.Limits.MaxMetadataBytes) is { } exif)
        {
            builder.TryAdoptExif(exif, adoptOrientation: false);
        }

        var metadata = builder.Metadata;
        metadata.TransferFunction = ColorTransferFunction.Linear;
        metadata.DngProperties = ReadProperties(document);
        return metadata;
    }

    private static DngProperties ReadProperties(DngDocument document)
    {
        var ifd0 = document.Ifd0;
        var version = document.Version;
        var backward = document.BackwardVersion;
        return new DngProperties(
            new Version(version[0], version[1], version[2], version[3]),
            new Version(backward[0], backward[1], backward[2], backward[3]),
            DngTagReader.TryReadString(ifd0, DngFormat.UniqueCameraModelTag, "UniqueCameraModel"),
            DngTagReader.TryReadString(ifd0, DngFormat.LocalizedCameraModelTag, "LocalizedCameraModel"),
            DngTagReader.TryReadString(ifd0, DngFormat.MakeTag, "Make"),
            DngTagReader.TryReadString(ifd0, DngFormat.ModelTag, "Model"),
            DngTagReader.TryReadString(ifd0, DngFormat.CameraSerialNumberTag, "CameraSerialNumber"),
            DngTagReader.TryReadString(ifd0, DngFormat.ProfileNameTag, "ProfileName"),
            TryReadNumber(ifd0, DngFormat.BaselineExposureTag) ?? 0);
    }

    private static double? TryReadNumber(TiffDirectory directory, ushort tag)
    {
        try
        {
            return DngTagReader.TryReadNumber(directory, tag, "BaselineExposure");
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
    }

    /// <summary>Writes the default crop of stage 3, transformed to the output colors, into the image.</summary>
    private static void Write(DecodedFrameSink sink, DngPlanes stage3, Rectangle crop, Rectangle area, DngMatrix matrix, ImageCodecContext context)
    {
        var planes = stage3.Planes;
        Span<double> m = stackalloc double[3 * planes];
        matrix.CopyTo(m);
        Image? staging = null;
        try
        {
            if (!sink.Plan.IsIdentity)
            {
                staging = new Image<RgbaFloat>(context.Configuration, area.Size, context.Scope, layoutOptions: null);
            }

            sink.BeginFrame(FrameDuration.Zero);
            using (var lease = staging is null ? sink.LeaseCurrentFrame() : staging.FrameCore(0).GetStorage().AcquireLease())
            {
                for (var y = 0; y < area.Height; y++)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var source = stage3.GetRow(crop.Y + area.Y + y).Slice((crop.X + area.X) * planes, area.Width * planes);
                    var row = unsafe(MemoryMarshal.Cast<byte, float>(lease.GetRowBytes(y)[..(area.Width * 16)]));
                    for (var x = 0; x < area.Width; x++)
                    {
                        var pixel = source.Slice(x * planes, planes);
                        for (var channel = 0; channel < 3; channel++)
                        {
                            var sum = 0.0;
                            for (var plane = 0; plane < planes; plane++)
                            {
                                sum += m[(channel * planes) + plane] * pixel[plane];
                            }

                            row[(x * 4) + channel] = (float)sum;
                        }

                        row[(x * 4) + 3] = 1;
                    }
                }
            }

            if (staging is not null)
            {
                // The peak of a tone mapping, and the samples a conversion rejects, are those of the whole image
                var frame = staging.FrameCore(0);
                Image.PrepareConversion(sink.Plan, [frame]);
                using var source = frame.GetStorage().AcquireLease();
                using var destination = sink.LeaseCurrentFrame();
                for (var y = 0; y < source.Height; y++)
                {
                    sink.WriteRow(destination, y, source.GetRowBytes(y));
                }
            }

            sink.EndImage();
        }
        finally
        {
            staging?.Dispose();
        }
    }

    /// <summary>Reports the errors of the TIFF reader as errors of the DNG format.</summary>
    private static T Translate<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (InvalidImageContentException exception) when (exception.Format == ImageFormat.Tiff)
        {
            throw DngFormat.Invalid(exception.Message, exception);
        }
        catch (UnsupportedImageFeatureException exception) when (exception.Format == ImageFormat.Tiff)
        {
            throw new UnsupportedImageFeatureException(exception.Message, ImageFormat.Dng, exception.Feature);
        }
    }
}
