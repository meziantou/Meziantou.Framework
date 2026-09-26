using System.Collections.Generic;
using System.Text.Json.Serialization;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Tomlyn.Tests;

public class NewApiReflectionPocoTests
{
    private sealed class CallbackModel : ITomlOnSerializing, ITomlOnSerialized, ITomlOnDeserializing, ITomlOnDeserialized
    {
        public string Name { get; set; } = "";

        [TomlIgnore]
        public int OnSerializingCount { get; private set; }

        [TomlIgnore]
        public int OnSerializedCount { get; private set; }

        [TomlIgnore]
        public int OnDeserializingCount { get; private set; }

        [TomlIgnore]
        public int OnDeserializedCount { get; private set; }

        [TomlIgnore]
        public bool NameWasAlreadyAssignedInOnDeserializing { get; private set; }

        [TomlIgnore]
        public string? NameSeenInOnDeserialized { get; private set; }

        public void OnTomlSerializing() => OnSerializingCount++;

        public void OnTomlSerialized() => OnSerializedCount++;

        public void OnTomlDeserializing()
        {
            OnDeserializingCount++;
            NameWasAlreadyAssignedInOnDeserializing = Name == "Ada";

            // If this callback runs before TOML member mapping, this should be overwritten by TOML data.
            Name = "from-callback";
        }

        public void OnTomlDeserialized()
        {
            OnDeserializedCount++;
            NameSeenInOnDeserialized = Name;
        }
    }

    private sealed class Person
    {
        public string Name { get; set; } = "";

        public long Age { get; set; }
    }

    private sealed class JsonNamedPerson
    {
        [JsonPropertyName("first_name")]
        public string Name { get; set; } = "";

        public long Age { get; set; }
    }

    private abstract class JsonNamedBaseOptions
    {
        [JsonPropertyName("baseValue")]
        public string? Base { get; init; }
    }

    private sealed class JsonNamedDerivedOptions : JsonNamedBaseOptions
    {
        [JsonPropertyName("derivedValue")]
        public string? Derived { get; init; }
    }

    private abstract class JsonNamedOverriddenBaseOptions
    {
        [JsonPropertyName("baseValue")]
        public virtual string? Base { get; init; }
    }

    private sealed class JsonNamedOverriddenDerivedOptions : JsonNamedOverriddenBaseOptions
    {
        [JsonPropertyName("derivedValue")]
        public string? Derived { get; init; }

        public override string? Base { get; init; }
    }

    private sealed class PrivateSetterModel
    {
        [JsonInclude]
        public int Value { get; private set; }
    }

    private sealed class IncludedFieldModel
    {
        [JsonInclude]
        public int Value;
    }

    private sealed class IncludedNonPublicPropertyModel
    {
        [TomlInclude]
        private bool MyProperty { get; set; } = true;

        public bool GetMyProperty() => MyProperty;
    }

    private sealed class StringEnumModel
    {
        public List<StringEnumValue> Values { get; set; } = new();
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    private enum StringEnumValue
    {
        First,
        Second,
    }

    private sealed class ReadOnlyPropertyModel
    {
        public int Value { get; } = 42;
    }

