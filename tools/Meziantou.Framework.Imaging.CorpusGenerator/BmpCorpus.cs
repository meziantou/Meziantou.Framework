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
/// Every input is then decoded by the strict transcription of the specification in this generator (headers, masks, palettes
/// and run-length streams are all validated) and by FFmpeg/libavcodec, and the decodings must agree.
/// The recorded disagreements are FFmpeg's 5-bit-to-8-bit expansion of 16-bit samples, which replicates bits instead of
/// rounding the exact ratio (at most one unit per sample), its reading of the fourth byte of 32-bit BI_RGB pixels as alpha,
/// and the OS/2 variants it does not decode; each is recorded as a crossCheck with its notes, never hidden.
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
    // BMP: the specification of BITMAPFILEHEADER, of the BITMAPINFOHEADER family and of the OS/2 BITMAPCOREHEADER and
    // BITMAPCOREHEADER2, transcribed as a strict reader and as writers that assemble inputs byte by byte
    // ---------------------------------------------------------------------------------------------------------------------

    private const int BiRgb = 0;
    private const int BiRle8 = 1;
    private const int BiRle4 = 2;
    private const int BiBitfields = 3;
    private const int BiJpeg = 4;
    private const int BiPng = 5;
    private const int BiAlphabitfields = 6;
    private const int BcaHuffman1D = 3;
    private const int BcaRle24 = 4;
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

    /// <summary>
    /// Assembles a BMP file. rows are the stored rows in top-down order; they are written bottom-up unless top_down.
    /// A run-length file passes its stream instead (and no rows): the stream is stored as is.
    /// </summary>
    private static byte[] BmpFile(int width, int height, int bitsPerPixel, IReadOnlyList<byte[]> rows, Px[]? palette = null, int compression = BiRgb,
        IReadOnlyList<uint>? masks = null, int headerLength = 40, bool topDown = false, (int X, int Y)? pixelsPerMeter = null, int gap = 0, int? colorsUsed = null,
        byte[]? stream = null)
    {
        palette ??= [];
        var stored = topDown ? rows.ToList() : rows.Reverse().ToList();
        var rowLength = BmpRowLength(width, bitsPerPixel);
        foreach (var row in stored)
            Py.Assert(row.Length == rowLength, Inv($"({row.Length}, {rowLength})"));
        Py.Assert(stream is null || stored.Count == 0, "a run-length file stores a stream, not rows");
        var body = stream ?? Bytes.Concat(stored);
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
            .U32LE(compression).U32LE(body.Length).I32LE(pixelsPerMeter?.X ?? 0).I32LE(pixelsPerMeter?.Y ?? 0)
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
        var header = new ByteBuilder().Ascii("BM").U32LE(14 + dib.Length + trailingMasks.Length + paletteBytes.Length + gap + body.Length).U16LE(0).U16LE(0).U32LE(offset);
        return Bytes.Concat(header.ToArray(), dib.ToArray(), trailingMasks.ToArray(), paletteBytes.ToArray(), new byte[gap], body);
    }

    /// <summary>
    /// Assembles an OS/2 1.x file: a 12-byte BITMAPCOREHEADER (16-bit dimensions, no compression, no color count) and
    /// RGBTRIPLE palette entries. rows are the stored rows in top-down order; they are always written bottom-up.
    /// </summary>
    private static byte[] BmpCoreFile(int width, int height, int bitsPerPixel, IReadOnlyList<byte[]> rows, Px[]? palette = null, int gap = 0)
    {
        palette ??= [];
        var rowLength = BmpRowLength(width, bitsPerPixel);
        foreach (var row in rows)
            Py.Assert(row.Length == rowLength, Inv($"({row.Length}, {rowLength})"));
        var paletteBytes = new ByteBuilder();
        foreach (var p in palette)
            paletteBytes.U8(p[2]).U8(p[1]).U8(p[0]);
        var dib = new ByteBuilder().U32LE(12).U16LE(width).U16LE(height).U16LE(1).U16LE(bitsPerPixel);
        var body = Bytes.Concat(rows.Reverse());
        var offset = 14 + dib.Length + paletteBytes.Length + gap;
        var header = new ByteBuilder().Ascii("BM").U32LE(offset + body.Length).U16LE(0).U16LE(0).U32LE(offset);
        return Bytes.Concat(header.ToArray(), dib.ToArray(), paletteBytes.ToArray(), new byte[gap], body);
    }

    /// <summary>
    /// Assembles an OS/2 2.x file: a BITMAPCOREHEADER2 cut to headerLength bytes (16 to 64; the fields it leaves out are
    /// zero) and RGBQUAD palette entries. body is the pixel data as stored: bottom-up rows or a run-length stream.
    /// </summary>
    private static byte[] BmpOs2V2File(int headerLength, int width, int height, int bitsPerPixel, byte[] body, Px[]? palette = null, int compression = BiRgb,
        (int X, int Y)? pixelsPerMeter = null, int units = 0, int recording = 0, long colorEncoding = 0)
    {
        palette ??= [];

        // Without cclrUsed (offset 32), the palette has the full size of the bit count
        Py.Assert(headerLength >= 36 || palette.Length == (bitsPerPixel <= 8 ? 1 << bitsPerPixel : 0), "the header is too short to declare a partial palette");
        var paletteBytes = new ByteBuilder();
        foreach (var p in palette)
            paletteBytes.U8(p[2]).U8(p[1]).U8(p[0]).U8(0);
        var dib = new ByteBuilder()
            .U32LE(headerLength).U32LE(width).U32LE(height).U16LE(1).U16LE(bitsPerPixel)
            .U32LE(compression).U32LE(body.Length).U32LE(pixelsPerMeter?.X ?? 0).U32LE(pixelsPerMeter?.Y ?? 0)
            .U32LE(palette.Length).U32LE(0)
            // usUnits, usReserved, usRecording, usRendering, cSize1, cSize2, ulColorEncoding, ulIdentifier
            .U16LE(units).U16LE(0).U16LE(recording).U16LE(0).U32LE(0).U32LE(0).U32LE(colorEncoding).U32LE(0)
            .ToArray();
        Py.Assert(dib.Length == 64, "BITMAPCOREHEADER2 is 64 bytes");
        var offset = 14 + headerLength + paletteBytes.Length;
        var header = new ByteBuilder().Ascii("BM").U32LE(offset + body.Length).U16LE(0).U16LE(0).U32LE(offset);
        return Bytes.Concat(header.ToArray(), Bytes.Slice(dib, null, headerLength), paletteBytes.ToArray(), body);
    }

    /// <summary>
    /// Assembles a run-length stream command by command (BI_RLE8, BI_RLE4 and BCA_RLE24). The stream describes the rows
    /// bottom-up: the first row it writes is the last displayed one.
    /// </summary>
    private sealed class RleStream
    {
        private readonly ByteBuilder _bytes = new();

        /// <summary>Encoded mode, 8 bits: count pixels of one palette index.</summary>
        public RleStream Run(int count, int index)
        {
            Py.Assert(count is >= 1 and <= 255, "count");
            _bytes.U8(count).U8(index);
            return this;
        }

        /// <summary>Encoded mode, 4 bits: count pixels alternating two palette indexes, the first one first.</summary>
        public RleStream Run4(int count, int first, int second)
        {
            Py.Assert(count is >= 1 and <= 255, "count");
            _bytes.U8(count).U8((first << 4) | second);
            return this;
        }

        /// <summary>Encoded mode, 24 bits: count pixels of one blue, green, red triplet.</summary>
        public RleStream Run24(int count, Px pixel)
        {
            Py.Assert(count is >= 1 and <= 255, "count");
            _bytes.U8(count).U8(pixel[2]).U8(pixel[1]).U8(pixel[0]);
            return this;
        }

        /// <summary>Absolute mode, 8 bits: 3 to 255 literal indexes, padded to a 16-bit boundary.</summary>
        public RleStream Absolute(params int[] indexes)
        {
            Py.Assert(indexes.Length is >= 3 and <= 255, "count");
            _bytes.U8(0).U8(indexes.Length);
            foreach (var index in indexes)
                _bytes.U8(index);
            return Pad(indexes.Length);
        }

        /// <summary>Absolute mode, 4 bits: 3 to 255 literal indexes, two per byte (high-order first), padded to a 16-bit boundary.</summary>
        public RleStream Absolute4(params int[] indexes)
        {
            Py.Assert(indexes.Length is >= 3 and <= 255, "count");
            _bytes.U8(0).U8(indexes.Length);
            for (var i = 0; i < indexes.Length; i += 2)
                _bytes.U8((indexes[i] << 4) | (i + 1 < indexes.Length ? indexes[i + 1] : 0));
            return Pad((indexes.Length + 1) / 2);
        }

        /// <summary>Absolute mode, 24 bits: 3 to 255 literal blue, green, red triplets, padded to a 16-bit boundary.</summary>
        public RleStream Absolute24(params Px[] pixels)
        {
            Py.Assert(pixels.Length is >= 3 and <= 255, "count");
            _bytes.U8(0).U8(pixels.Length);
            foreach (var pixel in pixels)
                _bytes.U8(pixel[2]).U8(pixel[1]).U8(pixel[0]);
            return Pad(pixels.Length * 3);
        }

        /// <summary>The escape 00 02: moves the current position right and to a later stored row, skipping the pixels in between.</summary>
        public RleStream Delta(int right, int up)
        {
            _bytes.U8(0).U8(2).U8(right).U8(up);
            return this;
        }

        public RleStream EndOfLine()
        {
            _bytes.U8(0).U8(0);
            return this;
        }

        public RleStream EndOfBitmap()
        {
            _bytes.U8(0).U8(1);
            return this;
        }

        public byte[] ToArray() => _bytes.ToArray();

        private RleStream Pad(int dataLength)
        {
            if (dataLength % 2 == 1)
                _bytes.U8(0);
            return this;
        }
    }

    /// <summary>
    /// Strict transcription of the run-length streams: returns the stored rows (bottom-up) as one value per pixel, a palette
    /// index or a packed 0xRRGGBB color for 24 bits. The pixels the stream skips keep the value 0. Raises FormatError for a
    /// run that leaves its row, a delta that leaves the canvas, data after the last row and a missing end-of-bitmap marker.
    /// </summary>
    private static int[] RleRead(byte[] data, long position, int width, int height, int bits, List<string> features)
    {
        var values = new int[width * height];
        var unit = bits == 24 ? 3 : 1;
        var x = 0;
        var row = 0;
        while (true)
        {
            if (data.Length < position + 2)
                throw new FormatError("truncated run-length data");
            int count = data[position];
            int second = data[position + 1];
            if (count != 0)
            {
                if (data.Length < position + 1 + unit)
                    throw new FormatError("truncated run");
                if (row >= height)
                    throw new FormatError("run after the last row");
                if (x + count > width)
                    throw new FormatError(Inv($"run of {count} pixels past the end of the row"));
                for (var i = 0; i < count; i++)
                {
                    values[row * width + x + i] = bits switch
                    {
                        8 => second,
                        4 => i % 2 == 0 ? second >> 4 : second & 0x0F,
                        _ => (data[position + 3] << 16) | (data[position + 2] << 8) | data[position + 1],
                    };
                }

                x += count;
                position += 1 + unit;
                continue;
            }

            if (second == 1)
            {
                if (row < height - 1 || (row == height - 1 && x < width))
                    features.Add("bmp.rle=earlyEndOfBitmap");
                return values;
            }

            if (row >= height)
                throw new FormatError("run-length data after the last row");
            if (second == 0)
            {
                if (x < width)
                    features.Add("bmp.rle=earlyEndOfLine");
                row++;
                x = 0;
                position += 2;
            }
            else if (second == 2)
            {
                if (data.Length < position + 4)
                    throw new FormatError("truncated delta");
                int right = data[position + 2];
                int up = data[position + 3];
                if (x + right > width || row + up >= height)
                    throw new FormatError(Inv($"delta ({right}, {up}) outside the canvas"));
                x += right;
                row += up;
                position += 4;
                features.Add("bmp.rle=delta");
            }
            else
            {
                if (x + second > width)
                    throw new FormatError(Inv($"absolute run of {second} pixels past the end of the row"));
                var length = bits == 4 ? (second + 1) / 2 : second * unit;
                if (data.Length < position + 2 + length + length % 2)
                    throw new FormatError("truncated absolute run");
                for (var i = 0; i < second; i++)
                {
                    var at = position + 2;
                    values[row * width + x + i] = bits switch
                    {
                        8 => data[at + i],
                        4 => i % 2 == 0 ? data[at + i / 2] >> 4 : data[at + i / 2] & 0x0F,
                        _ => (data[at + i * 3 + 2] << 16) | (data[at + i * 3 + 1] << 8) | data[at + i * 3],
                    };
                }

                x += second;
                position += 2 + length + length % 2;
                features.Add("bmp.rle=absolute");
                if (length % 2 == 1)
                    features.Add("bmp.rle=absolutePadding");
            }
        }
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

        // 12 bytes: OS/2 1.x BITMAPCOREHEADER. 40, 52, 56, 108, 124: the Windows headers. Any other length from 16 to 64:
        // an OS/2 2.x BITMAPCOREHEADER2, which may stop after any field that follows the bit count
        var core = headerLength == 12;
        var windows = headerLength is 40 or 52 or 56 or 108 or 124;
        var os2 = !windows && headerLength is >= 16 and <= 64;
        if (!core && !windows && !os2)
            throw new FormatError(Inv($"undefined DIB header of {headerLength} bytes"));
        if (data.Length < 14 + headerLength)
            throw new FormatError("truncated DIB header");

        // The fields a short BITMAPCOREHEADER2 leaves out are zero
        var dibBytes = new byte[Math.Max(headerLength, 64)];
        data.AsSpan(14, (int)headerLength).CopyTo(dibBytes);
        ReadOnlySpan<byte> dib = dibBytes;
        int width, storedHeight, planes, bpp;
        long compression = BiRgb, colorsUsed = 0;
        int ppmX = 0, ppmY = 0;
        if (core)
        {
            width = Bytes.U16LE(dib, 4);
            storedHeight = Bytes.U16LE(dib, 6);
            planes = Bytes.U16LE(dib, 8);
            bpp = Bytes.U16LE(dib, 10);
        }
        else
        {
            width = Bytes.I32LE(dib, 4);
            storedHeight = Bytes.I32LE(dib, 8);
            planes = Bytes.U16LE(dib, 12);
            bpp = Bytes.U16LE(dib, 14);
            compression = Bytes.U32LE(dib, 16);
            ppmX = Bytes.I32LE(dib, 24);
            ppmY = Bytes.I32LE(dib, 28);
            colorsUsed = Bytes.U32LE(dib, 32);
        }

        if (width <= 0)
            throw new FormatError(Inv($"width {width}"));
        if (storedHeight == 0 || storedHeight == int.MinValue)
            throw new FormatError(Inv($"height {storedHeight}"));
        if (os2 && storedHeight < 0)
            throw new FormatError("the height of a BITMAPCOREHEADER2 is unsigned");
        if (planes != 1)
            throw new FormatError(Inv($"planes {planes}"));

        // The values 3 and 4 are BI_BITFIELDS and BI_JPEG in a Windows header, Huffman 1D and RLE24 in an OS/2 2.x header
        string storage;
        if (os2)
        {
            if (compression == BcaHuffman1D)
                throw new FormatError("unsupported compression: Huffman 1D");
            storage = compression switch
            {
                BiRgb => "rgb",
                BiRle8 => "rle8",
                BiRle4 => "rle4",
                BcaRle24 => "rle24",
                _ => throw new FormatError(Inv($"undefined compression {compression}")),
            };
        }
        else
        {
            if (compression is BiJpeg or BiPng)
                throw new FormatError(Inv($"unsupported compression {compression}"));
            storage = compression switch
            {
                BiRgb => "rgb",
                BiRle8 => "rle8",
                BiRle4 => "rle4",
                BiBitfields or BiAlphabitfields => "bitfields",
                _ => throw new FormatError(Inv($"undefined compression {compression}")),
            };
        }

        if (windows ? bpp is not (1 or 4 or 8 or 16 or 24 or 32) : bpp is not (1 or 4 or 8 or 24))
            throw new FormatError(Inv($"bit count {bpp}"));
        var bitfields = storage == "bitfields";
        if (bitfields && bpp is not (16 or 32))
            throw new FormatError(Inv($"masks with {bpp} bits per pixel"));
        var runLengthBits = storage switch
        {
            "rle8" => 8,
            "rle4" => 4,
            "rle24" => 24,
            _ => 0,
        };
        if (runLengthBits != 0 && bpp != runLengthBits)
            throw new FormatError(Inv($"{storage} with {bpp} bits per pixel"));
        if (runLengthBits != 0 && storedHeight < 0)
            throw new FormatError("top-down run-length rows");
        if (os2)
        {
            // usRecording (bottom-up is the only algorithm) and ulColorEncoding (RGB is the only encoding decoded)
            if (Bytes.U16LE(dib, 44) != 0)
                throw new FormatError("unsupported OS/2 recording algorithm");
            if (Bytes.U32LE(dib, 56) != 0)
                throw new FormatError("unsupported OS/2 color encoding");

            // usUnits: the resolution is in pixels per meter for the metric unit only
            if (Bytes.U16LE(dib, 40) != 0)
                (ppmX, ppmY) = (0, 0);
        }

        var height = Math.Abs(storedHeight);
        var topDown = storedHeight < 0;
        var indexed = bpp <= 8;
        var maximumEntries = indexed ? 1L << bpp : 256;
        if (colorsUsed > maximumEntries)
            throw new FormatError(Inv($"clrUsed {colorsUsed}"));
        var entries = colorsUsed != 0 ? colorsUsed : (indexed ? 1L << bpp : 0);
        var entryLength = core ? 3 : 4;
        var paletteFromOffset = false;
        if (core && indexed)
        {
            // BITMAPCOREHEADER has no color count: a short palette is only visible through the pixel data offset
            var available = (offsetBits - 14 - headerLength) / entryLength;
            if (available < entries)
            {
                if (available < 1)
                    throw new FormatError(Inv($"pixel offset {offsetBits} leaves no palette entry"));
                entries = available;
                paletteFromOffset = true;
            }
        }

        var masks = new uint[4];
        var position = 14 + headerLength;
        if (windows && headerLength >= 52)
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
            if (data.Length < position + entryLength * entries)
                throw new FormatError("truncated palette");
            for (var i = 0; i < entries; i++)
            {
                var at = (int)position + entryLength * i;
                palette.Add(new Px(data[at + 2], data[at + 1], data[at]));
            }

            position += entryLength * entries;
        }

        var headerEnd = position;
        if (offsetBits < headerEnd)
            throw new FormatError(Inv($"pixel offset {offsetBits} before the end of the header and palette ({headerEnd})"));
        position = offsetBits;
        var rowLength = BmpRowLength(width, bpp);
        var hasAlpha = masks[3] != 0;
        var rows = new List<List<Px>>();
        var features = new List<string>();
        if (runLengthBits != 0)
        {
            var values = RleRead(data, position, width, height, runLengthBits, features);
            for (var index = 0; index < height; index++)
            {
                var pixels = new List<Px>();
                for (var x = 0; x < width; x++)
                {
                    var value = values[index * width + x];
                    if (!indexed)
                    {
                        pixels.Add(new Px(value >> 16, (value >> 8) & 0xFF, value & 0xFF, 255));
                        continue;
                    }

                    if (value >= palette.Count)
                        throw new FormatError(Inv($"palette index {value} outside the {palette.Count}-entry palette"));
                    pixels.Add(palette[value].Append(255));
                }

                rows.Add(pixels);
            }
        }
        else if (data.Length < position + (long)rowLength * height)
        {
            throw new FormatError("truncated pixel data");
        }

        for (var index = 0; runLengthBits == 0 && index < height; index++)
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

        features.Add(Inv($"bmp.header={headerLength}"));
        features.Add(Inv($"bmp.bpp={bpp}"));
        features.Add("bmp.rowOrder=" + (topDown ? "top-down" : "bottom-up"));
        features.Add("bmp.compression=" + storage);
        if (indexed)
            features.Add(Inv($"bmp.palette={palette.Count}"));
        if (core && indexed)
            features.Add("bmp.paletteEntry=rgbtriple");
        if (paletteFromOffset)
            features.Add("bmp.paletteFromOffset");
        if (bpp is 16 or 32)
            features.Add("bmp.alphaMask=" + (hasAlpha ? "yes" : "no"));
        if (runLengthBits == 0 && rowLength != width * bpp / 8)
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

        BuildRunLength(c, palette5);
        BuildOs2(c, rgb, rgbImg, palette5, indexes8, indexes4, palette2, indexes1);

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

    // A palette whose first entry is not black: the pixels a run-length stream skips are palette index 0, not black
    private static readonly Px[] BmpSkipPalette = [new(200, 100, 50), new(255, 255, 255), new(10, 20, 30), new(0, 128, 255)];

    /// <summary>Checks that a run-length stream ends with its end-of-bitmap marker: a file with bytes after it would have a valid proper prefix.</summary>
    private static byte[] RleBody(RleStream stream)
    {
        var body = stream.ToArray();
        Py.Assert(body.Length >= 2 && body[^2] == 0 && body[^1] == 1, "the stream must end with the end-of-bitmap marker");
        return body;
    }

    private static void BuildRunLength(Corpus<FfmpegTools> c, Px[] palette5)
    {
        // BI_RLE8: encoded runs, absolute runs with and without a padding byte, an end of line after every row
        int[] indexes =
        [
            0, 1, 2, 3, 4, 2, 1,
            4, 4, 4, 4, 4, 3, 3,
            1, 1, 1, 0, 4, 2, 3,
        ];
        var stream = new RleStream()
            .Run(3, 1).Absolute(0, 4, 2, 3).EndOfLine()
            .Run(5, 4).Run(2, 3).EndOfLine()
            .Absolute(0, 1, 2, 3, 4).Run(1, 2).Run(1, 1).EndOfLine()
            .EndOfBitmap();
        var data = BmpFile(7, 3, 8, [], palette: palette5, compression: BiRle8, stream: RleBody(stream));
        AddFixture(c, Format, "bmp/rle8-runs-absolute", "bmp/rle8-runs-absolute.bmp", data, new Img(7, 3, "rgb", 8, indexes.Select(i => palette5[i])), "Rgb24", "Indexed", 8,
            "BuildRunLength", new Obj { ["width"] = 7, ["height"] = 3, ["compression"] = "BI_RLE8" },
            "BI_RLE8, every row complete: encoded runs (a count and one index), an absolute run of four indexes, an " +
            "absolute run of five indexes followed by its padding byte (absolute runs end on a 16-bit boundary), single " +
            "pixels stored as runs of one, an end-of-line escape after each row and the end-of-bitmap escape. The stream " +
            "stores the rows bottom-up.",
            required: ["bmp.compression=rle8", "bmp.rle=absolute", "bmp.rle=absolutePadding", "bmp.bpp=8"]);

        // BI_RLE8: every way of skipping pixels. The skipped pixels are palette index 0 (not black, not transparent)
        int[] skipped =
        [
            0, 0, 0, 0, 0, 0,
            0, 0, 0, 1, 1, 0,
            0, 0, 0, 0, 0, 0,
            3, 0, 0, 0, 0, 0,
            0, 0, 0, 2, 2, 2,
            1, 1, 0, 0, 0, 0,
        ];
        stream = new RleStream()
            .Run(2, 1).EndOfLine()
            .Delta(3, 0).Run(3, 2).EndOfLine()
            .Run(1, 3).Delta(2, 2).Run(2, 1)
            .EndOfBitmap();
        data = BmpFile(6, 6, 8, [], palette: BmpSkipPalette, compression: BiRle8, stream: RleBody(stream));
        AddFixture(c, Format, "bmp/rle8-delta-skips", "bmp/rle8-delta-skips.bmp", data, new Img(6, 6, "rgb", 8, skipped.Select(i => BmpSkipPalette[i])), "Rgb24", "Indexed", 8,
            "BuildRunLength", new Obj { ["width"] = 6, ["height"] = 6, ["compression"] = "BI_RLE8" },
            "BI_RLE8 with skipped pixels: an end of line after two of six pixels, a delta that moves three pixels right, " +
            "a delta that moves two pixels right and two rows up (one row is skipped entirely), and an end of bitmap " +
            "before the end of the fifth stored row, with the sixth never started. The pixels the stream does not store " +
            "are palette index 0, whose color is (200, 100, 50) here: they are neither black nor transparent.",
            required: ["bmp.compression=rle8", "bmp.rle=delta", "bmp.rle=earlyEndOfLine", "bmp.rle=earlyEndOfBitmap"]);

        // BI_RLE4: a run alternates the two 4-bit indexes of its byte; absolute runs pack two indexes per byte
        int[] indexes4 =
        [
            0, 1, 2, 3, 4,
            4, 3, 4, 3, 4,
            2, 2, 2, 2, 1,
        ];
        stream = new RleStream()
            .Run4(4, 2, 2).Run4(1, 1, 0).EndOfLine()
            .Run4(5, 4, 3).EndOfLine()
            .Absolute4(0, 1, 2, 3, 4).EndOfLine()
            .EndOfBitmap();
        data = BmpFile(5, 3, 4, [], palette: palette5, compression: BiRle4, stream: RleBody(stream));
        AddFixture(c, Format, "bmp/rle4-runs-absolute", "bmp/rle4-runs-absolute.bmp", data, new Img(5, 3, "rgb", 8, indexes4.Select(i => palette5[i])), "Rgb24", "Indexed", 4,
            "BuildRunLength", new Obj { ["width"] = 5, ["height"] = 3, ["compression"] = "BI_RLE4" },
            "BI_RLE4, odd width: a run of four pixels of one index, a run of one pixel (only the high-order index of its " +
            "byte is used), a run of five pixels alternating two indexes (4, 3, 4, 3, 4), and an absolute run of five " +
            "indexes stored in three bytes (the last low-order half is unused) followed by its padding byte.",
            required: ["bmp.compression=rle4", "bmp.rle=absolute", "bmp.rle=absolutePadding", "bmp.bpp=4", "bmp.width=odd"]);

        int[] skipped4 =
        [
            0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 2, 2,
            3, 1, 0, 0, 0, 0, 0,
            0, 1, 2, 3, 0, 0, 0,
        ];
        stream = new RleStream()
            .Delta(1, 0).Absolute4(1, 2, 3).EndOfLine()
            .Run4(2, 3, 1).Delta(3, 1).Run4(2, 2, 2)
            .EndOfBitmap();
        data = BmpFile(7, 4, 4, [], palette: BmpSkipPalette, compression: BiRle4, stream: RleBody(stream));
        AddFixture(c, Format, "bmp/rle4-delta-skips", "bmp/rle4-delta-skips.bmp", data, new Img(7, 4, "rgb", 8, skipped4.Select(i => BmpSkipPalette[i])), "Rgb24", "Indexed", 4,
            "BuildRunLength", new Obj { ["width"] = 7, ["height"] = 4, ["compression"] = "BI_RLE4" },
            "BI_RLE4 with skipped pixels: a delta that starts a row at its second pixel (an absolute run of three indexes " +
            "then starts on an odd column and needs no padding byte), an early end of line, a delta that moves three " +
            "pixels right and one row up, and an end of bitmap that leaves the last stored row unwritten. The skipped " +
            "pixels are palette index 0, whose color is (200, 100, 50).",
            required: ["bmp.compression=rle4", "bmp.rle=delta", "bmp.rle=earlyEndOfLine", "bmp.rle=earlyEndOfBitmap", "bmp.rle=absolute"]);
    }

    private static void BuildOs2(Corpus<FfmpegTools> c, List<List<Px>> rgb, Img rgbImg, Px[] palette5, int[] indexes8, int[] indexes4, Px[] palette2, int[] indexes1)
    {
        // OS/2 1.x: BITMAPCOREHEADER, 16-bit dimensions, RGBTRIPLE palette entries and no color count
        var data = BmpCoreFile(3, 2, 24, BmpPack24(rgb, 3));
        AddFixture(c, Format, "bmp/os2-core-bgr24", "bmp/os2-core-bgr24.bmp", data, rgbImg, "Rgb24", "Rgb", 8,
            "BuildOs2", new Obj { ["width"] = 3, ["height"] = 2, ["header"] = 12 },
            "OS/2 1.x BITMAPCOREHEADER (12 bytes): 16-bit unsigned width and height, planes and bit count, nothing else. " +
            "The rows are those of bmp/bgr24-bottom-up-padding, always bottom-up.",
            required: ["bmp.header=12", "bmp.bpp=24", "bmp.rowPadding"]);

        // The palette after a BITMAPCOREHEADER has the full size of the bit count: 16 entries of three bytes
        Px[] palette16 = [.. palette5, .. Enumerable.Range(5, 11).Select(i => new Px(i * 16, 255 - i * 16, i * 7))];
        var img4 = new Img(5, 3, "rgb", 8, indexes4.Select(i => palette16[i]));
        data = BmpCoreFile(5, 3, 4, BmpPackIndexed(ChunkRows(indexes4, 5), 5, 4), palette16);
        AddFixture(c, Format, "bmp/os2-core-palette4", "bmp/os2-core-palette4.bmp", data, img4, "Rgb24", "Indexed", 4,
            "BuildOs2", new Obj { ["width"] = 5, ["height"] = 3, ["bitsPerPixel"] = 4, ["paletteEntries"] = 16 },
            "OS/2 1.x with 4-bit indexes: the 16 palette entries are RGBTRIPLE (blue, green, red, three bytes each, no " +
            "reserved byte), so the pixel data starts at 14 + 12 + 48. Reading four bytes per entry shifts every color.",
            required: ["bmp.header=12", "bmp.bpp=4", "bmp.palette=16", "bmp.paletteEntry=rgbtriple"]);

        var img1 = new Img(9, 2, "rgb", 8, indexes1.Select(i => palette2[i]));
        data = BmpCoreFile(9, 2, 1, BmpPackIndexed(ChunkRows(indexes1, 9), 9, 1), palette2);
        AddFixture(c, Format, "bmp/os2-core-palette1", "bmp/os2-core-palette1.bmp", data, img1, "Rgb24", "Indexed", 1,
            "BuildOs2", new Obj { ["width"] = 9, ["height"] = 2, ["bitsPerPixel"] = 1 },
            "OS/2 1.x with 1-bit indexes and two RGBTRIPLE entries that are not black and white.",
            required: ["bmp.header=12", "bmp.bpp=1", "bmp.palette=2", "bmp.paletteEntry=rgbtriple"]);

        // A BITMAPCOREHEADER cannot declare a short palette: the pixel data offset is the only clue
        var img8 = new Img(7, 3, "rgb", 8, indexes8.Select(i => palette5[i]));
        data = BmpCoreFile(7, 3, 8, BmpPackIndexed(ChunkRows(indexes8, 7), 7, 8), palette5, gap: 1);
        AddFixture(c, Format, "bmp/os2-core-palette8-short", "bmp/os2-core-palette8-short.bmp", data, img8, "Rgb24", "Indexed", 8,
            "BuildOs2", new Obj { ["width"] = 7, ["height"] = 3, ["paletteEntries"] = 5, ["gap"] = 1 },
            "OS/2 1.x with 8-bit indexes and five palette entries instead of 256, as some files in the wild store: the " +
            "header has no color count, so the number of entries is what fits before bfOffBits ((42 - 26) / 3 = 5, with " +
            "one unused byte before the pixel data). An index outside these five entries would be malformed.",
            required: ["bmp.header=12", "bmp.bpp=8", "bmp.palette=5", "bmp.paletteFromOffset", "bmp.gapBeforePixels"]);

        // OS/2 2.x: BITMAPCOREHEADER2, complete (64 bytes) and cut after its bit count (16 bytes)
        var resolutionImg = new Img(2, 2, "rgb", 8, [new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(250, 251, 252)]);
        data = BmpOs2V2File(64, 2, 2, 24, Bytes.Concat(BmpPack24(ChunkRows(resolutionImg.Pixels, 2), 2).AsEnumerable().Reverse()), pixelsPerMeter: (2835, 5670));
        AddFixture(c, Format, "bmp/os2-v2-header64-bgr24", "bmp/os2-v2-header64-bgr24.bmp", data, resolutionImg, "Rgb24", "Rgb", 8,
            "BuildOs2", new Obj { ["width"] = 2, ["height"] = 2, ["header"] = 64, ["pixelsPerMeter"] = new List<object?> { 2835, 5670 } },
            "OS/2 2.x BITMAPCOREHEADER2, all 64 bytes: its first 40 bytes are laid out like BITMAPINFOHEADER, and the " +
            "resolution is in pixels per meter because usUnits is 0 (metric), so it decodes like bmp/bgr24-resolution.",
            required: ["bmp.header=64", "bmp.bpp=24", "bmp.resolution"]);

        data = BmpOs2V2File(16, 5, 3, 4, Bytes.Concat(BmpPackIndexed(ChunkRows(indexes4, 5), 5, 4).AsEnumerable().Reverse()), palette16);
        AddFixture(c, Format, "bmp/os2-v2-header16-palette4", "bmp/os2-v2-header16-palette4.bmp", data, img4, "Rgb24", "Indexed", 4,
            "BuildOs2", new Obj { ["width"] = 5, ["height"] = 3, ["header"] = 16, ["paletteEntries"] = 16 },
            "The shortest BITMAPCOREHEADER2: 16 bytes, cut after the bit count. Every field it leaves out is zero " +
            "(uncompressed, a full 16-entry palette, no resolution), and its palette entries are RGBQUAD (four bytes), " +
            "unlike those of the 12-byte header.",
            required: ["bmp.header=16", "bmp.bpp=4", "bmp.palette=16"],
            crossCheck: "differs",
            crossCheckNotes: "FFmpeg does not decode a BITMAPCOREHEADER2 shorter than 40 bytes.");

        // BCA_RLE24: the run-length scheme of BI_RLE8 with three-byte pixels, only defined by the OS/2 2.x header
        Px a = new(255, 0, 0), b = new(0, 255, 0), d = new(0, 0, 255), e = new(10, 20, 30), f = new(200, 150, 100);
        Px g = new(1, 2, 254), h = new(90, 80, 70), i = new(33, 66, 99), j = new(250, 240, 230), black = new(0, 0, 0);
        Px[] pixels24 =
        [
            f, g, h, i, j,
            black, e, e, e, black,
            a, a, b, d, e,
        ];
        var stream = new RleStream()
            .Run24(2, a).Absolute24(b, d, e).EndOfLine()
            .Delta(1, 0).Run24(3, e).EndOfLine()
            .Absolute24(f, g, h, i).Run24(1, j)
            .EndOfBitmap();
        data = BmpOs2V2File(64, 5, 3, 24, RleBody(stream), compression: BcaRle24);
        AddFixture(c, Format, "bmp/os2-v2-rle24", "bmp/os2-v2-rle24.bmp", data, new Img(5, 3, "rgb", 8, pixels24), "Rgb24", "Rgb", 8,
            "BuildOs2", new Obj { ["width"] = 5, ["height"] = 3, ["header"] = 64, ["compression"] = "BCA_RLE24" },
            "OS/2 2.x 24-bit run-length encoding (compression 4, which a Windows header would read as BI_JPEG): runs of " +
            "one blue, green, red triplet, an absolute run of three triplets (nine bytes and a padding byte), an absolute " +
            "run of four triplets (no padding), a delta and an early end of line. There is no palette, so the two skipped " +
            "pixels are black.",
            required: ["bmp.header=64", "bmp.compression=rle24", "bmp.rle=absolute", "bmp.rle=absolutePadding", "bmp.rle=delta", "bmp.rle=earlyEndOfLine"],
            crossCheck: "differs",
            crossCheckNotes: "FFmpeg does not decode BCA_RLE24.");
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

        // Run-length streams: header rules, then the stream itself
        var runLength = read["bmp/rle8-runs-absolute"];
        AddError(c, Format, "invalid/bmp/rle8-top-down", "invalid/bmp/rle8-top-down.bmp", Splice(runLength, 22, I32(-3)),
            "InvalidImageContentException", "BI_RLE8 with a negative biHeight: a run-length encoded bitmap is always bottom-up.");
        AddError(c, Format, "invalid/bmp/rle8-bit-count-4", "invalid/bmp/rle8-bit-count-4.bmp", Splice(runLength, 28, U16(4)),
            "InvalidImageContentException", "BI_RLE8 with biBitCount 4: BI_RLE8 only stores 8-bit indexes (BI_RLE4 stores the 4-bit ones).");

        Px[] palette = [new(0, 0, 0), new(255, 255, 255), new(220, 30, 40), new(30, 220, 40), new(40, 30, 220)];
        byte[] Rle8(RleStream stream) => BmpFile(7, 3, 8, [], palette: palette, compression: BiRle8, stream: stream.ToArray());
        AddError(c, Format, "invalid/bmp/rle8-run-past-row-end", "invalid/bmp/rle8-run-past-row-end.bmp",
            Rle8(new RleStream().Run(5, 1).Run(3, 2).EndOfLine().EndOfBitmap()),
            "InvalidImageContentException", "A run of three pixels starts at the sixth pixel of a seven-pixel row: a run " +
            "never wraps to the next row and is never clamped.");
        AddError(c, Format, "invalid/bmp/rle8-data-after-last-row", "invalid/bmp/rle8-data-after-last-row.bmp",
            Rle8(new RleStream().Run(7, 1).EndOfLine().Run(7, 2).EndOfLine().Run(7, 3).EndOfLine().Run(1, 4).EndOfBitmap()),
            "InvalidImageContentException", "A run follows the end of line of the third and last row: only the " +
            "end-of-bitmap escape may follow the last row.");
        AddError(c, Format, "invalid/bmp/rle8-missing-end-of-bitmap", "invalid/bmp/rle8-missing-end-of-bitmap.bmp",
            Bytes.Slice(runLength, null, -2),
            "InvalidImageContentException", "bmp/rle8-runs-absolute without its last two bytes: every row is complete, " +
            "but the input ends before the end-of-bitmap escape, so it is truncated.");
        AddError(c, Format, "invalid/bmp/rle8-palette-index-out-of-range", "invalid/bmp/rle8-palette-index-out-of-range.bmp",
            Rle8(new RleStream().Run(7, 1).EndOfLine().Run(3, 5).EndOfBitmap()),
            "InvalidImageContentException", "A run uses the palette index 5, outside the five-entry palette (0 to 4).");
        AddError(c, Format, "invalid/bmp/rle4-delta-outside-canvas", "invalid/bmp/rle4-delta-outside-canvas.bmp",
            BmpFile(5, 3, 4, [], palette: palette, compression: BiRle4, stream: new RleStream().Run4(2, 1, 2).Delta(0, 3).EndOfBitmap().ToArray()),
            "InvalidImageContentException", "A delta moves three rows up from the first of three rows: the current " +
            "position may not leave the canvas.");

        // OS/2 headers
        var core = read["bmp/os2-core-bgr24"];
        AddError(c, Format, "invalid/bmp/os2-core-bit-count-16", "invalid/bmp/os2-core-bit-count-16.bmp", Splice(core, 24, U16(16)),
            "InvalidImageContentException", "A BITMAPCOREHEADER with bcBitCount 16: OS/2 bitmaps store 1, 4, 8 or 24 bits per pixel.");
        AddError(c, Format, "invalid/bmp/os2-core-offset-inside-palette", "invalid/bmp/os2-core-offset-inside-palette.bmp",
            Splice(read["bmp/os2-core-palette4"], 10, U32(28)),
            "InvalidImageContentException", "A 4-bit BITMAPCOREHEADER file whose bfOffBits is 28: two bytes after the " +
            "26 bytes of the headers, less than one three-byte palette entry.");
        AddError(c, Format, "invalid/bmp/os2-huffman-1d", "invalid/bmp/os2-huffman-1d.bmp",
            BmpOs2V2File(64, 9, 2, 1, new byte[8], [new(16, 32, 48), new(240, 224, 208)], compression: BcaHuffman1D),
            "UnsupportedImageFeatureException", "An OS/2 2.x header with compression 3, which is CCITT Group 3 " +
            "one-dimensional (Huffman 1D) coding there, not BI_BITFIELDS: recognized and rejected.",
            feature: "Compression: Huffman 1D", features: ["bmp.unsupported=huffman1d"]);
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
