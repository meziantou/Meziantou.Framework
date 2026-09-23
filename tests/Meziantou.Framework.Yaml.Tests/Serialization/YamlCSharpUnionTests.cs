#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework.Yaml.Model;
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
        var mapping = Assert.IsAssignableTo<IDictionary<object, object?>>(other.Value);
        Assert.Equal(3, mapping["Depth"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralClassifierKeepsQuotedScalarsAsText(bool useSourceGeneration)
    {
        Assert.Equal("42", Deserialize<TextOrAnyUnion>("\"42\"\n", useSourceGeneration, StructuralOptions).Value);
        Assert.Equal("true", Deserialize<TextOrAnyUnion>("'true'\n", useSourceGeneration, StructuralOptions).Value);
        Assert.Equal("42\n", Deserialize<TextOrAnyUnion>("|\n  42\n", useSourceGeneration, StructuralOptions).Value);
        Assert.Equal("42", Deserialize<TextOrAnyUnion>("!!str 42\n", useSourceGeneration, StructuralOptions).Value);
        Assert.Equal(42L, Deserialize<TextOrAnyUnion>("42\n", useSourceGeneration, StructuralOptions).Value);
        Assert.Equal(true, Deserialize<TextOrAnyUnion>("true\n", useSourceGeneration, StructuralOptions).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralClassifierSelectsTheStringCaseForAQuotedNumberReadableFromString(bool useSourceGeneration)
    {
        Assert.Equal("42", Deserialize<StringNumberOrTextUnion>("\"42\"\n", useSourceGeneration, StructuralOptions).Value);
        Assert.Equal(42, Deserialize<StringNumberOrTextUnion>("42\n", useSourceGeneration, StructuralOptions).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumberHandlingOnUnionWritesNumbersAsStrings(bool useSourceGeneration)
    {
        var yaml = Serialize(new StringNumberUnion(42), useSourceGeneration);

        Assert.Equal("\"42\"\n", yaml);
        Assert.Equal(42, Deserialize<StringNumberUnion>(yaml, useSourceGeneration).Value);
        Assert.Equal(true, Deserialize<StringNumberUnion>("true\n", useSourceGeneration).Value);
        Assert.Equal("\"42\"\n", Serialize(new StringNullableNumberUnion((int?)42), useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumberHandlingOnUnionReadsNumbersFromStrings(bool useSourceGeneration)
    {
        Assert.Equal(42, Deserialize<StringNumberUnion>("\"42\"\n", useSourceGeneration).Value);
        Assert.Equal(42, Deserialize<StringNumberUnion>("42\n", useSourceGeneration).Value);

        var exception = Assert.Throws<YamlException>(() => Deserialize<StringNumberUnion>("\"hello\"\n", useSourceGeneration));
        Assert.Contains("does not define a case", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumberHandlingOnUnionHonorsNamedFloatingPointLiterals(bool useSourceGeneration)
    {
        var yaml = Serialize(new NamedFloatUnion(double.NaN), useSourceGeneration);

        Assert.Equal("\"NaN\"\n", yaml);
        Assert.Equal(double.NaN, Deserialize<NamedFloatUnion>(yaml, useSourceGeneration).Value);
        Assert.Equal(double.PositiveInfinity, Deserialize<NamedFloatUnion>("\"Infinity\"\n", useSourceGeneration).Value);
        Assert.Equal(1.5, Deserialize<NamedFloatUnion>("1.5\n", useSourceGeneration).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumberHandlingOnUnionMakesNumericStringsAmbiguous(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<StringNumberOrTextUnion>("\"42\"\n", useSourceGeneration));

        Assert.Contains("multiple cases", exception.Message);
        Assert.Equal("hello", Deserialize<StringNumberOrTextUnion>("\"hello\"\n", useSourceGeneration).Value);
        Assert.Equal(42, Deserialize<StringNumberOrTextUnion>("42\n", useSourceGeneration).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumberHandlingOnUnionLetsClassifierResolveNumericStrings(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { TypeClassifiers = [new NumberOrTextUnionClassifier()] };

        Assert.Equal(42, Deserialize<StringNumberOrTextUnion>("\"42\"\n", useSourceGeneration, options).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumberHandlingWithoutAllowReadingFromStringKeepsStringsUnambiguous(bool useSourceGeneration)
    {
        Assert.Equal("42", Deserialize<WriteAsStringNumberOrTextUnion>("\"42\"\n", useSourceGeneration).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SerializeUsesTheMostSpecificCaseForADerivedValue(bool useSourceGeneration)
    {
        var yaml = Serialize(new AnimalHierarchyUnion(new UnionPuppy { Name = "Rex", Breed = "Lab" }), useSourceGeneration);

        Assert.Equal("Name: Rex\nBreed: Lab\n", yaml);
        Assert.Equal("Name: Rex\n", Serialize(new AnimalHierarchyUnion(new UnionAnimal { Name = "Rex" }), useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SerializePrefersAnInterfaceCaseOverAnObjectCase(bool useSourceGeneration)
    {
        var yaml = Serialize(new AnyOrShapeUnion(new UnionSquare { Size = 3 }), useSourceGeneration);

        Assert.Equal("$type: square\nSize: 3\n", yaml);
        Assert.Equal("3\n", Serialize(new AnyOrShapeUnion(3), useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnionWithAdditionalConstructorsIsStillAUnion(bool useSourceGeneration)
    {
        Assert.Equal("3\n", Serialize(new ExtraConstructorUnion(1, 2), useSourceGeneration));
        Assert.Equal(42, Deserialize<ExtraConstructorUnion>("42\n", useSourceGeneration).Value);
        Assert.Equal("a", Deserialize<ExtraConstructorUnion>("a\n", useSourceGeneration).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandWrittenUnionTypeIsSupported(bool useSourceGeneration)
    {
        var yaml = Serialize(new HandWrittenUnionHolder { Value = new HandWrittenUnion(42) }, useSourceGeneration);
        var roundTrip = Deserialize<HandWrittenUnionHolder>("Value: hello\n", useSourceGeneration);

        Assert.Equal("Value: 42\n", yaml);
        Assert.NotNull(roundTrip?.Value);
        Assert.Equal("hello", roundTrip.Value.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnumerableTypeSerializedAsObjectIsAMappingCase(bool useSourceGeneration)
    {
        var yaml = Serialize(new EnumerableObjectOrNumberUnion(new UnionEnumerableObject { Name = "x" }), useSourceGeneration);
        var value = Deserialize<EnumerableObjectOrNumberUnion>(yaml, useSourceGeneration);

        Assert.Equal("Name: x\n", yaml);
        Assert.Equal("x", Assert.IsType<UnionEnumerableObject>(value.Value).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralClassifierDoesNotUseTheMembersOfAPolymorphicCase(bool useSourceGeneration)
    {
        var exception = Assert.Throws<NotSupportedException>(() => Deserialize<LabelOrPolymorphicUnion>("Name: a\n", useSourceGeneration, StructuralOptions));

        Assert.Contains("same YAML shape", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralClassifierHonorsRuntimeUnmappedMemberHandling(bool useSourceGeneration)
    {
        var options = StructuralOptions with { UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow };

        var lenient = Deserialize<LenientOrPersonUnion>("Name: a\nExtra: 1\n", useSourceGeneration, options);
        var person = Deserialize<LenientOrPersonUnion>("Name: a\nAge: 1\n", useSourceGeneration, options);

        Assert.Equal("a", Assert.IsType<UnionLenientLabel>(lenient.Value).Name);
        Assert.Equal(1, Assert.IsType<UnionPerson>(person.Value).Age);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassifierSelectingACaseThatCannotRepresentTheValueFails(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { TypeClassifiers = [new AlwaysDogUnionClassifier()] };

        var exception = Assert.Throws<YamlException>(() => Deserialize<NumberOrDogUnion>("42\n", useSourceGeneration, options));

        Assert.Contains($"Union type '{typeof(NumberOrDogUnion)}' does not define a case that matches YAML number values.", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErrorMessagesUseTheRuntimeTypeName(bool useSourceGeneration)
    {
        var noCase = Assert.Throws<YamlException>(() => Deserialize<ScalarUnion>("- 1\n", useSourceGeneration));
        var ambiguous = Assert.Throws<YamlException>(() => Deserialize<AmbiguousNumberUnion>("42\n", useSourceGeneration));
        var alias = Assert.Throws<YamlException>(() => Deserialize<DogAndBooleanUnionHolder>("Dog: &d {Name: Rex}\nValue: *d\n", useSourceGeneration, new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve }));

        Assert.Contains($"Union type '{typeof(ScalarUnion)}' does not define a case that matches YAML sequence values.", noCase.Message);
        Assert.Contains($"Cannot deserialize union type '{typeof(AmbiguousNumberUnion)}' because multiple cases match YAML number values.", ambiguous.Message);
        Assert.Contains($"Union type '{typeof(BooleanOrTextUnion)}' does not define a case that can represent the referenced value.", alias.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedUnionCaseMatchesTheKindsOfItsCases(bool useSourceGeneration)
    {
        Assert.Equal(42, Assert.IsType<ScalarUnion>(Deserialize<NestedScalarOrListUnion>("42\n", useSourceGeneration).Value).Value);
        Assert.Equal(true, Assert.IsType<ScalarUnion>(Deserialize<NestedScalarOrListUnion>("true\n", useSourceGeneration).Value).Value);
        Assert.Equal("a", Assert.IsType<ScalarUnion>(Deserialize<NestedScalarOrListUnion>("a\n", useSourceGeneration).Value).Value);
        Assert.Equal(new[] { 1, 2 }, Assert.IsType<List<int>>(Deserialize<NestedScalarOrListUnion>("- 1\n- 2\n", useSourceGeneration).Value));
        Assert.Equal("42\n", Serialize(new NestedScalarOrListUnion(new ScalarUnion(42)), useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedUnionCaseSharingAKindIsAmbiguous(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<NestedScalarOrNumberUnion>("42\n", useSourceGeneration));

        Assert.Contains("multiple cases", exception.Message);
        Assert.Equal(new[] { 1 }, Assert.IsType<List<int>>(Deserialize<NestedScalarOrNumberUnion>("- 1\n", useSourceGeneration).Value));
        Assert.Equal("a", Assert.IsType<ScalarUnion>(Deserialize<NestedScalarOrNumberUnion>("a\n", useSourceGeneration).Value).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MutuallyRecursiveUnionsRoundTrip(bool useSourceGeneration)
    {
        Assert.Equal(42, Deserialize<NumberOrRecursiveTextUnion>("42\n", useSourceGeneration).Value);
        Assert.Equal("a", Assert.IsType<TextOrRecursiveNumberUnion>(Deserialize<NumberOrRecursiveTextUnion>("a\n", useSourceGeneration).Value).Value);
        Assert.Equal("a", Deserialize<TextOrRecursiveNumberUnion>("a\n", useSourceGeneration).Value);
        Assert.Equal(42, Assert.IsType<NumberOrRecursiveTextUnion>(Deserialize<TextOrRecursiveNumberUnion>("42\n", useSourceGeneration).Value).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaseWithTypeLevelConverterReadsScalarsNoOtherCaseMatches(bool useSourceGeneration)
    {
        var point = Assert.IsType<UnionConvertedPoint>(Deserialize<ConvertedPointOrListUnion>("1,2\n", useSourceGeneration).Value);

        Assert.Equal(1, point.X);
        Assert.Equal(2, point.Y);
        Assert.Equal(new[] { 1 }, Assert.IsType<List<int>>(Deserialize<ConvertedPointOrListUnion>("- 1\n", useSourceGeneration).Value));
        Assert.Equal("1,2\n", Serialize(new ConvertedPointOrListUnion(point), useSourceGeneration));
        Assert.Equal("a", Deserialize<ConvertedPointOrTextUnion>("a\n", useSourceGeneration).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaseWithOptionsConverterReadsScalarsNoOtherCaseMatches(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { Converters = [new UnionOptionsPointConverter()] };

        var point = Assert.IsType<UnionOptionsPoint>(Deserialize<OptionsPointOrListUnion>("3,4\n", useSourceGeneration, options).Value);

        Assert.Equal(3, point.X);
        Assert.Equal(4, point.Y);
        var exception = Assert.Throws<YamlException>(() => Deserialize<OptionsPointOrListUnion>("3,4\n", useSourceGeneration));
        Assert.Contains("does not define a case", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaseWithOptionsConverterFactoryReadsScalarsNoOtherCaseMatches(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { Converters = [new UnionOptionsPointConverterFactory()] };

        var point = Assert.IsType<UnionOptionsPoint>(Deserialize<OptionsPointOrListUnion>("5,6\n", useSourceGeneration, options).Value);

        Assert.Equal(5, point.X);
        Assert.Equal(6, point.Y);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaseWithGenerationOptionsConverterReadsScalarsNoOtherCaseMatches(bool useSourceGeneration)
    {
        var value = useSourceGeneration
            ? YamlSerializer.Deserialize<OptionsPointOrListUnion>("7,8\n", CSharpUnionConverterYamlContext.Default)
            : YamlSerializer.Deserialize<OptionsPointOrListUnion>("7,8\n", new YamlSerializerOptions { Converters = [new UnionOptionsPointConverter()] });

        Assert.Equal(7, Assert.IsType<UnionOptionsPoint>(value.Value).X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableNestedUnionCaseMatchesTheKindsOfItsCases(bool useSourceGeneration)
    {
        Assert.Equal("a", Assert.IsType<ScalarUnion>(Deserialize<NestedNullableScalarOrListUnion>("a\n", useSourceGeneration).Value).Value);
        Assert.Null(Deserialize<NestedNullableScalarOrListUnion>("null\n", useSourceGeneration).Value);
        Assert.IsType<List<int>>(Deserialize<NestedNullableScalarOrListUnion>("[]\n", useSourceGeneration).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void YamlModelCasesMatchTheirOwnShape(bool useSourceGeneration)
    {
        Assert.IsType<YamlSequence>(Deserialize<YamlSequenceOrValueUnion>("- 1\n", useSourceGeneration).Value);
        Assert.IsType<YamlValue>(Deserialize<YamlSequenceOrValueUnion>("a\n", useSourceGeneration).Value);
        Assert.IsType<YamlMapping>(Deserialize<YamlMappingOrNumberUnion>("a: 1\n", useSourceGeneration).Value);
        Assert.Equal(42, Deserialize<YamlMappingOrNumberUnion>("42\n", useSourceGeneration).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarNoCaseMatchesExactlyFallsBackToCasesThatCanReadIt(bool useSourceGeneration)
    {
        Assert.Equal("42", Deserialize<TextOrListUnion>("42\n", useSourceGeneration).Value);
        Assert.Equal("true", Deserialize<TextOrListUnion>("true\n", useSourceGeneration).Value);
        Assert.Equal(UnionColor.Green, Deserialize<ColorOrListUnion>("1\n", useSourceGeneration).Value);
        Assert.Equal('7', Deserialize<CharOrListUnion>("7\n", useSourceGeneration).Value);
        Assert.Equal(42, Deserialize<ScalarUnion>("42\n", useSourceGeneration).Value);
        Assert.Equal("42", Deserialize<ScalarUnion>("\"42\"\n", useSourceGeneration).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeveralFallbackCasesAreAmbiguous(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<TextOrColorUnion>("1\n", useSourceGeneration));

        Assert.Contains("multiple cases match YAML number values", exception.Message);
        Assert.Equal("a", Deserialize<TextOrColorUnion>("a\n", useSourceGeneration, new YamlSerializerOptions { TypeClassifiers = [new TextOrColorUnionClassifier()] }).Value);
        Assert.Equal(UnionColor.Green, Deserialize<TextOrColorUnion>("1\n", useSourceGeneration, new YamlSerializerOptions { TypeClassifiers = [new TextOrColorUnionClassifier()] }).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedUnionNumberHandlingLetsTheCaseReadNumericStrings(bool useSourceGeneration)
    {
        var value = Deserialize<NestedStringNumberOrListUnion>("\"42\"\n", useSourceGeneration);

        Assert.Equal(42, Assert.IsType<StringNumberUnion>(value.Value).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionsConverterOfAScalarCaseIsUsed(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { Converters = [new UnionMinutesConverter()] };

        Assert.Equal(TimeSpan.FromMinutes(5), Deserialize<MinutesOrListUnion>("5m\n", useSourceGeneration, options).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionsConverterOfANestedUnionCaseReadsScalars(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { Converters = [new UnionOptionsPointConverter()] };

        var value = Deserialize<NestedOptionsPointOrNumberUnion>("3,4\n", useSourceGeneration, options);

        Assert.Equal(3, Assert.IsType<UnionOptionsPoint>(Assert.IsType<OptionsPointOrListUnion>(value.Value).Value).X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SerializeOrdersVariantInterfaceCasesByAssignability(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { Converters = [new UnionStringSequenceConverter()] };

        var yaml = useSourceGeneration
            ? YamlSerializer.Serialize(new VariantSequenceUnion(new List<string> { "a", "b" }), new CSharpUnionYamlContext(options))
            : YamlSerializer.Serialize(new VariantSequenceUnion(new List<string> { "a", "b" }), options);

        Assert.Equal("a|b\n", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralClassifierUsesTheMembersOfAnInterfaceCase(bool useSourceGeneration)
    {
        var circle = Deserialize<NamedOrCircleUnion>("Radius: 3\n", useSourceGeneration, StructuralOptions);

        Assert.Equal(3, Assert.IsType<UnionCircle>(circle.Value).Radius);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassifierErrorsReportThePositionOfTheValue(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { TypeClassifiers = [new AlwaysDogUnionClassifier()] };

        var exception = Assert.Throws<YamlException>(() => Deserialize<NumberOrDogUnionHolder>("Other: 1\nValue: 42\n", useSourceGeneration, options));

        var inner = exception;
        while (inner.InnerException is YamlException innerYamlException)
        {
            inner = innerYamlException;
        }

        Assert.Contains("does not define a case that matches YAML number values", inner.Message);
        Assert.Equal(1, inner.Start.Line);
        Assert.Equal(7, inner.Start.Column);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnionsRoundTripInsideCollections(bool useSourceGeneration)
    {
        var holder = new UnionCollectionsHolder
        {
            Map = new Dictionary<string, NestedScalarOrListUnion> { ["a"] = new(new ScalarUnion(42)), ["b"] = new(new List<int> { 1 }) },
            List = [new TextOrListUnion("42"), new TextOrListUnion(new List<int> { 2 })],
        };

        var yaml = Serialize(holder, useSourceGeneration);
        var roundTrip = Deserialize<UnionCollectionsHolder>(yaml, useSourceGeneration);

        Assert.Equal("Map:\n  a: 42\n  b:\n    - 1\nList:\n  - \"42\"\n  -\n    - 2\n", yaml);
        Assert.NotNull(roundTrip);
        Assert.Equal(42, Assert.IsType<ScalarUnion>(roundTrip.Map["a"].Value).Value);
        Assert.Equal(new[] { 1 }, Assert.IsType<List<int>>(roundTrip.Map["b"].Value));
        Assert.Equal("42", roundTrip.List[0].Value);
        Assert.Equal(new[] { 2 }, Assert.IsType<List<int>>(roundTrip.List[1].Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnionConstructorParametersUseTheFallbackCases(bool useSourceGeneration)
    {
        var value = Deserialize<UnionConstructorHolder>("Nested: true\nText: 42\n", useSourceGeneration);

        Assert.NotNull(value);
        Assert.Equal(true, Assert.IsType<ScalarUnion>(value.Nested.Value).Value);
        Assert.Equal("42", value.Text.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AliasToANestedUnionValueIsReused(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };

        var value = Deserialize<NestedUnionAliasHolder>("First: &a\n- 1\nSecond: *a\n", useSourceGeneration, options);

        Assert.NotNull(value);
        Assert.Same(value.First.Value, value.Second.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SerializeUnionAsObjectWritesTheCase(bool useSourceGeneration)
    {
        Assert.Equal("Value: 42\n", Serialize(new UnionObjectHolder { Value = new NestedScalarOrListUnion(new ScalarUnion(42)) }, useSourceGeneration));
    }

    [Fact]
    public void SerializeUnionAsRootObjectWritesTheCase()
    {
        Assert.Equal("42\n", YamlSerializer.Serialize<object>(new NestedScalarOrListUnion(new ScalarUnion(42))));
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
    internal union TextOrAnyUnion(string, object);

    [YamlNumberHandling(YamlNumberHandling.AllowReadingFromString | YamlNumberHandling.WriteAsString)]
    internal union StringNumberUnion(int, bool);

    [YamlNumberHandling(YamlNumberHandling.AllowReadingFromString | YamlNumberHandling.WriteAsString)]
    internal union StringNullableNumberUnion(int?, bool);

    [YamlNumberHandling(YamlNumberHandling.AllowReadingFromString)]
    internal union StringNumberOrTextUnion(int, string);

    [YamlNumberHandling(YamlNumberHandling.WriteAsString)]
    internal union WriteAsStringNumberOrTextUnion(int, string);

    [YamlNumberHandling(YamlNumberHandling.AllowNamedFloatingPointLiterals)]
    internal union NamedFloatUnion(double, bool);

    private sealed class NumberOrTextUnionClassifier : YamlTypeClassifierFactory
    {
        public override bool CanClassify(YamlTypeClassifierContext context)
            => context.DeclaringType == typeof(StringNumberOrTextUnion);

        public override YamlTypeClassifier CreateYamlClassifier(YamlTypeClassifierContext context, YamlSerializerOptions options)
            => reader => YamlScalar.TryParseInt32(reader, out _) ? typeof(int) : typeof(string);
    }

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

    internal class UnionAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class UnionPuppy : UnionAnimal
    {
        public string Breed { get; set; } = string.Empty;
    }

    // A comparison sort keeps 'UnionAnimal' before 'UnionPuppy' for four cases, because unrelated cases compare as equal.
    internal union AnimalHierarchyUnion(UnionAnimal, int, string, UnionPuppy);
    internal union AnyOrShapeUnion(object, IUnionShape);
    internal union ExtraConstructorUnion(int, string)
    {
        public ExtraConstructorUnion(int left, int right)
            : this(left + right)
        {
        }
    }

    [System.Runtime.CompilerServices.Union]
    internal sealed class HandWrittenUnion : System.Runtime.CompilerServices.IUnion
    {
        public HandWrittenUnion(int value) => Value = value;

        public HandWrittenUnion(string value) => Value = value;

        public object? Value { get; }
    }

    internal sealed class HandWrittenUnionHolder
    {
        public HandWrittenUnion? Value { get; set; }
    }

    internal sealed class UnionEnumerableObject : IEnumerable<int>
    {
        public string Name { get; set; } = string.Empty;

        public IEnumerator<int> GetEnumerator() => Enumerable.Empty<int>().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal union EnumerableObjectOrNumberUnion(UnionEnumerableObject, int);

    [YamlDerivedType(typeof(UnionPolymorphicSquare), "square")]
    internal class UnionPolymorphicShape
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class UnionPolymorphicSquare : UnionPolymorphicShape
    {
        public int Size { get; set; }
    }

    internal union LabelOrPolymorphicUnion(UnionLabel, UnionPolymorphicShape);

    [YamlUnmappedMemberHandling(YamlUnmappedMemberHandling.Skip)]
    internal sealed class UnionLenientLabel
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class UnionPerson
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    internal union LenientOrPersonUnion(UnionLenientLabel, UnionPerson);
    internal union NumberOrDogUnion(int, double, UnionDog);
    internal union BooleanOrTextUnion(bool, string);

    internal sealed class DogAndBooleanUnionHolder
    {
        public UnionDog? Dog { get; set; }
        public BooleanOrTextUnion Value { get; set; }
    }

    private sealed class AlwaysDogUnionClassifier : YamlTypeClassifierFactory
    {
        public override bool CanClassify(YamlTypeClassifierContext context)
            => context.DeclaringType == typeof(NumberOrDogUnion);

        public override YamlTypeClassifier CreateYamlClassifier(YamlTypeClassifierContext context, YamlSerializerOptions options)
            => reader => typeof(UnionDog);
    }

    internal union NestedScalarOrListUnion(ScalarUnion, List<int>);
    internal union NestedScalarOrNumberUnion(ScalarUnion, double, List<int>);
    internal union NumberOrRecursiveTextUnion(int, TextOrRecursiveNumberUnion);
    internal union TextOrRecursiveNumberUnion(string, NumberOrRecursiveTextUnion);

    [YamlConverter(typeof(UnionConvertedPointConverter))]
    internal sealed class UnionConvertedPoint
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    internal sealed class UnionConvertedPointConverter : YamlConverter<UnionConvertedPoint>
    {
        public override UnionConvertedPoint Read(YamlReader reader)
        {
            var parts = reader.GetScalarValue().Split(',');
            reader.Read();
            return new UnionConvertedPoint { X = int.Parse(parts[0], CultureInfo.InvariantCulture), Y = int.Parse(parts[1], CultureInfo.InvariantCulture) };
        }

        public override void Write(YamlWriter writer, UnionConvertedPoint value)
            => writer.WriteScalar(value.X.ToString(CultureInfo.InvariantCulture) + "," + value.Y.ToString(CultureInfo.InvariantCulture));
    }

    internal sealed class UnionOptionsPoint
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    internal sealed class UnionOptionsPointConverter : YamlConverter<UnionOptionsPoint>
    {
        public override UnionOptionsPoint Read(YamlReader reader)
        {
            var parts = reader.GetScalarValue().Split(',');
            reader.Read();
            return new UnionOptionsPoint { X = int.Parse(parts[0], CultureInfo.InvariantCulture), Y = int.Parse(parts[1], CultureInfo.InvariantCulture) };
        }

        public override void Write(YamlWriter writer, UnionOptionsPoint value)
            => writer.WriteScalar(value.X.ToString(CultureInfo.InvariantCulture) + "," + value.Y.ToString(CultureInfo.InvariantCulture));
    }

    internal sealed class UnionOptionsPointConverterFactory : YamlConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(UnionOptionsPoint);

        public override YamlConverter CreateConverter(Type typeToConvert, YamlSerializerOptions options) => new UnionOptionsPointConverter();
    }

    internal union NestedNullableScalarOrListUnion(ScalarUnion?, List<int>);
    internal union ConvertedPointOrListUnion(UnionConvertedPoint, List<int>);
    internal union ConvertedPointOrTextUnion(UnionConvertedPoint, string);
    internal union OptionsPointOrListUnion(UnionOptionsPoint, List<int>);
    internal union YamlSequenceOrValueUnion(YamlSequence, YamlValue);
    internal union YamlMappingOrNumberUnion(YamlMapping, int);
    internal union TextOrListUnion(string, List<int>);
    internal union ColorOrListUnion(UnionColor, List<int>);
    internal union CharOrListUnion(char, List<int>);

    internal enum UnionColor
    {
        Red,
        Green,
    }

    internal union TextOrColorUnion(string, UnionColor);

    private sealed class TextOrColorUnionClassifier : YamlTypeClassifierFactory
    {
        public override bool CanClassify(YamlTypeClassifierContext context)
            => context.DeclaringType == typeof(TextOrColorUnion);

        public override YamlTypeClassifier CreateYamlClassifier(YamlTypeClassifierContext context, YamlSerializerOptions options)
            => reader => YamlScalar.TryParseInt32(reader, out _) ? typeof(UnionColor) : typeof(string);
    }

    internal union NestedStringNumberOrListUnion(StringNumberUnion, List<int>);
    internal union MinutesOrListUnion(TimeSpan, List<int>);
    internal union NestedOptionsPointOrNumberUnion(OptionsPointOrListUnion, int);

    internal sealed class UnionMinutesConverter : YamlConverter<TimeSpan>
    {
        public override TimeSpan Read(YamlReader reader)
        {
            var text = reader.GetScalarValue();
            reader.Read();
            return TimeSpan.FromMinutes(int.Parse(text.TrimEnd('m'), CultureInfo.InvariantCulture));
        }

        public override void Write(YamlWriter writer, TimeSpan value)
            => writer.WriteScalar(((int)value.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m");
    }

    internal union VariantSequenceUnion(IEnumerable<object>, IEnumerable<string>);

    internal sealed class UnionStringSequenceConverter : YamlConverter<IEnumerable<string>>
    {
        public override IEnumerable<string> Read(YamlReader reader)
        {
            var text = reader.GetScalarValue();
            reader.Read();
            return text.Split('|');
        }

        public override void Write(YamlWriter writer, IEnumerable<string> value)
            => writer.WriteScalar(string.Join('|', value));
    }

    internal interface IUnionNamed
    {
        string Name { get; }
    }

    internal union NamedOrCircleUnion(IUnionNamed, UnionCircle);

    internal sealed class NumberOrDogUnionHolder
    {
        public int Other { get; set; }
        public NumberOrDogUnion Value { get; set; }
    }

    internal sealed class UnionCollectionsHolder
    {
        public Dictionary<string, NestedScalarOrListUnion> Map { get; set; } = [];
        public List<TextOrListUnion> List { get; set; } = [];
    }

    internal sealed record UnionConstructorHolder(NestedScalarOrListUnion Nested, TextOrListUnion Text);

    internal sealed class UnionObjectHolder
    {
        public object? Value { get; set; }
    }

    internal sealed class NestedUnionAliasHolder
    {
        public NestedScalarOrListUnion First { get; set; }
        public NestedScalarOrListUnion Second { get; set; }
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
[YamlSerializable(typeof(YamlCSharpUnionTests.TextOrAnyUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.StringNumberUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.StringNullableNumberUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.StringNumberOrTextUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.WriteAsStringNumberOrTextUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NamedFloatUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.AnimalHierarchyUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.AnyOrShapeUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.ExtraConstructorUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.HandWrittenUnionHolder))]
[YamlSerializable(typeof(YamlCSharpUnionTests.EnumerableObjectOrNumberUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.LabelOrPolymorphicUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.LenientOrPersonUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NumberOrDogUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.DogAndBooleanUnionHolder))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NestedScalarOrListUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NestedScalarOrNumberUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NumberOrRecursiveTextUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.TextOrRecursiveNumberUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.ConvertedPointOrListUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.ConvertedPointOrTextUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.OptionsPointOrListUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.YamlSequenceOrValueUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.YamlMappingOrNumberUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.TextOrListUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.ColorOrListUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.CharOrListUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.TextOrColorUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NestedStringNumberOrListUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.MinutesOrListUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NestedOptionsPointOrNumberUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.VariantSequenceUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NamedOrCircleUnion))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NumberOrDogUnionHolder))]
[YamlSerializable(typeof(YamlCSharpUnionTests.UnionCollectionsHolder))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NestedUnionAliasHolder))]
[YamlSerializable(typeof(YamlCSharpUnionTests.UnionConstructorHolder))]
[YamlSerializable(typeof(YamlCSharpUnionTests.UnionObjectHolder))]
[YamlSerializable(typeof(YamlCSharpUnionTests.NestedNullableScalarOrListUnion))]
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

[YamlSourceGenerationOptions(Converters = [typeof(YamlCSharpUnionTests.UnionOptionsPointConverter)])]
[YamlSerializable(typeof(YamlCSharpUnionTests.OptionsPointOrListUnion))]
internal sealed partial class CSharpUnionConverterYamlContext : YamlSerializerContext
{
}
#endif
