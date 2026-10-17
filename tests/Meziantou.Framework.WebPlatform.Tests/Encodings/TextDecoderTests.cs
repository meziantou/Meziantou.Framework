using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Encodings;

// Adapted from WPT encoding/textdecoder-*.any.js
public sealed class TextDecoderTests
{
    private const string Sample = "z\U000000A2\U00006C34\U0001D11E\U000F8FFF";

    private static readonly byte[] Utf8Sample = [0x7A, 0xC2, 0xA2, 0xE6, 0xB0, 0xB4, 0xF0, 0x9D, 0x84, 0x9E, 0xF3, 0xB8, 0xBF, 0xBF];

    [Fact]
    public void Constructor_InvalidLabel_ThrowsRangeError()
    {
        Assert.Throws<RangeErrorException>(() => new TextDecoder("invalid"));
        Assert.Throws<RangeErrorException>(() => new TextDecoder("replacement"));
        Assert.Throws<RangeErrorException>(() => new TextDecoder("iso-2022-kr"));
    }

    [Theory]
    [InlineData("utf-8", "utf-8")]
    [InlineData("  UTF8 ", "utf-8")]
    [InlineData("latin1", "windows-1252")]
    [InlineData("utf-16", "utf-16le")]
    [InlineData("sjis", "shift_jis")]
    public void Encoding_IsLowercasedName(string label, string expected)
    {
        Assert.Equal(expected, new TextDecoder(label).Encoding);
    }

    [Fact]
    public void Decode_Utf8()
    {
        Assert.Equal(Sample, new TextDecoder().Decode(Utf8Sample));
    }

