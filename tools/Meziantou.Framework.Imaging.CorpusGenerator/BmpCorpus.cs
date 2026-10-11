using System.Numerics;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using static Meziantou.Framework.Imaging.CorpusGenerator.Common.HandAssembledCorpus;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the BMP fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/bmp, invalid/bmp and limit/bmp entries).
/// <para>
/// This generator is the reviewed, reproducible recipe of every committed BMP fixture. It is NEVER run by the tests and
/// does not use the library under test. The shared driver and helpers are in HandAssembledCorpus.
/// </para>
/// <para>
/// Expected pixels are hand-defined:
/// - every input is assembled byte by byte by the writers below, next to the literal pixels it must produce;
/// - a few inputs are produced by FFmpeg's independent BMP encoder, from hand-defined patterns: the format is lossless,
///   so the pattern is the reference.
/// </para>
/// <para>
/// Every input is then decoded by the strict transcription of the specification in this generator (headers, masks and palettes
/// are all validated) and by FFmpeg/libavcodec, and the decodings must agree.
/// The only recorded disagreement is FFmpeg's 5-bit-to-8-bit expansion of 16-bit samples, which replicates bits instead of
/// rounding the exact ratio (at most one unit per sample); it is recorded as a crossCheck with its measured error, never hidden.
/// </para>
/// <para>
/// Malformed, unsupported and over-limit inputs are byte edits of valid inputs; each defect is confirmed by the strict
/// transcription.
/// </para>
/// <para>
/// It complements GoldenCorpus: this generator only replaces the manifest entries whose format is bmp and their files, and
/// keeps every other entry and file unchanged.
/// </para>
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- bmp          # check (temporary directory and diff)
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- bmp --write  # explicit regeneration (review the diff!)
/// Requirements: FFmpeg at the pinned version (FfmpegTools; override with --accept-tool-versions after reviewing the differences).
/// Any OS.
/// </summary>
internal static class BmpCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/BmpCorpus.cs";

    private static readonly HandAssembledFormat Format = new("bmp", "Bmp", BmpRead);

    public static int Run(GeneratorOptions options) => HandAssembledCorpus.Run(options, Format, ScriptPath, c =>
    {
        BuildBmp(c);
        BuildErrors(c);
        BuildLimits(c);
    });

    // ---------------------------------------------------------------------------------------------------------------------
    // Windows BMP: the specification of BITMAPFILEHEADER and the BITMAPINFOHEADER family, transcribed as a strict
    // reader and as a writer that assembles inputs byte by byte
    // ---------------------------------------------------------------------------------------------------------------------

    private const int BiRgb = 0;
    private const int BiRle8 = 1;
    private const int BiRle4 = 2;
    private const int BiBitfields = 3;
    private const int BiJpeg = 4;
    private const int BiPng = 5;
    private const int BiAlphabitfields = 6;
    private const uint LcsSrgb = 0x73524742;


    private static (int Shift, int Bits) MaskShiftBits(uint mask)
    {
        if (mask == 0)
            return (0, 0);
        var shift = BitOperations.TrailingZeroCount(mask);
        var bits = BitOperations.PopCount(mask);
        if ((mask >> shift) != (1L << bits) - 1)
            throw new FormatError("non-contiguous mask 0x" + mask.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        return (shift, bits);
    }

    private static int BmpRowLength(int width, int bitsPerPixel) => (width * bitsPerPixel + 31) / 32 * 4;

    /// <summary>Assembles a BMP file. rows are the stored rows in top-down order; they are written bottom-up unless top_down.</summary>
    private static byte[] BmpFile(int width, int height, int bitsPerPixel, IReadOnlyList<byte[]> rows, Px[]? palette = null, int compression = BiRgb,
        IReadOnlyList<uint>? masks = null, int headerLength = 40, bool topDown = false, (int X, int Y)? pixelsPerMeter = null, int gap = 0, int? colorsUsed = null)
    {
        palette ??= [];
        var stored = topDown ? rows.ToList() : rows.Reverse().ToList();
        var rowLength = BmpRowLength(width, bitsPerPixel);
        foreach (var row in stored)
            Py.Assert(row.Length == rowLength, Inv($"({row.Length}, {rowLength})"));
        var paletteBytes = new ByteBuilder();
        foreach (var p in palette)
            paletteBytes.U8(p[2]).U8(p[1]).U8(p[0]).U8(0);
        var trailingMasks = new ByteBuilder();
        if (headerLength == 40 && compression is BiBitfields or BiAlphabitfields)
        {
            var count = compression == BiAlphabitfields ? 4 : 3;
            foreach (var mask in masks!.Take(count))
                trailingMasks.U32LE(mask);
        }

        var dib = new ByteBuilder()
            .U32LE(headerLength).I32LE(width).I32LE(topDown ? -height : height).U16LE(1).U16LE(bitsPerPixel)
            .U32LE(compression).U32LE((long)rowLength * height).I32LE(pixelsPerMeter?.X ?? 0).I32LE(pixelsPerMeter?.Y ?? 0)
            .U32LE(colorsUsed ?? palette.Length).U32LE(0);
        if (headerLength >= 52)
        {
            foreach (var mask in masks!.Take(3))
                dib.U32LE(mask);
        }

        if (headerLength >= 56)
            dib.U32LE(masks!.Count > 3 ? masks[3] : 0);
        if (headerLength >= 108)
            dib.U32LE(LcsSrgb);
        dib.Zeros(headerLength - dib.Length);
        var offset = 14 + headerLength + trailingMasks.Length + paletteBytes.Length + gap;
        var body = Bytes.Concat(stored);
        var header = new ByteBuilder().Ascii("BM").U32LE(14 + dib.Length + trailingMasks.Length + paletteBytes.Length + gap + body.Length).U16LE(0).U16LE(0).U32LE(offset);
        return Bytes.Concat(header.ToArray(), dib.ToArray(), trailingMasks.ToArray(), paletteBytes.ToArray(), new byte[gap], body);
    }

    /// <summary>Strict transcription of the supported BMP subset: returns (Img rgba8, features). Raises FormatError for every
    /// defect this library must reject.</summary>
    private static (Img Image, List<string> Features) BmpRead(byte[] data)
    {
        if (data.Length < 14 || data[0] != (byte)'B' || data[1] != (byte)'M')
            throw new FormatError("signature");
        var offsetBits = (long)Bytes.U32LE(data, 10);
        if (data.Length < 18)
            throw new FormatError("truncated DIB header");
        var headerLength = (long)Bytes.U32LE(data, 14);
        if (headerLength is not (40 or 52 or 56 or 108 or 124))
            throw new FormatError(Inv($"unsupported DIB header of {headerLength} bytes"));
        if (data.Length < 14 + headerLength)
            throw new FormatError("truncated DIB header");
        var dib = data.AsSpan(14, (int)headerLength);
        var width = Bytes.I32LE(dib, 4);
        var storedHeight = Bytes.I32LE(dib, 8);
        int planes = Bytes.U16LE(dib, 12);
        int bpp = Bytes.U16LE(dib, 14);
        var compression = (long)Bytes.U32LE(dib, 16);
        var ppmX = Bytes.I32LE(dib, 24);
        var ppmY = Bytes.I32LE(dib, 28);
        var colorsUsed = (long)Bytes.U32LE(dib, 32);
        if (width <= 0)
            throw new FormatError(Inv($"width {width}"));
        if (storedHeight == 0 || storedHeight == int.MinValue)
            throw new FormatError(Inv($"height {storedHeight}"));
        if (planes != 1)
            throw new FormatError(Inv($"planes {planes}"));
        if (compression is BiRle8 or BiRle4 or BiJpeg or BiPng)
            throw new FormatError(Inv($"unsupported compression {compression}"));
        if (compression is not (BiRgb or BiBitfields or BiAlphabitfields))
            throw new FormatError(Inv($"undefined compression {compression}"));
        if (bpp is not (1 or 4 or 8 or 16 or 24 or 32))
            throw new FormatError(Inv($"bit count {bpp}"));
        var bitfields = compression is BiBitfields or BiAlphabitfields;
        if (bitfields && bpp is not (16 or 32))
            throw new FormatError(Inv($"masks with {bpp} bits per pixel"));
        var height = Math.Abs(storedHeight);
        var topDown = storedHeight < 0;
        var indexed = bpp <= 8;
        var maximumEntries = indexed ? 1L << bpp : 256;
        if (colorsUsed > maximumEntries)
            throw new FormatError(Inv($"clrUsed {colorsUsed}"));
        var entries = colorsUsed != 0 ? colorsUsed : (indexed ? 1L << bpp : 0);

        var masks = new uint[4];
        var position = 14 + headerLength;
        if (headerLength >= 52)
        {
            masks[0] = Bytes.U32LE(dib, 40);
            masks[1] = Bytes.U32LE(dib, 44);
            masks[2] = Bytes.U32LE(dib, 48);
            if (headerLength >= 56)
                masks[3] = Bytes.U32LE(dib, 52);
            if (!bitfields)
                masks = new uint[4];
        }
        else if (bitfields)
        {
            var count = compression == BiAlphabitfields ? 4 : 3;
            if (data.Length < position + 4 * count)
                throw new FormatError("truncated masks");
            for (var i = 0; i < count; i++)
                masks[i] = Bytes.U32LE(data, (int)position + 4 * i);
            position += 4 * count;
        }

        if (bitfields)
        {
            if (!(masks[0] != 0 && masks[1] != 0 && masks[2] != 0))
                throw new FormatError("zero color mask");
        }
        else if (bpp == 16)
        {
            (masks[0], masks[1], masks[2]) = (0x7C00, 0x03E0, 0x001F);
        }
        else if (bpp == 32)
        {
            (masks[0], masks[1], masks[2]) = (0x00FF0000, 0x0000FF00, 0x000000FF);
        }

        var channels = new List<(uint Mask, int Shift, int Bits)>();
        if (bpp is 16 or 32)
        {
            var used = 0u;
            foreach (var mask in masks)
            {
                var (shift, bits) = MaskShiftBits(mask);
                if (mask != 0)
                {
                    if (shift + bits > bpp)
                        throw new FormatError(Inv($"mask 0x{mask:X8} outside a {bpp}-bit pixel"));
                    if (bits > 8)
                        throw new FormatError(Inv($"mask 0x{mask:X8} is {bits} bits wide"));
                    if ((used & mask) != 0)
                        throw new FormatError("overlapping masks");
                    used |= mask;
                }

                channels.Add((mask, shift, bits));
            }
        }

        var palette = new List<Px>();
        if (entries != 0)
        {
            if (data.Length < position + 4 * entries)
                throw new FormatError("truncated palette");
            for (var i = 0; i < entries; i++)
            {
                var at = (int)position + 4 * i;
                palette.Add(new Px(data[at + 2], data[at + 1], data[at]));
            }

            position += 4 * entries;
        }

        var headerEnd = position;
        if (offsetBits < headerEnd)
            throw new FormatError(Inv($"pixel offset {offsetBits} before the end of the header and palette ({headerEnd})"));
        position = offsetBits;
        var rowLength = BmpRowLength(width, bpp);
        if (data.Length < position + (long)rowLength * height)
            throw new FormatError("truncated pixel data");

        var hasAlpha = masks[3] != 0;
        var rows = new List<List<Px>>();
        for (var index = 0; index < height; index++)
        {
            var row = data.AsSpan((int)position + index * rowLength, rowLength);
            var pixels = new List<Px>();
            for (var x = 0; x < width; x++)
            {
                if (bpp == 24)
                {
                    pixels.Add(new Px(row[x * 3 + 2], row[x * 3 + 1], row[x * 3], 255));
                }
                else if (indexed)
                {
                    int value;
                    if (bpp == 1)
                        value = (row[x >> 3] >> (7 - (x & 7))) & 1;
                    else if (bpp == 4)
                        value = x % 2 == 0 ? row[x >> 1] >> 4 : row[x >> 1] & 0x0F;
                    else
                        value = row[x];
                    if (value >= palette.Count)
                        throw new FormatError(Inv($"palette index {value} outside the {palette.Count}-entry palette"));
                    pixels.Add(palette[value].Append(255));
                }
                else
                {
                    var size = bpp / 8;
                    long value = 0;
                    for (var i = size - 1; i >= 0; i--)
                        value = (value << 8) | row[x * size + i];
                    var sample = new List<int>();
                    foreach (var (mask, shift, bits) in channels.Take(3))
                        sample.Add(ScaleChannel((value & mask) >> shift, bits));
                    var (alphaMask, alphaShift, alphaBits) = channels[3];
                    sample.Add(alphaMask != 0 ? ScaleChannel((value & alphaMask) >> alphaShift, alphaBits) : 255);
                    pixels.Add(Px.From(sample));
                }
            }

            rows.Add(pixels);
        }

        if (!topDown)
            rows.Reverse();

        var features = new List<string>
        {
            Inv($"bmp.header={headerLength}"),
            Inv($"bmp.bpp={bpp}"),
            "bmp.rowOrder=" + (topDown ? "top-down" : "bottom-up"),
            "bmp.compression=" + (bitfields ? "bitfields" : "rgb"),
        };
        if (indexed)
            features.Add(Inv($"bmp.palette={palette.Count}"));
        if (bpp is 16 or 32)
            features.Add("bmp.alphaMask=" + (hasAlpha ? "yes" : "no"));
        if (rowLength != width * bpp / 8)
            features.Add("bmp.rowPadding");
        if (width % 2 == 1)
            features.Add("bmp.width=odd");
        if (offsetBits > headerEnd)
            features.Add("bmp.gapBeforePixels");
        if (ppmX > 0 && ppmY > 0)
            features.Add("bmp.resolution");
        return (RgbaImage(width, height, rows.SelectMany(r => r)), SortedSet(features));
    }


    // ---------------------------------------------------------------------------------------------------------------------
    // BMP fixtures
    // ---------------------------------------------------------------------------------------------------------------------

    // Literal pixel grids: every channel and every row differs, so a channel swap, a flip or a transposition is detected
    private static readonly Px[] BmpRgb3X2 =
    [
        new(255, 0, 0), new(0, 255, 0), new(0, 0, 255),
        new(10, 20, 30), new(200, 150, 100), new(1, 2, 254),
    ];

    private static readonly Px[] BmpRgba3X2 =
    [
        new(255, 0, 0, 255), new(0, 255, 0, 128), new(0, 0, 255, 0),
        new(10, 20, 30, 1), new(200, 150, 100, 254), new(1, 2, 254, 64),
    ];

    private static List<byte[]> BmpPack24(IEnumerable<IReadOnlyList<Px>> rows, int width)
    {
        var padding = BmpRowLength(width, 24) - width * 3;
        return [.. rows.Select(row =>
        {
            var b = new ByteBuilder();
            foreach (var p in row)
                b.U8(p[2]).U8(p[1]).U8(p[0]);
            return b.Zeros(padding).ToArray();
        })];
    }

    private static List<byte[]> BmpPack32(IEnumerable<IReadOnlyList<Px>> rows, Func<Px, long> pack) =>
        [.. rows.Select(row =>
        {
            var b = new ByteBuilder();
            foreach (var p in row)
                b.U32LE(pack(p));
            return b.ToArray();
        })];

    private static List<byte[]> BmpPack16(IEnumerable<IReadOnlyList<Px>> rows, int width, Func<Px, int> pack)
    {
        var padding = BmpRowLength(width, 16) - width * 2;
        return [.. rows.Select(row =>
        {
            var b = new ByteBuilder();
            foreach (var p in row)
                b.U16LE(pack(p));
            return b.Zeros(padding).ToArray();
        })];
    }

    private static List<byte[]> BmpPackIndexed(IEnumerable<IReadOnlyList<int>> rows, int width, int bits)
    {
        var output = new List<byte[]>();
        foreach (var row in rows)
        {
            var data = new byte[BmpRowLength(width, bits)];
            for (var x = 0; x < row.Count; x++)
            {
                var index = row[x];
                if (bits == 8)
                {
                    data[x] = (byte)index;
                }
                else if (bits == 4)
                {
                    data[x >> 1] |= (byte)(x % 2 == 0 ? index << 4 : index);
                }
                else
                {
                    if (index != 0)
                        data[x >> 3] |= (byte)(0x80 >> (x & 7));
                }
            }

            output.Add(data);
        }

        return output;
    }


    private static void BuildBmp(Corpus<FfmpegTools> c)
    {
        var rgb = ChunkRows(BmpRgb3X2, 3);
        var rgbImg = new Img(3, 2, "rgb", 8, BmpRgb3X2);
        var data = BmpFile(3, 2, 24, BmpPack24(rgb, 3));
        AddFixture(c, Format, "bmp/bgr24-bottom-up-padding", "bmp/bgr24-bottom-up-padding.bmp", data, rgbImg, "Rgb24", "Rgb", 8,
            "BuildBmp", new Obj { ["width"] = 3, ["height"] = 2, ["bitsPerPixel"] = 24 },
            "Assembled byte by byte: BITMAPINFOHEADER, uncompressed 24-bit BGR, bottom-up rows (the first stored row is " +
            "the bottom one) and three padding bytes per row (an odd width of 3 pixels needs 9 bytes padded to 12).",
            required: ["bmp.bpp=24", "bmp.rowOrder=bottom-up", "bmp.rowPadding", "bmp.width=odd"]);

        data = BmpFile(3, 2, 24, BmpPack24(rgb, 3), topDown: true);
        AddFixture(c, Format, "bmp/bgr24-top-down", "bmp/bgr24-top-down.bmp", data, rgbImg, "Rgb24", "Rgb", 8,
            "BuildBmp", new Obj { ["width"] = 3, ["height"] = 2, ["biHeight"] = -2 },
            "The same pixels with a negative biHeight: the first stored row is the top one. Decoding both fixtures to the " +
            "same reference proves the row order is honored and not merely consistent.",
            required: ["bmp.rowOrder=top-down"]);

        var rgbaImg = new Img(3, 2, "rgba", 8, BmpRgba3X2);
        var rows = ChunkRows(BmpRgba3X2, 3);
        static long Pack(Px p) => ((long)p[3] << 24) | ((long)p[0] << 16) | ((long)p[1] << 8) | (long)p[2];
        data = BmpFile(3, 2, 32, BmpPack32(rows, Pack), compression: BiBitfields,
            masks: [0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000], headerLength: 108);
        AddFixture(c, Format, "bmp/bgra32-v4-alpha", "bmp/bgra32-v4-alpha.bmp", data, rgbaImg, "Rgba32", "Rgba", 8,
            "BuildBmp", new Obj { ["width"] = 3, ["height"] = 2, ["header"] = 108 },
            "BITMAPV4HEADER with BI_BITFIELDS and an explicit alpha mask: the fourth byte is real transparency, including " +
            "a fully transparent pixel whose color is still defined and an almost transparent one.",
            required: ["bmp.header=108", "bmp.alphaMask=yes", "bmp.compression=bitfields"]);

        // The same bytes with BI_RGB: the fourth byte is unspecified padding, so the image is opaque RGB
        data = BmpFile(3, 2, 32, BmpPack32(rows, Pack), compression: BiRgb);
        AddFixture(c, Format, "bmp/bgrx32-unspecified-alpha", "bmp/bgrx32-unspecified-alpha.bmp", data, rgbImg, "Rgb24", "Rgb", 8,
            "BuildBmp", new Obj { ["width"] = 3, ["height"] = 2, ["fourthByte"] = "unspecified" },
            "32-bit BI_RGB: the fourth byte holds the same values as bmp/bgra32-v4-alpha but no alpha mask is declared, so " +
            "it is unspecified padding. The image is the opaque RGB samples; alpha is never guessed from a 32-bit payload.",
            required: ["bmp.bpp=32", "bmp.alphaMask=no", "bmp.compression=rgb"],
            crossCheck: "differs",
            crossCheckNotes: "FFmpeg reads the fourth byte of a 32-bit BI_RGB payload as alpha; the specification leaves " +
                             "it unspecified and no alpha mask is declared, so the reference is opaque. Every color " +
                             "sample agrees exactly (mean color error 0); only the alpha interpretation differs.");

        // 16-bit layouts: the default 5-5-5 masks of BI_RGB, explicit 5-6-5 masks after a 40-byte header, and a 1-5-5-5 alpha mask
        Px[] bits555 =
        [
            new(0, 0, 0), new(255, 255, 255), new(255, 0, 0), new(0, 255, 0), new(0, 0, 255),
            new(8, 8, 8), new(33, 0, 0), new(0, 33, 0), new(0, 0, 33), new(255, 189, 115),
            new(74, 115, 189), new(189, 74, 115), new(115, 189, 74), new(255, 8, 255), new(8, 255, 8),
        ];
        var expected555 = bits555.Select(p => p.Select(v => ScaleChannel(Reduce(v, 31), 5))).ToList();
        var img555 = new Img(5, 3, "rgb", 8, expected555);
        static int Pack555(Px p) => (Reduce(p[0], 31) << 10) | (Reduce(p[1], 31) << 5) | Reduce(p[2], 31);
        data = BmpFile(5, 3, 16, BmpPack16(ChunkRows(bits555, 5), 5, Pack555));
        AddFixture(c, Format, "bmp/rgb555-default-masks", "bmp/rgb555-default-masks.bmp", data, img555, "Rgb24", "Rgb", 8,
            "BuildBmp", new Obj { ["width"] = 5, ["height"] = 3, ["masks"] = "implicit 5-5-5" },
            "16-bit BI_RGB: the implicit 5-5-5 masks of the specification. Every channel is expanded to 8 bits with the " +
            "exact ratio round(value * 255 / 31), so 0 maps to 0 and 31 to 255; two padding bytes follow each 10-byte row.",
            required: ["bmp.bpp=16", "bmp.compression=rgb", "bmp.rowPadding", "bmp.width=odd"],
            crossCheck: "differs",
            crossCheckNotes: "FFmpeg expands 5-bit channels by replicating their high bits instead of rounding the exact " +
                             "ratio round(value * 255 / 31); every sample agrees within one unit.");

        var expected565 = bits555.Select(p => new Px(ScaleChannel(Reduce(p[0], 31), 5), ScaleChannel(Reduce(p[1], 63), 6), ScaleChannel(Reduce(p[2], 31), 5))).ToList();
        var img565 = new Img(5, 3, "rgb", 8, expected565);
        static int Pack565(Px p) => (Reduce(p[0], 31) << 11) | (Reduce(p[1], 63) << 5) | Reduce(p[2], 31);
        data = BmpFile(5, 3, 16, BmpPack16(ChunkRows(bits555, 5), 5, Pack565), compression: BiBitfields,
            masks: [0xF800, 0x07E0, 0x001F]);
        AddFixture(c, Format, "bmp/bitfields565-trailing-masks", "bmp/bitfields565-trailing-masks.bmp", data, img565, "Rgb24", "Rgb", 8,
            "BuildBmp", new Obj { ["width"] = 5, ["height"] = 3, ["masks"] = "5-6-5 after the header" },
            "BI_BITFIELDS after a 40-byte BITMAPINFOHEADER: the three masks are stored between the header and the pixel " +
            "data (and counted in bfOffBits). The green channel is 6 bits wide, the others 5.",
            required: ["bmp.header=40", "bmp.compression=bitfields", "bmp.alphaMask=no"],
            crossCheck: "differs",
            crossCheckNotes: "FFmpeg expands 5- and 6-bit channels by replicating their high bits instead of rounding the " +
                             "exact ratio; every sample agrees within one unit.");

        var alpha1555 = bits555.Select((p, i) => p.Append(i % 3 != 0 ? 255 : 0)).ToList();
        var expected1555 = alpha1555.Select(p => p.Slice(0, 3).Select(v => ScaleChannel(Reduce(v, 31), 5)).Append(p[3])).ToList();
        var img1555 = new Img(5, 3, "rgba", 8, expected1555);
        static int Pack1555(Px p) => (p[3] != 0 ? 0x8000 : 0) | Pack555(p);
        data = BmpFile(5, 3, 16, BmpPack16(ChunkRows(alpha1555, 5), 5, Pack1555), compression: BiBitfields,
            masks: [0x7C00, 0x03E0, 0x001F, 0x8000], headerLength: 56);
        AddFixture(c, Format, "bmp/bitfields1555-alpha", "bmp/bitfields1555-alpha.bmp", data, img1555, "Rgba32", "Rgba", 8,
            "BuildBmp", new Obj { ["width"] = 5, ["height"] = 3, ["masks"] = "1-5-5-5" },
            "BITMAPV3INFOHEADER (56 bytes) with a one-bit alpha mask: the single alpha bit expands to 0 or 255, and the " +
            "color channels keep their defined values under transparent pixels.",
            required: ["bmp.header=56", "bmp.alphaMask=yes"],
            crossCheck: "differs",
            crossCheckNotes: "FFmpeg expands 5-bit channels by replicating their high bits instead of rounding the exact " +
                             "ratio, and reads this layout as opaque 5-5-5; the alpha bit and the rounding are recorded here.");

        // Indexed layouts: a partial palette with a gap before the pixel data, 4-bit and 1-bit indexes
        Px[] palette5 = [new(0, 0, 0), new(255, 255, 255), new(220, 30, 40), new(30, 220, 40), new(40, 30, 220)];
        int[] indexes8 =
        [
            0, 1, 2, 3, 4, 2, 1,
            4, 3, 2, 1, 0, 3, 4,
            1, 1, 0, 4, 2, 3, 0,
        ];
        var img8 = new Img(7, 3, "rgb", 8, indexes8.Select(i => palette5[i]));
        data = BmpFile(7, 3, 8, BmpPackIndexed(ChunkRows(indexes8, 7), 7, 8), palette: palette5, gap: 4);
        AddFixture(c, Format, "bmp/palette8-partial-gap", "bmp/palette8-partial-gap.bmp", data, img8, "Rgb24", "Indexed", 8,
            "BuildBmp", new Obj { ["width"] = 7, ["height"] = 3, ["paletteEntries"] = 5, ["gap"] = 4 },
            "8-bit indexes with a five-entry palette (biClrUsed = 5, not the full 256) and four unused bytes between the " +
            "palette and the pixel data: bfOffBits is authoritative, the gap is skipped, and the row is padded to 8 bytes.",
            required: ["bmp.palette=5", "bmp.gapBeforePixels", "bmp.rowPadding", "bmp.width=odd"]);

        int[] indexes4 =
        [
            0, 1, 2, 3, 4,
            4, 3, 2, 1, 0,
            2, 2, 0, 4, 1,
        ];
        var img4 = new Img(5, 3, "rgb", 8, indexes4.Select(i => palette5[i]));
        data = BmpFile(5, 3, 4, BmpPackIndexed(ChunkRows(indexes4, 5), 5, 4), palette: palette5);
        AddFixture(c, Format, "bmp/palette4-odd-width", "bmp/palette4-odd-width.bmp", data, img4, "Rgb24", "Indexed", 4,
            "BuildBmp", new Obj { ["width"] = 5, ["height"] = 3, ["bitsPerPixel"] = 4 },
            "4-bit indexes, odd width: the high nibble is the left pixel, the last byte of each row carries one pixel and " +
            "one unused nibble, and the row is padded to 4 bytes.",
            required: ["bmp.bpp=4", "bmp.palette=5", "bmp.rowPadding"]);

        Px[] palette2 = [new(16, 32, 48), new(240, 224, 208)];
        int[] indexes1 =
        [
            0, 1, 1, 0, 1, 0, 0, 1, 1,
            1, 0, 0, 1, 0, 1, 1, 0, 0,
        ];
        var img1 = new Img(9, 2, "rgb", 8, indexes1.Select(i => palette2[i]));
        data = BmpFile(9, 2, 1, BmpPackIndexed(ChunkRows(indexes1, 9), 9, 1), palette: palette2);
        AddFixture(c, Format, "bmp/palette1-two-colors", "bmp/palette1-two-colors.bmp", data, img1, "Rgb24", "Indexed", 1,
            "BuildBmp", new Obj { ["width"] = 9, ["height"] = 2, ["bitsPerPixel"] = 1 },
            "1-bit indexes with a two-entry palette that is not black and white: the most significant bit is the left " +
            "pixel, and the nine pixels of a row occupy two bytes padded to four.",
            required: ["bmp.bpp=1", "bmp.palette=2", "bmp.rowPadding", "bmp.width=odd"]);

        var resolutionImg = new Img(2, 2, "rgb", 8, [new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(250, 251, 252)]);
        data = BmpFile(2, 2, 24, BmpPack24(ChunkRows(resolutionImg.Pixels, 2), 2), pixelsPerMeter: (2835, 5670));
        AddFixture(c, Format, "bmp/bgr24-resolution", "bmp/bgr24-resolution.bmp", data, resolutionImg, "Rgb24", "Rgb", 8,
            "BuildBmp", new Obj { ["width"] = 2, ["height"] = 2, ["pixelsPerMeter"] = new List<object?> { 2835, 5670 } },
            "biXPelsPerMeter = 2835 and biYPelsPerMeter = 5670 (about 72 and 144 dpi): the only metadata a BMP file " +
            "stores, decoded as ImageMetadata.Resolution without rounding.",
            required: ["bmp.resolution"]);

        // Inputs produced by FFmpeg's independent BMP encoder from hand-defined patterns (BMP is lossless)
        foreach (var (name, pattern, pixelFormat, colorModel, extra, required) in new (string, Img, string, string, string[], string[])[]
        {
            ("bmp/ffmpeg-bgr24", CommonCorpus.PatternSmoothColor(17, 9), "Rgb24", "Rgb", ["-pix_fmt", "bgr24"], ["bmp.bpp=24"]),
            ("bmp/ffmpeg-bgrx32", CommonCorpus.PatternSmoothColor(14, 6), "Rgb24", "Rgb", ["-pix_fmt", "bgra"], ["bmp.bpp=32", "bmp.alphaMask=no"]),
        })
        {
            var source = new Img(pattern.Width, pattern.Height, "rgb", 8, pattern.Rgba().Select(p => p.Slice(0, 3)));
            var (encoded, command) = FfmpegEncode(c, source, name.Replace('/', '-'), "bmp", extra);
            AddFixture(c, Format, name, name + ".bmp", encoded, source, pixelFormat, colorModel, 8,
                "BuildBmp " + (pattern.PatternName ?? "pattern"), new Obj { ["width"] = source.Width, ["height"] = source.Height },
                "Encoded by FFmpeg's independent BMP encoder from the hand-defined pattern; BMP is lossless, so the " +
                "pattern is the reference.",
                commands: [command], required: required);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------
    // Malformed, unsupported and over-limit inputs: byte edits of the valid fixtures, each defect confirmed by the
    // transcribed specification above
    // ---------------------------------------------------------------------------------------------------------------------

    private static void BuildErrors(Corpus<FfmpegTools> c)
    {
        var read = ReadValidInputs(c);
        var valid = read["bmp/bgr24-bottom-up-padding"];

        byte[] BmpEdit(int offset, byte[] raw) => Splice(valid, offset, raw);

        AddError(c, Format, "invalid/bmp/zero-width", "invalid/bmp/zero-width.bmp", BmpEdit(18, I32(0)),
            "InvalidImageContentException", "biWidth is 0; a positive width is required.");
        AddError(c, Format, "invalid/bmp/zero-height", "invalid/bmp/zero-height.bmp", BmpEdit(22, I32(0)),
            "InvalidImageContentException", "biHeight is 0; the sign gives the row order and the magnitude the row count.");
        AddError(c, Format, "invalid/bmp/planes-2", "invalid/bmp/planes-2.bmp", BmpEdit(26, U16(2)),
            "InvalidImageContentException", "biPlanes is 2; 1 is the only legal value.");
        AddError(c, Format, "invalid/bmp/bit-count-7", "invalid/bmp/bit-count-7.bmp", BmpEdit(28, U16(7)),
            "InvalidImageContentException", "biBitCount is 7; 1, 4, 8, 16, 24 and 32 are the defined values.");
        AddError(c, Format, "invalid/bmp/undefined-compression", "invalid/bmp/undefined-compression.bmp", BmpEdit(30, U32(12)),
            "InvalidImageContentException", "biCompression is 12, which is not a defined BI_* value.");
        AddError(c, Format, "invalid/bmp/pixel-offset-too-small", "invalid/bmp/pixel-offset-too-small.bmp", BmpEdit(10, U32(50)),
            "InvalidImageContentException", "bfOffBits is 50, inside the 54-byte header and palette area.");
        AddError(c, Format, "invalid/bmp/truncated-header", "invalid/bmp/truncated-header.bmp", Bytes.Slice(valid, null, 30),
            "InvalidImageContentException", "The input ends inside the BITMAPINFOHEADER (after biCompression's first byte).");
        AddError(c, Format, "invalid/bmp/truncated-pixels", "invalid/bmp/truncated-pixels.bmp", Bytes.Slice(valid, null, -5),
            "InvalidImageContentException", "The input ends five bytes before the end of the last row: truncated input is " +
            "malformed data, never a clean end of input.");

        var indexed = read["bmp/palette8-partial-gap"];
        var offsetBits = (int)Bytes.U32LE(indexed, 10);
        AddError(c, Format, "invalid/bmp/palette-index-out-of-range", "invalid/bmp/palette-index-out-of-range.bmp",
            Bytes.Concat(Bytes.Slice(indexed, null, offsetBits + 3), [5], Bytes.Slice(indexed, offsetBits + 4)),
            "InvalidImageContentException", "A pixel uses the palette index 5, outside the five-entry palette (0 to 4).");

        var bitfields = read["bmp/bitfields565-trailing-masks"];
        AddError(c, Format, "invalid/bmp/overlapping-masks", "invalid/bmp/overlapping-masks.bmp",
            Bytes.Concat(Bytes.Slice(bitfields, null, 54), U32(0xF800, 0xF800, 0x001F), Bytes.Slice(bitfields, 66)),
            "InvalidImageContentException", "The red and green masks are both 0xF800: the channels of a pixel may not overlap.");
        AddError(c, Format, "invalid/bmp/non-contiguous-mask", "invalid/bmp/non-contiguous-mask.bmp",
            Bytes.Concat(Bytes.Slice(bitfields, null, 54), U32(0xF810, 0x07E0, 0x000F), Bytes.Slice(bitfields, 66)),
            "InvalidImageContentException", "The red mask 0x0000F810 is not a contiguous run of bits.");
        AddError(c, Format, "invalid/bmp/zero-green-mask", "invalid/bmp/zero-green-mask.bmp",
            Bytes.Concat(Bytes.Slice(bitfields, null, 54), U32(0xF800, 0x0000, 0x001F), Bytes.Slice(bitfields, 66)),
            "InvalidImageContentException", "BI_BITFIELDS is declared with a zero green mask.");

        AddError(c, Format, "invalid/bmp/core-header", "invalid/bmp/core-header.bmp",
            Bytes.Concat(new ByteBuilder().Ascii("BM").U32LE(14 + 12 + 12).U16LE(0).U16LE(0).U32LE(26).ToArray(),
                new ByteBuilder().U32LE(12).I16LE(3).I16LE(2).U16LE(1).U16LE(24).ToArray(), Bytes.Slice(valid, 54)),
            "UnsupportedImageFeatureException", "The 12-byte OS/2 BITMAPCOREHEADER is recognized and rejected; it is a " +
            "documented follow-up, not a silently mis-parsed header.", feature: "Header: BITMAPCOREHEADER", features: ["bmp.unsupported=core-header"]);
        AddError(c, Format, "invalid/bmp/os2-v2-header", "invalid/bmp/os2-v2-header.bmp",
            Bytes.Concat(Bytes.Slice(valid, null, 14), U32(64), Bytes.Slice(valid, 18, 54), new byte[24], Bytes.Slice(valid, 54)),
            "UnsupportedImageFeatureException", "The 64-byte OS/2 BITMAPCOREHEADER2 is recognized and rejected.",
            feature: "Header: BITMAPCOREHEADER2", features: ["bmp.unsupported=os2-v2-header"]);
        AddError(c, Format, "invalid/bmp/rle8", "invalid/bmp/rle8.bmp",
            BmpEdit(28, Bytes.Concat(U16(8), U32(BiRle8))),
            "UnsupportedImageFeatureException", "BI_RLE8 is recognized and rejected: run-length encoded BMP is a " +
            "documented follow-up.", feature: "Compression: RLE", features: ["bmp.unsupported=rle"]);
        AddError(c, Format, "invalid/bmp/embedded-png", "invalid/bmp/embedded-png.bmp",
            BmpEdit(28, Bytes.Concat(U16(24), U32(BiPng))),
            "UnsupportedImageFeatureException", "BI_PNG (an embedded PNG payload) is recognized and rejected.",
            feature: "Compression: embedded codec", features: ["bmp.unsupported=embedded-codec"]);
        AddError(c, Format, "invalid/bmp/bit-count-2", "invalid/bmp/bit-count-2.bmp", BmpEdit(28, U16(2)),
            "UnsupportedImageFeatureException", "biBitCount 2 is a Windows CE extension, recognized and rejected.",
            feature: "Bit depth: 2", features: ["bmp.unsupported=bit-count-2"]);
        AddError(c, Format, "invalid/bmp/mask-10-bits", "invalid/bmp/mask-10-bits.bmp",
            Bytes.Concat(Bytes.Slice(read["bmp/bgra32-v4-alpha"], null, 54), U32(0x3FF00000, 0x000FFC00, 0x000003FF, 0xC0000000),
                Bytes.Slice(read["bmp/bgra32-v4-alpha"], 70)),
            "UnsupportedImageFeatureException", "A 10-10-10-2 bit-field layout: channels wider than 8 bits are recognized " +
            "and rejected rather than truncated.", feature: "Bit fields: channel wider than 8 bits", features: ["bmp.unsupported=wide-mask"]);
    }

    private static void BuildLimits(Corpus<FfmpegTools> c)
    {
        AddLimit(c, Format, "limit/bmp/width-over-limit", "bmp/palette8-partial-gap", new Obj { ["MaxWidth"] = 5 }, "Width",
            "Reuses the 7x3 bmp/palette8-partial-gap input with MaxWidth = 5 (checked from the DIB header, before the " +
            "palette and the pixel data are read).");
        AddLimit(c, Format, "limit/bmp/frame-pixels-over-limit", "bmp/ffmpeg-bgr24", new Obj { ["MaxFramePixels"] = 100 }, "FramePixels",
            "Reuses the 17x9 (153 pixels) bmp/ffmpeg-bgr24 input with MaxFramePixels = 100.");
    }
}
