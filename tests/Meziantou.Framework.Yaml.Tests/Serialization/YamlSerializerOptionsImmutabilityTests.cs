using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlSerializerOptionsImmutabilityTests
{
    private sealed class NoopInt32Converter : YamlConverter<int>
    {
        public override int Read(YamlReader reader)
        {
            reader.Skip();
            return 0;
        }

        public override void Write(YamlWriter writer, int value)
        {
            writer.WriteScalar(value);
        }
    }

    [Fact]
    public void Converters_AreCopiedFromInitializer()
    {
        var list = new List<YamlConverter>
        {
            new NoopInt32Converter(),
        };

        var options = new YamlSerializerOptions
        {
            Converters = list,
        };

        list.Add(new NoopInt32Converter());

        Assert.Equal(1, options.Converters.Count);
    }

    [Fact]
    public void Context_RejectsOptionsWithDifferentTypeInfoResolver()
    {
        var resolver = new DummyResolver();
        var options = new YamlSerializerOptions
        {
            TypeInfoResolver = resolver,
        };

        _ = Assert.Throws<ArgumentException>(() => new DummyContext(options));
    }

    private sealed class DummyResolver : IYamlTypeInfoResolver
    {
        public YamlTypeInfo? GetTypeInfo(Type type, YamlSerializerOptions options) => null;
    }

    private sealed class DummyContext : YamlSerializerContext
    {
        public DummyContext(YamlSerializerOptions options) : base(options)
        {
        }

        public override YamlTypeInfo? GetTypeInfo(Type type, YamlSerializerOptions options) => null;
    }
}
