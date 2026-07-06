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
    public class YamlSourceGeneratedDerivedTypeMappingTests
    {
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
