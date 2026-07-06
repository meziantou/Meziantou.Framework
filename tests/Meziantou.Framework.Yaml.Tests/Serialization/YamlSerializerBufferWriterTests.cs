using System.Buffers;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlSerializerBufferWriterTests
{
    [Fact]
    public void Serialize_IBufferWriter_ShouldWriteYaml()
    {
        var writer = new ArrayBufferWriter<char>();

        YamlSerializer.Serialize(writer, new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 });

        var yaml = new string(writer.WrittenSpan);
        Assert.Contains("a:", yaml);
        Assert.Contains("1", yaml);
    }

    [Fact]
    public void Serialize_IBufferWriter_WithContext_ShouldWriteYaml()
    {
        var writer = new ArrayBufferWriter<char>();
        var context = new TestYamlSerializerContext();

        YamlSerializer.Serialize(writer, new GeneratedPerson { FirstName = "Bob", Age = 42 }, context);

        var yaml = new string(writer.WrittenSpan);
        Assert.Contains("first_name", yaml);
        Assert.Contains("Bob", yaml);
        Assert.Contains("Age", yaml);
    }
}
