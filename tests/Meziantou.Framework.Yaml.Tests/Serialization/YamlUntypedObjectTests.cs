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

    // The bytes of most plain scalars are resolved without being decoded: they must resolve like their text
    [Theory]
    [InlineData("0", 0L)]
    [InlineData("-5", -5L)]
    [InlineData("+7", 7L)]
    [InlineData("-0", 0L)]
    [InlineData("007", 7L)]
    [InlineData("123456789012345678", 123456789012345678L)]
    [InlineData("1234567890123456789", 1234567890123456789L)]
    [InlineData("-9223372036854775808", long.MinValue)]
    [InlineData("18446744073709551615", ulong.MaxValue)]
    [InlineData("1_000", 1000L)]
    [InlineData("_12", 12L)]
    [InlineData("_-5", -5L)]
    [InlineData("+_1", 1L)]
    [InlineData("_1_000", 1000L)]
    [InlineData("_x", "_x")]
    [InlineData("0x1F", 31L)]
    [InlineData(".5", 0.5)]
    [InlineData("1.5", 1.5)]
    [InlineData("-0.0", -0.0)]
    [InlineData("1.", 1.0)]
    [InlineData("1e3", 1000.0)]
    [InlineData("6.02E23", 6.02E23)]
    [InlineData("1.5_0", 1.5)]
    [InlineData("1.5.5", "1.5.5")]
    [InlineData("1e", "1e")]
    [InlineData("0b1e1", "0b1e1")]
    [InlineData("0x1e", 30L)]
    [InlineData("1.5é", "1.5é")]
    [InlineData("-1.5e3", -1500.0)]
    [InlineData("-.inf", double.NegativeInfinity)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("FALSE", false)]
    [InlineData("tru", "tru")]
    [InlineData("falsey", "falsey")]
    [InlineData("nullx", "nullx")]
    [InlineData("+foo", "+foo")]
    [InlineData("+", "+")]
    [InlineData("~x", "~x")]
    [InlineData("Infinity", "Infinity")]
    [InlineData("NaN", "NaN")]
    [InlineData("12 monkeys", "12 monkeys")]
    [InlineData("'42'", "42")]
    [InlineData("\"true\"", "true")]
    [InlineData("café", "café")]
    public void DeserializeObject_ResolvesPlainScalarsFromTheirBytes(string yaml, object expected)
    {
        Assert.Equal(expected, YamlSerializer.Deserialize<object>(yaml));
        Assert.Equal(expected, YamlSerializer.Deserialize<object>(System.Text.Encoding.UTF8.GetBytes(yaml)));
        var dict = Assert.IsType<Dictionary<object, object?>>(YamlSerializer.Deserialize<object>("key: " + yaml + "\n"));
        Assert.Equal(expected, dict["key"]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("Null")]
    [InlineData("NULL")]
    [InlineData("~")]
    [InlineData("")]
    public void DeserializeObject_ResolvesNullFromTheBytes(string value)
    {
        var dict = Assert.IsType<Dictionary<object, object?>>(YamlSerializer.Deserialize<object>("key: " + value + "\n"));
        Assert.Contains("key", dict);
        Assert.Null(dict["key"]);
    }

    // The keys of the mappings, which are read from a cache of the keys by their bytes, resolve like their text
    [Fact]
    public void DeserializeObject_ReadsTheKeysOfTheMappings()
    {
        var keys = Enumerable.Range(0, 200).Select(i => "key" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Concat(["clé", "'quoted key'", "\"double quoted\"", new string('k', 33), "1", "true", "a b"])
            .ToList();
        var yaml = new System.Text.StringBuilder();
        for (var item = 0; item < 3; item++)
        {
            yaml.Append("- name: item\n");
            for (var i = 0; i < keys.Count; i++)
            {
                yaml.Append("  ").Append(keys[i]).Append(": ").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        var list = Assert.IsType<List<object?>>(YamlSerializer.Deserialize<object>(yaml.ToString()));
        var expectedKeys = keys.Select(key => key switch
        {
            "1" => (object)1L,
            "true" => true,
            ['\'' or '"', .. var unquoted, '\'' or '"'] => unquoted,
            _ => key,
        }).ToList();
        foreach (var item in list)
        {
            var dict = Assert.IsType<Dictionary<object, object?>>(item);
            Assert.HasCount(keys.Count + 1, dict);
            Assert.Equal("item", dict["name"]);
            for (var i = 0; i < expectedKeys.Count; i++)
            {
                Assert.Equal((long)i, dict[expectedKeys[i]]);
            }
        }

        // The keys of a few mappings are kept: they share their strings
        var records = Assert.IsType<List<object?>>(YamlSerializer.Deserialize<object>("- name: a\n  id: 1\n- name: b\n  id: 2\n"));
        var first = Assert.IsType<Dictionary<object, object?>>(records[0]).Keys.OfType<string>().First();
        var last = Assert.IsType<Dictionary<object, object?>>(records[1]).Keys.OfType<string>().First();
        Assert.Equal("name", first);
        Assert.Same(first, last);
    }

    // The keys of a mapping are checked against the keys it declares, not against the keys of its merge keys
    [Theory]
    [InlineData(YamlDuplicateKeyHandling.Error, "a: 1\n<<: {a: 2, b: 3}\nb: 4\n", "a=1,b=4")]
    [InlineData(YamlDuplicateKeyHandling.Error, "<<: {a: 2}\na: 1\n", "a=1")]
    [InlineData(YamlDuplicateKeyHandling.FirstWins, "a: 1\na: 2\n", "a=1")]
    [InlineData(YamlDuplicateKeyHandling.FirstWins, "a: 1\na: 2\n<<: {a: 9, c: 3}\n", "a=1,c=3")]
    [InlineData(YamlDuplicateKeyHandling.FirstWins, "<<: {a: 9}\na: 1\na: 2\n", "a=1")]
    [InlineData(YamlDuplicateKeyHandling.FirstWins, "a: 1\n<<: {b: 9}\nb: 2\nb: 3\na: 4\n", "a=1,b=2")]
    [InlineData(YamlDuplicateKeyHandling.LastWins, "a: 1\na: 2\n<<: {a: 9, c: 3}\na: 5\n", "a=5,c=3")]
    [InlineData(YamlDuplicateKeyHandling.LastWins, "<<: [{a: 1}, {a: 2, b: 3}]\n", "a=1,b=3")]
    public void DeserializeObject_DuplicateKeysAndMergeKeys(YamlDuplicateKeyHandling handling, string yaml, string expected)
    {
        var dict = Assert.IsType<Dictionary<object, object?>>(YamlSerializer.Deserialize<object>(yaml, new YamlSerializerOptions { DuplicateKeyHandling = handling }));

        Assert.Equal(expected, string.Join(',', dict.OrderBy(pair => (string)pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value)));
    }

    [Theory]
    [InlineData("a: 1\na: 2\n", 1)]
    [InlineData("a: 1\n<<: {b: 2}\na: 3\n", 2)]
    [InlineData("<<: {a: 2}\nb: 1\nb: 3\n", 2)]
    public void DeserializeObject_DuplicateKeyThrowsAtItsValue(string yaml, int line)
    {
        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<object>(yaml));

        Assert.Contains("Duplicate mapping key", exception.Message, StringComparison.Ordinal);
        Assert.Equal(line, exception.Start.Line);
        Assert.Equal(3, exception.Start.Column);
    }

    [Theory]
    [InlineData("42", false)]
    [InlineData("!!int 42", false)]
    [InlineData("42", true)]
    [InlineData("!!int 42", true)]
    [InlineData("0x2A", true)]
    public void DeserializeObject_IntegersAreLong(string yaml, bool useSchema)
    {
        var value = YamlSerializer.Deserialize<object>(yaml, new YamlSerializerOptions { UseSchema = useSchema });

        Assert.Equal(42L, Assert.IsType<long>(value));
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
