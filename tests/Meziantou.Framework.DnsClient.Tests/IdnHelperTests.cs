using Meziantou.Framework.DnsClient.Helpers;

namespace Meziantou.Framework.DnsClient.Tests;

public sealed class IdnHelperTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("EXAMPLE.COM", "EXAMPLE.COM")]
    [InlineData("münchen.de", "xn--mnchen-3ya.de")]
    [InlineData("例え.jp", "xn--r8jz45g.jp")]
    public void ToAscii_ConvertsCorrectly(string input, string expected)
    {
        var result = IdnHelper.ToAscii(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToAscii_AlreadyAscii_ReturnsSameString()
    {
        var result = IdnHelper.ToAscii("example.com");
        Assert.Equal("example.com", result);
    }

    [Fact]
    public void ToAscii_PunycodePrefix()
    {
        var result = IdnHelper.ToAscii("xn--mnchen-3ya.de");
        Assert.Equal("xn--mnchen-3ya.de", result);
    }
}
