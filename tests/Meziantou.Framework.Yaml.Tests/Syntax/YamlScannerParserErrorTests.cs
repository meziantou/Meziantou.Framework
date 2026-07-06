using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Syntax;

namespace Meziantou.Framework.Yaml.Tests.Syntax;
public sealed class YamlScannerParserErrorTests
{
    [Fact]
    public void Parse_InvalidEscape_ThrowsSyntaxErrorException()
    {
        const string Yaml = "key: \"\\q\"\n";

        var ex = Assert.Throws<SyntaxErrorException>(() => YamlSyntaxTree.Parse(Yaml));
        Assert.True(ex.Start.Index >= 0);
        Assert.True(ex.End.Index >= ex.Start.Index);
        Assert.Contains("escape", ex.Message);
    }

    [Fact]
    public void Parse_InvalidFlowSequence_ThrowsSemanticErrorException()
    {
        const string Yaml = "a: [1, 2\n";

        var ex = Assert.Throws<SemanticErrorException>(() => YamlSyntaxTree.Parse(Yaml));
        Assert.True(ex.Start.Index >= 0);
        Assert.True(ex.End.Index >= ex.Start.Index);
        Assert.Contains("flow sequence", ex.Message);
    }

    [Fact]
    public void Parser_ReadsUtf32Escapes()
    {
        // Scanner uses CharHelper.ConvertFromUtf32 for the \UXXXXXXXX escape sequence.
        const string Yaml = "k: \"\\U0001F600\"\n";
        var parser = Parser.CreateParser(new StringReader(Yaml));

        Scalar? scalar = null;
        while (parser.MoveNext())
        {
            if (parser.Current is Scalar currentScalar && currentScalar.Value is not "k")
            {
                scalar = currentScalar;
                break;
            }
        }

        Assert.NotNull(scalar);
        Assert.Equal("\U0001F600", scalar!.Value);
    }
}
