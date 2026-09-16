#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework.Yaml.Model;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlObjectConverterContractTests
{
    private sealed class FieldIncludedModel
    {
#pragma warning disable IDE0032 // Use auto property
#pragma warning disable IDE0044 // Add readonly modifier
        [YamlInclude]
        [YamlPropertyName("age")]
        private int _age;
#pragma warning restore IDE0044 // Add readonly modifier
#pragma warning restore IDE0032 // Use auto property

        public int Age => _age;

        public FieldIncludedModel()
        {
        }

        public FieldIncludedModel(int age)
        {
            _age = age;
        }
    }

    private sealed class IgnoredModel
    {
        public int Keep { get; set; }

        [YamlIgnore]
        public int Skip { get; set; }
    }

    private sealed class DefaultIgnoredModel
    {
        public int Count { get; set; }
    }

    private sealed class YamlIgnoreConditionModel
    {
        public int Keep { get; set; }

        [YamlIgnore]
        public int Always { get; set; }

        [YamlIgnore(Condition = YamlIgnoreCondition.Never)]
        public int Never { get; set; }

        [YamlIgnore(Condition = YamlIgnoreCondition.WhenWritingDefault)]
        public int Default { get; set; }

        [YamlIgnore(Condition = YamlIgnoreCondition.WhenWritingNull)]
        public string? Null { get; set; }

        [YamlIgnore(Condition = YamlIgnoreCondition.WhenWriting)]
        public int WriteOnly { get; set; }

        [YamlIgnore(Condition = YamlIgnoreCondition.WhenReading)]
        public int ReadOnly { get; set; }
    }

    private sealed class Person
    {
        public string FirstName { get; set; } = string.Empty;
    }

    [YamlUnmappedMemberHandling(YamlUnmappedMemberHandling.Disallow)]
    private sealed class StrictPerson
    {
        public string FirstName { get; set; } = string.Empty;
    }

    [YamlUnmappedMemberHandling(YamlUnmappedMemberHandling.Disallow)]
    private sealed class StrictExtensionDataPerson
    {
        public string FirstName { get; set; } = string.Empty;

        [YamlExtensionData]
        public Dictionary<string, object?> Extra { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class NullIgnoreModel
    {
        public string? Nick { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private abstract class AbstractBase
    {
        public int X { get; set; }
    }

    private sealed class NoDefaultCtor
    {
        public NoDefaultCtor(int value) => Value = value;

        public int Value { get; }
    }

    private sealed class MultiplePublicCtors
    {
        public MultiplePublicCtors(int value) => Value = value;

        public MultiplePublicCtors(string name) => Name = name;

        public int Value { get; }

        public string? Name { get; }
    }

    private sealed class PublicFieldsModel
    {
        public int Value;
        public string? Optional;
        public readonly int ReadOnlyValue = 5;

        public int Property { get; set; }
    }

    private sealed class ReadOnlyPropertyModel
    {
        public int Mutable { get; set; } = 1;

        public int ReadOnly { get; } = 2;
    }

    private sealed class ConstructorParametersModel
    {
        public ConstructorParametersModel(string name, int age)
        {
            Name = name;
            Age = age;
        }

        public string? Name { get; }

        public int Age { get; }
    }

    private sealed class NullableAnnotationsModel
    {
        public string Name { get; set; } = string.Empty;

        public string? Optional { get; set; }
    }

    [Fact]
    public void IncludedField_IsSerializedAndDeserialized()
    {
        var yaml = YamlSerializer.Serialize(new FieldIncludedModel(37));
        Assert.Contains("age: 37", yaml);

        var roundTrip = YamlSerializer.Deserialize<FieldIncludedModel>("age: 41\n");
        Assert.NotNull(roundTrip);
        Assert.Equal(41, roundTrip.Age);
    }

    [Fact]
    public void YamlIgnore_SkipsMemberForWriteAndRead()
    {
        var yaml = YamlSerializer.Serialize(new IgnoredModel { Keep = 1, Skip = 2 });
        Assert.Contains("Keep: 1", yaml);
        Assert.DoesNotContain("Skip:", yaml);

        var deserialized = YamlSerializer.Deserialize<IgnoredModel>("Keep: 1\nSkip: 999\n");
        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized.Keep);
        Assert.Equal(0, deserialized.Skip);
    }

    [Fact]
    public void JsonIgnore_WhenWritingDefault_HidesDefaultValue()
    {
        var yaml = YamlSerializer.Serialize(new DefaultIgnoredModel { Count = 0 }, new YamlSerializerOptions { DefaultIgnoreCondition = YamlIgnoreCondition.WhenWritingDefault });
        Assert.Equal("{}\n", yaml);

        var yaml2 = YamlSerializer.Serialize(new DefaultIgnoredModel { Count = 2 }, new YamlSerializerOptions { DefaultIgnoreCondition = YamlIgnoreCondition.WhenWritingDefault });
        Assert.Contains("Count: 2", yaml2);
    }

    [Fact]
    public void YamlIgnoreCondition_MatchesJsonIgnoreBehavior()
    {
        var yaml = YamlSerializer.Serialize(
            new YamlIgnoreConditionModel { Keep = 1, Always = 2, Never = 0, Default = 0, Null = null, WriteOnly = 3, ReadOnly = 4 },
            new YamlSerializerOptions { DefaultIgnoreCondition = YamlIgnoreCondition.WhenWritingDefault });

        Assert.Contains("Keep: 1", yaml);
        Assert.Contains("Never: 0", yaml);
        Assert.Contains("ReadOnly: 4", yaml);
        Assert.DoesNotContain("Always:", yaml);
        Assert.DoesNotContain("Default:", yaml);
        Assert.DoesNotContain("Null:", yaml);
        Assert.DoesNotContain("WriteOnly:", yaml);

        var deserialized = YamlSerializer.Deserialize<YamlIgnoreConditionModel>("Keep: 10\nAlways: 20\nNever: 60\nDefault: 30\nNull: hi\nWriteOnly: 40\nReadOnly: 50\n");
        Assert.NotNull(deserialized);
        Assert.Equal(10, deserialized.Keep);
        Assert.Equal(0, deserialized.Always);
        Assert.Equal(60, deserialized.Never);
        Assert.Equal(30, deserialized.Default);
        Assert.Equal("hi", deserialized.Null);
        Assert.Equal(40, deserialized.WriteOnly);
        Assert.Equal(0, deserialized.ReadOnly);
    }

    [Fact]
    public void DefaultIgnoreCondition_WhenWritingNull_HidesNullMembers()
    {
        var yaml = YamlSerializer.Serialize(
            new NullIgnoreModel { Nick = null, Name = "Ada" },
            new YamlSerializerOptions { DefaultIgnoreCondition = YamlIgnoreCondition.WhenWritingNull });

        Assert.Contains("Name: Ada", yaml);
        Assert.DoesNotContain("Nick:", yaml);
    }

    [Fact]
    public void PropertyNameCaseInsensitive_AllowsMismatchedCasing()
    {
        var person = YamlSerializer.Deserialize<Person>(
            "firstname: Ada\n",
            new YamlSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(person);
        Assert.Equal("Ada", person.FirstName);
    }

    [Fact]
    public void UnmappedMembers_AreSkippedByDefault()
    {
        var person = YamlSerializer.Deserialize<Person>("FirstName: Ada\nLastName: Lovelace\n");

        Assert.NotNull(person);
        Assert.Equal("Ada", person.FirstName);
    }

    [Fact]
    public void UnmappedMembers_CanBeDisallowedViaOptions()
    {
        var options = new YamlSerializerOptions { UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Person>("FirstName: Ada\nLastName: Lovelace\n", options));

        Assert.Contains("LastName", exception.Message);
        Assert.Contains(typeof(Person).ToString(), exception.Message);
    }

    [Fact]
    public void JsonUnmappedMemberHandlingAttribute_CanDisallowUnknownMembers()
    {
        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<StrictPerson>("FirstName: Ada\nLastName: Lovelace\n"));

        Assert.Contains("LastName", exception.Message);
        Assert.Contains(typeof(StrictPerson).ToString(), exception.Message);
    }

    [Fact]
    public void JsonUnmappedMemberHandling_DoesNotConflictWithExtensionData()
    {
        var person = YamlSerializer.Deserialize<StrictExtensionDataPerson>("FirstName: Ada\nLastName: Lovelace\n");

        Assert.NotNull(person);
        Assert.Equal("Ada", person.FirstName);
        Assert.Equal("Lovelace", person.Extra["LastName"]);
    }

    [Fact]
    public void ContractErrors_AreWrappedInYamlExceptionWithContext()
    {
        var options = new YamlSerializerOptions { SourceName = "model.yaml" };

        var ex1 = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<AbstractBase>("X: 1\n", options));
        Assert.Equal("model.yaml", ex1.SourceName);
        Assert.Contains("cannot be instantiated", ex1.Message);

        var value = YamlSerializer.Deserialize<NoDefaultCtor>("Value: 1\n", options);
        Assert.NotNull(value);
        Assert.Equal(1, value.Value);

        var ex2 = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<MultiplePublicCtors>("Value: 1\n", options));
        Assert.Equal("model.yaml", ex2.SourceName);
        Assert.Contains("multiple public constructors", ex2.Message);
    }

    [Fact]
    public void IncludeFields_IncludesPublicFieldsForReadAndWrite()
    {
        var options = new YamlSerializerOptions { IncludeFields = true };

        var yaml = YamlSerializer.Serialize(new PublicFieldsModel { Property = 1, Value = 2, Optional = "field" }, options);
        Assert.Contains("Property: 1", yaml);
        Assert.Contains("Value: 2", yaml);
        Assert.Contains("Optional: field", yaml);
        Assert.Contains("ReadOnlyValue: 5", yaml);

        var result = YamlSerializer.Deserialize<PublicFieldsModel>("Property: 3\nValue: 4\nOptional: test\n", options);

        Assert.NotNull(result);
        Assert.Equal(3, result.Property);
        Assert.Equal(4, result.Value);
        Assert.Equal("test", result.Optional);
    }

    [Fact]
    public void IncludeFields_DoesNotIncludePublicFieldsByDefault()
    {
        var yaml = YamlSerializer.Serialize(new PublicFieldsModel { Property = 1, Value = 2, Optional = "field" });
        Assert.Contains("Property: 1", yaml);
        Assert.DoesNotContain("Value:", yaml);
        Assert.DoesNotContain("Optional:", yaml);

        var result = YamlSerializer.Deserialize<PublicFieldsModel>("Property: 3\nValue: 4\nOptional: test\n");

        Assert.NotNull(result);
        Assert.Equal(3, result.Property);
        Assert.Equal(0, result.Value);
        Assert.Null(result.Optional);
    }

    [Fact]
    public void IgnoreReadOnlyMembers_SkipsReadOnlyMembersDuringSerialization()
    {
        var yaml = YamlSerializer.Serialize(
            new PublicFieldsModel { Property = 1, Value = 2 },
            new YamlSerializerOptions
            {
                IncludeFields = true,
                IgnoreReadOnlyFields = true,
                IgnoreReadOnlyProperties = true,
            });

        Assert.Contains("Property: 1", yaml);
        Assert.Contains("Value: 2", yaml);
        Assert.DoesNotContain("ReadOnlyValue:", yaml);

        var propertyYaml = YamlSerializer.Serialize(
            new ReadOnlyPropertyModel { Mutable = 3 },
            new YamlSerializerOptions { IgnoreReadOnlyProperties = true });
        Assert.Contains("Mutable: 3", propertyYaml);
        Assert.DoesNotContain("ReadOnly:", propertyYaml);
    }

    [Fact]
    public void RejectUnmatchedProperties_DisallowsUnknownMembers()
    {
        var options = new YamlSerializerOptions { RejectUnmatchedProperties = true };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Person>("FirstName: Ada\nLastName: Lovelace\n", options));

        Assert.Contains("LastName", exception.Message);
    }

    [Fact]
    public void RejectUnmatchedProperties_DoesNotConflictWithExtensionData()
    {
        var options = new YamlSerializerOptions { RejectUnmatchedProperties = true };

        var person = YamlSerializer.Deserialize<StrictExtensionDataPerson>("FirstName: Ada\nLastName: Lovelace\n", options);

        Assert.NotNull(person);
        Assert.Equal("Ada", person.FirstName);
        Assert.Equal("Lovelace", person.Extra["LastName"]);
    }

    [Fact]
    public void RespectRequiredConstructorParameters_RequiresNonOptionalParametersByDefault()
    {
        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<ConstructorParametersModel>("Age: 42\n"));

        Assert.Contains("name", exception.Message);
    }

    [Fact]
    public void RespectRequiredConstructorParameters_CanBeDisabled()
    {
        var result = YamlSerializer.Deserialize<ConstructorParametersModel>(
            "Age: 42\n",
            new YamlSerializerOptions { RespectRequiredConstructorParameters = false });

        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.Equal(42, result.Age);

        var missingValueType = YamlSerializer.Deserialize<ConstructorParametersModel>(
            "Name: Ada\n",
            new YamlSerializerOptions { RespectRequiredConstructorParameters = false });

        Assert.NotNull(missingValueType);
        Assert.Equal("Ada", missingValueType.Name);
        Assert.Equal(0, missingValueType.Age);
    }

    [Fact]
    public void RespectNullableAnnotations_RejectsNullDuringDeserializationByDefault()
    {
        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<NullableAnnotationsModel>("Name: null\nOptional: null\n"));

        Assert.Contains("Name", exception.Message);
    }

    [Fact]
    public void RespectNullableAnnotations_CanBeDisabledForDeserialization()
    {
        var result = YamlSerializer.Deserialize<NullableAnnotationsModel>(
            "Name: null\nOptional: null\n",
            new YamlSerializerOptions { RespectNullableAnnotations = false });

        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.Null(result.Optional);
    }

    [Fact]
    public void RespectNullableAnnotations_RejectsNullDuringSerializationByDefault()
    {
        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Serialize(new NullableAnnotationsModel { Name = null! }));

        Assert.Contains("Name", exception.Message);
    }

    [Fact]
    public void RespectNullableAnnotations_CanBeDisabledForSerialization()
    {
        var yaml = YamlSerializer.Serialize(
            new NullableAnnotationsModel { Name = null! },
            new YamlSerializerOptions { RespectNullableAnnotations = false });

        Assert.Contains("Name: null", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MembersNamedLikeKeywords_RoundTrip(bool useSourceGeneration)
    {
        var yaml = Serialize(new ContractKeywordMembersModel { @class = 1, @event = "e" }, useSourceGeneration);
        Assert.Equal("class: 1\nevent: e\n", yaml);

        var result = Deserialize<ContractKeywordMembersModel>("class: 2\nevent: f\n", useSourceGeneration)!;
        Assert.Equal(2, result.@class);
        Assert.Equal("f", result.@event);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaticMembers_AreNotSerialized(bool useSourceGeneration)
    {
        ContractStaticMembersModel.StaticProperty = 5;
        ContractStaticMembersModel.StaticField = 6;
        var options = new YamlSerializerOptions { IncludeFields = true };

        var yaml = useSourceGeneration
            ? YamlSerializer.Serialize(new ContractStaticMembersModel { Value = 1 }, new ContractContext(options))
            : YamlSerializer.Serialize(new ContractStaticMembersModel { Value = 1 }, options);

        Assert.Equal("Value: 1\n", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitInterfaceImplementationWithYamlInclude_RoundTrips(bool useSourceGeneration)
    {
        var yaml = Serialize(new ContractExplicitInterfaceModel(), useSourceGeneration);
        Assert.Equal("Meziantou.Framework.Yaml.Tests.Serialization.IContractExplicitInterface.Value: 3\n", yaml);

        var result = Deserialize<ContractExplicitInterfaceModel>("Meziantou.Framework.Yaml.Tests.Serialization.IContractExplicitInterface.Value: 4\n", useSourceGeneration)!;
        Assert.Equal(4, ((IContractExplicitInterface)result).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructWithInitOnlyProperties_RoundTrips(bool useSourceGeneration)
    {
        var yaml = Serialize(new ContractInitOnlyStruct { Value = 1, Name = "a" }, useSourceGeneration);
        Assert.Equal("Value: 1\nName: a\n", yaml);

        var result = Deserialize<ContractInitOnlyStruct>("Value: 2\nName: b\n", useSourceGeneration);
        Assert.Equal(2, result.Value);
        Assert.Equal("b", result.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MembersSharingAYamlName_Throw(bool useSourceGeneration)
    {
        const string ExpectedMessage = "Members 'First' and 'Second' of 'Meziantou.Framework.Yaml.Tests.Serialization.ContractDuplicateNamesModel' both map to the YAML member name 'same'.";

        var writeException = Assert.Throws<InvalidOperationException>(() => Serialize(new ContractDuplicateNamesModel(), useSourceGeneration));
        Assert.Equal(ExpectedMessage, writeException.Message);

        var readException = Assert.Throws<YamlException>(() => Deserialize<ContractDuplicateNamesModel>("same: 1\n", useSourceGeneration));
        Assert.EndsWith(ExpectedMessage, readException.Message);

        var policyException = Assert.Throws<InvalidOperationException>(() => Serialize(new ContractPolicyCollisionModel(), useSourceGeneration));
        Assert.Equal("Members 'Value' and 'value' of 'Meziantou.Framework.Yaml.Tests.Serialization.ContractPolicyCollisionModel' both map to the YAML member name 'value'.", policyException.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorBoundType_ReadOnlyMemberKey_IsNotUnmapped(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow };
        const string Yaml = "Name: a\nComputed: 3\nCount: 4\n";

        var result = useSourceGeneration
            ? YamlSerializer.Deserialize<ContractConstructorWithReadOnlyMembers>(Yaml, new ContractContext(options))!
            : YamlSerializer.Deserialize<ContractConstructorWithReadOnlyMembers>(Yaml, options)!;

        Assert.Equal("a", result.Name);
        Assert.Equal(1, result.Computed);

        var exception = useSourceGeneration
            ? Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<ContractConstructorWithReadOnlyMembers>("Name: a\n", new ContractContext(options)))
            : Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<ContractConstructorWithReadOnlyMembers>("Name: a\n", options));
        Assert.Contains("Missing required members for", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PopulateReadOnlyValueTypeMember_ThrowsTheSameMessage(bool useSourceGeneration)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Deserialize<ContractPopulateReadOnlyStructModel>("Inner: {A: 5}\n", useSourceGeneration));

        Assert.Equal("Property 'Inner' on type 'Meziantou.Framework.Yaml.Tests.Serialization.ContractPopulateReadOnlyStructModel' is marked with YamlObjectCreationHandling.Populate but is a value type that doesn't have a setter.", exception.Message);
    }

    [Theory]
    [InlineData(false, "Value: ~\n", "Invalid integer scalar '~'.")]
    [InlineData(false, "Field: ~\n", "Invalid integer scalar '~'.")]
    [InlineData(false, "Flag: ~\n", "Invalid boolean scalar '~'.")]
    [InlineData(false, "Point: ~\n", "Expected a StartMapping token but found 'Scalar'.")]
    [InlineData(false, "<<: { Value: ~ }\n", "Invalid integer scalar '~'.")]
    [InlineData(true, "Value: ~\n", "Invalid integer scalar '~'.")]
    [InlineData(true, "Field: ~\n", "Invalid integer scalar '~'.")]
    [InlineData(true, "Flag: ~\n", "Invalid boolean scalar '~'.")]
    [InlineData(true, "Point: ~\n", "Expected a StartMapping token but found 'Scalar'.")]
    [InlineData(true, "<<: { Value: ~ }\n", "Invalid integer scalar '~'.")]
    public void NullScalar_IsRejectedByNonNullableValueTypeMembers(bool useSourceGeneration, string yaml, string expectedMessage)
    {
        var options = new YamlSerializerOptions { IncludeFields = true };
        var populateOptions = options with { PreferredObjectCreationHandling = YamlObjectCreationHandling.Populate };

        var exception = Assert.Throws<YamlException>(() => Deserialize<ContractNullValueTypeModel>(yaml, useSourceGeneration, options));
        var populateException = Assert.Throws<YamlException>(() => Deserialize<ContractNullValueTypeModel>(yaml, useSourceGeneration, populateOptions));
        var initOnlyException = Assert.Throws<YamlException>(() => Deserialize<ContractNullValueTypeInitOnlyModel>(yaml, useSourceGeneration, options));

        Assert.EndsWith(expectedMessage, exception.Message);
        Assert.EndsWith(expectedMessage, populateException.Message);
        Assert.EndsWith(expectedMessage, initOnlyException.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullScalar_RootAndCollectionStructs_AreRejected(bool useSourceGeneration)
    {
        Assert.Throws<YamlException>(() => Deserialize<ContractNullPoint>("~", useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<List<ContractNullPoint>>("[~]", useSourceGeneration));
        Assert.Null(Deserialize<ContractNullPoint?>("~", useSourceGeneration));
    }

    [Theory]
    [InlineData(false, YamlObjectCreationHandling.Replace)]
    [InlineData(false, YamlObjectCreationHandling.Populate)]
    [InlineData(true, YamlObjectCreationHandling.Replace)]
    [InlineData(true, YamlObjectCreationHandling.Populate)]
    public void NullScalar_AssignsNullToMembersAcceptingIt_UnlessACustomConverterReadsIt(bool useSourceGeneration, YamlObjectCreationHandling objectCreationHandling)
    {
        var options = new YamlSerializerOptions { PreferredObjectCreationHandling = objectCreationHandling };

        var model = Deserialize<ContractNullReferenceModel>("Node: ~\nNumber: ~\nConverted: ~\n", useSourceGeneration, options)!;
        var record = Deserialize<ContractNullReferenceRecord>("Value: 1\nNode: ~\nConverted: ~\n", useSourceGeneration, options)!;

        Assert.Null(model.Node);
        Assert.Null(model.Number);
        Assert.Equal("converted:~", model.Converted);
        Assert.Null(record.Node);
        Assert.Equal("converted:~", record.Converted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Populate_GetOnlyMemberOfTypeWithInitOnlyOrRequiredMembers_IsPopulated(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { PreferredObjectCreationHandling = YamlObjectCreationHandling.Populate };

        var required = Deserialize<ContractPopulateRequiredModel>("Name: a\nItems: [3]\n", useSourceGeneration, options)!;
        var initOnly = Deserialize<ContractPopulateInitOnlyModel>("Name: a\nItems: [3]\n", useSourceGeneration)!;

        Assert.Equal("a", required.Name);
        Assert.Equal([1, 3], required.Items);
        Assert.Equal("a", initOnly.Name);
        Assert.Equal([1, 3], initOnly.Items);
        Assert.Throws<YamlException>(() => Deserialize<ContractPopulateRequiredModel>("Items: [3]\n", useSourceGeneration, options));
    }

    [Theory]
    [InlineData(false, "Name: x\nOther: 1\n", "(Lin: 1, Col: 0, Chr: 8) - (Lin: 1, Col: 5, Chr: 13)")]
    [InlineData(false, "Name: x\n<<: { Other: 1 }\n", "(Lin: 1, Col: 6, Chr: 14) - (Lin: 1, Col: 11, Chr: 19)")]
    [InlineData(true, "Name: x\nOther: 1\n", "(Lin: 1, Col: 0, Chr: 8) - (Lin: 1, Col: 5, Chr: 13)")]
    [InlineData(true, "Name: x\n<<: { Other: 1 }\n", "(Lin: 1, Col: 6, Chr: 14) - (Lin: 1, Col: 11, Chr: 19)")]
    public void GetOnlyNullExtensionData_ThrowsAtTheKey(bool useSourceGeneration, string yaml, string expectedPosition)
    {
        var mapping = Assert.Throws<YamlException>(() => Deserialize<ContractGetOnlyMappingExtensionDataModel>(yaml, useSourceGeneration));
        var dictionary = Assert.Throws<YamlException>(() => Deserialize<ContractGetOnlyDictionaryExtensionDataModel>(yaml, useSourceGeneration));
        var record = Assert.Throws<YamlException>(() => Deserialize<ContractGetOnlyExtensionDataRecord>(yaml, useSourceGeneration));

        Assert.Equal(expectedPosition + ": Extension data member 'Extra' could not be assigned on 'Meziantou.Framework.Yaml.Tests.Serialization.ContractGetOnlyMappingExtensionDataModel'.", mapping.Message);
        Assert.Equal(expectedPosition + ": Extension data member 'Extra' could not be assigned on 'Meziantou.Framework.Yaml.Tests.Serialization.ContractGetOnlyDictionaryExtensionDataModel'.", dictionary.Message);
        Assert.Equal(expectedPosition + ": Extension data member 'Extra' could not be assigned on 'Meziantou.Framework.Yaml.Tests.Serialization.ContractGetOnlyExtensionDataRecord'.", record.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructWithYamlConstructor_UsesTheConstructor(bool useSourceGeneration)
    {
        var value = Deserialize<ContractConstructorStruct>("X: 1\nName: a\nExtra: 2\n", useSourceGeneration);
        var privateConstructor = Deserialize<ContractPrivateConstructorStruct>("X: 3\n", useSourceGeneration);
        var holder = Deserialize<ContractConstructorStructHolder>("Value: { X: 4, Name: b }\nNullable: { X: 5, Name: c }\nItems: [{ X: 6, Name: d }]\n", useSourceGeneration)!;

        Assert.Equal(1, value.X);
        Assert.Equal("a", value.Name);
        Assert.Equal(2, value.Extra);
        Assert.Equal(3, privateConstructor.X);
        Assert.Equal(4, holder.Value.X);
        Assert.Equal(5, holder.Nullable!.Value.X);
        Assert.Equal("d", Assert.Single(holder.Items!).Name);
        var exception = Assert.Throws<YamlException>(() => Deserialize<ContractConstructorStruct>("Name: a\n", useSourceGeneration));
        Assert.EndsWith("Missing required constructor parameter 'x' for 'Meziantou.Framework.Yaml.Tests.Serialization.ContractConstructorStruct'.", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorParameter_IsReadWithTheConverterOfItsMember(bool useSourceGeneration)
    {
        var value = new ContractConstructorConverterRecord("abc", 5);

        var yaml = Serialize(value, useSourceGeneration);
        var result = Deserialize<ContractConstructorConverterRecord>(yaml, useSourceGeneration)!;
        var merged = Deserialize<ContractConstructorConverterRecord>("<<: { Text: '>abc', Number: 'NaN' }\n", useSourceGeneration)!;

        Assert.Contains(">abc", yaml);
        Assert.Equal(value, result);
        Assert.Equal("abc", merged.Text);
        Assert.True(double.IsNaN(merged.Number));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorParameterDefaultValues_AreUsed(bool useSourceGeneration)
    {
        var value = Deserialize<ContractConstructorDefaultsRecord>("{}", useSourceGeneration)!;

        Assert.Equal(2.5m, value.Amount);
        Assert.Equal(1.5f, value.Ratio);
        Assert.True(double.IsNaN(value.Missing));
        Assert.Equal('\n', value.Separator);
        Assert.Equal(ContractSignedEnum.Negative, value.Signed);
        Assert.Equal(ContractSignedEnum.Positive, value.NullableSigned);
        Assert.Equal(DayOfWeek.Friday, value.Day);
    }

    private static string Serialize<T>(T value, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Serialize(value, ContractContext.Default)
            : YamlSerializer.Serialize(value);

    private static T? Deserialize<T>(string yaml, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, ContractContext.Default)
            : YamlSerializer.Deserialize<T>(yaml);

    private static T? Deserialize<T>(string yaml, bool useSourceGeneration, YamlSerializerOptions options)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, new ContractContext(options))
            : YamlSerializer.Deserialize<T>(yaml, options);
}

#pragma warning disable IDE1006 // Naming Styles: the members are named like C# keywords on purpose
internal sealed class ContractKeywordMembersModel
{
    public int @class { get; set; }

    public string? @event { get; set; }
}
#pragma warning restore IDE1006

internal sealed class ContractStaticMembersModel
{
    public const int Constant = 1;

    public static int StaticField;

    public static int StaticProperty { get; set; }

    public int Value { get; set; }
}

internal interface IContractExplicitInterface
{
    int Value { get; set; }
}

internal sealed class ContractExplicitInterfaceModel : IContractExplicitInterface
{
    [YamlInclude]
    int IContractExplicitInterface.Value { get; set; } = 3;
}

internal readonly struct ContractInitOnlyStruct
{
    public int Value { get; init; }

    public string? Name { get; init; }
}

internal sealed class ContractDuplicateNamesModel
{
    [YamlPropertyName("same")]
    public int First { get; set; }

    [YamlPropertyName("same")]
    public int Second { get; set; }
}

[YamlNamingPolicy(YamlKnownNamingPolicy.CamelCase)]
internal sealed class ContractPolicyCollisionModel
{
    public int Value { get; set; }

#pragma warning disable IDE1006 // Naming Styles: the member collides with 'Value' once the naming policy applies
    public int value { get; set; }
#pragma warning restore IDE1006
}

internal sealed class ContractConstructorWithReadOnlyMembers
{
    public ContractConstructorWithReadOnlyMembers(string name) => Name = name;

    public string Name { get; }

    public int Computed => Name.Length;

    [YamlRequired]
    public int Count => Name.Length + 1;
}

internal struct ContractPopulateStruct
{
    public int A { get; set; }
}

internal sealed class ContractPopulateReadOnlyStructModel
{
    [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
    public ContractPopulateStruct Inner { get; } = new() { A = 1 };
}

internal struct ContractNullPoint
{
    public int X { get; set; }
}

internal sealed class ContractNullValueTypeModel
{
    public int Field = 5;

    public int Value { get; set; } = 5;

    public bool Flag { get; set; } = true;

    public ContractNullPoint Point { get; set; } = new() { X = 5 };
}

internal sealed class ContractNullValueTypeInitOnlyModel
{
    public int Field = 5;

    public int Value { get; init; } = 5;

    public required bool Flag { get; init; } = true;

    public ContractNullPoint Point { get; init; } = new() { X = 5 };
}

internal sealed class ContractPrefixConverter : YamlConverter<string?>
{
    public override string? Read(YamlReader reader)
    {
        var value = "converted:" + reader.ScalarValue;
        reader.Read();
        return value;
    }

    public override void Write(YamlWriter writer, string? value) => writer.WriteString(value);
}

internal sealed class ContractNullReferenceModel
{
    public YamlNode? Node { get; set; } = new YamlValue("initial");

    public int? Number { get; set; } = 1;

    [YamlConverter(typeof(ContractPrefixConverter))]
    public string? Converted { get; set; } = "initial";
}

internal sealed record ContractNullReferenceRecord(int Value)
{
    public YamlNode? Node { get; set; } = new YamlValue("initial");

    [YamlConverter(typeof(ContractPrefixConverter))]
    public string? Converted { get; set; } = "initial";
}

internal sealed class ContractPopulateRequiredModel
{
    public required string Name { get; set; }

    public List<int> Items { get; } = [1];
}

internal sealed class ContractPopulateInitOnlyModel
{
    public string? Name { get; init; }

    [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
    public List<int> Items { get; } = [1];
}

internal sealed class ContractGetOnlyMappingExtensionDataModel
{
    public string? Name { get; set; }

    [YamlExtensionData]
    public YamlMapping? Extra { get; }
}

internal sealed class ContractGetOnlyDictionaryExtensionDataModel
{
    public string? Name { get; init; }

    [YamlExtensionData]
    public Dictionary<string, object?>? Extra { get; }
}

internal sealed record ContractGetOnlyExtensionDataRecord(string Name)
{
    [YamlExtensionData]
    public Dictionary<string, object?>? Extra { get; }
}

internal struct ContractConstructorStruct
{
    [YamlConstructor]
    public ContractConstructorStruct(int x, string name)
    {
        X = x;
        Name = name;
    }

    public int X { get; }

    public string Name { get; }

    public int Extra { get; set; }
}

internal readonly struct ContractPrivateConstructorStruct
{
#pragma warning disable IDE0051 // Remove unused private members
    [YamlConstructor]
    private ContractPrivateConstructorStruct(int x) => X = x;
#pragma warning restore IDE0051 // Remove unused private members

    public int X { get; }
}

internal sealed class ContractConstructorStructHolder
{
    public ContractConstructorStruct Value { get; set; }

    public ContractConstructorStruct? Nullable { get; set; }

    public List<ContractConstructorStruct>? Items { get; set; }
}

internal sealed class ContractQuotePrefixConverter : YamlConverter<string?>
{
    public override string? Read(YamlReader reader)
    {
        var value = reader.ScalarValue?.TrimStart('>');
        reader.Read();
        return value;
    }

    public override void Write(YamlWriter writer, string? value) => writer.WriteString(">" + value);
}

internal sealed record ContractConstructorConverterRecord(
    [property: YamlConverter(typeof(ContractQuotePrefixConverter))] string Text,
    [property: YamlNumberHandling(YamlNumberHandling.WriteAsString | YamlNumberHandling.AllowNamedFloatingPointLiterals)] double Number);

internal enum ContractSignedEnum
{
    Negative = -1,
    Positive = 1,
}

internal sealed record ContractConstructorDefaultsRecord(
    decimal Amount = 2.5m,
    float Ratio = 1.5f,
    double Missing = double.NaN,
    char Separator = '\n',
    ContractSignedEnum Signed = ContractSignedEnum.Negative,
    ContractSignedEnum? NullableSigned = ContractSignedEnum.Positive,
    DayOfWeek Day = DayOfWeek.Friday);

[YamlSerializable(typeof(ContractNullValueTypeModel))]
[YamlSerializable(typeof(ContractNullValueTypeInitOnlyModel))]
[YamlSerializable(typeof(ContractNullPoint))]
[YamlSerializable(typeof(ContractNullPoint?))]
[YamlSerializable(typeof(List<ContractNullPoint>))]
[YamlSerializable(typeof(ContractNullReferenceModel))]
[YamlSerializable(typeof(ContractNullReferenceRecord))]
[YamlSerializable(typeof(ContractPopulateRequiredModel))]
[YamlSerializable(typeof(ContractPopulateInitOnlyModel))]
[YamlSerializable(typeof(ContractGetOnlyMappingExtensionDataModel))]
[YamlSerializable(typeof(ContractGetOnlyDictionaryExtensionDataModel))]
[YamlSerializable(typeof(ContractGetOnlyExtensionDataRecord))]
[YamlSerializable(typeof(ContractConstructorStruct))]
[YamlSerializable(typeof(ContractPrivateConstructorStruct))]
[YamlSerializable(typeof(ContractConstructorStructHolder))]
[YamlSerializable(typeof(ContractConstructorConverterRecord))]
[YamlSerializable(typeof(ContractConstructorDefaultsRecord))]
[YamlSerializable(typeof(ContractConstructorWithReadOnlyMembers))]
[YamlSerializable(typeof(ContractPopulateReadOnlyStructModel))]
[YamlSerializable(typeof(ContractKeywordMembersModel))]
[YamlSerializable(typeof(ContractStaticMembersModel))]
[YamlSerializable(typeof(ContractExplicitInterfaceModel))]
[YamlSerializable(typeof(ContractInitOnlyStruct))]
[YamlSerializable(typeof(ContractDuplicateNamesModel))]
[YamlSerializable(typeof(ContractPolicyCollisionModel))]
internal sealed partial class ContractContext : YamlSerializerContext
{
    public ContractContext()
    {
    }

    public ContractContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}