    private sealed class JsonConstructorModel
    {
        public JsonConstructorModel(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    private sealed class AnnotatedJsonConstructorModel
    {
        public AnnotatedJsonConstructorModel()
        {
            Value = -1;
        }

        [JsonConstructor]
        public AnnotatedJsonConstructorModel(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    private sealed class SingleCtorModel
    {
        public SingleCtorModel(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    private sealed class RequiredModel
    {
        [JsonRequired]
        public int Value { get; set; }
    }

    private sealed class EmptyModel
    {
    }

    private sealed class TomlObjectModel
    {
        public TomlObject? Value { get; set; }
    }

    private sealed class MultipleAnnotatedConstructorsModel
    {
        [JsonConstructor]
        public MultipleAnnotatedConstructorsModel(int value)
        {
            Value = value;
        }

        [TomlConstructor]
        public MultipleAnnotatedConstructorsModel(string value)
        {
            Value = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        public int Value { get; }
    }

    private sealed class AmbiguousConstructorsModel
    {
        public AmbiguousConstructorsModel(int value)
        {
            Value = value;
        }

        public AmbiguousConstructorsModel(string value)
        {
            Value = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        public int Value { get; }
    }

    [Fact]
    public void SerializeDeserialize_Poco_UsesReflectionFallback()
    {
        var original = new Person { Name = "Ada", Age = 37 };
        var toml = TomlSerializer.Serialize(original);
        var roundtrip = TomlSerializer.Deserialize<Person>(toml);

        Assert.NotNull(roundtrip);
        Assert.Equal("Ada", roundtrip!.Name);
        Assert.Equal(37, roundtrip.Age);
    }

    [Fact]
    public void Deserialize_Poco_RespectsJsonPropertyName()
    {
        var toml = """
            first_name = "Ada"
            Age = 37
            """;

        var person = TomlSerializer.Deserialize<JsonNamedPerson>(toml);

        Assert.NotNull(person);
        Assert.Equal("Ada", person!.Name);
        Assert.Equal(37, person.Age);
    }

    [Fact]
    public void SerializeDeserialize_InheritedProperties_RespectJsonPropertyName()
    {
        var original = new JsonNamedDerivedOptions
        {
            Base = "shared",
            Derived = "leaf",
        };

        var toml = TomlSerializer.Serialize(original);
        var roundtrip = TomlSerializer.Deserialize<JsonNamedDerivedOptions>(toml);

        Assert.Contains("baseValue = \"shared\"", toml);
        Assert.Contains("derivedValue = \"leaf\"", toml);
        Assert.NotNull(roundtrip);
        Assert.Equal("shared", roundtrip!.Base);
        Assert.Equal("leaf", roundtrip.Derived);
    }

    [Fact]
    public void SerializeDeserialize_OverriddenProperties_RespectInheritedJsonPropertyName()
    {
        var original = new JsonNamedOverriddenDerivedOptions
        {
            Base = "shared",
            Derived = "leaf",
        };

        var toml = TomlSerializer.Serialize(original);
        var roundtrip = TomlSerializer.Deserialize<JsonNamedOverriddenDerivedOptions>(toml);

        Assert.Contains("baseValue = \"shared\"", toml);
        Assert.Contains("derivedValue = \"leaf\"", toml);
        Assert.NotNull(roundtrip);
        Assert.Equal("shared", roundtrip!.Base);
        Assert.Equal("leaf", roundtrip.Derived);
    }

    [Fact]
    public void Deserialize_PrivateSetter_WithJsonInclude_Works()
    {
        var model = TomlSerializer.Deserialize<PrivateSetterModel>("Value = 123\n");
        Assert.NotNull(model);
        Assert.Equal(123, model!.Value);
    }

    [Fact]
    public void SerializeDeserialize_PublicField_WithJsonInclude_Works()
    {
        var original = new IncludedFieldModel { Value = 7 };
        var toml = TomlSerializer.Serialize(original);
        var roundtrip = TomlSerializer.Deserialize<IncludedFieldModel>(toml);
        Assert.NotNull(roundtrip);
        Assert.Equal(7, roundtrip!.Value);
    }

    [Fact]
    public void SerializeDeserialize_NonPublicProperty_WithTomlInclude_Works()
    {
        var toml = TomlSerializer.Serialize(new IncludedNonPublicPropertyModel());
        var roundtrip = TomlSerializer.Deserialize<IncludedNonPublicPropertyModel>("MyProperty = false\n");

        Assert.Contains("MyProperty = true", toml);
        Assert.NotNull(roundtrip);
        Assert.False(roundtrip!.GetMyProperty());
    }

    [Fact]
    public void Serialize_ReadOnlyProperty_IsIncluded()
    {
        var toml = TomlSerializer.Serialize(new ReadOnlyPropertyModel());
        Assert.Contains("Value", toml);
        Assert.Contains("42", toml);
    }

    [Fact]
    public void SerializeDeserialize_EnumWithJsonStringEnumConverter_UsesStrings()
    {
        var original = new StringEnumModel { Values = [StringEnumValue.First, StringEnumValue.Second] };
        var toml = TomlSerializer.Serialize(original);
        var roundtrip = TomlSerializer.Deserialize<StringEnumModel>(toml);

        Assert.Contains("Values = [\"First\", \"Second\"]", toml);
        Assert.NotNull(roundtrip);
        Assert.Equal(original.Values, roundtrip!.Values);
    }

    [Fact]
    public void Deserialize_UsesSinglePublicConstructor_WhenNoParameterlessExists()
    {
        var model = TomlSerializer.Deserialize<SingleCtorModel>("Name = \"Ada\"\n");
        Assert.NotNull(model);
        Assert.Equal("Ada", model!.Name);
    }

    [Fact]
    public void Deserialize_BindsConstructorParameter_ByMemberName()
    {
        var model = TomlSerializer.Deserialize<JsonConstructorModel>("Value = 5\n");
        Assert.NotNull(model);
        Assert.Equal(5, model!.Value);
    }

    [Fact]
    public void Deserialize_UsesJsonConstructor_WhenAnnotated()
    {
        var model = TomlSerializer.Deserialize<AnnotatedJsonConstructorModel>("Value = 6\n");
        Assert.NotNull(model);
        Assert.Equal(6, model!.Value);
    }

    [Fact]
    public void Deserialize_ThrowsWhenRequiredMemberMissing()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<RequiredModel>("Other = 1\n"));
    }

    [Fact]
    public void SerializeDeserialize_EmptyPoco_NoMembers_Works()
    {
        var toml = TomlSerializer.Serialize(new EmptyModel());
        var model = TomlSerializer.Deserialize<EmptyModel>(toml);
        Assert.NotNull(model);
    }

    [Fact]
    public void SerializeDeserialize_TomlObjectProperty_UsesRuntimeModelNode()
    {
        var arrayModel = TomlSerializer.Deserialize<TomlObjectModel>("Value = [1, 2]\n");

        Assert.NotNull(arrayModel);
        Assert.IsAssignableTo<TomlArray>(arrayModel!.Value);
        Assert.Equal(new TomlArray { 1L, 2L }, (TomlArray)arrayModel.Value!);

        var tableModel = new TomlObjectModel
        {
            Value = new TomlTable { ["Answer"] = 42L },
        };

        var toml = TomlSerializer.Serialize(tableModel);
        var roundtrip = TomlSerializer.Deserialize<TomlObjectModel>(toml);

        Assert.Contains("[Value]", toml);
        Assert.NotNull(roundtrip);
        Assert.IsAssignableTo<TomlTable>(roundtrip!.Value);
        Assert.Equal(42L, ((TomlTable)roundtrip.Value!)["Answer"]);
    }

    [Fact]
    public void Deserialize_MultipleAnnotatedConstructors_ThrowsTomlException()
    {
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<MultipleAnnotatedConstructorsModel>("Value = 1\n"));
        Assert.Contains("Multiple constructors", ex!.Message);
    }

    [Fact]
    public void Deserialize_AmbiguousConstructors_ThrowsTomlException()
    {
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<AmbiguousConstructorsModel>("Value = 1\n"));
        Assert.Contains("No suitable constructor", ex!.Message);
    }

    [Fact]
    public void SerializeDeserialize_LifecycleCallbacks_AreInvoked()
    {
        var original = new CallbackModel { Name = "Ada" };
        var toml = TomlSerializer.Serialize(original);

        Assert.Equal(1, original.OnSerializingCount);
        Assert.Equal(1, original.OnSerializedCount);
        Assert.Equal(0, original.OnDeserializingCount);
        Assert.Equal(0, original.OnDeserializedCount);

        var roundtrip = TomlSerializer.Deserialize<CallbackModel>(toml);

        Assert.NotNull(roundtrip);
        Assert.Equal("Ada", roundtrip!.Name);
        Assert.Equal(1, roundtrip.OnDeserializingCount);
        Assert.Equal(1, roundtrip.OnDeserializedCount);
        Assert.False(roundtrip.NameWasAlreadyAssignedInOnDeserializing);
        Assert.Equal("Ada", roundtrip.NameSeenInOnDeserialized);
    }
}
