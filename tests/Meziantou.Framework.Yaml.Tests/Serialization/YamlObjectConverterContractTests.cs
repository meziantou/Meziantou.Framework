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
}
