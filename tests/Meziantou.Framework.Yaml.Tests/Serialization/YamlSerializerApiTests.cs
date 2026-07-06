using Meziantou.Framework.Yaml.Model;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public class YamlSerializerApiTests
{
    private sealed class Person
    {
        public string FirstName { get; set; } = string.Empty;

        public int Age { get; set; }
    }

    private sealed class OrderingModel
    {
        public string Zeta { get; set; } = string.Empty;

        public string Alpha { get; set; } = string.Empty;
    }

    private sealed class YamlNodePayload
    {
        public string Name { get; set; } = string.Empty;

        public YamlNode? Content { get; set; }
    }

    private sealed class StringTypeInfo : YamlTypeInfo<string>
    {
        public StringTypeInfo(YamlSerializerOptions options) : base(options)
        {
        }

        public override void Write(YamlWriter writer, string value)
        {
            writer.WriteStartMapping();
            writer.WritePropertyName("value");
            writer.WriteScalar(value);
            writer.WriteEndMapping();
        }

        public override string Read(YamlReader reader)
        {
            if (reader.TokenType != YamlTokenType.StartMapping)
            {
                throw YamlThrowHelper.ThrowExpectedMapping(reader);
            }

            reader.Read();
            var value = string.Empty;
            while (reader.TokenType != YamlTokenType.EndMapping)
            {
                if (reader.TokenType != YamlTokenType.Scalar)
                {
                    throw YamlThrowHelper.ThrowExpectedScalarKey(reader);
                }

                var key = reader.ScalarValue ?? string.Empty;
                reader.Read();
                if (string.Equals(key, "value", StringComparison.Ordinal))
                {
                    if (reader.TokenType != YamlTokenType.Scalar)
                    {
                        throw YamlThrowHelper.ThrowExpectedScalar(reader);
                    }

                    value = reader.ScalarValue ?? string.Empty;
                    reader.Read();
                    continue;
                }

                reader.Skip();
            }

            reader.Read();
            return value;
        }
    }

    private sealed class StringTypeInfoResolver : IYamlTypeInfoResolver
    {
        public StringTypeInfoResolver(YamlTypeInfo typeInfo)
        {
            TypeInfo = typeInfo;
        }

        public YamlTypeInfo TypeInfo { get; }

        public YamlTypeInfo? GetTypeInfo(Type type, YamlSerializerOptions options)
        {
            return type == typeof(string) ? TypeInfo : null;
        }
    }

    private sealed class JsonAnnotatedPerson
    {
        [YamlPropertyOrder(-10)]
        public int Age { get; set; }

        [YamlPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;

        public string? NickName { get; set; }

        [YamlInclude]
        public string Secret { get; private set; } = string.Empty;

        public void SetSecret(string secret)
        {
            Secret = secret;
        }
    }

    private sealed class YamlAndJsonNamedModel
    {
        [YamlPropertyName("yaml_name")]
        [YamlPropertyOrder(-100)]
        public string Name { get; set; } = string.Empty;

        public int Rank { get; set; }
    }

    [Fact]
    public void SerializeAndDeserializeRoundTrip()
    {
        var value = new Person { FirstName = "Ada", Age = 37 };
        var yaml = YamlSerializer.Serialize(value);
        var roundTrip = YamlSerializer.Deserialize<Person>(yaml);

        Assert.NotNull(roundTrip);
        Assert.Equal("Ada", roundTrip.FirstName);
        Assert.Equal(37, roundTrip.Age);
    }

    [Fact]
    public void ReflectionContext_YamlNodeRoot_DeserializesDynamicContent()
    {
        var yaml = "items:\n- one\n- two\n";

        var node = YamlSerializer.Deserialize<YamlNode>(yaml);

        Assert.IsType(typeof(YamlMapping), node);
        var mapping = (YamlMapping)node!;
        Assert.IsType(typeof(YamlSequence), mapping["items"]);
    }

    [Fact]
    public void ReflectionContext_YamlNodeMember_RoundTripsDynamicContent()
    {
        var yaml = """
            Name: dynamic
            Content:
              values:
              - one
              - two
            """;

        var payload = YamlSerializer.Deserialize<YamlNodePayload>(yaml);

        Assert.NotNull(payload);
        Assert.Equal("dynamic", payload.Name);
        Assert.IsType(typeof(YamlMapping), payload.Content);

        var serialized = YamlSerializer.Serialize(payload);

        Assert.Contains("Content:", serialized);
        Assert.Contains("values:", serialized);
    }

    [Fact]
    public void MappingOrderDefaultsToDeclaration()
    {
        var yaml = YamlSerializer.Serialize(new OrderingModel
        {
            Zeta = "z",
            Alpha = "a",
        });

        var zetaIndex = yaml.IndexOf("Zeta:", StringComparison.Ordinal);
        var alphaIndex = yaml.IndexOf("Alpha:", StringComparison.Ordinal);
        Assert.True(zetaIndex >= 0);
        Assert.True(alphaIndex > zetaIndex);
    }

    [Fact]
    public void MappingOrderCanBeSorted()
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

        var zetaIndex = yaml.IndexOf("Zeta:", StringComparison.Ordinal);
        var alphaIndex = yaml.IndexOf("Alpha:", StringComparison.Ordinal);
        Assert.True(alphaIndex >= 0);
        Assert.True(zetaIndex > alphaIndex);
    }

    [Fact]
    public void SerializeWithCamelCasePolicy()
    {
        var options = new YamlSerializerOptions { PropertyNamingPolicy = YamlNamingPolicy.CamelCase };
        var yaml = YamlSerializer.Serialize(new Person { FirstName = "Ada", Age = 37 }, options);

        Assert.Contains("firstName", yaml);
        Assert.Contains("age", yaml);
    }

    [Fact]
    public void DeserializeFromReadOnlySpan()
    {
        ReadOnlySpan<char> yaml = "FirstName: Ada\nAge: 37";
        var result = YamlSerializer.Deserialize<Person>(yaml);

        Assert.NotNull(result);
        Assert.Equal("Ada", result.FirstName);
        Assert.Equal(37, result.Age);
    }

    [Fact]
    public void SerializeWithExplicitTypeInfo()
    {
        var typeInfo = new StringTypeInfo(new YamlSerializerOptions());
        var yaml = YamlSerializer.Serialize("hello", typeInfo);
        var value = YamlSerializer.Deserialize(yaml, typeInfo);

        Assert.Equal("value: hello\n", yaml);
        Assert.Equal("hello", value);
    }

    [Fact]
    public void SerializeWithResolverTypeInfo()
    {
        var typeInfo = new StringTypeInfo(new YamlSerializerOptions());
        var options = new YamlSerializerOptions
        {
            TypeInfoResolver = new StringTypeInfoResolver(typeInfo),
        };

        var yaml = YamlSerializer.Serialize("hello", typeof(string), options);
        var value = YamlSerializer.Deserialize(yaml, typeof(string), options);

        Assert.Equal("value: hello\n", yaml);
        Assert.Equal("hello", value);
    }

    [Fact]
    public void JsonAttributesAreRespectedByReflectionSerializer()
    {
        var person = new JsonAnnotatedPerson
        {
            Age = 37,
            FirstName = "Ada",
            NickName = null,
        };
        person.SetSecret("s3cr3t");

        var yaml = YamlSerializer.Serialize(person, new YamlSerializerOptions { DefaultIgnoreCondition = YamlIgnoreCondition.WhenWritingNull });
        var ageIndex = yaml.IndexOf("Age:", StringComparison.Ordinal);
        var firstNameIndex = yaml.IndexOf("first_name:", StringComparison.Ordinal);

        Assert.True(ageIndex >= 0);
        Assert.True(firstNameIndex > ageIndex);
        Assert.Contains("first_name: Ada", yaml);
        Assert.Contains("Secret: s3cr3t", yaml);
        Assert.DoesNotContain("NickName:", yaml);

        var roundTrip = YamlSerializer.Deserialize<JsonAnnotatedPerson>(
            "Age: 37\nfirst_name: Ada\nSecret: from-yaml");

        Assert.NotNull(roundTrip);
        Assert.Equal(37, roundTrip.Age);
        Assert.Equal("Ada", roundTrip.FirstName);
        Assert.Equal("from-yaml", roundTrip.Secret);
    }

    [Fact]
    public void YamlSpecificAttributesOverrideJsonAttributes()
    {
        var yaml = YamlSerializer.Serialize(new YamlAndJsonNamedModel
        {
            Name = "value",
            Rank = 7,
        });
        var yamlNameIndex = yaml.IndexOf("yaml_name:", StringComparison.Ordinal);
        var rankIndex = yaml.IndexOf("Rank:", StringComparison.Ordinal);

        Assert.True(yamlNameIndex >= 0);
        Assert.True(rankIndex > yamlNameIndex);
        Assert.DoesNotContain("json_name:", yaml);

        var roundTrip = YamlSerializer.Deserialize<YamlAndJsonNamedModel>("yaml_name: value\nRank: 7");
        Assert.NotNull(roundTrip);
        Assert.Equal("value", roundTrip.Name);
        Assert.Equal(7, roundTrip.Rank);
    }
}
