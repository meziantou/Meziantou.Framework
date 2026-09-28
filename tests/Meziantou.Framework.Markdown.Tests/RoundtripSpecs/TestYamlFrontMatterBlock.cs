using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs;

public class TestYamlFrontMatterBlock
{
    [Theory]
    [InlineData("---\nkey1: value1\nkey2: value2\n---\n\nContent\n")]
    [InlineData("---\nkey: value\n...\n\nContent\n")]
    [InlineData("--- \t\nkey: value\n---  \nContent\n")]
    [InlineData("---\r\nkey: value\r\n...\r\nContent\r\n")]
    [InlineData("---\nkey: value\n---")]
    [InlineData("No front matter")]
    [InlineData("Looks like front matter but actually is not\n---\nkey1: value1\nkey2: value2\n---")]
    public void FrontMatterBlockIsPreserved(string value)
    {
        RoundTrip(value);
    }
}
