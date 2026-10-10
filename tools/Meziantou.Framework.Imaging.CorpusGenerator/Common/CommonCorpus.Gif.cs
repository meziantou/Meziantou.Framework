using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>Statistics of an LZW encoding (lzw_encode).</summary>
internal sealed class LzwStats
{
    public int MaxCodeSize { get; set; }

    public int ClearCodes { get; set; }

    public bool TableFull { get; set; }

    public int Codes { get; set; }
}

/// <summary>A Graphic Control Extension: disposal, delay (hundredths) and the optional transparent index.</summary>
internal sealed record GifGce(int Disposal, int Delay, int? TransparentIndex);

/// <summary>One image of a parsed GIF (gif_inspect).</summary>
internal sealed record GifFrameInfo((int X, int Y, int W, int H) Rect, int Local, bool Interlaced, GifGce? Gce, int MinCodeSize, byte[] Data);

/// <summary>Parsed GIF container (gif_inspect).</summary>
internal sealed class GifInfo
{
    public int Width { get; set; }

    public int Height { get; set; }

    public List<GifFrameInfo> Frames { get; } = [];

    public int? Loop { get; set; }

    public string? LoopExtension { get; set; }

    public HashSet<string> Extensions { get; } = new(StringComparer.Ordinal);
}

internal static partial class CommonCorpus
{
    // -----------------------------------------------------------------------------------------------------------------
    // GIF writer (hand-authored inputs) with an LZW encoder covering clear codes, full tables and deferred clears.
    // -----------------------------------------------------------------------------------------------------------------

    public static (byte[] Data, LzwStats Stats) LzwEncode(IReadOnlyList<int> indices, int minCodeSize, bool deferredClear = false)
    {
        int clear = 1 << minCodeSize, eoi = (1 << minCodeSize) + 1;
        var stats = new LzwStats { MaxCodeSize = minCodeSize + 1 };
        var bits = new List<(int Code, int Size)>();

        void Emit(int code, int size)
        {
            stats.Codes++;
            stats.MaxCodeSize = Math.Max(stats.MaxCodeSize, size);
            bits.Add((code, size));
        }

        var table = new Dictionary<(int, int), int>();
        var nextCode = eoi + 1;
        var codeSize = minCodeSize + 1;
        Emit(clear, codeSize);
        stats.ClearCodes++;
        var prefix = indices[0];
        foreach (var k in indices.Skip(1))
        {
            var key = (prefix, k);
            if (table.TryGetValue(key, out var existing))
            {
                prefix = existing;
                continue;
            }

            Emit(prefix, codeSize);
            if (nextCode < 4096)
            {
                table[key] = nextCode;
                nextCode++;
                if (nextCode > (1 << codeSize) && codeSize < 12)
                    codeSize++;
            }
            else
            {
                stats.TableFull = true;
                if (!deferredClear)
                {
                    Emit(clear, codeSize);
                    stats.ClearCodes++;
                    table.Clear();
                    nextCode = eoi + 1;
                    codeSize = minCodeSize + 1;
                }
            }

            prefix = k;
        }

        Emit(prefix, codeSize);
        if (nextCode >= (1 << codeSize) && codeSize < 12)
            codeSize++; // decoders add a table entry for the last data code too: the end code uses their code size
        Emit(eoi, codeSize);
        var output = new List<byte>();
        long accumulator = 0;
        var count = 0;
        foreach (var (code, size) in bits)
        {
            accumulator |= (long)code << count;
            count += size;
            while (count >= 8)
            {
                output.Add((byte)(accumulator & 0xFF));
                accumulator >>= 8;
                count -= 8;
            }
        }

        if (count != 0)
            output.Add((byte)(accumulator & 0xFF));
        return ([.. output], stats);
    }

    public static byte[] GifSubBlocks(byte[] data)
    {
        var output = new ByteBuilder();
        for (var i = 0; i < data.Length; i += 255)
        {
            var block = Bytes.Slice(data, i, i + 255);
            output.U8(block.Length).Bytes(block);
        }

        return output.U8(0).ToArray();
    }

    public static (int SizeField, byte[] Palette) GifPaletteBytes(IReadOnlyList<Px> colors)
    {
        var sizeField = Math.Max(0, Py.BitLength(colors.Count - 1) - 1);
        var entries = 2 << sizeField;
        var output = new ByteBuilder();
        foreach (var c in colors.Concat(Enumerable.Repeat(new Px(0, 0, 0), entries - colors.Count)))
            foreach (var v in c)
                output.U8(v);
        return (sizeField, output.ToArray());
    }

    public static byte[] GifNetscape(int loopCount) =>
        new ByteBuilder().U8(0x21).U8(0xFF).U8(0x0B).Ascii("NETSCAPE2.0").U8(0x03).U8(0x01).U16LE(loopCount).U8(0).ToArray();

    public static byte[] GifGce(int disposal, int delay, int? transparentIndex = null)
    {
        var packed = (disposal << 2) | (transparentIndex is not null ? 1 : 0);
        return new ByteBuilder().U8(0x21).U8(0xF9).U8(0x04).U8(packed).U16LE(delay).U8(transparentIndex ?? 0).U8(0).ToArray();
    }

    public static List<int> GifInterlaceOrder(int height) =>
        [.. new[] { (0, 8), (4, 8), (2, 4), (1, 2) }.SelectMany(p => Range(p.Item1, height, p.Item2))];

