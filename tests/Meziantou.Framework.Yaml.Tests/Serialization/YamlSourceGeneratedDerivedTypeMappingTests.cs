#pragma warning disable MA0048 // File name must match type name

using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization.CrossProject.Core
{
    [YamlPolymorphic]
    internal abstract class Animal
    {
        public string Name { get; set; } = string.Empty;
    }
}

namespace Meziantou.Framework.Yaml.Tests.Serialization.CrossProject.Plugins
{
    internal sealed class Dog : Core.Animal
    {
        public string Breed { get; set; } = string.Empty;
    }

    internal sealed class Cat : Core.Animal
    {
        public bool Indoor { get; set; }
    }
}

namespace Meziantou.Framework.Yaml.Tests.Serialization.CrossProject.AttributeCore
{
    [YamlPolymorphic]
    [YamlDerivedType(typeof(AttributePlugins.BuiltInDog), "dog", Tag = "!dog")]
    internal abstract class Animal
    {
        public string Name { get; set; } = string.Empty;
    }
}

namespace Meziantou.Framework.Yaml.Tests.Serialization.CrossProject.AttributePlugins
{
    internal sealed class BuiltInDog : AttributeCore.Animal
    {
        public int BarkVolume { get; set; }
    }

    internal sealed class ConflictingDog : AttributeCore.Animal
    {
        public string Skill { get; set; } = string.Empty;
    }
}


namespace Meziantou.Framework.Yaml.Tests.Serialization.ClosedHierarchy
{
    internal closed class Shape
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class Circle : Shape
    {
        public double Radius { get; set; }
    }

    internal sealed class Square : Shape
    {
        public double Side { get; set; }
    }

    [YamlPolymorphic(InferClosedTypePolymorphism = true, TypeDiscriminatorPropertyName = "$kind")]
    internal closed class OptInShape
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class OptInTriangle : OptInShape
    {
        public double Height { get; set; }
    }

    internal closed class Pet
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class Cat : Pet
    {
        public bool Indoor { get; set; }
    }

    internal closed class Dog : Pet
    {
        public bool GoodBoy { get; set; }
    }

    internal sealed class Labrador : Dog
    {
        public string Color { get; set; } = string.Empty;
    }

    internal sealed class Collie : Dog
    {
        public bool Herding { get; set; }
    }

    internal sealed class PetHolder
    {
        public Pet? Pet { get; set; }
    }

    internal sealed class ShapeHolder
    {
        public Shape? Shape { get; set; }
    }

    internal sealed class OptInShapeHolder
    {
        public OptInShape? Shape { get; set; }
    }
}

namespace Meziantou.Framework.Yaml.Tests.Serialization.RuntimeClosedHierarchy
{
    internal closed class RuntimeShape
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class RuntimeCircle : RuntimeShape
    {
        public int Radius { get; set; }
    }

    internal sealed class RuntimeSquare : RuntimeShape
    {
        public int Side { get; set; }
    }

    [YamlPolymorphic(InferClosedTypePolymorphism = true)]
    internal closed class RuntimeOptInShape
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class RuntimeOptInCircle : RuntimeOptInShape
    {
        public int Radius { get; set; }
    }

    [YamlPolymorphic(InferClosedTypePolymorphism = false)]
    internal closed class RuntimeOptOutShape
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class RuntimeOptOutCircle : RuntimeOptOutShape
    {
        public int Radius { get; set; }
    }

    [YamlPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
    internal closed class RuntimeCustomDiscriminatorShape
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class RuntimeCustomDiscriminatorCircle : RuntimeCustomDiscriminatorShape
    {
        public int Radius { get; set; }
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(RuntimeExplicitCircle), "custom")]
    internal closed class RuntimeExplicitShape
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class RuntimeExplicitCircle : RuntimeExplicitShape
    {
        public int Radius { get; set; }
    }

    internal sealed class RuntimeExplicitSquare : RuntimeExplicitShape
    {
        public int Side { get; set; }
    }

    internal closed class RuntimePet
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class RuntimeCat : RuntimePet
    {
        public bool Indoor { get; set; }
    }

