using System.Text;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A text entry: keyword, value, language tag and translated keyword.</summary>
internal sealed record TextEntry(string Keyword, string Value, string? LanguageTag, string? TranslatedKeyword);

/// <summary>Metadata parsed from an encoded input.</summary>
internal sealed class ParsedMetadata
{
    public Obj? Resolution { get; set; }

    public byte[]? Icc { get; set; }

    public byte[]? Exif { get; set; }

    public byte[]? Xmp { get; set; }

    public List<TextEntry> Text { get; } = [];
}

/// <summary>A TIFF directory entry: tag, field type (2 ASCII, 3 SHORT, 4 LONG) and values.</summary>
internal sealed record TiffEntry(int Tag, int Kind, IReadOnlyList<long> Values);

internal static partial class CommonCorpus
{
    // -----------------------------------------------------------------------------------------------------------------
    // Metadata payloads (hand-built test data, CC0) and metadata inspection. Expected metadata is parsed from the encoded
    // bytes of every input (never trusted from the builder parameters) and recorded with exact hashes of the payloads that
    // decoders must preserve byte for byte (EXIF from the TIFF header, the uncompressed ICC profile, the XMP packet).
    // -----------------------------------------------------------------------------------------------------------------

    public static byte[] S15F16(double value) => new ByteBuilder().I32BE(Py.Round(value * 65536)).ToArray();

    /// <summary>A small, structurally valid ICC v4.3 display profile ("GRAY" or "RGB ") built by hand for the tests. It is
    /// labeled "Lorem ipsum" and is never applied by the library (profiles are preserved, not interpreted).</summary>
    public static byte[] IccProfile(string colorSpace)
    {
        static byte[] TagXyz(double x, double y, double z) => Bytes.Concat(Bytes.Ascii("XYZ \0\0\0\0"), S15F16(x), S15F16(y), S15F16(z));

        static byte[] TagMluc(string text)
        {
            var encoded = Encoding.BigEndianUnicode.GetBytes(text);
            return new ByteBuilder().Ascii("mluc\0\0\0\0").U32BE(1).U32BE(12).Ascii("enUS").U32BE(encoded.Length).U32BE(28).Bytes(encoded).ToArray();
        }

        var gamma = Bytes.Concat(new ByteBuilder().Ascii("para\0\0\0\0").U16BE(0).U16BE(0).ToArray(), S15F16(2.2));
        var name = "Lorem ipsum (" + (colorSpace == "GRAY" ? "gray" : "rgb") + ")";
        var tags = new List<(string Signature, byte[] Body)> { ("desc", TagMluc(name)), ("cprt", TagMluc("CC0-1.0")), ("wtpt", TagXyz(0.9642, 1.0, 0.8249)) };
        if (colorSpace == "GRAY")
        {
            tags.Add(("kTRC", gamma));
        }
        else
        {
            tags.AddRange([("rXYZ", TagXyz(0.4361, 0.2225, 0.0139)), ("gXYZ", TagXyz(0.3851, 0.7169, 0.0971)), ("bXYZ", TagXyz(0.1431, 0.0606, 0.7141)),
                ("rTRC", gamma), ("gTRC", gamma), ("bTRC", gamma)]);
        }

        var tableSize = 4 + 12 * tags.Count;
        var offset = 128 + tableSize;
        var table = new ByteBuilder().U32BE(tags.Count);
        var data = new ByteBuilder();
        var shared = new List<(byte[] Body, int Offset)>();
        foreach (var (signature, body) in tags)
        {
            var existing = shared.FindIndex(s => s.Body.AsSpan().SequenceEqual(body));
            if (existing >= 0)
            {
                // identical TRCs share their data, as allowed by the ICC specification
                table.Ascii(signature).U32BE(shared[existing].Offset).U32BE(body.Length);
                continue;
            }

            while ((offset + data.Length) % 4 != 0)
                data.U8(0);
            shared.Add((body, offset + data.Length));
            table.Ascii(signature).U32BE(offset + data.Length).U32BE(body.Length);
            data.Bytes(body);
        }

        var total = 128 + tableSize + data.Length;
        var header = new ByteBuilder().U32BE(total).Zeros(4).U32BE(0x04300000).Ascii("mntr").Ascii(colorSpace).Ascii("XYZ ");
        header.U16BE(2026).U16BE(1).U16BE(1).U16BE(0).U16BE(0).U16BE(0).Ascii("acsp").Zeros(4).U32BE(0).Zeros(8);
        header.Zeros(8).U32BE(0).Bytes(S15F16(0.9642)).Bytes(S15F16(1.0)).Bytes(S15F16(0.8249)).Zeros(4);
        header.Zeros(16).Zeros(28);
        Py.Assert(header.Length == 128);
        return Bytes.Concat(header.ToArray(), table.ToArray(), data.ToArray());
    }

