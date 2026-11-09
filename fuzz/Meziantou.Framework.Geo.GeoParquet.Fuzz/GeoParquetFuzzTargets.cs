using System.IO.Compression;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Geo;
using Meziantou.Framework.Geo.Fuzz;
using Meziantou.Framework.Geo.Internals;
using Meziantou.Framework.Geo.Internals.Parquet;
using static Meziantou.Framework.GeoPackageFuzz.GeoFeatureDescriber;

namespace Meziantou.Framework.GeoParquetFuzz;

/// <summary>
/// The GeoParquet reader rejects an invalid file with a <see cref="FormatException"/>, or a <see cref="NotSupportedException"/>
/// for a feature it does not support, and never throws anything else. The streaming reader returns the features of the
/// reader of the whole file. The decoders of the Parquet format return what they are asked for, or reject invalid data.
/// </summary>
public static class GeoParquetFuzzTargets
{
    private const string TestDataPrefix = "TestData/";

    // The decompressed pages of the codec target are at most 1 MB
    private const int MaxDecompressedLength = 1 << 20;

    private static readonly string[] ParquetTokens =
    [
        "PAR1", "PARE", "geo", "\"primary_column\"", "\"columns\"", "\"encoding\"", "\"WKB\"", "\"covering\"", "\"bbox\"", "\"xmin\"", "geometry",
        "\u0015", "\u0016", "\u0018", "\u0019", "\u001C", "&", "\u0000", "\u0001", "\u0002", "\u0003", "\u0004", "\u0005", "\u0006", "\u0007", "\u0008",
        "ÿÿÿÿ\u000F", "\u0080\u0080\u0080\u0080\u0010", "\u0001\u0001\u0000\u0000\u0000", "\u0000\u0000\u0000\u0000\u0000\u0000ø\u007F",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("geo-geoparquet", GeoParquetTarget) { Seeds = GetGeoParquetSeeds, Dictionary = ParquetTokens },
        new("geo-parquet-compression", ParquetCompressionTarget) { Seeds = GetParquetCompressionSeeds, Dictionary = ParquetTokens },
        new("geo-parquet-encodings", ParquetEncodingsTarget) { Seeds = GetParquetEncodingsSeeds, Dictionary = ParquetTokens },
    ];

    /// <summary>
    /// <c>options, west, south, east, north, data</c>: a GeoParquet file. The readers reject an invalid file with a
    /// <see cref="FormatException"/>, or a <see cref="NotSupportedException"/> for a feature they do not support, and never
    /// throw anything else. <see cref="GeoParquet.ReadFeaturesAsync(Stream, GeoParquetReaderOptions?, CancellationToken)"/>
    /// returns the features of <see cref="GeoParquet.ReadAsync(Stream, GeoParquetReaderOptions?, CancellationToken)"/> from a
    /// stream whose reads return at most <c>1 + options % 64</c> bytes. With a box (bit 7 of <c>options</c>), the features
    /// are some of the features whose bounds intersect the box, in order: the statistics of a row group can skip it, and a
    /// read that skips a row group may succeed where a read of the whole file fails.
    /// </summary>
    private static void GeoParquetTarget(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var box = ReadBox(ref data);
        var bytes = data.ToArray();
        var expected = ReadGeoParquet(() => Wait(GeoParquet.ReadAsync(new MemoryStream(bytes, writable: false))), out var expectedError);
        var (actual, error) = ReadAllGeoParquet(() => GeoParquet.ReadFeaturesAsync(new ShortReadStream(bytes, 1 + (options % 64), canSeek: true)));
        if (expected is not null)
        {
            expected.Name = null;
        }

        FuzzAssert.Equal(expectedError?.GetType(), error?.GetType(), $"GeoParquet.ReadAsync and GeoParquet.ReadFeaturesAsync disagree on whether the file is valid: {expectedError?.Message} / {error?.Message}");
        if (expected is not null)
        {
            Exercise(expected);
            FuzzAssert.Equal(Describe(expected), Describe(actual), "GeoParquet.ReadFeaturesAsync returns other features than GeoParquet.ReadAsync");
        }

        if ((options & 0x80) is 0)
            return;

        var (bounded, boundedError) = ReadAllGeoParquet(() => GeoParquet.ReadFeaturesAsync(new MemoryStream(bytes, writable: false), new GeoParquetReaderOptions { Bounds = box }));
        if (expected is null)
            return;

        FuzzAssert.True(boundedError is null, $"A read with a box fails where a read of the whole file succeeds: {boundedError?.Message}");
        var filtered = expected.Where(feature => feature.Geometry?.GetBounds() is { } bounds && Intersects(box, bounds)).Select(feature => Describe([feature])).ToList();
        var next = 0;
        foreach (var feature in bounded.Select(feature => Describe([feature])))
        {
            while (next < filtered.Count && filtered[next] != feature)
            {
                next++;
            }

            FuzzAssert.True(next < filtered.Count, "A read with a box returns a feature that does not intersect the box, or out of order");
            next++;
        }
    }

