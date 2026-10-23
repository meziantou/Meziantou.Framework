using Meziantou.Framework.Imaging;
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
            return DecodeCompressed(data, dictionary, cache);
        }

        data = stream.GetDecodedData();
        return DecodeSamples(data, dictionary, resources, cache);
    }

    public static DecodedImage? DecodeInline(PdfDictionary inline, ReadOnlyMemory<byte> encoded, PdfDictionary? resources, RenderCache cache)
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
        var parameters = PdfFilterPipeline.GetParameters(dictionary, filters.Count);
        var last = filters.Count > 0 ? filters[^1].Value : null;
        var stopBeforeImage = last is "DCTDecode" or "JPXDecode";
        var data = PdfFilterPipeline.Decode(encoded.Span, filters, parameters, PdfDecodeLimits.Default, stopBeforeImage, out _);
        if (stopBeforeImage)
            return DecodeCompressed(data, dictionary, cache);

        return DecodeSamples(data, dictionary, resources, cache);
    }

    private static DecodedImage? DecodeCompressed(byte[] data, PdfDictionary dictionary, RenderCache cache)
    {
        using var image = Image.Load<Rgba32>(data, new ImageDecodeOptions { Configuration = cache.Options.Configuration ?? ImageConfiguration.Default });
        var pixels = new Rgba32[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        cache.Charge(pixels.Length / 1024);
        return Finish(pixels, image.Width, image.Height, dictionary, cache, colorKey: null, samples: null);
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

        // The samples, unpacked: one per component, rows on whole bytes
        var rowBytes = (((long)width * components * bitsPerComponent) + 7) / 8;
        var samples = new int[width * height * components];
        var maxValue = (1 << bitsPerComponent) - 1;
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * rowBytes;
            for (var i = 0; i < width * components; i++)
            {
                samples[(y * width * components) + i] = ReadSample(data, rowStart, i, bitsPerComponent);
            }
        }

        // The colors: a lookup per component value for the bit depths up to 8, a conversion per pixel otherwise
        var pixels = new Rgba32[width * height];
        if (bitsPerComponent <= 8 && components == 1)
        {
            var lookup = new Rgba32[maxValue + 1];
            for (var value = 0; value <= maxValue; value++)
            {
                lookup[value] = ToPixel(colorSpace.ToRgb([Decode(value, maxValue, decodeArray[0], decodeArray[1])]));
            }

            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = lookup[samples[i]];
            }
        }
        else
        {
            const int RowsPerBatch = 16;
            var batch = new double[width * components * RowsPerBatch];
            var batchRgb = new byte[width * 3 * RowsPerBatch];
            for (var y = 0; y < height; y += RowsPerBatch)
            {
                var rows = Math.Min(RowsPerBatch, height - y);
                var count = width * rows;
                for (var i = 0; i < count * components; i++)
                {
                    var component = i % components;
                    batch[i] = Decode(samples[(y * width * components) + i], maxValue, decodeArray[2 * component], decodeArray[(2 * component) + 1]);
                }

                colorSpace.ToRgb(batch.AsSpan(0, count * components), batchRgb.AsSpan(0, count * 3));
                for (var i = 0; i < count; i++)
                {
                    pixels[(y * width) + i] = new Rgba32(batchRgb[3 * i], batchRgb[(3 * i) + 1], batchRgb[(3 * i) + 2], 255);
                }

                cache.Charge(count / 1024);
            }
        }

        var colorKey = dictionary.Get<PdfArray>(PdfNames.Mask);
        return Finish(pixels, width, height, dictionary, cache, colorKey, samples);
    }

    /// <summary>Applies the masks of an image (8.9.6): a soft mask, an explicit mask or a color key mask.</summary>
    private static DecodedImage Finish(Rgba32[] pixels, int width, int height, PdfDictionary dictionary, RenderCache cache, PdfArray? colorKey, int[]? samples)
    {
        if (dictionary.GetResolved(PdfNames.SMask) is PdfStream softMask)
        {
            var alpha = DecodeMaskSamples(softMask, cache, width, height, stencil: false);
            if (alpha is not null)
            {
                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i].A = (byte)((pixels[i].A * alpha[i]) / 255);
                }
            }
        }
        else if (dictionary.GetResolved(PdfNames.Mask) is PdfStream explicitMask)
        {
            var alpha = DecodeMaskSamples(explicitMask, cache, width, height, stencil: true);
            if (alpha is not null)
            {
                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i].A = (byte)((pixels[i].A * alpha[i]) / 255);
                }
            }
        }
        else if (colorKey is not null && samples is not null)
        {
            // A color key mask: the pixels whose samples are all in the ranges are not painted (8.9.6.4)
            var components = samples.Length / pixels.Length;
            if (colorKey.Count >= components * 2)
            {
                for (var i = 0; i < pixels.Length; i++)
                {
                    var masked = true;
                    for (var c = 0; c < components && masked; c++)
                    {
                        var value = samples[(i * components) + c];
                        masked = value >= colorKey.GetNumber(2 * c) && value <= colorKey.GetNumber((2 * c) + 1);
                    }

                    if (masked)
                    {
                        pixels[i].A = 0;
                    }
                }
            }
        }

        var interpolate = dictionary.GetBoolean(PdfName.Create("Interpolate"));
        return new DecodedImage(Track(cache, Image.ImportPixelData<Rgba32>(pixels, width, height, configuration: cache.Options.Configuration)), isStencil: false, interpolate);
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
