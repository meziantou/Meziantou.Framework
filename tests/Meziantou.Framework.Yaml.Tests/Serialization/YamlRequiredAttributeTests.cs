using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlRequiredAttributeTests
{
    [Fact]
    public void Deserialize_WhenYamlRequiredMissing_ThrowsYamlException()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<YamlRequiredModel>("b: 2\n"));
        Assert.Contains("a", ex.Message);
    }

    [Fact]
    public void Deserialize_WhenJsonRequiredMissing_ThrowsYamlException()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<JsonRequiredModel>("b: 2\n"));
        Assert.Contains("a", ex.Message);
    }

    [Fact]
    public void Deserialize_WhenRequiredPresent_Succeeds()
    {
        var value = YamlSerializer.Deserialize<YamlRequiredModel>("A: 1\nB: 2\n")!;
        Assert.Equal(1, value.A);
        Assert.Equal(2, value.B);
    }

    private sealed class YamlRequiredModel
    {
        [YamlRequired]
        public int A { get; set; }

        public int B { get; set; }
    }

    private sealed class JsonRequiredModel
    {
        [YamlRequired]
        public int A { get; set; }

        public int B { get; set; }
    }
}