    [Fact]
    public void Decode_ByteOrderMarks()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Utf8Sample];
        Assert.Equal(Sample, new TextDecoder("utf-8").Decode(withBom));
        Assert.Equal("\U0000FEFF" + Sample, new TextDecoder("utf-8", new TextDecoderOptions { IgnoreBom = true }).Decode(withBom));

        // Only one BOM is removed
        Assert.Equal("\U0000FEFF" + Sample, new TextDecoder("utf-8").Decode([0xEF, 0xBB, 0xBF, .. withBom]));

        // The BOM of another encoding is not removed
        Assert.Equal("\U0000FFFD\U0000FFFD" + "z", new TextDecoder("utf-8").Decode([0xFF, 0xFE, 0x7A]));

        Assert.Equal("a", new TextDecoder("utf-16le").Decode([0xFF, 0xFE, 0x61, 0x00]));
        Assert.Equal("a", new TextDecoder("utf-16be").Decode([0xFE, 0xFF, 0x00, 0x61]));
    }

    [Fact]
    public void Decode_Streaming_BomAcrossChunks()
    {
        var decoder = new TextDecoder();
        var result = decoder.Decode([0xEF], new TextDecodeOptions { Stream = true });
        result += decoder.Decode([0xBB], new TextDecodeOptions { Stream = true });
        result += decoder.Decode([0xBF, 0x61], new TextDecodeOptions { Stream = true });
        result += decoder.Decode();
        Assert.Equal("a", result);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16le")]
    [InlineData("utf-16be")]
    public void Decode_Streaming(string encoding)
    {
        var bytes = encoding switch
        {
            "utf-8" => Utf8Sample,
            "utf-16le" => System.Text.Encoding.Unicode.GetBytes(Sample),
            _ => System.Text.Encoding.BigEndianUnicode.GetBytes(Sample),
        };

        for (var chunkSize = 1; chunkSize < 5; chunkSize++)
        {
            var decoder = new TextDecoder(encoding);
            var result = "";
            for (var i = 0; i < bytes.Length; i += chunkSize)
            {
                result += decoder.Decode(bytes.AsSpan(i, Math.Min(chunkSize, bytes.Length - i)), new TextDecodeOptions { Stream = true });
            }

            result += decoder.Decode();
            Assert.Equal(Sample, result);
        }
    }

    [Theory]
    [InlineData("utf-8", new byte[] { 0xFF })]
    [InlineData("utf-8", new byte[] { 0xC0 })]
    [InlineData("utf-8", new byte[] { 0xE0 })]
    [InlineData("utf-8", new byte[] { 0xC0, 0x00 })]
    [InlineData("utf-8", new byte[] { 0xC0, 0xC0 })]
    [InlineData("utf-8", new byte[] { 0xE0, 0x00 })]
    [InlineData("utf-8", new byte[] { 0xE0, 0xC0 })]
    [InlineData("utf-8", new byte[] { 0xE0, 0x80, 0x00 })]
    [InlineData("utf-8", new byte[] { 0xE0, 0x80, 0xC0 })]
    [InlineData("utf-8", new byte[] { 0xFC, 0x80, 0x80, 0x80, 0x80, 0x80 })]
    [InlineData("utf-16le", new byte[] { 0x00 })]
    [InlineData("utf-16le", new byte[] { 0x00, 0xd8 })]
    [InlineData("utf-16le", new byte[] { 0x00, 0xd8, 0x00 })]
    [InlineData("utf-16le", new byte[] { 0x00, 0xd8, 0x00, 0x00 })]
    [InlineData("utf-16le", new byte[] { 0x00, 0xdc, 0x00, 0x00 })]
    [InlineData("utf-16le", new byte[] { 0x00, 0xdc, 0x00, 0xd8 })]
    public void Decode_Fatal_ThrowsTypeError(string encoding, byte[] input)
    {
        Assert.Throws<TypeErrorException>(() => new TextDecoder(encoding, new TextDecoderOptions { Fatal = true }).Decode(input));
    }

    [Fact]
    public void Decode_Fatal_IncompleteSequenceIsOnlyAnErrorWhenFlushing()
    {
        var decoder = new TextDecoder("utf-8", new TextDecoderOptions { Fatal = true });
        Assert.Equal("", decoder.Decode([0xE2], new TextDecodeOptions { Stream = true }));
        Assert.Throws<TypeErrorException>(() => decoder.Decode());

        // The decoder is usable again after an error
        Assert.Equal("a", decoder.Decode([0x61]));
    }

    [Fact]
    public void Decode_Utf16Surrogates()
    {
        // From WPT encoding/textdecoder-utf16-surrogates.any.js
        Assert.Equal("\U0000FFFD", new TextDecoder("utf-16le").Decode([0x00, 0xD8]));
        Assert.Equal("\U0000FFFD", new TextDecoder("utf-16le").Decode([0x00, 0xDC]));
        Assert.Equal("\U0000FFFD\U0000FFFD", new TextDecoder("utf-16le").Decode([0x00, 0xDC, 0x00, 0xD8]));
        Assert.Equal("\U0000FFFDa", new TextDecoder("utf-16le").Decode([0x00, 0xD8, 0x61, 0x00]));
    }

    [Fact]
    public void TextEncoder_Encode()
    {
        var encoder = new TextEncoder();
        Assert.Equal("utf-8", encoder.Encoding);
        Assert.Equal(Utf8Sample, encoder.Encode(Sample));
        Assert.Equal([0xEF, 0xBF, 0xBD], encoder.Encode(((char)0xD800).ToString()));
        Assert.Empty(encoder.Encode());
    }

    [Fact]
    public void TextEncoder_EncodeInto()
    {
        var encoder = new TextEncoder();
        var destination = new byte[4];
        var result = encoder.EncodeInto("a\U0001F600", destination);
        Assert.Equal(1UL, result.Read);
        Assert.Equal(1UL, result.Written);

        destination = new byte[5];
        result = encoder.EncodeInto("a\U0001F600", destination);
        Assert.Equal(3UL, result.Read);
        Assert.Equal(5UL, result.Written);
        Assert.Equal([0x61, 0xF0, 0x9F, 0x98, 0x80], destination);
    }

    [Fact]
    public void Decode_MissingInputWithOptions_IsAnEmptyInput()
    {
        var decoder = new TextDecoder();
        Assert.Equal("", decoder.Decode([0xC2], new TextDecodeOptions { Stream = true }));
        Assert.Equal("", decoder.Decode(default, new TextDecodeOptions { Stream = true }));
        Assert.Equal("\U000000A2", decoder.Decode([0xA2]));
    }

    [Fact]
    public void Decode_FatalErrorInStreamMode_KeepsTheStateOfTheDecoder()
    {
        // The do not flush flag is kept after an error: ISO-2022-JP stays in the Roman mode (encoding/textdecoder-mistakes.any.js)
        var decoder = new TextDecoder("iso-2022-jp", new TextDecoderOptions { Fatal = true });
        Assert.Equal("~", decoder.Decode([0x7E]));
        Assert.Throws<TypeErrorException>(() => decoder.Decode([0x1B, 0x28, 0x4A, 0xFF], new TextDecodeOptions { Stream = true }));
        Assert.Equal("\U0000203E", decoder.Decode([0x7E]));
    }
}
