namespace Meziantou.Framework.JavaScriptEngine.WebApis.Tests;

public sealed class EncodingTests
{
    [Fact]
    public void TextEncoder_Encode()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("utf-8", context.Run("new TextEncoder().encoding"));
        Assert.Equal("226,130,172", context.Run("new TextEncoder().encode('€').join()"));
        Assert.Equal("239,191,189", context.Run("new TextEncoder().encode('\\uD800').join()"));
        Assert.Equal("0", context.Run("new TextEncoder().encode().length"));
        Assert.Equal("true", context.Run("new TextEncoder().encode('a') instanceof Uint8Array"));
        Assert.Equal("TypeError", context.GetErrorName("Object.getOwnPropertyDescriptor(TextEncoder.prototype, 'encoding').get.call({})"));
    }

    [Fact]
    public void TextEncoder_EncodeInto()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("2,4,97,226,130,172,0", context.Run("""
            const bytes = new Uint8Array(5);
            const result = new TextEncoder().encodeInto('a€😀', bytes);
            [result.read, result.written, ...bytes].join()
            """));
        Assert.Equal("1,1,98", context.Run("const view = new Uint8Array(new ArrayBuffer(4), 3); const r = new TextEncoder().encodeInto('bc', view); [r.read, r.written, view[0]].join()"));
        Assert.Equal("TypeError", context.GetErrorName("new TextEncoder().encodeInto('a', new Uint16Array(2))"));
        Assert.Equal("TypeError", context.GetErrorName("new TextEncoder().encodeInto('a')"));
    }

    [Fact]
    public void TextDecoder_Decode()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("utf-8,false,false", context.Run("const d = new TextDecoder(); [d.encoding, d.fatal, d.ignoreBOM].join()"));
        Assert.Equal("a€", context.Run("new TextDecoder().decode(new Uint8Array([0xEF, 0xBB, 0xBF, 0x61, 0xE2, 0x82, 0xAC]))"));
        Assert.Equal("\uFEFFa", context.Run("new TextDecoder('utf-8', { ignoreBOM: true }).decode(new Uint8Array([0xEF, 0xBB, 0xBF, 0x61]))"));
        Assert.Equal("a\uFFFD", context.Run("new TextDecoder().decode(new Uint8Array([0x61, 0xFF]))"));
        Assert.Equal("", context.Run("new TextDecoder().decode()"));
        Assert.Equal("ab", context.Run("new TextDecoder().decode(new Uint8Array([0x61, 0x62]).buffer)"));
        Assert.Equal("b", context.Run("new TextDecoder().decode(new DataView(new Uint8Array([0x61, 0x62]).buffer, 1))"));
        Assert.Equal("TypeError", context.GetErrorName("new TextDecoder('utf-8', { fatal: true }).decode(new Uint8Array([0xFF]))"));
        Assert.Equal("TypeError", context.GetErrorName("new TextDecoder().decode('text')"));
    }

    [Fact]
    public void TextDecoder_Stream()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("|€|", context.Run("""
            const decoder = new TextDecoder();
            const bytes = new Uint8Array([0xE2, 0x82, 0xAC]);
            [decoder.decode(bytes.subarray(0, 1), { stream: true }), decoder.decode(bytes.subarray(1), { stream: true }), decoder.decode()].join('|')
            """));
        Assert.Equal("\uFFFD", context.Run("const d2 = new TextDecoder(); d2.decode(new Uint8Array([0xE2]), { stream: true }); d2.decode()"));
    }

    [Fact]
    public void TextDecoder_LegacyEncodings()
    {
        using var context = new WebApisTestContext();
        Assert.Equal("windows-1252|€", context.Run("const d = new TextDecoder('latin1'); [d.encoding, d.decode(new Uint8Array([0x80]))].join('|')"));
        Assert.Equal("shift_jis|あ", context.Run("const s = new TextDecoder('sjis'); [s.encoding, s.decode(new Uint8Array([0x82, 0xA0]))].join('|')"));
        Assert.Equal("utf-16le|a", context.Run("const u = new TextDecoder('utf-16'); [u.encoding, u.decode(new Uint8Array([0x61, 0x00]))].join('|')"));
        Assert.Equal("RangeError", context.GetErrorName("new TextDecoder('unknown')"));
        Assert.Equal("RangeError", context.GetErrorName("new TextDecoder('replacement')"));
        Assert.Equal("TypeError", context.GetErrorName("new TextDecoder('utf-8', 1)"));
    }
}
