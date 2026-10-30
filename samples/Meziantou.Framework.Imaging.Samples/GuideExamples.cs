using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>Examples of the documentation site (docs/Imaging/03-Guides), run by DocumentationExampleTests.</summary>
internal static class GuideExamples
{
    /// <summary>A thumbnail of a photo: upright, fitted to a box without enlarging, sRGB and without metadata.</summary>
    #region Imaging_CreateThumbnail
    public static void CreateThumbnail(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // A camera stores the rotation of the photo as metadata: turn the pixels upright before resizing
        image.AutoOrient();

        // Fits inside 200x200 and keeps the aspect ratio; an image that is already smaller keeps its size
        image.Resize(new ResizeOptions(200, 200) { Mode = ResizeMode.Contain, AllowUpscaling = false });

        // Stripping the metadata also removes the color profile: convert the colors to sRGB first, which is what
        // viewers assume for an image without profile. Nothing is converted when the image is already sRGB
        image.ConvertColorProfile(PixelFormats.IsGrayscale(image.PixelFormat) ? IccProfile.SrgbGray : IccProfile.Srgb);

        image.Save(outputPath, new JpegEncoder
        {
            Quality = 80,
            BackgroundColor = new Rgba32(255, 255, 255), // transparent pixels become white
            AllowBitDepthReduction = true,               // 16-bit images are written with 8 bits
            MetadataHandling = MetadataHandling.Strip,   // no EXIF (camera, GPS position), XMP or text
        });
    }
    #endregion

    /// <summary>Converting a file to the format of the extension of the output.</summary>
    #region Imaging_ConvertFile
    public static void ConvertFile(string inputPath, string outputPath)
    {
        // The format of the input comes from its content, the format of the output from the extension of outputPath
        using var image = Image.Load(inputPath);

        // Throws UnsupportedImageFeatureException, before writing anything, when the output cannot hold the image:
        // an animation, transparency, 16-bit samples or metadata the format does not store
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Converting every image of a folder to JPEG, and skipping the files that cannot be converted.</summary>
    #region Imaging_ConvertFolder
    public static IReadOnlyList<string> ConvertFolderToJpeg(string inputDirectory, string outputDirectory)
    {
        // JPEG stores one picture: decoding stops after the first frame of an animation. A CMYK JPEG is converted to
        // RGB; the other images are loaded as usual
        var decodeOptions = new ImageDecodeOptions { FrameLimit = 1, CmykConversion = CmykConversionOptions.Default };

        // Encoders are immutable: one instance serves every file. Each setting allows a loss that is otherwise rejected
        var encoder = new JpegEncoder
        {
            Quality = 85,
            BackgroundColor = new Rgba32(255, 255, 255),           // transparency
            AllowBitDepthReduction = true,                         // 16-bit samples
            MetadataHandling = MetadataHandling.DiscardUnsupported, // metadata JPEG cannot store
        };

        Directory.CreateDirectory(outputDirectory);
        var skipped = new List<string>();
        foreach (var inputPath in Directory.EnumerateFiles(inputDirectory))
        {
            try
            {
                using var image = Image.Load(inputPath, decodeOptions);

                // The first frame of an animation keeps the animation settings (and an APNG its poster): make it a
                // still image
                image.RemovePosterFrame();
                image.Animation = null;

                image.Save(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + ".jpg"), encoder);
            }
            catch (ImageException)
            {
                // Not an image, a corrupted file, or a feature that cannot be converted: nothing was written
                skipped.Add(inputPath);
            }
        }

        return skipped;
    }
    #endregion

    /// <summary>Changing the speed and the play count of an animation, with exact durations.</summary>
    #region Imaging_ChangeAnimationSpeed
    public static void PlayTwiceAsFast(string inputPath, string outputPath)
    {
        using var animation = Image.Load(inputPath);
        foreach (var frame in animation.Frames)
        {
            // Durations are exact fractions of a second: half of 1/30 s is exactly 1/60 s
            var duration = frame.Metadata.Duration;
            frame.Metadata.Duration = new FrameDuration(duration.Numerator, duration.Denominator * 2);
        }

        // Played three times in total, the first play included; TotalPlays = null loops forever
        animation.Animation = new AnimationMetadata { TotalPlays = 3 };

        // GIF stores hundredths of a second: its encoder rounds each duration to the nearest one by default
        animation.Save(outputPath);
    }
    #endregion

    /// <summary>A photo of a phone or a camera prepared for a web page: upright, resized, sRGB, without metadata.</summary>
    #region Imaging_PreparePhotoForWeb
    public static void PreparePhotoForWeb(string inputPath, string outputPath)
    {
        // HEIC, AVIF or JPEG: the format comes from the content of the file
        using var photo = Image.Load(inputPath);

        // The decoder reports the orientation of the camera and moves no pixel: turn the pixels upright
        photo.AutoOrient();

        // At most 2048 pixels on the long side; a smaller photo keeps its size
        photo.Resize(new ResizeOptions(2048, 2048) { AllowUpscaling = false });

        // Phones label their photos with a wider color space (Display P3), as an ICC profile or a color description.
        // Converting to sRGB gives the colors every browser shows. The tone mapping only applies to high dynamic range
        // samples (PQ or HLG), which it compresses into the range of sRGB; it is ignored for the other photos
        var options = new IccColorTransformOptions { ToneMapping = HdrToneMappingOptions.Default };
        photo.ConvertColorProfile(PixelFormats.IsGrayscale(photo.PixelFormat) ? IccProfile.SrgbGray : IccProfile.Srgb, options);

        // The pixels are upright and sRGB: the metadata (EXIF with the GPS position, XMP, the profile) can go
        photo.Save(outputPath, new JpegEncoder
        {
            Quality = 85,
            AllowBitDepthReduction = true, // 10-bit and 12-bit photos load with 16-bit samples
            MetadataHandling = MetadataHandling.Strip,
        });
    }
    #endregion

