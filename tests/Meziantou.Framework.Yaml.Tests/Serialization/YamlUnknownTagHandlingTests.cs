using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public class YamlUnknownTagHandlingTests
{
    // ---- Model types ----

    [YamlPolymorphic(DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag)]
    [YamlDerivedType(typeof(AttributeDog), Tag = "!dog")]
    [YamlDerivedType(typeof(AttributeCat), Tag = "!cat")]
    private class AttributeAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class AttributeDog : AttributeAnimal
    {
        public int BarkVolume { get; set; }
    }

    private sealed class AttributeCat : AttributeAnimal
    {
        public bool Indoor { get; set; }
    }

    [YamlPolymorphic(DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag, UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase)]
    [YamlDerivedType(typeof(FallbackDog), Tag = "!dog")]
    private class FallbackAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class FallbackDog : FallbackAnimal
    {
        public int BarkVolume { get; set; }
    }

    private abstract class RuntimeAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class RuntimeDog : RuntimeAnimal
    {
        public int BarkVolume { get; set; }
    }

    private sealed class RuntimeCat : RuntimeAnimal
    {
        public bool Indoor { get; set; }
    }

    // ---- Attribute-based: unknown tag with default (Fail) handling ----

    [Fact]
    public void UnknownTagFailsByDefaultWithAttributes()
    {
        var yaml = "!lizard\nName: Gecko\n";
        var ex = Assert.Throws<YamlException>(
            () => YamlSerializer.Deserialize<AttributeAnimal>(yaml));
        Assert.Contains("!lizard", ex.Message);
    }

    [Fact]
    public void KnownTagWorksWithAttributes()
    {
        var yaml = "!dog\nName: Rex\nBarkVolume: 5\n";
        var value = YamlSerializer.Deserialize<AttributeAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<AttributeDog>(value);
        Assert.Equal("Rex", value.Name);
        Assert.Equal(5, ((AttributeDog)value).BarkVolume);
    }

    [Fact]
    public void NoTagDeserializesToBaseTypeWithAttributes()
    {
        var yaml = "Name: Plain\n";
        var value = YamlSerializer.Deserialize<AttributeAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<AttributeAnimal>(value);
        Assert.Equal("Plain", value.Name);
    }

    // ---- Attribute-based: FallBackToBase ----

    [Fact]
    public void UnknownTagFallsBackToBaseWhenConfigured()
    {
        var yaml = "!lizard\nName: Gecko\n";
        var value = YamlSerializer.Deserialize<FallbackAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<FallbackAnimal>(value);
        Assert.Equal("Gecko", value.Name);
    }

    // ---- Options-level: Fail ----

    [Fact]
    public void UnknownTagFailsWithOptionsLevelFail()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.Fail,
                DerivedTypeMappings =
                {
                    [typeof(RuntimeAnimal)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(RuntimeDog), "dog") { Tag = "!dog" },
                        new YamlDerivedType(typeof(RuntimeCat), "cat") { Tag = "!cat" },
                    },
                },
            },
        };

        var yaml = "!parrot\nName: Polly\n";
        var ex = Assert.Throws<YamlException>(
            () => YamlSerializer.Deserialize<RuntimeAnimal>(yaml, options));
        Assert.Contains("!parrot", ex.Message);
    }

    [Fact]
    public void KnownTagWorksWithRuntime()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                DerivedTypeMappings =
                {
                    [typeof(RuntimeAnimal)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(RuntimeDog), "dog") { Tag = "!dog" },
                    },
                },
            },
        };

        var yaml = "!dog\nName: Rex\nBarkVolume: 3\n";
        var value = YamlSerializer.Deserialize<RuntimeAnimal>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<RuntimeDog>(value);
        Assert.Equal("Rex", value.Name);
        Assert.Equal(3, ((RuntimeDog)value).BarkVolume);
    }

    // ---- Options-level: FallBackToBase ----

    private class ConcreteAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ConcreteDog : ConcreteAnimal
    {
        public int BarkVolume { get; set; }
    }

    [Fact]
    public void UnknownTagFallsBackToBaseWithOptions()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase,
                DerivedTypeMappings =
                {
                    [typeof(ConcreteAnimal)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(ConcreteDog), "dog") { Tag = "!dog" },
                    },
                },
            },
        };

        var yaml = "!parrot\nName: Polly\n";
        var value = YamlSerializer.Deserialize<ConcreteAnimal>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<ConcreteAnimal>(value);
        Assert.Equal("Polly", value.Name);
    }

    // ---- Attribute-level override takes precedence over options ----

    [Fact]
    public void AttributeUnknownHandlingOverridesOptions()
    {
        // FallbackAnimal has FallBackToBase in attribute; options say Fail — attribute wins
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.Fail,
            },
        };

        var yaml = "!lizard\nName: Gecko\n";
        var value = YamlSerializer.Deserialize<FallbackAnimal>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<FallbackAnimal>(value);
        Assert.Equal("Gecko", value.Name);
    }

    // ---- Dictionary of polymorphic values with unknown tags ----

    [Fact]
    public void UnknownTagInDictionaryValueFails()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.Fail,
                DerivedTypeMappings =
                {
                    [typeof(RuntimeAnimal)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(RuntimeDog), "dog") { Tag = "!dog" },
                    },
                },
            },
        };

        var yaml = "rex: !dog\n  Name: Rex\n  BarkVolume: 3\npolly: !parrot\n  Name: Polly\n";
        Assert.Throws<YamlException>(
            () => YamlSerializer.Deserialize<Dictionary<string, RuntimeAnimal>>(yaml, options));
    }

    // ---- Tag-only entries (no discriminator) with unknown tags ----

    [Fact]
    public void UnknownTagFailsWithTagOnlyEntries()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.Fail,
                DerivedTypeMappings =
                {
                    [typeof(RuntimeAnimal)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(RuntimeDog)) { Tag = "!dog" },
                        new YamlDerivedType(typeof(RuntimeCat)) { Tag = "!cat" },
                    },
                },
            },
        };

        var yaml = "!fish\nName: Nemo\n";
        var ex = Assert.Throws<YamlException>(
            () => YamlSerializer.Deserialize<RuntimeAnimal>(yaml, options));
        Assert.Contains("!fish", ex.Message);
    }
}
