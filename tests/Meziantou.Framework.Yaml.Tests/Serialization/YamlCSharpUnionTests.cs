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
    public void NullWithoutNullableCaseReturnsDefault(bool useSourceGeneration)
    {
        Assert.Null(Deserialize<NonNullableUnion>("null\n", useSourceGeneration).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultUnionRoundTripsThroughNull(bool useSourceGeneration)
    {
        var yaml = Serialize(default(NonNullableUnion), useSourceGeneration);
        var roundTrip = Deserialize<NonNullableUnion>(yaml, useSourceGeneration);

        Assert.Equal("null\n", yaml);
        Assert.Null(roundTrip.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultUnionRoundTripsInsideObject(bool useSourceGeneration)
    {
        var yaml = Serialize(new NonNullableUnionHolder(), useSourceGeneration);
        var roundTrip = Deserialize<NonNullableUnionHolder>(yaml, useSourceGeneration);

        Assert.Equal("Value: null\n", yaml);
        Assert.NotNull(roundTrip);
        Assert.Null(roundTrip.Value.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableAndNonNullableOverloadsOfTheSameCaseAreNotAmbiguous(bool useSourceGeneration)
    {
        var number = Deserialize<NullableOverloadUnion>("42\n", useSourceGeneration);
        var nullValue = Deserialize<NullableOverloadUnion>("null\n", useSourceGeneration);

        Assert.NotNull(number);
        Assert.Equal(42, number.Value);
        Assert.NotNull(nullValue);
        Assert.Null(nullValue.Value);
        Assert.Equal("42\n", Serialize(new NullableOverloadUnion(42), useSourceGeneration));
        Assert.Equal("42\n", Serialize(new NullableOverloadUnion((int?)42), useSourceGeneration));
        Assert.Equal("null\n", Serialize(new NullableOverloadUnion((int?)null), useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecursiveUnionCaseUsesThePayloadAndNotTheUnionInstance(bool useSourceGeneration)
    {
        var yaml = Serialize(new RecursiveUnion(new RecursiveUnion(true)), useSourceGeneration);
        var roundTrip = Deserialize<RecursiveUnion>(yaml, useSourceGeneration);

        Assert.Equal("true\n", yaml);
        Assert.NotNull(roundTrip);
        Assert.Equal(true, roundTrip.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnionImplementingItsOwnCaseInterfaceUsesThePayload(bool useSourceGeneration)
    {
        var yaml = Serialize(new ShapeUnion(new UnionSquare { Size = 3 }), useSourceGeneration);

        Assert.Equal("$type: square\nSize: 3\n", yaml);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralClassifierSelectsMappingCaseByKeys(bool useSourceGeneration)
    {
        var circle = Deserialize<ShapeOrCircleUnion>("Radius: 3\n", useSourceGeneration, StructuralOptions);
        var rectangle = Deserialize<ShapeOrCircleUnion>("Width: 4\nHeight: 5\n", useSourceGeneration, StructuralOptions);

        Assert.Equal(3, Assert.IsType<UnionCircle>(circle.Value).Radius);
        var value = Assert.IsType<UnionRectangle>(rectangle.Value);
        Assert.Equal(4, value.Width);
        Assert.Equal(5, value.Height);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralClassifierFailsWhenNoCaseMatchesTheKeys(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<ShapeOrCircleUnion>("Depth: 3\n", useSourceGeneration, StructuralOptions));

        Assert.Contains("multiple cases", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralClassifierIsNotUsedWhenNotRegistered(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<ShapeOrCircleUnion>("Radius: 3\n", useSourceGeneration));

        Assert.Contains("multiple cases", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralClassifierUsesRequiredKeysToTellCasesApart(bool useSourceGeneration)
    {
        var full = Deserialize<PointOrLabelUnion>("Name: a\nX: 1\n", useSourceGeneration, StructuralOptions);
        var partial = Deserialize<PointOrLabelUnion>("Name: a\n", useSourceGeneration, StructuralOptions);

        Assert.Equal(1, Assert.IsType<UnionPoint>(full.Value).X);
        Assert.Equal("a", Assert.IsType<UnionLabel>(partial.Value).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralClassifierIgnoresKeysNoCaseDeclares(bool useSourceGeneration)
    {
        var value = Deserialize<ShapeOrCircleUnion>("Radius: 3\nExtra: 4\n", useSourceGeneration, StructuralOptions);

        Assert.Equal(3, Assert.IsType<UnionCircle>(value.Value).Radius);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralClassifierHonorsCaseInsensitivePropertyNames(bool useSourceGeneration)
    {
        var options = StructuralOptions with { PropertyNameCaseInsensitive = true };
        var value = Deserialize<ShapeOrCircleUnion>("radius: 3\n", useSourceGeneration, options);

        Assert.Equal(3, Assert.IsType<UnionCircle>(value.Value).Radius);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralClassifierRejectsCasesThatCannotBeToldApart(bool useSourceGeneration)
    {
        var exception = Assert.Throws<NotSupportedException>(() => Deserialize<AmbiguousAnimalUnion>("Name: Rex\n", useSourceGeneration, StructuralOptions));

        Assert.Contains("cannot be told apart", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralClassifierFallsBackToTheCatchAllCase(bool useSourceGeneration)
    {
        var label = Deserialize<LabelOrAnyUnion>("Name: a\n", useSourceGeneration, StructuralOptions);
        var other = Deserialize<LabelOrAnyUnion>("Depth: 3\n", useSourceGeneration, StructuralOptions);

        Assert.Equal("a", Assert.IsType<UnionLabel>(label.Value).Name);
        var mapping = Assert.IsAssignableTo<IDictionary<string, object?>>(other.Value);
        Assert.Equal(3, mapping["Depth"]);
    }

    private static YamlSerializerOptions StructuralOptions { get; } = new() { TypeClassifiers = [new YamlUnionTypeStructuralClassifier()] };

    private static string Serialize<T>(T value, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Serialize(value, CSharpUnionYamlContext.Default)
            : YamlSerializer.Serialize(value);

    private static T? Deserialize<T>(string yaml, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, CSharpUnionYamlContext.Default)
            : YamlSerializer.Deserialize<T>(yaml);

    private static T? Deserialize<T>(string yaml, bool useSourceGeneration, YamlSerializerOptions options)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, new CSharpUnionYamlContext(options))
            : YamlSerializer.Deserialize<T>(yaml, options);

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
    internal union NullableOverloadUnion(int, int?, string);
    internal union ShapeOrCircleUnion(UnionCircle, UnionRectangle);
    internal union PointOrLabelUnion(UnionPoint, UnionLabel);
    internal union LabelOrAnyUnion(UnionLabel, object);

    internal sealed class UnionPoint
    {
        public required string Name { get; set; }
        public required int X { get; set; }
    }

    internal sealed class UnionLabel
    {
        public required string Name { get; set; }
    }


    internal sealed class UnionCircle
    {
        public int Radius { get; set; }
    }

    internal sealed class UnionRectangle
    {
        public int Width { get; set; }
        public int Height { get; set; }
    }

    internal union RecursiveUnion(bool, RecursiveUnion?);
    internal union ShapeUnion(int, IUnionShape) : IUnionShape
    {
        int IUnionShape.Size => -1;
    }

    [YamlDerivedType(typeof(UnionSquare), "square")]
    internal interface IUnionShape
    {
        int Size { get; }
    }

    internal sealed class UnionSquare : IUnionShape
    {
        public int Size { get; set; }
    }

    internal sealed class NonNullableUnionHolder
    {
        public NonNullableUnion Value { get; set; }
    }
}

[YamlSerializable(typeof(YamlCSharpUnionTests.ScalarUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NullableUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NullableNumberUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NonNullableUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.CollectionOrDogUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.AmbiguousAnimalUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.AmbiguousNumberUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.UnionHolder))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NullableOverloadUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.RecursiveUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.ShapeUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NonNullableUnionHolder))]
[YamlSerializable(typeof(YamlCSharpUnionTests.ShapeOrCircleUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.PointOrLabelUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.LabelOrAnyUnion))]
internal sealed partial class CSharpUnionYamlContext : YamlSerializerContext
{
    public CSharpUnionYamlContext()
    {
    }

    public CSharpUnionYamlContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}
#endif