    internal closed class RuntimeDog : RuntimePet
    {
        public bool GoodBoy { get; set; }
    }

    internal sealed class RuntimeLabrador : RuntimeDog
    {
        public string Color { get; set; } = string.Empty;
    }

    internal sealed class RuntimeShapeHolder
    {
        public RuntimeShape? Shape { get; set; }

        public List<RuntimeShape> Shapes { get; set; } = [];
    }

    internal sealed class RuntimePetHolder
    {
        public RuntimePet? Pet { get; set; }

        public RuntimeDog? Dog { get; set; }
    }

    internal sealed class RuntimeAttributeShapeHolder
    {
        public RuntimeOptInShape? OptIn { get; set; }

        public RuntimeOptOutShape? OptOut { get; set; }

        public RuntimeCustomDiscriminatorShape? Custom { get; set; }

        public RuntimeExplicitShape? Explicit { get; set; }
    }
}

namespace Meziantou.Framework.Yaml.Tests.Serialization
{
    internal sealed class CrossProjectZoo
    {
        public CrossProject.Core.Animal? Animal { get; set; }
    }

    internal sealed class AttributeMappedZoo
    {
        public CrossProject.AttributeCore.Animal? Animal { get; set; }
    }

    [YamlSerializable(typeof(CrossProjectZoo))]
    [YamlSerializable(typeof(AttributeMappedZoo))]
    [YamlDerivedTypeMapping(typeof(CrossProject.Core.Animal), typeof(CrossProject.Plugins.Dog), "dog", Tag = "!dog")]
    [YamlDerivedTypeMapping(typeof(CrossProject.Core.Animal), typeof(CrossProject.Plugins.Cat), "cat", Tag = "!cat")]
    [YamlDerivedTypeMapping(typeof(CrossProject.AttributeCore.Animal), typeof(CrossProject.AttributePlugins.ConflictingDog), "dog", Tag = "!conflict")]
    internal sealed partial class CrossProjectYamlContext : YamlSerializerContext
    {
        public CrossProjectYamlContext()
        {
        }

        public CrossProjectYamlContext(YamlSerializerOptions options)
            : base(options)
        {
        }
    }
    [YamlSourceGenerationOptions(InferClosedTypePolymorphism = true)]
    [YamlSerializable(typeof(ClosedHierarchy.ShapeHolder))]
    [YamlSerializable(typeof(ClosedHierarchy.PetHolder))]
    internal sealed partial class InferredClosedTypeYamlContext : YamlSerializerContext
    {
    }

    [YamlSerializable(typeof(ClosedHierarchy.OptInShapeHolder))]
    internal sealed partial class OptInClosedTypeYamlContext : YamlSerializerContext
    {
    }

    [YamlSerializable(typeof(RuntimeClosedHierarchy.RuntimeShape))]
    [YamlSerializable(typeof(RuntimeClosedHierarchy.RuntimeShapeHolder))]
    [YamlSerializable(typeof(RuntimeClosedHierarchy.RuntimePetHolder))]
    [YamlSerializable(typeof(RuntimeClosedHierarchy.RuntimeAttributeShapeHolder))]
    internal sealed partial class RuntimeInferredClosedTypeYamlContext : YamlSerializerContext
    {
        public RuntimeInferredClosedTypeYamlContext()
        {
        }

        public RuntimeInferredClosedTypeYamlContext(YamlSerializerOptions options)
            : base(options)
        {
        }
    }

    public class YamlSourceGeneratedDerivedTypeMappingTests
    {
        [Fact]
        public void GeneratedContextInfersClosedHierarchyDerivedTypes()
        {
            var context = new InferredClosedTypeYamlContext();
            var typeInfo = context.ShapeHolder;

            var yaml = YamlSerializer.Serialize(
                new ClosedHierarchy.ShapeHolder
                {
                    Shape = new ClosedHierarchy.Circle { Name = "circle", Radius = 3 },
                },
                typeInfo);

            Assert.Contains("$type: Circle", yaml);
            Assert.Contains("Radius: 3", yaml);

            var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
            var circle = Assert.IsType<ClosedHierarchy.Circle>(roundtripped?.Shape);
            Assert.Equal("circle", circle.Name);
            Assert.Equal(3, circle.Radius);
        }

