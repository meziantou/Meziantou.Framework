using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Tests;

public class QRCodePngRendererTests
{
    [Fact]
    public void ToPng_DefaultOptions()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.L);
        var png = qr.ToPng();

        Snapshot.Validate(SnapshotType.Png, png); // TODO allow diff percentage + Extension ValidatePng (ImageSharp)
    }

    [Fact]
    public void ToPng_HasPngSignature()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var png = qr.ToPng();

        Assert.Equal(137, png[0]);
        Assert.Equal((byte)'P', png[1]);
        Assert.Equal((byte)'N', png[2]);
        Assert.Equal((byte)'G', png[3]);
        Assert.Equal(13, png[4]);
        Assert.Equal(10, png[5]);
        Assert.Equal(26, png[6]);
        Assert.Equal(10, png[7]);
    }

    [Fact]
    public void ToPng_DefaultOptions_UsesModuleSize10_QuietZone4()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var png = qr.ToPng();

        var (width, height, _) = ParsePng(png);
        Assert.Equal(290, width);
        Assert.Equal(290, height);
    }

    [Fact]
    public void ToPng_MicroQr_HasExpectedDimensions()
    {
        var qr = QRCode.CreateMicroQR("123", ErrorCorrectionLevel.L);
        var png = qr.ToPng(new QRCodePngOptions { ModuleSize = 2, QuietZoneModules = 1 });

        var (width, height, _) = ParsePng(png);
        Assert.Equal((11 + 2) * 2, width);
        Assert.Equal((11 + 2) * 2, height);
    }

    [Fact]
    public void ToPng_Rmqr_HasExpectedDimensions()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.M);
        var png = qr.ToPng(new QRCodePngOptions { ModuleSize = 3, QuietZoneModules = 0 });

        var (width, height, _) = ParsePng(png);
        Assert.Equal(27 * 3, width);
        Assert.Equal(11 * 3, height);
    }

    [Fact]
    public void WriteToPng_MatchesToPng()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var options = new QRCodePngOptions { ModuleSize = 2, QuietZoneModules = 1 };
        var expected = qr.ToPng(options);

        using var stream = new MemoryStream();
        qr.WriteToPng(stream, options);
        var actual = stream.ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ToPng_InvertedColors_InvertsPixelValues()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var options = new QRCodePngOptions { ModuleSize = 1, QuietZoneModules = 0 };
        var normal = ParsePng(qr.ToPng(options));
        var inverted = ParsePng(qr.ToPng(new QRCodePngOptions { ModuleSize = 1, QuietZoneModules = 0, InvertColors = true }));

        var width = normal.Width;
        var pixelIndex = 1;
        var expectedNormalPixel = qr[0, 0] ? (byte)0 : (byte)255;
        var expectedInvertedPixel = qr[0, 0] ? (byte)255 : (byte)0;

        Assert.Equal(width, inverted.Width);
        Assert.Equal(normal.Height, inverted.Height);
        Assert.Equal(expectedNormalPixel, normal.ImageData[pixelIndex]);
        Assert.Equal(expectedInvertedPixel, inverted.ImageData[pixelIndex]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToPng_InvalidModuleSize_ThrowsArgumentOutOfRangeException(int moduleSize)
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToPng(new QRCodePngOptions { ModuleSize = moduleSize }));
    }

    [Fact]
    public void ToPng_NegativeQuietZone_ThrowsArgumentOutOfRangeException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToPng(new QRCodePngOptions { QuietZoneModules = -1 }));
    }

    [Fact]
    public void WriteToPng_NullStream_ThrowsArgumentNullException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentNullException>(() => qr.WriteToPng(stream: null!));
    }

    [Fact]
    public void ToPng_NullOptions_ThrowsArgumentNullException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentNullException>(() => qr.ToPng(options: null!));
    }

    private static (int Width, int Height, byte[] ImageData) ParsePng(byte[] data)
    {
        var offset = 8;
        var width = 0;
        var height = 0;
        using var idatData = new MemoryStream();

        while (offset < data.Length)
        {
            var chunkLength = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
            var chunkType = data.AsSpan(offset + 4, 4);
            var chunkData = data.AsSpan(offset + 8, chunkLength);

            if (chunkType.SequenceEqual("IHDR"u8))
            {
                width = BinaryPrimitives.ReadInt32BigEndian(chunkData[..4]);
                height = BinaryPrimitives.ReadInt32BigEndian(chunkData[4..8]);
            }
            else if (chunkType.SequenceEqual("IDAT"u8))
            {
                idatData.Write(chunkData);
            }
            else if (chunkType.SequenceEqual("IEND"u8))
            {
                break;
            }

            offset += 12 + chunkLength;
        }

        using var compressed = new MemoryStream(idatData.ToArray());
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var imageData = new MemoryStream();
        zlib.CopyTo(imageData);

        return (width, height, imageData.ToArray());
    }
}
