namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlSerializerStreamTests
{
    [Fact]
    public void Serialize_Stream_ShouldWriteYamlAndLeaveStreamOpen()
    {
        using var stream = new MemoryStream();

        YamlSerializer.Serialize(stream, new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 });

        Assert.True(stream.CanRead);
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var yaml = reader.ReadToEnd();

        Assert.Contains("a:", yaml);
        Assert.Contains("1", yaml);
    }

    [Fact]
    public void Deserialize_Stream_ShouldReadYamlAndLeaveStreamOpen()
    {
        var data = Encoding.UTF8.GetBytes("a: 1\n");
        using var stream = new MemoryStream(data);

        var dict = YamlSerializer.Deserialize<Dictionary<string, int>>(stream);

        Assert.NotNull(dict);
        Assert.Equal(1, dict["a"]);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void SerializeAndDeserialize_Stream_WithContext()
    {
        var context = new TestYamlSerializerContext();
        var person = new GeneratedPerson { FirstName = "Bob", Age = 42 };

        using var stream = new MemoryStream();
        YamlSerializer.Serialize(stream, person, context);

        Assert.True(stream.CanRead);
        stream.Position = 0;

        var roundtripped = YamlSerializer.Deserialize<GeneratedPerson>(stream, context);

        Assert.NotNull(roundtripped);
        Assert.Equal("Bob", roundtripped.FirstName);
        Assert.Equal(42, roundtripped.Age);
    }
}

