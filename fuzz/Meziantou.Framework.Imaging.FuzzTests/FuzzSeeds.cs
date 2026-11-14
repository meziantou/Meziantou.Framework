using System.Buffers.Binary;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Hevc = Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>Seed groups of the fuzz tests: every corpus input (valid, invalid, unsupported and limit fixtures) of a codec variant, and the generated seeds of the formats the corpus has no fixture of (<see cref="FuzzGeneratedSeeds"/>).</summary>
internal static class FuzzSeeds
{
    public static IReadOnlyList<string> Groups { get; } = ["png", "apng", "gif", "jpeg-baseline", "jpeg-progressive", "jpeg-lossless", "webp-lossless", "webp-lossy", "webp-animated", "qoi", "bmp", "tga", "pnm", "avif-still", "avif-sequence", "av1-intra", "av1-inter", "heic-still", "heic-sequence", "hevc-intra", "hevc-inter", "jxl-lossless", "jxl-lossy", "jxl-animated", "jp2-reversible", "jp2-irreversible", "jp2-file", "jp2-ht", .. FuzzGeneratedSeeds.Groups];

    public static IReadOnlyList<FuzzSeed> Get(string group)
    {
        // The AV1 vectors of the corpus are raw streams: each one is wrapped into an AVIF file, a still image or an image
        // sequence, so that it goes through the public API
        if (group is "hevc-intra" or "hevc-inter")
            return GetHevcVectors(group);

        // The corpus has no fixture of the multi-entry formats and of the animated cursors: their seeds are generated. The
        // corpus fixtures of a generated group's format are added too (the OpenEXR files written by OpenEXR: PXR24, B44,
        // B44A, compressed tiles, mipmaps and ripmaps, which the encoder of the library never writes)
        if (FuzzGeneratedSeeds.Groups.Contains(group, StringComparer.Ordinal))
        {
            var generated = FuzzGeneratedSeeds.Get(group);
            generated.AddRange(GoldenCorpus.Default.Fixtures
                .Where(fixture => fixture.Entry.Format == group)
                .Select(fixture => new FuzzSeed(fixture.Id, fixture.ReadInput())));
            return generated;
        }

        var seeds = group is "av1-intra" or "av1-inter"
            ? GoldenCorpus.Default.Av1Vectors
                .Where(vector => (vector.HasFeature("av1.frame_types=inter") ? "av1-inter" : "av1-intra") == group)
                .Select(vector => (vector.Id, Stream: new FuzzAv1Stream(File.ReadAllBytes(GoldenCorpus.Default.RootDirectory / vector.Input.Path), vector.Frames[0].Width, vector.Frames[0].Height, vector.BitDepth, vector.Subsampling)))
                .Select(item => new FuzzSeed(item.Id, FuzzAvifContainer.Wrap(item.Stream.Data, item.Stream.Width, item.Stream.Height, item.Stream.BitDepth, item.Stream.Subsampling), Av1Stream: item.Stream))
                .ToList()
            : GoldenCorpus.Default.Fixtures
                .Select(fixture => (Fixture: fixture, Data: fixture.ReadInput()))
                .Where(item => GetGroup(item.Fixture.Id, item.Fixture.Entry.Format, item.Data) == group)
                .Select(item => new FuzzSeed(item.Fixture.Id, item.Data))
                .ToList();
        // The HEIC files of the unit tests, written by libheif, and the JPEG XL files of the samples the corpus has no fixture of:
        // the groups also take the fixtures of the format of the corpus
        if (group is "heic-still")
        {
            seeds.AddRange(
            [
                new FuzzSeed("heic/embedded-rgb", Tests.HeicTestFiles.Rgb),
                new FuzzSeed("heic/embedded-rgba", Tests.HeicTestFiles.Rgba),
                new FuzzSeed("heic/embedded-gray", Tests.HeicTestFiles.Gray),
                new FuzzSeed("heic/embedded-deep", Tests.HeicTestFiles.Deep),
                new FuzzSeed("heic/embedded-grid", Tests.HeicTestFiles.Grid),
            ]);
        }
        else if (group is "jxl-lossless")
        {
            seeds.AddRange(
            [
                new FuzzSeed("jxl/embedded-float32", Tests.JpegXlTestFiles.Float32),
                new FuzzSeed("jxl/embedded-half-float", Tests.JpegXlTestFiles.HalfFloat),
                new FuzzSeed("jxl/embedded-integer-24-bit", Tests.JpegXlTestFiles.Integer24Bit),
                new FuzzSeed("jxl/embedded-integer-25-bit", Tests.JpegXlTestFiles.Integer25Bit),
                new FuzzSeed("jxl/embedded-cmyk-alpha", Tests.JpegXlTestFiles.CmykWithAlpha),
                new FuzzSeed("jxl/embedded-many-extra-channels", Tests.JpegXlTestFiles.ManyExtraChannels),
            ]);
        }
        else if (group is "jxl-lossy")
        {
            seeds.AddRange(FuzzGeneratedSeeds.GetSmoothedRecompressedJpegSeeds());
        }
        else if (group is "gif")
        {
            seeds.Add(CreateGifWithXmpAndIcc(seeds.First(seed => seed.Id.StartsWith("gif/", StringComparison.Ordinal)).Data));
        }
        else if (group is "bmp")
        {
            seeds.Add(new FuzzSeed("bmp/generated-2-bit", CreateTwoBitBmp()));
            seeds.Add(new FuzzSeed("bmp/generated-10-10-10-2", CreateWideBitFieldsBmp()));
        }
        else if (group is "pnm")
        {
            // A concatenation of images of several variants, the pages of a collection, with white space between two of them
            var images = seeds.Where(seed => seed.Id.StartsWith("pnm/", StringComparison.Ordinal)).Take(3).Select(seed => seed.Data).ToArray();
            seeds.Add(new FuzzSeed("pnm/generated-concatenation", [.. images[0], .. images[1], .. "\n\n"u8, .. images[2]]));
        }
        else if (group is "heic-sequence")
        {
            seeds.AddRange(
            [
                new FuzzSeed("heic/embedded-sequence", Tests.HeicTestFiles.Sequence),
                new FuzzSeed("heic/embedded-reordered-sequence", Tests.HeicTestFiles.ReorderedSequence),
            ]);
        }

        if (seeds.Count == 0)
            throw new InvalidOperationException($"The fuzz seed group '{group}' has no corpus input.");

        return seeds;
    }