        [Fact]
        public void GeneratedContextInfersEveryClosedHierarchyDerivedType()
        {
            var context = new InferredClosedTypeYamlContext();
            var typeInfo = context.ShapeHolder;

            var yaml = YamlSerializer.Serialize(
                new ClosedHierarchy.ShapeHolder
                {
                    Shape = new ClosedHierarchy.Square { Name = "square", Side = 4 },
                },
                typeInfo);

            Assert.Contains("$type: Square", yaml);
            Assert.IsType<ClosedHierarchy.Square>(YamlSerializer.Deserialize(yaml, typeInfo)?.Shape);
        }

        [Fact]
        public void GeneratedContextInfersDescendantsOfNestedClosedTypes()
        {
            var context = new InferredClosedTypeYamlContext();
            var typeInfo = context.PetHolder;

            var yaml = YamlSerializer.Serialize(
                new ClosedHierarchy.PetHolder
                {
                    Pet = new ClosedHierarchy.Labrador { Name = "Rex", GoodBoy = true, Color = "chocolate" },
                },
                typeInfo);

            Assert.Contains("$type: Labrador", yaml);

            var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
            var labrador = Assert.IsType<ClosedHierarchy.Labrador>(roundtripped?.Pet);
            Assert.Equal("Rex", labrador.Name);
            Assert.True(labrador.GoodBoy);
            Assert.Equal("chocolate", labrador.Color);
        }

        [Fact]
        public void GeneratedContextInfersEveryDescendantOfANestedClosedHierarchy()
        {
            var context = new InferredClosedTypeYamlContext();
            var typeInfo = context.PetHolder;

            var collieYaml = YamlSerializer.Serialize(
                new ClosedHierarchy.PetHolder
                {
                    Pet = new ClosedHierarchy.Collie { Name = "Lassie", Herding = true },
                },
                typeInfo);

            Assert.Contains("$type: Collie", collieYaml);
            Assert.IsType<ClosedHierarchy.Collie>(YamlSerializer.Deserialize(collieYaml, typeInfo)?.Pet);

            var catYaml = YamlSerializer.Serialize(
                new ClosedHierarchy.PetHolder
                {
                    Pet = new ClosedHierarchy.Cat { Name = "Mittens", Indoor = true },
                },
                typeInfo);

            Assert.Contains("$type: Cat", catYaml);
            Assert.IsType<ClosedHierarchy.Cat>(YamlSerializer.Deserialize(catYaml, typeInfo)?.Pet);
        }

        [Fact]
        public void GeneratedContextInfersClosedHierarchyFromTypeLevelOptIn()
        {
            var context = new OptInClosedTypeYamlContext();
            var typeInfo = context.OptInShapeHolder;

            var yaml = YamlSerializer.Serialize(
                new ClosedHierarchy.OptInShapeHolder
                {
                    Shape = new ClosedHierarchy.OptInTriangle { Name = "triangle", Height = 5 },
                },
                typeInfo);

            Assert.Contains("$kind: OptInTriangle", yaml);

            var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
            var triangle = Assert.IsType<ClosedHierarchy.OptInTriangle>(roundtripped?.Shape);
            Assert.Equal("triangle", triangle.Name);
            Assert.Equal(5, triangle.Height);
        }

        [Fact]
        public void GeneratedContextSupportsCrossProjectPropertyDiscriminatorMappings()
        {
            var context = new CrossProjectYamlContext();
            var typeInfo = context.CrossProjectZoo;

            var yaml = YamlSerializer.Serialize(
                new CrossProjectZoo
                {
                    Animal = new CrossProject.Plugins.Dog { Name = "Rex", Breed = "Collie" },
                },
                typeInfo);

            Assert.Contains("$type: dog", yaml);
            Assert.Contains("Breed: Collie", yaml);

            var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
            Assert.NotNull(roundtripped?.Animal);
            Assert.IsType<CrossProject.Plugins.Dog>(roundtripped.Animal);
            var dog = (CrossProject.Plugins.Dog)roundtripped.Animal;
            Assert.Equal("Rex", dog.Name);
            Assert.Equal("Collie", dog.Breed);
        }

