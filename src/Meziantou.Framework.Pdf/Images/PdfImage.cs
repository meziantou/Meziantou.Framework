using System.Buffers.Binary;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// An image that <see cref="PdfContentBuilder.DrawImage"/> draws: an image XObject (ISO 32000-2, 8.9.5). A JPEG file is
/// kept as it is (<c>DCTDecode</c>); another image is stored as samples compressed with <c>FlateDecode</c>, or as JPEG
/// data with <see cref="FromImageAsJpeg"/>, its alpha channel as a soft mask (11.6.5.3). An ICC profile of the image
/// becomes an <c>ICCBased</c> color space. A bilevel image (<see cref="FromBilevel"/>) is compressed with
/// <c>CCITTFaxDecode</c>, <c>JBIG2Decode</c> or <c>FlateDecode</c>.
/// </summary>
/// <remarks>An image does not belong to a document: each document that uses it gets its own objects. Instances are immutable and thread-safe.</remarks>
public sealed partial class PdfImage
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly PdfName? _filter;
    private readonly PdfDeviceColorSpace _colorSpace;
    private readonly int _bitsPerComponent;
    private readonly bool _invertedCmyk;
    private readonly byte[]? _iccProfile;
    private readonly PdfImage? _softMask;

    private PdfImage(int width, int height, PdfDeviceColorSpace colorSpace, int bitsPerComponent, ReadOnlyMemory<byte> data, PdfName? filter, byte[]? iccProfile, PdfImage? softMask, bool invertedCmyk = false)
    {
        Width = width;
        Height = height;
        _colorSpace = colorSpace;
        _bitsPerComponent = bitsPerComponent;
        _data = data;
        _filter = filter;
        _iccProfile = iccProfile;
        _softMask = softMask;
        _invertedCmyk = invertedCmyk;
    }

    /// <summary>Gets the width of the image, in samples.</summary>
    public int Width { get; }

    /// <summary>Gets the height of the image, in samples.</summary>
    public int Height { get; }

    /// <summary>Gets a value indicating whether the image has a soft mask (an alpha channel).</summary>
    public bool HasSoftMask => _softMask is not null;

    /// <summary>Creates an image from a JPEG file, kept as it is (<c>DCTDecode</c>).</summary>
    /// <param name="data">The JPEG file: gray, YCbCr or CMYK, 8 bits per sample.</param>
    /// <returns>The image.</returns>
    /// <exception cref="ArgumentException">The data is not a JPEG file the <c>DCTDecode</c> filter supports.</exception>
    public static PdfImage FromJpeg(ReadOnlySpan<byte> data)
    {
        ImageInfo info;
        try
        {
            info = Image.Identify(data);
        }
        catch (ImageException ex)
        {
            throw new ArgumentException("The data is not a valid JPEG file: " + ex.Message, nameof(data), ex);
        }

        if (info.Format != ImageFormat.Jpeg)
            throw new ArgumentException("The data is not a JPEG file", nameof(data));

        if (info.BitsPerComponent != 8)
            throw new ArgumentException("The DCTDecode filter supports JPEG files of 8 bits per sample only", nameof(data));

        var colorSpace = info.ColorModel switch
        {
            ImageColorModel.Grayscale => PdfDeviceColorSpace.Gray,
            ImageColorModel.Cmyk or ImageColorModel.Ycck => PdfDeviceColorSpace.Cmyk,
            _ => PdfDeviceColorSpace.Rgb,
        };

        // Adobe applications write the inks of CMYK JPEG files inverted, and say so in an APP14 segment
        var inverted = colorSpace == PdfDeviceColorSpace.Cmyk && HasAdobeSegment(data);
        return new PdfImage(info.Width, info.Height, colorSpace, 8, data.ToArray(), PdfNames.DCTDecode, GetIccProfile(info.Metadata.IccProfile, colorSpace), softMask: null, inverted);
    }

    /// <summary>Creates an image from the first frame of an image of <c>Meziantou.Framework.Imaging</c>, compressed without loss.</summary>
    /// <param name="image">The image. Its pixels are copied.</param>
    /// <returns>
    /// The image: 8 or 16 bits per component, gray, RGB or CMYK (<c>DeviceCMYK</c> for <see cref="PixelFormat.Cmyk32"/> and
    /// <see cref="PixelFormat.Cmyk64"/>), with a soft mask when the image has transparent pixels. The packed pixel formats
    /// get the precision of their widening: 8 bits for <see cref="PixelFormat.Bgr565"/>, 16 bits for
    /// <see cref="PixelFormat.Rgba1010102"/>. The samples are compressed with <c>FlateDecode</c> and a PNG predictor when
    /// the document is saved with <see cref="PdfSaveOptions.CompressStreams"/>.
    /// </returns>
    /// <exception cref="ArgumentException">The image has floating-point pixels, which PDF samples cannot store: convert it to an integer pixel format first.</exception>
    public static PdfImage FromImage(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var samples = GetSamples(image);
        return new PdfImage(samples.Width, samples.Height, samples.ColorSpace, samples.BitsPerComponent, samples.Data, filter: null, samples.IccProfile, samples.SoftMask);
    }

    /// <summary>
    /// Creates an image from the first frame of an image of <c>Meziantou.Framework.Imaging</c>, compressed as JPEG
    /// (<c>DCTDecode</c>): much smaller than the samples <see cref="FromImage"/> stores for photos, at the cost of the
    /// losses of JPEG.
    /// </summary>
    /// <param name="image">The image. Its pixels are copied.</param>
    /// <param name="encoder">
    /// The settings of the compression (<see cref="JpegEncoder.Quality"/>, <see cref="JpegEncoder.ChromaSubsampling"/>,
    /// <see cref="JpegEncoder.Process"/>...), or <see langword="null"/> for the default settings of <see cref="JpegEncoder"/>.
    /// </param>
    /// <returns>
    /// The image: gray or RGB (YCbCr in the JPEG data), or CMYK for the CMYK pixel formats, 8 bits per component; the
    /// samples of 16 bits are rounded to 8 bits. The transparent pixels give a soft mask compressed without loss, as with
    /// <see cref="FromImage"/>, and the ICC profile of the image becomes the color space of the image.
    /// </returns>
    /// <exception cref="ArgumentException">The image has floating-point pixels: convert it to an integer pixel format first.</exception>
    public static PdfImage FromImageAsJpeg(Image image, JpegEncoder? encoder = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (PixelFormats.IsCmyk(image.PixelFormat))
        {
            // A CMYK JPEG file (Adobe, inverted samples) with the profile of the image, read back like any JPEG file
            using var inks = image.PixelFormat == PixelFormat.Cmyk64 ? image.CloneAs<Cmyk32>() : null;
            using var cmyk = new MemoryStream();
            (inks ?? image).Save(cmyk, encoder ?? new JpegEncoder());
            return FromJpeg(cmyk.ToArray());
        }

        var samples = GetSamples(image);
        var data = samples.Data;
        if (samples.BitsPerComponent == 16)
        {
            // The samples are big-endian: rounded to the nearest value of 8 bits
            var reduced = new byte[data.Length / 2];
            for (var i = 0; i < reduced.Length; i++)
            {
                var value = (data[2 * i] << 8) | data[(2 * i) + 1];
                reduced[i] = (byte)(((value * 255) + 32767) / 65535);
            }

            data = reduced;
        }

        // An image without metadata: the ICC profile is the color space of the image object
        using var pixels = samples.ColorSpace == PdfDeviceColorSpace.Gray
            ? (Image)Image.ImportPixelBytes<Gray8>(data, samples.Width, samples.Height)
            : Image.ImportPixelBytes<Rgb24>(data, samples.Width, samples.Height);
        using var jpeg = new MemoryStream();
        pixels.Save(jpeg, encoder ?? new JpegEncoder());
        return new PdfImage(samples.Width, samples.Height, samples.ColorSpace, 8, jpeg.ToArray(), PdfNames.DCTDecode, samples.IccProfile, samples.SoftMask);
    }

    /// <summary>Creates an image from an image file: a JPEG file is kept as it is, another format is decoded by <c>Meziantou.Framework.Imaging</c>.</summary>
    /// <param name="data">The content of the file.</param>
    /// <returns>The image.</returns>
    /// <exception cref="ArgumentException">The data is not an image the library can read.</exception>
    public static PdfImage Load(ReadOnlySpan<byte> data)
    {
        if (data.Length >= Image.FormatDetectionPrefixLength && Image.DetectFormat(data[..Image.FormatDetectionPrefixLength]) == ImageFormat.Jpeg)
        {
            try
            {
                return FromJpeg(data);
            }
            catch (ArgumentException)
            {
                // A JPEG file DCTDecode does not support (12 bits per sample): decode it
            }
        }

        try
        {
            using var image = Image.Load(data);
            return FromImage(image);
        }
        catch (ImageException ex)
        {
            throw new ArgumentException("The data is not an image the library can read: " + ex.Message, nameof(data), ex);
        }
    }

    /// <summary>Creates an image from an image file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The image.</returns>
    /// <exception cref="ArgumentException">The file is not an image the library can read.</exception>
    public static PdfImage Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Load(File.ReadAllBytes(path));
    }

    /// <summary>Gets the samples of the first frame of an image, as PDF stores them: gray or RGB, 8 or 16 bits big-endian, and the alpha channel as a soft mask.</summary>
    private static ImageSamples GetSamples(Image image)
    {
        // The formats whose samples are not whole bytes in RGB order are written from their lossless widening
        Image? widened = image.PixelFormat switch
        {
            PixelFormat.Argb32 or PixelFormat.Abgr32 => image.CloneAs<Rgba32>(),
            PixelFormat.Bgr565 => image.CloneAs<Rgb24>(),
            PixelFormat.Rgba1010102 => image.CloneAs<Rgba64>(),
            _ => null,
        };

        if (widened is not null)
        {
            using (widened)
            {
                return GetSamples(widened);
            }
        }

        var frame = image.Frames[0];
        var width = frame.Width;
        var height = frame.Height;
        var pixelFormat = frame.PixelFormat;
        if (PixelFormats.IsFloatingPoint(pixelFormat))
            throw new ArgumentException($"PDF image samples are integers of 8 or 16 bits: convert the {pixelFormat} pixels of the image first (Image.CloneAs<Rgba64> with the PixelConversionOptions that bring them into range).", nameof(image));

        var pixels = new byte[checked(width * height * PixelFormats.GetBytesPerPixel(pixelFormat))];
        frame.CopyPixelBytesTo(pixels);

        // An alpha mask (A8) has no color: it is black gray samples with its alpha as the soft mask
        var hasColor = PixelFormats.HasColor(pixelFormat);
        var gray = PixelFormats.IsGrayscale(pixelFormat) || !hasColor;

        // CMYK pixels are ink amounts, which are the samples of DeviceCMYK (0 is no ink)
        var cmyk = PixelFormats.IsCmyk(pixelFormat);
        var colorSpace = gray ? PdfDeviceColorSpace.Gray : cmyk ? PdfDeviceColorSpace.Cmyk : PdfDeviceColorSpace.Rgb;
        var bitsPerComponent = PixelFormats.GetBitsPerComponent(pixelFormat);
        var bytesPerSample = bitsPerComponent / 8;
        var colorComponents = gray ? 1 : cmyk ? 4 : 3;
        var storedColorComponents = hasColor ? colorComponents : 0;
        var hasAlpha = PixelFormats.HasAlpha(pixelFormat);
        var components = storedColorComponents + (hasAlpha ? 1 : 0);

        var samples = new byte[width * height * colorComponents * bytesPerSample];
        var alpha = hasAlpha ? new byte[width * height * bytesPerSample] : null;
        var opaque = true;
        var isBgra = pixelFormat == PixelFormat.Bgra32;
        for (var i = 0; i < width * height; i++)
        {
            for (var c = 0; c < storedColorComponents; c++)
            {
                var source = isBgra ? 2 - c : c;
                CopySample(pixels, ((i * components) + source) * bytesPerSample, samples, ((i * colorComponents) + c) * bytesPerSample, bytesPerSample);
            }

            if (alpha is not null)
            {
                var offset = ((i * components) + storedColorComponents) * bytesPerSample;
                CopySample(pixels, offset, alpha, i * bytesPerSample, bytesPerSample);
                opaque &= pixels[offset] == 0xFF && (bytesPerSample == 1 || pixels[offset + 1] == 0xFF);
            }
        }

        var softMask = alpha is not null && !opaque ? new PdfImage(width, height, PdfDeviceColorSpace.Gray, bitsPerComponent, alpha, filter: null, iccProfile: null, softMask: null) : null;
        return new ImageSamples(width, height, colorSpace, bitsPerComponent, samples, GetIccProfile(image.Metadata.IccProfile, colorSpace), softMask);
    }

    internal PdfReference GetReference(PdfDocument document)
    {
        if (document.SharedResources.TryGetValue(this, out var reference))
            return reference;

        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.XObject,
            [PdfNames.Subtype] = PdfNames.Image,
            [PdfNames.Width] = PdfInteger.Get(Width),
            [PdfNames.Height] = PdfInteger.Get(Height),
            [PdfNames.ColorSpace] = GetColorSpace(document),
            [PdfNames.BitsPerComponent] = PdfInteger.Get(_bitsPerComponent),
        };
        if (_invertedCmyk)
        {
            dictionary[PdfNames.Decode] = PdfArray.FromNumbers(1, 0, 1, 0, 1, 0, 1, 0);
        }

        if (_softMask is not null)
        {
            dictionary[PdfNames.SMask] = _softMask.GetReference(document);
        }

        var stream = new PdfStream(dictionary);
        if (_filter is not null)
        {
            dictionary[PdfNames.Filter] = _filter;
            AddDecodeParameters(dictionary);
            stream.SetData(_data, compressible: false);
        }
        else
        {
            // The samples are compressed with the PNG predictors (7.4.4.4), which make them smaller for most images
            stream.SetData(_data, compressible: true);
            stream.PngPredictor = new PdfPngPredictor(_colorSpace switch { PdfDeviceColorSpace.Gray => 1, PdfDeviceColorSpace.Rgb => 3, _ => 4 }, _bitsPerComponent, Width);
        }

        reference = document.Objects.Add(stream);
        document.SharedResources[this] = reference;
        return reference;
    }

    private PdfObject GetColorSpace(PdfDocument document)
    {
        var deviceName = _colorSpace switch
        {
            PdfDeviceColorSpace.Gray => PdfNames.DeviceGray,
            PdfDeviceColorSpace.Rgb => PdfNames.DeviceRGB,
            _ => PdfNames.DeviceCMYK,
        };
        if (_iccProfile is null)
            return deviceName;

        var profile = new PdfStream(new PdfDictionary
        {
            [PdfNames.N] = PdfInteger.Get(_colorSpace switch { PdfDeviceColorSpace.Gray => 1, PdfDeviceColorSpace.Rgb => 3, _ => 4 }),
            [PdfNames.Alternate] = deviceName,
        });
        profile.SetData(_iccProfile, compressible: true);
        return new PdfArray(PdfNames.ICCBased, document.Objects.Add(profile));
    }

    private static byte[]? GetIccProfile(Imaging.Metadata.IccProfile? profile, PdfDeviceColorSpace colorSpace)
    {
        if (profile is null)
            return null;

        var matches = (profile.ColorSpace, colorSpace) switch
        {
            (Imaging.Metadata.IccProfileColorSpace.Gray, PdfDeviceColorSpace.Gray) => true,
            (Imaging.Metadata.IccProfileColorSpace.Rgb, PdfDeviceColorSpace.Rgb) => true,
            (Imaging.Metadata.IccProfileColorSpace.Cmyk, PdfDeviceColorSpace.Cmyk) => true,
            _ => false,
        };
        return matches ? profile.Data.ToArray() : null;
    }

    private static void CopySample(byte[] source, int sourceOffset, byte[] destination, int destinationOffset, int bytesPerSample)
    {
        if (bytesPerSample == 1)
        {
            destination[destinationOffset] = source[sourceOffset];
            return;
        }

        // The samples of Imaging are in the byte order of the machine, PDF samples are big-endian (8.9.5.2)
        var value = BitConverter.IsLittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(sourceOffset)) : BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(sourceOffset));
        BinaryPrimitives.WriteUInt16BigEndian(destination.AsSpan(destinationOffset), value);
    }

    private static bool HasAdobeSegment(ReadOnlySpan<byte> data)
    {
        // The marker segments before the first frame header: APP14 (FFEE) with the identifier "Adobe"
        var position = 2;
        while (position + 4 <= data.Length && data[position] == 0xFF)
        {
            var marker = data[position + 1];
            var length = (data[position + 2] << 8) | data[position + 3];
            if (marker == 0xEE && length >= 7 && data.Slice(position + 4).StartsWith("Adobe"u8))
                return true;

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return false;

            position += 2 + length;
        }

        return false;
    }

    private sealed record ImageSamples(int Width, int Height, PdfDeviceColorSpace ColorSpace, int BitsPerComponent, byte[] Data, byte[]? IccProfile, PdfImage? SoftMask);
}