    /// <summary>Serializes an IFD; values longer than 4 bytes are placed from
    /// <paramref name="dataOffset"/>. Returns (ifd, data). <paramref name="littleEndian"/> selects the byte order.</summary>
    public static (byte[] Ifd, byte[] Data) TiffIfd(IEnumerable<TiffEntry> entries, bool littleEndian, int nextOffset, int dataOffset)
    {
        var sorted = entries.OrderBy(e => e.Tag).ThenBy(e => e.Kind).ToList();
        var ifd = new ByteBuilder();
        U16(ifd, sorted.Count);
        var data = new ByteBuilder();
        foreach (var (tag, kind, values) in sorted)
        {
            var payload = new ByteBuilder();
            foreach (var value in values)
            {
                if (kind == 3)
                    U16(payload, (int)value);
                else if (kind == 4)
                    U32(payload, value);
                else
                    payload.U8((int)value);
            }

            var payloadBytes = payload.ToArray();
            U16(ifd, tag);
            U16(ifd, kind);
            U32(ifd, values.Count);
            if (payloadBytes.Length <= 4)
            {
                ifd.Bytes(payloadBytes).Zeros(4 - payloadBytes.Length);
            }
            else
            {
                U32(ifd, dataOffset + data.Length);
                data.Bytes(payloadBytes);
                if (payloadBytes.Length % 2 != 0)
                    data.U8(0);
            }
        }

        U32(ifd, nextOffset);
        return (ifd.ToArray(), data.ToArray());

        void U16(ByteBuilder b, int value)
        {
            if (littleEndian)
                b.U16LE(value);
            else
                b.U16BE(value);
        }

        void U32(ByteBuilder b, long value)
        {
            if (littleEndian)
                b.U32LE(value);
            else
                b.U32BE(value);
        }
    }

    /// <summary>TIFF-structured EXIF (no "Exif\0\0" prefix): IFD0 (Orientation, optional Software, Exif IFD pointer), an
    /// optional Exif IFD with PixelXDimension (SHORT) / PixelYDimension (LONG), and an optional IFD1 thumbnail directory
    /// whose JPEGInterchangeFormat points to placeholder bytes (not a decodable JPEG; decoders never decode thumbnails).</summary>
    public static byte[] ExifTiff(bool littleEndian, int orientation, (int Width, int Height)? pixelSize = null, byte[]? thumbnail = null, string? software = null)
    {
        var order = littleEndian ? Bytes.Ascii("II*\0") : Bytes.Ascii("MM\0*");
        var ifd0Entries = new List<TiffEntry> { new(0x0112, 3, [orientation]) };
        var hasSoftware = !string.IsNullOrEmpty(software);
        var hasThumbnail = thumbnail is { Length: > 0 };
        if (hasSoftware)
            ifd0Entries.Add(new(0x0131, 2, [.. Bytes.Ascii(software!).Select(b => (long)b), 0]));
        var ifd0Count = ifd0Entries.Count + (pixelSize is not null ? 1 : 0);
        var ifd0Size = 2 + 12 * ifd0Count + 4;
        var softwareSize = hasSoftware ? (software!.Length + 1 + 1) / 2 * 2 : 0;
        var exifOffset = 8 + ifd0Size + softwareSize;
        var exifSize = pixelSize is not null ? 2 + 12 * 2 + 4 : 0;
        var ifd1Offset = exifOffset + exifSize;
        var ifd1Size = hasThumbnail ? 2 + 12 * 2 + 4 : 0;
        var thumbnailOffset = ifd1Offset + ifd1Size;
        if (pixelSize is not null)
            ifd0Entries.Add(new(0x8769, 4, [exifOffset]));
        var (ifd0, ifd0Data) = TiffIfd(ifd0Entries, littleEndian, hasThumbnail ? ifd1Offset : 0, 8 + ifd0Size);
        var output = new ByteBuilder().Bytes(order);
        if (littleEndian)
            output.U32LE(8);
        else
            output.U32BE(8);
        output.Bytes(ifd0).Bytes(ifd0Data);
        Py.Assert(output.Length == exifOffset);
        if (pixelSize is { } size)
        {
            var (exifIfd, _) = TiffIfd([new(0xA002, 3, [size.Width]), new(0xA003, 4, [size.Height])], littleEndian, 0, 0);
            output.Bytes(exifIfd);
        }

        if (hasThumbnail)
        {
            var (ifd1, _) = TiffIfd([new(0x0201, 4, [thumbnailOffset]), new(0x0202, 4, [thumbnail!.Length])], littleEndian, 0, 0);
            output.Bytes(ifd1).Bytes(thumbnail);
        }

        return output.ToArray();
    }

