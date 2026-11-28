#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlDuplicateKeyHandlingTests
{
    private sealed class DuplicateModel
    {
        public int Age { get; set; }
    }

    [Fact]
    public void Dictionary_DuplicateKey_Error_Throws()
    {
        var yaml = "a: 1\na: 2\n";
        var options = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.Error };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Dictionary<string, int>>(yaml, options));
        Assert.Contains("Duplicate", exception.Message);
    }

    [Fact]
    public void Dictionary_DuplicateKey_FirstWins()
    {
        var yaml = "a: 1\na: 2\n";
        var options = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.FirstWins };

        var result = YamlSerializer.Deserialize<Dictionary<string, int>>(yaml, options);

        Assert.NotNull(result);
        Assert.Equal(1, result["a"]);
    }

    [Fact]
    public void Dictionary_DuplicateKey_LastWins()
    {
        var yaml = "a: 1\na: 2\n";
        var options = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.LastWins };

        var result = YamlSerializer.Deserialize<Dictionary<string, int>>(yaml, options);

        Assert.NotNull(result);
        Assert.Equal(2, result["a"]);
    }

    [Fact]
    public void Dictionary_DuplicateKey_CaseInsensitive_UsesComparer()
    {
        var yaml = "A: 1\na: 2\n";
        var options = new YamlSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DuplicateKeyHandling = YamlDuplicateKeyHandling.LastWins,
        };

        var result = YamlSerializer.Deserialize<Dictionary<string, int>>(yaml, options);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(2, result["a"]);
    }

    [Fact]
    public void Object_DuplicateKey_FirstWins()
    {
        var yaml = "Age: 1\nAge: 2\n";
        var options = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.FirstWins };

        var result = YamlSerializer.Deserialize<DuplicateModel>(yaml, options);

        Assert.NotNull(result);
        Assert.Equal(1, result.Age);
    }

    [Fact]
    public void Object_DuplicateKey_LastWins()
    {
        var yaml = "Age: 1\nAge: 2\n";
        var options = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.LastWins };

        var result = YamlSerializer.Deserialize<DuplicateModel>(yaml, options);

        Assert.NotNull(result);
        Assert.Equal(2, result.Age);
    }

    [Fact]
    public void Object_DuplicateKey_Error_Throws()
    {
        var yaml = "Age: 1\nAge: 2\n";
        var options = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.Error };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<DuplicateModel>(yaml, options));
        Assert.Contains("Duplicate", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Object_DuplicateKey_DefaultOptions_Throws(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyObjectModel>("Age: 1\nName: a\nAge: 2\n", new YamlSerializerOptions(), useSourceGeneration));

        Assert.Equal("(Lin: 2, Col: 0, Chr: 15) - (Lin: 2, Col: 3, Chr: 18): Duplicate mapping key 'Age'.", exception.Message);
    }

    [Theory]
    [InlineData(false, YamlDuplicateKeyHandling.FirstWins, 1)]
    [InlineData(true, YamlDuplicateKeyHandling.FirstWins, 1)]
    [InlineData(false, YamlDuplicateKeyHandling.LastWins, 2)]
    [InlineData(true, YamlDuplicateKeyHandling.LastWins, 2)]
    public void Object_DuplicateKey_UsesDuplicateKeyHandling(bool useSourceGeneration, YamlDuplicateKeyHandling handling, int expectedAge)
    {
        var options = new YamlSerializerOptions { DuplicateKeyHandling = handling };

        var result = Deserialize<DuplicateKeyObjectModel>("Age: 1\nName: a\nAge: 2\n", options, useSourceGeneration)!;
        Assert.Equal(expectedAge, result.Age);
        Assert.Equal("a", result.Name);

        var constructed = Deserialize<DuplicateKeyConstructorModel>("Age: 1\nName: a\nAge: 2\n", options, useSourceGeneration)!;
        Assert.Equal(expectedAge, constructed.Age);
        Assert.Equal("a", constructed.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Object_DuplicateKey_IsDetectedForEveryKind(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions();

        // Keys matching no member, and keys matching a member only when names are compared case-insensitively.
        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyObjectModel>("extra: 1\nextra: 2\n", options, useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyConstructorModel>("extra: 1\nextra: 2\nName: a\nAge: 1\n", options, useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyObjectModel>("age: 1\nAge: 2\n", options with { PropertyNameCaseInsensitive = true }, useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyConstructorModel>("name: a\nName: b\nAge: 1\n", options with { PropertyNameCaseInsensitive = true }, useSourceGeneration));

        var result = Deserialize<DuplicateKeyObjectModel>("age: 1\nAge: 2\n", options, useSourceGeneration)!;
        Assert.Equal(2, result.Age);
    }

    // The value of a duplicate key that keeps its first value is skipped without being read, and the error of a
    // duplicate key is reported at the key, before its value is read
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DictionaryMember_DuplicateKey_IsHandledBeforeTheValueIsRead(bool useSourceGeneration)
    {
        var firstWins = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.FirstWins };
        var lastWins = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.LastWins };

        Assert.Equal(1, Deserialize<DuplicateKeyDictionaryModel>("Values: {a: 1, a: not a number}\n", firstWins, useSourceGeneration)!.Values!["a"]);
        Assert.Equal(1, Deserialize<DuplicateKeyDictionaryModel>("Values: {a: 1, a: *unknown}\n", firstWins, useSourceGeneration)!.Values!["a"]);
        Assert.Equal(2, Deserialize<DuplicateKeyDictionaryModel>("Values: {a: 1, a: 2}\n", lastWins, useSourceGeneration)!.Values!["a"]);
        Assert.Equal(1, Deserialize<Dictionary<string, int>>("{a: 1, a: not a number}\n", firstWins, useSourceGeneration)!["a"]);

        var exception = Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyDictionaryModel>("Values: {a: 1, a: not a number}\n", new YamlSerializerOptions(), useSourceGeneration));
        Assert.Contains("Duplicate", exception.Message, StringComparison.Ordinal);
    }

    // The keys of the first 64 members are tracked by their index, the others by their name
    [Theory]
    [InlineData(false, "P0")]
    [InlineData(false, "P63")]
    [InlineData(false, "P64")]
    [InlineData(false, "P69")]
    [InlineData(true, "P0")]
    [InlineData(true, "P63")]
    [InlineData(true, "P64")]
    [InlineData(true, "P69")]
    public void Object_DuplicateKey_IsDetectedForEveryMember(bool useSourceGeneration, string member)
    {
        var yaml = $"P1: 1\n{member}: 2\nP65: 3\n{member}: 4\n";

        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyLargeModel>(yaml, new YamlSerializerOptions(), useSourceGeneration));

        var firstWins = Deserialize<DuplicateKeyLargeModel>(yaml, new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.FirstWins }, useSourceGeneration)!;
        var lastWins = Deserialize<DuplicateKeyLargeModel>(yaml, new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.LastWins }, useSourceGeneration)!;
        Assert.Equal(2, (int)typeof(DuplicateKeyLargeModel).GetProperty(member)!.GetValue(firstWins)!);
        Assert.Equal(4, (int)typeof(DuplicateKeyLargeModel).GetProperty(member)!.GetValue(lastWins)!);
        Assert.Equal(3, firstWins.P65);
    }

    // A merge key does not replace the members that the mapping sets, before or after it, whatever their index
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Object_MergeKey_KeepsTheExplicitMembers(bool useSourceGeneration)
    {
        var result = Deserialize<DuplicateKeyLargeModel>("P0: 1\nP66: 2\nextra: 0\n<<: {P0: 10, P1: 11, P66: 12, P67: 13}\nP67: 3\n", new YamlSerializerOptions(), useSourceGeneration)!;

        Assert.Equal(1, result.P0);
        Assert.Equal(11, result.P1);
        Assert.Equal(2, result.P66);
        Assert.Equal(3, result.P67);
    }

    // The keys of a dictionary are the keys of the mapping only until a merge key adds others: an explicit key is still
    // a duplicate of an earlier explicit key after a merge, and replaces a merged key without being a duplicate
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dictionary_DuplicateKeys_AroundMergeKeys(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions();
        AssertDuplicateKeysAroundMergeKeys<Dictionary<string, int>>(options, useSourceGeneration);
        AssertDuplicateKeysAroundMergeKeys<OrderedDictionary<string, int>>(options, useSourceGeneration);
        AssertDuplicateKeysAroundMergeKeys<SortedDictionary<string, int>>(options, useSourceGeneration);

        var member = Deserialize<DuplicateKeyDictionaryKindsModel>("Values: {a: 1, <<: {a: 10, b: 20}, b: 2}\nOrdered: {a: 1, <<: {a: 10, b: 20}, b: 2}\nSorted: {a: 1, <<: {a: 10, b: 20}, b: 2}\n", options, useSourceGeneration)!;
        Assert.Equal([new("a", 1), new("b", 2)], member.Values!.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal([new("a", 1), new("b", 2)], member.Ordered!);
        Assert.Equal([new("a", 1), new("b", 2)], member.Sorted!);
        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyDictionaryKindsModel>("Values: {a: 1, <<: {b: 20}, a: 2}\n", options, useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyDictionaryKindsModel>("Ordered: {a: 1, <<: {b: 20}, a: 2}\n", options, useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyDictionaryKindsModel>("Sorted: {a: 1, <<: {b: 20}, a: 2}\n", options, useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyDictionaryKindsModel>("Values: {a: 1, A: 2}\n", options with { PropertyNameCaseInsensitive = true }, useSourceGeneration));
        Assert.HasCount(2, Deserialize<DuplicateKeyDictionaryKindsModel>("Values: {a: 1, A: 2}\n", options, useSourceGeneration)!.Values!);
    }

    // A key of the dictionary that a member already holds is replaced, it is not a duplicate key of the mapping
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dictionary_PopulatedDictionary_KeysItAlreadyHoldsAreNotDuplicates(bool useSourceGeneration)
    {
        var result = Deserialize<DuplicateKeyPopulatedDictionaryModel>("Values: {a: 1, b: 2}\n", new YamlSerializerOptions(), useSourceGeneration)!;

        Assert.Equal([new("a", 1), new("b", 2), new("z", 26)], result.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyPopulatedDictionaryModel>("Values: {a: 1, a: 2}\n", new YamlSerializerOptions(), useSourceGeneration));
    }

    // An exception of the setter of a member is reported at the key of the member; an invalid contract is not a YAML error
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Object_MemberExceptions_AreReportedAtTheirKey(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyThrowingSetterModel>("Name: a\nValue: 1\nOther: 2\n", new YamlSerializerOptions(), useSourceGeneration));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(1, exception.Start.Line);
        Assert.Equal(0, exception.Start.Column);

        var mergedException = Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyThrowingSetterModel>("Name: a\n<<: {Other: 2, Value: 1}\n", new YamlSerializerOptions(), useSourceGeneration));
        Assert.IsType<InvalidOperationException>(mergedException.InnerException);

        Assert.Equal(2, Deserialize<DuplicateKeyThrowingSetterModel>("Name: a\nOther: 2\n", new YamlSerializerOptions(), useSourceGeneration)!.Other);
        Assert.Throws<InvalidOperationException>(() => Deserialize<DuplicateKeyInvalidPopulateModel>("Value: 1\n", new YamlSerializerOptions(), useSourceGeneration));
    }

    // A type created by its constructor matches its keys and reports the exceptions of their values in the same way
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorObject_MemberExceptions_AreReportedAtTheirKey(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions();
        var result = Deserialize<DuplicateKeyConstructorConverterModel>("Name: a\nOther: 2\nIgnored: 3\n", options, useSourceGeneration)!;
        Assert.Equal("a", result.Name);
        Assert.Equal(2, result.Other);

        var parameterException = Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyConstructorConverterModel>("Other: 2\nName: throw\n", options, useSourceGeneration));
        Assert.IsType<InvalidOperationException>(parameterException.InnerException);
        Assert.Equal(1, parameterException.Start.Line);

        var memberException = Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyConstructorConverterModel>("Name: a\nCount: -1\n", options, useSourceGeneration));
        Assert.IsType<InvalidOperationException>(memberException.InnerException);
        Assert.Equal(1, memberException.Start.Line);

        var mergedException = Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyConstructorConverterModel>("Name: a\n<<: {Count: -1}\n", options, useSourceGeneration));
        Assert.IsType<InvalidOperationException>(mergedException.InnerException);

        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyConstructorConverterModel>("Name: a\nName: b\n", options, useSourceGeneration));
        Assert.Equal("b", Deserialize<DuplicateKeyConstructorConverterModel>("name: b\n", options with { PropertyNameCaseInsensitive = true }, useSourceGeneration)!.Name);
    }

    private static void AssertDuplicateKeysAroundMergeKeys<T>(YamlSerializerOptions options, bool useSourceGeneration)
        where T : IDictionary<string, int>
    {
        var merged = Deserialize<T>("a: 1\n<<: {a: 10, b: 20, c: 30}\nb: 2\n", options, useSourceGeneration)!;
        Assert.Equal([new("a", 1), new("b", 2), new("c", 30)], merged.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Throws<YamlException>(() => Deserialize<T>("a: 1\n<<: {b: 20}\na: 2\n", options, useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<T>("<<: {a: 10}\na: 1\na: 2\n", options, useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<T>("a: 1\nA: 2\n", options with { PropertyNameCaseInsensitive = true }, useSourceGeneration));
        Assert.HasCount(2, Deserialize<T>("a: 1\nA: 2\n", options, useSourceGeneration)!);
        Assert.Equal(1, Deserialize<T>("a: 1\na: 2\n", options with { DuplicateKeyHandling = YamlDuplicateKeyHandling.FirstWins }, useSourceGeneration)!["a"]);
        Assert.Equal(2, Deserialize<T>("a: 1\na: 2\n", options with { DuplicateKeyHandling = YamlDuplicateKeyHandling.LastWins }, useSourceGeneration)!["a"]);
    }

    private static T? Deserialize<T>(string yaml, YamlSerializerOptions options, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, new DuplicateKeyHandlingContext(options))
            : YamlSerializer.Deserialize<T>(yaml, options);
}

internal sealed class DuplicateKeyObjectModel
{
    public int Age { get; set; }

    public string? Name { get; set; }
}

internal sealed class DuplicateKeyConstructorModel
{
    public DuplicateKeyConstructorModel(string name, int age)
    {
        Name = name;
        Age = age;
    }

    public string Name { get; }

    public int Age { get; }
}

internal sealed class DuplicateKeyDictionaryModel
{
    public Dictionary<string, int>? Values { get; set; }
}

internal sealed class DuplicateKeyDictionaryKindsModel
{
    public Dictionary<string, int>? Values { get; set; }

    public OrderedDictionary<string, int>? Ordered { get; set; }

    public SortedDictionary<string, int>? Sorted { get; set; }
}

internal sealed class DuplicateKeyPopulatedDictionaryModel
{
    [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
    public Dictionary<string, int> Values { get; set; } = new(StringComparer.Ordinal) { ["a"] = 0, ["z"] = 26 };
}

internal sealed class DuplicateKeyThrowingSetterModel
{
    public string? Name { get; set; }

    private int _value;

    public int Value
    {
        get => _value;
        set
        {
            _value = value;
            throw new InvalidOperationException("The value cannot be set.");
        }
    }

    public int Other { get; set; }
}

internal sealed class DuplicateKeyInvalidPopulateModel
{
    [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
    public int Value { get; }
}

internal sealed class DuplicateKeyConstructorConverterModel(string name)
{
    [YamlConverter(typeof(DuplicateKeyThrowingStringConverter))]
    public string Name { get; } = name;

    public int Other { get; set; }

    [YamlConverter(typeof(DuplicateKeyThrowingIntConverter))]
    public int Count { get; set; }

    [YamlIgnore]
    public int Ignored { get; set; }
}

// Throws an exception that is not a YamlException for "throw"
internal sealed class DuplicateKeyThrowingStringConverter : YamlConverter<string>
{
    public override string? Read(YamlReader reader)
    {
        var value = reader.GetScalarValue();
        if (value is "throw")
            throw new InvalidOperationException("The value cannot be read.");

        reader.Read();
        return value;
    }

    public override void Write(YamlWriter writer, string value) => writer.WriteScalar(value);
}

// Throws an exception that is not a YamlException for a negative value
internal sealed class DuplicateKeyThrowingIntConverter : YamlConverter<int>
{
    public override int Read(YamlReader reader)
    {
        var value = int.Parse(reader.GetScalarValue(), CultureInfo.InvariantCulture);
        if (value < 0)
            throw new InvalidOperationException("The value cannot be negative.");

        reader.Read();
        return value;
    }

    public override void Write(YamlWriter writer, int value) => writer.WriteScalar(value.ToString(CultureInfo.InvariantCulture));
}

// More members than the bits of the index of a key
internal sealed class DuplicateKeyLargeModel
{
    public int P0 { get; set; }

    public int P1 { get; set; }

    public int P2 { get; set; }

    public int P3 { get; set; }

    public int P4 { get; set; }

    public int P5 { get; set; }

    public int P6 { get; set; }

    public int P7 { get; set; }

    public int P8 { get; set; }

    public int P9 { get; set; }

    public int P10 { get; set; }

    public int P11 { get; set; }

    public int P12 { get; set; }

    public int P13 { get; set; }

    public int P14 { get; set; }

    public int P15 { get; set; }

    public int P16 { get; set; }

    public int P17 { get; set; }

    public int P18 { get; set; }

    public int P19 { get; set; }

    public int P20 { get; set; }

    public int P21 { get; set; }

    public int P22 { get; set; }

    public int P23 { get; set; }

    public int P24 { get; set; }

    public int P25 { get; set; }

    public int P26 { get; set; }

    public int P27 { get; set; }

    public int P28 { get; set; }

    public int P29 { get; set; }

    public int P30 { get; set; }

    public int P31 { get; set; }

    public int P32 { get; set; }

    public int P33 { get; set; }

    public int P34 { get; set; }

    public int P35 { get; set; }

    public int P36 { get; set; }

    public int P37 { get; set; }

    public int P38 { get; set; }

    public int P39 { get; set; }

    public int P40 { get; set; }

    public int P41 { get; set; }

    public int P42 { get; set; }

    public int P43 { get; set; }

    public int P44 { get; set; }

    public int P45 { get; set; }

    public int P46 { get; set; }

    public int P47 { get; set; }

    public int P48 { get; set; }

    public int P49 { get; set; }

    public int P50 { get; set; }

    public int P51 { get; set; }

    public int P52 { get; set; }

    public int P53 { get; set; }

    public int P54 { get; set; }

    public int P55 { get; set; }

    public int P56 { get; set; }

    public int P57 { get; set; }

    public int P58 { get; set; }

    public int P59 { get; set; }

    public int P60 { get; set; }

    public int P61 { get; set; }

    public int P62 { get; set; }

    public int P63 { get; set; }

    public int P64 { get; set; }

    public int P65 { get; set; }

    public int P66 { get; set; }

    public int P67 { get; set; }

    public int P68 { get; set; }

    public int P69 { get; set; }
}

[YamlSerializable(typeof(DuplicateKeyObjectModel))]
[YamlSerializable(typeof(DuplicateKeyConstructorModel))]
[YamlSerializable(typeof(DuplicateKeyDictionaryModel))]
[YamlSerializable(typeof(DuplicateKeyLargeModel))]
[YamlSerializable(typeof(Dictionary<string, int>))]
[YamlSerializable(typeof(OrderedDictionary<string, int>))]
[YamlSerializable(typeof(SortedDictionary<string, int>))]
[YamlSerializable(typeof(DuplicateKeyDictionaryKindsModel))]
[YamlSerializable(typeof(DuplicateKeyPopulatedDictionaryModel))]
[YamlSerializable(typeof(DuplicateKeyThrowingSetterModel))]
[YamlSerializable(typeof(DuplicateKeyInvalidPopulateModel))]
[YamlSerializable(typeof(DuplicateKeyConstructorConverterModel))]
internal sealed partial class DuplicateKeyHandlingContext : YamlSerializerContext
{
    public DuplicateKeyHandlingContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}
