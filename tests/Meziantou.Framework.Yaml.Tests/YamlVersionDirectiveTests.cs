using Meziantou.Framework.Yaml.Syntax;

namespace Meziantou.Framework.Yaml.Tests;
public sealed class YamlVersionDirectiveTests
{
    [Fact]
    public void Parse_WithYaml12Directive_ShouldSucceed()
    {
        var yaml = "%YAML 1.2\n---\na: 1\n";

        _ = YamlSyntaxTree.Parse(yaml);
    }

    [Fact]
    public void Parse_WithUnsupportedYamlDirective_ShouldThrow()
    {
        var yaml = "%YAML 1.3\n---\na: 1\n";

        var ex = Assert.Throws<SemanticErrorException>(() => YamlSyntaxTree.Parse(yaml));
        Assert.Contains("incompatible", ex.Message);
    }
}
