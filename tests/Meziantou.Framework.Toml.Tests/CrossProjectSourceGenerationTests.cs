using System;
using System.Collections.Generic;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

#pragma warning disable CA1002 // Test models use List<T> on purpose
#pragma warning disable MA0048 // File name must match type name

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedCrossProjectAnimal))]
[TomlSerializable(typeof(GeneratedCrossProjectEnvelope))]
[TomlDerivedTypeMapping(typeof(GeneratedCrossProjectAnimal), typeof(GeneratedCrossProjectCat), "cat")]
[TomlDerivedTypeMapping(typeof(GeneratedCrossProjectAnimal), typeof(GeneratedCrossProjectDog), "dog")]
internal sealed partial class GeneratedCrossProjectContext : TomlSerializerContext
{
}

[TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
public abstract class GeneratedCrossProjectAnimal
{
    public string Name { get; set; } = "";
}

public sealed class GeneratedCrossProjectCat : GeneratedCrossProjectAnimal
{
    public int Lives { get; set; }
}

public sealed class GeneratedCrossProjectDog : GeneratedCrossProjectAnimal
{
    public bool GoodBoy { get; set; }
}

public sealed class GeneratedCrossProjectEnvelope
{
    public List<GeneratedCrossProjectAnimal> Animals { get; set; } = [];
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedMappedShape))]
[TomlDerivedTypeMapping(typeof(GeneratedMappedShape), typeof(GeneratedMappedCircle))]
[TomlDerivedTypeMapping(typeof(GeneratedMappedShape), typeof(GeneratedMappedSquare), "square")]
internal sealed partial class GeneratedMappedShapeContext : TomlSerializerContext
{
}

[TomlPolymorphic(TypeDiscriminatorPropertyName = "type")]
public abstract class GeneratedMappedShape
{
    public string Color { get; set; } = "";
}

public sealed class GeneratedMappedCircle : GeneratedMappedShape
{
    public double Radius { get; set; }
}

public sealed class GeneratedMappedSquare : GeneratedMappedShape
{
    public double Side { get; set; }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedPreferredAnimal))]
[TomlDerivedTypeMapping(typeof(GeneratedPreferredAnimal), typeof(GeneratedMappedFox), "fox")]
[TomlDerivedTypeMapping(typeof(GeneratedPreferredAnimal), typeof(GeneratedIgnoredDog), "cat")]
internal sealed partial class GeneratedPreferredAnimalContext : TomlSerializerContext
{
}

[TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[TomlDerivedType(typeof(GeneratedPreferredCat), "cat")]
public abstract class GeneratedPreferredAnimal
{
    public string Name { get; set; } = "";
}

public sealed class GeneratedPreferredCat : GeneratedPreferredAnimal
{
    public int Lives { get; set; }
}

public sealed class GeneratedMappedFox : GeneratedPreferredAnimal
{
    public int Tricks { get; set; }
}

public sealed class GeneratedIgnoredDog : GeneratedPreferredAnimal
{
    public bool GoodBoy { get; set; }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedMappedNumberShape))]
[TomlDerivedTypeMapping(typeof(GeneratedMappedNumberShape), typeof(GeneratedMappedNumberCircle), 1)]
[TomlDerivedTypeMapping(typeof(GeneratedMappedNumberShape), typeof(GeneratedMappedNumberSquare), 2)]
internal sealed partial class GeneratedMappedNumberShapeContext : TomlSerializerContext
{
}

[TomlPolymorphic(TypeDiscriminatorPropertyName = "type")]
public abstract class GeneratedMappedNumberShape
{
}

public sealed class GeneratedMappedNumberCircle : GeneratedMappedNumberShape
{
    public double Radius { get; set; }
}

public sealed class GeneratedMappedNumberSquare : GeneratedMappedNumberShape
{
    public double Side { get; set; }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedMappedNode))]
[TomlSerializable(typeof(GeneratedMappedNodeHolder))]
[TomlDerivedTypeMapping(typeof(IGeneratedMappedNode), typeof(GeneratedMappedNode), "node")]
internal sealed partial class GeneratedMappedNodeContext : TomlSerializerContext
{
}

[TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
public interface IGeneratedMappedNode
{
    string Name { get; }

    int Level { get; }
}

public sealed class GeneratedMappedNode : IGeneratedMappedNode
{
    public string Name { get; set; } = "";

    public int Level { get; set; }
}

public sealed class GeneratedMappedNodeHolder
{
    public IGeneratedMappedNode? Value { get; set; }
}

public sealed class CrossProjectSourceGenerationTests
{
    [Fact]
    public void GeneratedContext_CanSerializeAndDeserializeCrossProjectRoot()
    {
        var context = GeneratedCrossProjectContext.Default;
        GeneratedCrossProjectAnimal value = new GeneratedCrossProjectDog { Name = "Rex", GoodBoy = true };

        var toml = TomlSerializer.Serialize(value, context.GeneratedCrossProjectAnimal);
        var roundtrip = TomlSerializer.Deserialize(toml, context.GeneratedCrossProjectAnimal);

        Assert.Contains("kind = \"dog\"", toml);
        Assert.IsType<GeneratedCrossProjectDog>(roundtrip);
        Assert.True(((GeneratedCrossProjectDog)roundtrip!).GoodBoy);
    }

    [Fact]
    public void GeneratedContext_AutoIncludesContextMappedDerivedTypes()
    {
        var context = GeneratedCrossProjectContext.Default;

        var catTypeInfo = context.GetTypeInfo(typeof(GeneratedCrossProjectCat), context.Options);
        var dogTypeInfo = context.GetTypeInfo(typeof(GeneratedCrossProjectDog), context.Options);

        Assert.NotNull(catTypeInfo);
        Assert.NotNull(dogTypeInfo);
    }

    [Fact]
    public void GeneratedContext_CanRoundtripCollectionsOfCrossProjectValues()
    {
        var context = GeneratedCrossProjectContext.Default;
        var payload = new GeneratedCrossProjectEnvelope
        {
            Animals =
            [
                new GeneratedCrossProjectCat { Name = "Mog", Lives = 9 },
                new GeneratedCrossProjectDog { Name = "Rex", GoodBoy = true },
            ],
        };

        var toml = TomlSerializer.Serialize(payload, context.GeneratedCrossProjectEnvelope);
        var roundtrip = TomlSerializer.Deserialize(toml, context.GeneratedCrossProjectEnvelope);

        Assert.NotNull(roundtrip);
        Assert.HasCount(2, roundtrip!.Animals);
        Assert.IsType<GeneratedCrossProjectCat>(roundtrip.Animals[0]);
        Assert.IsType<GeneratedCrossProjectDog>(roundtrip.Animals[1]);
    }

    [Fact]
    public void GeneratedContext_ContextMappingsSupportDefaultDerivedTypes()
    {
        var context = GeneratedMappedShapeContext.Default;
        GeneratedMappedShape value = new GeneratedMappedCircle { Color = "red", Radius = 5.0 };

        var toml = TomlSerializer.Serialize(value, context.GeneratedMappedShape);
        var missing = TomlSerializer.Deserialize(
            """
            color = "red"
            radius = 5.0
            """,
            context.GeneratedMappedShape);
        var unknown = TomlSerializer.Deserialize(
            """
            type = "triangle"
            color = "blue"
            radius = 2.0
            """,
            context.GeneratedMappedShape);

        Assert.DoesNotContain("type", toml);
        Assert.IsType<GeneratedMappedCircle>(missing);
        Assert.IsType<GeneratedMappedCircle>(unknown);
    }

    [Fact]
    public void GeneratedContext_ContextMappingsRespectAttributePrecedence()
    {
        var context = GeneratedPreferredAnimalContext.Default;
        var preferred = TomlSerializer.Deserialize(
            """
            kind = "cat"
            name = "Ada"
            lives = 9
            """,
            context.GeneratedPreferredAnimal);
        var additive = TomlSerializer.Deserialize(
            """
            kind = "fox"
            name = "Pip"
            tricks = 3
            """,
            context.GeneratedPreferredAnimal);

        Assert.IsType<GeneratedPreferredCat>(preferred);
        Assert.Equal(9, ((GeneratedPreferredCat)preferred!).Lives);
        Assert.IsType<GeneratedMappedFox>(additive);
        Assert.Equal(3, ((GeneratedMappedFox)additive!).Tricks);
    }

    [Fact]
    public void GeneratedContext_ContextMappingsSupportIntegerDiscriminators()
    {
        var context = GeneratedMappedNumberShapeContext.Default;
        GeneratedMappedNumberShape value = new GeneratedMappedNumberCircle { Radius = 4.5 };

        var toml = TomlSerializer.Serialize(value, context.GeneratedMappedNumberShape);
        var roundtrip = TomlSerializer.Deserialize(
            """
            type = "2"
            side = 3.0
            """,
            context.GeneratedMappedNumberShape);

        Assert.Contains("type = \"1\"", toml);
        Assert.IsType<GeneratedMappedNumberSquare>(roundtrip);
        Assert.Equal(3.0, ((GeneratedMappedNumberSquare)roundtrip!).Side);
    }

    [Fact]
    public void GeneratedContext_ContextMappingsSupportInterfaceRootTypes()
    {
        var context = GeneratedMappedNodeContext.Default;
        IGeneratedMappedNode value = new GeneratedMappedNode { Name = "Root", Level = 4 };

        var toml = TomlSerializer.Serialize(value, typeof(IGeneratedMappedNode), context);
        var roundtrip = (IGeneratedMappedNode?)TomlSerializer.Deserialize(toml, typeof(IGeneratedMappedNode), context);

        Assert.NotNull(context.GetTypeInfo(typeof(IGeneratedMappedNode), context.Options));
        Assert.Contains("kind = \"node\"", toml);
        Assert.Contains("name = \"Root\"", toml);
        Assert.Contains("level = 4", toml);
        Assert.IsType<GeneratedMappedNode>(roundtrip);
        Assert.Equal("Root", roundtrip!.Name);
        Assert.Equal(4, roundtrip.Level);
    }

    [Fact]
    public void GeneratedContext_ContextMappingsSupportInterfaceMemberTypes()
    {
        var context = GeneratedMappedNodeContext.Default;
        var holder = new GeneratedMappedNodeHolder
        {
            Value = new GeneratedMappedNode { Name = "Child", Level = 3 },
        };

        var toml = TomlSerializer.Serialize(holder, context.GeneratedMappedNodeHolder);
        var roundtrip = TomlSerializer.Deserialize(toml, context.GeneratedMappedNodeHolder);

        Assert.Contains("kind = \"node\"", toml);
        Assert.Contains("name = \"Child\"", toml);
        Assert.Contains("level = 3", toml);
        Assert.NotNull(roundtrip);
        Assert.IsType<GeneratedMappedNode>(roundtrip!.Value);
        Assert.Equal("Child", roundtrip.Value!.Name);
        Assert.Equal(3, roundtrip.Value.Level);
    }
}
