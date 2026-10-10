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

    private sealed class WriteOnlyStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
