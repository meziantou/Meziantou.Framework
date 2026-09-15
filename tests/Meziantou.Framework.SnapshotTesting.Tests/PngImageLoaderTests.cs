using System.Buffers.Binary;
using System.IO.Hashing;

namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class PngImageLoaderTests
{
    [Fact]
    public async Task Image_LoadAsync_Stream_DecodesPngPixels()
    {
        var imageData = ImageTestData.CreatePngRgba32(
            width: 2,
            height: 1,
            pixels:
            [
                0xFFFF0000u,
                0x800000FFu,
            ]);

        using var stream = new MemoryStream(imageData);
        var image = await Image.LoadAsync(stream);

        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(
        [
            new Argb(0xFFFF0000u),
            new Argb(0x800000FFu),
        ], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_Grayscale8_RepeatsTheSampleOnEveryChannel()
    {
        var image = Image.Load(ImageTestData.CreatePng(width: 2, height: 1, bitDepth: 8, colorType: 0, samples: [0x10, 0x20]));

        Assert.Equal([new Argb(0xFF101010u), new Argb(0xFF202020u)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_Grayscale8_WithTransparency_MarksTheTransparentSample()
    {
        var image = Image.Load(ImageTestData.CreatePng(width: 2, height: 1, bitDepth: 8, colorType: 0, samples: [0x10, 0x20], transparency: [0x00, 0x20]));

        Assert.Equal([new Argb(0xFF101010u), new Argb(0x00202020u)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_Rgb8_DecodesOpaquePixels()
    {
        var image = Image.Load(ImageTestData.CreatePng(width: 2, height: 1, bitDepth: 8, colorType: 2, samples: [0x10, 0x20, 0x30, 0x40, 0x50, 0x60]));

        Assert.Equal([new Argb(0xFF102030u), new Argb(0xFF405060u)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_Rgb8_WithTransparency_MarksTheTransparentColor()
    {
        var image = Image.Load(ImageTestData.CreatePng(
            width: 2,
            height: 1,
            bitDepth: 8,
            colorType: 2,
            samples: [0x10, 0x20, 0x30, 0x40, 0x50, 0x60],
            transparency: [0x00, 0x10, 0x00, 0x20, 0x00, 0x30]));

        Assert.Equal([new Argb(0x00102030u), new Argb(0xFF405060u)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_Indexed8_ResolvesThePalette()
    {
        var image = Image.Load(ImageTestData.CreatePng(
            width: 3,
            height: 1,
            bitDepth: 8,
            colorType: 3,
            samples: [0, 1, 2],
            palette: [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF]));

        Assert.Equal([new Argb(0xFFFF0000u), new Argb(0xFF00FF00u), new Argb(0xFF0000FFu)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_Indexed8_WithTransparency_AppliesThePaletteAlpha()
    {
        var image = Image.Load(ImageTestData.CreatePng(
            width: 3,
            height: 1,
            bitDepth: 8,
            colorType: 3,
            samples: [0, 1, 2],
            palette: [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF],
            transparency: [0x00, 0x80]));

        Assert.Equal([new Argb(0x00FF0000u), new Argb(0x8000FF00u), new Argb(0xFF0000FFu)], image.Pixels.ToArray());
    }

    [Theory]
    [InlineData(8)]
    [InlineData(4)]
    public void Load_Indexed_DecodesAnOutOfRangePaletteIndexAsOpaqueBlack(byte bitDepth)
    {
        // libpng keeps a zero-filled 256-entry palette, so decoders built on it show such pixels as opaque black
        byte[] samples = bitDepth == 8 ? [0, 5] : [0x05];
        var data = ImageTestData.CreatePng(width: 2, height: 1, bitDepth, colorType: 3, samples, palette: [0xFF, 0x00, 0x00], transparency: [0x80, 0x00]);

        Assert.Equal([new Argb(0x80FF0000u), new Argb(0xFF000000u)], Image.Load(data).Pixels.ToArray());
    }

    [Fact]
    public void Load_Indexed8_IgnoresTransparencyEntriesBeyondThePalette()
    {
        var data = ImageTestData.CreatePng(width: 2, height: 1, bitDepth: 8, colorType: 3, samples: [0, 1], palette: [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00], transparency: [0x10, 0x20, 0x30, 0x40]);

        Assert.Equal([new Argb(0x10FF0000u), new Argb(0x2000FF00u)], Image.Load(data).Pixels.ToArray());
    }

    [Fact]
    public void Load_IgnoresTransparencyChunkInImagesWithAnAlphaChannel()
    {
        var data = ImageTestData.CreatePng(width: 1, height: 1, bitDepth: 8, colorType: 6, samples: [0x10, 0x20, 0x30, 0x40]);
        data = InsertChunkBefore(data, "IDAT", "tRNS", [0x00, 0x10, 0x00, 0x20, 0x00, 0x30]);

        Assert.Equal([new Argb(0x40102030u)], Image.Load(data).Pixels.ToArray());
    }

    [Fact]
    public void Load_IgnoresPaletteInGrayscaleImages()
    {
        var data = ImageTestData.CreatePng(width: 2, height: 1, bitDepth: 8, colorType: 0, samples: [0x10, 0x20], palette: [0xFF, 0x00, 0x00]);

        Assert.Equal([new Argb(0xFF101010u), new Argb(0xFF202020u)], Image.Load(data).Pixels.ToArray());
    }

    [Fact]
    public void Load_IgnoresDataAfterTheEndChunk()
    {
        var data = ImageTestData.CreatePng(width: 1, height: 1, bitDepth: 8, colorType: 2, samples: [0x10, 0x20, 0x30]);

        var image = Image.Load([.. data, .. "trailing data"u8]);

        Assert.Equal([new Argb(0xFF102030u)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_IgnoresAncillaryChunkWithInvalidCrc()
    {
        var data = ImageTestData.CreatePng(width: 1, height: 1, bitDepth: 8, colorType: 2, samples: [0x10, 0x20, 0x30]);
        data = InsertChunkBefore(data, "IDAT", "tEXt", "Comment\0text"u8.ToArray(), corruptCrc: true);

        Assert.Equal([new Argb(0xFF102030u)], Image.Load(data).Pixels.ToArray());
    }

    [Fact]
    public void Load_ThrowsWhenCriticalChunkCrcIsInvalid()
    {
        var data = ImageTestData.CreatePng(width: 1, height: 1, bitDepth: 8, colorType: 2, samples: [0x10, 0x20, 0x30]);
        var idatOffset = FindChunk(data, "IDAT");
        var crcOffset = idatOffset + 8 + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(idatOffset));
        data[crcOffset] ^= 0xFF;

        var exception = Assert.Throws<InvalidDataException>(() => Image.Load(data));
        Assert.Contains("CRC", exception.Message);
    }

    [Fact]
    public void Load_ReportsAnInvalidAdler32ChecksumAsCorruptData()
    {
        var data = ImageTestData.CreatePng(width: 1, height: 1, bitDepth: 8, colorType: 2, samples: [0x10, 0x20, 0x30]);
        var idatOffset = FindChunk(data, "IDAT");
        var compressedData = data.AsSpan(idatOffset + 8, (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(idatOffset))).ToArray();
        compressedData[^1] ^= 0xFF;
        data = InsertChunkBefore(RemoveChunk(data, "IDAT"), "IEND", "IDAT", compressedData);

        var exception = Assert.Throws<InvalidDataException>(() => Image.Load(data));
        Assert.Contains("corrupt", exception.Message);
    }

    [Fact]
    public void Load_AnimatedPng_DecodesTheDefaultImage()
    {
        var data = ImageTestData.CreatePng(width: 1, height: 1, bitDepth: 8, colorType: 2, samples: [0x10, 0x20, 0x30]);
        data = InsertChunkBefore(data, "IDAT", "acTL", [0, 0, 0, 2, 0, 0, 0, 0]);
        data = InsertChunkBefore(data, "IEND", "fdAT", [0, 0, 0, 1, 0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);

        Assert.Equal([new Argb(0xFF102030u)], Image.Load(data).Pixels.ToArray());
    }

    [Fact]
    public void Load_ThrowsWhenTheDimensionsExceedTheLimitWithoutAllocatingThem()
    {
        // 1-bit grayscale needs 2 bytes per row, so this header asks for a 2 GiB buffer
        var imageData = ImageTestData.CreatePngWithRawImageData(width: 1, height: 1_073_741_800, bitDepth: 1, colorType: 0, new byte[2]);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => Image.Load(imageData));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(allocated < 1024 * 1024, $"Decoding allocated {allocated} bytes.");
    }

    [Fact]
    public void Load_Rgba16_ExactComparisonSeesTheLowByte()
    {
        var expectedData = ImageTestData.CreatePng(width: 1, height: 1, bitDepth: 16, colorType: 6, samples: [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xFF, 0xFF]);
        var actualData = ImageTestData.CreatePng(width: 1, height: 1, bitDepth: 16, colorType: 6, samples: [0x12, 0x35, 0x56, 0x78, 0x9A, 0xBC, 0xFF, 0xFF]);
        var expected = Image.Load(expectedData);
        var actual = Image.Load(actualData);

        Assert.Equal(expected.Pixels.ToArray(), actual.Pixels.ToArray());
        Assert.Equal([0xFFFF, 0x1234, 0x5678, 0x9ABC], expected.HighPrecisionSamples.ToArray());
        Assert.NotEqual(expected, actual);
        Assert.False(ImageComparer.Instance.Equals(new SnapshotData("png", expectedData), new SnapshotData("png", actualData)));
    }

    [Theory]
    [InlineData(0x12, true)]
    [InlineData(0x13, false)]
    public void Load_Rgb16_EqualsAn8BitImageOnlyWhenEverySampleIsExact(byte lowByte, bool expectedResult)
    {
        // The 8-bit sample 0x12 is the 16-bit sample 0x1212
        var image16 = Image.Load(ImageTestData.CreatePng(width: 1, height: 1, bitDepth: 16, colorType: 2, samples: [0x12, lowByte, 0x34, 0x34, 0x56, 0x56]));
        var image8 = Image.Load(ImageTestData.CreatePng(width: 1, height: 1, bitDepth: 8, colorType: 2, samples: [0x12, 0x34, 0x56]));

        Assert.Equal(expectedResult, image16.Equals(image8));
        Assert.Equal(expectedResult, image8.Equals(image16));
    }

    [Fact]
    public void Load_Grayscale16_WithTransparency_KeepsTheSamples()
    {
        var image = Image.Load(ImageTestData.CreatePng(width: 2, height: 1, bitDepth: 16, colorType: 0, samples: [0x12, 0x34, 0xAB, 0xCD], transparency: [0xAB, 0xCD]));

        Assert.Equal([new Argb(0xFF121212u), new Argb(0x00ABABABu)], image.Pixels.ToArray());
        Assert.Equal([0xFFFF, 0x1234, 0x1234, 0x1234, 0x0000, 0xABCD, 0xABCD, 0xABCD], image.HighPrecisionSamples.ToArray());
    }

    [Fact]
    public void Encode_16BitImage_KeepsTheHighPrecisionSamples()
    {
        var image = Image.Load(ImageTestData.CreatePng(width: 2, height: 1, bitDepth: 16, colorType: 4, samples: [0x12, 0x34, 0x80, 0x01, 0xAB, 0xCD, 0xFF, 0xFE]));

        var roundTripped = Image.Load(PngImageEncoder.Encode(image));

        Assert.Equal(image.HighPrecisionSamples.ToArray(), roundTripped.HighPrecisionSamples.ToArray());
        Assert.Equal(image, roundTripped);
    }

    [Fact]
    public void Load_GrayscaleAlpha8_DecodesTheAlphaChannel()
    {
        var image = Image.Load(ImageTestData.CreatePng(width: 2, height: 1, bitDepth: 8, colorType: 4, samples: [0x10, 0xFF, 0x20, 0x80]));

        Assert.Equal([new Argb(0xFF101010u), new Argb(0x80202020u)], image.Pixels.ToArray());
    }

    [Fact]
    public void Load_IgnoresImageDataLongerThanTheHeaderAllowsWithoutInflatingIt()
    {
        // The header describes a 1x1 RGBA image (5 bytes of filtered image data) while the IDAT inflates to
        // 64 MiB of zeros. libpng ignores the excess; reading the whole stream would materialize all of it.
        const int InflatedSize = 64 * 1024 * 1024;
        var imageData = ImageTestData.CreatePngWithRawImageData(width: 1, height: 1, bitDepth: 8, colorType: 6, new byte[InflatedSize]);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var image = Image.Load(imageData);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal([new Argb(0x00000000u)], image.Pixels.ToArray());
        Assert.True(allocated < 4 * 1024 * 1024, $"Decoding allocated {allocated} bytes for a 1x1 image.");
    }

    [Fact]
    public void Load_ThrowsWhenTheImageDataIsShorterThanTheHeaderRequires()
    {
        var imageData = ImageTestData.CreatePngWithRawImageData(width: 2, height: 2, bitDepth: 8, colorType: 6, new byte[9]);

        Assert.Throws<InvalidDataException>(() => Image.Load(imageData));
    }

    private static int FindChunk(byte[] png, string type)
    {
        var offset = 8;
        while (offset + 8 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            if (png.AsSpan(offset + 4, 4).SequenceEqual(Encoding.ASCII.GetBytes(type)))
                return offset;

            offset += 12 + length;
        }

        throw new InvalidOperationException($"Chunk {type} not found");
    }

    private static byte[] RemoveChunk(byte[] png, string type)
    {
        var offset = FindChunk(png, type);
        var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
        return [.. png.AsSpan(0, offset), .. png.AsSpan(offset + 12 + length)];
    }

    private static byte[] InsertChunkBefore(byte[] png, string beforeType, string type, byte[] data, bool corruptCrc = false)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        typeBytes.CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        var crc = Crc32.HashToUInt32([.. typeBytes, .. data]);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), corruptCrc ? ~crc : crc);

        var offset = FindChunk(png, beforeType);
        return [.. png.AsSpan(0, offset), .. chunk, .. png.AsSpan(offset)];
    }
}
