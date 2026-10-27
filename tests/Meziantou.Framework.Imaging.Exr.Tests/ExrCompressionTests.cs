using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The compressions of OpenEXR chunks on hand-computed data and on the worked examples of the description of their data
/// formats (<c>ExrCompression.md</c> in the library): the byte predictor, the run-length coder, zlib, the PIZ wavelet,
/// range compression and Huffman coder, PXR24 and B44. The files written by OpenEXR are compared in the conformance tests.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class ExrCompressionTests
{
    private static readonly ExrChannel HalfChannel = new("Y", ExrPixelType.Half, PLinear: false, XSampling: 1, YSampling: 1);

    [Fact]
    public void ThePredictorSplitsEvenAndOddBytesThenStoresDifferencesPlus128()
    {
        // t = 1 3 5 2 4 (even offsets, then odd ones): 1, 3 - 1 + 128, 5 - 3 + 128, 2 - 5 + 128, 4 - 2 + 128
        byte[] source = [1, 2, 3, 4, 5];
        var encoded = new byte[5];
        ExrPredictor.Encode(source, encoded);
        Assert.Equal([1, 130, 130, 125, 130], encoded);

        var decoded = new byte[5];
        ExrPredictor.Decode(encoded, decoded);
        Assert.Equal(source, decoded);

        // Differences wrap modulo 256, and equal bytes give 128
        byte[] wrap = [255, 0, 255, 0];
        var transformed = new byte[4];
        ExrPredictor.Encode(wrap, transformed);
        Assert.Equal([255, 128, 129, 128], transformed);
    }

    [Fact]
    public void RunLengthPacketsAreRepeatsAndLiterals()
    {
        // 2: three 7s; -2: the literal 1 2; 0: one 9; -128: a literal of 128 bytes
        var literal = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
        byte[] packets = [2, 7, 0xFE, 1, 2, 0, 9, 0x80, .. literal];
        var output = new byte[3 + 2 + 1 + 128];
        ExrRle.Decode(packets, output);
        Assert.Equal([7, 7, 7, 1, 2, 9, .. literal], output);

        Assert.Throws<InvalidImageContentException>(() => ExrRle.Decode([5, 1], new byte[5]));
        Assert.Throws<InvalidImageContentException>(() => ExrRle.Decode([2], new byte[3]));
        Assert.Throws<InvalidImageContentException>(() => ExrRle.Decode([0xFD, 1, 2], new byte[3]));
        Assert.Throws<InvalidImageContentException>(() => ExrRle.Decode([1, 4], new byte[3]));
        Assert.Throws<InvalidImageContentException>(() => ExrRle.Decode([2, 4, 0, 4], new byte[3]));
    }

    [Fact]
    public void TheRunLengthEncoderWritesRunsOfThreeTo128AndShortLiterals()
    {
        var data = new byte[129];
        data.AsSpan().Fill(4);
        var packets = new byte[200];
        var length = ExrRle.Encode(data, packets);
        Assert.Equal([127, 4, 0xFF, 4], packets[..length]);

        // Alternating bytes: literals of at most 127 bytes
        var alternating = Enumerable.Range(0, 300).Select(i => (byte)(i % 2)).ToArray();
        packets = new byte[400];
        length = ExrRle.Encode(alternating, packets);
        Assert.Equal(0x81, packets[0]);
        Assert.Equal(0x81, packets[128]);
        var decoded = new byte[300];
        ExrRle.Decode(packets.AsSpan(0, length), decoded);
        Assert.Equal(alternating, decoded);

        Assert.Equal(-1, ExrRle.Encode(alternating, new byte[10]));
    }

    [Fact]
    public void ZipDataMustProduceTheChunkAndEndWithIt()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, "exr zlib");
        byte[] expected = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var zlib = Zlib(expected);
        using (var buffer = Copy(scope, zlib))
        {
            var output = new byte[10];
            ExrZlib.Inflate(buffer, output);
            Assert.Equal(expected, output);
            Assert.Throws<InvalidImageContentException>(() => ExrZlib.Inflate(buffer, new byte[11]));
            Assert.Throws<InvalidImageContentException>(() => ExrZlib.Inflate(buffer, new byte[9]));
        }

        // Truncated, followed by another byte, or with a wrong checksum
        foreach (var damaged in new[] { zlib[..^1], [.. zlib, 0], [.. zlib[..^1], (byte)(zlib[^1] ^ 1)] })
        {
            using var buffer = Copy(scope, damaged);
            Assert.Throws<InvalidImageContentException>(() => ExrZlib.Inflate(buffer, new byte[10]));
        }
    }

    [Fact]
    public void TheFourteenBitPairTransformIsAnExactSignedHaarStep()
    {
        // l = floor((a + b) / 2), h = a - b; a = l + ceil(h / 2), b = a - h
        Assert.Equal(((ushort)4, (ushort)2), ExrWavelet.Forward(5, 3, fourteenBits: true));
        Assert.Equal(((ushort)0, unchecked((ushort)-7)), ExrWavelet.Forward(unchecked((ushort)-3), 4, fourteenBits: true));
        Assert.Equal(((ushort)5, (ushort)3), ExrWavelet.Inverse(4, 2, fourteenBits: true));
        Assert.Equal((unchecked((ushort)-3), (ushort)4), ExrWavelet.Inverse(0, unchecked((ushort)-7), fourteenBits: true));
        for (var a = -8192; a < 8192; a += 97)
        {
            for (var b = -8192; b < 8192; b += 89)
            {
                var (low, high) = ExrWavelet.Forward((ushort)a, (ushort)b, fourteenBits: true);
                Assert.Equal(((ushort)a, (ushort)b), ExrWavelet.Inverse(low, high, fourteenBits: true));
            }
        }
    }

    [Fact]
    public void TheSixteenBitPairTransformIsExactModulo65536()
    {
        // a' = a + 32768, m = floor((a' + b) / 2), d = a' - b, m moved by 32768 when d < 0, d modulo 65536
        Assert.Equal(((ushort)16384, (ushort)32768), ExrWavelet.Forward(0, 0, fourteenBits: false));
        Assert.Equal(((ushort)0, (ushort)0), ExrWavelet.Inverse(16384, 32768, fourteenBits: false));
        Assert.Equal(((ushort)((32768 + 65535) / 2 + 32768 - 65536), (ushort)(32768 - 65535 + 65536)), ExrWavelet.Forward(0, 65535, fourteenBits: false));
        for (var a = 0; a < 65536; a += 251)
        {
            for (var b = 0; b < 65536; b += 241)
            {
                var (low, high) = ExrWavelet.Forward((ushort)a, (ushort)b, fourteenBits: false);
                Assert.Equal(((ushort)a, (ushort)b), ExrWavelet.Inverse(low, high, fourteenBits: false));
            }
        }
    }

    [Theory]
    [InlineData(1, 9)]
    [InlineData(9, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(5, 6)]
    [InlineData(6, 5)]
    [InlineData(7, 3)]
    [InlineData(13, 27)]
    [InlineData(64, 33)]
    public void TheWaveletOfAPlaneIsInvertible(int nx, int ny)
    {
        foreach (var fourteenBits in new[] { true, false })
        {
            var random = new Random(nx * 1000 + ny);
            var words = Enumerable.Range(0, nx * ny * 2).Select(_ => (ushort)random.Next(fourteenBits ? 1 << 14 : 1 << 16)).ToArray();
            var original = words.ToArray();

            // Two interleaved planes, as the two words of 32-bit samples
            for (var j = 0; j < 2; j++)
            {
                ExrWavelet.Encode(words, j, nx, 2, ny, 2 * nx, fourteenBits);
            }

            if (Math.Min(nx, ny) >= 2)
            {
                Assert.NotEqual(original, words);
            }

            for (var j = 0; j < 2; j++)
            {
                ExrWavelet.Decode(words, j, nx, 2, ny, 2 * nx, fourteenBits);
            }

            Assert.Equal(original, words);
        }
    }

    [Fact]
    public void FourZerosAreTheWorkedHuffmanExample()
    {
        // Section 5.4.5: symbol 0 and the run symbol 1 have one-bit codes; the run of 4 is not worth coding
        byte[] expected = [0, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0x04, 0x10, 0x00];
        var scope = AllocationScope.Create(ImageConfiguration.Default, "exr huffman");
        using var huffman = new ExrHuffman(scope, encoder: true);
        var output = new byte[64];
        var length = huffman.Encode(new ushort[4], output);
        Assert.Equal(expected, output[..length]);

        var decoded = new ushort[4];
        decoded.AsSpan().Fill(9);
        huffman.Decode(expected, decoded);
        Assert.Equal(new ushort[4], decoded);
    }

    [Fact]
    public void ThreeHundredZerosAreTwoRuns()
    {
        // 0, run, 255, then 0, run, 43: 20 bits, 7F D2 B0
        byte[] expected = [0, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 20, 0, 0, 0, 0, 0, 0, 0, 0x04, 0x10, 0x7F, 0xD2, 0xB0];
        var scope = AllocationScope.Create(ImageConfiguration.Default, "exr huffman");
        using var huffman = new ExrHuffman(scope, encoder: true);
        var output = new byte[64];
        var length = huffman.Encode(new ushort[300], output);
        Assert.Equal(expected, output[..length]);

        var decoded = new ushort[300];
        decoded.AsSpan().Fill(9);
        huffman.Decode(expected, decoded);
        Assert.Equal(new ushort[300], decoded);

        // One value too many or too few, a missing bit, a run that starts the data, an incomplete code
        Assert.Throws<InvalidImageContentException>(() => huffman.Decode(expected, new ushort[301]));
        Assert.Throws<InvalidImageContentException>(() => huffman.Decode(expected, new ushort[299]));
        Assert.Throws<InvalidImageContentException>(() => huffman.Decode(WithBitCount(expected, 19), new ushort[300]));
        byte[] runFirst = [0, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 9, 0, 0, 0, 0, 0, 0, 0, 0x04, 0x10, 0x80, 0x80];
        Assert.Throws<InvalidImageContentException>(() => huffman.Decode(runFirst, new ushort[2]));
        byte[] incomplete = [0, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0x04, 0x20, 0x00];
        Assert.Throws<InvalidImageContentException>(() => huffman.Decode(incomplete, new ushort[1]));
    }

    [Fact]
    public void HuffmanStreamsOfManySymbolsRoundTrip()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, "exr huffman");
        using var huffman = new ExrHuffman(scope, encoder: true);
        var random = new Random(42);

        // Skewed values with long runs, the largest value (the run symbol is then 65536) and gaps of absent symbols
        var values = new ushort[20000];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (i / 300 % 3) switch
            {
                0 => 7,
                1 => (ushort)(random.Next(4) * 1000),
                _ => (ushort)(random.NextDouble() < 0.001 ? 65535 : random.Next(20)),
            };
        }

        var output = new byte[100000];
        var length = huffman.Encode(values, output);
        Assert.True(length > 0);
        var decoded = new ushort[values.Length];
        huffman.Decode(output.AsSpan(0, length), decoded);
        Assert.Equal(values, decoded);
        Assert.Equal(-1, huffman.Encode(values, new byte[100]));
    }

    [Fact]
    public void AnAllZeroPizChunkHasAnEmptyBitmap()
    {
        // First byte 8191, last byte 0: no bitmap; the Huffman stream of four zeros
        byte[] stream = [0, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0x04, 0x10, 0x00];
        byte[] chunk = [0xFF, 0x1F, 0x00, 0x00, (byte)stream.Length, 0, 0, 0, .. stream];
        var scope = AllocationScope.Create(ImageConfiguration.Default, "exr piz");
        using var piz = new ExrPiz(scope, maxDataLength: 8, encoder: true);
        var layout = new ExrChunkLayout([HalfChannel], Width: 2, Y0: 0, Lines: 2);
        var output = new byte[8];
        output.AsSpan().Fill(0xAA);
        piz.Decompress(chunk, output, layout);
        Assert.Equal(new byte[8], output);

        var compressed = new byte[64];
        var length = piz.Compress(new byte[8], compressed, layout);
        Assert.Equal(chunk, compressed[..length]);

        Assert.Throws<InvalidImageContentException>(() => piz.Decompress(chunk.AsSpan(0, chunk.Length - 1), output, layout));
        Assert.Throws<InvalidImageContentException>(() => piz.Decompress([0x00, 0x00, 0x00, 0x20, 0, 0, 0, 0], output, layout));
    }

    [Theory]
    [InlineData(1, 37, 32)]
    [InlineData(2, 5, 7)]
    [InlineData(0, 64, 3)]
    public void PizChunksRoundTrip(int pixelType, int width, int lines)
    {
        var type = (ExrPixelType)pixelType;
        var channels = new[] { new ExrChannel("A", ExrPixelType.Half, false, 1, 1), new ExrChannel("B", type, false, 1, 1) };
        var layout = new ExrChunkLayout(channels, width, 0, lines);
        var length = layout.GetDataLength();
        var random = new Random(width * lines);
        var data = new byte[length];
        random.NextBytes(data);
        var scope = AllocationScope.Create(ImageConfiguration.Default, "exr piz");
        using var piz = new ExrPiz(scope, length, encoder: true);
        var compressed = new byte[(length * 2) + 100_000];
        var compressedLength = piz.Compress(data, compressed, layout);
        Assert.True(compressedLength > 0);
        var decoded = new byte[length];
        piz.Decompress(compressed.AsSpan(0, compressedLength), decoded, layout);
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void Pxr24RebuildsSamplesFromBytePlanesOfDifferences()
    {
        // FLOAT 1.0 and 2.0: 24-bit 0x3F8000 and 0x400000, differences 0x3F8000 and 0x008000
        var channels = new[] { new ExrChannel("R", ExrPixelType.Float, false, 1, 1) };
        var layout = new ExrChunkLayout(channels, 2, 0, 1);
        Assert.Equal(6, ExrPxr24.GetInflatedLength(layout));
        var output = new byte[8];
        ExrPxr24.Decode([0x3F, 0x00, 0x80, 0x80, 0x00, 0x00], output, layout);
        Assert.Equal(1f, BinaryPrimitives.ReadSingleLittleEndian(output));
        Assert.Equal(2f, BinaryPrimitives.ReadSingleLittleEndian(output.AsSpan(4)));

        // UINT differences wrap modulo 2^32 and restart at each line; HALF ones modulo 2^16
        layout = new ExrChunkLayout([new ExrChannel("R", ExrPixelType.UInt, false, 1, 1), new ExrChannel("S", ExrPixelType.Half, false, 1, 1)], 2, 0, 2);
        byte[] planes =
        [
            0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x01, // line 0, UINT: 0xFFFFFFFF, then + 1
            0xFF, 0x00, 0xFF, 0x02, // line 0, HALF: 0xFFFF, then + 2
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x01, // line 1, UINT: 5, then + 1
            0x3C, 0x00, 0x00, 0x00, // line 1, HALF: 0x3C00, then + 0
        ];
        output = new byte[24];
        ExrPxr24.Decode(planes, output, layout);
        Assert.Equal(0xFFFFFFFFu, BinaryPrimitives.ReadUInt32LittleEndian(output));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(4)));
        Assert.Equal((ushort)0xFFFF, BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(8)));
        Assert.Equal((ushort)0x0001, BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(10)));
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(12)));
        Assert.Equal(6u, BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(16)));
        Assert.Equal((ushort)0x3C00, BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(20)));
        Assert.Equal((ushort)0x3C00, BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(22)));
    }

    [Fact]
    public void B44BlocksOfOneValueHaveTwoForms()
    {
        // Section 7.8: sixteen 1.0 (key 0xBC00), as 14 bytes with the shift 0 and every difference 32, or as 3 bytes
        var layout = new ExrChunkLayout([HalfChannel], 4, 0, 4);
        foreach (var block in new byte[][] { [0xBC, 0x00, 0x02, 0x08, 0x20, 0x82, 0x08, 0x20, 0x82, 0x08, 0x20, 0x82, 0x08, 0x20], [0xBC, 0x00, 0xFC] })
        {
            var planes = new byte[32];
            ExrB44.Decode(block, planes, layout);
            Assert.All(ToUInt16(planes), value => Assert.Equal((ushort)0x3C00, value));
        }

        Assert.Throws<InvalidImageContentException>(() => ExrB44.Decode([0xBC, 0x00, 0x02], new byte[32], layout));
    }

    [Fact]
    public void B44DifferencesAreScaledByTheShift()
    {
        // t0 = 0x8000 (+0.0), shift 1: t4 = t0 + (33 - 32) * 2, the other differences 0
        var fields = new int[16];
        fields[0] = 1;
        fields.AsSpan(1).Fill(32);
        fields[1] = 33;
        var block = new byte[14];
        block[0] = 0x80;
        UInt128 bits = 0;
        foreach (var field in fields)
        {
            bits = (bits << 6) | (uint)field;
        }

        for (var i = 0; i < 12; i++)
        {
            block[2 + i] = (byte)(bits >> (88 - (8 * i)));
        }

        // A 3x3 plane: only its samples are written
        var layout = new ExrChunkLayout([HalfChannel], 3, 0, 3);
        var planes = new byte[18];
        ExrB44.Decode(block, planes, layout);
        var values = ToUInt16(planes);
        Assert.Equal([0, 0, 0, 2, 2, 2, 2, 2, 2], values);
    }

    [Fact]
    public void TheTableOfPLinearChannelsIsTheExponentialOfTheStoredLogarithm()
    {
        // L(x) = half(8 ln(x)): the hash of the 65,536 little-endian values and some of them (section 7.6)
        var table = ExrB44.GetFromPerceptualTable();
        var bytes = new byte[table.Length * 2];
        for (var i = 0; i < table.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2 * i), table[i]);
        }

        Assert.Equal("92de2787a44544c6e4d54f942f38cf50453b342f179189dbaeb514743fc9109f", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(0xFC00, table[0x0000]);
        Assert.Equal(0xD829, table[0x0001]);
        Assert.Equal(0x0000, table[0x3C00]);
        Assert.Equal(0x458C, table[0x4000]);
        Assert.Equal(0x4800, table[0x4170]);
        Assert.Equal(0x558C, table[0x7BFF]);
        Assert.Equal(0xFC00, table[0x8000]);
        Assert.Equal(0x0000, table[0x8001]);
    }

    private static ushort[] ToUInt16(byte[] bytes)
    {
        var values = new ushort[bytes.Length / 2];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2 * i));
        }

        return values;
    }

    private static byte[] WithBitCount(byte[] stream, int bitCount)
    {
        var copy = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(12), bitCount);
        return copy;
    }

    private static PooledBuffer Copy(AllocationScope scope, byte[] data)
    {
        var buffer = scope.Rent(data.Length, AllocationKind.Temporary, clear: false);
        data.CopyTo(buffer.Span);
        return buffer;
    }

    private static byte[] Zlib(byte[] data)
    {
        using var stream = new MemoryStream();
        using (var zlib = new ZLibStream(stream, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return stream.ToArray();
    }
}
