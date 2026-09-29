using Meziantou.Framework.Markdown.Extensions.SmartyPants;

namespace Meziantou.Framework.Markdown.Tests;

public class TestSmartyPants
{
    [Fact]
    public void MappingCanBeReconfigured()
    {
        var options = new SmartyPantOptions();
        options.Mapping[SmartyPantType.LeftAngleQuote] = "foo";
        options.Mapping[SmartyPantType.RightAngleQuote] = "bar";

        var pipeline = new MarkdownPipelineBuilder()
            .UseSmartyPants(options)
            .Build();

        TestParser.TestSpec("<<test>>", "<p>footestbar</p>", pipeline);
    }

    [Fact]
    public void MappingCanBeReconfigured_HandlesRemovedMappings()
    {
        var options = new SmartyPantOptions();
        options.Mapping.Remove(SmartyPantType.LeftAngleQuote);
        options.Mapping.Remove(SmartyPantType.RightAngleQuote);

        var pipeline = new MarkdownPipelineBuilder()
            .UseSmartyPants(options)
            .Build();

        TestParser.TestSpec("<<test>>", "<p>&laquo;test&raquo;</p>", pipeline);
    }

    [Fact]
    public void RecognizesSupplementaryCharacters()
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseSmartyPants()
            .Build();

        TestParser.TestSpec("\"𝜵\"𠮷\"𝜵\"𩸽\"", "<p>&quot;𝜵&ldquo;𠮷&rdquo;𝜵&ldquo;𩸽&rdquo;</p>", pipeline);
    }

    [Theory]
    [InlineData("''a", "<p>''a</p>")]
    [InlineData("a''", "<p>a''</p>")]
    [InlineData("''a''", "<p>&ldquo;a&rdquo;</p>")]
    public void UnmatchedDoubleQuotesAreWrittenAsInTheSource(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseSmartyPants()
            .Build();

        TestParser.TestSpec(markdown, expected, pipeline);
    }

    [Theory]
    [InlineData(SmartyPantType.Quote, '\'', "'")]
    [InlineData(SmartyPantType.LeftQuote, '\'', "'")]
    [InlineData(SmartyPantType.RightQuote, '\'', "'")]
    [InlineData(SmartyPantType.LeftDoubleQuote, '"', "\"")]
    [InlineData(SmartyPantType.LeftDoubleQuote, '\'', "''")]
    [InlineData(SmartyPantType.RightDoubleQuote, '"', "\"")]
    [InlineData(SmartyPantType.RightDoubleQuote, '\'', "''")]
    [InlineData(SmartyPantType.LeftAngleQuote, '<', "<<")]
    [InlineData(SmartyPantType.RightAngleQuote, '>', ">>")]
    [InlineData(SmartyPantType.Ellipsis, '.', "...")]
    [InlineData(SmartyPantType.Dash2, '-', "--")]
    [InlineData(SmartyPantType.Dash3, '-', "---")]
    public void ToStringReturnsTheSourceText(SmartyPantType type, char openingCharacter, string expected)
    {
        var pant = new SmartyPant { Type = type, OpeningCharacter = openingCharacter };

        Assert.Equal(expected, pant.ToString());
    }

    [Theory]
    [InlineData("This is a \"text\" and 'text'")]
    [InlineData("This is a ''text'' and ''text")]
    [InlineData("<<a>> <<b >>c")]
    [InlineData("a -- b --- c ----- d")]
    [InlineData("a... b ...c")]
    [InlineData("\"a\r\nb\" c\r\n")]
    [InlineData("> 'a' -- b\n> \"c\n")]
    [InlineData("- *'a'* **b...**\n")]
    public void Roundtrip(string markdown)
    {
        TestRoundtrip.RoundTrip(markdown, new MarkdownPipelineBuilder().UseSmartyPants());
    }
}