        [Fact]
        public void GeneratedContextSupportsCrossProjectTagMappings()
        {
            var context = new CrossProjectYamlContext(
                new YamlSerializerOptions
                {
                    PolymorphismOptions = new YamlPolymorphismOptions
                    {
                        DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                    },
                });

            var typeInfo = context.CrossProjectZoo;
            var yaml = YamlSerializer.Serialize(
                new CrossProjectZoo
                {
                    Animal = new CrossProject.Plugins.Cat { Name = "Mittens", Indoor = true },
                },
                typeInfo);

            Assert.Contains("!cat", yaml);
            Assert.DoesNotContain("$type:", yaml);

            var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
            Assert.NotNull(roundtripped?.Animal);
            Assert.IsType<CrossProject.Plugins.Cat>(roundtripped.Animal);
            var cat = (CrossProject.Plugins.Cat)roundtripped.Animal;
            Assert.Equal("Mittens", cat.Name);
            Assert.True(cat.Indoor);
        }

        [Fact]
        public void GeneratedContextAutoIncludesDerivedTypesReferencedByMappings()
        {
            var context = new CrossProjectYamlContext();
            var typeInfo = context.GetTypeInfo(typeof(CrossProject.Plugins.Dog), context.Options);

            Assert.NotNull(typeInfo);

            var yaml = YamlSerializer.Serialize(
                new CrossProject.Plugins.Dog { Name = "Scout", Breed = "Husky" },
                typeof(CrossProject.Plugins.Dog),
                context);

            var roundtripped = (CrossProject.Plugins.Dog?)YamlSerializer.Deserialize(
                yaml,
                typeof(CrossProject.Plugins.Dog),
                context);
            Assert.NotNull(roundtripped);
            Assert.Equal("Scout", roundtripped.Name);
            Assert.Equal("Husky", roundtripped.Breed);
        }

        [Fact]
        public void GeneratedContextKeepsAttributeMappingsAheadOfContextMappings()
        {
            var context = new CrossProjectYamlContext();
            var typeInfo = context.AttributeMappedZoo;

            var roundtripped = YamlSerializer.Deserialize(
                "Animal:\n  $type: dog\n  Name: Spot\n  BarkVolume: 5\n",
                typeInfo);

            Assert.NotNull(roundtripped?.Animal);
            Assert.IsType<CrossProject.AttributePlugins.BuiltInDog>(roundtripped.Animal);
            Assert.Equal(5, ((CrossProject.AttributePlugins.BuiltInDog)roundtripped.Animal).BarkVolume);

            var exception = Assert.Throws<NotSupportedException>(
                () => YamlSerializer.Serialize(
                    new AttributeMappedZoo
                    {
                        Animal = new CrossProject.AttributePlugins.ConflictingDog { Name = "Patch", Skill = "herding" },
                    },
                    typeInfo));
            Assert.Contains(typeof(CrossProject.AttributePlugins.ConflictingDog).ToString(), exception.Message);
        }

