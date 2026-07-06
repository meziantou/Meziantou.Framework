namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlSerializerTryDeserializeTests
{
    [Fact]
    public void TryDeserialize_InvalidYaml_ReturnsFalse()
    {
        var ok = YamlSerializer.TryDeserialize<Dictionary<string, int>>("a: [", out var value);

        Assert.False(ok);
        Assert.Null(value);
    }

    [Fact]
    public void TryDeserialize_InvalidYaml_WithContext_ReturnsFalse()
    {
        var context = new TestYamlSerializerContext();

        var ok = YamlSerializer.TryDeserialize<GeneratedPerson>("a: [", context, out var value);

        Assert.False(ok);
        Assert.Null(value);
    }

    [Fact]
    public void TryDeserialize_InvalidYaml_FromTextReader_ReturnsFalse()
    {
        using var reader = new StringReader("a: [");

        var ok = YamlSerializer.TryDeserialize<Dictionary<string, int>>(reader, out var value);

        Assert.False(ok);
        Assert.Null(value);
    }

    [Fact]
    public void TryDeserialize_ValidYaml_ReturnsTrue()
    {
        var ok = YamlSerializer.TryDeserialize<Dictionary<string, int>>("a: 1\n", out var value);

        Assert.True(ok);
        Assert.NotNull(value);
        Assert.Equal(1, value["a"]);
    }

    [Fact]
    public void TryDeserialize_ValidYaml_FromStream_ReturnsTrue()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a: 1\n"));

        var ok = YamlSerializer.TryDeserialize<Dictionary<string, int>>(stream, out var value);

        Assert.True(ok);
        Assert.NotNull(value);
        Assert.Equal(1, value["a"]);
    }
}
