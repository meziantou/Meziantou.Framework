using Meziantou.Framework.Yaml.Model;

namespace Meziantou.Framework.Yaml.Tests;
public sealed class YamlModelAnchorAliasTests
{
    [Fact]
    public void Load_ShouldResolveAnchorAlias_ByMaterializingACopy()
    {
        var yaml = """
field1: &data ABCD
field2: *data
""";

        var stream = YamlStream.Load(new StringReader(yaml));
        Assert.Single(stream);

        var mapping = (YamlMapping)stream[0].Contents!;

        var field1 = (YamlValue)mapping["field1"]!;
        var field2 = (YamlValue)mapping["field2"]!;

        Assert.Equal("data", field1.Anchor);
        Assert.Equal("ABCD", field1.Value);

        // The model API doesn't preserve aliases as a distinct node type: we materialize a copy.
        Assert.Null(field2.Anchor);
        Assert.Equal("ABCD", field2.Value);
    }
}
