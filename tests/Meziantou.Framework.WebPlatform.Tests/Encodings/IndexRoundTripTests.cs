using System.Text;
using System.Text.Json;
using Meziantou.Framework.WebPlatform.Encodings;

namespace Meziantou.Framework.WebPlatform.Tests.Encodings;

/// <summary>
/// Verifies every index entry of the Encoding Standard through the decoders and encoders, using the non-normative indexes.json.
/// </summary>
public sealed class IndexRoundTripTests
{
    private static readonly Lazy<JsonDocument> Indexes = new(() => JsonDocument.Parse(File.ReadAllText(TestPaths.SpecData("encoding", "indexes.json"))));

    private static int?[] Index(string name) => Indexes.Value.RootElement.GetProperty(name).EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? (int?)null : v.GetInt32()).ToArray();

    private static string Decode(string label, byte[] bytes, bool fatal = false)
    {
        Assert.True(WebEncodingRegistry.TryGetEncoding(label, out var encoding));
        return WebEncodingRegistry.CreateDecoder(encoding, fatal).DecodeToString(bytes);
    }

    private static byte[] Encode(string label, string value)
    {
        var result = TryEncode(label, value);
        Assert.NotNull(result);
        return result;
    }

    private static byte[]? TryEncode(string label, string value)
    {
        Assert.True(WebEncodingRegistry.TryGetEncoding(label, out var encoding));
        var encoder = WebEncodingRegistry.CreateEncoder(encoding);
        var output = new List<byte>();
        Span<byte> buffer = stackalloc byte[WebEncoder.MaxBytesPerCodePoint];
        foreach (var rune in value.EnumerateRunes())
        {
            if (!encoder.Encode(rune.Value, buffer, out var written, out _))
                return null;

            output.AddRange(buffer[..written]);
        }

        output.AddRange(buffer[..encoder.Flush(buffer)]);
        return [.. output];
    }

    public static TheoryData<string> SingleByteEncodings() =>
    [
        "IBM866", "ISO-8859-2", "ISO-8859-3", "ISO-8859-4", "ISO-8859-5", "ISO-8859-6", "ISO-8859-7", "ISO-8859-8", "ISO-8859-10",
        "ISO-8859-13", "ISO-8859-14", "ISO-8859-15", "ISO-8859-16", "KOI8-R", "KOI8-U", "macintosh", "windows-874", "windows-1250",
        "windows-1251", "windows-1252", "windows-1253", "windows-1254", "windows-1255", "windows-1256", "windows-1257", "windows-1258", "x-mac-cyrillic",
    ];

    [Theory]
    [MemberData(nameof(SingleByteEncodings))]
    public void SingleByte(string name)
    {
        var index = Index(name.ToLowerInvariant());
        for (var b = 0; b < 256; b++)
        {
            var expected = b < 0x80 ? b : index[b - 0x80];
            var decoded = Decode(name, [(byte)b]);
            Assert.Equal(expected is null ? "\U0000FFFD" : char.ConvertFromUtf32(expected.Value), decoded);
            if (expected is null)
            {
                Assert.Throws<DecoderFallbackException>(() => Decode(name, [(byte)b], fatal: true));
            }

            if (expected is not null)
            {
                Assert.Equal([(byte)b], Encode(name, char.ConvertFromUtf32(expected.Value)));
            }
        }
    }

    [Fact]
    public void SingleByte_Iso8859_8I_UsesIso8859_8Index()
    {
        var all = Enumerable.Range(0, 256).Select(b => (byte)b).ToArray();
        Assert.Equal(Decode("ISO-8859-8", all), Decode("ISO-8859-8-I", all));
    }

    [Fact]
    public void Gb18030_TwoBytes()
    {
        var index = Index("gb18030");
        for (var pointer = 0; pointer < index.Length; pointer++)
        {
            var leading = (pointer / 190) + 0x81;
            var trailing = pointer % 190;
            var offset = trailing < 0x3F ? 0x40 : 0x41;
            byte[] bytes = [(byte)leading, (byte)(trailing + offset)];
            var expected = index[pointer];
            Assert.Equal(expected is null ? null : char.ConvertFromUtf32(expected.Value), NullIfError(Decode("gb18030", bytes)));
        }
    }

    [Fact]
    public void Gb18030_FourBytes_RoundTrip()
    {
        foreach (var codePoint in new[] { 0x80, 0xA5, 0x2014, 0xE5E5, 0xFFFF, 0x10000, 0x10FFFF, 0xE7C7, 0x1E3F })
        {
            var value = char.ConvertFromUtf32(codePoint);
            var bytes = TryEncode("gb18030", value);
            if (codePoint == 0xE5E5)
            {
                Assert.Null(bytes);
                continue;
            }

            Assert.NotNull(bytes);
            Assert.Equal(value, Decode("gb18030", bytes));
        }

        Assert.Equal([0x84, 0x31, 0xA4, 0x39], Encode("gb18030", "\U0000FFFF"));
        Assert.Equal([0x90, 0x30, 0x81, 0x30], Encode("gb18030", "\U00010000"));
        Assert.Equal([0x81, 0x35, 0xF4, 0x37], Encode("gb18030", "\U0000E7C7"));
        Assert.Equal([0xA6, 0xD9], Encode("gb18030", "\U0000E78D"));
        Assert.Equal([0x80], Encode("gbk", "\U000020AC"));
        Assert.Equal([0xA2, 0xE3], Encode("gb18030", "\U000020AC"));
        Assert.Null(TryEncode("gbk", "\U00010000"));
    }

    [Fact]
    public void Gb18030_InvalidSequences()
    {
        // Incomplete sequences at the end of the queue produce a single error
        Assert.Equal("\U0000FFFD", Decode("gb18030", [0x81, 0x30]));
        Assert.Equal("\U0000FFFD", Decode("gb18030", [0x81, 0x30, 0xFE]));

        // Restore « second, byte » and « second, third, byte »
        Assert.Equal("\U0000FFFD00", Decode("gb18030", [0x81, 0x30, 0x30]));
        Assert.Equal("\U0000FFFD0\U00004E04", Decode("gb18030", [0x81, 0x30, 0x81, 0x41]));

        // Four-byte pointer > 39419 and < 189000 is null
        Assert.Equal("\U0000FFFD", Decode("gb18030", [0x84, 0x32, 0xA4, 0x30]));
        Assert.Equal("\U0000FFFD\u0000", Decode("gb18030", [0x81, 0x00]));
        Assert.Equal("\U000020AC", Decode("gbk", [0x80]));
    }

    [Fact]
    public void Big5()
    {
        var index = Index("big5");
        for (var pointer = 0; pointer < index.Length; pointer++)
        {
            var leading = (pointer / 157) + 0x81;
            var trailing = pointer % 157;
            var offset = trailing < 0x3F ? 0x40 : 0x62;
            byte[] bytes = [(byte)leading, (byte)(trailing + offset)];
            var expected = pointer switch
            {
                1133 => "\U000000CA\U00000304",
                1135 => "\U000000CA\U0000030C",
                1164 => "\U000000EA\U00000304",
                1166 => "\U000000EA\U0000030C",
                _ => index[pointer] is { } cp ? char.ConvertFromUtf32(cp) : null,
            };
            Assert.Equal(expected, NullIfError(Decode("big5", bytes)));
        }

        // index Big5 pointer excludes HKSCS extensions and uses the last pointer for some code points
        Assert.Equal([0xF9, 0xF9], Encode("big5", "\U00002550"));
        Assert.Equal([0xA4, 0x51], Encode("big5", "\U00005341"));
        Assert.Null(TryEncode("big5", "\U000000CA"));
        Assert.Equal("\U0000FFFDA", Decode("big5", [0x81, 0x41])); // null pointer, the ASCII trailing byte is restored
        Assert.Equal("\U0000FFFD", Decode("big5", [0x81, 0x80]));
    }

    [Fact]
    public void EucKR()
    {
        var index = Index("euc-kr");
        for (var pointer = 0; pointer < index.Length; pointer++)
        {
            byte[] bytes = [(byte)((pointer / 190) + 0x81), (byte)((pointer % 190) + 0x41)];
            var expected = index[pointer];
            Assert.Equal(expected is null ? null : char.ConvertFromUtf32(expected.Value), NullIfError(Decode("euc-kr", bytes)));
            if (expected is not null && TryEncode("euc-kr", char.ConvertFromUtf32(expected.Value)) is { } encoded)
            {
                Assert.Equal(char.ConvertFromUtf32(expected.Value), Decode("euc-kr", encoded));
            }
        }
    }

    [Fact]
    public void EucJP()
    {
        var jis0208 = Index("jis0208");
        var jis0212 = Index("jis0212");
        for (var pointer = 0; pointer < 94 * 94; pointer++)
        {
            byte[] bytes = [(byte)((pointer / 94) + 0xA1), (byte)((pointer % 94) + 0xA1)];
            var expected = pointer < jis0208.Length ? jis0208[pointer] : null;
            Assert.Equal(expected is null ? null : char.ConvertFromUtf32(expected.Value), NullIfError(Decode("euc-jp", bytes)));

            byte[] bytes0212 = [0x8F, (byte)((pointer / 94) + 0xA1), (byte)((pointer % 94) + 0xA1)];
            var expected0212 = pointer < jis0212.Length ? jis0212[pointer] : null;
            Assert.Equal(expected0212 is null ? null : char.ConvertFromUtf32(expected0212.Value), NullIfError(Decode("euc-jp", bytes0212)));
        }

        Assert.Equal("\U0000FF61", Decode("euc-jp", [0x8E, 0xA1]));
        Assert.Equal([0x8E, 0xA1], Encode("euc-jp", "\U0000FF61"));
        Assert.Equal([0x5C], Encode("euc-jp", "\U000000A5"));
        Assert.Equal([0xA1, 0xDD], Encode("euc-jp", "\U00002212"));
    }

    [Fact]
    public void ShiftJis()
    {
        var index = Index("jis0208");
        for (var pointer = 0; pointer < index.Length; pointer++)
        {
            var leading = pointer / 188;
            var leadingOffset = leading < 0x1F ? 0x81 : 0xC1;
            var trailing = pointer % 188;
            var offset = trailing < 0x3F ? 0x40 : 0x41;
            byte[] bytes = [(byte)(leading + leadingOffset), (byte)(trailing + offset)];
            var expected = pointer is >= 8836 and <= 10715 ? 0xE000 - 8836 + pointer : index[pointer];
            Assert.Equal(expected is null ? null : char.ConvertFromUtf32(expected.Value), NullIfError(Decode("shift_jis", bytes)));
        }

        Assert.Equal("\u0080", Decode("shift_jis", [0x80]));
        Assert.Equal("\U0000FF61", Decode("shift_jis", [0xA1]));
        Assert.Equal([0xA1], Encode("shift_jis", "\U0000FF61"));

        // NEC duplicates (8272-8835) are excluded from "index Shift_JIS pointer"
        Assert.Equal([0xFA, 0x40], Encode("shift_jis", "\U00002170"));
    }

    [Fact]
    public void Iso2022JP()
    {
        Assert.Equal("a\U00003000b", Decode("iso-2022-jp", [0x61, 0x1B, 0x24, 0x42, 0x21, 0x21, 0x1B, 0x28, 0x42, 0x62]));
        Assert.Equal("\U000000A5\U0000203Ea", Decode("iso-2022-jp", [0x1B, 0x28, 0x4A, 0x5C, 0x7E, 0x61]));
        Assert.Equal("\U0000FF61", Decode("iso-2022-jp", [0x1B, 0x28, 0x49, 0x21]));

        // Two consecutive escape sequences: the second one outputs an error
        Assert.Equal("\U0000FFFDa", Decode("iso-2022-jp", [0x1B, 0x28, 0x4A, 0x1B, 0x28, 0x42, 0x61]));

        // Invalid escape sequences restore the bytes
        Assert.Equal("\U0000FFFD$A", Decode("iso-2022-jp", [0x1B, 0x24, 0x41]));
        Assert.Equal("\U0000FFFD(", Decode("iso-2022-jp", [0x1B, 0x28]));
        Assert.Equal("\U0000FFFD", Decode("iso-2022-jp", [0x1B]));
        Assert.Equal("\U0000FFFD", Decode("iso-2022-jp", [0x0E]));

        Assert.Equal([0x1B, 0x24, 0x42, 0x21, 0x21, 0x1B, 0x28, 0x42], Encode("iso-2022-jp", "\U00003000"));
        Assert.Equal([0x1B, 0x28, 0x4A, 0x5C, 0x1B, 0x28, 0x42], Encode("iso-2022-jp", "\U000000A5"));
        Assert.Equal([0x1B, 0x24, 0x42, 0x21, 0x23, 0x1B, 0x28, 0x42], Encode("iso-2022-jp", "\U0000FF61"));
        Assert.Null(TryEncode("iso-2022-jp", "\u000E"));
    }

    [Fact]
    public void Utf16()
    {
        Assert.Equal("A\U0001F600", Decode("utf-16le", [0x41, 0x00, 0x3D, 0xD8, 0x00, 0xDE]));
        Assert.Equal("A\U0001F600", Decode("utf-16be", [0x00, 0x41, 0xD8, 0x3D, 0xDE, 0x00]));
        Assert.Equal("\U0000FFFD", Decode("utf-16le", [0x00, 0xD8]));
        Assert.Equal("\U0000FFFD", Decode("utf-16le", [0x00, 0xDC]));
        Assert.Equal("\U0000FFFDA", Decode("utf-16le", [0x00, 0xD8, 0x41, 0x00]));
        Assert.Equal("\U0000FFFD\U0000FFFD", Decode("utf-16le", [0x00, 0xD8, 0x00, 0xD8]));
        Assert.Equal("A\U0000FFFD", Decode("utf-16le", [0x41, 0x00, 0x42]));
        Assert.Equal("\U000000FF\U0000FEFF", Decode("utf-16le", [0xFF, 0x00, 0xFF, 0xFE])); // BOM handling is not part of the decoder
    }

    [Fact]
    public void Replacement()
    {
        Assert.Equal("", Decode("replacement", []));
        Assert.Equal("\U0000FFFD", Decode("replacement", [0x41, 0x42, 0x43]));
    }

    [Fact]
    public void XUserDefined()
    {
        for (var b = 0; b < 256; b++)
        {
            var expected = b < 0x80 ? (char)b : (char)(0xF780 + b - 0x80);
            Assert.Equal(expected.ToString(), Decode("x-user-defined", [(byte)b]));
            Assert.Equal([(byte)b], Encode("x-user-defined", expected.ToString()));
        }
    }

    public static TheoryData<string> AllEncodings()
    {
        var result = new TheoryData<string>("UTF-8", "gb18030", "gbk", "Big5", "EUC-JP", "ISO-2022-JP", "Shift_JIS", "EUC-KR", "UTF-16LE", "UTF-16BE", "x-user-defined");
        foreach (var name in SingleByteEncodings())
        {
            result.Add(name);
        }

        return result;
    }

    /// <summary>The decoding of a whole input (with its fast paths) matches the decoding of each byte by the handler, errors included.</summary>
    [Theory]
    [MemberData(nameof(AllEncodings))]
    public void DecodeToString_MatchesByteByByteDecoding(string name)
    {
        Assert.True(WebEncodingRegistry.TryGetEncoding(name, out var encoding));
        var random = new Random(42);
        for (var iteration = 0; iteration < 500; iteration++)
        {
#pragma warning disable CA5394 // The inputs are reproducible (seeded), not secret
            var bytes = new byte[random.Next(0, 80)];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = random.Next(iteration % 5 == 0 ? 40 : 4) switch
                {
                    0 => (byte)random.Next(0x80, 0x100),
                    1 => (byte)random.Next(0xC0, 0xF8),
                    2 => (byte)(iteration % 7 == 0 ? 0x1B : random.Next(0x20, 0x7F)),
                    _ => (byte)random.Next(0x20, 0x7F),
                };
            }
#pragma warning restore CA5394

            foreach (var fatal in new[] { false, true })
            {
                var expected = new StringBuilder();
                var decoder = WebEncodingRegistry.CreateDecoder(encoding, fatal);
                var valid = true;
                for (var i = 0; i < bytes.Length && valid; i++)
                {
                    valid = decoder.DecodeTo(bytes.AsSpan(i, 1), expected, flush: false);
                }

                valid = valid && decoder.DecodeTo([], expected, flush: true);
                if (valid)
                {
                    Assert.Equal(expected.ToString(), WebEncodingRegistry.CreateDecoder(encoding, fatal).DecodeToString(bytes));
                }
                else
                {
                    Assert.Throws<DecoderFallbackException>(() => WebEncodingRegistry.CreateDecoder(encoding, fatal).DecodeToString(bytes));
                }
            }
        }
    }

    private static string? NullIfError(string value) => value.Contains('\U0000FFFD', StringComparison.Ordinal) ? null : value;
}