    /// <summary>
    /// A GIF of the corpus with an ICC application extension (<c>ICCRGBG1012</c>, an RGB profile in 255-byte sub-blocks) and
    /// an XMP one (<c>XMP DataXMP</c>, the packet stored as it is and its 257-byte magic trailer) before its first block.
    /// </summary>
    private static FuzzSeed CreateGifWithXmpAndIcc(byte[] gif)
    {
        var globalTable = (gif[10] & 0x80) != 0 ? 3 * (2 << (gif[10] & 0x07)) : 0;
        var position = 13 + globalTable;
        var profile = Tests.IccTestProfiles.Rgb(Tests.IccTestProfiles.SrgbColorants, Tests.IccTestProfiles.SrgbCurve()).Data.ToArray();
        var extensions = new List<byte>([0x21, 0xFF, 11, .. "ICCRGBG1012"u8]);
        foreach (var chunk in profile.Chunk(255))
        {
            extensions.Add((byte)chunk.Length);
            extensions.AddRange(chunk);
        }

        extensions.Add(0);
        extensions.AddRange([0x21, 0xFF, 11, .. "XMP DataXMP"u8, .. "<?xpacket begin='' id='W5M0MpCehiHzreSzNTczkc9d'?><x:xmpmeta xmlns:x='adobe:ns:meta/'/><?xpacket end='w'?>"u8, 0x01]);
        for (var value = 255; value >= 0; value--)
        {
            extensions.Add((byte)value);
        }

        extensions.Add(0);
        return new FuzzSeed("gif/generated-xmp-and-icc", [.. gif.AsSpan(0, position), .. extensions, .. gif.AsSpan(position)]);
    }