    public static int? ExifReadOrientation(byte[] tiff)
    {
        var little = tiff[0] == (byte)'I' && tiff[1] == (byte)'I';
        int U16(int at) => little ? Bytes.U16LE(tiff, at) : Bytes.U16BE(tiff, at);
        long U32(int at) => little ? Bytes.U32LE(tiff, at) : Bytes.U32BE(tiff, at);
        var offset = (int)U32(4);
        var count = U16(offset);
        for (var i = 0; i < count; i++)
        {
            int tag = U16(offset + 2 + 12 * i), kind = U16(offset + 4 + 12 * i);
            var n = U32(offset + 6 + 12 * i);
            if (tag == 0x0112 && kind == 3 && n == 1)
                return U16(offset + 10 + 12 * i);
        }

        return null;
    }

    public static Obj ProfileRecord(byte[] data) => new() { ["sha256"] = Bytes.Sha256Hex(data), ["length"] = data.Length };

    /// <summary>Splits at the first NUL byte (bytes.split(b"\0", 1)).</summary>
    private static (byte[] Head, byte[] Tail) SplitNul(byte[] data)
    {
        var index = Array.IndexOf(data, (byte)0);
        Py.Assert(index >= 0, "missing NUL separator");
        return (data[..index], data[(index + 1)..]);
    }

    private static Obj Resolution(object horizontal, object vertical, string unit, long x, long y) => new()
    {
        ["horizontalDpi"] = horizontal,
        ["verticalDpi"] = vertical,
        ["encoded"] = new Obj { ["unit"] = unit, ["x"] = x, ["y"] = y },
    };

    public static ParsedMetadata PngMetadata(byte[] data)
    {
        var offset = 8;
        var result = new ParsedMetadata();
        while (offset < data.Length)
        {
            var length = (int)Bytes.U32BE(data, offset);
            var kind = Bytes.Latin1(data.AsSpan(offset + 4, 4));
            var body = Bytes.Slice(data, offset + 8, offset + 8 + length);
            offset += 12 + length;
            if (kind == "pHYs")
            {
                long x = Bytes.U32BE(body, 0), y = Bytes.U32BE(body, 4);
                var unit = body[8];
                if (unit == 1 && x != 0 && y != 0)
                    result.Resolution = Resolution(x * 0.0254, y * 0.0254, "meter", x, y);
            }
            else if (kind == "iCCP")
            {
                var (_, rest) = SplitNul(body);
                Py.Assert(rest[0] == 0);
                result.Icc = Bytes.ZlibDecompress(rest[1..]);
            }
            else if (kind == "eXIf")
            {
                result.Exif = body;
            }
            else if (kind == "tEXt")
            {
                var (keyword, text) = SplitNul(body);
                result.Text.Add(new(Bytes.Latin1(keyword), Bytes.Latin1(text), null, null));
            }
            else if (kind == "zTXt")
            {
                var (keyword, rest) = SplitNul(body);
                Py.Assert(rest[0] == 0);
                result.Text.Add(new(Bytes.Latin1(keyword), Bytes.Latin1(Bytes.ZlibDecompress(rest[1..])), null, null));
            }
            else if (kind == "iTXt")
            {
                var (keyword, rest) = SplitNul(body);
                int compressed = rest[0], method = rest[1];
                var (language, rest2) = SplitNul(rest[2..]);
                var (translated, text) = SplitNul(rest2);
                if (compressed != 0)
                {
                    Py.Assert(method == 0);
                    text = Bytes.ZlibDecompress(text);
                }

                if (Bytes.Latin1(keyword) == "XML:com.adobe.xmp")
                {
                    result.Xmp = text;
                }
                else
                {
                    var languageTag = Encoding.ASCII.GetString(language);
                    var translatedKeyword = Encoding.UTF8.GetString(translated);
                    result.Text.Add(new(Bytes.Latin1(keyword), Encoding.UTF8.GetString(text), languageTag.Length > 0 ? languageTag : null, translatedKeyword.Length > 0 ? translatedKeyword : null));
                }
            }
        }

        return result;
    }

