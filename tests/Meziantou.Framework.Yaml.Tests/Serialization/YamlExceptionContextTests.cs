namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlExceptionContextTests
{
    [Fact]
    public void ReflectionDeserializationErrorsIncludeSourceNameAndLocation()
    {
        var options = new YamlSerializerOptions
        {
            SourceName = "config.yaml",
        };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<int>("a: b", options));
        Assert.Equal("config.yaml", exception.SourceName);
        Assert.Contains("Lin:", exception.Message);
        Assert.Contains("Col:", exception.Message);
    }
}
