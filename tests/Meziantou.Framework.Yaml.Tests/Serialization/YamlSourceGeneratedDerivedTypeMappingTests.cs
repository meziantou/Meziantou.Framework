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
    }
}
