namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlExceptionFormattingTests
{
    [Fact]
    public void YamlException_FormatsMessageWithoutSourceName()
    {
        var ex = new YamlException(new Mark(1, 2, 3), new Mark(4, 5, 6), "boom");

        Assert.Contains("(Lin: 2, Col: 3, Chr: 1)", ex.Message);
        Assert.Contains("(Lin: 5, Col: 6, Chr: 4)", ex.Message);
        Assert.Contains("boom", ex.Message);
        Assert.Null(ex.SourceName);
        Assert.Equal(1, ex.Start.Index);
        Assert.Equal(4, ex.End.Index);
    }

    [Fact]
    public void YamlException_FormatsMessageWithSourceNameAndInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new YamlException("file.yaml", new Mark(0, 0, 0), new Mark(1, 0, 1), "outer", inner);

        Assert.Contains("file.yaml:", ex.Message);
        Assert.Contains("outer", ex.Message);
        Assert.Same(inner, ex.InnerException);
        Assert.Equal("file.yaml", ex.SourceName);
    }

    [Fact]
    public void SyntaxAndSemanticErrorExceptions_AreYamlExceptions()
    {
        var syntax = new SyntaxErrorException(new Mark(0, 0, 0), new Mark(1, 0, 1), "syntax");
        Assert.IsAssignableTo<YamlException>(syntax);

        var semantic = new SemanticErrorException(new Mark(0, 0, 0), new Mark(1, 0, 1), "semantic");
        Assert.IsAssignableTo<YamlException>(semantic);
    }
}
