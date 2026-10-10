using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// A small, independent container walker that reads the raw timing, loop and metadata fields of PNG/APNG, GIF, JPEG, WebP,
/// QOI, BMP, TGA and Netpbm fixture inputs. It never uses the library under test, so tests can check that (1) the manifest agrees with the encoded
/// bytes and (2) the library's conversions of these raw fields produce the expected exact values. It only parses
/// container structure (chunks, blocks, marker segments, RIFF chunks); it never decodes pixels.
/// </summary>
public static class EncodedFieldInspector
{
    private const int MaxInflatedBytes = 16 * 1024 * 1024;
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Inspects an encoded input.</summary>
    /// <param name="format">The fixture format (<c>png</c>, <c>gif</c>, <c>jpeg</c>, <c>webp</c>, <c>qoi</c>, <c>bmp</c>, <c>tga</c> or <c>pnm</c>).</param>
    /// <param name="data">The encoded bytes.</param>
    /// <returns>The raw fields.</returns>
    /// <exception cref="InvalidDataException">The container structure is malformed.</exception>
    public static EncodedFields Inspect(string format, ReadOnlySpan<byte> data) => format switch
    {
        "png" => InspectPng(data),
        "gif" => InspectGif(data),
        "jpeg" => InspectJpeg(data),
        "webp" => InspectWebP(data),
        "qoi" => InspectQoi(data),
        "bmp" => InspectBmp(data),
        "tga" => InspectTga(data),
        "pnm" => InspectPnm(data),
        _ => throw new ArgumentException($"Unknown fixture format '{format}'.", nameof(format)),
    };

    /// <summary>QOI has no metadata, timing or loop field: only the header colorspace (a transfer-function label) is read.</summary>
    private static EncodedFields InspectQoi(ReadOnlySpan<byte> data)
    {
        if (data.Length < 14 || !data[..4].SequenceEqual("qoif"u8))
            throw new InvalidDataException("Missing QOI header.");

        return new EncodedFields
        {
            TransferFunction = data[13] switch
            {
                0 => "srgb",
                1 => "linear",
                _ => throw new InvalidDataException("Invalid QOI colorspace."),
            },
        };
    }

