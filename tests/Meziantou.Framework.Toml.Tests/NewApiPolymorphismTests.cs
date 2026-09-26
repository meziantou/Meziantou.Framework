using System;
using System.Text.Json.Serialization;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public class NewApiPolymorphismTests
{
    [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [TomlDerivedType(typeof(Cat), "cat")]
    [TomlDerivedType(typeof(Dog), "dog")]
    private abstract class Animal
    {
        public string? Name { get; set; }
    }

    private sealed class Cat : Animal
    {
        public int Lives { get; set; }
    }

    private sealed class Dog : Animal
    {
        public bool GoodBoy { get; set; }
    }

    private sealed class TwoLevelPolymorphicConfig
    {
        public required DepartLevel Depart { get; set; }
    }

    [TomlDerivedType(typeof(Depart1), "dd1")]
    private abstract class DepartLevel
    {
        public required string Name { get; set; }

        public required GroupLevel Group { get; set; }
    }

    private sealed class Depart1 : DepartLevel
    {
        public required string Field1 { get; set; }
    }

    [TomlDerivedType(typeof(Group2), "gg2")]
    private abstract class GroupLevel
    {
        public required string Name { get; set; }
    }

    private sealed class Group2 : GroupLevel
    {
        public required string Field2 { get; set; }
    }

    [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [TomlDerivedType(typeof(LiteralDottedShape), "literal")]
    private abstract class DottedShape
    {
    }

    [TomlDottedKeyHandling(TomlDottedKeyHandling.Literal)]
    private sealed class LiteralDottedShape : DottedShape
    {
        [TomlPropertyName("size.width")]
        public int Width { get; set; }
    }

    [Fact]
    public void Serialize_Polymorphic_KeepsLiteralDottedKeysOfTheDerivedType()
    {
        var options = new TomlSerializerOptions { DottedKeyHandling = TomlDottedKeyHandling.Expand };
        DottedShape value = new LiteralDottedShape { Width = 3 };

        var toml = TomlSerializer.Serialize(value, options);

        Assert.Contains("\"size.width\" = 3", toml, StringComparison.Ordinal);
        var roundtrip = Assert.IsType<LiteralDottedShape>(TomlSerializer.Deserialize<DottedShape>(toml, options));
        Assert.Equal(3, roundtrip.Width);
    }

    [Fact]
    public void Serialize_Polymorphic_WritesDiscriminator()
    {
        Animal value = new Cat { Name = "Nyan", Lives = 9 };
        var toml = TomlSerializer.Serialize(value);

        Assert.Contains("kind", toml);
        Assert.Contains("cat", toml);
        Assert.Contains("Name", toml);
        Assert.Contains("Nyan", toml);
        Assert.Contains("Lives", toml);
        Assert.Contains("9", toml);
    }

    [Fact]
    public void Deserialize_Polymorphic_UsesDiscriminatorWhenNotFirst()
    {
        var toml =
            """
            kind = "cat"
            Name = "Nyan"
            Lives = 9
            """;

        var result = TomlSerializer.Deserialize<Animal>(toml);

        Assert.IsType<Cat>(result);
        var cat = (Cat)result!;
        Assert.Equal("Nyan", cat.Name);
        Assert.Equal(9, cat.Lives);
    }

    [Fact]
    public void Deserialize_Polymorphic_CanReadNestedPolymorphicTables()
    {
        var toml =
            """
            [Depart]
            "$type" = "dd1"
            Name = "this is depart1"
            Field1 = "depart 1 field"

            [Depart.Group]
            "$type" = "gg2"
            Name = "this is group 2"
            Field2 = "group 2 field"
            """;

        var result = TomlSerializer.Deserialize<TwoLevelPolymorphicConfig>(toml);

        Assert.NotNull(result);
        Assert.IsAssignableTo<Depart1>(result!.Depart);
        var depart = (Depart1)result.Depart;
        Assert.Equal("this is depart1", depart.Name);
        Assert.Equal("depart 1 field", depart.Field1);
        Assert.IsAssignableTo<Group2>(depart.Group);
        var group = (Group2)depart.Group;
        Assert.Equal("this is group 2", group.Name);
        Assert.Equal("group 2 field", group.Field2);
    }

    [Fact]
    public void Deserialize_Polymorphic_UnknownDiscriminator_FailsByDefault()
    {
        var toml =
            """
            kind = "fox"
            Name = "X"
            """;

        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Animal>(toml));
    }

    [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [TomlDerivedType(typeof(DerivedPet), "derived")]
    private class Pet
    {
        public string? Name { get; set; }
    }

    private sealed class DerivedPet : Pet
    {
        public int Age { get; set; }
    }

    [Fact]
    public void Deserialize_Polymorphic_UnknownDiscriminator_CanFallbackToBase()
    {
        var toml =
            """
            kind = "unknown"
            Name = "Base"
            extra = 123
            """;

        var options = new TomlSerializerOptions
        {
            PolymorphismOptions = new TomlPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "kind",
                UnknownDerivedTypeHandling = TomlUnknownDerivedTypeHandling.FallBackToBaseType,
            },
        };

        var result = TomlSerializer.Deserialize<Pet>(toml, options);
        Assert.IsType<Pet>(result);
        Assert.Equal("Base", result!.Name);
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(JsonCat), "cat")]
    private abstract class JsonAnimal
    {
        public string? Name { get; set; }
    }

    private sealed class JsonCat : JsonAnimal
    {
        public int Lives { get; set; }
    }

    [Fact]
    public void Deserialize_Polymorphic_RespectsSystemTextJsonAttributes()
    {
        var toml =
            """
            kind = "cat"
            Name = "Ada"
            Lives = 9
            """;

        var result = TomlSerializer.Deserialize<JsonAnimal>(toml);
        Assert.IsType<JsonCat>(result);
        Assert.Equal(9, ((JsonCat)result!).Lives);
    }

    // --- Feature 1: Default Derived Type (no discriminator) ---

    [TomlPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [TomlDerivedType(typeof(DefaultCircle))]
    [TomlDerivedType(typeof(DefaultSquare), "square")]
    private abstract class DefaultShape
    {
        public string? Color { get; set; }
    }

    private sealed class DefaultCircle : DefaultShape
    {
        public double Radius { get; set; }
    }

    private sealed class DefaultSquare : DefaultShape
    {
        public double Side { get; set; }
    }

    [Fact]
    public void Serialize_DefaultDerivedType_OmitsDiscriminator()
    {
        DefaultShape value = new DefaultCircle { Color = "red", Radius = 5.0 };
        var toml = TomlSerializer.Serialize(value);

        Assert.DoesNotContain("type", toml);
        Assert.Contains("Color", toml);
        Assert.Contains("red", toml);
        Assert.Contains("Radius", toml);
        Assert.Contains("5", toml);
    }

    [Fact]
    public void Serialize_NonDefaultDerivedType_WritesDiscriminator()
    {
        DefaultShape value = new DefaultSquare { Color = "blue", Side = 3.0 };
        var toml = TomlSerializer.Serialize(value);

        Assert.Contains("type = \"square\"", toml);
        Assert.Contains("Color", toml);
        Assert.Contains("blue", toml);
        Assert.Contains("Side", toml);
        Assert.Contains("3", toml);
    }

    [Fact]
    public void Deserialize_DefaultDerivedType_WhenMissingDiscriminator()
    {
        var toml =
            """
            Color = "red"
            Radius = 5.0
            """;

        var result = TomlSerializer.Deserialize<DefaultShape>(toml);

        Assert.IsType<DefaultCircle>(result);
        var circle = (DefaultCircle)result!;
        Assert.Equal("red", circle.Color);
        Assert.Equal(5.0, circle.Radius);
    }

    [Fact]
    public void Deserialize_DefaultDerivedType_WhenUnknownDiscriminator()
    {
        var toml =
            """
            type = "triangle"
            Color = "green"
            """;

        var result = TomlSerializer.Deserialize<DefaultShape>(toml);

        Assert.IsType<DefaultCircle>(result);
        var circle = (DefaultCircle)result!;
        Assert.Equal("green", circle.Color);
    }

    [Fact]
    public void Deserialize_NonDefaultDerivedType_WithDiscriminator()
    {
        var toml =
            """
            type = "square"
            Color = "blue"
            Side = 3.0
            """;

        var result = TomlSerializer.Deserialize<DefaultShape>(toml);

        Assert.IsType<DefaultSquare>(result);
        var square = (DefaultSquare)result!;
        Assert.Equal("blue", square.Color);
        Assert.Equal(3.0, square.Side);
    }

    [Fact]
    public void Roundtrip_DefaultDerivedType()
    {
        DefaultShape original = new DefaultCircle { Color = "red", Radius = 5.0 };
        var toml = TomlSerializer.Serialize(original);
        var result = TomlSerializer.Deserialize<DefaultShape>(toml);

        Assert.IsType<DefaultCircle>(result);
        var circle = (DefaultCircle)result!;
        Assert.Equal("red", circle.Color);
        Assert.Equal(5.0, circle.Radius);
    }

    [Fact]
    public void Roundtrip_NonDefaultDerivedType()
    {
        DefaultShape original = new DefaultSquare { Color = "blue", Side = 3.0 };
        var toml = TomlSerializer.Serialize(original);
        var result = TomlSerializer.Deserialize<DefaultShape>(toml);

        Assert.IsType<DefaultSquare>(result);
        var square = (DefaultSquare)result!;
        Assert.Equal("blue", square.Color);
        Assert.Equal(3.0, square.Side);
    }

    // Default derived type with JsonDerivedType (no discriminator is 1-arg constructor)
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(JsonDefaultCircle))]
    [JsonDerivedType(typeof(JsonDefaultSquare), "square")]
    private abstract class JsonDefaultShape
    {
        public string? Color { get; set; }
    }

    private sealed class JsonDefaultCircle : JsonDefaultShape
    {
        public double Radius { get; set; }
    }

    private sealed class JsonDefaultSquare : JsonDefaultShape
    {
        public double Side { get; set; }
    }

    [Fact]
    public void Serialize_JsonDefaultDerivedType_OmitsDiscriminator()
    {
        JsonDefaultShape value = new JsonDefaultCircle { Color = "red", Radius = 5.0 };
        var toml = TomlSerializer.Serialize(value);

        Assert.DoesNotContain("kind", toml);
        Assert.Contains("Color", toml);
        Assert.Contains("Radius", toml);
    }

    [Fact]
    public void Deserialize_JsonDefaultDerivedType_WhenMissingDiscriminator()
    {
        var toml =
            """
            Color = "red"
            Radius = 5.0
            """;

        var result = TomlSerializer.Deserialize<JsonDefaultShape>(toml);

        Assert.IsType<JsonDefaultCircle>(result);
        Assert.Equal("red", ((JsonDefaultCircle)result!).Color);
        Assert.Equal(5.0, ((JsonDefaultCircle)result).Radius);
    }

    // --- Feature 2: UnknownDerivedTypeHandling on attribute ---

    [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = TomlUnknownDerivedTypeHandling.FallBackToBaseType)]
    [TomlDerivedType(typeof(AttrFallbackDerived), "derived")]
    private class AttrFallbackBase
    {
        public string? Name { get; set; }
    }

    private sealed class AttrFallbackDerived : AttrFallbackBase
    {
        public int Extra { get; set; }
    }

    [Fact]
    public void Deserialize_UnknownDiscriminator_FallbackViaAttribute()
    {
        var toml =
            """
            kind = "unknown"
            Name = "test"
            """;

        // No options override needed - attribute sets FallBackToBaseType
        var result = TomlSerializer.Deserialize<AttrFallbackBase>(toml);

        Assert.IsType<AttrFallbackBase>(result);
        Assert.Equal("test", result!.Name);
    }

    [Fact]
    public void Deserialize_UnknownDiscriminator_AttributeOverridesOptions()
    {
        var toml =
            """
            kind = "unknown"
            Name = "test"
            """;

        // Options say Fail, but attribute says FallBackToBaseType - attribute wins
        var options = new TomlSerializerOptions
        {
            PolymorphismOptions = new TomlPolymorphismOptions
            {
                UnknownDerivedTypeHandling = TomlUnknownDerivedTypeHandling.Fail,
            },
        };

        var result = TomlSerializer.Deserialize<AttrFallbackBase>(toml, options);

        Assert.IsType<AttrFallbackBase>(result);
        Assert.Equal("test", result!.Name);
    }

    // Test: attribute says Fail explicitly, options say FallBackToBaseType - attribute wins
    [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = TomlUnknownDerivedTypeHandling.Fail)]
    [TomlDerivedType(typeof(AttrFailDerived), "derived")]
    private class AttrFailBase
    {
        public string? Name { get; set; }
    }

    private sealed class AttrFailDerived : AttrFailBase
    {
        public int Extra { get; set; }
    }

    [Fact]
    public void Deserialize_UnknownDiscriminator_AttributeFailOverridesFallbackOptions()
    {
        var toml =
            """
            kind = "unknown"
            Name = "test"
            """;

        var options = new TomlSerializerOptions
        {
            PolymorphismOptions = new TomlPolymorphismOptions
            {
                UnknownDerivedTypeHandling = TomlUnknownDerivedTypeHandling.FallBackToBaseType,
            },
        };

        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<AttrFailBase>(toml, options));
    }

    // Test: JsonPolymorphic attribute sets FallBackToBaseType
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType)]
    [JsonDerivedType(typeof(JsonAttrFallbackDerived), "derived")]
    private class JsonAttrFallbackBase
    {
        public string? Name { get; set; }
    }

    private sealed class JsonAttrFallbackDerived : JsonAttrFallbackBase
    {
        public int Extra { get; set; }
    }

    [Fact]
    public void Deserialize_UnknownDiscriminator_JsonAttributeFallback()
    {
        var toml =
            """
            kind = "unknown"
            Name = "test"
            """;

        var result = TomlSerializer.Deserialize<JsonAttrFallbackBase>(toml);

        Assert.IsType<JsonAttrFallbackBase>(result);
        Assert.Equal("test", result!.Name);
    }

    // Test: TomlPolymorphic overrides JsonPolymorphic when both present
    [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = TomlUnknownDerivedTypeHandling.Fail)]
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType)]
    [TomlDerivedType(typeof(TomlOverridesJsonDerived), "derived")]
    private class TomlOverridesJsonBase
    {
        public string? Name { get; set; }
    }

    private sealed class TomlOverridesJsonDerived : TomlOverridesJsonBase
    {
        public int Extra { get; set; }
    }

    [Fact]
    public void Deserialize_UnknownDiscriminator_TomlAttributeOverridesJsonAttribute()
    {
        var toml =
            """
            kind = "unknown"
            Name = "test"
            """;

        // TomlPolymorphic says Fail, JsonPolymorphic says FallBack - Toml wins
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlOverridesJsonBase>(toml));
    }

    [Fact]
    public void Options_RejectsUnspecifiedUnknownDerivedTypeHandling()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = new TomlPolymorphismOptions
            {
                UnknownDerivedTypeHandling = TomlUnknownDerivedTypeHandling.Unspecified,
            };
        });
    }

    // --- Feature 3: Integer Discriminators ---

    [TomlPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [TomlDerivedType(typeof(IntDiscrimCircle), 1)]
    [TomlDerivedType(typeof(IntDiscrimSquare), 2)]
    private abstract class IntDiscrimShape
    {
        public string? Color { get; set; }
    }

    private sealed class IntDiscrimCircle : IntDiscrimShape
    {
        public double Radius { get; set; }
    }

    private sealed class IntDiscrimSquare : IntDiscrimShape
    {
        public double Side { get; set; }
    }

    [Fact]
    public void Serialize_IntDiscriminator_WritesAsString()
    {
        IntDiscrimShape value = new IntDiscrimCircle { Color = "red", Radius = 5.0 };
        var toml = TomlSerializer.Serialize(value);

        Assert.Contains("type = \"1\"", toml);
        Assert.Contains("Color", toml);
        Assert.Contains("Radius", toml);
    }

    [Fact]
    public void Deserialize_IntDiscriminator_MatchesByString()
    {
        var toml =
            """
            type = "1"
            Color = "red"
            Radius = 5.0
            """;

        var result = TomlSerializer.Deserialize<IntDiscrimShape>(toml);

        Assert.IsType<IntDiscrimCircle>(result);
        var circle = (IntDiscrimCircle)result!;
        Assert.Equal("red", circle.Color);
        Assert.Equal(5.0, circle.Radius);
    }

    [Fact]
    public void Roundtrip_IntDiscriminator()
    {
        IntDiscrimShape original = new IntDiscrimSquare { Color = "blue", Side = 3.0 };
        var toml = TomlSerializer.Serialize(original);
        var result = TomlSerializer.Deserialize<IntDiscrimShape>(toml);

        Assert.IsType<IntDiscrimSquare>(result);
        var square = (IntDiscrimSquare)result!;
        Assert.Equal("blue", square.Color);
        Assert.Equal(3.0, square.Side);
    }

    // JsonDerivedType int discriminators (already supported at runtime)
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(JsonIntDiscrimCircle), 1)]
    [JsonDerivedType(typeof(JsonIntDiscrimSquare), 2)]
    private abstract class JsonIntDiscrimShape
    {
        public string? Color { get; set; }
    }

    private sealed class JsonIntDiscrimCircle : JsonIntDiscrimShape
    {
        public double Radius { get; set; }
    }

    private sealed class JsonIntDiscrimSquare : JsonIntDiscrimShape
    {
        public double Side { get; set; }
    }

    [Fact]
    public void Roundtrip_JsonIntDiscriminator()
    {
        JsonIntDiscrimShape original = new JsonIntDiscrimCircle { Color = "red", Radius = 5.0 };
        var toml = TomlSerializer.Serialize(original);

        Assert.Contains("type = \"1\"", toml);

        var result = TomlSerializer.Deserialize<JsonIntDiscrimShape>(toml);

        Assert.IsType<JsonIntDiscrimCircle>(result);
        Assert.Equal("red", ((JsonIntDiscrimCircle)result!).Color);
        Assert.Equal(5.0, ((JsonIntDiscrimCircle)result).Radius);
    }

    [Fact]
    public void Deserialize_DeeplyNestedPolymorphicValues_AllocatesLinearly()
    {
        var options = new TomlSerializerOptions { MaxDepth = 256 };
        var shallow = MeasureAllocations(20);
        var deep = MeasureAllocations(80);

        // Copying the subtree at every level would make the ratio about 16
        Assert.True(deep < shallow * 8, $"depth 20: {shallow} bytes, depth 80: {deep} bytes");

        long MeasureAllocations(int depth)
        {
            var toml = CreateNestedDocument(depth);
            Assert.Equal(depth + 1, CountDepth(TomlSerializer.Deserialize<NestedPolymorphicNode>(toml, options)));
            var before = GC.GetAllocatedBytesForCurrentThread();
            TomlSerializer.Deserialize<NestedPolymorphicNode>(toml, options);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        static string CreateNestedDocument(int depth)
        {
            var builder = new System.Text.StringBuilder("kind = \"leaf\"\n");
            var path = "Child";
            for (var i = 0; i < depth; i++)
            {
                builder.Append('[').Append(path).Append("]\nkind = \"leaf\"\nValue = \"").Append('x', 200).Append("\"\n");
                path += ".Child";
            }

            return builder.ToString();
        }

        static int CountDepth(NestedPolymorphicNode? node)
        {
            var count = 0;
            for (; node is not null; node = node.Child)
            {
                count++;
            }

            return count;
        }
    }

    [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [TomlDerivedType(typeof(NestedPolymorphicLeaf), "leaf")]
    private class NestedPolymorphicNode
    {
        public NestedPolymorphicNode? Child { get; set; }

        public string? Value { get; set; }
    }

    private sealed class NestedPolymorphicLeaf : NestedPolymorphicNode
    {
    }
}