    public static ParsedMetadata JpegMetadata(byte[] data)
    {
        var offset = 2;
        var result = new ParsedMetadata();
        var iccChunks = new SortedDictionary<int, byte[]>();
        while (offset < data.Length)
        {
            var marker = data[offset + 1];
            offset += 2;
            if (marker is 0xD9 or 0xDA)
                break;
            var length = Bytes.U16BE(data, offset);
            var body = Bytes.Slice(data, offset + 2, offset + length);
            offset += length;
            if (marker == 0xE0 && Bytes.StartsWith(body, "JFIF\0"u8))
            {
                int units = body[7], x = Bytes.U16BE(body, 8), y = Bytes.U16BE(body, 10);
                if (units is 1 or 2 && x != 0 && y != 0)
                {
                    // inches: integers (factor 1); centimeters: x * 2.54
                    result.Resolution = units == 1
                        ? Resolution((long)x, (long)y, "inch", x, y)
                        : Resolution(x * 2.54, y * 2.54, "centimeter", x, y);
                }
            }
            else if (marker == 0xE1 && Bytes.StartsWith(body, "Exif\0\0"u8))
            {
                result.Exif = body[6..];
            }
            else if (marker == 0xE1 && Bytes.StartsWith(body, "http://ns.adobe.com/xap/1.0/\0"u8))
            {
                result.Xmp = body[29..];
            }
            else if (marker == 0xE2 && Bytes.StartsWith(body, "ICC_PROFILE\0"u8))
            {
                iccChunks[body[12]] = body[14..];
            }
            else if (marker == 0xFE)
            {
                result.Text.Add(new("Comment", Bytes.Latin1(body), null, null));
            }
        }

        if (iccChunks.Count > 0)
            result.Icc = Bytes.Concat(iccChunks.Values);
        return result;
    }

    /// <summary>Metadata chunks of a WebP input (WebPCorpus): ICCP, EXIF (TIFF data; an optional "Exif\0\0" prefix is not
    /// part of it) and XMP of an extended-layout file. A simple-layout file ends with its image chunk and has no metadata;
    /// WebP has no resolution or text chunk.</summary>
    public static ParsedMetadata WebPMetadata(byte[] data)
    {
        var result = new ParsedMetadata();
        var offset = 12;
        var extended = false;
        while (offset < data.Length)
        {
            var kind = Bytes.Latin1(data.AsSpan(offset, 4));
            var length = (int)Bytes.U32LE(data, offset + 4);
            var body = Bytes.Slice(data, offset + 8, offset + 8 + length);
            offset += 8 + length + (length & 1);
            if (kind == "VP8X")
                extended = true;
            else if (extended && kind == "ICCP")
                result.Icc = body;
            else if (extended && kind == "EXIF")
                result.Exif = Bytes.StartsWith(body, "Exif\0\0"u8) ? body[6..] : body;
            else if (extended && kind == "XMP ")
                result.Xmp = body;
        }

        return result;
    }

    /// <summary>QOI stores no metadata (QoiCorpus): its header colorspace is a transfer-function label, recorded separately
    /// in expected.transferFunction.</summary>
    public static ParsedMetadata QoiMetadata(byte[] data)
    {
        Py.Assert(Bytes.StartsWith(data, "qoif"u8));
        return new ParsedMetadata();
    }