    /// <summary>A 3x2 BMP of 32-bit pixels whose bit fields are 10-10-10-2 (a <c>BITMAPV4HEADER</c> with <c>BI_BITFIELDS</c>).</summary>
    private static byte[] CreateWideBitFieldsBmp()
    {
        const int HeaderLength = 108;
        var offset = 14 + HeaderLength;
        var data = new byte[offset + (3 * 2 * 4)];
        "BM"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), offset);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), HeaderLength);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), 3);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), 2);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28), 32);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(30), 3);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(34), 3 * 2 * 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(54), 0x3FF00000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(58), 0x000FFC00);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(62), 0x000003FF);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(66), 0xC0000000);
        for (var i = 0; i < 6; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + (i * 4)), (uint)((i * 0x2F1C3A5) ^ 0x9E3779B9));
        }

        return data;
    }

    /// <summary>A 5x2 BMP of 2 bits per pixel (Windows CE) with a palette of four entries.</summary>
    private static byte[] CreateTwoBitBmp()
    {
        byte[] palette = [30, 20, 10, 0, 100, 150, 200, 0, 0, 0, 0, 0, 255, 255, 255, 0];
        byte[] rows = [0b10_11_00_01, 0b11_000000, 0, 0, 0b00_01_10_11, 0b01_000000, 0, 0];
        var offset = 14 + 40 + palette.Length;
        var data = new byte[offset + rows.Length];
        "BM"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), offset);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), 5);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), 2);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28), 2);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(34), rows.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(46), 4);
        palette.CopyTo(data, 54);
        rows.CopyTo(data, offset);
        return data;
    }

    // The HEVC vectors of the corpus are raw byte streams: each one is wrapped into a HEIC file, a still image or an image
    // sequence, so that it goes through the public API. The vectors that the decoder does not support are left out.
    private static List<FuzzSeed> GetHevcVectors(string group)
    {
        var seeds = new List<FuzzSeed>();
        foreach (var vector in GoldenCorpus.Default.HevcVectors)
        {
            if (vector.HasFeature("hevc.unsupported") || (vector.HasFeature("hevc.frameTypes=inter") ? "hevc-inter" : "hevc-intra") != group)
                continue;

            if (FuzzSettings.SeedFilter is { } filter && !vector.Id.Contains(filter, StringComparison.Ordinal))
                continue;

            var stream = File.ReadAllBytes(GoldenCorpus.Default.RootDirectory / vector.Input.Path);
            if (FuzzHeicContainer.Wrap(stream) is { } data)
            {
                seeds.Add(new FuzzSeed(vector.Id, data, stream));

                // Some of the small vectors are also the alpha plane of themselves: an auxiliary item, or an auxiliary track
                // with as many samples as the track of the color
                if (seeds.Count % 16 is 0 && stream.Length < 4096 && FuzzHeicContainer.Wrap(Hevc.ReferenceHevcStream.SplitAnnexB(stream), new FuzzHeicContainer.Options(AlphaUnits: Hevc.ReferenceHevcStream.SplitAnnexB(stream))) is { } withAlpha)
                {
                    seeds.Add(new FuzzSeed(vector.Id + " with alpha", withAlpha, stream));
                }
            }
        }

        if (seeds.Count == 0)
            throw new InvalidOperationException($"The fuzz seed group '{group}' has no corpus input.");

        return seeds;
    }

    /// <summary>A stable per-group salt (FNV-1a), so that each group explores its own mutation sequence.</summary>
    public static ulong GetGroupSalt(string group)
    {
        var hash = 0xCBF29CE484222325UL;
        foreach (var c in group)
        {
            hash = (hash ^ c) * 0x100000001B3UL;
        }

        return hash;
    }

    private static string GetGroup(string id, string format, byte[] data) => format switch
    {
        "avif" => FuzzAvifContainer.IsSequence(data) ? "avif-sequence" : "avif-still",
        "heic" => FuzzHeicContainer.IsSequence(data) ? "heic-sequence" : "heic-still",
        "png" => id.Contains("apng", StringComparison.Ordinal) ? "apng" : "png",
        "gif" => "gif",
        "jpeg" => id.Contains("lossless", StringComparison.Ordinal) ? "jpeg-lossless" : id.Contains("progressive", StringComparison.Ordinal) ? "jpeg-progressive" : "jpeg-baseline",
        "webp" => id.Contains("anim", StringComparison.Ordinal) ? "webp-animated" : id.Contains("lossy", StringComparison.Ordinal) ? "webp-lossy" : "webp-lossless",
        "jxl" => id.Contains("anim", StringComparison.Ordinal) ? "jxl-animated" : id.Contains("lossy", StringComparison.Ordinal) || id.Contains("jpeg", StringComparison.Ordinal) ? "jxl-lossy" : "jxl-lossless",
        // JPEG 2000: the high-throughput block coder, the boxes of a file, then the two wavelet transformations
        "jp2" => id.Contains("/ht-", StringComparison.Ordinal) ? "jp2-ht" : id.Contains("/file-", StringComparison.Ordinal) ? "jp2-file" : id.Contains("lossy", StringComparison.Ordinal) ? "jp2-irreversible" : "jp2-reversible",
        _ => format,
    };
}
