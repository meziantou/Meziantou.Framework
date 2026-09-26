using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines;

public class TestBackslashEscapeInline
{
    [Theory]
    [InlineData(@"\!")]
    [InlineData(@"\""")]
    [InlineData(@"\#")]
    [InlineData(@"\$")]
    [InlineData(@"\&")]
    [InlineData(@"\'")]
    [InlineData(@"\(")]
    [InlineData(@"\)")]
    [InlineData(@"\*")]
    [InlineData(@"\+")]
    [InlineData(@"\,")]
    [InlineData(@"\-")]
    [InlineData(@"\.")]
    [InlineData(@"\/")]
    [InlineData(@"\:")]
    [InlineData(@"\;")]
    [InlineData(@"\<")]
    [InlineData(@"\=")]
    [InlineData(@"\>")]
    [InlineData(@"\?")]
    [InlineData(@"\@")]
    [InlineData(@"\[")]
    [InlineData(@"\\")]
    [InlineData(@"\]")]
    [InlineData(@"\^")]
    [InlineData(@"\_")]
    [InlineData(@"\`")]
    [InlineData(@"\{")]
    [InlineData(@"\|")]
    [InlineData(@"\}")]
    [InlineData(@"\~")]

    // below test breaks visual studio
    //[TestCase(@"\!\""\#\$\%\&\'\(\)\*\+\,\-\.\/\:\;\<\=\>\?\@\[\\\]\^\_\`\{\|\}\~")]
    public void Test(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(@"# \#\#h1")]
    [InlineData(@"# \#\#h1\#")]
    public void TestHeading(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData(@"`\``")]
    [InlineData(@"` \``")]
    [InlineData(@"`\` `")]
    [InlineData(@"` \` `")]
    [InlineData(@" ` \` `")]
    [InlineData(@"` \` ` ")]
    [InlineData(@" ` \` ` ")]
    public void TestCodeSpanInline(string value)
    {
        RoundTrip(value);
    }
}