    /// <summary>BMP stores only the physical resolution (biXPelsPerMeter/biYPelsPerMeter, pixels per meter).</summary>
    public static ParsedMetadata BmpMetadata(byte[] data)
    {
        Py.Assert(Bytes.StartsWith(data, "BM"u8));
        var headerLength = Bytes.U32LE(data, 14);
        Py.Assert(headerLength >= 40);
        long x = Bytes.I32LE(data, 38), y = Bytes.I32LE(data, 42);
        var result = new ParsedMetadata();
        if (x > 0 && y > 0)
            result.Resolution = Resolution(x * 0.0254, y * 0.0254, "meter", x, y);
        return result;
    }

    /// <summary>TGA stores no metadata this library exposes (TgaCorpus).</summary>
    public static ParsedMetadata TgaMetadata() => new();

    /// <summary>Netpbm files store no metadata.</summary>
    public static ParsedMetadata PnmMetadata(byte[] data)
    {
        Py.Assert(data[0] == (byte)'P');
        return new ParsedMetadata();
    }

    /// <summary>GIF comment extensions in file order (Latin-1 text entries with the keyword Comment); GIF has no other
    /// metadata.</summary>
    public static ParsedMetadata GifMetadata(byte[] data)
    {
        var packed = data[10];
        var offset = 13 + ((packed & 0x80) != 0 ? 3 * (2 << (packed & 7)) : 0);
        var result = new ParsedMetadata();
        while (data[offset] != 0x3B)
        {
            if (data[offset] == 0x21)
            {
                var label = data[offset + 1];
                offset += 2;
                var body = new List<byte>();
                while (data[offset] != 0)
                {
                    body.AddRange(Bytes.Slice(data, offset + 1, offset + 1 + data[offset]));
                    offset += 1 + data[offset];
                }

                offset++;
                if (label == 0xFE)
                    result.Text.Add(new("Comment", Bytes.Latin1([.. body]), null, null));
                continue;
            }

            var flags = data[offset + 9];
            offset += 10 + ((flags & 0x80) != 0 ? 3 * (2 << (flags & 7)) : 0) + 1;
            while (data[offset] != 0)
                offset += 1 + data[offset];
            offset++;
        }

        return result;
    }

    /// <summary>Expected metadata parsed from the encoded input; asserts consistency with the declared orientation/ICC label.</summary>
    public static Obj MetadataExpectation(string format, byte[] data, int orientation, string iccLabel)
    {
        var parsed = format switch
        {
            "png" => PngMetadata(data),
            "jpeg" => JpegMetadata(data),
            "gif" => GifMetadata(data),
            "webp" => WebPMetadata(data),
            "qoi" => QoiMetadata(data),
            "bmp" => BmpMetadata(data),
            "tga" => TgaMetadata(),
            "pnm" => PnmMetadata(data),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format),
        };
        var exifOrientation = parsed.Exif is { Length: > 0 } ? ExifReadOrientation(parsed.Exif) : null;
        Py.Assert((exifOrientation is null or 0 ? 1 : exifOrientation) == orientation, $"orientation {exifOrientation} {orientation}");
        var icc = parsed.Icc;
        var label = icc is null ? "none" : Bytes.Latin1(icc.AsSpan(16, 4)) switch
        {
            "GRAY" => "gray",
            "RGB " => "rgb",
            var other => throw new InvalidOperationException("Unexpected ICC color space " + other),
        };
        Py.Assert(label == iccLabel, $"icc {label} {iccLabel}");
        return new Obj
        {
            ["resolution"] = parsed.Resolution,
            ["profiles"] = new Obj
            {
                ["icc"] = parsed.Icc is not null ? ProfileRecord(parsed.Icc) : null,
                ["exif"] = parsed.Exif is not null ? ProfileRecord(parsed.Exif) : null,
                ["xmp"] = parsed.Xmp is not null ? ProfileRecord(parsed.Xmp) : null,
            },
            ["text"] = parsed.Text.Select(t => (object?)new Obj
            {
                ["keyword"] = t.Keyword,
                ["value"] = t.Value,
                ["languageTag"] = t.LanguageTag,
                ["translatedKeyword"] = t.TranslatedKeyword,
            }).ToList(),
        };
    }
}
