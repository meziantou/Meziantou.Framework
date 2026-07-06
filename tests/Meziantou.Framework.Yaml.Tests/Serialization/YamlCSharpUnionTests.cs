#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;

#if NET11_0_OR_GREATER
public sealed class YamlCSharpUnionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SerializeScalarUnionWritesUnderlyingCase(bool useSourceGeneration)
    {
        Assert.Equal("42\n", Serialize(new ScalarUnion(42), useSourceGeneration));
        Assert.Equal("hello\n", Serialize(new ScalarUnion("hello"), useSourceGeneration));
        Assert.Equal("true\n", Serialize(new ScalarUnion(true), useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeserializeScalarUnionUsesScalarKind(bool useSourceGeneration)
    {
        Assert.Equal(42, Deserialize<ScalarUnion>("42\n", useSourceGeneration)!.Value);
        Assert.Equal("42", Deserialize<ScalarUnion>("\"42\"\n", useSourceGeneration)!.Value);
        Assert.Equal(true, Deserialize<ScalarUnion>("true\n", useSourceGeneration)!.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullDeserializesToFirstNullableCase(bool useSourceGeneration)
    {
        var value = Deserialize<NullableUnion>("null\n", useSourceGeneration);

        Assert.NotNull(value);
        Assert.Null(value.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableValueTypeCaseSupportsNumberAndNull(bool useSourceGeneration)
    {
        var number = Deserialize<NullableNumberUnion>("42\n", useSourceGeneration);
        var nullValue = Deserialize<NullableNumberUnion>("null\n", useSourceGeneration);

        Assert.NotNull(number);
        Assert.Equal(42, number.Value);
        Assert.NotNull(nullValue);
        Assert.Null(nullValue.Value);
        Assert.Equal("42\n", Serialize(new NullableNumberUnion((int?)42), useSourceGeneration));
        Assert.Equal("null\n", Serialize(new NullableNumberUnion((int?)null), useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullWithoutNullableCaseThrows(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<NonNullableUnion>("null\n", useSourceGeneration));

        Assert.Contains("nullable case", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SequenceAndMappingCasesAreSelectedByTokenShape(bool useSourceGeneration)
    {
        var sequence = Deserialize<CollectionOrDogUnion>("- 1\n- 2\n", useSourceGeneration);
        var dog = Deserialize<CollectionOrDogUnion>("Name: Rex\n", useSourceGeneration);

        Assert.NotNull(sequence);
        Assert.Equal(new[] { 1, 2 }, Assert.IsType<List<int>>(sequence.Value));
        Assert.NotNull(dog);
        Assert.Equal("Rex", Assert.IsType<UnionDog>(dog.Value).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SerializeObjectCaseWritesObjectDirectly(bool useSourceGeneration)
    {
        var yaml = Serialize(new CollectionOrDogUnion(new UnionDog { Name = "Rex" }), useSourceGeneration);

        Assert.Equal("Name: Rex\n", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousMappingCasesThrowWhenDeserializing(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<AmbiguousAnimalUnion>("Name: Rex\n", useSourceGeneration));

        Assert.Contains("multiple cases", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousNumberCasesThrowWhenDeserializing(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<AmbiguousNumberUnion>("42\n", useSourceGeneration));

        Assert.Contains("multiple cases", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnionMemberRoundTripsInsideObject(bool useSourceGeneration)
    {
        var yaml = Serialize(new UnionHolder { Value = new ScalarUnion("hello") }, useSourceGeneration);
        var roundTrip = Deserialize<UnionHolder>(yaml, useSourceGeneration);

        Assert.Equal("Value: hello\n", yaml);
        Assert.NotNull(roundTrip);
        Assert.Equal("hello", roundTrip.Value.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousUnionCanStillSerializeConcreteCase(bool useSourceGeneration)
    {
        var yaml = Serialize(new AmbiguousAnimalUnion(new UnionDog { Name = "Rex" }), useSourceGeneration);

        Assert.Equal("Name: Rex\n", yaml);
    }

    private static string Serialize<T>(T value, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Serialize(value, CSharpUnionYamlContext.Default)
            : YamlSerializer.Serialize(value);

    private static T? Deserialize<T>(string yaml, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, CSharpUnionYamlContext.Default)
            : YamlSerializer.Deserialize<T>(yaml);

    internal sealed class UnionDog
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class UnionCat
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class UnionHolder
    {
        public ScalarUnion Value { get; set; } = new(0);
    }

    internal union ScalarUnion(int, string, bool);
    internal union NullableUnion(string?, int);
    internal union NullableNumberUnion(int?, string);
    internal union NonNullableUnion(string, int);
    internal union CollectionOrDogUnion(List<int>, UnionDog);
    internal union AmbiguousAnimalUnion(UnionDog, UnionCat);
    internal union AmbiguousNumberUnion(int, double);
}

[YamlSerializable(typeof(YamlCSharpUnionTests.ScalarUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NullableUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NullableNumberUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NonNullableUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.CollectionOrDogUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.AmbiguousAnimalUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.AmbiguousNumberUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.UnionHolder))]
internal sealed partial class CSharpUnionYamlContext : YamlSerializerContext
{
}
#endif
