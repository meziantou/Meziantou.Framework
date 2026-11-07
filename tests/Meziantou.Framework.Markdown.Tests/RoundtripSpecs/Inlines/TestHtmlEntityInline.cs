using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines;

/// <summary>
///
/// </summary>
/// <seealso href="https://spec.commonmark.org/0.29/#entity-and-numeric-character-references"/>
public class TestHtmlEntityInline
{
    [Theory]
    [InlineData("&gt;")]
    [InlineData("&lt;")]
    [InlineData("&nbsp;")]
    [InlineData("&heartsuit;")]
    [InlineData("&#42;")]
    [InlineData("&#0;")]
    [InlineData("&#1234;")]
    [InlineData("&#xcab;")]

    [InlineData(" &gt;")]
    [InlineData(" &lt;")]
    [InlineData(" &nbsp;")]
    [InlineData(" &heartsuit;")]
    [InlineData(" &#42;")]
    [InlineData(" &#0;")]
    [InlineData(" &#1234;")]
    [InlineData(" &#xcab;")]

    [InlineData("&gt; ")]
    [InlineData("&lt; ")]
    [InlineData("&nbsp; ")]
    [InlineData("&heartsuit; ")]
    [InlineData("&#42; ")]
    [InlineData("&#0; ")]
    [InlineData("&#1234; ")]
    [InlineData("&#xcab; ")]

    [InlineData(" &gt; ")]
    [InlineData(" &lt; ")]
    [InlineData(" &nbsp; ")]
    [InlineData(" &heartsuit; ")]
    [InlineData(" &#42; ")]
    [InlineData(" &#0; ")]
    [InlineData(" &#1234; ")]
    [InlineData(" &#xcab; ")]
    public void Test(string value)
    {
        RoundTrip(value);
    }
}