        [Fact]
        public void YamlDerivedTypeMappingAttributeValidatesArgumentsAndStoresValues()
        {
            Assert.Throws<ArgumentNullException>(() => new YamlDerivedTypeMappingAttribute(null!, typeof(CrossProject.Plugins.Dog)));
            Assert.Throws<ArgumentNullException>(() => new YamlDerivedTypeMappingAttribute(typeof(CrossProject.Core.Animal), null!));
            Assert.Throws<ArgumentNullException>(() => new YamlDerivedTypeMappingAttribute(typeof(CrossProject.Core.Animal), typeof(CrossProject.Plugins.Dog), (string)null!));

            var mapping = new YamlDerivedTypeMappingAttribute(
                typeof(CrossProject.Core.Animal),
                typeof(CrossProject.Plugins.Cat),
                2)
            {
                Tag = "!cat",
            };

            Assert.Equal(typeof(CrossProject.Core.Animal), mapping.BaseType);
            Assert.Equal(typeof(CrossProject.Plugins.Cat), mapping.DerivedType);
            Assert.Equal("2", mapping.Discriminator);
            Assert.Equal("!cat", mapping.Tag);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RuntimeClosedTypeInferenceWritesAndReadsDerivedTypes(bool useSourceGeneration)
        {
            var options = CreateRuntimeInferenceOptions(inferClosedTypePolymorphism: true);
            var value = new RuntimeClosedHierarchy.RuntimeShapeHolder
            {
                Shape = new RuntimeClosedHierarchy.RuntimeCircle { Name = "circle", Radius = 3 },
                Shapes =
                [
                    new RuntimeClosedHierarchy.RuntimeSquare { Name = "square", Side = 4 },
                    new RuntimeClosedHierarchy.RuntimeCircle { Name = "other", Radius = 5 },
                ],
            };

            var yaml = SerializeWithRuntimeInference(value, options, useSourceGeneration);

            Assert.Equal("Shape:\n  $type: RuntimeCircle\n  Name: circle\n  Radius: 3\nShapes:\n  - $type: RuntimeSquare\n    Name: square\n    Side: 4\n  - $type: RuntimeCircle\n    Name: other\n    Radius: 5\n", yaml);

            var roundtripped = DeserializeWithRuntimeInference<RuntimeClosedHierarchy.RuntimeShapeHolder>(yaml, options, useSourceGeneration);
            var circle = Assert.IsType<RuntimeClosedHierarchy.RuntimeCircle>(roundtripped?.Shape);
            Assert.Equal("circle", circle.Name);
            Assert.Equal(3, circle.Radius);
            Assert.Collection(
                roundtripped!.Shapes,
                shape => Assert.Equal(4, Assert.IsType<RuntimeClosedHierarchy.RuntimeSquare>(shape).Side),
                shape => Assert.Equal(5, Assert.IsType<RuntimeClosedHierarchy.RuntimeCircle>(shape).Radius));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RuntimeClosedTypeInferenceAppliesToTheListedClosedType(bool useSourceGeneration)
        {
            var options = CreateRuntimeInferenceOptions(inferClosedTypePolymorphism: true);
            RuntimeClosedHierarchy.RuntimeShape value = new RuntimeClosedHierarchy.RuntimeSquare { Name = "square", Side = 4 };

            var yaml = SerializeWithRuntimeInference(value, options, useSourceGeneration);

            Assert.Equal("$type: RuntimeSquare\nName: square\nSide: 4\n", yaml);
            var square = Assert.IsType<RuntimeClosedHierarchy.RuntimeSquare>(DeserializeWithRuntimeInference<RuntimeClosedHierarchy.RuntimeShape>(yaml, options, useSourceGeneration));
            Assert.Equal("square", square.Name);
            Assert.Equal(4, square.Side);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RuntimeClosedTypeInferenceIsDisabledByDefault(bool useSourceGeneration)
        {
            var options = CreateRuntimeInferenceOptions(inferClosedTypePolymorphism: false);
            var value = new RuntimeClosedHierarchy.RuntimeShapeHolder
            {
                Shape = new RuntimeClosedHierarchy.RuntimeCircle { Name = "circle", Radius = 3 },
            };

            var yaml = SerializeWithRuntimeInference(value, options, useSourceGeneration);

            Assert.Equal("Shape:\n  Name: circle\nShapes: []\n", yaml);
            var exception = Assert.Throws<YamlException>(() => DeserializeWithRuntimeInference<RuntimeClosedHierarchy.RuntimeShapeHolder>("Shape:\n  $type: RuntimeCircle\n  Name: circle\n", options, useSourceGeneration));
            Assert.Contains("'" + typeof(RuntimeClosedHierarchy.RuntimeShape).FullName + "'", exception.Message);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RuntimeClosedTypeInferenceRegistersDescendantsOfNestedClosedTypes(bool useSourceGeneration)
        {
            var options = CreateRuntimeInferenceOptions(inferClosedTypePolymorphism: true);
            var value = new RuntimeClosedHierarchy.RuntimePetHolder
            {
                Pet = new RuntimeClosedHierarchy.RuntimeLabrador { Name = "Rex", GoodBoy = true, Color = "chocolate" },
                Dog = new RuntimeClosedHierarchy.RuntimeLabrador { Name = "Max", Color = "black" },
            };

            var yaml = SerializeWithRuntimeInference(value, options, useSourceGeneration);

            Assert.Equal("Pet:\n  $type: RuntimeLabrador\n  Name: Rex\n  GoodBoy: true\n  Color: chocolate\nDog:\n  $type: RuntimeLabrador\n  Name: Max\n  GoodBoy: false\n  Color: black\n", yaml);

            var roundtripped = DeserializeWithRuntimeInference<RuntimeClosedHierarchy.RuntimePetHolder>(yaml, options, useSourceGeneration);
            var labrador = Assert.IsType<RuntimeClosedHierarchy.RuntimeLabrador>(roundtripped?.Pet);
            Assert.Equal("Rex", labrador.Name);
            Assert.True(labrador.GoodBoy);
            Assert.Equal("chocolate", labrador.Color);
            Assert.Equal("black", Assert.IsType<RuntimeClosedHierarchy.RuntimeLabrador>(roundtripped!.Dog).Color);

            var cat = DeserializeWithRuntimeInference<RuntimeClosedHierarchy.RuntimePetHolder>("Pet:\n  $type: RuntimeCat\n  Indoor: true\n", options, useSourceGeneration);
            Assert.True(Assert.IsType<RuntimeClosedHierarchy.RuntimeCat>(cat?.Pet).Indoor);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void RuntimeClosedTypeInferenceIsOverriddenByTheDeclaration(bool inferClosedTypePolymorphism, bool useSourceGeneration)
        {
            var options = CreateRuntimeInferenceOptions(inferClosedTypePolymorphism);
            var value = new RuntimeClosedHierarchy.RuntimeAttributeShapeHolder
            {
                OptIn = new RuntimeClosedHierarchy.RuntimeOptInCircle { Name = "in", Radius = 1 },
                OptOut = new RuntimeClosedHierarchy.RuntimeOptOutCircle { Name = "out", Radius = 2 },
            };

            var yaml = SerializeWithRuntimeInference(value, options, useSourceGeneration);

            Assert.Equal("OptIn:\n  $type: RuntimeOptInCircle\n  Name: in\n  Radius: 1\nOptOut:\n  Name: out\nCustom: null\nExplicit: null\n", yaml);
            var roundtripped = DeserializeWithRuntimeInference<RuntimeClosedHierarchy.RuntimeAttributeShapeHolder>("OptIn:\n  $type: RuntimeOptInCircle\n  Radius: 1\n", options, useSourceGeneration);
            Assert.Equal(1, Assert.IsType<RuntimeClosedHierarchy.RuntimeOptInCircle>(roundtripped?.OptIn).Radius);
            Assert.Throws<YamlException>(() => DeserializeWithRuntimeInference<RuntimeClosedHierarchy.RuntimeAttributeShapeHolder>("OptOut:\n  $type: RuntimeOptOutCircle\n  Radius: 2\n", options, useSourceGeneration));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RuntimeClosedTypeInferenceIsReplacedByExplicitDerivedTypes(bool useSourceGeneration)
        {
            var options = CreateRuntimeInferenceOptions(inferClosedTypePolymorphism: true);
            var value = new RuntimeClosedHierarchy.RuntimeAttributeShapeHolder
            {
                Explicit = new RuntimeClosedHierarchy.RuntimeExplicitCircle { Name = "explicit", Radius = 3 },
            };

            var yaml = SerializeWithRuntimeInference(value, options, useSourceGeneration);

            Assert.Equal("OptIn: null\nOptOut: null\nCustom: null\nExplicit:\n  $type: custom\n  Name: explicit\n  Radius: 3\n", yaml);
            Assert.IsType<RuntimeClosedHierarchy.RuntimeExplicitCircle>(DeserializeWithRuntimeInference<RuntimeClosedHierarchy.RuntimeAttributeShapeHolder>(yaml, options, useSourceGeneration)?.Explicit);

            var unregistered = new RuntimeClosedHierarchy.RuntimeAttributeShapeHolder
            {
                Explicit = new RuntimeClosedHierarchy.RuntimeExplicitSquare { Name = "square", Side = 4 },
            };
            Assert.Throws<NotSupportedException>(() => SerializeWithRuntimeInference(unregistered, options, useSourceGeneration));
            Assert.Throws<YamlException>(() => DeserializeWithRuntimeInference<RuntimeClosedHierarchy.RuntimeAttributeShapeHolder>("Explicit:\n  $type: RuntimeExplicitSquare\n", options, useSourceGeneration));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RuntimeClosedTypeInferenceHonorsDiscriminatorSettings(bool useSourceGeneration)
        {
            var options = new YamlSerializerOptions
            {
                PolymorphismOptions = new YamlPolymorphismOptions
                {
                    InferClosedTypePolymorphism = true,
                    TypeDiscriminatorPropertyName = "$t",
                    DiscriminatorStyle = YamlTypeDiscriminatorStyle.Both,
                },
            };
            var value = new RuntimeClosedHierarchy.RuntimeAttributeShapeHolder
            {
                Custom = new RuntimeClosedHierarchy.RuntimeCustomDiscriminatorCircle { Name = "custom", Radius = 3 },
            };
            var holder = new RuntimeClosedHierarchy.RuntimeShapeHolder
            {
                Shape = new RuntimeClosedHierarchy.RuntimeCircle { Name = "circle", Radius = 3 },
            };

            var yaml = SerializeWithRuntimeInference(value, options, useSourceGeneration) + SerializeWithRuntimeInference(holder, options, useSourceGeneration);

            Assert.Equal("OptIn: null\nOptOut: null\nCustom:\n  $kind: RuntimeCustomDiscriminatorCircle\n  Name: custom\n  Radius: 3\nExplicit: null\nShape:\n  $t: RuntimeCircle\n  Name: circle\n  Radius: 3\nShapes: []\n", yaml);
            Assert.IsType<RuntimeClosedHierarchy.RuntimeCircle>(DeserializeWithRuntimeInference<RuntimeClosedHierarchy.RuntimeShapeHolder>("Shape:\n  $t: RuntimeCircle\n", options, useSourceGeneration)?.Shape);
            Assert.IsType<RuntimeClosedHierarchy.RuntimeCustomDiscriminatorCircle>(DeserializeWithRuntimeInference<RuntimeClosedHierarchy.RuntimeAttributeShapeHolder>("Custom:\n  $kind: RuntimeCustomDiscriminatorCircle\n", options, useSourceGeneration)?.Custom);
            Assert.Throws<YamlException>(() => DeserializeWithRuntimeInference<RuntimeClosedHierarchy.RuntimeShapeHolder>("Shape:\n  $t: Unknown\n", options, useSourceGeneration));
        }

        private static YamlSerializerOptions CreateRuntimeInferenceOptions(bool inferClosedTypePolymorphism) => new()
        {
            PolymorphismOptions = new YamlPolymorphismOptions { InferClosedTypePolymorphism = inferClosedTypePolymorphism },
        };

        private static string SerializeWithRuntimeInference<T>(T value, YamlSerializerOptions options, bool useSourceGeneration)
            => useSourceGeneration
                ? YamlSerializer.Serialize(value, new RuntimeInferredClosedTypeYamlContext(options))
                : YamlSerializer.Serialize(value, options);

        private static T? DeserializeWithRuntimeInference<T>(string yaml, YamlSerializerOptions options, bool useSourceGeneration)
            => useSourceGeneration
                ? YamlSerializer.Deserialize<T>(yaml, new RuntimeInferredClosedTypeYamlContext(options))
                : YamlSerializer.Deserialize<T>(yaml, options);
    }
}