    public static (byte[] Block, LzwStats Stats) GifImage(int x, int y, int width, int height, IReadOnlyList<int> indices, int minCodeSize, IReadOnlyList<Px>? localPalette = null,
        bool interlaced = false, bool deferredClear = false)
    {
        var rows = Enumerable.Range(0, height).Select(r => indices.Skip(r * width).Take(width).ToList()).ToList();
        var order = interlaced ? GifInterlaceOrder(height) : Range(0, height);
        var ordered = order.SelectMany(r => rows[r]).ToList();
        var packed = interlaced ? 0x40 : 0;
        byte[] palette = [];
        if (localPalette is not null)
        {
            (var sizeField, palette) = GifPaletteBytes(localPalette);
            packed |= 0x80 | sizeField;
        }

        var (data, stats) = LzwEncode(ordered, minCodeSize, deferredClear);
        var block = new ByteBuilder().U8(0x2C).U16LE(x).U16LE(y).U16LE(width).U16LE(height).U8(packed).Bytes(palette);
        return (block.U8(minCodeSize).Bytes(GifSubBlocks(data)).ToArray(), stats);
    }

    /// <summary>version is "GIF87a" or "GIF89a".</summary>
    public static byte[] GifFile(string version, int width, int height, IReadOnlyList<Px> globalPalette, int backgroundIndex, IEnumerable<byte[]> blocks)
    {
        var (sizeField, palette) = GifPaletteBytes(globalPalette);
        var output = new ByteBuilder().Ascii(version).U16LE(width).U16LE(height).U8(0x80 | 0x70 | sizeField).U8(backgroundIndex).U8(0).Bytes(palette);
        foreach (var block in blocks)
            output.Bytes(block);
        return output.U8(0x3B).ToArray();
    }

    public static (GifInfo Info, List<string> Features) GifInspect(byte[] data)
    {
        var version = Bytes.Latin1(data.AsSpan(0, 6));
        int width = Bytes.U16LE(data, 6), height = Bytes.U16LE(data, 8), packed = data[10];
        var offset = 13;
        var features = new List<string> { "gif.version=" + version[3..] };
        if ((packed & 0x80) != 0)
        {
            var entries = 2 << (packed & 7);
            features.Add("gif.globalPalette=" + Str(entries));
            offset += 3 * entries;
        }

        var info = new GifInfo { Width = width, Height = height };
        GifGce? gce = null;
        while (true)
        {
            var introducer = data[offset];
            if (introducer == 0x3B)
                break;
            if (introducer == 0x21)
            {
                var label = data[offset + 1];
                offset += 2;
                var body = new List<byte>();
                var first = true;
                while (data[offset] != 0)
                {
                    var size = data[offset];
                    var chunk = Bytes.Slice(data, offset + 1, offset + 1 + size);
                    if (label == 0xFF && !first && body.Count >= 11)
                    {
                        var identifier = Bytes.Latin1([.. body.Take(11)]);
                        if ((identifier is "NETSCAPE2.0" or "ANIMEXTS1.0") && chunk[0] == 1)
                        {
                            // The last loop extension wins (FFmpeg, Apple ImageIO)
                            info.Loop = Bytes.U16LE(chunk, 1);
                            info.LoopExtension = identifier;
                        }
                    }

                    body.AddRange(chunk);
                    first = false;
                    offset += 1 + size;
                }

                offset++;
                if (label == 0xF9)
                    gce = new GifGce((body[0] >> 2) & 7, Bytes.U16LE([.. body], 1), (body[0] & 1) != 0 ? body[3] : null);
                info.Extensions.Add(label switch
                {
                    0xF9 => "graphicControl",
                    0xFF => "application",
                    0xFE => "comment",
                    0x01 => "plainText",
                    _ => "0x" + label.ToString("X2", System.Globalization.CultureInfo.InvariantCulture),
                });
                continue;
            }

            Py.Assert(introducer == 0x2C, "0x" + introducer.ToString("x", System.Globalization.CultureInfo.InvariantCulture));
            int x = Bytes.U16LE(data, offset + 1), y = Bytes.U16LE(data, offset + 3), w = Bytes.U16LE(data, offset + 5), h = Bytes.U16LE(data, offset + 7), flags = data[offset + 9];
            offset += 10;
            var local = 0;
            if ((flags & 0x80) != 0)
            {
                local = 2 << (flags & 7);
                offset += 3 * local;
            }

            var minCodeSize = data[offset];
            offset++;
            var lzw = new List<byte>();
            while (data[offset] != 0)
            {
                lzw.AddRange(Bytes.Slice(data, offset + 1, offset + 1 + data[offset]));
                offset += 1 + data[offset];
            }

            offset++;
            info.Frames.Add(new GifFrameInfo((x, y, w, h), local, (flags & 0x40) != 0, gce, minCodeSize, [.. lzw]));
            gce = null;
        }

        var frames = info.Frames;
        if (info.Loop is not null)
            features.Add((info.LoopExtension == "ANIMEXTS1.0" ? "gif.animextsLoop=" : "gif.netscapeLoop=") + Str(info.Loop.Value));
        if (frames.Any(f => f.Local != 0))
            features.Add("gif.localPalette");
        if (frames.Any(f => f.Interlaced))
            features.Add("gif.interlaced");
        var disposals = frames.Where(f => f.Gce is not null).Select(f => f.Gce!.Disposal).Distinct().Order().ToList();
        if (disposals.Count > 0)
            features.Add("gif.disposal=" + string.Join(',', disposals));
        if (frames.Any(f => f.Gce is not null && f.Gce.TransparentIndex is not null))
            features.Add("gif.transparency");
        if (frames.Any(f => f.Rect != (0, 0, width, height)))
            features.Add("gif.subRectangles");
        features.Add("gif.minCodeSize=" + string.Join(',', frames.Select(f => f.MinCodeSize).Distinct().Order()));
        foreach (var extension in info.Extensions.Where(e => e != "graphicControl").Order(StringComparer.Ordinal))
            features.Add("gif.extension=" + extension);
        return (info, [.. features.Order(StringComparer.Ordinal)]);
    }
}
