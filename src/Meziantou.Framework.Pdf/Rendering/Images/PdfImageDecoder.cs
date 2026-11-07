using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Colors;
using Meziantou.Framework.Pdf.Rendering.Functions;

namespace Meziantou.Framework.Pdf.Rendering.Images;

/// <summary>
/// Decodes the image XObjects and inline images of a document (ISO 32000-2, 8.9) to sRGB with alpha: the samples of
/// every bit depth and color space with their decode array, the stencil masks, the explicit and color key masks, the
/// soft masks. The JPEG and JPEG 2000 images are decoded by the codecs of Meziantou.Framework.Imaging.
/// </summary>
internal static class PdfImageDecoder
{
    // The pixels of an image: the limit of a frame of Imaging
    private const long MaxPixels = 100_000_000;

    // The samples of an image of several components, unpacked
    private const long MaxSamples = 256_000_000;

    public static DecodedImage? Decode(PdfStream stream, PdfDictionary? resources, RenderCache cache)
    {
        var dictionary = stream.Dictionary;
        var filters = PdfFilterPipeline.GetFilterNames(dictionary);
        var last = filters.Count > 0 ? filters[^1].Value : null;
        byte[] data;
        if (last is "DCTDecode" or "JPXDecode")
        {
            // The compressed image: decoded by Imaging, with its own color management
            data = PdfFilterPipeline.Decode(stream, stopBeforeImageFilters: true, out _);
            return DecodeCompressed(data, dictionary, resources, cache, isJpx: last == "JPXDecode");
        }

        data = stream.GetDecodedData();
        return DecodeSamples(data, dictionary, resources, cache);
    }

    public static DecodedImage? DecodeInline(PdfDictionary inline, ReadOnlyMemory<byte> encoded, PdfDictionary? resources, PdfDecodeLimits limits, RenderCache cache)
    {
        // The abbreviations of inline images (8.9.7)
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
            dictionary[PdfName.Create(name)] = value;
        }

        var filters = PdfFilterPipeline.GetFilterNames(dictionary);
        var last = filters.Count > 0 ? filters[^1].Value : null;
        var stopBeforeImage = last is "DCTDecode" or "JPXDecode";
        var data = PdfFilterPipeline.Decode(encoded, dictionary, limits, stopBeforeImage, out _);
        if (stopBeforeImage)
            return DecodeCompressed(data, dictionary, resources, cache, isJpx: last == "JPXDecode");

