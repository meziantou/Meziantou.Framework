using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using static Meziantou.Framework.Imaging.CorpusGenerator.Common.HandAssembledCorpus;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the TGA fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/tga, invalid/tga and limit/tga entries).
/// <para>
/// This generator is the reviewed, reproducible recipe of every committed TGA fixture. It is NEVER run by the tests and
/// does not use the library under test. The shared driver and helpers are in HandAssembledCorpus.
/// </para>
/// <para>
/// Expected pixels are hand-defined:
/// - every input is assembled byte by byte by the writers below, next to the literal pixels it must produce;
/// - a few inputs are produced by FFmpeg's independent TGA encoder, from hand-defined patterns: the format is lossless,
///   so the pattern is the reference.
/// </para>
/// <para>
/// Every input is then decoded by the strict transcription of the specification in this generator (headers, color maps and run-length packets
/// are all validated) and by FFmpeg/libavcodec, and the decodings must agree.
/// The only recorded disagreement is FFmpeg's 5-bit-to-8-bit expansion of 16-bit samples, which replicates bits instead of
/// rounding the exact ratio (at most one unit per sample); it is recorded as a crossCheck with its measured error, never hidden.
/// </para>
/// <para>
/// Malformed, unsupported and over-limit inputs are byte edits of valid inputs; each defect is confirmed by the strict
/// transcription.
/// </para>
/// <para>
/// It complements GoldenCorpus: this generator only replaces the manifest entries whose format is tga and their files, and
/// keeps every other entry and file unchanged.
/// </para>
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- tga          # check (temporary directory and diff)
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- tga --write  # explicit regeneration (review the diff!)
/// Requirements: FFmpeg at the pinned version (FfmpegTools; override with --accept-tool-versions after reviewing the differences).
/// Any OS.
/// </summary>
internal static class TgaCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/TgaCorpus.cs";

    private static readonly HandAssembledFormat Format = new("tga", "Tga", TgaRead);

    public static int Run(GeneratorOptions options) => HandAssembledCorpus.Run(options, Format, ScriptPath, c =>
    {
        BuildTga(c);
        BuildErrors(c);
        BuildLimits(c);
    });

    // ---------------------------------------------------------------------------------------------------------------------
    // Truevision TGA: the TGA File Format Specification 2.0, transcribed as a strict reader and as writers
    // ---------------------------------------------------------------------------------------------------------------------

    private static readonly byte[] TgaFooterSignature = Bytes.Ascii("TRUEVISION-XFILE.\0");
    private const int TgaExtensionLength = 495;

    /// <summary>colorMap is (first_entry, length, entry_bits) or None.</summary>
    private static byte[] TgaHeader(int imageType, int width, int height, int pixelDepth, int descriptor = 0, int idLength = 0, (int First, int Length, int EntryBits)? colorMap = null)
    {
        var hasMap = colorMap is not null ? 1 : 0;
        var (first, length, entryBits) = colorMap ?? (0, 0, 0);
        return new ByteBuilder().U8(idLength).U8(hasMap).U8(imageType).U16LE(first).U16LE(length).U8(entryBits).U16LE(0).U16LE(0)
            .U16LE(width).U16LE(height).U8(pixelDepth).U8(descriptor).ToArray();
    }

    private static byte[] TgaFooter(long extensionOffset = 0, long developerOffset = 0) =>
        new ByteBuilder().U32LE(extensionOffset).U32LE(developerOffset).Bytes(TgaFooterSignature).ToArray();

    private static byte[] TgaExtensionArea(int attributesType)
    {
        var area = new byte[TgaExtensionLength];
        area[0] = TgaExtensionLength & 0xFF;
        area[1] = TgaExtensionLength >> 8;
        area[494] = (byte)attributesType;
        return area;
    }

    /// <summary>Run-length encodes one scan line; packets never cross the scan line (TGA 2.0 recommendation).</summary>
    private static byte[] TgaRle(List<byte[]> pixels, int bytesPerPixel)
    {
        _ = bytesPerPixel;
        var output = new ByteBuilder();
        var x = 0;
        var count = pixels.Count;
        while (x < count)
        {
            var run = 1;
            while (x + run < count && run < 128 && pixels[x + run].AsSpan().SequenceEqual(pixels[x]))
                run++;
            if (run >= 2)
            {
                output.U8(0x80 | (run - 1));
                output.Bytes(pixels[x]);
                x += run;
                continue;
            }

            var raw = 1;
            while (x + raw < count && raw < 128)
            {
                if (x + raw + 1 < count && pixels[x + raw].AsSpan().SequenceEqual(pixels[x + raw + 1]))
                    break;
                raw++;
            }

            output.U8(raw - 1);
            for (var i = 0; i < raw; i++)
                output.Bytes(pixels[x + i]);
            x += raw;
        }

        return output.ToArray();
    }

    /// <summary>Strict transcription of the supported TGA subset: returns (Img rgba8, features).</summary>
    private static (Img Image, List<string> Features) TgaRead(byte[] data)
    {
        if (data.Length < 18)
            throw new FormatError("truncated header");
        int idLength = data[0];
        int colorMapType = data[1];
        int imageType = data[2];
        int mapFirst = Bytes.U16LE(data, 3);
        int mapLength = Bytes.U16LE(data, 5);
        int mapEntryBits = data[7];
        int width = Bytes.U16LE(data, 12);
        int height = Bytes.U16LE(data, 14);
        int pixelDepth = data[16];
        int descriptor = data[17];
        if (colorMapType > 1 || width == 0 || height == 0 || (descriptor & 0xC0) != 0)
            throw new FormatError("implausible header");
        if (imageType is not (1 or 2 or 3 or 9 or 10 or 11))
            throw new FormatError(Inv($"image type {imageType}"));
        var colorMapped = imageType is 1 or 9;
        if (colorMapped != (colorMapType == 1))
            throw new FormatError(Inv($"color map type {colorMapType} for image type {imageType}"));
        if (colorMapped)
        {
            if (mapLength == 0 || mapEntryBits is not (15 or 16 or 24 or 32))
                throw new FormatError("color map");
            if (pixelDepth != 8)
                throw new FormatError(Inv($"unsupported {pixelDepth}-bit color-map indexes"));
        }
        else if (mapFirst != 0 || mapLength != 0 || mapEntryBits != 0)
        {
            throw new FormatError("color map without a color-mapped image type");
        }

        var grayscale = imageType is 3 or 11;
        if (grayscale && pixelDepth != 8)
            throw new FormatError(Inv($"unsupported {pixelDepth}-bit grayscale"));
        if (!colorMapped && !grayscale && pixelDepth is not (15 or 16 or 24 or 32))
            throw new FormatError(Inv($"pixel depth {pixelDepth}"));
        var alphaBits = descriptor & 0x0F;
        var sampleBits = colorMapped ? mapEntryBits : pixelDepth;
        int[] legalAlpha = sampleBits switch { 32 => [0, 8], 16 => [0, 1], _ => [0] };
        if (!legalAlpha.Contains(alphaBits))
            throw new FormatError(Inv($"{alphaBits} alpha bits for {sampleBits}-bit samples"));

        var offset = 18 + idLength;
        if (data.Length < offset)
            throw new FormatError("truncated image identification field");

        static Px DecodeSample(byte[] raw, int bits, bool withAlpha)
        {
            if (bits is 15 or 16)
            {
                var value = raw[0] | (raw[1] << 8);
                var alpha = !withAlpha ? 255 : ((value & 0x8000) != 0 ? 255 : 0);
                return new Px(ScaleChannel((value >> 10) & 31, 5), ScaleChannel((value >> 5) & 31, 5), ScaleChannel(value & 31, 5), alpha);
            }

            if (bits == 24)
                return new Px(raw[2], raw[1], raw[0], 255);
            return new Px(raw[2], raw[1], raw[0], withAlpha ? raw[3] : 255);
        }

        var palette = new Dictionary<int, Px>();
        if (colorMapped)
        {
            var entryBytes = (mapEntryBits + 7) / 8;
            if (data.Length < offset + mapLength * entryBytes)
                throw new FormatError("truncated color map");
            for (var i = 0; i < mapLength; i++)
            {
                var raw = Bytes.Slice(data, offset + i * entryBytes, offset + (i + 1) * entryBytes);
                palette[mapFirst + i] = DecodeSample(raw, mapEntryBits, alphaBits > 0);
            }

            offset += mapLength * entryBytes;
        }

        var bytesPerPixel = (pixelDepth + 7) / 8;
        var total = width * height;
        var stored = new List<byte[]>();
        var packetStats = new HashSet<string>(StringComparer.Ordinal);
        if (imageType is 9 or 10 or 11)
        {
            while (stored.Count < total)
            {
                if (offset >= data.Length)
                    throw new FormatError("truncated packet stream");
                int packet = data[offset];
                offset++;
                var count = (packet & 0x7F) + 1;
                if (count > total - stored.Count)
                    throw new FormatError(Inv($"packet of {count} pixels past the last pixel"));
                var start = stored.Count;
                packetStats.Add("tga.rle." + ((packet & 0x80) != 0 ? "run" : "raw"));
                if (count == 128)
                    packetStats.Add("tga.rle.packet=128");
                if (start / width != (start + count - 1) / width)
                    packetStats.Add("tga.rle.crossesRow");
                if (start % width == 0 && count % width == 0)
                    packetStats.Add("tga.rle.alignedToRow");
                if ((packet & 0x80) != 0)
                {
                    if (data.Length < offset + bytesPerPixel)
                        throw new FormatError("truncated run-length packet");
                    var pixel = Bytes.Slice(data, offset, offset + bytesPerPixel);
                    for (var i = 0; i < count; i++)
                        stored.Add(pixel);
                    offset += bytesPerPixel;
                }
                else
                {
                    if (data.Length < offset + count * bytesPerPixel)
                        throw new FormatError("truncated raw packet");
                    for (var i = 0; i < count; i++)
                        stored.Add(Bytes.Slice(data, offset + i * bytesPerPixel, offset + (i + 1) * bytesPerPixel));
                    offset += count * bytesPerPixel;
                }
            }
        }
        else
        {
            if (data.Length < offset + total * bytesPerPixel)
                throw new FormatError("truncated pixel data");
            for (var i = 0; i < total; i++)
                stored.Add(Bytes.Slice(data, offset + i * bytesPerPixel, offset + (i + 1) * bytesPerPixel));
            offset += total * bytesPerPixel;
        }

        var pixelDataEnd = offset;

        // The TGA 2.0 trailer: the footer is the last 26 bytes of the file and holds the offsets of the extension area and of
        // the developer directory, both counted from the start of the file. The extension area lies between the image data
        // and the footer, holds at least the 495 bytes of TGA 2.0, and ends with the attributes type, which says what the
        // alpha data is: 4 is premultiplied alpha, which a reader of straight alpha must not take for straight alpha.
        int? attributesType = null;
        var hasDeveloperArea = false;
        var trailer = Bytes.Slice(data, pixelDataEnd);
        var hasFooter = trailer.Length >= 26 && Bytes.Slice(trailer, -18).AsSpan().SequenceEqual(TgaFooterSignature);
        if (hasFooter)
        {
            var footerOffset = data.Length - 26;
            var extensionOffset = (long)Bytes.U32LE(data, footerOffset);
            hasDeveloperArea = Bytes.U32LE(data, footerOffset + 4) != 0;
            if (extensionOffset != 0)
            {
                if (extensionOffset < pixelDataEnd || extensionOffset + TgaExtensionLength > footerOffset)
                    throw new FormatError(Inv($"extension area at {extensionOffset} outside the bytes between the image data ({pixelDataEnd}) and the footer ({footerOffset})"));
                var extensionSize = Bytes.U16LE(data, (int)extensionOffset);
                if (extensionSize < TgaExtensionLength)
                    throw new FormatError(Inv($"extension area of {extensionSize} bytes"));
                attributesType = data[(int)extensionOffset + 494];
                if (attributesType == 4 && alphaBits > 0)
                    throw new FormatError("premultiplied alpha (attributes type 4)");
            }
        }

        var rows = new List<List<Px>>();
        for (var y = 0; y < height; y++)
        {
            var row = new List<Px>();
            for (var x = 0; x < width; x++)
            {
                var raw = stored[y * width + x];
                if (colorMapped)
                {
                    int index = raw[0];
                    if (!palette.TryGetValue(index, out var entry))
                        throw new FormatError(Inv($"color-map index {index} outside the stored map"));
                    row.Add(entry);
                }
                else if (grayscale)
                {
                    row.Add(new Px(raw[0], raw[0], raw[0], 255));
                }
                else
                {
                    row.Add(DecodeSample(raw, pixelDepth, alphaBits > 0));
                }
            }

            if ((descriptor & 0x10) != 0)
                row.Reverse();
            rows.Add(row);
        }

        if ((descriptor & 0x20) == 0)
            rows.Reverse();

        var features = new List<string>
        {
            Inv($"tga.imageType={imageType}"),
            Inv($"tga.depth={pixelDepth}"),
            "tga.rowOrder=" + ((descriptor & 0x20) != 0 ? "top-down" : "bottom-up"),
            "tga.columnOrder=" + ((descriptor & 0x10) != 0 ? "right-to-left" : "left-to-right"),
            Inv($"tga.alphaBits={alphaBits}"),
        };
        features.AddRange(packetStats.Order(StringComparer.Ordinal));
        if (colorMapped)
        {
            features.Add(Inv($"tga.colorMap={mapFirst}/{mapLength}/{mapEntryBits}"));
            if (mapFirst != 0)
                features.Add("tga.colorMap.offset");
        }

        if (idLength != 0)
            features.Add("tga.idField");
        if (trailer.Length > 0)
            features.Add("tga.trailingData");
        if (hasFooter)
            features.Add("tga.footer");
        if (attributesType is not null)
            features.Add(Inv($"tga.attributesType={attributesType}"));
        if (hasDeveloperArea)
            features.Add("tga.developerArea");
        if (width % 2 == 1)
            features.Add("tga.width=odd");
        return (RgbaImage(width, height, rows.SelectMany(r => r)), SortedSet(features));
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // TGA fixtures
    // ---------------------------------------------------------------------------------------------------------------------

    private static readonly Px[] TgaRgb5X3 =
    [
        new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), new(16, 32, 48), new(240, 224, 208),
        new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(250, 251, 252), new(128, 128, 128),
        new(11, 22, 33), new(44, 55, 66), new(77, 88, 99), new(200, 100, 50), new(0, 0, 0),
    ];

    private static readonly Px[] TgaRgba5X3 = [.. TgaRgb5X3.Zip([255, 128, 0, 1, 254, 64, 255, 0, 192, 32, 255, 7, 250, 128, 0], (p, a) => p.Append(a))];

    private static List<byte[]> TgaPixels24(IEnumerable<Px> pixels) => [.. pixels.Select(p => new byte[] { (byte)p[2], (byte)p[1], (byte)p[0] })];

    private static List<byte[]> TgaPixels32(IEnumerable<Px> pixels) => [.. pixels.Select(p => new byte[] { (byte)p[2], (byte)p[1], (byte)p[0], (byte)p[3] })];

    private static List<byte[]> TgaPixels16(IEnumerable<Px> pixels, bool withAlpha)
    {
        var output = new List<byte[]>();
        foreach (var p in pixels)
        {
            var value = (Reduce(p[0], 31) << 10) | (Reduce(p[1], 31) << 5) | Reduce(p[2], 31);
            if (withAlpha && p[3] != 0)
                value |= 0x8000;
            output.Add(new ByteBuilder().U16LE(value).ToArray());
        }

        return output;
    }

    /// <summary>The order the pixels are stored in, from the displayed top-down rows.</summary>
    private static List<T> TgaStoreOrder<T>(IReadOnlyList<T> pixels, int width, int height, bool topDown, bool rightToLeft)
    {
        var rows = Enumerable.Range(0, height).Select(y => pixels.Skip(y * width).Take(width).ToList()).ToList();
        if (rightToLeft)
            rows = [.. rows.Select(row => Enumerable.Reverse(row).ToList())];
        if (!topDown)
            rows.Reverse();
        return [.. rows.SelectMany(row => row)];
    }

    private static List<Px> TgaExpand16(IEnumerable<Px> pixels, bool withAlpha)
    {
        var output = new List<Px>();
        foreach (var p in pixels)
        {
            var sample = p.Slice(0, 3).Select(v => ScaleChannel(Reduce(v, 31), 5));
            output.Add(sample.Append(withAlpha ? (p[3] != 0 ? 255 : 0) : 255));
        }

        return output;
    }

    /// <summary>Bands of identical pixels separated by single different ones: run-length encoders produce both packet kinds.</summary>
    private static Img PatternRunBands(int w, int h)
    {
        var pixels = new List<Px>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (x % 5 == 4)
                    pixels.Add(new Px((x * 29 + 3) % 256, (y * 53 + 7) % 256, (x * y + 11) % 256));
                else
                    pixels.Add(new Px((y * 23 + 5) % 256, (y * 97 + 13) % 256, (y * 41 + 211) % 256));
            }
        }

        return new Img(w, h, "rgb", 8, pixels);
    }

    private static void BuildTga(Corpus<FfmpegTools> c)
    {
        var rgbImg = new Img(5, 3, "rgb", 8, TgaRgb5X3);
        var rgbaImg = new Img(5, 3, "rgba", 8, TgaRgba5X3);

        var data = Bytes.Concat([TgaHeader(2, 5, 3, 24), .. TgaPixels24(TgaStoreOrder(TgaRgb5X3, 5, 3, false, false))]);
        AddFixture(c, Format, "tga/truecolor24-bottom-left", "tga/truecolor24-bottom-left.tga", data, rgbImg, "Rgb24", "Rgb", 8,
            "BuildTga", new Obj { ["width"] = 5, ["height"] = 3, ["imageType"] = 2 },
            "Assembled byte by byte: uncompressed true-color, 24-bit BGR samples, origin at the bottom left (image " +
            "descriptor 0), no image identification field, no color map and no footer (a TGA 1.0 file).",
            required: ["tga.imageType=2", "tga.depth=24", "tga.rowOrder=bottom-up", "tga.columnOrder=left-to-right", "tga.width=odd"]);

        data = Bytes.Concat([TgaHeader(2, 5, 3, 24, descriptor: 0x20), .. TgaPixels24(TgaStoreOrder(TgaRgb5X3, 5, 3, true, false))]);
        AddFixture(c, Format, "tga/truecolor24-top-left", "tga/truecolor24-top-left.tga", data, rgbImg, "Rgb24", "Rgb", 8,
            "BuildTga", new Obj { ["width"] = 5, ["height"] = 3, ["descriptor"] = 0x20 },
            "The same pixels with the origin at the top left (image-descriptor bit 5): decoding both fixtures to the same " +
            "reference proves the row order is honored.",
            required: ["tga.rowOrder=top-down"]);

        data = Bytes.Concat([TgaHeader(2, 5, 3, 32, descriptor: 0x08), .. TgaPixels32(TgaStoreOrder(TgaRgba5X3, 5, 3, false, false))]);
        AddFixture(c, Format, "tga/truecolor32-alpha", "tga/truecolor32-alpha.tga", data, rgbaImg, "Rgba32", "Rgba", 8,
            "BuildTga", new Obj { ["width"] = 5, ["height"] = 3, ["alphaBits"] = 8 },
            "32-bit BGRA with eight declared alpha bits: real transparency, including fully transparent pixels whose " +
            "colors stay defined.",
            required: ["tga.depth=32", "tga.alphaBits=8"]);

        data = Bytes.Concat([TgaHeader(2, 5, 3, 32, descriptor: 0x18), .. TgaPixels32(TgaStoreOrder(TgaRgba5X3, 5, 3, false, true))]);
        AddFixture(c, Format, "tga/truecolor32-right-to-left", "tga/truecolor32-right-to-left.tga", data, rgbaImg, "Rgba32", "Rgba", 8,
            "BuildTga", new Obj { ["width"] = 5, ["height"] = 3, ["descriptor"] = 0x18 },
            "The same pixels stored right to left (image-descriptor bit 4) and bottom-up: both origin bits are resolved " +
            "to the same displayed image.",
            required: ["tga.columnOrder=right-to-left"]);

        data = Bytes.Concat([TgaHeader(2, 5, 3, 32, descriptor: 0x00), .. TgaPixels32(TgaStoreOrder(TgaRgba5X3, 5, 3, false, false))]);
        AddFixture(c, Format, "tga/truecolor32-unspecified-alpha", "tga/truecolor32-unspecified-alpha.tga", data, rgbImg, "Rgb24", "Rgb", 8,
            "BuildTga", new Obj { ["width"] = 5, ["height"] = 3, ["alphaBits"] = 0 },
            "The same 32-bit payload with zero declared alpha bits: the fourth byte is unspecified and discarded, so the " +
            "image is the opaque RGB samples. A 32-bit payload never implies transparency.",
            required: ["tga.depth=32", "tga.alphaBits=0"],
            crossCheck: "differs",
            crossCheckNotes: "FFmpeg reads the fourth byte of a 32-bit TGA as alpha whatever the image descriptor " +
                             "declares; the specification makes it an attribute field whose meaning the descriptor " +
                             "defines, so the reference is opaque. Every color sample agrees exactly (mean color " +
                             "error 0); only the alpha interpretation differs.");

        var expanded = TgaExpand16(TgaRgba5X3, true);
        var img16a = new Img(5, 3, "rgba", 8, expanded);
        data = Bytes.Concat([TgaHeader(2, 5, 3, 16, descriptor: 0x01), .. TgaPixels16(TgaStoreOrder(TgaRgba5X3, 5, 3, false, false), true)]);
        AddFixture(c, Format, "tga/truecolor16-1555-alpha", "tga/truecolor16-1555-alpha.tga", data, img16a, "Rgba32", "Rgba", 8,
            "BuildTga", new Obj { ["width"] = 5, ["height"] = 3, ["pixelDepth"] = 16 },
            "16-bit samples with one declared alpha bit: A1 R5 G5 B5 little-endian. Channels are expanded with the exact " +
            "ratio round(value * 255 / 31) and the alpha bit becomes 0 or 255.",
            required: ["tga.depth=16", "tga.alphaBits=1"],
            crossCheck: "differs",
            crossCheckNotes: "FFmpeg expands 5-bit channels by replicating their high bits instead of rounding the exact " +
                             "ratio, and reads this layout as opaque 5-5-5; the alpha bit and the rounding are recorded here.");

        var img15 = new Img(5, 3, "rgb", 8, TgaExpand16(TgaRgb5X3, false).Select(p => p.Slice(0, 3)));
        data = Bytes.Concat([TgaHeader(2, 5, 3, 15), .. TgaPixels16(TgaStoreOrder([.. TgaRgb5X3.Select(p => p.Append(0))], 5, 3, false, false), false)]);
        AddFixture(c, Format, "tga/truecolor15-opaque", "tga/truecolor15-opaque.tga", data, img15, "Rgb24", "Rgb", 8,
            "BuildTga", new Obj { ["width"] = 5, ["height"] = 3, ["pixelDepth"] = 15 },
            "15-bit samples: the same two bytes per pixel as 16-bit, with the top bit undefined and no declared alpha " +
            "bit, so the image is opaque.",
            required: ["tga.depth=15", "tga.alphaBits=0"],
            crossCheck: "differs",
            crossCheckNotes: "FFmpeg expands 5-bit channels by replicating their high bits instead of rounding the exact " +
                             "ratio round(value * 255 / 31); every sample agrees within one unit.");

        int[] grays = [0, 17, 34, 51, 255, 128, 64, 32, 16, 8, 200, 150, 100, 50, 1];
        var grayImg = new Img(5, 3, "gray", 8, grays.Select(v => new Px(v)));
        data = Bytes.Concat(TgaHeader(3, 5, 3, 8), [.. TgaStoreOrder(grays, 5, 3, false, false).Select(v => (byte)v)]);
        AddFixture(c, Format, "tga/grayscale8", "tga/grayscale8.tga", data, grayImg, "Gray8", "Grayscale", 8,
            "BuildTga", new Obj { ["width"] = 5, ["height"] = 3, ["imageType"] = 3 },
            "Uncompressed 8-bit grayscale (image type 3), decoded to Gray8 without replicating the samples into RGB.",
            required: ["tga.imageType=3", "tga.depth=8"]);

        Px[] palette = [new(0, 0, 0), new(255, 255, 255), new(220, 30, 40), new(30, 220, 40), new(40, 30, 220)];
        int[] indexes = [2, 3, 4, 5, 6, 6, 5, 4, 3, 2, 2, 2, 6, 4, 3]; // the map starts at index 2
        var mapImg = new Img(5, 3, "rgb", 8, indexes.Select(i => palette[i - 2]));
        data = Bytes.Concat(
            TgaHeader(1, 5, 3, 8, colorMap: (2, 5, 24)),
            [.. palette.SelectMany(p => new[] { (byte)p[2], (byte)p[1], (byte)p[0] })],
            [.. TgaStoreOrder(indexes, 5, 3, false, false).Select(v => (byte)v)]);
        AddFixture(c, Format, "tga/colormap8-offset", "tga/colormap8-offset.tga", data, mapImg, "Rgb24", "Indexed", 8,
            "BuildTga", new Obj { ["width"] = 5, ["height"] = 3, ["colorMapFirstEntry"] = 2 },
            "Color-mapped image whose map starts at index 2 (first-entry offset) with five 24-bit BGR entries: pixel " +
            "index i reads the entry stored at position i - 2.",
            required: ["tga.imageType=1", "tga.colorMap.offset"]);

        Px[] paletteRgba = [new(0, 0, 0, 255), new(255, 255, 255, 0), new(220, 30, 40, 128), new(30, 220, 40, 255), new(40, 30, 220, 7)];
        int[] indexes0 = [0, 1, 2, 3, 4, 4, 3, 2, 1, 0, 2, 2, 4, 1, 3];
        var mapRgbaImg = new Img(5, 3, "rgba", 8, indexes0.Select(i => paletteRgba[i]));
        data = Bytes.Concat(
            TgaHeader(1, 5, 3, 8, descriptor: 0x08, colorMap: (0, 5, 32)),
            [.. paletteRgba.SelectMany(p => new[] { (byte)p[2], (byte)p[1], (byte)p[0], (byte)p[3] })],
            [.. TgaStoreOrder(indexes0, 5, 3, false, false).Select(v => (byte)v)]);
        AddFixture(c, Format, "tga/colormap8-rgba-entries", "tga/colormap8-rgba-entries.tga", data, mapRgbaImg, "Rgba32", "Indexed", 8,
            "BuildTga", new Obj { ["width"] = 5, ["height"] = 3, ["colorMapEntryBits"] = 32 },
            "Color-mapped image with 32-bit BGRA map entries and eight declared alpha bits: the alpha of the palette is " +
            "real transparency.",
            required: ["tga.imageType=1", "tga.alphaBits=8"]);

        // Run-length packets: a run crossing rows, a 128-pixel packet and raw packets
        List<Px> rlePixels =
        [
            .. Enumerable.Repeat(new Px(10, 20, 30), 7), .. Enumerable.Repeat(new Px(40, 50, 60), 130), new(1, 2, 3), new(4, 5, 6), new(7, 8, 9),
            .. Enumerable.Repeat(new Px(200, 100, 50), 20), new(9, 8, 7), new(6, 5, 4), .. Enumerable.Repeat(new Px(255, 254, 253), 30),
        ];
        Py.Assert(rlePixels.Count == 192);
        var rleImg = new Img(12, 16, "rgb", 8, rlePixels);
        var stored = TgaStoreOrder(rlePixels, 12, 16, true, false);
        // Encoded as one stream over the whole raster, so packets cross scan lines
        var body = TgaRle(TgaPixels24(stored), 3);
        data = Bytes.Concat(TgaHeader(10, 12, 16, 24, descriptor: 0x20), body);
        AddFixture(c, Format, "tga/rle-truecolor24-packets", "tga/rle-truecolor24-packets.tga", data, rleImg, "Rgb24", "Rgb", 8,
            "BuildTga", new Obj { ["width"] = 12, ["height"] = 16, ["imageType"] = 10 },
            "Run-length encoded true color: runs of 7, 130 (split into a 128-pixel packet and a 2-pixel one), 20 and 30 " +
            "identical pixels, raw packets in between, and packets that cross scan lines (the specification only " +
            "recommends against it; decoders must accept them).",
            required: ["tga.imageType=10", "tga.rle.run", "tga.rle.raw", "tga.rle.packet=128", "tga.rle.crossesRow"]);

        int[] rleGrays = [.. Enumerable.Repeat(0, 9), .. Enumerable.Repeat(200, 20), 1, 2, 3, 4, .. Enumerable.Repeat(255, 15)];
        var rleGrayImg = new Img(8, 6, "gray", 8, rleGrays.Select(v => new Px(v)));
        data = Bytes.Concat(TgaHeader(11, 8, 6, 8, descriptor: 0x20), TgaRle([.. rleGrays.Select(v => new[] { (byte)v })], 1));
        AddFixture(c, Format, "tga/rle-grayscale8", "tga/rle-grayscale8.tga", data, rleGrayImg, "Gray8", "Grayscale", 8,
            "BuildTga", new Obj { ["width"] = 8, ["height"] = 6, ["imageType"] = 11 },
            "Run-length encoded 8-bit grayscale (image type 11) with runs crossing scan lines and raw packets.",
            required: ["tga.imageType=11", "tga.rle.run", "tga.rle.raw"]);

        body = Bytes.Concat(TgaPixels32(TgaStoreOrder(TgaRgba5X3, 5, 3, false, false)));
        data = Bytes.Concat(TgaHeader(2, 5, 3, 32, descriptor: 0x08), body, TgaExtensionArea(3), TgaFooter(extensionOffset: 18 + body.Length));
        AddFixture(c, Format, "tga/extension-useful-alpha", "tga/extension-useful-alpha.tga", data, rgbaImg, "Rgba32", "Rgba", 8,
            "BuildTga", new Obj { ["width"] = 5, ["height"] = 3, ["attributesType"] = 3 },
            "A TGA 2.0 file: the 495-byte extension area immediately follows the pixel data and declares attributes type " +
            "3 (useful alpha), and the 26-byte footer points at it. The trailer is validated; the image descriptor stays " +
            "authoritative for the decoded representation.",
            required: ["tga.footer", "tga.attributesType=3"]);

        // A developer area (one field and its directory, in that order) between the image data and the extension area
        var developerField = Bytes.Ascii("Fixture\0");
        var developerOffset = 18 + body.Length;
        var developerDirectory = new ByteBuilder().U16LE(1).U16LE(32768).U32LE(developerOffset).U32LE(developerField.Length).ToArray();
        var extensionOffset = developerOffset + developerField.Length + developerDirectory.Length;
        data = Bytes.Concat(TgaHeader(2, 5, 3, 32, descriptor: 0x08), body, developerField, developerDirectory, TgaExtensionArea(3),
            TgaFooter(extensionOffset, developerOffset + developerField.Length));
        AddFixture(c, Format, "tga/developer-area-extension", "tga/developer-area-extension.tga", data, rgbaImg, "Rgba32", "Rgba", 8,
            "BuildTga", new Obj { ["width"] = 5, ["height"] = 3, ["attributesType"] = 3, ["developerTag"] = 32768 },
            "A TGA 2.0 file whose extension area does not follow the pixel data: a developer area (an 8-byte field and its " +
            "one-tag directory) comes first, and the footer locates both. The extension area is read where the footer " +
            "says; the developer area is application data and is not interpreted.",
            required: ["tga.footer", "tga.developerArea", "tga.attributesType=3"]);

        data = Bytes.Concat([TgaHeader(2, 5, 3, 24, idLength: 11), Bytes.Ascii("Fixture ID\0"),
            .. TgaPixels24(TgaStoreOrder(TgaRgb5X3, 5, 3, false, false)), TgaFooter()]);
        AddFixture(c, Format, "tga/id-field-footer-only", "tga/id-field-footer-only.tga", data, rgbImg, "Rgb24", "Rgb", 8,
            "BuildTga", new Obj { ["width"] = 5, ["height"] = 3, ["idLength"] = 11 },
            "An 11-byte image identification field between the header and the pixel data (skipped), and a TGA 2.0 footer " +
            "with no developer and no extension area.",
            required: ["tga.idField", "tga.footer"]);

        var pattern = CommonCorpus.Named(PatternRunBands(17, 9), "PatternRunBands");
        (data, var command) = FfmpegEncode(c, pattern, "tga-ffmpeg-rle", "tga", ["-pix_fmt", "bgr24"]);
        AddFixture(c, Format, "tga/ffmpeg-rle-truecolor24", "tga/ffmpeg-rle-truecolor24.tga", data, pattern, "Rgb24", "Rgb", 8,
            "BuildTga " + pattern.PatternName, new Obj { ["width"] = 17, ["height"] = 9 },
            "Encoded by FFmpeg's independent TGA encoder (run-length by default) from the hand-defined pattern; TGA is " +
            "lossless, so the pattern is the reference.",
            commands: [command], required: ["tga.imageType=10", "tga.rle.run", "tga.rle.raw"]);

        // FFmpeg's other TGA layouts: run-length grayscale (the green samples of the bands), and 32-bit truecolor with an
        // 8-bit alpha channel (one alpha value per row)
        var gray = CommonCorpus.Named(new Img(15, 7, "gray", 8, PatternRunBands(15, 7).Pixels.Select(p => new Px(p[1]))), "PatternRunBands");
        (data, command) = FfmpegEncode(c, gray, "tga-ffmpeg-rle-gray", "tga", ["-pix_fmt", "gray"]);
        AddFixture(c, Format, "tga/ffmpeg-rle-gray8", "tga/ffmpeg-rle-gray8.tga", data, gray, "Gray8", "Grayscale", 8,
            "BuildTga " + gray.PatternName, new Obj { ["width"] = 15, ["height"] = 7, ["sample"] = "green" },
            "Encoded by FFmpeg's independent TGA encoder (run-length by default) from the hand-defined pattern; TGA is " +
            "lossless, so the pattern is the reference.",
            commands: [command], required: ["tga.imageType=11", "tga.depth=8"]);

        var rgba = CommonCorpus.Named(new Img(13, 7, "rgba", 8, PatternRunBands(13, 7).Pixels.Select((p, i) => new Px(p[0], p[1], p[2], ((i / 13) * 37 + 40) % 256))), "PatternRunBands");
        (data, command) = FfmpegEncode(c, rgba, "tga-ffmpeg-rle-bgra", "tga", ["-pix_fmt", "bgra"]);
        AddFixture(c, Format, "tga/ffmpeg-rle-truecolor32", "tga/ffmpeg-rle-truecolor32.tga", data, rgba, "Rgba32", "Rgba", 8,
            "BuildTga " + rgba.PatternName, new Obj { ["width"] = 13, ["height"] = 7, ["alpha"] = "(row * 37 + 40) % 256" },
            "Encoded by FFmpeg's independent TGA encoder (run-length by default, 8 alpha bits) from the hand-defined " +
            "pattern; TGA is lossless, so the pattern is the reference.",
            commands: [command], required: ["tga.imageType=10", "tga.depth=32", "tga.alphaBits=8"]);
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Malformed, unsupported and over-limit inputs: byte edits of the valid fixtures, each defect confirmed by the
    // transcribed specification above
    // ---------------------------------------------------------------------------------------------------------------------

    private static void BuildErrors(Corpus<FfmpegTools> c)
    {
        var read = ReadValidInputs(c);
        var rle = read["tga/rle-truecolor24-packets"];
        AddError(c, Format, "invalid/tga/packet-past-end", "invalid/tga/packet-past-end.tga",
            Bytes.Concat(Bytes.Slice(rle, null, 18), [0x80 | 127], [1, 2, 3], Bytes.Slice(rle, 18 + 4)),
            "InvalidImageContentException", "The first packet repeats 128 pixels and the following packets then run past " +
            "the last pixel of the image.", confirm: false);
        AddError(c, Format, "invalid/tga/truncated-packet", "invalid/tga/truncated-packet.tga", Bytes.Slice(rle, null, -2),
            "InvalidImageContentException", "The input ends inside the last packet.");
        AddError(c, Format, "invalid/tga/truncated-pixels", "invalid/tga/truncated-pixels.tga",
            Bytes.Slice(read["tga/truecolor24-bottom-left"], null, -4),
            "InvalidImageContentException", "The input ends four bytes before the end of the uncompressed pixel data.");

        var colorMap = read["tga/colormap8-offset"];
        var mapStart = 18 + 5 * 3;
        AddError(c, Format, "invalid/tga/colormap-index-out-of-range", "invalid/tga/colormap-index-out-of-range.tga",
            Bytes.Concat(Bytes.Slice(colorMap, null, mapStart), [1], Bytes.Slice(colorMap, mapStart + 1)),
            "InvalidImageContentException", "A pixel uses the color-map index 1, below the first stored entry (the map " +
            "starts at index 2 and holds five entries).");

        AddError(c, Format, "invalid/tga/colormap-16bit-indexes", "invalid/tga/colormap-16bit-indexes.tga",
            Bytes.Concat(Bytes.Slice(colorMap, null, 16), [16], Bytes.Slice(colorMap, 17)),
            "UnsupportedImageFeatureException", "16-bit color-map indexes are recognized and rejected; this version " +
            "decodes 8-bit indexes.", feature: "Color map: 16-bit indexes", features: ["tga.unsupported=colormap-16bit"]);
        AddError(c, Format, "invalid/tga/grayscale-16bit", "invalid/tga/grayscale-16bit.tga",
            Bytes.Concat(Bytes.Slice(read["tga/grayscale8"], null, 16), [16], Bytes.Slice(read["tga/grayscale8"], 17)),
            "UnsupportedImageFeatureException", "16-bit grayscale samples are recognized and rejected; this version " +
            "decodes 8-bit grayscale.", feature: "Grayscale: 16-bit samples", features: ["tga.unsupported=grayscale-16bit"]);

        // The trailer of tga/extension-useful-alpha: the 495-byte extension area starts at the end of the image data and
        // the 26-byte footer ends the file
        var extended = read["tga/extension-useful-alpha"];
        var extensionStart = extended.Length - 26 - TgaExtensionLength;
        AddError(c, Format, "invalid/tga/extension-premultiplied-alpha", "invalid/tga/extension-premultiplied-alpha.tga",
            Bytes.Concat(Bytes.Slice(extended, null, extensionStart + 494), [4], Bytes.Slice(extended, extensionStart + 495)),
            "UnsupportedImageFeatureException", "The extension area declares attributes type 4 (premultiplied alpha) for a " +
            "32-bit image with 8 alpha bits: the working pixel formats store straight alpha, and the color samples are " +
            "never divided by the alpha.", feature: "Attributes type: premultiplied alpha",
            features: ["tga.unsupported=premultiplied-alpha"]);
        AddError(c, Format, "invalid/tga/extension-offset-past-footer", "invalid/tga/extension-offset-past-footer.tga",
            Bytes.Concat(Bytes.Slice(extended, null, -26), TgaFooter(extensionOffset: extensionStart + 1)),
            "InvalidImageContentException", "The footer locates the 495-byte extension area one byte after its start, so " +
            "that it would overlap the footer.");
        AddError(c, Format, "invalid/tga/extension-size-too-small", "invalid/tga/extension-size-too-small.tga",
            Bytes.Concat(Bytes.Slice(extended, null, extensionStart), [494 & 0xFF, 494 >> 8], Bytes.Slice(extended, extensionStart + 2)),
            "InvalidImageContentException", "The extension area declares 494 bytes, one fewer than the TGA 2.0 extension " +
            "area: it cannot hold the attributes type, its last byte.");
    }

    private static void BuildLimits(Corpus<FfmpegTools> c)
    {
        AddLimit(c, Format, "limit/tga/width-over-limit", "tga/rle-truecolor24-packets", new Obj { ["MaxWidth"] = 8 }, "Width",
            "Reuses the 12x16 tga/rle-truecolor24-packets input with MaxWidth = 8 (checked from the 18-byte header, " +
            "before any packet is read).");
        AddLimit(c, Format, "limit/tga/encoded-bytes-over-limit", "tga/truecolor24-bottom-left", new Obj { ["MaxEncodedBytes"] = 40 }, "EncodedBytes",
            "Reuses tga/truecolor24-bottom-left with MaxEncodedBytes = 40, below the 18-byte header plus 45 bytes of pixels.");
    }
}
