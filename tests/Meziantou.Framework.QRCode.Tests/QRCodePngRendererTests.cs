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

    public static TheoryData<string> ScenarioNames()
    {
        var data = new TheoryData<string>();
        foreach (var scenario in QRCodePngScenarios.All)
        {
            data.Add(scenario.Name);
        }

        return data;
    }

    public static TheoryData<QRCodeType, int, int> Layouts()
    {
        var data = new TheoryData<QRCodeType, int, int>();
        foreach (var type in new[] { QRCodeType.Standard, QRCodeType.MicroQR, QRCodeType.RMQR })
        {
            // A module size of 1 with no quiet zone leaves a row width that is not a multiple of
            // 8, which is where the bit packing is most likely to go wrong.
            foreach (var moduleSize in new[] { 1, 2, 3, 7 })
            {
                foreach (var quietZoneModules in new[] { 0, 1, 4 })
                {
                    data.Add(type, moduleSize, quietZoneModules);
                }
            }
        }

        return data;
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

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ToPng_PixelsMatchTheModuleMatrix(QRCodeType type, int moduleSize, int quietZoneModules)
    {
        var qr = CreateSample(type);
        var png = qr.ToPng(new QRCodePngOptions { ModuleSize = moduleSize, QuietZoneModules = quietZoneModules });

        var parsed = ParsePng(png);
        Assert.Equal((qr.Width + (2 * quietZoneModules)) * moduleSize, parsed.Width);
        Assert.Equal((qr.Height + (2 * quietZoneModules)) * moduleSize, parsed.Height);

        for (var y = 0; y < parsed.Height; y++)
        {
            for (var x = 0; x < parsed.Width; x++)
            {
                var row = (y / moduleSize) - quietZoneModules;
                var column = (x / moduleSize) - quietZoneModules;
                var expected = row >= 0 && row < qr.Height && column >= 0 && column < qr.Width && qr[row, column];
                if (expected != parsed.IsSet(y, x))
                {
                    Assert.Fail($"Pixel ({x}, {y}) is {(expected ? "light" : "dark")} but module ({column}, {row}) is {(expected ? "dark" : "light")}.");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ToPng_UnusedBitsAtTheEndOfARowAreClear(QRCodeType type, int moduleSize, int quietZoneModules)
    {
        // A row is padded to a whole number of bytes. Leaving the padding bits set would still
        // decode, but it makes the image data depend on uninitialised state.
        var qr = CreateSample(type);
        var png = qr.ToPng(new QRCodePngOptions { ModuleSize = moduleSize, QuietZoneModules = quietZoneModules });

        var parsed = ParsePng(png);
        for (var y = 0; y < parsed.Height; y++)
        {
            for (var bit = parsed.Width; bit < parsed.BytesPerRow * 8; bit++)
            {
                if (parsed.IsSet(y, bit))
                {
                    Assert.Fail($"Padding bit {bit} of row {y} is set.");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void ToPng_Scenario(string name)
    {
        var scenario = QRCodePngScenarios.Get(name);

        Snapshot.Validate(scenario.CreatePng(), SnapshotType.Png);
    }

    [Fact]
    public void Scenarios_HaveUniqueNames()
    {
        // Two scenarios sharing a name would share a snapshot file, so one of them would never
        // be checked against anything.
        Assert.Distinct(QRCodePngScenarios.All.Select(scenario => scenario.Name), StringComparer.Ordinal);
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
    public void WriteToPng_WritesAtTheCurrentPositionAndLeavesTheStreamOpen()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var expected = qr.ToPng();

        using var stream = new MemoryStream();
        stream.WriteByte(0xAA);
        qr.WriteToPng(stream);
        stream.WriteByte(0xBB);

        var actual = stream.ToArray();
        Assert.Equal(0xAA, actual[0]);
        Assert.Equal(0xBB, actual[^1]);
        Assert.Equal<byte>(expected, actual.AsSpan(1, actual.Length - 2));
    }

    [Fact]
    public void ToPng_IsDeterministic()
    {
        var qr = QRCode.Create("https://www.meziantou.net/", ErrorCorrectionLevel.Q);

        Assert.Equal(qr.ToPng(), qr.ToPng());
    }

    [Fact]
    public void ToPng_ChunksAreWellFormed()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var chunks = ReadChunks(qr.ToPng());
        string[] expectedTypes = ["IHDR", "PLTE", "IDAT", "IEND"];

        Assert.Equal(expectedTypes, chunks.Select(chunk => chunk.Type));
        Assert.All(chunks, chunk => Assert.Equal(ComputeCrc32(chunk.Type, chunk.Data), chunk.Crc));
        Assert.Empty(chunks[^1].Data);
    }

    [Fact]
    public void ToPng_TransparentColors_WritesTransparencyBeforeTheImageData()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var png = qr.ToPng(new QRCodePngOptions { LightColor = Color.Transparent });
        var chunks = ReadChunks(png);
        string[] expectedTypes = ["IHDR", "PLTE", "tRNS", "IDAT", "IEND"];

        // tRNS must come after PLTE and before IDAT.
        Assert.Equal(expectedTypes, chunks.Select(chunk => chunk.Type));
        Assert.All(chunks, chunk => Assert.Equal(ComputeCrc32(chunk.Type, chunk.Data), chunk.Crc));
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
    public void ToPng_SameDarkAndLightColor_StillWritesTwoPaletteEntries()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var parsed = ParsePng(qr.ToPng(new QRCodePngOptions
        {
            ModuleSize = 1,
            QuietZoneModules = 0,
            DarkColor = Color.FromRgb(0x12, 0x34, 0x56),
            LightColor = Color.FromRgb(0x12, 0x34, 0x56),
        }));

        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x12, 0x34, 0x56 }, parsed.Palette);
        Assert.Equal(qr[0, 0], parsed.IsSet(0, 0));
    }

    [Fact]
    public void ToPng_FullyTransparentColors_WritesBothAlphaValues()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var parsed = ParsePng(qr.ToPng(new QRCodePngOptions
        {
            DarkColor = Color.Transparent,
            LightColor = Color.Transparent,
        }));

        Assert.Equal(new byte[] { 0x00, 0x00 }, parsed.Transparency);
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
    [InlineData(int.MinValue)]
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
    public void ToPng_ModuleSizeOverflowsTheImageDimensions_ThrowsArgumentOutOfRangeException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        // The size is rejected before anything is allocated, so this must not try to build a
        // 21 * int.MaxValue pixel image.
        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToPng(new QRCodePngOptions { ModuleSize = int.MaxValue }));
    }

    [Fact]
    public void ToPng_QuietZoneOverflowsTheImageDimensions_ThrowsArgumentOutOfRangeException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToPng(new QRCodePngOptions { ModuleSize = 1, QuietZoneModules = int.MaxValue }));
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

    private static QRCode CreateSample(QRCodeType type)
    {
        return type switch
        {
            QRCodeType.MicroQR => QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L),
            QRCodeType.RMQR => QRCode.CreateRMQR("https://example.com", ErrorCorrectionLevel.M),
            _ => QRCode.Create("https://www.meziantou.net/", ErrorCorrectionLevel.M),
        };
    }

    private sealed record ParsedPng(int Width, int Height, byte BitDepth, byte ColorType, byte[] Palette, byte[] Transparency, byte[] ImageData)
    {
        public int BytesPerRow => ((Width * BitDepth) + 7) / 8;

        /// <summary>Gets whether the pixel at the given position uses palette entry 1.</summary>
        public bool IsSet(int row, int column)
        {
            var rowOffset = row * BytesPerRow;

            return (ImageData[rowOffset + (column >> 3)] & (0x80 >> (column & 7))) != 0;
        }
    }

    private sealed record PngChunk(string Type, byte[] Data, uint Crc);

    private static ParsedPng ParsePng(byte[] data)
    {
        var width = 0;
        var height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        var palette = Array.Empty<byte>();
        var transparency = Array.Empty<byte>();
        using var idatData = new MemoryStream();

        foreach (var chunk in ReadChunks(data))
        {
            switch (chunk.Type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(chunk.Data.AsSpan(0, 4));
                    height = BinaryPrimitives.ReadInt32BigEndian(chunk.Data.AsSpan(4, 4));
                    bitDepth = chunk.Data[8];
                    colorType = chunk.Data[9];
                    break;

                case "PLTE":
                    palette = chunk.Data;
                    break;

                case "tRNS":
                    transparency = chunk.Data;
                    break;

                case "IDAT":
                    idatData.Write(chunk.Data);
                    break;
            }
        }

        using var compressed = new MemoryStream(idatData.ToArray());
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var imageData = new MemoryStream();
        zlib.CopyTo(imageData);

        var bytesPerRow = ((width * bitDepth) + 7) / 8;

        return new ParsedPng(width, height, bitDepth, colorType, palette, transparency, Unfilter(imageData.ToArray(), bytesPerRow, height));
    }

    /// <summary>Reverses the per-scanline filters and returns the raw rows, without the filter bytes.</summary>
    private static byte[] Unfilter(byte[] filteredData, int bytesPerRow, int height)
    {
        // One byte per pixel or fewer, so a filter looks one byte back.
        const int BytesPerPixel = 1;

        var result = new byte[bytesPerRow * height];
        for (var row = 0; row < height; row++)
        {
            var filterType = filteredData[row * (bytesPerRow + 1)];
            var source = filteredData.AsSpan((row * (bytesPerRow + 1)) + 1, bytesPerRow);
            var target = result.AsSpan(row * bytesPerRow, bytesPerRow);
            var previous = row is 0 ? default : result.AsSpan((row - 1) * bytesPerRow, bytesPerRow);

            for (var i = 0; i < bytesPerRow; i++)
            {
                var left = i >= BytesPerPixel ? target[i - BytesPerPixel] : (byte)0;
                var up = previous.IsEmpty ? (byte)0 : previous[i];
                var upperLeft = previous.IsEmpty || i < BytesPerPixel ? (byte)0 : previous[i - BytesPerPixel];

                target[i] = filterType switch
                {
                    0 => source[i],
                    1 => (byte)(source[i] + left),
                    2 => (byte)(source[i] + up),
                    3 => (byte)(source[i] + ((left + up) / 2)),
                    4 => (byte)(source[i] + Paeth(left, up, upperLeft)),
                    _ => throw new InvalidOperationException($"Unknown PNG filter type {filterType}."),
                };
            }
        }

        return result;
    }

    private static byte Paeth(byte left, byte up, byte upperLeft)
    {
        var p = left + up - upperLeft;
        var pa = Math.Abs(p - left);
        var pb = Math.Abs(p - up);
        var pc = Math.Abs(p - upperLeft);

        if (pa <= pb && pa <= pc)
            return left;

        return pb <= pc ? up : upperLeft;
    }

    private static List<PngChunk> ReadChunks(byte[] data)
    {
        var chunks = new List<PngChunk>();
        var offset = 8;
        while (offset < data.Length)
        {
            var chunkLength = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
            var chunkType = Encoding.ASCII.GetString(data.AsSpan(offset + 4, 4));
            var chunkData = data.AsSpan(offset + 8, chunkLength).ToArray();
            var crc = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 8 + chunkLength, 4));
            chunks.Add(new PngChunk(chunkType, chunkData, crc));

            offset += 12 + chunkLength;
        }

        return chunks;
    }

    private static uint ComputeCrc32(string chunkType, byte[] data)
    {
        var crc = uint.MaxValue;
        foreach (var value in Encoding.ASCII.GetBytes(chunkType).Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : 0xEDB88320u ^ (crc >> 1);
            }
        }

        return ~crc;
    }
}
