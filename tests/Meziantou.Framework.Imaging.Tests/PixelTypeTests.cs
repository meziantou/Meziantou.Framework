using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Tests;

public sealed class PixelTypeTests
{
    [Fact]
    public void StructSizesMatchPixelFormats()
    {
        Assert.Equal(PixelFormats.GetBytesPerPixel(PixelFormat.Rgba32), Unsafe.SizeOf<Rgba32>());
        Assert.Equal(PixelFormats.GetBytesPerPixel(PixelFormat.Bgra32), Unsafe.SizeOf<Bgra32>());
        Assert.Equal(PixelFormats.GetBytesPerPixel(PixelFormat.Rgb24), Unsafe.SizeOf<Rgb24>());
        Assert.Equal(PixelFormats.GetBytesPerPixel(PixelFormat.Rgba64), Unsafe.SizeOf<Rgba64>());
        Assert.Equal(PixelFormats.GetBytesPerPixel(PixelFormat.Gray8), Unsafe.SizeOf<Gray8>());
        Assert.Equal(PixelFormats.GetBytesPerPixel(PixelFormat.Gray16), Unsafe.SizeOf<Gray16>());
        Assert.Equal(PixelFormats.GetBytesPerPixel(PixelFormat.La16), Unsafe.SizeOf<La16>());
        Assert.Equal(PixelFormats.GetBytesPerPixel(PixelFormat.La32), Unsafe.SizeOf<La32>());
        Assert.Equal(PixelFormats.GetBytesPerPixel(PixelFormat.Rgb48), Unsafe.SizeOf<Rgb48>());
        Assert.Equal(PixelFormats.GetBytesPerPixel(PixelFormat.A8), Unsafe.SizeOf<A8>());
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32, 4, 8, 4, true, false, true)]
    [InlineData(PixelFormat.Bgra32, 4, 8, 4, true, false, true)]
    [InlineData(PixelFormat.Rgb24, 3, 8, 3, false, false, true)]
    [InlineData(PixelFormat.Rgba64, 8, 16, 4, true, false, true)]
    [InlineData(PixelFormat.Gray8, 1, 8, 1, false, true, true)]
    [InlineData(PixelFormat.Gray16, 2, 16, 1, false, true, true)]
    [InlineData(PixelFormat.RgbaFloat, 16, 32, 4, true, false, true)]
    [InlineData(PixelFormat.RgbaHalf, 8, 16, 4, true, false, true)]
    [InlineData(PixelFormat.La16, 2, 8, 2, true, true, true)]
    [InlineData(PixelFormat.La32, 4, 16, 2, true, true, true)]
    [InlineData(PixelFormat.Rgb48, 6, 16, 3, false, false, true)]
    [InlineData(PixelFormat.A8, 1, 8, 1, true, false, false)]
    public void PixelFormatsDescribeEveryFormat(PixelFormat format, int bytesPerPixel, int bitsPerComponent, int componentCount, bool hasAlpha, bool isGrayscale, bool hasColor)
    {
        Assert.Contains(format, PixelFormats.All);
        Assert.Equal(bytesPerPixel, PixelFormats.GetBytesPerPixel(format));
        Assert.Equal(bitsPerComponent, PixelFormats.GetBitsPerComponent(format));
        Assert.Equal(componentCount, PixelFormats.GetComponentCount(format));
        Assert.Equal(hasAlpha, PixelFormats.HasAlpha(format));
        Assert.Equal(isGrayscale, PixelFormats.IsGrayscale(format));
        Assert.Equal(hasColor, PixelFormats.HasColor(format));
        Assert.Equal(format is PixelFormat.RgbaFloat or PixelFormat.RgbaHalf, PixelFormats.IsFloatingPoint(format));
    }

    [Fact]
    public void PixelFormatsListsEveryFormatOnce()
    {
        Assert.Equal(Enum.GetValues<PixelFormat>().Where(format => format != PixelFormat.Unknown).Order(), PixelFormats.All.Order());
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelFormats.HasColor(PixelFormat.Unknown));
    }

    [Fact]
    public void CompactPixelStructsCompareTheirComponents()
    {
        Assert.Equal(new La16(10, 20), new La16(10, 20));
        Assert.NotEqual(new La16(10, 20), new La16(10, 21));
        Assert.True(new La32(1000, 2000) == new La32(1000, 2000));
        Assert.True(new Rgb48(1, 2, 3) != new Rgb48(1, 2, 4));
        Assert.Equal(new A8(7).GetHashCode(), new A8(7).GetHashCode());
        Assert.Equal("La16(10, 20)", new La16(10, 20).ToString());
        Assert.Equal("La32(1000, 65535)", new La32(1000).ToString());
        Assert.Equal("Rgb48(1, 2, 3)", new Rgb48(1, 2, 3).ToString());
        Assert.Equal("A8(7)", new A8(7).ToString());
    }

    [Fact]
    public void Bgra32ConstructorTakesLogicalOrderAndStoresBgra()
    {
        var pixel = new Bgra32(r: 1, g: 2, b: 3, a: 4);
        Assert.Equal(new byte[] { 3, 2, 1, 4 }, unsafe(MemoryMarshal.AsBytes(new ReadOnlySpan<Bgra32>(in pixel))).ToArray());
    }

    [Fact]
    public void Rgba32StoresRgba()
    {
        var pixel = new Rgba32(1, 2, 3, 4);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, unsafe(MemoryMarshal.AsBytes(new ReadOnlySpan<Rgba32>(in pixel))).ToArray());
    }

    [Fact]
    public void Rgba32ToRgba64IsFullRangeExpansion()
    {
        Assert.Equal(new Rgba64(0, 257, 65535, 65535), new Rgba32(0, 1, 255).ToRgba64());
    }

    [Fact]
    public void PixelFormatMappingRoundTrips()
    {
        foreach (var format in PixelFormats.All)
        {
            var type = PixelFormats.GetPixelType(format);
            Assert.Equal(format.ToString(), type.Name);
        }

        Assert.Equal(PixelFormat.Gray16, PixelFormats.GetPixelFormat<Gray16>());
        Assert.True(PixelFormats.IsSupported<Rgba64>());
        Assert.False(PixelFormats.IsSupported<int>());
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelFormats.GetBytesPerPixel(PixelFormat.Unknown));
    }

    [Fact]
    public void UnsupportedPixelTypesAreRejectedBeforeAnyIo()
    {
        using var stream = new ThrowingStream();
        var missingFile = FullPath.GetTempPath() / (Guid.NewGuid().ToString("N") + ".png");

        Assert.Throws<NotSupportedException>(() => PixelFormats.GetPixelFormat<int>());
        Assert.Throws<NotSupportedException>(() => new Image<int>(1, 1));
        Assert.Throws<NotSupportedException>(() => Image.Load<int>(stream));
        Assert.Throws<NotSupportedException>(() => Image.Load<int>(missingFile));
        Assert.Throws<NotSupportedException>(() => Image.Load<int>([0x89, 0x50]));
        Assert.Throws<NotSupportedException>(() => { _ = Image.LoadAsync<long>(missingFile, cancellationToken: XunitCancellationToken); });
        Assert.Throws<NotSupportedException>(() => Image.OpenReader<Guid>(stream));
        Assert.Throws<NotSupportedException>(() => { _ = Image.OpenReaderAsync<Guid>(missingFile, cancellationToken: XunitCancellationToken); });
        Assert.Throws<NotSupportedException>(() => Image.CreateWriter<double>(stream, new ImageWriterOptions(1, 1) { Encoder = new PngEncoder(), ExpectedFrameCount = 1 }));
        Assert.Throws<NotSupportedException>(() => Image.ImportPixelData<int>([1], 1, 1));
        Assert.Throws<NotSupportedException>(() => Image.ImportPixelBytes<int>([1, 2, 3, 4], 1, 1));
        Assert.False(File.Exists(missingFile));
    }

    [Fact]
    public void InvalidArgumentsAreRejectedBeforeAnyIo()
    {
        Assert.Throws<ArgumentNullException>(() => Image.Load((Stream)null!));
        Assert.Throws<ArgumentException>(() => Image.Load(string.Empty));
        Assert.Throws<ArgumentException>(() => Image.Load(new WriteOnlyStream()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Image<Rgba32>(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Image<Rgba32>(1, -1));
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(new ThrowingStream(), new ImageWriterOptions(1, 1)));
    }

    [Theory]
    [InlineData("#FF8000", 255, 128, 0, 255)]
    [InlineData("ff8000", 255, 128, 0, 255)]
    [InlineData("#Ff800040", 255, 128, 0, 64)]
    [InlineData("#F80", 255, 136, 0, 255)]
    [InlineData("#f804", 255, 136, 0, 68)]
    [InlineData("000", 0, 0, 0, 255)]
    [InlineData("#00000000", 0, 0, 0, 0)]
    public void HexadecimalColorsAreParsedAsInCss(string text, byte r, byte g, byte b, byte a)
    {
        Assert.Equal(new Rgba32(r, g, b, a), Rgba32.ParseHex(text));
        Assert.True(Rgba32.TryParseHex(text, out var color));
        Assert.Equal(new Rgba32(r, g, b, a), color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#123456789")]
    [InlineData("#GGGGGG")]
    [InlineData(" #FFFFFF")]
    [InlineData("#FFFFFF ")]
    [InlineData("##FFFFF")]
    [InlineData("#+12345")]
    [InlineData("red")]
    [InlineData("#\uFF11\uFF12\uFF13")]
    public void TextThatIsNotAHexadecimalColorIsRejected(string text)
    {
        Assert.False(Rgba32.TryParseHex(text, out var color));
        Assert.Equal(default, color);
        Assert.Throws<FormatException>(() => Rgba32.ParseHex(text));
    }

    [Fact]
    public void HexadecimalFormattingOmitsAnOpaqueAlphaAndRoundTrips()
    {
        Assert.Equal("#FF8000", new Rgba32(255, 128, 0).ToHexString());
        Assert.Equal("#0A0B0C0D", new Rgba32(10, 11, 12, 13).ToHexString());
        Assert.Equal("#00000000", default(Rgba32).ToHexString());
        foreach (var color in new[] { new Rgba32(1, 2, 3), new Rgba32(254, 0, 17, 0), new Rgba32(9, 99, 199, 254) })
        {
            Assert.Equal(color, Rgba32.ParseHex(color.ToHexString()));
        }
    }

    [Fact]
    public void NamedColorsAreTheNamedColorsOfCss()
    {
        // Literals from the CSS specification
        Assert.Equal(new Rgba32(0, 0, 0, 0), Colors.Transparent);
        Assert.Equal(new Rgba32(0, 0, 0), Colors.Black);
        Assert.Equal(new Rgba32(255, 255, 255), Colors.White);
        Assert.Equal(new Rgba32(255, 0, 0), Colors.Red);
        Assert.Equal(new Rgba32(0, 128, 0), Colors.Green);
        Assert.Equal(new Rgba32(0, 255, 0), Colors.Lime);
        Assert.Equal(new Rgba32(100, 149, 237), Colors.CornflowerBlue);
        Assert.Equal(new Rgba32(102, 51, 153), Colors.RebeccaPurple);

        // Every other one against an independent table: the web colors of System.Drawing, which are the named colors of
        // CSS but for two entries (its transparent color is white, and its dark sea green ends with 8B instead of 8F)
        var properties = typeof(Colors).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.HasCount(142, properties);
        foreach (var property in properties)
        {
            var color = (Rgba32)property.GetValue(null)!;
            var reference = System.Drawing.Color.FromName(property.Name);
            Assert.True(reference.IsKnownColor, property.Name);
            var expected = property.Name switch
            {
                nameof(Colors.Transparent) => new Rgba32(0, 0, 0, 0),
                nameof(Colors.DarkSeaGreen) => new Rgba32(0x8F, 0xBC, 0x8F),
                _ => new Rgba32(reference.R, reference.G, reference.B, reference.A),
            };

            Assert.True(expected == color, $"{property.Name}: expected {expected}, actual {color}");
        }
    }

    private sealed class WriteOnlyStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