        return DecodeSamples(data, dictionary, resources, cache);
    }

    private static DecodedImage? DecodeCompressed(byte[] data, PdfDictionary dictionary, PdfDictionary? resources, RenderCache cache, bool isJpx)
    {
        var options = new ImageDecodeOptions
        {
            Configuration = cache.Options.Configuration ?? ImageConfiguration.Default,
            CmykConversion = GetCmykConversion(dictionary),
        };

        // The image Imaging decodes is the image drawn: its pixels are not copied
        var image = Image.Load<Rgba32>(data, options);
        try
        {
            cache.Charge(image.Width * image.Height / 1024);
            ApplyEmbeddedOpacity(image, dictionary, isJpx);
            ApplyMasks(image, dictionary, cache, RemoveMatte(image, dictionary, resources, cache));
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return Finish(image, dictionary, cache);
    }

    /// <summary>
    /// Applies the opacity of a JPEG 2000 image (8.9.5): ignored, unless <c>/SMaskInData</c> is 1, or 2 for colors
    /// premultiplied by it, and the image has no <c>/SMask</c>, which replaces it.
    /// </summary>
    private static void ApplyEmbeddedOpacity(Image<Rgba32> image, PdfDictionary dictionary, bool isJpx)
    {
        var smaskInData = isJpx && dictionary.GetResolved(PdfNames.SMask) is null ? dictionary.GetInteger(PdfName.Create("SMaskInData"), 0) : 0;
        if (smaskInData is not (0 or 2))
            return;

        image.Frames[0].ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                foreach (ref var pixel in rows.GetRowSpan(y))
                {
                    if (smaskInData is 0)
                    {
                        pixel.A = 255;
                    }
                    else if (pixel.A is > 0 and < 255)
                    {
                        pixel.R = Unpremultiply(pixel.R, pixel.A);
                        pixel.G = Unpremultiply(pixel.G, pixel.A);
                        pixel.B = Unpremultiply(pixel.B, pixel.A);
                    }
                }
            }
        });
    }

    private static byte Unpremultiply(byte value, byte alpha) => (byte)Math.Min(255, ((value * 255) + (alpha / 2)) / alpha);

    /// <summary>Gets the matte color of the soft mask of an image (11.6.5.3): the color its samples were premultiplied with, in the color space of the image.</summary>
    private static double[]? GetMatte(PdfDictionary dictionary, out PdfStream? softMask)
    {
        softMask = dictionary.GetResolved(PdfNames.SMask) as PdfStream;
        return RenderFunction.ReadNumbers(softMask?.Dictionary.Get<PdfArray>(PdfName.Create("Matte"))) is { Length: > 0 } matte && matte.All(double.IsFinite) ? matte : null;
    }

    /// <summary>
    /// Removes the matte color of the soft mask from the 8-bit colors of a compressed image, in sRGB: c = m + (c' - m) / alpha
    /// (11.6.5.3). Returns the alpha of the soft mask when it was decoded, <see langword="null"/> otherwise.
    /// </summary>
    private static byte[]? RemoveMatte(Image<Rgba32> image, PdfDictionary dictionary, PdfDictionary? resources, RenderCache cache)
    {
        if (GetMatte(dictionary, out var softMask) is not { } matte)
            return null;

        RgbColor color;
        try
        {
            if (dictionary.GetResolved(PdfNames.ColorSpace) is not { } colorSpaceObject || RenderColorSpace.Read(colorSpaceObject, resources, cache) is not { } colorSpace || colorSpace.ComponentCount != matte.Length)
                return null;

            color = colorSpace.ToRgb(matte);
        }
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException)
        {
            return null;
        }

        if (DecodeMaskSamples(softMask!, cache, image.Width, image.Height, stencil: false) is not { } alpha)
            return null;

        var (r, g, b) = (color.R * 255, color.G * 255, color.B * 255);
        image.Frames[0].ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var value = alpha[(y * rows.Width) + x];
                    if (value is 0 or 255)
                        continue;

                    var a = value / 255.0;
                    ref var pixel = ref row[x];
                    pixel.R = ToByte(r + ((pixel.R - r) / a));
                    pixel.G = ToByte(g + ((pixel.G - g) / a));
                    pixel.B = ToByte(b + ((pixel.B - b) / a));
                }
            }
        });
        return alpha;

        static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
    }

    private static DecodedImage? DecodeSamples(byte[] data, PdfDictionary dictionary, PdfDictionary? resources, RenderCache cache)
    {
        var width = (int)Math.Clamp(dictionary.GetInteger(PdfNames.Width, 0), 0, int.MaxValue);
        var height = (int)Math.Clamp(dictionary.GetInteger(PdfNames.Height, 0), 0, int.MaxValue);
        if (width == 0 || height == 0)
            return null;

        if ((long)width * height > MaxPixels)
            throw new PdfResourceLimitException($"The image is {width} x {height} pixels");

        var isStencil = dictionary.GetBoolean(PdfName.Create("ImageMask"));
        var bitsPerComponent = isStencil ? 1 : (int)dictionary.GetInteger(PdfNames.BitsPerComponent, 8);
        if (bitsPerComponent is not (1 or 2 or 4 or 8 or 16))
            throw new InvalidPdfContentException($"The image has {bitsPerComponent} bits per component");

        var interpolate = dictionary.GetBoolean(PdfName.Create("Interpolate"));
        if (isStencil)
        {
            // A stencil mask: with the default decode array, a sample of 0 marks the painted area (8.9.6.2)
            var decode = RenderFunction.ReadNumbers(dictionary.Get<PdfArray>(PdfNames.Decode));
            var paintedValue = decode is { Length: >= 2 } && decode[0] > decode[1] ? 1 : 0;
            var stencil = new Rgba32[width * height];
            var stride = (width + 7) / 8;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = (y * stride) + (x >> 3);
                    var bit = index < data.Length ? (data[index] >> (7 - (x & 7))) & 1 : 1 - paintedValue;
                    stencil[(y * width) + x] = bit == paintedValue ? new Rgba32(255, 255, 255, 255) : default;
                }
            }

            return new DecodedImage(Track(cache, Image.ImportPixelData<Rgba32>(stencil, width, height, configuration: cache.Options.Configuration)), isStencil: true, interpolate);
        }

        var colorSpaceObject = dictionary.GetResolved(PdfNames.ColorSpace);
        var colorSpace = colorSpaceObject is null ? RenderColorSpace.Gray : RenderColorSpace.Read(colorSpaceObject, resources, cache);
        var components = colorSpace.ComponentCount;
        if (components == 0)
            return null;

        var decodeArray = RenderFunction.ReadNumbers(dictionary.Get<PdfArray>(PdfNames.Decode));
        if (decodeArray is null || decodeArray.Length < components * 2)
        {
            decodeArray = colorSpace.GetDefaultDecode(bitsPerComponent);
        }

        // The samples are decoded row by row to the pixels of the image: an image of 8-bit samples does not need more
        // memory than its pixels
        var sampleCount = (long)width * height * components;
        if (sampleCount > MaxSamples)
            throw new PdfResourceLimitException($"The image has {sampleCount} samples");

        cache.Charge(sampleCount / 256);
        var rowBytes = (((long)width * components * bitsPerComponent) + 7) / 8;
        var maxValue = (1 << bitsPerComponent) - 1;
        var colorKey = ReadColorKey(dictionary, components);

        // The matte color of the soft mask: the samples were mixed with it, c' = m + alpha (c - m), and are restored
        // before they are converted (11.6.5.3)
        var matte = GetMatte(dictionary, out var softMask);
        var alpha = matte is not null && matte.Length == components ? DecodeMaskSamples(softMask!, cache, width, height, stencil: false) : null;
        var image = new Image<Rgba32>(width, height, cache.Options.Configuration);
        try
        {
            image.Frames[0].ProcessPixelRows(rows =>
            {
                if (bitsPerComponent <= 8 && components == 1 && alpha is null)
                {
                    // The colors: a lookup per sample value for the bit depths up to 8
                    var lookup = new Rgba32[maxValue + 1];
                    for (var value = 0; value <= maxValue; value++)
                    {
                        lookup[value] = ToPixel(colorSpace.ToRgb([Decode(value, maxValue, decodeArray[0], decodeArray[1])]));
                    }

                    for (var y = 0; y < height; y++)
                    {
                        var row = rows.GetRowSpan(y);
                        var rowStart = y * rowBytes;
                        for (var x = 0; x < width; x++)
                        {
                            var sample = ReadSample(data, rowStart, x, bitsPerComponent);
                            row[x] = lookup[sample];
                            if (colorKey is not null && sample >= colorKey[0] && sample <= colorKey[1])
                            {
                                row[x].A = 0;
                            }
                        }
                    }

                    return;
                }

                // A conversion per pixel otherwise, a few rows at a time
                const int RowsPerBatch = 16;
                var rowSamples = width * components;
                var batch = new double[rowSamples * RowsPerBatch];
                var batchRgb = new byte[width * 3 * RowsPerBatch];
                var keyed = colorKey is null ? null : new bool[width * RowsPerBatch];
                for (var y = 0; y < height; y += RowsPerBatch)
                {
                    var rowCount = Math.Min(RowsPerBatch, height - y);
                    var count = width * rowCount;
                    for (var r = 0; r < rowCount; r++)
                    {
                        var rowStart = (y + r) * rowBytes;
                        for (var i = 0; i < rowSamples; i++)
                        {
                            var component = i % components;
                            var sample = ReadSample(data, rowStart, i, bitsPerComponent);
                            var decoded = Decode(sample, maxValue, decodeArray[2 * component], decodeArray[(2 * component) + 1]);
                            if (alpha is not null && alpha[((y + r) * width) + (i / components)] is > 0 and var pixelAlpha)
                            {
                                decoded = matte![component] + ((decoded - matte[component]) * 255 / pixelAlpha);
                            }

                            batch[(r * rowSamples) + i] = decoded;
                            if (keyed is not null)
                            {
                                // A color key mask: the pixels whose samples are all in the ranges are not painted (8.9.6.4)
                                var pixel = (r * width) + (i / components);
                                var inRange = sample >= colorKey![2 * component] && sample <= colorKey[(2 * component) + 1];
                                keyed[pixel] = component == 0 ? inRange : keyed[pixel] && inRange;
                            }
                        }
                    }

                    colorSpace.ToRgb(batch.AsSpan(0, count * components), batchRgb.AsSpan(0, count * 3));
                    for (var r = 0; r < rowCount; r++)
                    {
                        var row = rows.GetRowSpan(y + r);
                        for (var x = 0; x < width; x++)
                        {
                            var i = (r * width) + x;
                            row[x] = new Rgba32(batchRgb[3 * i], batchRgb[(3 * i) + 1], batchRgb[(3 * i) + 2], keyed is not null && keyed[i] ? (byte)0 : (byte)255);
                        }
                    }

                    cache.Charge(count / 1024);
                }
            });

            ApplyMasks(image, dictionary, cache, alpha);
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return Finish(image, dictionary, cache);
    }

    /// <summary>
    /// Reads the ranges of a color key mask (8.9.6.4), once for the pixels of the image; <see langword="null"/> when the
    /// image has none, or a soft mask or an explicit mask, which replace it.
    /// </summary>
    private static double[]? ReadColorKey(PdfDictionary dictionary, int components)
    {
        if (dictionary.GetResolved(PdfNames.SMask) is PdfStream || dictionary.GetResolved(PdfNames.Mask) is PdfStream)
            return null;

        if (dictionary.Get<PdfArray>(PdfNames.Mask) is not { } colorKey || colorKey.Count < components * 2)
            return null;

        var ranges = new double[components * 2];
        for (var i = 0; i < ranges.Length; i++)
        {
            ranges[i] = colorKey.GetNumber(i);
        }

        return ranges;
    }

    /// <summary>
    /// The conversion of the inks of a CMYK or YCCK image to sRGB: Imaging loads them only with one. Imaging reads the
    /// samples as Adobe applications write them (255 is no ink), which is what the inverted decode array of these images
    /// says (Adobe Technical Note #5116). The profile embedded in the file is used first; the CMYK profile of an ICCBased
    /// color space of the image (8.6.5.5) is used when the file has none, and the CMYK profile of DeviceCMYK otherwise.
    /// </summary>
    private static CmykConversionOptions GetCmykConversion(PdfDictionary dictionary)
    {
        if (dictionary.GetResolved(PdfNames.ColorSpace) is PdfArray { Count: > 1 } array && array.GetResolved(0) is PdfName { Value: "ICCBased" } && array.GetResolved(1) is PdfStream stream && stream.Dictionary.GetInteger(PdfNames.N, 0) == 4)
        {
            try
            {
                var profile = new IccProfile(new MetadataBlob(stream.GetDecodedData()));
                if (profile.ColorSpace == IccProfileColorSpace.Cmyk)
                    return new CmykConversionOptions { AssumedSourceProfile = profile };
            }
            catch (Exception ex) when (ex is InvalidImageContentException or UnsupportedImageFeatureException or InvalidPdfContentException or ArgumentException)
            {
            }
        }

        return CmykConversionOptions.Default;
    }

    /// <summary>Applies the soft mask or the explicit mask of an image (8.9.6) to the alpha of its pixels; the alpha of the soft mask when it was decoded already.</summary>
    private static void ApplyMasks(Image<Rgba32> image, PdfDictionary dictionary, RenderCache cache, byte[]? softMaskAlpha)
    {
        var alpha = softMaskAlpha ?? (dictionary.GetResolved(PdfNames.SMask) is PdfStream softMask
            ? DecodeMaskSamples(softMask, cache, image.Width, image.Height, stencil: false)
            : dictionary.GetResolved(PdfNames.Mask) is PdfStream explicitMask ? DecodeMaskSamples(explicitMask, cache, image.Width, image.Height, stencil: true) : null);
        if (alpha is null)
            return;

        image.Frames[0].ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                var offset = y * rows.Width;
                for (var x = 0; x < row.Length; x++)
                {
                    row[x].A = (byte)((row[x].A * alpha[offset + x]) / 255);
                }
            }
        });
    }

    private static DecodedImage Finish(Image<Rgba32> image, PdfDictionary dictionary, RenderCache cache)
    {
        var interpolate = dictionary.GetBoolean(PdfName.Create("Interpolate"));
        return new DecodedImage(Track(cache, image), isStencil: false, interpolate);
    }

    /// <summary>Decodes a soft mask (its gray values) or an explicit mask (a stencil: 0 shows the image) to alpha values at the size of the image.</summary>
    private static byte[]? DecodeMaskSamples(PdfStream mask, RenderCache cache, int width, int height, bool stencil)
    {
        var dictionary = mask.Dictionary;
        var maskWidth = (int)Math.Clamp(dictionary.GetInteger(PdfNames.Width, 0), 0, int.MaxValue);
        var maskHeight = (int)Math.Clamp(dictionary.GetInteger(PdfNames.Height, 0), 0, int.MaxValue);
        if (maskWidth == 0 || maskHeight == 0 || (long)maskWidth * maskHeight > MaxPixels)
            return null;

        byte[] values;
        var filters = PdfFilterPipeline.GetFilterNames(dictionary);
        if (filters.Count > 0 && filters[^1].Value is "DCTDecode" or "JPXDecode")
        {
            var data = PdfFilterPipeline.Decode(mask, stopBeforeImageFilters: true, out _);
            using var image = Image.Load<Gray8>(data, new ImageDecodeOptions { Configuration = cache.Options.Configuration ?? ImageConfiguration.Default });
            maskWidth = image.Width;
            maskHeight = image.Height;
            var gray = new Gray8[maskWidth * maskHeight];
            image.Frames[0].CopyPixelDataTo(gray);
            values = [.. gray.Select(pixel => pixel.Value)];
        }
        else
        {
            var data = mask.GetDecodedData();
            var bitsPerComponent = stencil ? 1 : (int)dictionary.GetInteger(PdfNames.BitsPerComponent, 8);
            if (bitsPerComponent is not (1 or 2 or 4 or 8 or 16))
                return null;

            var decode = RenderFunction.ReadNumbers(dictionary.Get<PdfArray>(PdfNames.Decode));
            var low = decode is { Length: >= 2 } ? decode[0] : 0;
            var high = decode is { Length: >= 2 } ? decode[1] : 1;
            var maxValue = (1 << bitsPerComponent) - 1;
            var rowBytes = (((long)maskWidth * bitsPerComponent) + 7) / 8;
            values = new byte[maskWidth * maskHeight];
            for (var y = 0; y < maskHeight; y++)
            {
                for (var x = 0; x < maskWidth; x++)
                {
                    var sample = ReadSample(data, y * rowBytes, x, bitsPerComponent);
                    var value = Decode(sample, maxValue, low, high);

                    // An explicit mask paints where its decoded sample is 0
                    values[(y * maskWidth) + x] = stencil ? (value < 0.5 ? (byte)255 : (byte)0) : RenderColorSpace.ToByte(value);
                }
            }
        }

        if (maskWidth == width && maskHeight == height)
            return values;

        // A mask of another size covers the same unit square: it is sampled at the centers of the pixels of the image
        var result = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var maskY = Math.Min(maskHeight - 1, (int)(((y + 0.5) * maskHeight) / height));
            for (var x = 0; x < width; x++)
            {
                var maskX = Math.Min(maskWidth - 1, (int)(((x + 0.5) * maskWidth) / width));
                result[(y * width) + x] = values[(maskY * maskWidth) + maskX];
            }
        }

        cache.Charge((long)width * height / 1024);
        return result;
    }

    private static int ReadSample(byte[] data, long rowStart, int index, int bitsPerComponent)
    {
        switch (bitsPerComponent)
        {
            case 8:
                {
                    var position = rowStart + index;
                    return position < data.Length ? data[position] : 0;
                }

            case 16:
                {
                    var position = rowStart + (2L * index);
                    return position + 1 < data.Length ? (data[position] << 8) | data[position + 1] : 0;
                }

            default:
                {
                    var bit = (long)index * bitsPerComponent;
                    var position = rowStart + (bit >> 3);
                    if (position >= data.Length)
                        return 0;

                    var shift = 8 - bitsPerComponent - (int)(bit & 7);
                    return (data[position] >> shift) & ((1 << bitsPerComponent) - 1);
                }
        }
    }

    private static double Decode(int sample, int maxValue, double low, double high) => low + (sample * (high - low) / maxValue);

    private static Rgba32 ToPixel(RgbColor color) => new(RenderColorSpace.ToByte(color.R), RenderColorSpace.ToByte(color.G), RenderColorSpace.ToByte(color.B), 255);

    private static Image<Rgba32> Track(RenderCache cache, Image<Rgba32> image) => cache.Track(image);
}