    /// <summary>BMP stores only the physical resolution (<c>biXPelsPerMeter</c>/<c>biYPelsPerMeter</c>, pixels per meter).</summary>
    private static EncodedFields InspectBmp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 14 || !data[..2].SequenceEqual("BM"u8))
            throw new InvalidDataException("Missing BMP file header.");

        Require(data, 14, 4);
        var headerLength = BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);
        if (headerLength < 40)
            throw new InvalidDataException("The DIB header is shorter than BITMAPINFOHEADER.");

        Require(data, 14, 40);
        var x = BinaryPrimitives.ReadInt32LittleEndian(data[38..]);
        var y = BinaryPrimitives.ReadInt32LittleEndian(data[42..]);
        var result = new EncodedFields();
        if (x > 0 && y > 0)
        {
            result.Resolution = new EncodedResolution { Unit = "meter", X = x, Y = y };
        }

        return result;
    }

    /// <summary>TGA stores no timing, loop or metadata field this library exposes; the header is validated for plausibility.</summary>
    private static EncodedFields InspectTga(ReadOnlySpan<byte> data)
    {
        if (data.Length < 18)
            throw new InvalidDataException("Missing TGA header.");

        if (data[1] > 1 || data[2] is not (1 or 2 or 3 or 9 or 10 or 11))
            throw new InvalidDataException("Implausible TGA header.");

        return new EncodedFields();
    }

    /// <summary>Netpbm files store no timing, loop or metadata field; the magic number is validated.</summary>
    private static EncodedFields InspectPnm(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3 || data[0] != (byte)'P' || data[1] is < (byte)'1' or > (byte)'7')
            throw new InvalidDataException("Missing Netpbm magic number.");

        return new EncodedFields();
    }

    private static EncodedFields InspectPng(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data[..8].SequenceEqual(PngSignature))
            throw new InvalidDataException("Missing PNG signature.");

        var result = new EncodedFields();
        var offset = 8;
        while (offset < data.Length)
        {
            Require(data, offset, 8);
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]));
            var type = Encoding.ASCII.GetString(data.Slice(offset + 4, 4));
            Require(data, offset + 8, length + 4);
            var body = data.Slice(offset + 8, length);
            offset += 12 + length;
            switch (type)
            {
                case "acTL":
                    result.LoopValue = (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
                    break;
                case "fcTL":
                    result.FrameDelays.Add(new EncodedDelayExpectation { Numerator = BinaryPrimitives.ReadUInt16BigEndian(body[20..]), Denominator = BinaryPrimitives.ReadUInt16BigEndian(body[22..]) });
                    break;
                case "pHYs":
                    var x = BinaryPrimitives.ReadUInt32BigEndian(body);
                    var y = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
                    result.Resolution = body[8] == 1 && x > 0 && y > 0 ? new EncodedResolution { Unit = ResolutionExpectation.Meter, X = (int)x, Y = (int)y } : null;
                    break;
                case "iCCP":
                    var nameEnd = body.IndexOf((byte)0);
                    result.Icc = Inflate(body[(nameEnd + 2)..]);
                    break;
                case "eXIf":
                    result.Exif = body.ToArray();
                    break;
                case "tEXt":
                {
                    var separator = body.IndexOf((byte)0);
                    result.Text.Add(Text(Encoding.Latin1.GetString(body[..separator]), Encoding.Latin1.GetString(body[(separator + 1)..])));
                    break;
                }

                case "zTXt":
                {
                    var separator = body.IndexOf((byte)0);
                    result.Text.Add(Text(Encoding.Latin1.GetString(body[..separator]), Encoding.Latin1.GetString(Inflate(body[(separator + 2)..]))));
                    break;
                }

                case "iTXt":
                {
                    var separator = body.IndexOf((byte)0);
                    var keyword = Encoding.Latin1.GetString(body[..separator]);
                    var compressed = body[separator + 1] != 0;
                    var rest = body[(separator + 3)..];
                    var languageEnd = rest.IndexOf((byte)0);
                    var language = Encoding.ASCII.GetString(rest[..languageEnd]);
                    rest = rest[(languageEnd + 1)..];
                    var translatedEnd = rest.IndexOf((byte)0);
                    var translated = Encoding.UTF8.GetString(rest[..translatedEnd]);
                    var text = rest[(translatedEnd + 1)..];
                    var bytes = compressed ? Inflate(text) : text.ToArray();
                    if (keyword == "XML:com.adobe.xmp")
                    {
                        result.Xmp = bytes;
                    }
                    else
                    {
                        result.Text.Add(new TextEntryExpectation { Keyword = keyword, Value = Encoding.UTF8.GetString(bytes), LanguageTag = language.Length == 0 ? null : language, TranslatedKeyword = translated.Length == 0 ? null : translated });
                    }

                    break;
                }
            }

            if (type == "IEND")
                break;
        }

        return result;
    }

    private static EncodedFields InspectGif(ReadOnlySpan<byte> data)
    {
        if (data.Length < 13 || !(data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
            throw new InvalidDataException("Missing GIF signature.");

        var result = new EncodedFields();
        var offset = 13;
        if ((data[10] & 0x80) != 0)
        {
            offset += 3 * (2 << (data[10] & 7));
        }

        EncodedDelayExpectation? pendingDelay = null;
        while (true)
        {
            Require(data, offset, 1);
            var introducer = data[offset];
            if (introducer == 0x3B)
                break;

            if (introducer == 0x21)
            {
                Require(data, offset, 2);
                var label = data[offset + 1];
                offset += 2;
                var body = new List<byte>();
                var first = true;
                while (true)
                {
                    Require(data, offset, 1);
                    var size = data[offset];
                    if (size == 0)
                    {
                        offset++;
                        break;
                    }

                    Require(data, offset + 1, size);
                    var block = data.Slice(offset + 1, size);
                    if (label == 0xFF && !first && body.Count >= 11 && (Encoding.ASCII.GetString([.. body.Take(11)]) is "NETSCAPE2.0" or "ANIMEXTS1.0") && block[0] == 1)
                    {
                        result.LoopValue = BinaryPrimitives.ReadUInt16LittleEndian(block[1..]);
                    }

                    body.AddRange(block);
                    first = false;
                    offset += 1 + size;
                }

                if (label == 0xF9)
                {
                    pendingDelay = new EncodedDelayExpectation { Hundredths = body[1] | (body[2] << 8) };
                }
                else if (label == 0xFE)
                {
                    result.Text.Add(Text("Comment", Encoding.Latin1.GetString([.. body])));
                }

                continue;
            }

            if (introducer != 0x2C)
                throw new InvalidDataException($"Unexpected GIF block 0x{introducer:X2} at offset {offset}.");

            Require(data, offset, 10);
            var flags = data[offset + 9];
            offset += 10;
            if ((flags & 0x80) != 0)
            {
                offset += 3 * (2 << (flags & 7));
            }

            offset++; // LZW minimum code size
            while (true)
            {
                Require(data, offset, 1);
                var size = data[offset];
                offset += 1 + size;
                if (size == 0)
                    break;
            }

            result.FrameDelays.Add(pendingDelay);
            pendingDelay = null;
        }

        return result;
    }

    private static EncodedFields InspectJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2 || data[0] != 0xFF || data[1] != 0xD8)
            throw new InvalidDataException("Missing JPEG SOI marker.");

        var result = new EncodedFields();
        var iccChunks = new SortedDictionary<int, byte[]>();
        var offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset] != 0xFF)
                throw new InvalidDataException($"Expected a JPEG marker at offset {offset}.");

            var marker = data[offset + 1];
            if (marker is 0xD9 or 0xDA)
                break; // Metadata segments precede the first scan

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
            Require(data, offset + 2, length);
            var body = data.Slice(offset + 4, length - 2);
            offset += 2 + length;
            if (marker == 0xE0 && body.StartsWith("JFIF\0"u8))
            {
                var units = body[7];
                var x = BinaryPrimitives.ReadUInt16BigEndian(body[8..]);
                var y = BinaryPrimitives.ReadUInt16BigEndian(body[10..]);
                result.Resolution = units is 1 or 2 && x > 0 && y > 0 ? new EncodedResolution { Unit = units == 1 ? ResolutionExpectation.Inch : ResolutionExpectation.Centimeter, X = x, Y = y } : null;
            }
            else if (marker == 0xE1 && body.StartsWith("Exif\0\0"u8))
            {
                result.Exif = body[6..].ToArray();
            }
            else if (marker == 0xE1 && body.StartsWith("http://ns.adobe.com/xap/1.0/\0"u8))
            {
                result.Xmp = body[29..].ToArray();
            }
            else if (marker == 0xE2 && body.StartsWith("ICC_PROFILE\0"u8))
            {
                iccChunks[body[12]] = body[14..].ToArray();
            }
            else if (marker == 0xFE)
            {
                result.Text.Add(Text("Comment", Encoding.Latin1.GetString(body)));
            }
        }

        if (iccChunks.Count > 0)
        {
            result.Icc = iccChunks.Values.SelectMany(chunk => chunk).ToArray();
        }

        return result;
    }

    private static EncodedFields InspectWebP(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WEBP"u8))
            throw new InvalidDataException("Missing RIFF/WEBP signature.");

        var riffEnd = 8 + (long)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if (riffEnd > data.Length)
            throw new InvalidDataException("The RIFF size exceeds the data.");

        var result = new EncodedFields();
        var offset = 12;
        var extended = false;
        while (offset < riffEnd)
        {
            Require(data, offset, 8);
            var type = Encoding.ASCII.GetString(data.Slice(offset, 4));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
            if (length > int.MaxValue - 9)
                throw new InvalidDataException($"Chunk '{type}' declares {length} bytes.");

            Require(data, offset + 8, (int)length);
            var body = data.Slice(offset + 8, (int)length);
            offset += 8 + (int)length + ((int)length & 1);
            switch (type)
            {
                case "VP8X":
                    extended = true;
                    break;
                case "ANIM":
                    result.LoopValue = BinaryPrimitives.ReadUInt16LittleEndian(body[4..]);
                    break;
                case "ANMF":
                    result.FrameDelays.Add(new EncodedDelayExpectation { Milliseconds = body[12] | (body[13] << 8) | (body[14] << 16) });
                    break;

                // Metadata chunks are only meaningful in the extended layout (a simple file ends with its image chunk)
                case "ICCP" when extended:
                    result.Icc = body.ToArray();
                    break;
                case "EXIF" when extended:
                    result.Exif = body.StartsWith("Exif\0\0"u8) ? body[6..].ToArray() : body.ToArray();
                    break;
                case "XMP " when extended:
                    result.Xmp = body.ToArray();
                    break;
            }
        }

        return result;
    }

    private static TextEntryExpectation Text(string keyword, string value) => new() { Keyword = keyword, Value = value, LanguageTag = null, TranslatedKeyword = null };

    private static byte[] Inflate(ReadOnlySpan<byte> zlibData)
    {
        using var input = new MemoryStream(zlibData.ToArray(), writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = zlib.Read(buffer)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > MaxInflatedBytes)
                throw new InvalidDataException("Compressed metadata exceeds the inspection bound.");
        }

        return output.ToArray();
    }

    private static void Require(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (offset < 0 || count < 0 || (long)offset + count > data.Length)
            throw new InvalidDataException($"Truncated container: {count} bytes needed at offset {offset}, {data.Length} available.");
    }
}
