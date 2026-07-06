namespace Meziantou.Framework.Yaml.Tests.Serialization;
public class YamlSerializerErrorHandlingTests
{
    [Fact]
    public void Reflection_ThrowsOnIntegerOverflow_WithLocation()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<int>("999999999999999999999"));
        Assert.Contains("Lin:", ex.Message);
        Assert.Contains("Col:", ex.Message);
    }

    [Fact]
    public void Reflection_ThrowsOnTypeMismatch_MappingToScalar()
    {
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<int>("a: 1"));
    }

    [Fact]
    public void Reflection_ThrowsOnTypeMismatch_ScalarToMapping()
    {
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Dictionary<string, int>>("1"));
    }

    [Fact]
    public void Reflection_ThrowsOnTypeMismatch_MappingToSequence()
    {
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<List<int>>("a: 1"));
    }

    [Fact]
    public void SourceGen_ThrowsOnIntegerOverflow_WithLocation()
    {
        var context = TestYamlSerializerContext.Default;
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("999999999999999999999", context.Int32));
        Assert.Contains("Lin:", ex.Message);
        Assert.Contains("Col:", ex.Message);
    }

    [Fact]
    public void SourceGen_ThrowsOnTypeMismatch_MappingToScalar()
    {
        var context = TestYamlSerializerContext.Default;
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("a: 1", context.Int32));
    }

    [Fact]
    public void SourceGen_ThrowsOnTypeMismatch_ScalarToMapping()
    {
        var context = TestYamlSerializerContext.Default;
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("1", context.DictionaryStringInt32));
    }

    [Fact]
    public void SourceGen_ThrowsOnTypeMismatch_MappingToSequence()
    {
        var context = TestYamlSerializerContext.Default;
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("a: 1", context.ListInt32));
    }

    [Fact]
    public void SourceGen_PropagatesSourceName()
    {
        var context = new TestYamlSerializerContext(new YamlSerializerOptions { SourceName = "input.yml" });
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("a: 1", context.Int32));
        Assert.Equal("input.yml", ex.SourceName);
    }
}

