namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlFlowPlainScalarColonTests
{
    [Fact]
    public void FlowSequence_WithColonInPlainScalar_ShouldDeserializeAsString()
    {
        var yaml = "[x:x]\n";

        var list = YamlSerializer.Deserialize<List<string>>(yaml);

        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal("x:x", list[0]);
    }
}
