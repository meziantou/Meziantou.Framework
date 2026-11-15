using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The compressions of OpenEXR chunks on hand-computed data and on the worked examples of the description of their data
/// formats (<c>ExrCompression.md</c> in the library): the byte predictor, the run-length coder, zlib, the PIZ wavelet,
/// range compression and Huffman coder, PXR24, B44 and DWA, their encoders included (each chunk they write decodes to the
/// samples it was given, within the loss of the compression). The files written by OpenEXR are compared in the conformance
/// tests.
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
    public void ZstandardFramesOfRawAndRunLengthBlocksAreCopied()
    {
        // A frame of one segment and a content size of 7 (header 0x20), a raw block of 4 bytes, then a last RLE block of 3
        byte[] frame = [0x28, 0xB5, 0x2F, 0xFD, 0x20, 7, 0x20, 0, 0, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 0x1B, 0, 0, (byte)'z'];
        var output = new byte[7];
        new ZstdDecoder().Decompress(frame, output);
        Assert.Equal("abcdzzz"u8.ToArray(), output);

        // A skippable frame before it is skipped; the content size and the destination must agree
        byte[] skippable = [0x5A, 0x2A, 0x4D, 0x18, 2, 0, 0, 0, 1, 2, .. frame];
        new ZstdDecoder().Decompress(skippable, output);
        Assert.Equal("abcdzzz"u8.ToArray(), output);
        Assert.Throws<InvalidDataException>(() => new ZstdDecoder().Decompress(frame, new byte[8]));
        Assert.Throws<InvalidDataException>(() => new ZstdDecoder().Decompress(frame.AsSpan(0, frame.Length - 1), output));
    }

    [Fact]
    public void ZstandardCompressedBlocksAreThoseOfTheReferenceEncoder()
    {
        // zstd -19 of "abracadabra 0; ... abracadabra 6; " (600 bytes): raw literals, then repeated matches; and its checksum
        var repeated = Convert.FromHexString(
            "28b52ffd6458015d0100b8616272616361646162726120303b203132333435363b200700ec6c5007c80764066401f20199156ed601dc2881c1");
        var text = string.Concat(Enumerable.Range(0, 40).Select(i => $"abracadabra {i % 7}; "));
        var output = new byte[text.Length];
        new ZstdDecoder().Decompress(repeated, output);
        Assert.Equal(Encoding.ASCII.GetBytes(text), output);

        // zstd -3 --no-check of 2000 pseudo-random letters of skewed frequencies: Huffman-coded literals in four streams
        var skewed = Convert.FromHexString(
            ""
            + "28b52ffd60d00645130012c90a05e00f69b00e2c104ef765b666fcba6b8a0bf5133e57d6c2bd8852eadeca505b7a71b9591b382b05068405"
            + "8154a8f1eea6d0182104108481d65b3d11f0351ba50ebc004a105e636641a123ec94fd1e28714ea68cd984a153919a0dfa636f505367979b"
            + "a80bd862ba72837434d3496d732fe80071be2e493c0c9df1fc0163324a9a8eb3e44d84466ba446ccc9713e958a7cf564ac8e25f52ae5f774"
            + "cb0066d758033a5487239ef705fa163e2b8685d49b0fd2c37321c14eff2e3684b563554d2df4d59c498eaa8a2308519a8f77d7171180be46"
            + "49ed5921297d7222f43f479019e28c6469bbaa7007e3df9874f3fbbc38c524a20b052d5629a7f08e073eb34fc70e0d2a4e153b364b2be374"
            + "14b21f0dea6816d8b258fea9894c699c810979fddda27369463c56d601312eae6524e5376254fda7a34cf2bfd680c1187525f2155f9a0537"
            + "6857ae5f23840137a77858cf86baca2f965c84201217a074c46a5026b2f5038ca4d0ec1868f99d05023f440db47bb12b7a37e0510a706dd6"
            + "9c631089631d0a0e044c5dc41ab273edd3711508f4ac715a77ec5f73e61751326bd819811783b150e7a0125b341b8a927260d3724f26899d"
            + "cd128869a2192af6685e5b7cd87f240ebda38336928de2688ccf2d5b367ca613c0fa3079bf14e704a3be1bd8ada5bff29110c3aff52a82b0"
            + "7ed80716c24e44396d69893316246c6296f1b2c68810515d183ac7827d98e02b7505004524c8d6a35d5324428eb937e1a0226e9e9372e343"
            + "eecf8c5e8c17c1231957ae80c4525c09e6b8a0c9c05fd2077ea35a30ac7088a9d78039a0a6b4ac035455114246bf792aeab603b51c17d226"
            + "8c29d8d93901fd38d82a");
        var letters = "aaaaaaaabbbbccd"u8.ToArray();
        var expected = new byte[2000];
        var state = 1u;
        for (var i = 0; i < expected.Length; i++)
        {
            state = ((state * 1103515245) + 12345) & 0x7FFFFFFF;
            expected[i] = letters[(state >> 16) % 15];
        }

        output = new byte[2000];
        new ZstdDecoder().Decompress(skewed, output);
        Assert.Equal(expected, output);

        // A corrupt checksum, and a frame that needs a dictionary, are errors
        var corrupt = repeated.ToArray();
        corrupt[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => new ZstdDecoder().Decompress(corrupt, new byte[text.Length]));
        byte[] dictionary = [0x28, 0xB5, 0x2F, 0xFD, 0x21, 1, 7, 0x20, 0, 0, 1, 2, 3, 4, 0x1B, 0, 0, 5];
        Assert.Throws<InvalidDataException>(() => new ZstdDecoder().Decompress(dictionary, new byte[7]));
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

    [Fact]
    public void TheDwaPerceptualCurveIsAPowerOfTheCodedValue()
    {
        // toLinear(x) = half(x^2.2) up to 1, half(9.025^(x - 1)) above, in single precision: the hash of the 65,536
        // little-endian values and some of them (section 8.13)
        var table = ExrDwa.GetToLinearTable();
        var bytes = new byte[table.Length * 2];
        for (var i = 0; i < table.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2 * i), table[i]);
        }

        Assert.Equal("72e80880a49b9428073cf87e2b181a6da74cfac9f8f7f4c6e459f0ac1f0225a5", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(0x0000, table[0x0001]);
        Assert.Equal(0x32F7, table[0x3800]);
        Assert.Equal(0x3C00, table[0x3C00]);
        Assert.Equal(0x3C02, table[0x3C01]);
        Assert.Equal(0x4883, table[0x4000]);
        Assert.Equal(0x7C00, table[0x4900]);
        Assert.Equal(0x0000, table[0x7C00]);
        Assert.Equal(0x8000, table[0x8001]);
        Assert.Equal(0xC883, table[0xC000]);
        Assert.Equal(0xFC00, table[0xFBFF]);
    }

    [Fact]
    public void Pxr24RoundsFloatsToTwentyFourBits()
    {
        // Section 6.2: ties away from zero, a carry into the exponent, truncation instead of infinity, payloads kept
        Assert.Equal(0x3F8000u, ExrPxr24.ToFloat24(0x3F800000));
        Assert.Equal(0x3F8001u, ExrPxr24.ToFloat24(0x3F800080));
        Assert.Equal(0x3F8000u, ExrPxr24.ToFloat24(0x3F80007F));
        Assert.Equal(0x7F7FFFu, ExrPxr24.ToFloat24(0x7F7FFFFF));
        Assert.Equal(0x800000u, ExrPxr24.ToFloat24(0x80000000));
        Assert.Equal(0x7FC000u, ExrPxr24.ToFloat24(0x7FC00000));
        Assert.Equal(0x7F8001u, ExrPxr24.ToFloat24(0x7F800001));
        Assert.Equal(0x7F8000u, ExrPxr24.ToFloat24(0x7F800000));
        Assert.Equal(0x000001u, ExrPxr24.ToFloat24(0x00000080));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 3)]
    [InlineData(64, 16)]
    public void Pxr24ChunksRoundTrip(int width, int lines)
    {
        // HALF and UINT samples come back exactly, FLOAT samples rounded to 24 bits; a sub-sampled channel has every other line
        var channels = new[]
        {
            new ExrChannel("A", ExrPixelType.Half, false, 1, 1),
            new ExrChannel("B", ExrPixelType.Float, false, 1, 1),
            new ExrChannel("C", ExrPixelType.UInt, false, 1, 1),
            new ExrChannel("D", ExrPixelType.Half, false, 1, 2),
        };
        var layout = new ExrChunkLayout(channels, width, 0, lines);
        var data = new byte[layout.GetDataLength()];
        new Random(width * lines).NextBytes(data);
        var planes = new byte[ExrPxr24.GetInflatedLength(layout)];
        Assert.Equal(planes.Length, ExrPxr24.Encode(data, planes, layout));
        var decoded = new byte[data.Length];
        ExrPxr24.Decode(planes, decoded, layout);

        var position = 0;
        for (var line = 0; line < lines; line++)
        {
            for (var c = 0; c < channels.Length; c++)
            {
                if (!layout.HasLine(c, line))
                    continue;

                for (var x = 0; x < width; x++)
                {
                    if (channels[c].PixelType == ExrPixelType.Float)
                    {
                        var bits = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position));
                        Assert.Equal(ExrPxr24.ToFloat24(bits) << 8, BinaryPrimitives.ReadUInt32LittleEndian(decoded.AsSpan(position)));
                    }
                    else
                    {
                        Assert.Equal(data.AsSpan(position, channels[c].BytesPerSample), decoded.AsSpan(position, channels[c].BytesPerSample));
                    }

                    position += channels[c].BytesPerSample;
                }
            }
        }

        Assert.Equal(data.Length, position);
    }

    [Fact]
    public void B44EncodesTheTestVectorsOfTheDescription()
    {
        // Section 7.8: sixteen 1.0
        var ones = Enumerable.Repeat((ushort)0x3C00, 16).ToArray();
        var block = new byte[14];
        Assert.Equal(3, ExrB44.EncodeBlock(ones, block, flatBlocks: true, adjustBase: true));
        Assert.Equal([0xBC, 0x00, 0xFC], block[..3]);
        Assert.Equal(14, ExrB44.EncodeBlock(ones, block, flatBlocks: false, adjustBase: true));
        Assert.Equal([0xBC, 0x00, 0x02, 0x08, 0x20, 0x82, 0x08, 0x20, 0x82, 0x08, 0x20, 0x82, 0x08, 0x20], block);

        // The largest spread of a block needs the largest shift; infinities and values that are not numbers become +0
        var spread = new ushort[16];
        spread[0] = 0xFBFF;
        spread[1] = 0x7BFF;
        spread[7] = 0x7C00;
        spread[8] = 0xFE00;
        Assert.Equal(14, ExrB44.EncodeBlock(spread, block, flatBlocks: true, adjustBase: true));
        Assert.Equal(11, block[2] >> 2);
        var planes = new byte[32];
        ExrB44.Decode(block, planes, new ExrChunkLayout([HalfChannel], 4, 0, 4));
        var values = ToUInt16(planes);
        Assert.Equal(0x7BFF, values[1]);
        Assert.True(values[7] < 0x7C00 && values[8] < 0x7C00);
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(7, 2, false)]
    [InlineData(5, 5, true)]
    [InlineData(37, 32, true)]
    public void B44ChunksRoundTripWithinTheirStep(int width, int lines, bool flat)
    {
        // A smooth half channel, a float channel stored as it is, a pLinear channel and a flat channel: within the step of
        // their blocks (B44 is coarse for blocks whose values cross zero, whose keys are far apart)
        var channels = new[]
        {
            new ExrChannel("A", ExrPixelType.Half, false, 1, 1),
            new ExrChannel("B", ExrPixelType.Float, false, 1, 1),
            new ExrChannel("C", ExrPixelType.Half, PLinear: true, 1, 1),
            new ExrChannel("D", ExrPixelType.Half, false, 1, 1),
        };
        var layout = new ExrChunkLayout(channels, width, 0, lines);
        var random = new Random(width + lines);
        var planes = new byte[layout.GetDataLength()];
        var offset = 0;
        foreach (var channel in channels)
        {
            for (var i = 0; i < width * lines; i++)
            {
                var x = i % width;
                var y = i / width;
                switch (channel.Name)
                {
                    case "A":
                        BinaryPrimitives.WriteHalfLittleEndian(planes.AsSpan(offset), (Half)(1 + (x * 0.05) - (y * 0.02) + (random.NextDouble() * 0.01)));
                        break;
                    case "B":
                        BinaryPrimitives.WriteSingleLittleEndian(planes.AsSpan(offset), (float)random.NextDouble());
                        break;
                    case "C":
                        BinaryPrimitives.WriteHalfLittleEndian(planes.AsSpan(offset), (Half)(1 + (x * 0.01)));
                        break;
                    default:
                        BinaryPrimitives.WriteHalfLittleEndian(planes.AsSpan(offset), (Half)1);
                        break;
                }

                offset += channel.BytesPerSample;
            }
        }

        var stored = new byte[(planes.Length * 4) + 1024];
        var length = ExrB44.Encode(planes, stored, layout, flat);
        Assert.True(length > 0);
        var decoded = new byte[planes.Length];
        ExrB44.Decode(stored.AsSpan(0, length), decoded, layout);
        var plane = width * lines;
        for (var i = 0; i < plane; i++)
        {
            var expected = (double)BinaryPrimitives.ReadHalfLittleEndian(planes.AsSpan(2 * i));
            var actual = (double)BinaryPrimitives.ReadHalfLittleEndian(decoded.AsSpan(2 * i));
            Assert.True(Math.Abs(expected - actual) <= Math.Max(0.02, Math.Abs(expected) / 64), $"A {i}: {expected} {actual}");
        }

        Assert.Equal(planes.AsSpan(2 * plane, 4 * plane), decoded.AsSpan(2 * plane, 4 * plane));
        for (var i = 0; i < plane; i++)
        {
            var expected = (double)BinaryPrimitives.ReadHalfLittleEndian(planes.AsSpan((6 * plane) + (2 * i)));
            var actual = (double)BinaryPrimitives.ReadHalfLittleEndian(decoded.AsSpan((6 * plane) + (2 * i)));
            Assert.True(Math.Abs(expected - actual) <= 0.01, $"C {i}: {expected} {actual}");
        }

        Assert.Equal(planes.AsSpan(8 * plane), decoded.AsSpan(8 * plane));
    }

    [Fact]
    public void TheTableOfPLinearChannelsBeforePackingIsTheExponential()
    {
        // E(x) = half(exp(x / 8)): the hash of the 65,536 little-endian values and some of them (section 7.6)
        var table = ExrB44.GetToPerceptualTable();
        var bytes = new byte[table.Length * 2];
        for (var i = 0; i < table.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2 * i), table[i]);
        }

        Assert.Equal("aeb0250f258b38cb0fb4fb86dca1e32f1f512a93b3ceca1fcf825a523324aea1", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(0x3C00, table[0x0000]);
        Assert.Equal(0x3C88, table[0x3C00]);
        Assert.Equal(0x3B0F, table[0xBC00]);
        Assert.Equal(0x7BF7, table[0x558B]);
        Assert.Equal(0x7BFF, table[0x558C]);
        Assert.Equal(0x0000, table[0x7C00]);
    }

    [Fact]
    public void TheDwaEncoderCurveIsTheInverseOfTheDecoderCurve()
    {
        // toNonlinear(x) = half(x^(1/2.2)) up to 1, half(ln(x) / 2.2 + 1) above (section 8.13)
        var table = ExrDwa.GetToNonlinearTable();
        var bytes = new byte[table.Length * 2];
        for (var i = 0; i < table.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2 * i), table[i]);
        }

        Assert.Equal("9439b1fe1d69d77b3ea98dbe0af73e5bb7f4224b61eec3335a40b0c31bbccab8", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(0x1043, table[0x0001]);
        Assert.Equal(0x2237, table[0x03FF]);
        Assert.Equal(0x39D7, table[0x3800]);
        Assert.Equal(0x3C00, table[0x3C01]);
        Assert.Equal(0x3D43, table[0x4000]);
        Assert.Equal(0x460A, table[0x7BFF]);
        Assert.Equal(0x0000, table[0x8000]);
        Assert.Equal(0xBC00, table[0xBC00]);
        Assert.Equal(0xC60A, table[0xFBFF]);
    }

    [Fact]
    public void DwaAcSymbolsAreTheRunsOfTheDescription()
    {
        // Section 8.8: literals, runs of zeros, the end of the block; a single zero is a literal
        ushort[] Symbols(params (int Z, ushort Value)[] values)
        {
            var coefficients = new ushort[64];
            foreach (var (z, value) in values)
            {
                coefficients[z] = value;
            }

            var symbols = new ushort[64];
            var count = 0;
            ExrDwaEncoder.WriteAcSymbols(coefficients, symbols, ref count);
            return symbols[..count];
        }

        Assert.Equal([0x3C00, 0xFF03, 0x3800, 0xFF00], Symbols((1, 0x3C00), (5, 0x3800)));
        Assert.Equal([0xFF00], Symbols());
        Assert.Equal([0x0000, 0x3C00, 0xFF00], Symbols((2, 0x3C00)));
        Assert.Equal([0x8000, 0xFF00], Symbols((1, 0x8000)));
        Assert.Equal([0xFF3E, 0x3C00], Symbols((63, 0x3C00)));
        Assert.Equal([0x7E00, 0xFF00], Symbols((1, 0xFF01)));
    }

    [Fact]
    public void DwaQuantizationKeepsTheValueWithTheFewestBitsWithinTheTolerance()
    {
        Assert.Equal(0, ExrDwaEncoder.Quantize(0.4f, 0.5f));
        Assert.Equal(BitConverter.HalfToUInt16Bits((Half)0.4f), ExrDwaEncoder.Quantize(0.4f, 0f));
        Assert.Equal(0x7C00, ExrDwaEncoder.Quantize(float.PositiveInfinity, 1f));
        foreach (var value in new[] { 1.2345f, -7.5f, 300.25f, -0.0123f })
        {
            foreach (var tolerance in new[] { 0.001f, 0.01f, 0.1f })
            {
                var quantized = ExrDwaEncoder.Quantize(value, tolerance);
                var half = BitConverter.HalfToUInt16Bits((Half)value);
                Assert.True(Math.Abs((float)BitConverter.UInt16BitsToHalf(quantized) - (float)(Half)value) < tolerance);
                Assert.True(BitOperations.PopCount(quantized) <= BitOperations.PopCount(half));
            }
        }
    }

    [Fact]
    public void TheDwaRuleBlockHoldsTheRulesOfTheChannels()
    {
        // Section 8.3: the default rules that match a channel
        Assert.Equal([0x0E, 0x00, 0x52, 0x00, 0x14, 0x01, 0x47, 0x00, 0x24, 0x01, 0x42, 0x00, 0x34, 0x01], GetDwaRules(["B", "G", "R"]));
        Assert.Equal([0x06, 0x00, 0x41, 0x00, 0x08, 0x01], GetDwaRules(["A"]));
        Assert.Equal([0x06, 0x00, 0x59, 0x00, 0x04, 0x01], GetDwaRules(["Y"]));
        Assert.Equal([0x02, 0x00], GetDwaRules(["Z"]));
    }

    [Theory]
    [InlineData(1, 1, 45f, true)]
    [InlineData(9, 7, 45f, true)]
    [InlineData(37, 32, 45f, false)]
    [InlineData(64, 32, 0f, true)]
    [InlineData(20, 19, 200f, true)]
    public void DwaChunksDecodeCloseToTheirSamples(int width, int lines, float level, bool huffman)
    {
        // A colour set and alpha of a layer, a luminance, a float red alone, and a channel stored with zlib: alpha and the
        // zlib channel come back exactly, the colors close to their samples (relative errors after the perceptual curve)
        var channels = new[]
        {
            new ExrChannel("A", ExrPixelType.Half, false, 1, 1),
            new ExrChannel("B", ExrPixelType.Half, false, 1, 1),
            new ExrChannel("G", ExrPixelType.Float, false, 1, 1),
            new ExrChannel("R", ExrPixelType.Half, false, 1, 1),
            new ExrChannel("Z", ExrPixelType.UInt, false, 1, 1),
            new ExrChannel("other.R", ExrPixelType.Float, false, 1, 1),
            new ExrChannel("other.Y", ExrPixelType.Half, false, 1, 2),
        };
        var layout = new ExrChunkLayout(channels, width, 0, lines);
        var data = new byte[layout.GetDataLength()];
        var random = new Random(width * lines);
        var position = 0;
        for (var line = 0; line < lines; line++)
        {
            for (var c = 0; c < channels.Length; c++)
            {
                if (!layout.HasLine(c, line))
                    continue;

                for (var x = 0; x < width; x++)
                {
                    var value = 0.2 + (0.5 * Math.Sin((x + c) * 0.3) * Math.Cos(line * 0.2)) + 0.5;
                    switch (channels[c].PixelType)
                    {
                        case ExrPixelType.Half:
                            BinaryPrimitives.WriteHalfLittleEndian(data.AsSpan(position), (Half)(channels[c].Name == "A" ? random.NextDouble() : value));
                            position += 2;
                            break;
                        case ExrPixelType.Float:
                            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(position), (float)value);
                            position += 4;
                            break;
                        default:
                            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(position), (uint)random.Next());
                            position += 4;
                            break;
                    }
                }
            }
        }

        var scope = AllocationScope.Create(ImageConfiguration.Default, "exr dwa");
        using var encoder = new ExrDwaEncoder(scope, level, data.Length + 4096, width, lines, channels.Length, huffman);
        var stored = new byte[(data.Length * 4) + 4096];
        var length = encoder.Compress(data, stored, layout, huffman);
        Assert.True(length > 88);
        Assert.Equal(huffman ? 0 : 1, BinaryPrimitives.ReadInt64LittleEndian(stored.AsSpan(80)));
        using var decoder = new ExrDwa(scope);
        var decoded = new byte[data.Length];
        decoder.Decompress(stored.AsSpan(0, length), decoded, layout);

        var tolerance = level switch
        {
            0f => 0.002,
            45f => 0.03,
            _ => 0.12,
        };
        position = 0;
        for (var line = 0; line < lines; line++)
        {
            for (var c = 0; c < channels.Length; c++)
            {
                if (!layout.HasLine(c, line))
                    continue;

                for (var x = 0; x < width; x++)
                {
                    var name = channels[c].Name;
                    switch (channels[c].PixelType)
                    {
                        case ExrPixelType.Half when name == "A":
                            Assert.Equal(data.AsSpan(position, 2), decoded.AsSpan(position, 2));
                            position += 2;
                            break;
                        case ExrPixelType.Half:
                        {
                            var expected = (double)BinaryPrimitives.ReadHalfLittleEndian(data.AsSpan(position));
                            var actual = (double)BinaryPrimitives.ReadHalfLittleEndian(decoded.AsSpan(position));
                            Assert.True(Math.Abs(expected - actual) <= tolerance * Math.Max(1, expected), $"{name} ({x}, {line}): {expected} {actual}");
                            position += 2;
                            break;
                        }

                        case ExrPixelType.Float:
                        {
                            var expected = (double)BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(position));
                            var actual = BinaryPrimitives.ReadSingleLittleEndian(decoded.AsSpan(position));
                            Assert.True(Math.Abs(expected - actual) <= tolerance * Math.Max(1, expected), $"{name} ({x}, {line}): {expected} {actual}");
                            Assert.Equal(actual, (float)(Half)actual);
                            position += 4;
                            break;
                        }

                        default:
                            Assert.Equal(data.AsSpan(position, 4), decoded.AsSpan(position, 4));
                            position += 4;
                            break;
                    }
                }
            }
        }
    }

    [Fact]
    public void AConstantDwaBlockIsExact()
    {
        // A block of one value has only a DC coefficient, which the inverse of the DC-only block gives back
        var channels = new[] { new ExrChannel("Y", ExrPixelType.Half, false, 1, 1) };
        var layout = new ExrChunkLayout(channels, 8, 0, 8);
        var data = new byte[128];
        for (var i = 0; i < 64; i++)
        {
            BinaryPrimitives.WriteHalfLittleEndian(data.AsSpan(2 * i), (Half)1f);
        }

        var scope = AllocationScope.Create(ImageConfiguration.Default, "exr dwa");
        using var encoder = new ExrDwaEncoder(scope, 45, data.Length, 8, 8, 1, huffman: true);
        var stored = new byte[1024];
        var length = encoder.Compress(data, stored, layout, huffman: true);
        using var decoder = new ExrDwa(scope);
        var decoded = new byte[data.Length];
        decoder.Decompress(stored.AsSpan(0, length), decoded, layout);
        Assert.Equal(data, decoded);
    }

    private static byte[] GetDwaRules(string[] names)
    {
        var channels = names.Select(name => new ExrChannel(name, ExrPixelType.Half, false, 1, 1)).ToArray();
        var layout = new ExrChunkLayout(channels, 1, 0, 1);
        var scope = AllocationScope.Create(ImageConfiguration.Default, "exr dwa");
        using var encoder = new ExrDwaEncoder(scope, 45, 4096, 1, 1, channels.Length, huffman: true);
        var stored = new byte[1024];
        Assert.True(encoder.Compress(new byte[layout.GetDataLength()], stored, layout, huffman: true) > 0);
        var size = BinaryPrimitives.ReadUInt16LittleEndian(stored.AsSpan(88));
        return stored.AsSpan(88, size).ToArray();
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
