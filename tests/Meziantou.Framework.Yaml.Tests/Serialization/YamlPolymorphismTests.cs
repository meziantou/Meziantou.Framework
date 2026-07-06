using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public class YamlPolymorphismTests
{
    [YamlPolymorphic]
    [YamlDerivedType(typeof(Dog), "dog", Tag = "!dog")]
    [YamlDerivedType(typeof(Cat), "cat", Tag = "!cat")]
    private abstract class Animal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Dog : Animal
    {
        public int BarkVolume { get; set; }
    }

    private sealed class Cat : Animal
    {
        public int Lives { get; set; }
    }

    [YamlPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
    [YamlDerivedType(typeof(JsonDog), "dog")]
    private abstract class JsonAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class JsonDog : JsonAnimal
    {
        public int BarkVolume { get; set; }
    }

    [YamlPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [YamlDerivedType(typeof(JsonDefaultCat), "cat")]
    [YamlDerivedType(typeof(JsonDefaultOther))]
    private abstract class JsonDefaultAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class JsonDefaultCat : JsonDefaultAnimal
    {
        public int Lives { get; set; }
    }

    private sealed class JsonDefaultOther : JsonDefaultAnimal
    {
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(YamlDefaultDog), "dog")]
    [YamlDerivedType(typeof(YamlDefaultOther))]
    private abstract class YamlDefaultAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class YamlDefaultDog : YamlDefaultAnimal
    {
        public int BarkVolume { get; set; }
    }

    private sealed class YamlDefaultOther : YamlDefaultAnimal
    {
    }

    [YamlPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [YamlDerivedType(typeof(JsonIntDog), 1)]
    [YamlDerivedType(typeof(JsonIntCat), 2)]
    private abstract class JsonIntAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class JsonIntDog : JsonIntAnimal
    {
        public int BarkVolume { get; set; }
    }

    private sealed class JsonIntCat : JsonIntAnimal
    {
        public int Lives { get; set; }
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(YamlIntDog), 1)]
    [YamlDerivedType(typeof(YamlIntCat), 2)]
    private abstract class YamlIntAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class YamlIntDog : YamlIntAnimal
    {
        public int BarkVolume { get; set; }
    }

    private sealed class YamlIntCat : YamlIntAnimal
    {
        public int Lives { get; set; }
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(Circle), "circle")]
    private class Shape
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Circle : Shape
    {
        public double Radius { get; set; }
    }

    [YamlPolymorphic(UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase)]
    [YamlDerivedType(typeof(YamlFallbackCircle), "circle")]
    private class YamlFallbackShape
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class YamlFallbackCircle : YamlFallbackShape
    {
        public double Radius { get; set; }
    }

    [YamlPolymorphic(TypeDiscriminatorPropertyName = "$type", UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase)]
    [YamlDerivedType(typeof(JsonOverriddenCircle), "circle")]
    private class JsonOverriddenShape
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class JsonOverriddenCircle : JsonOverriddenShape
    {
        public double Radius { get; set; }
    }

    [YamlPolymorphic(TypeDiscriminatorPropertyName = "$type", UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase)]
    [YamlDerivedType(typeof(JsonFallbackCircle), "circle")]
    private class JsonFallbackShape
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class JsonFallbackCircle : JsonFallbackShape
    {
        public double Radius { get; set; }
    }

    [Fact]
    public void SerializeEmitsPropertyDiscriminatorFirst()
    {
        Animal animal = new Dog { Name = "Rex", BarkVolume = 3 };
        var yaml = YamlSerializer.Serialize(animal, typeof(Animal));

        var typeIndex = yaml.IndexOf("$type:", StringComparison.Ordinal);
        var nameIndex = yaml.IndexOf("Name:", StringComparison.Ordinal);
        Assert.True(typeIndex >= 0);
        Assert.True(nameIndex > typeIndex);
        Assert.Contains("$type: dog", yaml);
        Assert.Contains("BarkVolume: 3", yaml);
    }

    [Fact]
    public void DeserializeSelectsDerivedTypeWhenDiscriminatorNotFirst()
    {
        var yaml = "Name: Rex\nBarkVolume: 3\n$type: dog\n";
        var value = YamlSerializer.Deserialize<Animal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<Dog>(value);
        Assert.Equal("Rex", value.Name);
        Assert.Equal(3, ((Dog)value).BarkVolume);
    }

    [Fact]
    public void DeserializeSelectsDerivedTypeFromJsonPolymorphicAttributes()
    {
        var yaml = "Name: Rex\nBarkVolume: 3\n$kind: dog\n";
        var value = YamlSerializer.Deserialize<JsonAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonDog>(value);
        Assert.Equal("Rex", value.Name);
        Assert.Equal(3, ((JsonDog)value).BarkVolume);
    }

    [Fact]
    public void UnknownDiscriminatorFailsByDefault()
    {
        var yaml = "Name: Rex\n$type: lizard\n";
        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Animal>(yaml));
    }

    [Fact]
    public void UnknownDiscriminatorCanFallBackToBase()
    {
        var yaml = "Name: Base\n$type: unknown\n";
        var value = YamlSerializer.Deserialize<Shape>(
            yaml,
            new YamlSerializerOptions
            {
                PolymorphismOptions = new YamlPolymorphismOptions
                {
                    UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase,
                },
            });

        Assert.NotNull(value);
        Assert.IsType<Shape>(value);
        Assert.Equal("Base", value.Name);
    }

    [Fact]
    public void YamlPolymorphicAttributeUnknownHandlingFallsBackToBase()
    {
        var yaml = "Name: Base\n$type: unknown\n";
        var value = YamlSerializer.Deserialize<YamlFallbackShape>(yaml);

        Assert.NotNull(value);
        Assert.IsType<YamlFallbackShape>(value);
        Assert.Equal("Base", value.Name);
    }

    [Fact]
    public void YamlPolymorphicAttributeUnknownHandlingOverridesJsonAttribute()
    {
        // YamlPolymorphic sets UnknownDerivedTypeHandling to FallBackToBase
        var yaml = "Name: Base\n$type: unknown\n";
        var value = YamlSerializer.Deserialize<JsonOverriddenShape>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonOverriddenShape>(value);
        Assert.Equal("Base", value.Name);
    }

    [Fact]
    public void TagDiscriminatorCanBeUsedWhenEnabled()
    {
        var yaml = "!dog\nName: Rex\nBarkVolume: 3\n";
        var value = YamlSerializer.Deserialize<Animal>(
            yaml,
            new YamlSerializerOptions
            {
                PolymorphismOptions = new YamlPolymorphismOptions
                {
                    DiscriminatorStyle = YamlTypeDiscriminatorStyle.Both,
                },
            });

        Assert.NotNull(value);
        Assert.IsType<Dog>(value);
        Assert.Equal(3, ((Dog)value).BarkVolume);
    }

    [Fact]
    public void SerializeCanEmitTagDiscriminatorOnly()
    {
        Animal animal = new Cat { Name = "Mittens", Lives = 9 };
        var yaml = YamlSerializer.Serialize(
            animal,
            typeof(Animal),
            new YamlSerializerOptions
            {
                PolymorphismOptions = new YamlPolymorphismOptions
                {
                    DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                },
            });

        Assert.Contains("!cat", yaml);
        Assert.DoesNotContain("$type:", yaml);
        Assert.Contains("Lives: 9", yaml);
    }

    [Fact]
    public void JsonDefaultDerivedTypeDeserializesWhenDiscriminatorIsMissing()
    {
        var yaml = "Name: Cupcake\n";
        var value = YamlSerializer.Deserialize<JsonDefaultAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonDefaultOther>(value);
        Assert.Equal("Cupcake", value.Name);
    }

    [Fact]
    public void JsonDefaultDerivedTypeDeserializesWhenDiscriminatorMatches()
    {
        var yaml = "type: cat\nName: Biscuit\nLives: 7\n";
        var value = YamlSerializer.Deserialize<JsonDefaultAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonDefaultCat>(value);
        Assert.Equal("Biscuit", value.Name);
        Assert.Equal(7, ((JsonDefaultCat)value).Lives);
    }

    [Fact]
    public void JsonDefaultDerivedTypeDeserializesWhenDiscriminatorIsUnknown()
    {
        var yaml = "type: lizard\nName: Gex\n";
        var value = YamlSerializer.Deserialize<JsonDefaultAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonDefaultOther>(value);
        Assert.Equal("Gex", value.Name);
    }

    [Fact]
    public void JsonDefaultDerivedTypeSerializesWithoutDiscriminator()
    {
        JsonDefaultAnimal animal = new JsonDefaultOther { Name = "Cupcake" };
        var yaml = YamlSerializer.Serialize(animal, typeof(JsonDefaultAnimal));

        Assert.DoesNotContain("type:", yaml);
        Assert.Contains("Name: Cupcake", yaml);
    }

    [Fact]
    public void JsonDefaultDerivedTypeSerializesWithDiscriminatorForNonDefaultType()
    {
        JsonDefaultAnimal animal = new JsonDefaultCat { Name = "Biscuit", Lives = 7 };
        var yaml = YamlSerializer.Serialize(animal, typeof(JsonDefaultAnimal));

        Assert.Contains("type: cat", yaml);
        Assert.Contains("Name: Biscuit", yaml);
    }

    [Fact]
    public void YamlDefaultDerivedTypeDeserializesWhenDiscriminatorIsMissing()
    {
        var yaml = "Name: Cupcake\n";
        var value = YamlSerializer.Deserialize<YamlDefaultAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<YamlDefaultOther>(value);
        Assert.Equal("Cupcake", value.Name);
    }

    [Fact]
    public void YamlDefaultDerivedTypeDeserializesWhenDiscriminatorMatches()
    {
        var yaml = "$type: dog\nName: Rex\nBarkVolume: 5\n";
        var value = YamlSerializer.Deserialize<YamlDefaultAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<YamlDefaultDog>(value);
        Assert.Equal("Rex", value.Name);
        Assert.Equal(5, ((YamlDefaultDog)value).BarkVolume);
    }

    [Fact]
    public void YamlDefaultDerivedTypeSerializesWithoutDiscriminator()
    {
        YamlDefaultAnimal animal = new YamlDefaultOther { Name = "Cupcake" };
        var yaml = YamlSerializer.Serialize(animal, typeof(YamlDefaultAnimal));

        Assert.DoesNotContain("$type:", yaml);
        Assert.Contains("Name: Cupcake", yaml);
    }

    [Fact]
    public void YamlDefaultDerivedTypeSerializesWithDiscriminatorForNonDefaultType()
    {
        YamlDefaultAnimal animal = new YamlDefaultDog { Name = "Rex", BarkVolume = 5 };
        var yaml = YamlSerializer.Serialize(animal, typeof(YamlDefaultAnimal));

        Assert.Contains("$type: dog", yaml);
        Assert.Contains("Name: Rex", yaml);
    }

    [Fact]
    public void JsonIntDiscriminatorSerializesCorrectly()
    {
        JsonIntAnimal animal = new JsonIntDog { Name = "Rex", BarkVolume = 3 };
        var yaml = YamlSerializer.Serialize(animal, typeof(JsonIntAnimal));

        Assert.Contains("$type: 1", yaml);
        Assert.Contains("BarkVolume: 3", yaml);
    }

    [Fact]
    public void JsonIntDiscriminatorDeserializesCorrectly()
    {
        var yaml = "$type: 2\nName: Mittens\nLives: 9\n";
        var value = YamlSerializer.Deserialize<JsonIntAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonIntCat>(value);
        Assert.Equal("Mittens", value.Name);
        Assert.Equal(9, ((JsonIntCat)value).Lives);
    }

    [Fact]
    public void YamlIntDiscriminatorSerializesCorrectly()
    {
        YamlIntAnimal animal = new YamlIntDog { Name = "Rex", BarkVolume = 3 };
        var yaml = YamlSerializer.Serialize(animal, typeof(YamlIntAnimal));

        Assert.Contains("$type: 1", yaml);
        Assert.Contains("BarkVolume: 3", yaml);
    }

    [Fact]
    public void YamlIntDiscriminatorDeserializesCorrectly()
    {
        var yaml = "$type: 2\nName: Mittens\nLives: 9\n";
        var value = YamlSerializer.Deserialize<YamlIntAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<YamlIntCat>(value);
        Assert.Equal("Mittens", value.Name);
        Assert.Equal(9, ((YamlIntCat)value).Lives);
    }

    [Fact]
    public void JsonPolymorphicAttributeUnknownHandlingFallsBackToBase()
    {
        var yaml = "Name: Base\n$type: unknown\n";
        var value = YamlSerializer.Deserialize<JsonFallbackShape>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonFallbackShape>(value);
        Assert.Equal("Base", value.Name);
    }
}
