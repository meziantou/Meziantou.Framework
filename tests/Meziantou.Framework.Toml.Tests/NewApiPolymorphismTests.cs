using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    private sealed class PrefixStringConverter : TomlConverter<string>
    {
        public override string? Read(TomlReader reader)
        {
            var value = reader.GetString();
            reader.Read();
            return value.StartsWith("enc:", StringComparison.Ordinal) ? value[4..] : value;
        }

        public override void Write(TomlWriter writer, string value) => writer.WriteStringValue("enc:" + value);
    }

    private sealed class Zoo
    {
        public Animal? Main { get; set; }

        public System.Collections.Generic.List<Animal> Animals { get; set; } = [];
    }

    [Fact]
    public void Serialize_Polymorphic_AppliesConvertersOnceAndNotToTheDiscriminator()
    {
        var options = new TomlSerializerOptions { Converters = [new PrefixStringConverter()] };
        var zoo = new Zoo { Main = new Dog { Name = "rex" }, Animals = [new Cat { Name = "tom", Lives = 9 }] };

        var toml = TomlSerializer.Serialize(zoo, options);

        Assert.Equal("[Main]\nkind = \"dog\"\nName = \"enc:rex\"\nGoodBoy = false\n[[Animals]]\nkind = \"cat\"\nName = \"enc:tom\"\nLives = 9\n", toml.ReplaceLineEndings("\n"));
        var roundtrip = TomlSerializer.Deserialize<Zoo>(toml, options)!;
        Assert.Equal("rex", Assert.IsType<Dog>(roundtrip.Main).Name);
        Assert.Equal("tom", Assert.IsType<Cat>(roundtrip.Animals[0]).Name);
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

    [Theory]
    [InlineData(TomlDuplicateKeyHandling.LastWins, typeof(Dog))]
    [InlineData(TomlDuplicateKeyHandling.Error, null)]
    public void Deserialize_Polymorphic_DuplicateDiscriminatorFollowsDuplicateKeyHandling(TomlDuplicateKeyHandling duplicateKeyHandling, Type? expectedType)
    {
        var toml = "kind = \"cat\"\nName = \"Rex\"\nkind = \"dog\"\n";
        var options = new TomlSerializerOptions { DuplicateKeyHandling = duplicateKeyHandling };

        if (expectedType is null)
        {
            Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Animal>(toml, options));
            return;
        }

        var result = TomlSerializer.Deserialize<Animal>(toml, options);
        Assert.IsType(expectedType, result);
        Assert.Equal("dog", ((Model.TomlTable)TomlSerializer.Deserialize<Model.TomlTable>(toml, options)!)["kind"]);
    }

    [Fact]
    public void GeneratedContext_DuplicateDiscriminatorWithLastWins_UsesTheLastOne()
    {
        var context = TestTomlSerializerContextDefaultDerivedType.Default;
        var options = context.Options with { DuplicateKeyHandling = TomlDuplicateKeyHandling.LastWins };
        var typeInfo = (TomlTypeInfo<GeneratedDefaultShape>)context.GetTypeInfo(typeof(GeneratedDefaultShape), options)!;

        var result = TomlSerializer.Deserialize("type = \"circle\"\ncolor = \"blue\"\ntype = \"square\"\nside = 2.0\n", typeInfo);

        Assert.Equal(2.0, Assert.IsType<GeneratedDefaultSquare>(result).Side);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_NestedPolymorphicValues_SkipTheContainersBeforeTheDiscriminator(bool withMetadata)
    {
        var options = new TomlSerializerOptions { MaxDepth = 256, MetadataStore = withMetadata ? new TomlMetadataStore() : null };
        var builder = new System.Text.StringBuilder("x = [[1, { a = [2] }], []]\nkind = \"leaf\"\nChild = ");
        for (var i = 0; i < 30; i++)
        {
            builder.Append("{ x = { a = [1, [2, { b = 3 }]], c = {} }, Value = \"").Append(i).Append("\", kind = \"leaf\", Child = ");
        }

        builder.Append("{ kind = \"leaf\", Value = \"last\" }").Append('}', 30).Append('\n');

        var node = TomlSerializer.Deserialize<NestedPolymorphicNode>(builder.ToString(), options);

        var values = new List<string?>();
        for (node = node!.Child; node is not null; node = node.Child)
        {
            Assert.IsType<NestedPolymorphicLeaf>(node);
            values.Add(node.Value);
        }

        Assert.Equal([.. Enumerable.Range(0, 30).Select(i => i.ToString(CultureInfo.InvariantCulture)), "last"], values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructDerivedType_OfAnInterface_IsReadAndWritten(bool generated)
    {
        var context = SelfDerivedTomlSerializerContext.Default;
        var holder = new StructShapeHolder { S = new StructShapeSquare { X = 3 } };

        var toml = generated ? TomlSerializer.Serialize(holder, context.StructShapeHolder) : TomlSerializer.Serialize(holder);
        var read = generated ? TomlSerializer.Deserialize(toml, context.StructShapeHolder)! : TomlSerializer.Deserialize<StructShapeHolder>(toml)!;

        Assert.Equal("[S]\n\"$type\" = \"s\"\nX = 3\n", toml.ReplaceLineEndings("\n"));
        Assert.Equal(3, Assert.IsType<StructShapeSquare>(read.S).X);
    }

    // Without the metadata of the base type, as when reflection is disabled, a table without a discriminator is an input error
    [Fact]
    public void MissingDiscriminator_WithoutBaseMetadata_ThrowsTomlException()
    {
        var typeInfo = Meziantou.Framework.Toml.Serialization.Internal.TomlPolymorphicTypeInfo.TryCreate(typeof(SelfDefaultlessShape), TomlSerializerOptions.Default, baseTypeInfo: null)!;

        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("Size = 4", typeInfo));

        Assert.Contains("Missing discriminator key", exception.Message, StringComparison.Ordinal);
        Assert.False(TomlSerializer.TryDeserialize("Size = 4", typeInfo, out _));
    }

    // The generated polymorphic metadata is used as is, not wrapped again by the reflection-based polymorphism
    [Fact]
    public void BaseType_RegisteredAsItsOwnDerivedType_IsWrittenThroughAContextUsedAsResolver()
    {
        var options = new TomlSerializerOptions { TypeInfoResolver = SelfDerivedTomlSerializerContext.Default };

        var direct = TomlSerializer.Serialize<SelfDerivedShape>(new SelfDerivedShape { Size = 1 }, options);
        var nested = TomlSerializer.Serialize(new SelfDerivedHolderOutsideTheContext { S = new SelfDerivedShape { Size = 2 } }, options);
        var read = TomlSerializer.Deserialize<SelfDerivedHolderOutsideTheContext>(nested, options)!;

        Assert.Equal("\"$type\" = \"base\"\nSize = 1\n", direct.ReplaceLineEndings("\n"));
        Assert.Equal("[S]\n\"$type\" = \"base\"\nSize = 2\n", nested.ReplaceLineEndings("\n"));
        Assert.Equal(2, Assert.IsType<SelfDerivedShape>(read.S).Size);
    }

    // Like System.Text.Json, the base type can be one of its own derived types
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BaseType_RegisteredAsItsOwnDerivedType_UsesTheBaseMetadata(bool generated)
    {
        var context = SelfDerivedTomlSerializerContext.Default;
        string Serialize<T>(T value, TomlTypeInfo<T> typeInfo) => generated ? TomlSerializer.Serialize(value, typeInfo) : TomlSerializer.Serialize(value);
        T? Deserialize<T>(string toml, TomlTypeInfo<T> typeInfo) => generated ? TomlSerializer.Deserialize(toml, typeInfo) : TomlSerializer.Deserialize<T>(toml);

        var toml = Serialize(new SelfDerivedHolder { S = new SelfDerivedShape { Size = 3 } }, context.SelfDerivedHolder);
        var shape = Deserialize(toml, context.SelfDerivedHolder)!.S;
        var circle = Deserialize("[S]\n\"$type\" = \"circle\"\nRadius = 2\n", context.SelfDerivedHolder)!.S;
        var defaultToml = Serialize(new SelfDefaultHolder { S = new SelfDefaultShape { Size = 4 } }, context.SelfDefaultHolder);
        var defaultShape = Deserialize("[S]\nSize = 5\n", context.SelfDefaultHolder)!.S;

        Assert.Equal("[S]\n\"$type\" = \"base\"\nSize = 3\n", toml.ReplaceLineEndings("\n"));
        Assert.Equal(3, Assert.IsType<SelfDerivedShape>(shape).Size);
        Assert.Equal(2, Assert.IsType<SelfDerivedCircle>(circle).Radius);
        Assert.Equal("[S]\nSize = 4\n", defaultToml.ReplaceLineEndings("\n"));
        Assert.Equal(5, Assert.IsType<SelfDefaultShape>(defaultShape).Size);
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

#pragma warning disable MA0048 // File name must match type name
[TomlDerivedType(typeof(SelfDerivedShape), "base")]
[TomlDerivedType(typeof(SelfDerivedCircle), "circle")]
internal class SelfDerivedShape
{
    public int Size { get; set; }
}

internal sealed class SelfDerivedCircle : SelfDerivedShape
{
    public int Radius { get; set; }
}

[TomlDerivedType(typeof(SelfDefaultlessCircle), "circle")]
internal class SelfDefaultlessShape
{
    public int Size { get; set; }
}

internal sealed class SelfDefaultlessCircle : SelfDefaultlessShape
{
}

internal sealed class SelfDerivedHolderOutsideTheContext
{
    public SelfDerivedShape? S { get; set; }
}

internal sealed class SelfDerivedHolder
{
    public SelfDerivedShape? S { get; set; }
}

[TomlDerivedType(typeof(SelfDefaultShape))]
[TomlDerivedType(typeof(SelfDefaultSquare), "square")]
internal class SelfDefaultShape
{
    public int Size { get; set; }
}

internal sealed class SelfDefaultSquare : SelfDefaultShape
{
}

internal sealed class SelfDefaultHolder
{
    public SelfDefaultShape? S { get; set; }
}

[TomlDerivedType(typeof(StructShapeSquare), "s")]
internal interface IStructShape
{
    int X { get; set; }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal struct StructShapeSquare : IStructShape
{
    public int X { get; set; }
}

internal sealed class StructShapeHolder
{
    public IStructShape? S { get; set; }
}

[TomlSerializable(typeof(StructShapeHolder))]
[TomlSerializable(typeof(SelfDerivedHolder))]
[TomlSerializable(typeof(SelfDefaultHolder))]
internal sealed partial class SelfDerivedTomlSerializerContext : TomlSerializerContext
{
}
#pragma warning restore MA0048