    /// <summary>
    /// <c>codec, length (3 bytes, little-endian), data</c>: a compressed page and its decompressed length. The decompressors
    /// reject invalid data with a <see cref="FormatException"/>, and return exactly the declared length. GZIP, Brotli and
    /// Zstandard give the bytes that .NET decompresses. The Zstandard decoder shared with Meziantou.Framework.Imaging (that
    /// of the library before .NET 11, a copy of the fuzz project from .NET 11) gives them too.
    /// </summary>
    private static void ParquetCompressionTarget(ReadOnlySpan<byte> data)
    {
        int[] codecs = [ParquetCompression.Uncompressed, ParquetCompression.Snappy, ParquetCompression.Gzip, ParquetCompression.Brotli, ParquetCompression.Lz4Hadoop, ParquetCompression.Zstd, ParquetCompression.Lz4Raw];
        var codec = FuzzInput.Pick(codecs, FuzzInput.ReadByte(ref data));
        var length = (FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8) | (FuzzInput.ReadByte(ref data) << 16)) % (MaxDecompressedLength + 1);
        var input = data.ToArray();
        if (codec == ParquetCompression.Zstd)
        {
            CheckZstdDecoder(input, length);
        }

        byte[] output;
        try
        {
            output = ParquetCompression.Decompress(codec, input, 0, input.Length, length);
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
            return;
        }

        FuzzAssert.Equal(length, output.Length, "The decompressed page does not have the declared length");
        var reference = codec switch
        {
            ParquetCompression.Gzip => Decompress(input, stream => new GZipStream(stream, CompressionMode.Decompress)),
            ParquetCompression.Brotli => Decompress(input, stream => new BrotliStream(stream, CompressionMode.Decompress)),
#if NET11_0_OR_GREATER
            ParquetCompression.Zstd => Decompress(input, stream => new ZstandardStream(stream, CompressionMode.Decompress)),
#endif
            _ => null,
        };

        if (reference is not null)
        {
            // .NET decompresses the whole data; the page is its beginning
            FuzzAssert.True(reference.AsSpan().StartsWith(output), "The decompressed page is not what .NET decompresses");
        }

        static void CheckZstdDecoder(byte[] input, int length)
        {
            var output = new byte[length];
            try
            {
                new ZstdDecoder().Decompress(input, output);
            }
            catch (InvalidDataException)
            {
                return;
            }

#if NET11_0_OR_GREATER
            // .NET may reject data the decoder accepts, such as a frame whose window is larger than its limit
            var reference = Decompress(input, stream => new ZstandardStream(stream, CompressionMode.Decompress));
            if (reference is not null)
            {
                FuzzAssert.True(reference.AsSpan().SequenceEqual(output), "The Zstandard decoder does not give what .NET decompresses");
            }
#endif
        }

