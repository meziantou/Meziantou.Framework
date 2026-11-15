using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Dpx;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Kodak Cineon encoding: the header is read with the offsets of the draft 4.5, and the image data compared with the packing
/// of <see cref="CineonFileBuilder"/>, independent of the library.
/// </summary>
public sealed class CineonEncoderTests
{
    [Theory]
    [InlineData(PixelFormat.Rgb24, 3, 10)]
    [InlineData(PixelFormat.Rgba32, 3, 10)]
    [InlineData(PixelFormat.Gray8, 1, 10)]
    [InlineData(PixelFormat.La16, 1, 10)]
    [InlineData(PixelFormat.Rgb48, 3, 16)]
    [InlineData(PixelFormat.Gray16, 1, 16)]
    public void TheDefaultDepthKeepsTheSamples(PixelFormat format, int channels, int bits)
    {
        using var source = CreatePattern(7, 3);
        using var image = source.CloneAs(format, options: null);
        var data = Save(image, new CineonEncoder());
        Assert.Equal(0x802A5FD7u, BinaryPrimitives.ReadUInt32BigEndian(data));
        Assert.Equal(2048u, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)));
        Assert.Equal((uint)data.Length, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20)));
        Assert.Equal("V4.5", Encoding.ASCII.GetString(data, 24, 4));
        Assert.Equal(channels, data[193]);
        Assert.Equal(bits, data[196 + 2]);
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(200)));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(204)));
        Assert.Equal(5, data[681]);
        Assert.Equal(0, data[680]);

        // The samples are the code values of the nearest level, packed as the format packs them
        var max = (1UL << bits) - 1;
        var sourceMax = PixelFormats.GetBitsPerComponent(format) == 16 ? 65535UL : 255UL;
        using var reference = image.CloneAs<Rgba64>();
        var codes = new List<uint>();
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 7; x++)
            {
                var pixel = reference.Frames[0][x, y];
                foreach (var value in channels == 1 ? new[] { pixel.R } : [pixel.R, pixel.G, pixel.B])
                {
                    var sample = sourceMax == 255 ? value / 257UL : value;
                    codes.Add((uint)(((2 * sample * max) + sourceMax) / (2 * sourceMax)));
                }
            }
        }

        var expected = new CineonFileBuilder { Width = 7, Height = 3, BitsPerSample = bits, Designators = channels == 1 ? [0] : [1, 2, 3], Samples = [.. codes] }.Build();
        Assert.Equal(expected.AsSpan(2048).ToArray(), data.AsSpan(2048).ToArray());

        using var decoded = Image.Load(data);
        Assert.Equal(ImageFormat.Cineon, Image.DetectFormat(data));
        using var expectedPixels = image.CloneAs<Rgba64>();
        using var actualPixels = decoded.CloneAs<Rgba64>();
        if (bits == 10)
        {
            // 8-bit samples come back from 10 bits exactly
            using var expected8 = image.CloneAs<Rgba32>();
            using var actual8 = decoded.CloneAs<Rgba32>();
            Assert.Equal(GetBytes(expected8), GetBytes(actual8));
        }
        else
        {
            Assert.Equal(GetBytes(expectedPixels), GetBytes(actualPixels));
        }
    }

    [Fact]
    public void ChannelsInAnotherOrderKeepTheirColorsWhenSavedAgain()
    {
        // The file stores blue, green, red (designators 3, 2, 1): the decoder places them by their designators
        uint[] samples = [100, 200, 300, 400, 500, 600, 700, 800, 900, 1000, 50, 150];
        var data = new CineonFileBuilder { Width = 2, Height = 2, Designators = [3, 2, 1], Samples = samples }.Build();
        using var decoded = Image.Load(data);
        using var saved = Image.Load(Save(decoded, new CineonEncoder()));
        using var expected = decoded.CloneAs<Rgba64>();
        using var actual = saved.CloneAs<Rgba64>();
        Assert.Equal(GetBytes(expected), GetBytes(actual));

        // The samples are written red, green, blue, and so are the designators
        Assert.Equal([1, 2, 3], saved.Metadata.CineonHeader!.ChannelDesignators.Select(designator => (int)designator));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(16)]
    public void TheDepthCanBeChosen(int bits)
    {
        using var image = CreatePattern(5, 2);
        var data = Save(image, new CineonEncoder { BitDepth = bits });
        Assert.Equal(bits, data[198]);
        Assert.Equal(CineonEncoderLineLength(5, 3, bits) * 2, data.Length - 2048);
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(GetBytes(image), GetBytes(decoded));
    }

    [Fact]
    public void FewerBitsThanTheSamplesRequireAnExplicitSetting()
    {
        using var source = CreatePattern(2, 2);
        using var image = source.CloneAs<Rgb48>();
        Assert.Equal("Bit depth reduction", Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new CineonEncoder { BitDepth = 10 })).Feature);
        var data = Save(image, new CineonEncoder { BitDepth = 10, AllowBitDepthReduction = true });
        Assert.Equal(10, data[198]);
    }

    [Fact]
    public void TheHeaderOfTheSourceIsCopied()
    {
        var header = CreateHeader(bigEndian: false, packing: 6, label: "Scan");

        using var image = CreatePattern(4, 2);
        image.Metadata.CineonHeader = header;
        image.Metadata.Orientation = ExifOrientation.BottomLeft;
        var data = Save(image, new CineonEncoder());
        Assert.Equal(1, data[192]);
        Assert.Equal(1, data[683]);
        using var decoded = Image.Load(data);
        Assert.Equal(ExifOrientation.BottomLeft, decoded.Metadata.Orientation);
        var written = decoded.Metadata.CineonHeader!;

        // The file is big-endian, packing 5; the rest is the header of the source
        Assert.True(written.IsBigEndian);
        Assert.Equal(5, written.Packing);
        Assert.Equal(CreateHeader(bigEndian: true, packing: 5, label: "Scan"), written);

        // The channel values describe 10-bit code values: another depth gets the defaults
        using var deep = image.CloneAs<Rgb48>();
        deep.Metadata.CineonHeader = header;
        var defaults = Image.Identify(Save(deep, new CineonEncoder())).Metadata.CineonHeader!;
        Assert.Equal([0f, 0f, 0f], defaults.MinimumDataValues);
        Assert.Equal([65535f, 65535f, 65535f], defaults.MaximumDataValues);
        Assert.Equal([2.048f, 2.048f, 2.048f], defaults.MaximumQuantities);
        Assert.Equal([(ushort)4, (ushort)5, (ushort)6], defaults.ChannelDesignators);
        Assert.Equal("Scan", defaults.Label);

        // A text that does not fit its field is rejected
        image.Metadata.CineonHeader = CreateHeader(bigEndian: true, packing: 5, label: new string('x', 201));
        Assert.Equal("Metadata: Cineon header text", Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new CineonEncoder())).Feature);
    }

    [Fact]
    public void WithoutHeaderTheChannelsArePrintingDensities()
    {
        using var image = CreatePattern(3, 1);
        var header = Image.Identify(Save(image, new CineonEncoder())).Metadata.CineonHeader!;
        Assert.Equal([(ushort)1, (ushort)2, (ushort)3], header.ChannelDesignators);
        Assert.Equal([0f, 0f, 0f], header.MinimumDataValues);
        Assert.Equal([1023f, 1023f, 1023f], header.MaximumDataValues);
        Assert.Null(header.Label);
        Assert.Null(header.FramePosition);
        Assert.Null(header.Chromaticities);
        Assert.False(header.IsNegative);

        using var gray = image.CloneAs<Gray8>();
        Assert.Equal([(ushort)0], Image.Identify(Save(gray, new CineonEncoder())).Metadata.CineonHeader!.ChannelDesignators);
    }

    [Fact]
    public void UnsupportedImagesAreRejected()
    {
        using var transparent = CreatePattern(3, 3);
        transparent.Frames[0][1, 1] = new Rgba32(1, 2, 3, 4);
        Assert.Equal("Transparency", Assert.Throws<UnsupportedImageFeatureException>(() => Save(transparent, new CineonEncoder())).Feature);

        using var cmyk = new Image<Cmyk32>(2, 2);
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(cmyk, new CineonEncoder()));

        using var animated = CreatePattern(2, 2);
        animated.InsertFrame(1, animated.Frames[0]);
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(animated, new CineonEncoder()));

        // A linear label cannot be stored: the code values are printing densities
        using var linear = CreatePattern(2, 2);
        linear.Metadata.TransferFunction = ColorTransferFunction.Linear;
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(linear, new CineonEncoder()));
    }

    [Fact]
    public void TheExtensionSelectsTheEncoder()
    {
        Assert.IsType<CineonEncoder>(ImageEncoder.FromFileExtension(".cin"));
        Assert.IsType<CineonEncoder>(ImageEncoder.Create(ImageFormat.Cineon));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new CineonEncoder { BitDepth = 12 });
        Assert.Null(new CineonEncoder().BitDepth);
        Assert.False(new CineonEncoder().AllowBitDepthReduction);
    }

    private static CineonHeader CreateHeader(bool bigEndian, byte packing, string label) => new()
    {
        Version = "V4.5",
        IsBigEndian = bigEndian,
        ChannelDesignators = [4, 5, 6],
        BitsPerSample = 10,
        MinimumDataValues = [95, 95, 95],
        MinimumQuantities = [0.1f, 0.2f, 0.3f],
        MaximumDataValues = [685, 685, 685],
        MaximumQuantities = [1.5f, 1.6f, 1.7f],
        Chromaticities = [0.3127f, 0.329f, 0.64f, 0.33f, 0.3f, 0.6f, 0.15f, 0.06f],
        Packing = packing,
        IsNegative = true,
        Label = label,
        FileName = "frame.cin",
        CreationDate = "2026:11:15",
        CreationTime = "10:20:30",
        XOffset = -4,
        YOffset = 12,
        SourceFileName = "source.dpx",
        SourceCreationDate = "2025:01:02",
        SourceCreationTime = "01:02:03",
        InputDevice = "Scanner",
        InputDeviceModel = "Model",
        InputDeviceSerialNumber = "1234",
        XDevicePitch = 2.5f,
        YDevicePitch = 3.5f,
        Gamma = 1.7f,
        FilmEdgeCode = "0102030004560789",
        FilmFormat = "Academy",
        FramePosition = 42,
        FrameRate = 24,
        FrameAttribute = "Key",
        SlateInformation = "Slate",
    };

    private static int CineonEncoderLineLength(int width, int channels, int bits) => bits switch
    {
        10 => channels == 1 ? (width + 2) / 3 * 4 : width * 4,
        _ => ((width * channels * bits / 8) + 3) / 4 * 4,
    };

    private static Image<Rgba32> CreatePattern(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.Frames[0][x, y] = new Rgba32((byte)(x * 50), (byte)(255 - (y * 70)), (byte)((x * 31) + (y * 17)), 255);
            }
        }

        return image;
    }

    private static byte[] Save(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    private static byte[] GetBytes(Image image)
    {
        var frame = image.Frames[0];
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        frame.CopyPixelBytesTo(bytes);
        return bytes;
    }
}
