namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class HighlightOptionsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("hljs-")]
    [InlineData("syntax-")]
    [InlineData("_private")]
    [InlineData("a0-b_c")]
    public void ClassPrefix_ValidValue_IsAccepted(string prefix)
    {
        var options = new HighlightOptions { ClassPrefix = prefix };

        Assert.Equal(prefix, options.ClassPrefix);
    }

    [Theory]
    [InlineData("\"><script>alert(1)</script><span class=\"")]
    [InlineData("a b")]
    [InlineData("a.b")]
    [InlineData("a<b")]
    [InlineData("a\"b")]
    [InlineData("1abc")]
    public void ClassPrefix_InvalidValue_Throws(string prefix)
    {
        Assert.Throws<ArgumentException>(() => new HighlightOptions { ClassPrefix = prefix });
    }

    [Fact]
    public void ClassPrefix_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HighlightOptions { ClassPrefix = null! });
    }
}
