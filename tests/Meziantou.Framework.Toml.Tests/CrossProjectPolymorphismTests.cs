using System;
using System.Collections.Generic;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public sealed partial class CrossProjectPolymorphismTests
{
    [Fact]
    public void ReadmeExample_RuntimeMappings()
    {
        #region TomlPolymorphismOptions_DerivedTypeMappings
        var options = new TomlSerializerOptions
        {
            PolymorphismOptions = new TomlPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "kind",
                DerivedTypeMappings = new Dictionary<Type, IReadOnlyList<TomlDerivedType>>
                {
                    [typeof(Animal)] = [new(typeof(Cat), "cat"), new(typeof(Dog), "dog")],
                },
            },
        };
        #endregion

        const string Toml = "kind = \"dog\"\nName = \"Rex\"\nBreed = \"Labrador\"\n";
        Assert.Equal("Labrador", Assert.IsType<Dog>(TomlSerializer.Deserialize<Animal>(Toml, options)).Breed);
        Assert.Equal("Labrador", Assert.IsType<Dog>(TomlSerializer.Deserialize(Toml, AnimalContext.Default.Animal)).Breed);
        Assert.Equal(Toml, TomlSerializer.Serialize<Animal>(new Dog { Name = "Rex", Breed = "Labrador" }, AnimalContext.Default.Animal));
    }

    #region TomlDerivedTypeMapping_Context
    [TomlSerializable(typeof(Animal))]
    [TomlDerivedTypeMapping(typeof(Animal), typeof(Cat), "cat")]
    [TomlDerivedTypeMapping(typeof(Animal), typeof(Dog), "dog")]
    internal sealed partial class AnimalContext : TomlSerializerContext;
    #endregion

    [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    internal abstract class Animal
    {
        public string Name { get; set; } = "";
    }

    internal sealed class Cat : Animal
    {
        public bool Indoor { get; set; }
    }

    internal sealed class Dog : Animal
    {
        public string Breed { get; set; } = "";
    }

    [Fact]
    public void RuntimeMappings_CanSerializeAndDeserializeCrossProjectRoot()
    {
        RuntimeAnimal value = new RuntimeDog { Name = "Rex", GoodBoy = true };
        var options = CreateRuntimeAnimalOptions();

        var toml = TomlSerializer.Serialize(value, options);
        var roundtrip = TomlSerializer.Deserialize<RuntimeAnimal>(toml, options);

        Assert.Contains("kind = \"dog\"", toml);
        Assert.IsType<RuntimeDog>(roundtrip);
        Assert.Equal("Rex", ((RuntimeDog)roundtrip!).Name);
        Assert.True(((RuntimeDog)roundtrip).GoodBoy);
    }

    [Fact]
    public void RuntimeMappings_CanRoundtripCollectionsOfPolymorphicValues()
    {
        var payload = new RuntimeShelter
        {
            Animals =
            [
                new RuntimeCat { Name = "Mog", Lives = 9 },
                new RuntimeDog { Name = "Rex", GoodBoy = true },
            ],
        };

        var options = CreateRuntimeAnimalOptions();
        var toml = TomlSerializer.Serialize(payload, options);
        var roundtrip = TomlSerializer.Deserialize<RuntimeShelter>(toml, options);

        Assert.NotNull(roundtrip);
        Assert.HasCount(2, roundtrip!.Animals);
        Assert.IsType<RuntimeCat>(roundtrip.Animals[0]);
        Assert.IsType<RuntimeDog>(roundtrip.Animals[1]);
    }

    [Fact]
    public void RuntimeMappings_AttributeMappingsTakePrecedenceButRemainAdditive()
    {
        var options = new TomlSerializerOptions
        {
            PolymorphismOptions = new TomlPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "kind",
                DerivedTypeMappings = new Dictionary<Type, IReadOnlyList<TomlDerivedType>>
                {
                    [typeof(AttributedAnimal)] =
                    [
                        new(typeof(RuntimeMappedDog), "cat"),
                        new(typeof(RuntimeMappedFox), "fox"),
                    ],
                },
            },
        };

        var preferredToml =
            """
            kind = "cat"
            Name = "Ada"
            Lives = 9
            """;

        var additiveToml =
            """
            kind = "fox"
            Name = "Pip"
            Tricks = 3
            """;

        var preferred = TomlSerializer.Deserialize<AttributedAnimal>(preferredToml, options);
        var additive = TomlSerializer.Deserialize<AttributedAnimal>(additiveToml, options);

        Assert.IsType<AttributedCat>(preferred);
        Assert.Equal(9, ((AttributedCat)preferred!).Lives);
        Assert.IsType<RuntimeMappedFox>(additive);
        Assert.Equal(3, ((RuntimeMappedFox)additive!).Tricks);
    }

    [Fact]
    public void RuntimeMappings_DefaultDerivedType_WorksWithoutBaseTypeAttributes()
    {
        var options = CreateRuntimeShapeOptions();

        RuntimeShape value = new RuntimeCircle { Color = "red", Radius = 5.0 };
        var toml = TomlSerializer.Serialize(value, options);
        var missingDiscriminator =
            """
            Color = "red"
            Radius = 5.0
            """;
        var unknownDiscriminator =
            """
            type = "triangle"
            Color = "blue"
            Radius = 2.0
            """;

        var missing = TomlSerializer.Deserialize<RuntimeShape>(missingDiscriminator, options);
        var unknown = TomlSerializer.Deserialize<RuntimeShape>(unknownDiscriminator, options);

        Assert.DoesNotContain("type", toml);
        Assert.IsType<RuntimeCircle>(missing);
        Assert.IsType<RuntimeCircle>(unknown);
    }

    [Fact]
    public void RuntimeMappings_IntDiscriminators_UseInvariantStringValues()
    {
        var options = new TomlSerializerOptions
        {
            PolymorphismOptions = new TomlPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "type",
                DerivedTypeMappings = new Dictionary<Type, IReadOnlyList<TomlDerivedType>>
                {
                    [typeof(RuntimeNumberShape)] =
                    [
                        new(typeof(RuntimeNumberCircle), 1),
                        new(typeof(RuntimeNumberSquare), 2),
                    ],
                },
            },
        };

        RuntimeNumberShape value = new RuntimeNumberCircle { Radius = 4.5 };
        var toml = TomlSerializer.Serialize(value, options);
        var deserialized = TomlSerializer.Deserialize<RuntimeNumberShape>(
            """
            type = "2"
            Side = 3.0
            """,
            options);

        Assert.Contains("type = \"1\"", toml);
        Assert.IsType<RuntimeNumberSquare>(deserialized);
        Assert.Equal(3.0, ((RuntimeNumberSquare)deserialized!).Side);
    }

    [Fact]
    public void RuntimeMappings_InvalidDerivedType_ThrowsTomlException()
    {
        var options = new TomlSerializerOptions
        {
            PolymorphismOptions = new TomlPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "kind",
                DerivedTypeMappings = new Dictionary<Type, IReadOnlyList<TomlDerivedType>>
                {
                    [typeof(RuntimeAnimal)] =
                    [
                        new(typeof(string), "cat"),
                    ],
                },
            },
        };

        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<RuntimeAnimal>("kind = \"cat\"", options));

        Assert.NotNull(exception);
        Assert.Contains("is not assignable", exception!.Message);
    }

    [Fact]
    public void RuntimeMappings_EmptyDiscriminator_ThrowsTomlException()
    {
        var options = new TomlSerializerOptions
        {
            PolymorphismOptions = new TomlPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "kind",
                DerivedTypeMappings = new Dictionary<Type, IReadOnlyList<TomlDerivedType>>
                {
                    [typeof(RuntimeAnimal)] =
                    [
                        new(typeof(RuntimeCat), string.Empty),
                    ],
                },
            },
        };

        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<RuntimeAnimal>("kind = \"cat\"", options));

        Assert.NotNull(exception);
        Assert.Contains("cannot be empty", exception!.Message);
    }

    private static TomlSerializerOptions CreateRuntimeAnimalOptions()
        => new()
        {
            PolymorphismOptions = new TomlPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "kind",
                DerivedTypeMappings = new Dictionary<Type, IReadOnlyList<TomlDerivedType>>
                {
                    [typeof(RuntimeAnimal)] =
                    [
                        new(typeof(RuntimeCat), "cat"),
                        new(typeof(RuntimeDog), "dog"),
                    ],
                },
            },
        };

    private static TomlSerializerOptions CreateRuntimeShapeOptions()
        => new()
        {
            PolymorphismOptions = new TomlPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "type",
                DerivedTypeMappings = new Dictionary<Type, IReadOnlyList<TomlDerivedType>>
                {
                    [typeof(RuntimeShape)] =
                    [
                        new(typeof(RuntimeCircle)),
                        new(typeof(RuntimeSquare), "square"),
                    ],
                },
            },
        };

    private abstract class RuntimeAnimal
    {
        public string? Name { get; set; }
    }

    private sealed class RuntimeCat : RuntimeAnimal
    {
        public int Lives { get; set; }
    }

    private sealed class RuntimeDog : RuntimeAnimal
    {
        public bool GoodBoy { get; set; }
    }

    private sealed class RuntimeShelter
    {
        public List<RuntimeAnimal> Animals { get; set; } = [];
    }

    [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [TomlDerivedType(typeof(AttributedCat), "cat")]
    private abstract class AttributedAnimal
    {
        public string? Name { get; set; }
    }

    private sealed class AttributedCat : AttributedAnimal
    {
        public int Lives { get; set; }
    }

    private sealed class RuntimeMappedDog : AttributedAnimal
    {
        public bool GoodBoy { get; set; }
    }

    private sealed class RuntimeMappedFox : AttributedAnimal
    {
        public int Tricks { get; set; }
    }

    private abstract class RuntimeShape
    {
        public string? Color { get; set; }
    }

    private sealed class RuntimeCircle : RuntimeShape
    {
        public double Radius { get; set; }
    }

    private sealed class RuntimeSquare : RuntimeShape
    {
        public double Side { get; set; }
    }

    private abstract class RuntimeNumberShape
    {
    }

    private sealed class RuntimeNumberCircle : RuntimeNumberShape
    {
        public double Radius { get; set; }
    }

    private sealed class RuntimeNumberSquare : RuntimeNumberShape
    {
        public double Side { get; set; }
    }
}