        // The reference decompression, or null when .NET rejects the data: the decoder stops at the declared length, so it
        // may accept data that .NET rejects after it, such as a second GZIP member that is not valid
        static byte[]? Decompress(byte[] input, Func<Stream, Stream> create)
        {
            try
            {
                using var source = new MemoryStream(input);
                using var decompressor = create(source);
                using var copy = new MemoryStream();
                decompressor.CopyTo(copy);
                return copy.ToArray();
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// <c>kind, parameters, data</c>: the integer encodings of Parquet (the RLE/bit-packing hybrid, the deprecated
    /// BIT_PACKED encoding, DELTA_BINARY_PACKED) and the Thrift metadata (the footer and a page header). They reject invalid
    /// data with a <see cref="FormatException"/>, return the number of values they are asked for, and the values fit in
    /// their bit width.
    /// </summary>
    private static void ParquetEncodingsTarget(ReadOnlySpan<byte> data)
    {
        var kind = FuzzInput.ReadByte(ref data) % 5;
        var bitWidth = FuzzInput.ReadByte(ref data) % 33;
        var count = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
        try
        {
            switch (kind)
            {
                case 0:
                    var hybrid = ParquetEncodings.DecodeRleBitPacked(data, bitWidth, count);
                    FuzzAssert.Equal(count, hybrid.Length, "The RLE/bit-packing hybrid decoder does not return the number of values asked for");
                    AssertFits(hybrid, bitWidth);
                    break;

                case 1:
                    var packed = ParquetEncodings.DecodeBitPacked(data, Math.Min(bitWidth, 31), count, out var length);
                    FuzzAssert.Equal(count, packed.Length, "The BIT_PACKED decoder does not return the number of values asked for");
                    FuzzAssert.True(length <= data.Length, "The BIT_PACKED decoder reads more data than it has");
                    AssertFits(packed, Math.Min(bitWidth, 31));
                    break;

                case 2:
                    var deltas = ParquetEncodings.DecodeDeltaBinaryPacked(data, count, out var deltaLength);
                    FuzzAssert.True(deltas.Length <= count, "The DELTA_BINARY_PACKED decoder returns more values than the page has");
                    FuzzAssert.True(deltaLength <= data.Length, "The DELTA_BINARY_PACKED decoder reads more data than it has");
                    break;

                case 3:
                    ParquetMetadata.ReadFileMetadata(data);
                    break;

                default:
                    ParquetMetadata.ReadPageHeader(data, out var headerLength);
                    FuzzAssert.True(headerLength <= data.Length, "The page header reader reads more data than it has");
                    break;
            }
        }
        catch (FormatException)
        {
        }

        static void AssertFits(int[] values, int bitWidth)
        {
            if (bitWidth < 32)
            {
                FuzzAssert.True(values.All(value => (uint)value < 1u << bitWidth), "A decoded value does not fit in its bit width");
            }
        }
    }

    private static GeoFeatureCollection? ReadGeoParquet(Func<GeoFeatureCollection> read, out Exception? error)
    {
        try
        {
            error = null;
            return read();
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
            error = ex;
            return null;
        }
    }

    private static (List<GeoFeature> Features, Exception? Error) ReadAllGeoParquet(Func<IAsyncEnumerable<GeoFeature>> read)
    {
        try
        {
            var (features, error) = ReadAll(read);
            return (features, error);
        }
        catch (NotSupportedException ex)
        {
            return ([], ex);
        }
    }

    // The streams complete synchronously
    private static T Wait<T>(Task<T> task)
    {
        FuzzAssert.True(task.IsCompleted, "A reader did not complete synchronously on a stream that completes synchronously");
        return task.GetAwaiter().GetResult();
    }

    private static IEnumerable<FuzzSeed> GetGeoParquetSeeds()
    {
        foreach (var (name, data) in GetTestFiles())
        {
            yield return new FuzzSeed(name, (byte[])[255, 0x80, 0x80, 0x7F, 0x7F, .. data]);
            yield return new FuzzSeed(name + "-bounded", (byte[])[0x80 | 6, 0xE2, 0, 0xE7, 10, .. data]);
        }
    }

    private static IEnumerable<FuzzSeed> GetParquetCompressionSeeds()
    {
        byte[] text = [.. "GeoParquet GeoParquet GeoParquet, a columnar format of features"u8];
        yield return new FuzzSeed("uncompressed", (byte[])[0, (byte)text.Length, 0, 0, .. text]);
        // A literal, a copy of 8 bytes at offset 4, and a literal: "abcdabcdabcdwxyz"
        yield return new FuzzSeed("snappy", (byte[])[1, 16, 0, 0, 16, 0x0C, .. "abcd"u8, 0x11, 4, 0x0C, .. "wxyz"u8]);
        yield return new FuzzSeed("gzip", (byte[])[2, (byte)text.Length, 0, 0, .. Compress(text, stream => new GZipStream(stream, CompressionLevel.Optimal))]);
        yield return new FuzzSeed("brotli", (byte[])[3, (byte)text.Length, 0, 0, .. Compress(text, stream => new BrotliStream(stream, CompressionLevel.Optimal))]);
        // A sequence of 3 literals and a match of 5 bytes at offset 3, then the last literals: "abcabcabxyz"
        yield return new FuzzSeed("lz4-hadoop", (byte[])[4, 11, 0, 0, 0, 0, 0, 11, 0, 0, 0, 10, 0x31, .. "abc"u8, 3, 0, 0x30, .. "xyz"u8]);
        yield return new FuzzSeed("lz4-raw", (byte[])[6, 11, 0, 0, 0x31, .. "abc"u8, 3, 0, 0x30, .. "xyz"u8]);
        // The text compressed by the zstd tool (-19 --check): Huffman literals, sequences and the checksum of the frame
        yield return new FuzzSeed("zstd-checksum", (byte[])[5, (byte)text.Length, 0, 0, .. Convert.FromHexString("28b52ffd243f650100a202090ed0e79248cadd944c88293007a25be797e853c98aae33eab954121b7541f169cec21e020100fe8a177724fc37")]);
        // 300 zeros compressed by the zstd tool: an RLE literal and a long match
        yield return new FuzzSeed("zstd-zeros", (byte[])[5, 44, 1, 0, .. Convert.FromHexString("28b52ffd602c004d00001000000100272ac002")]);
#if NET11_0_OR_GREATER
        yield return new FuzzSeed("zstd", (byte[])[5, (byte)text.Length, 0, 0, .. Compress(text, stream => new ZstandardStream(stream, CompressionLevel.Optimal))]);
#endif

        static byte[] Compress(byte[] data, Func<Stream, Stream> create)
        {
            using var output = new MemoryStream();
            using (var compressor = create(output))
            {
                compressor.Write(data);
            }

            return output.ToArray();
        }
    }

    private static IEnumerable<FuzzSeed> GetParquetEncodingsSeeds()
    {
        // An RLE run of 5 then 8 bit-packed values, BIT_PACKED, and DELTA_BINARY_PACKED: block of 128, 4 miniblocks, 5 values
        yield return new FuzzSeed("rle-hybrid", (byte[])[0, 3, 13, 0, 0x0A, 5, 0x03, 0x88, 0xC6, 0xFA]);
        yield return new FuzzSeed("bit-packed", (byte[])[1, 3, 8, 0, 0x05, 0x39, 0x77]);
        yield return new FuzzSeed("delta", (byte[])[2, 0, 5, 0, 0x80, 0x01, 0x04, 0x05, 0x02, 0x02, 0x01, 0x00, 0x00, 0x00, 0x0B, 0x00, 0x00, 0x00]);

        // The footers and the first page headers of the test files
        foreach (var (name, file) in GetTestFiles())
        {
            var metadataLength = BitConverter.ToInt32(file, file.Length - 8);
            if (metadataLength > 0 && metadataLength < file.Length - 8)
            {
                yield return new FuzzSeed(name + "-footer", (byte[])[3, 0, 0, 0, .. file.AsSpan(file.Length - 8 - metadataLength, metadataLength)]);
            }

            yield return new FuzzSeed(name + "-page", (byte[])[4, 0, 0, 0, .. file.AsSpan(4, Math.Min(256, file.Length - 4))]);
        }
    }

    private static IEnumerable<(string Name, byte[] Data)> GetTestFiles()
    {
        var assembly = typeof(GeoParquetFuzzTargets).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.StartsWith(TestDataPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, resourceName);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return (resourceName[TestDataPrefix.Length..].Replace('\\', '-').Replace('/', '-'), memory.ToArray());
        }
    }
}
