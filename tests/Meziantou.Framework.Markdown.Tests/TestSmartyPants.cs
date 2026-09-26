using Meziantou.Framework.Markdown.Extensions.SmartyPants;

namespace Meziantou.Framework.Markdown.Tests;

public class TestSmartyPants
{
    [Test]
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

    [Test]
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

    [Test]
    public void RecognizesSupplementaryCharacters()
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseSmartyPants()
            .Build();

        TestParser.TestSpec("\"𝜵\"𠮷\"𝜵\"𩸽\"", "<p>&quot;𝜵&ldquo;𠮷&rdquo;𝜵&ldquo;𩸽&rdquo;</p>", pipeline);
    }
}
