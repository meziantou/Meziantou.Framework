using System.Text;
using Meziantou.Framework.WebTransport.Http3;

namespace Meziantou.Framework.WebTransport.Tests;

public sealed class Http3ProtocolTests
{
    // RFC 9000, Appendix A.1
    [Theory]
    [InlineData("c2197c5eff14e88c", 151288809941952652L)]
    [InlineData("9d7f3e7d", 494878333L)]
    [InlineData("7bbd", 15293L)]
    [InlineData("25", 37L)]
    [InlineData("4025", 37L)]
    public void VariableLengthInteger_Read(string hex, long expected)
    {
        var bytes = Convert.FromHexString(hex);
        Assert.True(VariableLengthInteger.TryRead(bytes, out var value, out var length));
        Assert.Equal(expected, value);
        Assert.Equal(bytes.Length, length);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(63L)]
    [InlineData(64L)]
    [InlineData(16383L)]
    [InlineData(16384L)]
    [InlineData(1073741823L)]
    [InlineData(1073741824L)]
    [InlineData((1L << 62) - 1)]
    public void VariableLengthInteger_RoundTrip(long value)
    {
        var buffer = new byte[8];
        var length = VariableLengthInteger.Write(buffer, value);
        Assert.Equal(VariableLengthInteger.GetEncodedLength(value), length);
        Assert.True(VariableLengthInteger.TryRead(buffer.AsSpan(0, length), out var read, out var readLength));
        Assert.Equal(value, read);
        Assert.Equal(length, readLength);
        Assert.False(VariableLengthInteger.TryRead(buffer.AsSpan(0, length - 1), out _, out _));
    }

    // RFC 7541, Appendix C.4
    [Theory]
    [InlineData("www.example.com", "f1e3c2e5f23a6ba0ab90f4ff")]
    [InlineData("no-cache", "a8eb10649cbf")]
    [InlineData("custom-key", "25a849e95ba97d7f")]
    [InlineData("custom-value", "25a849e95bb8e8b4bf")]
    public void Huffman_RoundTrip(string value, string hex)
    {
        var encoded = new List<byte>();
        Huffman.Encode(Encoding.ASCII.GetBytes(value), encoded);
        Assert.Equal(hex, Convert.ToHexStringLower([.. encoded]));
        Assert.Equal(value, Encoding.ASCII.GetString(Huffman.Decode(Convert.FromHexString(hex))));
    }

    [Fact]
    public void Huffman_AllBytes()
    {
        var value = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var encoded = new List<byte>();
        Huffman.Encode(value, encoded);
        Assert.HasCount(Huffman.GetEncodedLength(value), encoded);
        Assert.Equal(value, Huffman.Decode([.. encoded]));
    }

    [Theory]
    [InlineData("ff")] // 8 bits of padding
    [InlineData("f1e3c2e5f23a6ba0ab90f4fe")] // padding with a 0 bit
    [InlineData("fffffffc")] // EOS (30 bits of ones)
    public void Huffman_Invalid(string hex)
    {
        Assert.Throws<Http3ProtocolException>(() => Huffman.Decode(Convert.FromHexString(hex)));
    }

    // RFC 9204, Appendix B.1
    [Fact]
    public void QPack_DecodeLiteralWithNameReference()
    {
        var fields = QPackDecoder.Decode(Convert.FromHexString("0000510b2f696e6465782e68746d6c"), 1024);
        Assert.Equal([new QPackField(":path", "/index.html")], fields);
    }

    [Fact]
    public void QPack_RoundTrip()
    {
        QPackField[] fields =
        [
            new(":method", "CONNECT"), // exact match in the static table
            new(":protocol", "webtransport"),
            new(":scheme", "https"),
            new(":authority", "example.com:4433"), // name match
            new(":path", "/echo?query=1"),
            new("origin", "https://example.com"),
            new("x-custom", "value with spaces"), // literal name
            new("wt-available-protocols", "\"a\", \"b\""),
            new("empty", ""),
        ];

        var encoded = QPackEncoder.Encode(fields);
        Assert.Equal(0xC0 | 15, encoded[2]); // :method CONNECT is the entry 15 of the static table
        Assert.Equal(fields, QPackDecoder.Decode(encoded, 64 * 1024));
    }

    [Theory]
    [InlineData("0100")] // Required Insert Count 1
    [InlineData("000080")] // Indexed field line, dynamic table
    [InlineData("000010")] // Indexed field line with post-base index
    [InlineData("000040")] // Literal field line with dynamic name reference
    [InlineData("0000ff3f")] // Static index out of range (63 + 63 = 126)
    [InlineData("0000510b2f696e64")] // Truncated value
    [InlineData("0000ffffffffffffffffff7f")] // Static index whose 9th continuation byte overflows a 64-bit integer
    [InlineData("000027ffffffffffffffff7f")] // Literal name length whose 9th continuation byte overflows a 64-bit integer
    public void QPack_Invalid(string hex)
    {
        Assert.Throws<Http3ProtocolException>(() => QPackDecoder.Decode(Convert.FromHexString(hex), 1024));
    }

    [Fact]
    public void QPack_FieldSectionTooLarge()
    {
        var encoded = QPackEncoder.Encode([new QPackField("x-large", new string('a', 100))]);
        var exception = Assert.Throws<Http3ProtocolException>(() => QPackDecoder.Decode(encoded, 100));
        Assert.Equal(Http3ErrorCode.ExcessiveLoad, exception.ErrorCode);
    }

