using System.Buffers.Binary;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// An image that <see cref="PdfContentBuilder.DrawImage"/> draws: an image XObject (ISO 32000-2, 8.9.5). A JPEG file is
/// kept as it is (<c>DCTDecode</c>); another image is stored as samples compressed with <c>FlateDecode</c>, its alpha
/// channel as a soft mask (11.6.5.3). An ICC profile of the image becomes an <c>ICCBased</c> color space.
/// </summary>
/// <remarks>An image does not belong to a document: each document that uses it gets its own objects. Instances are immutable and thread-safe.</remarks>
public sealed class PdfImage
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

    /// <summary>Creates an image from the first frame of an image of <c>Meziantou.Framework.Imaging</c>.</summary>
    /// <param name="image">The image. Its pixels are copied.</param>
    /// <returns>The image: 8 or 16 bits per component, gray or RGB, with a soft mask when the image has transparent pixels.</returns>
    /// <exception cref="ArgumentException">The image has floating-point pixels, which PDF samples cannot store: convert it to an integer pixel format first.</exception>
    public static PdfImage FromImage(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var frame = image.Frames[0];
        var width = frame.Width;
        var height = frame.Height;
        var pixelFormat = frame.PixelFormat;
        if (PixelFormats.IsFloatingPoint(pixelFormat))
            throw new ArgumentException($"PDF image samples are integers of 8 or 16 bits: convert the {pixelFormat} pixels of the image first (Image.CloneAs<Rgba64> with the PixelConversionOptions that bring them into range).", nameof(image));

        var pixels = new byte[checked(width * height * PixelFormats.GetBytesPerPixel(pixelFormat))];
        frame.CopyPixelBytesTo(pixels);

        var gray = PixelFormats.IsGrayscale(pixelFormat);
        var colorSpace = gray ? PdfDeviceColorSpace.Gray : PdfDeviceColorSpace.Rgb;
        var bitsPerComponent = PixelFormats.GetBitsPerComponent(pixelFormat);
        var bytesPerSample = bitsPerComponent / 8;
        var colorComponents = gray ? 1 : 3;
        var hasAlpha = PixelFormats.HasAlpha(pixelFormat);
        var components = colorComponents + (hasAlpha ? 1 : 0);

        var samples = new byte[width * height * colorComponents * bytesPerSample];
        var alpha = hasAlpha ? new byte[width * height * bytesPerSample] : null;
        var opaque = true;
        var isBgra = pixelFormat == PixelFormat.Bgra32;
        for (var i = 0; i < width * height; i++)
        {
            for (var c = 0; c < colorComponents; c++)
            {
                var source = isBgra ? 2 - c : c;
                CopySample(pixels, ((i * components) + source) * bytesPerSample, samples, ((i * colorComponents) + c) * bytesPerSample, bytesPerSample);
            }

            if (alpha is not null)
            {
                var offset = ((i * components) + colorComponents) * bytesPerSample;
                CopySample(pixels, offset, alpha, i * bytesPerSample, bytesPerSample);
                opaque &= pixels[offset] == 0xFF && (bytesPerSample == 1 || pixels[offset + 1] == 0xFF);
            }
        }

        var softMask = alpha is not null && !opaque ? new PdfImage(width, height, PdfDeviceColorSpace.Gray, bitsPerComponent, alpha, filter: null, iccProfile: null, softMask: null) : null;
        return new PdfImage(width, height, colorSpace, bitsPerComponent, samples, filter: null, GetIccProfile(image.Metadata.IccProfile, colorSpace), softMask);
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
            stream.SetData(_data, compressible: false);
        }
        else
        {
            stream.SetData(_data, compressible: true);
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
}
