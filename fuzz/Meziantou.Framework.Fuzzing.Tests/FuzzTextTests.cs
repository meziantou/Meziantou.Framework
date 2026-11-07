using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Fuzzing.Tests;

public sealed class FuzzTextTests
{
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("caf\u00E9 \u4E2D \U0001F600")]
    public void EncodeThenDecodeRoundTrips(string text)
    {
        Assert.Equal(text, FuzzText.Decode(FuzzText.Encode(text)));
    }

    [Fact]
    public void UnpairedSurrogatesRoundTrip()
    {
        // Built from char values: xunit sends InlineData through UTF-8, which loses unpaired surrogates
        var text = new string(['a', (char)0xD800, 'b', (char)0xDFFF, (char)0xDC00, (char)0xD83D]);
        Assert.Equal(text, FuzzText.Decode(FuzzText.Encode(text)));
    }

    [Fact]
    public void InvalidBytesDecodeToUnpairedLowSurrogates()
    {
        Assert.Equal(new string(['a', (char)0xDCFF, (char)0xDCC3, 'b']), FuzzText.Decode([(byte)'a', 0xFF, 0xC3, (byte)'b']));
    }

    [Fact]
    public void DictionaryEscapesNonPrintableBytes()
    {
        var target = new FuzzTarget("test", _ => { }) { Dictionary = ["a\"b", "\\", "\n\u00E9"] };
        Assert.Equal("\"a\\x22b\"\n\"\\x5C\"\n\"\\x0A\\xC3\\xA9\"\n", FuzzProgram.GetDictionary(target));
    }
}
