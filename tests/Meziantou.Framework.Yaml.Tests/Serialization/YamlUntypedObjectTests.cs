namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlUntypedObjectTests
{
    [Fact]
    public void DeserializeObject_InfersScalarTypes()
    {
        Assert.Equal(true, YamlSerializer.Deserialize<object>("true"));
        Assert.Equal(42L, YamlSerializer.Deserialize<object>("42"));
        Assert.Equal(1.5d, (double)YamlSerializer.Deserialize<object>("1.5")!, 1e-12);
        Assert.Equal("text", YamlSerializer.Deserialize<object>("text"));
        Assert.Null(YamlSerializer.Deserialize<object>("null"));
        Assert.Null(YamlSerializer.Deserialize<object>("~"));
    }

    [Fact]
    public void DeserializeObject_InfersSequenceAndMapping()
    {
        var list = (List<object?>)YamlSerializer.Deserialize<object>("- 1\n- true\n- text\n- null\n")!;
        Assert.HasCount(4, list);
        Assert.Equal(1L, list[0]);
        Assert.Equal(true, list[1]);
        Assert.Equal("text", list[2]);
        Assert.Null(list[3]);

        var dict = (Dictionary<object, object?>)YamlSerializer.Deserialize<object>("a: 1\nb: true\nc: text\n")!;
        Assert.HasCount(3, dict);
        Assert.Equal(1L, dict["a"]);
        Assert.Equal(true, dict["b"]);
        Assert.Equal("text", dict["c"]);
    }

    [Fact]
    public void UnsafeTagActivation_IsOptIn()
    {
        var yaml = "!System.Int32 42\n";

        var defaultValue = YamlSerializer.Deserialize<object>(yaml);
        Assert.Equal(42L, defaultValue);

        var unsafeValue = YamlSerializer.Deserialize<object>(
            yaml,
            new YamlSerializerOptions { UnsafeAllowDeserializeFromTagTypeName = true });
        Assert.Equal(42, unsafeValue);
        Assert.IsType<int>(unsafeValue);
    }

    [Fact]
    public void UnsafeTagActivation_HandlesMscorlibTypeNames()
    {
        var yaml = "!System.Int32%2Cmscorlib 42\n";
        var value = YamlSerializer.Deserialize<object>(
            yaml,
            new YamlSerializerOptions { UnsafeAllowDeserializeFromTagTypeName = true });

        Assert.Equal(42, value);
        Assert.IsType<int>(value);
    }

    [Fact]
    public void UnsafeTagActivation_IgnoresUnknownTypes()
    {
        var yaml = "!NoSuch.Type 42\n";
        var value = YamlSerializer.Deserialize<object>(
            yaml,
            new YamlSerializerOptions { UnsafeAllowDeserializeFromTagTypeName = true });

        Assert.Equal(42L, value);
    }

    [Fact]
    public void DeserializeObject_AliasWithoutPreserve_ThrowsYamlException()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<object>("*id001\n"));
        Assert.Contains("ReferenceHandling", ex.Message);
        Assert.Contains("Preserve", ex.Message);
    }

    [Fact]
    public void SerializePlainObject_WritesAnEmptyMapping()
    {
        // Resolving the runtime type of a plain System.Object leads back to the untyped converter, so the converter
        // has to stop there instead of recursing until the stack overflows.
        Assert.Equal("{}\n", YamlSerializer.Serialize<object>(new object()));
        Assert.Equal("{}\n", YamlSerializer.Serialize(new object(), typeof(object)));
    }

    [Fact]
    public void SerializePlainObjectInsideACollection_WritesAnEmptyMapping()
    {
        Assert.Equal("- {}\n", YamlSerializer.Serialize<List<object>>([new object()]));
        Assert.Equal("a: {}\n", YamlSerializer.Serialize<Dictionary<string, object>>(new Dictionary<string, object>(StringComparer.Ordinal) { ["a"] = new object() }));
    }

    [Fact]
    public void SerializePlainObject_RoundTripsToAnEmptyMapping()
    {
        var yaml = YamlSerializer.Serialize<object>(new object());

        var roundTrip = YamlSerializer.Deserialize<object>(yaml);

        Assert.Empty(Assert.IsType<Dictionary<object, object?>>(roundTrip));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeserializeObject_ResolvesMappingKeysLikeValues(bool useSchema)
    {
        var options = new YamlSerializerOptions { UseSchema = useSchema };

        var dict = Assert.IsType<Dictionary<object, object?>>(YamlSerializer.Deserialize<object>("1: int\n'1': string\ntrue: bool\n1.5: float\n", options));

        Assert.HasCount(4, dict);
        Assert.Equal("int", dict[1L]);
        Assert.Equal("string", dict["1"]);
        Assert.Equal("bool", dict[true]);
        Assert.Equal("float", dict[1.5]);
    }

    [Theory]
    [InlineData("1: a\n0x1: b\n")]
    [InlineData("1: a\n!!int 1: b\n")]
    [InlineData("1: a\n+1: b\n")]
    [InlineData("a: 1\n!!str a: 2\n")]
    public void DeserializeObject_KeysResolvingToTheSameValueAreDuplicates(string yaml)
    {
        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<object>(yaml));
        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<object>(yaml, new YamlSerializerOptions { UseSchema = true }));
    }

    [Theory]
    [InlineData("~: a\n")]
    [InlineData("null: a\n")]
    [InlineData("? \n: a\n")]
    public void DeserializeObject_NullKeyThrows(string yaml)
    {
        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<object>(yaml));
    }

    [Fact]
    public void DeserializeObject_ReadsBackDictionaryWithKeysOfDifferentTypes()
    {
        var value = new Dictionary<object, int> { [1] = 1, ["1"] = 2, [true] = 3, ["true"] = 4 };

        var yaml = YamlSerializer.Serialize(value);
        var roundTrip = Assert.IsType<Dictionary<object, object?>>(YamlSerializer.Deserialize<object>(yaml));

        Assert.HasCount(4, roundTrip);
        Assert.Equal(1L, roundTrip[1L]);
        Assert.Equal(2L, roundTrip["1"]);
        Assert.Equal(3L, roundTrip[true]);
        Assert.Equal(4L, roundTrip["true"]);
    }
}