    [Theory]
    [InlineData(0u, 0x52E4A40FA8DBL)]
    [InlineData(0x1Du, 0x52E4A40FA8DBL + 0x1D)]
    [InlineData(0x1Eu, 0x52E4A40FA8DBL + 0x1F)]
    [InlineData(uint.MaxValue, 0x52E5AC983162L)]
    public void WebTransportErrorCode_Mapping(uint errorCode, long http3ErrorCode)
    {
        Assert.Equal(http3ErrorCode, Http3ErrorCode.FromWebTransportErrorCode(errorCode));
        Assert.Equal(errorCode, Http3ErrorCode.ToWebTransportErrorCode(http3ErrorCode));
    }

    [Fact]
    public void WebTransportErrorCode_SkipsReservedCodes()
    {
        for (var errorCode = 0u; errorCode < 1000; errorCode++)
        {
            var http3ErrorCode = Http3ErrorCode.FromWebTransportErrorCode(errorCode);
            Assert.NotEqual(0, (http3ErrorCode - 0x21) % 0x1F);
            Assert.Equal(errorCode, Http3ErrorCode.ToWebTransportErrorCode(http3ErrorCode));
        }

        Assert.Null(Http3ErrorCode.ToWebTransportErrorCode(0x52E4A40FA8DBL + 0x1E)); // reserved: 0x1f * N + 0x21
        Assert.Null(Http3ErrorCode.ToWebTransportErrorCode(Http3ErrorCode.NoError));
        Assert.Null(Http3ErrorCode.ToWebTransportErrorCode(0x52E5AC983163L));
    }

    [Fact]
    public void StructuredFieldStrings_RoundTrip()
    {
        var serialized = StructuredFieldStrings.SerializeList(["a", "b c", "quote\"backslash\\"]);
        Assert.Equal("\"a\", \"b c\", \"quote\\\"backslash\\\\\"", serialized);
        Assert.Equal(["a", "b c", "quote\"backslash\\"], StructuredFieldStrings.ParseList(serialized));
        Assert.Equal("proto", StructuredFieldStrings.ParseItem(" \"proto\";param=1 "));

        // A comma or an escaped quote inside a quoted parameter value does not end the member
        Assert.Equal(["echo", "other"], StructuredFieldStrings.ParseList("\"echo\";label=\"a,b\", \"other\""));
        Assert.Equal(["echo", "other"], StructuredFieldStrings.ParseList("\"echo\";label=\"a\\\",b\";x=1, \"other\""));
        Assert.Equal("echo", StructuredFieldStrings.ParseItem("\"echo\";label=\"a,b\""));
        Assert.Null(StructuredFieldStrings.ParseItem("token"));
        Assert.Null(StructuredFieldStrings.ParseList("\"a\","));
        Assert.Empty(StructuredFieldStrings.ParseList("")!);
    }

    [Fact]
    public void Capsule_Close()
    {
        var encoded = Capsule.EncodeClose(42, "bye");
        var capsules = new List<Capsule>();

        // A truncated capsule is not read until it is complete
        Assert.Equal(0, Capsule.ReadCapsules(encoded.AsSpan(0, encoded.Length - 1), capsules, 1024));
        Assert.Equal(encoded.Length, Capsule.ReadCapsules(encoded, capsules, 1024));
        var capsule = Assert.Single(capsules);
        Assert.Equal(Capsule.CloseWebTransportSession, capsule.Type);
        Assert.Equal((42u, "bye"), Capsule.DecodeClose(capsule.Value.Span));

        Assert.Throws<ArgumentException>(() => Capsule.EncodeClose(0, new string('a', 1025)));
        Assert.Throws<Http3ProtocolException>(() => Capsule.DecodeClose([0, 0, 0]));
    }

    [Fact]
    public void Settings_Parse()
    {
        var settings = Http3Settings.EncodeLocalSettings(1000, maxSessions: 10);
        Assert.True(VariableLengthInteger.TryRead(settings, out var type, out var typeLength));
        Assert.Equal(Http3FrameType.Settings, type);
        Assert.True(VariableLengthInteger.TryRead(settings.AsSpan(typeLength), out var length, out var lengthLength));
        var parsed = Http3Settings.Parse(settings.AsSpan(typeLength + lengthLength, (int)length));
        Assert.True(parsed.SupportsWebTransport);
        Assert.Equal(1000, parsed.Get(Http3SettingsIdentifier.MaxFieldSectionSize));
        Assert.Equal(10, parsed.Get(Http3SettingsIdentifier.WebTransportMaxSessionsDraft07));
        Assert.Null(parsed.Get(Http3SettingsIdentifier.WebTransportMaxSessions));

        // Duplicate and reserved settings
        Assert.Throws<Http3ProtocolException>(() => Http3Settings.Parse([0x06, 0x01, 0x06, 0x02]));
        Assert.Throws<Http3ProtocolException>(() => Http3Settings.Parse([0x02, 0x01]));

        // Boolean settings other than 0 or 1
        Assert.Equal(Http3ErrorCode.SettingsError, Assert.Throws<Http3ProtocolException>(() => Http3Settings.Parse([0x33, 0x02])).ErrorCode);
        Assert.Equal(Http3ErrorCode.SettingsError, Assert.Throws<Http3ProtocolException>(() => Http3Settings.Parse([0x08, 0x02])).ErrorCode);
        Assert.False(Http3Settings.Parse([]).SupportsWebTransport);
    }
}
