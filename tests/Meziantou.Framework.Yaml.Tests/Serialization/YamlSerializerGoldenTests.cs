using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public class YamlSerializerGoldenTests
{
    private sealed class OrderingModel
    {
        public string Zeta { get; set; } = string.Empty;

        public string Alpha { get; set; } = string.Empty;
    }

    private sealed class JsonAnnotatedPerson
    {
        [YamlPropertyOrder(-10)]
        public int Age { get; set; }

        [YamlPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [YamlInclude]
        public string Secret { get; private set; } = string.Empty;

        public void SetSecret(string value)
        {
            Secret = value;
        }
    }

    [Fact]
    public void DeclarationOrderMatchesGoldenFile()
    {
        var yaml = YamlSerializer.Serialize(
            new OrderingModel
            {
                Zeta = "z",
                Alpha = "a",
            });

        GoldenFileAssert.AreEqual("v3/ordered_declaration.yaml", yaml);
    }

    [Fact]
    public void SortedOrderMatchesGoldenFile()
    {
        var yaml = YamlSerializer.Serialize(
            new OrderingModel
            {
                Zeta = "z",
                Alpha = "a",
            },
            new YamlSerializerOptions
            {
                MappingOrder = YamlMappingOrderPolicy.Sorted,
            });

        GoldenFileAssert.AreEqual("v3/ordered_sorted.yaml", yaml);
    }

    [Fact]
    public void JsonAttributesProjectionMatchesGoldenFile()
    {
        var person = new JsonAnnotatedPerson
        {
            Age = 37,
            FirstName = "Ada",
        };
        person.SetSecret("s3cr3t");

        var yaml = YamlSerializer.Serialize(person);
        GoldenFileAssert.AreEqual("v3/json_attributes.yaml", yaml);
    }
}
