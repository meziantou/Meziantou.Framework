using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Tests;

public class QRCodePngRendererTests
{
    public static IEnumerable<object[]> ModuleSizes
    {
        get
        {
            for (var moduleSize = 1; moduleSize <= 10; moduleSize++)
            {
                yield return [moduleSize];
            }
        }
    }

    [Fact]
    public void ToPng_DefaultOptions()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.L);
        var png = qr.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
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

        var parsed = ParsePng(png);
        Assert.Equal(290, parsed.Width);
        Assert.Equal(290, parsed.Height);
    }

    [Theory]
    [MemberData(nameof(ModuleSizes))]
    public void ToPng_UsesConfiguredModuleSize(int moduleSize)
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var png = qr.ToPng(new QRCodePngOptions { ModuleSize = moduleSize });

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_MicroQr_HasExpectedDimensions()
    {
        var qr = QRCode.CreateMicroQR("123", ErrorCorrectionLevel.L);
        var png = qr.ToPng(new QRCodePngOptions { ModuleSize = 2, QuietZoneModules = 1 });

        var parsed = ParsePng(png);
        Assert.Equal((11 + 2) * 2, parsed.Width);
        Assert.Equal((11 + 2) * 2, parsed.Height);
    }

    [Fact]
    public void ToPng_Rmqr_HasExpectedDimensions()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.M);
        var png = qr.ToPng(new QRCodePngOptions { ModuleSize = 3, QuietZoneModules = 0 });

        var parsed = ParsePng(png);
        Assert.Equal(27 * 3, parsed.Width);
        Assert.Equal(11 * 3, parsed.Height);
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
    public void ToPng_CustomColors_UsesConfiguredRgb()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var options = new QRCodePngOptions { ModuleSize = 1, QuietZoneModules = 0 };
        var normal = ParsePng(qr.ToPng(options));
        var custom = ParsePng(qr.ToPng(new QRCodePngOptions
        {
            ModuleSize = 1,
            QuietZoneModules = 0,
            DarkColor = Color.FromArgb(0x7f, 0x11, 0x22, 0x33),
            LightColor = Color.FromArgb(0x40, 0xaa, 0xbb, 0xcc),
        }));

        Assert.Equal(normal.Width, custom.Width);
        Assert.Equal(normal.Height, custom.Height);

        // Indexed, one bit per pixel: the colours live in PLTE and tRNS, and a set bit means dark.
        Assert.Equal((byte)3, custom.ColorType);
        Assert.Equal((byte)1, custom.BitDepth);
        Assert.Equal(new byte[] { 0xaa, 0xbb, 0xcc, 0x11, 0x22, 0x33 }, custom.Palette);
        Assert.Equal(new byte[] { 0x40, 0x7f }, custom.Transparency);
        Assert.Equal(qr[0, 0], custom.IsSet(0, 0));

        // The default colours are opaque, so no tRNS chunk is written at all.
        Assert.Equal(new byte[] { 0xff, 0xff, 0xff, 0x00, 0x00, 0x00 }, normal.Palette);
        Assert.Empty(normal.Transparency);
        Assert.Equal(qr[0, 0], normal.IsSet(0, 0));
    }

    [Fact]
    public void ToPng_TransparentColors_Snapshot()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var png = qr.ToPng(new QRCodePngOptions
        {
            ModuleSize = 1,
            QuietZoneModules = 0,
            DarkColor = Color.FromArgb(0x80, 0x11, 0x22, 0x33),
            LightColor = Color.Transparent,
        });

        Snapshot.Validate(png, SnapshotType.Png);
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

    private sealed record ParsedPng(int Width, int Height, byte BitDepth, byte ColorType, byte[] Palette, byte[] Transparency, byte[] ImageData)
    {
        /// <summary>Gets whether the pixel at the given position uses palette entry 1.</summary>
        public bool IsSet(int row, int column)
        {
            var bytesPerRow = ((Width * BitDepth) + 7) / 8;
            var rowOffset = (row * (bytesPerRow + 1)) + 1;

            return (ImageData[rowOffset + (column >> 3)] & (0x80 >> (column & 7))) != 0;
        }
    }

    private static ParsedPng ParsePng(byte[] data)
    {
        var offset = 8;
        var width = 0;
        var height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        var palette = Array.Empty<byte>();
        var transparency = Array.Empty<byte>();
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
                bitDepth = chunkData[8];
                colorType = chunkData[9];
            }
            else if (chunkType.SequenceEqual("PLTE"u8))
            {
                palette = chunkData.ToArray();
            }
            else if (chunkType.SequenceEqual("tRNS"u8))
            {
                transparency = chunkData.ToArray();
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

        return new ParsedPng(width, height, bitDepth, colorType, palette, transparency, imageData.ToArray());
    }
}
