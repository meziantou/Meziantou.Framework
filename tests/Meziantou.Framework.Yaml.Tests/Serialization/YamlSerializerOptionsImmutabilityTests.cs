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
    public void DerivedTypeMappings_AreCopiedFromInitializer()
    {
        var derivedTypes = new List<YamlDerivedType> { new(typeof(ImmutabilityDog), "dog") };
        var polymorphismOptions = new YamlPolymorphismOptions
        {
            DerivedTypeMappings =
            {
                [typeof(ImmutabilityAnimal)] = derivedTypes,
            },
        };

        var options = new YamlSerializerOptions { PolymorphismOptions = polymorphismOptions };
        derivedTypes.Add(new YamlDerivedType(typeof(ImmutabilityCat), "cat"));

        Assert.HasCount(1, options.PolymorphismOptions.DerivedTypeMappings[typeof(ImmutabilityAnimal)]);
        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<ImmutabilityAnimal>("$type: cat\n", options));
    }

    [Fact]
    public void DerivedTypeMappings_RejectMutationOnceAssigned()
    {
        var polymorphismOptions = new YamlPolymorphismOptions
        {
            DerivedTypeMappings =
            {
                [typeof(ImmutabilityAnimal)] = [new YamlDerivedType(typeof(ImmutabilityDog), "dog")],
            },
        };

        var options = new YamlSerializerOptions { PolymorphismOptions = polymorphismOptions };
        var copy = options with { WriteIndented = false };

        Assert.Same(polymorphismOptions, options.PolymorphismOptions);
        Assert.Throws<NotSupportedException>(() => polymorphismOptions.DerivedTypeMappings.Add(typeof(ImmutabilityCat), []));
        Assert.Throws<NotSupportedException>(() => copy.PolymorphismOptions.DerivedTypeMappings.Remove(typeof(ImmutabilityAnimal)));
        Assert.Throws<NotSupportedException>(() => copy.PolymorphismOptions.DerivedTypeMappings[typeof(ImmutabilityAnimal)].Add(new YamlDerivedType(typeof(ImmutabilityCat), "cat")));
        Assert.HasCount(1, options.PolymorphismOptions.DerivedTypeMappings);
    }

    [Fact]
    public void DerivedTypeMappings_OfDefaultOptionsAreReadOnly()
    {
        Assert.Throws<NotSupportedException>(() => YamlSerializerOptions.Default.PolymorphismOptions.DerivedTypeMappings.Add(typeof(ImmutabilityAnimal), []));
        Assert.Throws<NotSupportedException>(() => new YamlSerializerOptions().PolymorphismOptions.DerivedTypeMappings.Add(typeof(ImmutabilityAnimal), []));
        Assert.Empty(YamlSerializerOptions.Default.PolymorphismOptions.DerivedTypeMappings);
    }

    [Fact]
    public void DerivedTypeMappings_RejectNullEntries()
    {
        Assert.Throws<ArgumentException>(() => new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions { DerivedTypeMappings = { [typeof(ImmutabilityAnimal)] = null! } },
        });
        Assert.Throws<ArgumentException>(() => new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions { DerivedTypeMappings = { [typeof(ImmutabilityAnimal)] = [null!] } },
        });
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

    private abstract class ImmutabilityAnimal
    {
    }

    private sealed class ImmutabilityDog : ImmutabilityAnimal
    {
    }

    private sealed class ImmutabilityCat : ImmutabilityAnimal
    {
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
