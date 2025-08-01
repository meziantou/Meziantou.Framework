using Xunit;

namespace Meziantou.Framework.Tests;
public sealed class EncodingTests
{
    [Fact]
    public static void Utf8WithoutPreambleTest()
    {
        var encoding = Encoding.UTF8WithoutPreamble;
        Assert.Equal(0, encoding.Preamble.Length);
    }
}
