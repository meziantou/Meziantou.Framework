using Meziantou.Framework.Yaml.Model;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests;
public sealed class MaxDepthTests
{
    private const int DefaultMaxDepth = 64;

    [Fact]
    public void Parser_DefaultMaxDepth_ThrowsForDeeplyNestedFlowSequence()
    {
        var yaml = CreateDeepFlowSequenceYaml(DefaultMaxDepth + 1);
        var parser = Parser.CreateParser(new StringReader(yaml));

        var exception = Assert.Throws<YamlException>(() => Drain(parser));
        Assert.Contains("maximum nesting depth", exception.Message);
    }

    [Fact]
    public void Parser_CustomMaxDepth_AllowsConfiguredDepth()
    {
        var depth = DefaultMaxDepth + 1;
        var yaml = CreateDeepFlowSequenceYaml(depth);
        var parser = Parser.CreateParser(new StringReader(yaml), depth);

        var events = Drain(parser);

        Assert.NotEmpty(events);
    }

    [Fact]
    public void YamlStream_Load_DefaultMaxDepth_ThrowsForDeeplyNestedFlowMapping()
    {
        var yaml = CreateDeepFlowMappingYaml(DefaultMaxDepth + 1);

        var exception = Assert.Throws<YamlException>(() => YamlStream.Load(new StringReader(yaml)));
        Assert.Contains("maximum nesting depth", exception.Message);
    }

    [Fact]
    public void YamlSerializer_Deserialize_DefaultMaxDepth_ThrowsForDeeplyNestedFlowSequence()
    {
        var yaml = CreateDeepFlowSequenceYaml(DefaultMaxDepth + 1);

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<object>(yaml));
        Assert.Contains("maximum nesting depth", exception.Message);
    }

    [Fact]
    public void YamlSerializer_Deserialize_CustomMaxDepth_AllowsDeeperSequence()
    {
        var depth = DefaultMaxDepth + 8;
        var yaml = CreateDeepFlowSequenceYaml(depth);
        var options = new YamlSerializerOptions { MaxDepth = depth };

        var result = YamlSerializer.Deserialize<object>(yaml, options);

        Assert.NotNull(result);
    }

    [Fact]
    public void YamlSerializer_DeserializeYamlNode_UsesConfiguredMaxDepth()
    {
        var depth = DefaultMaxDepth + 8;
        var yaml = CreateDeepFlowSequenceYaml(depth);
        var options = new YamlSerializerOptions { MaxDepth = depth };

        var node = YamlSerializer.Deserialize<YamlElement>(yaml, options);

        Assert.IsType<YamlSequence>(node);
    }

    [Fact]
    public void YamlWriter_DefaultMaxDepth_ThrowsForDeeplyNestedSequence()
    {
        var writer = new YamlWriter(new StringBuilder());
        for (var i = 0; i < DefaultMaxDepth; i++)
        {
            writer.WriteStartSequence();
        }

        var exception = Assert.Throws<YamlException>(() => writer.WriteStartSequence());
        Assert.Contains("maximum nesting depth", exception.Message);
    }

    [Fact]
    public void YamlSerializer_Serialize_DefaultMaxDepth_ThrowsForDeeplyNestedObjectGraph()
    {
        var value = CreateNestedList(DefaultMaxDepth + 1);

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Serialize(value));
        Assert.Contains("maximum nesting depth", exception.Message);
    }

    [Fact]
    public void YamlSerializer_Serialize_CustomMaxDepth_AllowsDeeperObjectGraph()
    {
        var depth = DefaultMaxDepth + 8;
        var value = CreateNestedList(depth);
        var options = new YamlSerializerOptions { MaxDepth = depth };

        var yaml = YamlSerializer.Serialize(value, options);

        Assert.False(string.IsNullOrEmpty(yaml));
    }

    [Fact]
    public void YamlNode_FromObject_UsesConfiguredMaxDepth()
    {
        var depth = DefaultMaxDepth + 8;
        var value = CreateNestedList(depth);
        var options = new YamlSerializerOptions { MaxDepth = depth };

        var node = YamlNode.FromObject(value, options);

        Assert.IsType<YamlSequence>(node);
    }

    private static List<Events.ParsingEvent> Drain(IParser parser)
    {
        var events = new List<Events.ParsingEvent>();
        while (parser.MoveNext())
        {
            if (parser.Current is not null)
            {
                events.Add(parser.Current);
            }
        }

        return events;
    }

    private static string CreateDeepFlowSequenceYaml(int depth)
    {
        return new string('[', depth) + "0" + new string(']', depth);
    }

    private static string CreateDeepFlowMappingYaml(int depth)
    {
        var builder = new StringBuilder(depth * 6 + 1);
        for (var i = 0; i < depth; i++)
        {
            builder.Append("{a: ");
        }

        builder.Append('0');

        for (var i = 0; i < depth; i++)
        {
            builder.Append('}');
        }

        return builder.ToString();
    }

    private static object CreateNestedList(int depth)
    {
        object current = 0;
        for (var i = 0; i < depth; i++)
        {
            current = new List<object?> { current };
        }

        return current;
    }
}
