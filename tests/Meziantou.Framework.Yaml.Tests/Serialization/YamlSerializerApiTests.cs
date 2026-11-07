using Meziantou.Framework.Yaml.Model;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public class YamlSerializerApiTests
{
    [Fact]
    public void ReadmeExample_SerializeAndDeserialize()
    {
        #region YamlSerializer_SerializeAndDeserialize
        var options = new YamlSerializerOptions
        {
            PropertyNamingPolicy = YamlNamingPolicy.KebabCaseLower,
        };

        var yaml = YamlSerializer.Serialize(new Product
        {
            Id = 1,
            DisplayName = "Sample product",
            Tags = ["new", "featured"],
        }, options);

        Assert.Equal("""
            id: 1
            name: Sample product
            tags:
              - new
              - featured

            """, yaml);

        var product = YamlSerializer.Deserialize<Product>(yaml, options);
        Assert.Equal("Sample product", product!.DisplayName);
        #endregion
    }

    [Fact]
    public void ReadmeExample_TryDeserialize()
    {
        #region YamlSerializer_TryDeserialize
        if (YamlSerializer.TryDeserialize<Product>("Id: 1", out var value))
        {
            Assert.Equal(1, value!.Id);
        }

        Assert.False(YamlSerializer.TryDeserialize<Product>("Id: not a number", out _));
        #endregion

        Assert.NotNull(value);
    }

    [Fact]
    public void ReadmeExample_LiteralStringStyle()
    {
        #region YamlScalarStylePreferences_StringStyle
        var options = new YamlSerializerOptions
        {
            ScalarStylePreferences = new YamlScalarStylePreferences { StringStyle = ScalarStyle.Literal },
        };

        var yaml = YamlSerializer.Serialize(new { Script = "echo one\necho two\n" }, options);
        #endregion

        Assert.Equal("Script: |\n  echo one\n  echo two\n", yaml);
    }

    [Fact]
    public void ReadmeExample_StringStyleAttribute()
    {
        var yaml = YamlSerializer.Serialize(new Job { Script = "echo one\necho two", Name = "build" });

        Assert.Equal("Script: |-\n  echo one\n  echo two\nName: build\n", yaml);
    }

    #region YamlStringStyleAttribute_Usage
    internal sealed class Job
    {
        [YamlStringStyle(ScalarStyle.Literal)]
        public string? Script { get; set; }

        public string? Name { get; set; }
    }
    #endregion

    [Fact]
    public void ReadmeExample_AmbiguousScalars()
    {
        #region YamlScalarStylePreferences_AmbiguousScalars
        var yaml = YamlSerializer.Serialize(new { A = "yes", B = "123", C = "plain" });
        #endregion

        Assert.Equal("A: yes\nB: \"123\"\nC: plain\n", yaml);
    }

    [Fact]
    public void ReadmeExample_Polymorphism()
    {
        #region YamlPolymorphism_RoundTrip
        var yaml = YamlSerializer.Serialize<Animal>(new Dog { Name = "Rex" });
        Assert.Equal("$type: dog\nName: Rex\n", yaml);

        var animal = YamlSerializer.Deserialize<Animal>(yaml);
        Assert.Equal("Rex", Assert.IsType<Dog>(animal).Name);
        #endregion
    }

    #region YamlPolymorphicAttribute_Usage
    [YamlPolymorphic]
    [YamlDerivedType(typeof(Dog), "dog")]
    [YamlDerivedType(typeof(Cat), "cat")]
    internal abstract class Animal
    {
        public string Name { get; set; } = "";
    }

    internal sealed class Dog : Animal;

    internal sealed class Cat : Animal;
    #endregion

    [Fact]
    public void ReadmeExample_InferClosedTypePolymorphismOnType()
    {
        var yaml = YamlSerializer.Serialize<Shape>(new Square());

        Assert.Equal("$type: Square\n", yaml);
        Assert.IsType<Square>(YamlSerializer.Deserialize<Shape>(yaml));
    }

    #region YamlPolymorphicAttribute_InferClosedTypePolymorphism
    [YamlPolymorphic(InferClosedTypePolymorphism = true)]
    public closed class Shape
    {
    }
    #endregion

    public sealed class Square : Shape;

    [Fact]
    public void ReadmeExample_FromObject()
    {
        #region YamlNode_FromObject
        var product = new Product { Id = 1, DisplayName = "Sample product" };

        var node = YamlNode.FromObject(product);
        var back = node.ToObject<Product>();
        Assert.Equal("Sample product", back!.DisplayName);
        #endregion
    }

    [Fact]
    public void ReadmeExample_SourceName()
    {
        #region YamlSerializerOptions_SourceName
        var options = new YamlSerializerOptions { SourceName = "config.yaml" };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Product>("Id: [1, 2]", options));
        Assert.Equal("config.yaml: (Lin: 0, Col: 4, Chr: 4) - (Lin: 0, Col: 4, Chr: 4): Expected a Scalar token but found 'StartSequence'.", exception.Message);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Hardening()
    {
        #region YamlSerializerOptions_Hardening
        var options = new YamlSerializerOptions
        {
            MaxDepth = 32,
            AllowAnchors = false,
            AllowAliases = false,
            UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow,
        };

        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Product>("Id: &id 1", options));
        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Product>("Price: 10", options));
        #endregion
    }

    [Fact]
    public void Documentation_Files()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory.GetFullPath("product.yaml");

        SaveProduct(path, new Product { Id = 1, DisplayName = "Sample product" });
        Assert.Equal("Sample product", LoadProduct(path)!.DisplayName);
    }

    #region YamlSerializer_Files
    private static void SaveProduct(string path, Product product)
    {
        using var stream = File.Create(path);
        YamlSerializer.Serialize(stream, product); // Written as UTF-8
    }

    private static Product? LoadProduct(string path)
    {
        using var stream = File.OpenRead(path);
        return YamlSerializer.Deserialize<Product>(stream);
    }
    #endregion

    [Fact]
    public void Documentation_OptionsWith()
    {
        #region YamlSerializerOptions_With
        var strict = YamlSerializerOptions.Default with
        {
            UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow,
        };

        Assert.Equal(1, YamlSerializer.Deserialize<Product>("Id: 1\nPrice: 10")!.Id); // Price is ignored
        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Product>("Id: 1\nPrice: 10", strict));
        #endregion
    }

    [Fact]
    public void Documentation_WriteIndented()
    {
        #region YamlSerializerOptions_WriteIndented
        var product = new Product { Id = 1, DisplayName = "Table", Tags = ["new", "sale"] };

        var yaml = YamlSerializer.Serialize(product, new YamlSerializerOptions { WriteIndented = false });
        Assert.Equal("{Id: 1, name: Table, Tags: [new, sale]}\n", yaml);
        #endregion
    }

    [Fact]
    public void Documentation_BlockSequences()
    {
        #region YamlSerializerOptions_BlockSequences
        var value = new
        {
            Tags = new[] { "new", "sale" },
            Items = new[] { new { Id = 1, Name = "first" } },
        };

        Assert.Equal("""
            Tags:
              - new
              - sale
            Items:
              - Id: 1
                Name: first

            """, YamlSerializer.Serialize(value));

        var options = new YamlSerializerOptions
        {
            IndentBlockSequences = false,
            BlockSequenceMappingStyle = YamlSequenceItemStyle.Expanded,
        };

        Assert.Equal("""
            Tags:
            - new
            - sale
            Items:
            -
              Id: 1
              Name: first

            """, YamlSerializer.Serialize(value, options));
        #endregion
    }

    [Fact]
    public void Documentation_AmbiguousStrings()
    {
        #region YamlSerializerOptions_AmbiguousStrings
        var yaml = YamlSerializer.Serialize(new { A = "yes", B = "123", C = "true", D = "plain" });
        Assert.Equal("""
            A: yes
            B: "123"
            C: "true"
            D: plain

            """, yaml);
        #endregion
    }

    [Fact]
    public void Documentation_LiteralStrings()
    {
        #region YamlSerializerOptions_LiteralStrings
        var options = new YamlSerializerOptions
        {
            ScalarStylePreferences = new YamlScalarStylePreferences { StringStyle = ScalarStyle.Literal },
        };

        var yaml = YamlSerializer.Serialize(new { Script = "echo one\necho two\n" }, options);
        Assert.Equal("""
            Script: |
              echo one
              echo two

            """, yaml);
        #endregion
    }

    [Fact]
    public void Documentation_StringStyleAttribute()
    {
        #region YamlStringStyleAttribute_Serialize
        var yaml = YamlSerializer.Serialize(new Job { Script = "echo one\necho two", Name = "build" });
        Assert.Equal("""
            Script: |-
              echo one
              echo two
            Name: build

            """, yaml);
        #endregion
    }

    [Fact]
    public void Documentation_NumberHandling()
    {
        #region YamlNumberHandlingAttribute_Serialize
        var yaml = YamlSerializer.Serialize(new Server { Port = 8080 });
        Assert.Equal("Port: \"8080\"\n", yaml);

        Assert.Equal(8080, YamlSerializer.Deserialize<Server>(yaml)!.Port);
        #endregion

        Assert.Equal(1, YamlSerializer.Deserialize<Product>("Id: \"1\"")!.Id);
    }

    #region YamlNumberHandlingAttribute_Usage
    internal sealed class Server
    {
        [YamlNumberHandling(YamlNumberHandling.WriteAsString)]
        public int Port { get; set; }
    }
    #endregion

    [Fact]
    public void Documentation_Record()
    {
        #region YamlSerializer_RecordDeserialize
        var endpoint = YamlSerializer.Deserialize<Endpoint>("Url: https://example.com");
        Assert.Equal(30, endpoint!.Timeout);

        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Endpoint>("Timeout: 10")); // Url is missing
        #endregion
    }

    #region YamlSerializer_Record
    internal sealed record Endpoint(string Url, int Timeout = 30);
    #endregion

    [Fact]
    public void Documentation_Untyped()
    {
        #region YamlSerializer_Untyped
        var value = YamlSerializer.Deserialize<object>("""
            name: api
            replicas: 3
            ports: [80, 443]
            """);

        var root = Assert.IsType<Dictionary<object, object?>>(value);
        Assert.Equal("api", root["name"]);
        Assert.Equal(3L, root["replicas"]);
        var ports = Assert.IsType<List<object?>>(root["ports"]);
        Assert.Equal(443L, ports[1]);
        #endregion
    }

    [Fact]
    public void Documentation_ExceptionLocation()
    {
        #region YamlException_Location
        static string? Validate(string yaml)
        {
            try
            {
                YamlSerializer.Deserialize<Product>(yaml);
                return null;
            }
            catch (YamlException exception)
            {
                // Line and Column are zero-based
                return $"line {exception.Start.Line + 1}, column {exception.Start.Column + 1}";
            }
        }

        Assert.Null(Validate("Id: 1"));
        Assert.Equal("line 2, column 7", Validate("Id: 1\nTags: 3"));
        Assert.Equal("line 1, column 5", Validate("Id: [1"));
        #endregion
    }

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

    private sealed class StringPayload
    {
        public string Value { get; set; } = string.Empty;
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
    public void ReflectionContext_YamlNodeRoot_PreservesOriginalNodeLocations()
    {
        var yaml = "foo: bar\n\nbaz:\n  bla: bloo\nseq:\n\n- item\n";

        var root = YamlSerializer.Deserialize<YamlMapping>(yaml)!;

        var baz = (YamlMapping)root["baz"]!;
        Assert.Equal(3, baz.MappingStart.Start.Line);

        var sequence = (YamlSequence)root["seq"]!;
        Assert.Equal(6, sequence.SequenceStart.Start.Line);
    }

    [Fact]
    public void ReflectionContext_YamlValueScalar_IsPublicAndPreservesOriginalNodeLocation()
    {
        var property = typeof(YamlValue).GetProperty(nameof(YamlValue.Scalar));
        Assert.NotNull(property);
        Assert.True(property.GetMethod!.IsPublic);
        Assert.True(property.SetMethod!.IsPublic);

        var root = YamlSerializer.Deserialize<YamlMapping>("key:\n value\n")!;
        var value = (YamlValue)root["key"]!;

        Assert.Equal(1, value.Scalar.Start.Line);
        Assert.Equal(1, value.Scalar.Start.Column);
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
    public void ReflectionContext_YamlNodeMember_PreservesOriginalNodeLocations()
    {
        var yaml = """
            Name: dynamic
            Content:
              foo: bar

              baz:
                bla: bloo
            """;

        var payload = YamlSerializer.Deserialize<YamlNodePayload>(yaml)!;

        var content = (YamlMapping)payload.Content!;
        var baz = (YamlMapping)content["baz"]!;
        Assert.Equal(5, baz.MappingStart.Start.Line);
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
    public void TextReaderWriterOverloadsSupportExplicitTypeInfo()
    {
        var typeInfo = new StringTypeInfo(new YamlSerializerOptions());
        using var writer = new System.IO.StringWriter();

        YamlSerializer.Serialize(writer, (object)"hello", typeInfo);
        using var reader = new System.IO.StringReader(writer.ToString());
        var value = YamlSerializer.Deserialize(reader, typeInfo);

        Assert.Equal("value: hello\n", writer.ToString());
        Assert.Equal("hello", value);
    }

    [Fact]
    public void StreamOverloadsSupportExplicitTypeInfo()
    {
        var typeInfo = new StringTypeInfo(new YamlSerializerOptions());
        using var stream = new System.IO.MemoryStream();

        YamlSerializer.Serialize(stream, "hello", typeInfo);
        stream.Position = 0;
        var value = YamlSerializer.Deserialize(stream, typeInfo);

        Assert.Equal("hello", value);
        Assert.Equal("value: hello\n", System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void NonGenericTypeInfoOverloadsSerializeAndDeserializePayload()
    {
        YamlTypeInfo typeInfo = new StringTypeInfo(new YamlSerializerOptions());

        var yaml = YamlSerializer.Serialize((object)"hello", typeInfo);
        var value = YamlSerializer.Deserialize(yaml, typeInfo);

        Assert.Equal("value: hello\n", yaml);
        Assert.Equal("hello", value);
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
    public void SerializeStringProperty_UsesPlainStyleWhenSafe()
    {
        var yaml = YamlSerializer.Serialize(new StringPayload { Value = "sample string" });

        Assert.Equal("Value: sample string\n", yaml);
    }

    [Fact]
    public void SerializeStringProperty_QuotesProblematicCharacters()
    {
        var yaml = YamlSerializer.Serialize(new StringPayload { Value = "sample: string" });

        Assert.Equal("Value: \"sample: string\"\n", yaml);
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
    public void GetTypeInfo_ReturnsGenericMetadataFromResolver()
    {
        var typeInfo = new StringTypeInfo(new YamlSerializerOptions());
        var options = new YamlSerializerOptions
        {
            TypeInfoResolver = new StringTypeInfoResolver(typeInfo),
        };

        Assert.Same(typeInfo, options.GetTypeInfo<string>());
        Assert.True(options.TryGetTypeInfo<string>(out var resolved));
        Assert.Same(typeInfo, resolved);
    }

    [Fact]
    public void GetTypeInfo_AdaptsNonGenericMetadata()
    {
        var options = new YamlSerializerOptions();

        var typeInfo = options.GetTypeInfo<Person>();

        Assert.Equal(typeof(Person), typeInfo.Type);
        Assert.Same(options, typeInfo.Options);

        var yaml = YamlSerializer.Serialize(new Person { FirstName = "Ada", Age = 37 }, typeInfo);
        var value = YamlSerializer.Deserialize(yaml, typeInfo);

        Assert.NotNull(value);
        Assert.Equal("Ada", value.FirstName);
        Assert.Equal(37, value.Age);
    }

    [Fact]
    public void GetTypeInfo_AdaptsNonGenericMetadataForValueTypes()
    {
        var typeInfo = YamlSerializerOptions.Default.GetTypeInfo<int>();

        Assert.Equal(typeof(int), typeInfo.Type);
        Assert.Equal(42, YamlSerializer.Deserialize("42", typeInfo));
        Assert.Equal("42\n", YamlSerializer.Serialize(42, typeInfo));
    }

    [Fact]
    public void TryGetTypeInfo_ReturnsFalseWhenContextHasNoMetadata()
    {
        var context = new EmptyContext();
        var options = context.CreateOptions(o => o with { SourceName = "sample.yaml" });

        Assert.False(options.TryGetTypeInfo<Person>(out var typeInfo));
        Assert.Null(typeInfo);
    }

    [Fact]
    public void GetTypeInfo_ThrowsWhenContextHasNoMetadata()
    {
        var context = new EmptyContext();
        var options = context.CreateOptions(o => o with { SourceName = "sample.yaml" });

        _ = Assert.Throws<InvalidOperationException>(() => options.GetTypeInfo<Person>());
    }

    private sealed class EmptyContext : YamlSerializerContext
    {
        public override YamlTypeInfo? GetTypeInfo(Type type, YamlSerializerOptions options) => null;
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

    [Fact]
    public void EmptyPayload_NonNullableValueType_ReturnsDefault()
    {
        Assert.Equal(0, YamlSerializer.Deserialize<int>(""));
        Assert.Equal(0, YamlSerializer.Deserialize<int>(new StringReader("# comment only")));
        Assert.Equal(0, YamlSerializer.Deserialize<int>(new MemoryStream()));
        Assert.Null(YamlSerializer.Deserialize<int?>(""));
        Assert.True(YamlSerializer.TryDeserialize<int>("", out var value));
        Assert.Equal(0, value);
    }

    [Fact]
    public void ReflectionContext_YamlNodeMember_KeepsTagsStylesAndKeyAnchors()
    {
        var node = YamlSerializer.Deserialize<YamlNode>("tagged: !!str 1\nquoted: '2'\nlocal: !foo x\n&k key: v\nalias: *k\n");

        var yaml = YamlSerializer.Serialize(node);

        Assert.Equal("tagged: !!str 1\nquoted: '2'\nlocal: !foo x\n&k key: v\nalias: key\n", yaml);
        var values = YamlSerializer.Deserialize<Dictionary<string, object>>(yaml)!;
        Assert.Equal("1", values["tagged"]);
        Assert.Equal("2", values["quoted"]);
    }
}