    /// <summary>A logo drawn in the bottom-right corner of an image, half transparent.</summary>
    #region Imaging_AddLogoWatermark
    public static void AddLogoWatermark(string inputPath, string logoPath, string outputPath)
    {
        using var image = Image.Load(inputPath);
        using var logo = Image.Load(logoPath);

        // The logo takes at most a quarter of the width and of the height of the image, whatever their sizes
        logo.Resize(new ResizeOptions(image.Width / 4, image.Height / 4) { AllowUpscaling = false });

        // At a position on whole pixels, the pixels of the logo are drawn as they are, faded by the opacity
        var margin = image.Width / 40;
        var position = new PointD(image.Width - logo.Width - margin, image.Height - logo.Height - margin);

        // Draw paints every frame of an animation identically
        image.Draw(canvas => canvas.DrawImage(logo.Frames[0], position, opacity: 0.6));
        image.Save(outputPath);
    }
    #endregion

    /// <summary>An uploaded file checked before it is decoded, decoded within limits, and re-encoded.</summary>
    #region Imaging_ProcessUpload
    public static string? SaveUpload(Stream upload, string outputPath)
    {
        // Safety bounds for this input: a file that exceeds one is rejected before the memory is allocated
        var configuration = new ImageConfiguration
        {
            Limits = new ImageResourceLimits
            {
                MaxEncodedBytes = 20 * 1024 * 1024,
                MaxWidth = 8192,
                MaxHeight = 8192,
                MaxFramePixels = 40_000_000,
                MaxLiveAllocationBytes = 256 * 1024 * 1024,
            },
        };

        try
        {
            // Reads the headers only, without decoding any pixel. The bytes it reads are consumed: remember the position
            var start = upload.Position;
            var info = Image.Identify(upload, new ImageIdentifyOptions { Configuration = configuration });
            if (info.Format is not (ImageFormat.Jpeg or ImageFormat.Png or ImageFormat.Gif or ImageFormat.WebP or ImageFormat.Heic or ImageFormat.Avif))
                return $"The {info.Format} format is not accepted";

            if (info.Width < 16 || info.Height < 16)
                return "The image is too small";

            // Decodes from the start again; the first frame of an animation is enough
            upload.Position = start;
            using var image = Image.Load(upload, new ImageDecodeOptions { Configuration = configuration, FrameLimit = 1 });
            image.RemovePosterFrame();
            image.Animation = null;

            // Re-encoding keeps the pixels and nothing else: data hidden in the file, its metadata (once the orientation
            // and the colors are applied to the pixels) and any bytes after the image are gone
            image.AutoOrient();
            var options = new IccColorTransformOptions { ToneMapping = HdrToneMappingOptions.Default };
            image.ConvertColorProfile(PixelFormats.IsGrayscale(image.PixelFormat) ? IccProfile.SrgbGray : IccProfile.Srgb, options);
            image.Save(outputPath, new WebPEncoder
            {
                Compression = WebPCompression.Lossy,
                Quality = 80,
                AllowBitDepthReduction = true,
                MetadataHandling = MetadataHandling.Strip,
            });
            return null;
        }
        catch (UnknownImageFormatException)
        {
            return "The file is not an image";
        }
        catch (ImageResourceLimitException ex)
        {
            return $"The image is too large ({ex.Kind})";
        }
        catch (InvalidImageContentException)
        {
            return "The file is corrupted or truncated";
        }
        catch (UnsupportedImageFeatureException ex)
        {
            return $"The image uses an unsupported feature ({ex.Feature})";
        }
    }
    #endregion

    /// <summary>Removing the private metadata of a photo (EXIF with its GPS position, XMP, text) and keeping its colors.</summary>
    #region Imaging_RemovePrivateMetadata
    public static void RemovePrivateMetadata(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // The orientation is stored in the EXIF data: apply it to the pixels before removing EXIF
        image.AutoOrient();

        // EXIF holds the GPS position, the date and the camera; XMP can hold the same and more
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.TextEntries.Clear();

        // The ICC profile is kept: it describes the colors of the pixels, and holds nothing about the photographer
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Removing every optional metadata from the output, after applying what changes how the pixels look.</summary>
    #region Imaging_StripAllMetadata
    public static void StripAllMetadata(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // Strip drops the orientation and the color profile too: apply them to the pixels first, so that the image
        // looks the same without them
        image.AutoOrient();
        image.ConvertColorProfile(PixelFormats.IsGrayscale(image.PixelFormat) ? IccProfile.SrgbGray : IccProfile.Srgb);

        image.Save(outputPath, new PngEncoder { MetadataHandling = MetadataHandling.Strip });
    }
    #endregion
}
