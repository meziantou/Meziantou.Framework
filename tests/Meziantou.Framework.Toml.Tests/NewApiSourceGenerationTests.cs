using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

#pragma warning disable CA1002 // Test models use List<T> on purpose
#pragma warning disable CA1040 // Test models use empty interfaces on purpose
#pragma warning disable MA0048 // File name must match type name

public sealed class GeneratedPerson
{
    public string Name { get; set; } = "";

    public long Age { get; set; }
}

public sealed class GeneratedIntPerson
{
    public int Age { get; set; }
}

public sealed class GeneratedSnakePerson
{
    public string FirstName { get; set; } = "";
}

public abstract class GeneratedBaseOptions
{
    [JsonPropertyName("baseValue")]
    public string? Base { get; init; }
}

public sealed class GeneratedDerivedOptions : GeneratedBaseOptions
{
    [JsonPropertyName("derivedValue")]
    public string? Derived { get; init; }
}

public abstract class GeneratedOverriddenBaseOptions
{
    [JsonPropertyName("baseValue")]
    public virtual required string? Base { get; init; }
}

public sealed class GeneratedOverriddenDerivedOptions : GeneratedOverriddenBaseOptions
{
    [JsonPropertyName("derivedValue")]
    public string? Derived { get; init; }

    public override required string? Base { get; init; }
}

public sealed class GeneratedOptionsPerson
{
    public string Name { get; set; } = "";

    public long Age { get; set; }
}

public sealed class GeneratedConvertedScalar
{
    public string Text { get; set; } = "";
}

public sealed class GeneratedConvertedScalarHolder
{
    public GeneratedConvertedScalar Value { get; set; } = new();
}

public sealed class GeneratedRuntimeConverterHolder
{
    public Guid Id { get; set; }

    public Guid? OptionalId { get; set; }
}

public sealed class GeneratedTransitiveConfig
{
    public GeneratedTransitiveBar? Foo { get; set; }

    public IReadOnlyList<GeneratedTransitiveBar> Bars { get; set; } = [];

    public List<List<GeneratedTransitiveBaz>> Matrix { get; set; } = [];

    public Dictionary<string, GeneratedTransitiveBaz> BazByName { get; set; } = [];

    public object Dynamic { get; set; } = new TomlArray();

    public List<object> DynamicList { get; set; } = [];

    public Dictionary<string, object> DynamicMap { get; set; } = [];
}

public sealed class GeneratedTransitiveBar
{
    public GeneratedTransitiveBaz? Baz { get; set; }
}

public sealed class GeneratedTransitiveBaz
{
    public string Value { get; set; } = "";
}

public sealed class GeneratedOrderedPerson
{
    [JsonPropertyOrder(-10)]
    public int Age { get; set; }

    [JsonPropertyOrder(10)]
    public string Name { get; set; } = "";
}

public sealed class GeneratedRequiredPerson
{
    [JsonRequired]
    public string Name { get; set; } = "";

    public int Age { get; set; }
}

public sealed class GeneratedInitOnlyPerson
{
    public string Name { get; init; } = "";

    public int Age { get; init; } = 42;
}

public sealed class GeneratedRequiredInitPerson
{
    public required string Name { get; init; } = "";

    public int Age { get; init; } = 42;
}

public sealed class GeneratedCtorInitRequiredPerson
{
    [JsonConstructor]
    public GeneratedCtorInitRequiredPerson(string name)
    {
        Name = name;
    }

    public required string Name { get; init; } = "";

    public int Age { get; init; } = 42;
}

public sealed class GeneratedExtensionDataPerson
{
    public string Name { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }
}

public sealed class GeneratedCtorPerson
{
    [JsonConstructor]
    public GeneratedCtorPerson(string firstName, int age = 42)
    {
        FirstName = firstName;
        Age = age;
    }

    [JsonPropertyName("first_name")]
    public string FirstName { get; }

    public int Age { get; }
}

public sealed class GeneratedCtorSelectionPerson
{
    public GeneratedCtorSelectionPerson()
    {
        Name = "default";
    }

    [JsonConstructor]
    public GeneratedCtorSelectionPerson(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(GeneratedCat), "cat")]
[JsonDerivedType(typeof(GeneratedDog), "dog")]
public interface IGeneratedAnimal
{
}

public sealed class GeneratedCat : IGeneratedAnimal
{
    public string Name { get; set; } = "";

    public int Lives { get; set; }
}

public sealed class GeneratedDog : IGeneratedAnimal
{
    public string Name { get; set; } = "";

    public bool GoodBoy { get; set; }
}

public sealed class GeneratedTwoLevelPolymorphicConfig
{
    public required GeneratedDepartLevel Depart { get; set; }
}

[TomlPolymorphic]
public abstract class GeneratedDepartLevel
{
    public required string Name { get; set; }

    public required GeneratedGroupLevel Group { get; set; }
}

public sealed class GeneratedDepart1 : GeneratedDepartLevel
{
    public required string Field1 { get; set; }
}

[TomlPolymorphic]
public abstract class GeneratedGroupLevel
{
    public required string Name { get; set; }
}

public sealed class GeneratedGroup2 : GeneratedGroupLevel
{
    public required string Field2 { get; set; }
}

public sealed class GeneratedCallbackPerson : ITomlOnSerializing, ITomlOnSerialized, ITomlOnDeserializing, ITomlOnDeserialized
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
        Name = "from-callback";
    }

    public void OnTomlDeserialized()
    {
        OnDeserializedCount++;
        NameSeenInOnDeserialized = Name;
    }
}

public sealed class GeneratedCollectionsPayload
{
    public ImmutableArray<int> Numbers { get; set; }

    public ImmutableList<string>? Names { get; set; }

    public ImmutableHashSet<int>? Ids { get; set; }

    public ISet<string>? Tags { get; set; }

    public HashSet<int>? Values { get; set; }
}

public sealed class GeneratedNullablePayload
{
    public int? Count { get; set; }

    public DateTimeOffset? When { get; set; }
}

public sealed class GeneratedNullableReferencePayload
{
    public string? NullableMock { get; init; }

    public string NonNullableMock { get; init; } = string.Empty;
}

public sealed class GeneratedIncludedInternalPropertyPayload
{
    [TomlInclude]
    internal bool MyProperty { get; set; } = true;
}

public sealed class GeneratedIncludedPrivatePropertyPayload
{
    [TomlInclude]
#pragma warning disable IDE0051 // Used by the serializer
    private bool MyProperty { get; set; } = true;
#pragma warning restore IDE0051
}

public sealed class GeneratedIncludedNonPublicSetterPayload
{
    [TomlInclude]
    public int PrivateSet { get; private set; }

    [TomlInclude]
#pragma warning disable IDE0044, CS0649 // Set by the serializer
    private int _privateField;
#pragma warning restore IDE0044, CS0649

    [TomlInclude]
    private int PrivateProperty { get; set; }

    public int GetPrivateField() => _privateField;

    public int GetPrivateProperty() => PrivateProperty;
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public struct GeneratedIncludedNonPublicSetterStruct
{
    [TomlInclude]
    public int PrivateSet { get; private set; }

    [TomlInclude]
#pragma warning disable IDE0044, CS0649 // Set by the serializer
    private int _privateField;
#pragma warning restore IDE0044, CS0649

    public readonly int GetPrivateField() => _privateField;
}

public sealed class GeneratedIncludedConstructorPayload
{
    public GeneratedIncludedConstructorPayload(string name) => Name = name;

    public string Name { get; }

    [TomlInclude]
    public int Count { get; private set; }
}

public sealed class GeneratedRequiredModifierModel
{
    public required string Name { get; set; }

    public int Age { get; set; }
}

public sealed class GeneratedSetsRequiredMembersModel
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public GeneratedSetsRequiredMembersModel()
    {
        Name = "default";
    }

    public required string Name { get; set; }

    public int Age { get; set; }
}

#pragma warning disable IDE1006 // The test needs members named like keywords
public sealed record GeneratedKeywordRecord(string @class, int @base)
{
    public string? @event { get; set; }
}
#pragma warning restore IDE1006

#pragma warning disable CA1051 // The test needs public fields
public class GeneratedOrderBase
{
    public int BaseProperty { get; set; } = 1;

    public int BaseField = 2;
}

public sealed class GeneratedOrderDerived : GeneratedOrderBase
{
    public int DerivedField = 4;

    public int DerivedProperty { get; set; } = 3;
}
#pragma warning restore CA1051

[TomlSourceGenerationOptions(IncludeFields = true)]
[TomlSerializable(typeof(GeneratedOrderDerived))]
internal sealed partial class TestTomlSerializerContextMemberOrder : TomlSerializerContext
{
}

public sealed class GeneratedNullableGeneric<T>
{
    public T Value { get; set; } = default!;
}

public sealed class GeneratedClassConstrainedGeneric<T>
    where T : class
{
    public T Value { get; set; } = default!;
}

public sealed class GeneratedNullabilityAttributes
{
    [System.Diagnostics.CodeAnalysis.NotNull]
    public string? NotNullValue { get; set; } = "a";
}

[TomlSerializable(typeof(GeneratedNullableGeneric<string>))]
[TomlSerializable(typeof(GeneratedClassConstrainedGeneric<string>))]
[TomlSerializable(typeof(GeneratedNullabilityAttributes))]
internal sealed partial class TestTomlSerializerContextGenericNullability : TomlSerializerContext
{
}

public sealed class GeneratedInitOnlyPopulateModel
{
    [TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
    public List<int> Items { get; init; } = [1];

    [TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
    public GeneratedInitOnlyPopulateChild Child { get; init; } = new() { A = 1 };
}

public sealed class GeneratedInitOnlyPopulateChild
{
    public int A { get; set; }

    public int B { get; set; }
}

[TomlSerializable(typeof(GeneratedInitOnlyPopulateModel))]
internal sealed partial class TestTomlSerializerContextInitOnlyPopulate : TomlSerializerContext
{
}

public sealed class GeneratedInternalSetterModel
{
    public int Value { get; internal set; }
}

public sealed class GeneratedInternalConstructorModel
{
    [TomlConstructor]
    internal GeneratedInternalConstructorModel(string name) => Name = name + "!";

    public GeneratedInternalConstructorModel()
    {
        Name = "default";
    }

    public string Name { get; }
}

[TomlSerializable(typeof(GeneratedInternalSetterModel))]
[TomlSerializable(typeof(GeneratedInternalConstructorModel))]
internal sealed partial class TestTomlSerializerContextVisibility : TomlSerializerContext
{
}

public sealed class GeneratedBraceNameModel
{
    [TomlRequired]
    [TomlPropertyName("{name}")]
    public string? Name { get; set; }
}

public enum GeneratedEnumKind
{
    A = 0,
    B = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GeneratedStringEnumKind
{
    LogLevel,
    Time,
    Component,
}

public sealed class GeneratedEnumAndObjectPayload
{
    public GeneratedEnumKind Kind { get; set; }

    public object? Value { get; set; }
}

public sealed class GeneratedTomlObjectPayload
{
    public TomlObject? Node { get; set; }
}

public sealed class GeneratedStringEnumPayload
{
    public List<GeneratedStringEnumKind> Order { get; set; } =
    [
        GeneratedStringEnumKind.LogLevel,
        GeneratedStringEnumKind.Time,
        GeneratedStringEnumKind.Component,
    ];
}

public sealed class GeneratedOptionsConverter : TomlConverter<string>
{
    public override string? Read(TomlReader reader) => reader.GetString();

    public override void Write(TomlWriter writer, string value) => writer.WriteStringValue(value.ToUpperInvariant());
}

public sealed class GeneratedConvertedScalarConverter : TomlConverter<GeneratedConvertedScalar>
{
    public override GeneratedConvertedScalar? Read(TomlReader reader) => new() { Text = reader.GetString()! };

    public override void Write(TomlWriter writer, GeneratedConvertedScalar value) => writer.WriteStringValue(value.Text);
}

public sealed class RuntimeGuidConverter(string wireValue, Guid value) : TomlConverter<Guid>
{
    private readonly Guid _value = value;

    public override Guid Read(TomlReader reader)
    {
        Assert.Equal(wireValue, reader.GetString());
        return _value;
    }

    public override void Write(TomlWriter writer, Guid value)
    {
        Assert.Equal(_value, value);
        writer.WriteStringValue(wireValue);
    }
}

public sealed class RuntimeNullableGuidConverter(string wireValue, Guid value) : TomlConverter<Guid?>
{
    private readonly Guid _value = value;

    public override Guid? Read(TomlReader reader)
    {
        Assert.Equal(wireValue, reader.GetString());
        return _value;
    }

    public override void Write(TomlWriter writer, Guid? value)
    {
        Assert.Equal(_value, value);
        writer.WriteStringValue(wireValue);
    }
}

public sealed class CountingRuntimeGuidConverterFactory(string wireValue, Guid value) : TomlConverterFactory
{
    public int CanConvertCount { get; private set; }

    public int CreateConverterCount { get; private set; }

    public override bool CanConvert(Type typeToConvert)
    {
        CanConvertCount++;
        return typeToConvert == typeof(Guid);
    }

    public override TomlConverter CreateConverter(Type typeToConvert, TomlSerializerOptions options)
    {
        CreateConverterCount++;
        return new RuntimeGuidConverter(wireValue, value);
    }
}

public sealed class ThrowingGeneratedConverterFactory : TomlConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => throw new InvalidOperationException("Source-generated converter resolution should be static.");

    public override TomlConverter CreateConverter(Type typeToConvert, TomlSerializerOptions options) => throw new InvalidOperationException("Source-generated converter resolution should be static.");
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedPerson))]
[TomlSerializable(typeof(GeneratedRuntimeConverterHolder))]
internal sealed partial class TestTomlSerializerContext : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.PascalCase)]
[TomlSerializable(typeof(GeneratedLowerCasePerson))]
internal sealed partial class TestTomlSerializerContextPascalCase : TomlSerializerContext
{
}

internal sealed class GeneratedMemberSelectionModel
{
#pragma warning disable CA1051 // The test needs public fields
    public int Field = 1;
    public readonly int ReadOnlyField = 2;
#pragma warning restore CA1051

    public int GetOnly { get; } = 3;

    public int InitOnly { get; init; } = 4;
}

internal sealed class GeneratedIncludedReadOnlyFieldModel
{
    [TomlInclude]
#pragma warning disable CA1051 // The test needs public fields
    public readonly int ReadOnlyField = 2;
#pragma warning restore CA1051
}

[TomlSerializable(typeof(GeneratedMemberSelectionModel))]
[TomlSerializable(typeof(GeneratedIncludedReadOnlyFieldModel))]
internal sealed partial class TestTomlSerializerContextDefaultMemberSelection : TomlSerializerContext
{
}

[TomlSerializable(typeof(GeneratedKeywordRecord))]
[TomlSerializable(typeof(GeneratedBraceNameModel))]
[TomlSerializable(typeof(GeneratedRequiredModifierModel))]
[TomlSerializable(typeof(GeneratedSetsRequiredMembersModel))]
[TomlSerializable(typeof(GeneratedIncludedNonPublicSetterPayload))]
[TomlSerializable(typeof(GeneratedIncludedNonPublicSetterStruct))]
[TomlSerializable(typeof(GeneratedIncludedConstructorPayload))]
internal sealed partial class TestTomlSerializerContextNonPublicSetters : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(IncludeFields = true)]
[TomlSerializable(typeof(GeneratedMemberSelectionModel))]
internal sealed partial class TestTomlSerializerContextIncludeFields : TomlSerializerContext
{
}

[JsonSourceGenerationOptions(IncludeFields = true)]
[TomlSerializable(typeof(GeneratedMemberSelectionModel))]
internal sealed partial class TestTomlSerializerContextJsonIncludeFields : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(IncludeFields = true, IgnoreReadOnlyFields = true, IgnoreReadOnlyProperties = true)]
[TomlSerializable(typeof(GeneratedMemberSelectionModel))]
internal sealed partial class TestTomlSerializerContextIgnoreReadOnlyMembers : TomlSerializerContext
{
}

public sealed class ValidationModel
{
    public string Name { get; set; } = "";

    public string? Optional { get; set; }
}

public sealed class ValidationCtorModel
{
    public ValidationCtorModel(string name, int count)
    {
        Name = name;
        Count = count;
    }

    public string Name { get; }

    public int Count { get; }
}

public sealed class ValidationAllowNullModel
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public string Name { get; set; } = "";
}

[TomlUnmappedMemberHandling(TomlUnmappedMemberHandling.Disallow)]
public sealed class TomlDisallowUnmappedModel
{
    public string Name { get; set; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class JsonDisallowUnmappedModel
{
    public string Name { get; set; } = "";
}

[TomlUnmappedMemberHandling(TomlUnmappedMemberHandling.Skip)]
public sealed class TomlSkipUnmappedModel
{
    public string Name { get; set; } = "";
}

public sealed class ValidationExtensionDataModel
{
    public string Name { get; set; } = "";

    [TomlExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }
}

public sealed class NullStringConverter : TomlConverter<string>
{
    public override string Read(TomlReader reader)
    {
        reader.Skip();
        return null!;
    }

    public override void Write(TomlWriter writer, string value) => writer.WriteStringValue(value);
}

[TomlSerializable(typeof(ValidationModel))]
[TomlSerializable(typeof(ValidationCtorModel))]
[TomlSerializable(typeof(TomlDisallowUnmappedModel))]
[TomlSerializable(typeof(JsonDisallowUnmappedModel))]
internal sealed partial class TestTomlSerializerContextValidationDefault : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(UnmappedMemberHandling = TomlUnmappedMemberHandling.Disallow)]
[TomlSerializable(typeof(ValidationModel))]
[TomlSerializable(typeof(ValidationCtorModel))]
[TomlSerializable(typeof(TomlSkipUnmappedModel))]
[TomlSerializable(typeof(ValidationExtensionDataModel))]
internal sealed partial class TestTomlSerializerContextDisallowUnmapped : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(UnmappedMemberHandling = TomlUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = true)]
[TomlSerializable(typeof(ValidationModel))]
[TomlSerializable(typeof(ValidationCtorModel))]
internal sealed partial class TestTomlSerializerContextDisallowUnmappedCaseInsensitive : TomlSerializerContext
{
}

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[TomlSerializable(typeof(ValidationModel))]
internal sealed partial class TestTomlSerializerContextJsonDisallowUnmapped : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(RespectRequiredConstructorParameters = false, RespectNullableAnnotations = false)]
[TomlSerializable(typeof(ValidationModel))]
[TomlSerializable(typeof(ValidationCtorModel))]
internal sealed partial class TestTomlSerializerContextRelaxedValidation : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(Converters = [typeof(NullStringConverter)])]
[TomlSerializable(typeof(ValidationModel))]
[TomlSerializable(typeof(ValidationCtorModel))]
[TomlSerializable(typeof(ValidationAllowNullModel))]
internal sealed partial class TestTomlSerializerContextNullStrings : TomlSerializerContext
{
}

public sealed class GeneratedLowerCasePerson
{
#pragma warning disable IDE1006 // The member name is lowercase so the naming policy has something to convert
    public string name { get; set; } = "";
#pragma warning restore IDE1006
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedPerson), TypeInfoPropertyName = "GeneratedPersonInfo")]
internal sealed partial class TestTomlSerializerContextCustomPropertyName : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedIntPerson))]
internal sealed partial class TestTomlSerializerContextInt : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.SnakeCaseLower)]
[TomlSerializable(typeof(GeneratedSnakePerson))]
internal sealed partial class TestTomlSerializerContextSnakeCase : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedDerivedOptions))]
internal sealed partial class TestTomlSerializerContextInheritedMembers : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedOverriddenDerivedOptions))]
internal sealed partial class TestTomlSerializerContextOverriddenMembers : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(
    WriteIndented = false,
    IndentSize = 4,
    NewLine = TomlNewLineKind.CrLf,
    PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = TomlKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = TomlIgnoreCondition.Never,
    DuplicateKeyHandling = TomlDuplicateKeyHandling.LastWins,
    MaxDepth = 16,
    MappingOrder = TomlMappingOrderPolicy.OrderThenAlphabetical,
    DottedKeyHandling = TomlDottedKeyHandling.Expand,
    RootValueHandling = TomlRootValueHandling.WrapInRootKey,
    RootValueKeyName = "root",
    InlineTablePolicy = TomlInlineTablePolicy.WhenSmall,
    TableArrayStyle = TomlTableArrayStyle.InlineArrayOfTables,
    Converters = [typeof(GeneratedOptionsConverter)])]
[TomlSerializable(typeof(GeneratedOptionsPerson))]
internal sealed partial class TestTomlSerializerContextWithOptions : TomlSerializerContext
{
}

// The generated code resolves converters at build time, so it never calls the factory (MFTOML012)
#pragma warning disable MFTOML012
[TomlSourceGenerationOptions(Converters = [typeof(ThrowingGeneratedConverterFactory), typeof(GeneratedConvertedScalarConverter)])]
[TomlSerializable(typeof(GeneratedConvertedScalarHolder))]
internal sealed partial class TestTomlSerializerContextWithConverter : TomlSerializerContext
#pragma warning restore MFTOML012
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedTransitiveConfig))]
internal sealed partial class TestTomlSerializerContextTransitive : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(
    PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase,
    MappingOrder = TomlMappingOrderPolicy.OrderThenDeclaration)]
[TomlSerializable(typeof(GeneratedOrderedPerson))]
internal sealed partial class TestTomlSerializerContextOrdering : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedCollectionsPayload))]
internal sealed partial class TestTomlSerializerContextCollections : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedNullablePayload))]
internal sealed partial class TestTomlSerializerContextNullables : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedNullableReferencePayload))]
internal sealed partial class TestTomlSerializerContextNullableReferences : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedIncludedInternalPropertyPayload))]
internal sealed partial class TestTomlSerializerContextIncludedInternalProperty : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedIncludedPrivatePropertyPayload))]
internal sealed partial class TestTomlSerializerContextIncludedPrivateProperty : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedEnumAndObjectPayload))]
internal sealed partial class TestTomlSerializerContextEnumsAndObjects : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedTomlObjectPayload))]
internal sealed partial class TestTomlSerializerContextTomlObject : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedStringEnumPayload))]
internal sealed partial class TestTomlSerializerContextStringEnums : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedCallbackPerson))]
internal sealed partial class TestTomlSerializerContextCallbacks : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedRequiredPerson))]
internal sealed partial class TestTomlSerializerContextRequired : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedInitOnlyPerson))]
internal sealed partial class TestTomlSerializerContextInitOnly : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedRequiredInitPerson))]
internal sealed partial class TestTomlSerializerContextRequiredInit : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedCtorInitRequiredPerson))]
internal sealed partial class TestTomlSerializerContextCtorInitRequired : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(
    PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedExtensionDataPerson))]
internal sealed partial class TestTomlSerializerContextExtensionData : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedCtorPerson))]
internal sealed partial class TestTomlSerializerContextConstructor : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedCtorSelectionPerson))]
internal sealed partial class TestTomlSerializerContextConstructorSelection : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(IGeneratedAnimal))]
internal sealed partial class TestTomlSerializerContextPolymorphism : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.SnakeCaseLower)]
[TomlDerivedTypeMapping(typeof(GeneratedDepartLevel), typeof(GeneratedDepart1), "dd1")]
[TomlDerivedTypeMapping(typeof(GeneratedGroupLevel), typeof(GeneratedGroup2), "gg2")]
[TomlSerializable(typeof(GeneratedTwoLevelPolymorphicConfig))]
internal sealed partial class TestTomlSerializerContextTwoLevelPolymorphism : TomlSerializerContext
{
}

[TomlPolymorphic(TypeDiscriminatorPropertyName = "type")]
[TomlDerivedType(typeof(GeneratedDefaultCircle))]
[TomlDerivedType(typeof(GeneratedDefaultSquare), "square")]
public abstract class GeneratedDefaultShape
{
}

public sealed class GeneratedDefaultCircle : GeneratedDefaultShape
{
    public string Color { get; set; } = "";
    public double Radius { get; set; }
}

public sealed class GeneratedDefaultSquare : GeneratedDefaultShape
{
    public string Color { get; set; } = "";
    public double Side { get; set; }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedDefaultShape))]
internal sealed partial class TestTomlSerializerContextDefaultDerivedType : TomlSerializerContext
{
}

[TomlPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = TomlUnknownDerivedTypeHandling.FallBackToBaseType)]
[TomlDerivedType(typeof(GeneratedAttrFallbackDerived), "derived")]
public class GeneratedAttrFallbackBase
{
    public string Name { get; set; } = "";
}

public sealed class GeneratedAttrFallbackDerived : GeneratedAttrFallbackBase
{
    public int Extra { get; set; }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedAttrFallbackBase))]
internal sealed partial class TestTomlSerializerContextAttrFallback : TomlSerializerContext
{
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType)]
[JsonDerivedType(typeof(GeneratedJsonAttrFallbackDerived), "derived")]
public class GeneratedJsonAttrFallbackBase
{
    public string Name { get; set; } = "";
}

public sealed class GeneratedJsonAttrFallbackDerived : GeneratedJsonAttrFallbackBase
{
    public int Extra { get; set; }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedJsonAttrFallbackBase))]
internal sealed partial class TestTomlSerializerContextJsonAttrFallback : TomlSerializerContext
{
}

[TomlPolymorphic(TypeDiscriminatorPropertyName = "type")]
[TomlDerivedType(typeof(GeneratedIntDiscrimCircle), 1)]
[TomlDerivedType(typeof(GeneratedIntDiscrimSquare), 2)]
public abstract class GeneratedIntDiscrimShape
{
}

public sealed class GeneratedIntDiscrimCircle : GeneratedIntDiscrimShape
{
    public string Color { get; set; } = "";
    public double Radius { get; set; }
}

public sealed class GeneratedIntDiscrimSquare : GeneratedIntDiscrimShape
{
    public string Color { get; set; } = "";
    public double Side { get; set; }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedIntDiscrimShape))]
internal sealed partial class TestTomlSerializerContextIntDiscriminator : TomlSerializerContext
{
}

public class NewApiSourceGenerationTests
{
    [Fact]
    public void GeneratedContext_RespectsJsonPropertyOrder_WhenWriting()
    {
        var context = TestTomlSerializerContextOrdering.Default;
        var toml = TomlSerializer.Serialize(new GeneratedOrderedPerson { Name = "Ada", Age = 37 }, context.GeneratedOrderedPerson);

        var ageIndex = toml.IndexOf("age", StringComparison.Ordinal);
        var nameIndex = toml.IndexOf("name", StringComparison.Ordinal);
        Assert.True(ageIndex >= 0);
        Assert.True(nameIndex >= 0);
        Assert.True(ageIndex < nameIndex);
    }

    [Fact]
    public void GeneratedContext_CanDeserializePoco()
    {
        var context = TestTomlSerializerContext.Default;
        var toml = """
            name = "Ada"
            age = 37
            """;

        var person = TomlSerializer.Deserialize(toml, context.GeneratedPerson);

        Assert.NotNull(person);
        Assert.Equal("Ada", person!.Name);
        Assert.Equal(37, person.Age);
    }

    [Fact]
    public void GeneratedContext_CanUseCustomTypeInfoPropertyName()
    {
        var context = TestTomlSerializerContextCustomPropertyName.Default;
        var toml = """
            name = "Ada"
            age = 37
            """;

        var person = TomlSerializer.Deserialize(toml, context.GeneratedPersonInfo);

        Assert.NotNull(person);
        Assert.Equal("Ada", person!.Name);
        Assert.Equal(37, person.Age);
    }

    [Fact]
    public void GeneratedContext_CanDeserializeInitOnlyMembers()
    {
        var context = TestTomlSerializerContextInitOnly.Default;
        var toml = """
            name = "Ada"
            """;

        var person = TomlSerializer.Deserialize(toml, context.GeneratedInitOnlyPerson);

        Assert.NotNull(person);
        Assert.Equal("Ada", person!.Name);
        Assert.Equal(42, person.Age);
    }

    [Fact]
    public void GeneratedContext_CanDeserializeRequiredKeywordMembers()
    {
        var context = TestTomlSerializerContextRequiredInit.Default;
        var toml = """
            name = "Ada"
            """;

        var person = TomlSerializer.Deserialize(toml, context.GeneratedRequiredInitPerson);

        Assert.NotNull(person);
        Assert.Equal("Ada", person!.Name);
        Assert.Equal(42, person.Age);
    }

    [Fact]
    public void GeneratedContext_Throws_WhenRequiredKeywordMemberIsMissing()
    {
        var context = TestTomlSerializerContextRequiredInit.Default;
        var toml = """
            age = 37
            """;

        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(toml, context.GeneratedRequiredInitPerson));

        Assert.NotNull(exception);
        Assert.Contains("Missing required TOML key 'name'", exception!.Message);
        Assert.Contains(typeof(GeneratedRequiredInitPerson).FullName!, exception.Message);
    }

    [Fact]
    public void GeneratedContext_Throws_WhenRequiredMemberIsMissing()
    {
        var context = TestTomlSerializerContextRequired.Default;
        var toml = """
            age = 37
            """;

        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(toml, context.GeneratedRequiredPerson));

        Assert.NotNull(exception);
        Assert.Contains("Missing required TOML key 'name'", exception!.Message);
        Assert.Contains(typeof(GeneratedRequiredPerson).FullName!, exception.Message);
        Assert.True(exception.Line >= 1);
        Assert.True(exception.Column >= 1);
    }

    [Fact]
    public void GeneratedContext_CanDeserializeConstructorBoundRequiredInitMembers()
    {
        var context = TestTomlSerializerContextCtorInitRequired.Default;
        var toml = """
            name = "Ada"
            age = 37
            """;

        var model = TomlSerializer.Deserialize(toml, context.GeneratedCtorInitRequiredPerson);

        Assert.NotNull(model);
        Assert.Equal("Ada", model!.Name);
        Assert.Equal(37, model.Age);
    }

    [Fact]
    public void GeneratedContext_PreservesConstructorDefaultsForMissingInitOnlyMembers()
    {
        var context = TestTomlSerializerContextCtorInitRequired.Default;
        var toml = """
            name = "Ada"
            """;

        var model = TomlSerializer.Deserialize(toml, context.GeneratedCtorInitRequiredPerson);

        Assert.NotNull(model);
        Assert.Equal("Ada", model!.Name);
        Assert.Equal(42, model.Age);
    }

    [Fact]
    public void GeneratedContext_CapturesJsonExtensionData_WhenReading()
    {
        var context = TestTomlSerializerContextExtensionData.Default;
        var toml = """
            name = "Ada"
            unknown = 1
            """;

        var model = TomlSerializer.Deserialize(toml, context.GeneratedExtensionDataPerson);

        Assert.NotNull(model);
        Assert.Equal("Ada", model!.Name);
        Assert.NotNull(model.Extra);
        Assert.Contains("unknown", model.Extra);
        Assert.IsAssignableTo<long>(model.Extra["unknown"]);
        Assert.Equal(1, (long)model.Extra["unknown"]!);
    }

    [Fact]
    public void GeneratedContext_Throws_WhenExtensionDataKeyConflictsWithMemberKey_OnWrite()
    {
        var context = TestTomlSerializerContextExtensionData.Default;
        var model = new GeneratedExtensionDataPerson
        {
            Name = "Ada",
            Extra = new Dictionary<string, object?> { ["name"] = "from-extra" },
        };

        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Serialize(model, context.GeneratedExtensionDataPerson));

        Assert.NotNull(exception);
        Assert.Contains("Extension data key", exception!.Message);
        Assert.Contains("conflicts", exception.Message);
    }

    [Fact]
    public void GeneratedContext_CanDeserializeUsingJsonConstructor_AndDefaultValues()
    {
        var context = TestTomlSerializerContextConstructor.Default;
        var toml = """
            first_name = "Ada"
            """;

        var model = TomlSerializer.Deserialize(toml, context.GeneratedCtorPerson);

        Assert.NotNull(model);
        Assert.Equal("Ada", model!.FirstName);
        Assert.Equal(42, model.Age);
    }

    [Fact]
    public void GeneratedContext_Throws_WhenRequiredConstructorParameterIsMissing()
    {
        var context = TestTomlSerializerContextConstructorSelection.Default;
        var toml = """
            other = 1
            """;

        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(toml, context.GeneratedCtorSelectionPerson));

        Assert.NotNull(exception);
        Assert.Contains("Missing required constructor parameter 'name'", exception!.Message);
        Assert.Contains(typeof(GeneratedCtorSelectionPerson).FullName!, exception.Message);
        Assert.True(exception.Line >= 1);
        Assert.True(exception.Column >= 1);
    }

    [Fact]
    public void GeneratedContext_PrefersAnnotatedJsonConstructor_WhenMultipleAreAvailable()
    {
        var context = TestTomlSerializerContextConstructorSelection.Default;
        var toml = """
            name = "Ada"
            """;

        var model = TomlSerializer.Deserialize(toml, context.GeneratedCtorSelectionPerson);

        Assert.NotNull(model);
        Assert.Equal("Ada", model!.Name);
    }

    [Fact]
    public void GeneratedContext_CanDeserializePolymorphicInterface()
    {
        var context = TestTomlSerializerContextPolymorphism.Default;
        var toml = """
            kind = "cat"
            name = "Ada"
            lives = 9
            """;

        var model = TomlSerializer.Deserialize(toml, context.IGeneratedAnimal);

        Assert.NotNull(model);
        Assert.IsAssignableTo<GeneratedCat>(model);
        var cat = (GeneratedCat)model!;
        Assert.Equal("Ada", cat.Name);
        Assert.Equal(9, cat.Lives);
    }

    [Fact]
    public void GeneratedContext_Throws_WhenPolymorphicDiscriminatorIsMissing()
    {
        var context = TestTomlSerializerContextPolymorphism.Default;
        var toml = """
            name = "Ada"
            """;

        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(toml, context.IGeneratedAnimal));

        Assert.NotNull(exception);
        Assert.Contains("Missing discriminator key 'kind'", exception!.Message);
        Assert.Contains(typeof(IGeneratedAnimal).FullName!, exception.Message);
        Assert.True(exception.Line >= 1);
        Assert.True(exception.Column >= 1);
    }

    [Fact]
    public void GeneratedContext_CanSerializePolymorphicInterface()
    {
        var context = TestTomlSerializerContextPolymorphism.Default;
        IGeneratedAnimal model = new GeneratedDog { Name = "Rex", GoodBoy = true };

        var toml = TomlSerializer.Serialize(model, context.IGeneratedAnimal);

        Assert.Contains("kind = \"dog\"", toml);
        Assert.Contains("name = \"Rex\"", toml);
        Assert.Contains("goodBoy = true", toml);
    }

    [Fact]
    public void GeneratedContext_CanDeserializeNestedPolymorphicTables()
    {
        var context = TestTomlSerializerContextTwoLevelPolymorphism.Default;
        var toml = """
            [depart]
            "$type" = "dd1"
            name = "this is depart1"
            field1 = "depart 1 field"

            [depart.group]
            "$type" = "gg2"
            name = "this is group 2"
            field2 = "group 2 field"
            """;

        var model = TomlSerializer.Deserialize(toml, context.GeneratedTwoLevelPolymorphicConfig);

        Assert.NotNull(model);
        Assert.IsAssignableTo<GeneratedDepart1>(model!.Depart);
        var depart = (GeneratedDepart1)model.Depart;
        Assert.Equal("this is depart1", depart.Name);
        Assert.Equal("depart 1 field", depart.Field1);
        Assert.IsAssignableTo<GeneratedGroup2>(depart.Group);
        var group = (GeneratedGroup2)depart.Group;
        Assert.Equal("this is group 2", group.Name);
        Assert.Equal("group 2 field", group.Field2);
    }

    [Fact]
    public void GeneratedContext_CanSerializePoco()
    {
        var context = TestTomlSerializerContext.Default;
        var toml = TomlSerializer.Serialize(new GeneratedPerson { Name = "Ada", Age = 37 }, context.GeneratedPerson);
        var roundtrip = TomlSerializer.Deserialize(toml, context.GeneratedPerson);

        Assert.NotNull(roundtrip);
        Assert.Equal("Ada", roundtrip!.Name);
        Assert.Equal(37, roundtrip.Age);
    }

    [Fact]
    public void GeneratedContext_InvokesLifecycleCallbacks()
    {
        var context = TestTomlSerializerContextCallbacks.Default;
        var original = new GeneratedCallbackPerson { Name = "Ada" };
        var toml = TomlSerializer.Serialize(original, context.GeneratedCallbackPerson);

        Assert.Equal(1, original.OnSerializingCount);
        Assert.Equal(1, original.OnSerializedCount);
        Assert.Equal(0, original.OnDeserializingCount);
        Assert.Equal(0, original.OnDeserializedCount);

        var roundtrip = TomlSerializer.Deserialize(toml, context.GeneratedCallbackPerson);

        Assert.NotNull(roundtrip);
        Assert.Equal("Ada", roundtrip!.Name);
        Assert.Equal(1, roundtrip.OnDeserializingCount);
        Assert.Equal(1, roundtrip.OnDeserializedCount);
        Assert.False(roundtrip.NameWasAlreadyAssignedInOnDeserializing);
        Assert.Equal("Ada", roundtrip.NameSeenInOnDeserialized);
    }

    [Fact]
    public void GeneratedContext_CanHandleIntProperties()
    {
        var context = TestTomlSerializerContextInt.Default;
        var toml = """
            age = 37
            """;

        var person = TomlSerializer.Deserialize(toml, context.GeneratedIntPerson);

        Assert.NotNull(person);
        Assert.Equal(37, person!.Age);
    }

    [Fact]
    public void GeneratedContext_CanApplySnakeCaseNamingPolicy()
    {
        var context = TestTomlSerializerContextSnakeCase.Default;
        var toml = """
            first_name = "Ada"
            """;

        var person = TomlSerializer.Deserialize(toml, context.GeneratedSnakePerson);

        Assert.NotNull(person);
        Assert.Equal("Ada", person!.FirstName);
    }

    [Fact]
    public void GeneratedContext_IncludeFields_DoesNotIncludePublicFieldsByDefault()
    {
        var typeInfo = TestTomlSerializerContextDefaultMemberSelection.Default.GeneratedMemberSelectionModel;

        var toml = TomlSerializer.Serialize(new GeneratedMemberSelectionModel(), typeInfo);
        var model = TomlSerializer.Deserialize("Field = 10", typeInfo)!;

        Assert.Equal("GetOnly = 3\nInitOnly = 4", toml.Trim());
        Assert.Equal(1, model.Field);
        Assert.False(TestTomlSerializerContextDefaultMemberSelection.Default.Options.IncludeFields);
    }

    [Fact]
    public void GeneratedContext_IncludeFields_IncludesPublicFieldsForReadAndWrite()
    {
        var context = TestTomlSerializerContextIncludeFields.Default;

        var toml = TomlSerializer.Serialize(new GeneratedMemberSelectionModel(), context.GeneratedMemberSelectionModel);
        var model = TomlSerializer.Deserialize("Field = 10", context.GeneratedMemberSelectionModel)!;

        Assert.Equal("Field = 1\nReadOnlyField = 2\nGetOnly = 3\nInitOnly = 4", toml.Trim());
        Assert.Equal(10, model.Field);
        Assert.True(context.Options.IncludeFields);
    }

    [Fact]
    public void GeneratedContext_JsonSourceGenerationOptions_IncludeFieldsIsApplied()
    {
        var context = TestTomlSerializerContextJsonIncludeFields.Default;

        var toml = TomlSerializer.Serialize(new GeneratedMemberSelectionModel(), context.GeneratedMemberSelectionModel);

        Assert.Contains("Field = 1", toml);
        Assert.True(context.Options.IncludeFields);
    }

    [Fact]
    public void GeneratedContext_IgnoreReadOnlyMembers_SkipsReadOnlyMembersDuringSerialization()
    {
        var context = TestTomlSerializerContextIgnoreReadOnlyMembers.Default;

        var toml = TomlSerializer.Serialize(new GeneratedMemberSelectionModel(), context.GeneratedMemberSelectionModel);
        var model = TomlSerializer.Deserialize("InitOnly = 40", context.GeneratedMemberSelectionModel)!;

        Assert.Equal("Field = 1\nInitOnly = 4", toml.Trim());
        Assert.Equal(40, model.InitOnly);
        Assert.True(context.Options.IgnoreReadOnlyFields);
        Assert.True(context.Options.IgnoreReadOnlyProperties);
    }

    [Fact]
    public void GeneratedContext_IncludedReadOnlyField_IsSerialized()
    {
        var typeInfo = TestTomlSerializerContextDefaultMemberSelection.Default.GeneratedIncludedReadOnlyFieldModel;

        var toml = TomlSerializer.Serialize(new GeneratedIncludedReadOnlyFieldModel(), typeInfo);
        var model = TomlSerializer.Deserialize("ReadOnlyField = 20", typeInfo)!;

        Assert.Equal("ReadOnlyField = 2", toml.Trim());
        Assert.Equal(2, model.ReadOnlyField);
    }

    [Fact]
    public void GeneratedContext_SkipsUnmappedMembersByDefault()
    {
        var context = TestTomlSerializerContextValidationDefault.Default;

        Assert.Equal("a", TomlSerializer.Deserialize("Name = \"a\"\nUnknown = 1", context.ValidationModel)!.Name);
        Assert.Equal("a", TomlSerializer.Deserialize("Name = \"a\"\nCount = 1\nUnknown = 1", context.ValidationCtorModel)!.Name);
        Assert.Equal(TomlUnmappedMemberHandling.Skip, context.Options.UnmappedMemberHandling);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedContext_CanDisallowUnmappedMembersViaOptions(bool caseInsensitive)
    {
        TomlSerializerContext context = caseInsensitive ? TestTomlSerializerContextDisallowUnmappedCaseInsensitive.Default : TestTomlSerializerContextDisallowUnmapped.Default;

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("Name = \"a\"\nUnknown = 1", typeof(ValidationModel), context));
        Assert.Contains($"The TOML key 'Unknown' could not be mapped to '{typeof(ValidationModel).FullName}'.", ex.Message);

        ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("Name = \"a\"\nCount = 1\nUnknown = 1", typeof(ValidationCtorModel), context));
        Assert.Contains($"The TOML key 'Unknown' could not be mapped to '{typeof(ValidationCtorModel).FullName}'.", ex.Message);

        Assert.Equal("a", ((ValidationModel)TomlSerializer.Deserialize("Name = \"a\"", typeof(ValidationModel), context)!).Name);
        Assert.Equal(TomlUnmappedMemberHandling.Disallow, context.Options.UnmappedMemberHandling);
    }

    [Fact]
    public void GeneratedContext_JsonSourceGenerationOptions_UnmappedMemberHandlingIsApplied()
    {
        var context = TestTomlSerializerContextJsonDisallowUnmapped.Default;

        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("Unknown = 1", context.ValidationModel));
        Assert.Equal(TomlUnmappedMemberHandling.Disallow, context.Options.UnmappedMemberHandling);
    }

    [Fact]
    public void GeneratedContext_UnmappedMemberHandlingAttribute_OverridesOptions()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("Unknown = 1", TestTomlSerializerContextValidationDefault.Default.TomlDisallowUnmappedModel));
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("Unknown = 1", TestTomlSerializerContextValidationDefault.Default.JsonDisallowUnmappedModel));
        Assert.Equal("a", TomlSerializer.Deserialize("Name = \"a\"\nUnknown = 1", TestTomlSerializerContextDisallowUnmapped.Default.TomlSkipUnmappedModel)!.Name);
    }

    [Fact]
    public void GeneratedContext_UnmappedMemberHandling_DoesNotConflictWithExtensionData()
    {
        var model = TomlSerializer.Deserialize("Name = \"a\"\nUnknown = 1", TestTomlSerializerContextDisallowUnmapped.Default.ValidationExtensionDataModel)!;

        Assert.Equal(1L, model.Extra!["Unknown"]);
    }

    [Fact]
    public void GeneratedContext_RespectRequiredConstructorParameters_RequiresNonOptionalParametersByDefault()
    {
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("Name = \"a\"", TestTomlSerializerContextValidationDefault.Default.ValidationCtorModel));

        Assert.Contains($"Missing required constructor parameter 'Count' when deserializing '{typeof(ValidationCtorModel).FullName}'.", ex.Message);
    }

    [Fact]
    public void GeneratedContext_RespectRequiredConstructorParameters_CanBeDisabled()
    {
        var context = TestTomlSerializerContextRelaxedValidation.Default;

        var model = TomlSerializer.Deserialize("Name = \"a\"", context.ValidationCtorModel)!;

        Assert.Equal("a", model.Name);
        Assert.Equal(0, model.Count);
        Assert.False(context.Options.RespectRequiredConstructorParameters);
    }

    [Fact]
    public void GeneratedContext_RespectNullableAnnotations_RejectsNullDuringSerializationByDefault()
    {
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new ValidationModel { Name = null! }, TestTomlSerializerContextValidationDefault.Default.ValidationModel));

        Assert.Equal($"The member 'Name' on '{typeof(ValidationModel).FullName}' cannot be serialized as null because it is declared as non-nullable.", ex.Message);
        Assert.Equal("Name = \"a\"", TomlSerializer.Serialize(new ValidationModel { Name = "a" }, TestTomlSerializerContextValidationDefault.Default.ValidationModel).Trim());
    }

    [Fact]
    public void GeneratedContext_RespectNullableAnnotations_CanBeDisabledForSerialization()
    {
        var context = TestTomlSerializerContextRelaxedValidation.Default;

        var toml = TomlSerializer.Serialize(new ValidationModel { Name = null! }, context.ValidationModel);

        Assert.Equal("", toml.Trim());
        Assert.False(context.Options.RespectNullableAnnotations);
    }

    [Fact]
    public void GeneratedContext_RespectNullableAnnotations_RejectsNullDuringDeserialization()
    {
        var context = TestTomlSerializerContextNullStrings.Default;

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("Name = \"a\"", context.ValidationModel));
        Assert.Contains($"The TOML key 'Name' cannot be null because '{typeof(ValidationModel).FullName}' declares it as non-nullable.", ex.Message);

        ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("Name = \"a\"\nCount = 1", context.ValidationCtorModel));
        Assert.Contains($"The constructor parameter 'name' on '{typeof(ValidationCtorModel).FullName}' cannot be null because it is declared as non-nullable.", ex.Message);

        Assert.Null(TomlSerializer.Deserialize("Optional = \"a\"", context.ValidationModel)!.Optional);
        Assert.Null(TomlSerializer.Deserialize("Name = \"a\"", context.ValidationAllowNullModel)!.Name);
    }

    [Fact]
    public void GeneratedContext_CanApplyPascalCaseNamingPolicy()
    {
        var context = TestTomlSerializerContextPascalCase.Default;

        var toml = TomlSerializer.Serialize(new GeneratedLowerCasePerson { name = "Ada" }, context.GeneratedLowerCasePerson);

        Assert.Equal("Name = \"Ada\"", toml.Trim());
        Assert.Equal("Ada", TomlSerializer.Deserialize(toml, context.GeneratedLowerCasePerson)!.name);
    }

    [Fact]
    public void GeneratedContext_CanSerializeAndDeserializeInheritedProperties()
    {
        var context = TestTomlSerializerContextInheritedMembers.Default;
        var original = new GeneratedDerivedOptions
        {
            Base = "shared",
            Derived = "leaf",
        };

        var toml = TomlSerializer.Serialize(original, context);
        var roundtrip = TomlSerializer.Deserialize<GeneratedDerivedOptions>(toml, context);

        Assert.Contains("baseValue = \"shared\"", toml);
        Assert.Contains("derivedValue = \"leaf\"", toml);
        Assert.NotNull(roundtrip);
        Assert.Equal("shared", roundtrip!.Base);
        Assert.Equal("leaf", roundtrip.Derived);
    }

    [Fact]
    public void GeneratedContext_CanDeserializeOverriddenProperties_WithInheritedJsonPropertyName()
    {
        var context = TestTomlSerializerContextOverriddenMembers.Default;
        var toml = """
            baseValue = "shared"
            derivedValue = "leaf"
            """;

        var roundtrip = TomlSerializer.Deserialize<GeneratedOverriddenDerivedOptions>(toml, context);

        Assert.NotNull(roundtrip);
        Assert.Equal("shared", roundtrip!.Base);
        Assert.Equal("leaf", roundtrip.Derived);
    }

    [Fact]
    public void GeneratedContext_CanSerializeOverriddenProperties_WithInheritedJsonPropertyName()
    {
        var context = TestTomlSerializerContextOverriddenMembers.Default;
        var original = new GeneratedOverriddenDerivedOptions
        {
            Base = "shared",
            Derived = "leaf",
        };

        var toml = TomlSerializer.Serialize(original, context);

        Assert.Contains("baseValue = \"shared\"", toml);
        Assert.Contains("derivedValue = \"leaf\"", toml);
    }

    [Fact]
    public void GeneratedContext_AppliesTomlSourceGenerationOptions()
    {
        var context = TestTomlSerializerContextWithOptions.Default;
        var options = context.Options;

        Assert.False(options.WriteIndented);
        Assert.Equal(4, options.IndentSize);
        Assert.Equal(TomlNewLineKind.CrLf, options.NewLine);
        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Equal(TomlIgnoreCondition.Never, options.DefaultIgnoreCondition);
        Assert.Equal(TomlDuplicateKeyHandling.LastWins, options.DuplicateKeyHandling);
        Assert.Equal(16, options.MaxDepth);
        Assert.Equal(TomlMappingOrderPolicy.OrderThenAlphabetical, options.MappingOrder);
        Assert.Equal(TomlDottedKeyHandling.Expand, options.DottedKeyHandling);
        Assert.Equal(TomlRootValueHandling.WrapInRootKey, options.RootValueHandling);
        Assert.Equal("root", options.RootValueKeyName);
        Assert.Equal(TomlInlineTablePolicy.WhenSmall, options.InlineTablePolicy);
        Assert.Equal(TomlTableArrayStyle.InlineArrayOfTables, options.TableArrayStyle);
        Assert.HasCount(1, options.Converters);
        Assert.IsAssignableTo<GeneratedOptionsConverter>(options.Converters[0]);
    }

    [Fact]
    public void GeneratedContext_UsesTomlSourceGenerationOptionsConverters_ForNestedTypes()
    {
        var context = TestTomlSerializerContextWithConverter.Default;
        var toml = "Value = \"hello\"";

        var fromContext = TomlSerializer.Deserialize<GeneratedConvertedScalarHolder>(toml, context);
        var fromTypeInfo = TomlSerializer.Deserialize(toml, context.GeneratedConvertedScalarHolder);
        var fromResolverOptions = TomlSerializer.Deserialize<GeneratedConvertedScalarHolder>(
            toml,
            new TomlSerializerOptions { TypeInfoResolver = context });
        var roundtripToml = TomlSerializer.Serialize(
            new GeneratedConvertedScalarHolder { Value = new GeneratedConvertedScalar { Text = "world" } },
            context.GeneratedConvertedScalarHolder);

        Assert.NotNull(fromContext);
        Assert.Equal("hello", fromContext!.Value.Text);
        Assert.NotNull(fromTypeInfo);
        Assert.Equal("hello", fromTypeInfo!.Value.Text);
        Assert.NotNull(fromResolverOptions);
        Assert.Equal("hello", fromResolverOptions!.Value.Text);
        Assert.Contains("Value = \"world\"", roundtripToml);
    }

    [Fact]
    public void GeneratedContext_UsesRuntimeConverters_ForGeneratedMembers()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var optionalId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var context = TestTomlSerializerContext.Default;
        var options = context.Options with
        {
            Converters =
            [
                new RuntimeGuidConverter("ref:id", id),
                new RuntimeNullableGuidConverter("ref:optional-id", optionalId),
            ],
        };

        var value = TomlSerializer.Deserialize<GeneratedRuntimeConverterHolder>(
            "id = \"ref:id\"\noptionalId = \"ref:optional-id\"",
            options);
        var toml = TomlSerializer.Serialize(
            new GeneratedRuntimeConverterHolder { Id = id, OptionalId = optionalId },
            options);

        Assert.NotNull(value);
        Assert.Equal(id, value!.Id);
        Assert.Equal(optionalId, value.OptionalId);
        Assert.Contains("id = \"ref:id\"", toml);
        Assert.Contains("optionalId = \"ref:optional-id\"", toml);
    }

    [Fact]
    public void GeneratedContext_ResolvesRuntimeConverters_WhenTypeInfoIsInitialized()
    {
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var factory = new CountingRuntimeGuidConverterFactory("ref:id", id);
        var context = TestTomlSerializerContext.Default;
        var options = context.Options with { Converters = [factory] };

        var typeInfo = (TomlTypeInfo<GeneratedRuntimeConverterHolder>)context.GetTypeInfo(typeof(GeneratedRuntimeConverterHolder), options)!;
        var canConvertCount = factory.CanConvertCount;
        var createConverterCount = factory.CreateConverterCount;

        var toml = TomlSerializer.Serialize(new GeneratedRuntimeConverterHolder { Id = id }, typeInfo);
        var value = TomlSerializer.Deserialize("id = \"ref:id\"", typeInfo);

        Assert.True(canConvertCount > 0);
        Assert.True(createConverterCount > 0);
        Assert.Equal(canConvertCount, factory.CanConvertCount);
        Assert.Equal(createConverterCount, factory.CreateConverterCount);
        Assert.Contains("id = \"ref:id\"", toml);
        Assert.NotNull(value);
        Assert.Equal(id, value!.Id);
    }

    [Fact]
    public void GeneratedContext_GeneratesTransitiveMemberTypes()
    {
        var context = TestTomlSerializerContextTransitive.Default;
        var value = new GeneratedTransitiveConfig
        {
            Foo = new GeneratedTransitiveBar
            {
                Baz = new GeneratedTransitiveBaz { Value = "nested" },
            },
            Bars =
            [
                new GeneratedTransitiveBar
                {
                    Baz = new GeneratedTransitiveBaz { Value = "collection" },
                },
            ],
            Matrix =
            [
                [new GeneratedTransitiveBaz { Value = "a" }],
                [new GeneratedTransitiveBaz { Value = "b" }],
            ],
            BazByName =
            {
                ["first"] = new GeneratedTransitiveBaz { Value = "dictionary" },
            },
            Dynamic = new TomlArray { "node" },
            DynamicList = [1, "two"],
            DynamicMap =
            {
                ["answer"] = 3,
                ["items"] = new TomlArray { "x", 4 },
            },
        };

        var toml = TomlSerializer.Serialize(value, context.GeneratedTransitiveConfig);
        var roundtrip = TomlSerializer.Deserialize(toml, context.GeneratedTransitiveConfig);

        Assert.NotNull(context.GetTypeInfo(typeof(GeneratedTransitiveBar), context.Options));
        Assert.NotNull(context.GetTypeInfo(typeof(GeneratedTransitiveBaz), context.Options));
        Assert.NotNull(roundtrip);
        Assert.Equal("nested", roundtrip!.Foo?.Baz?.Value);
        Assert.Equal("collection", roundtrip.Bars[0].Baz?.Value);
        Assert.Equal("b", roundtrip.Matrix[1][0].Value);
        Assert.Equal("dictionary", roundtrip.BazByName["first"].Value);
        Assert.IsType<TomlArray>(roundtrip.Dynamic);
        Assert.Equal(new TomlArray { "node" }, (TomlArray)roundtrip.Dynamic);
        Assert.Equal(new object[] { 1L, "two" }, roundtrip.DynamicList);
        Assert.Equal(3L, roundtrip.DynamicMap["answer"]);
        Assert.Equal(new TomlArray { "x", 4L }, roundtrip.DynamicMap["items"]);
    }

    [Fact]
    public void GeneratedContext_CanHandleImmutableAndSetCollections()
    {
        var context = TestTomlSerializerContextCollections.Default;
        var toml = """
            numbers = [1, 2]
            names = ["a", "b"]
            ids = [1, 2, 1]
            tags = ["x", "y", "x"]
            values = [10, 20, 10]
            """;

        var payload = TomlSerializer.Deserialize(toml, context.GeneratedCollectionsPayload);

        Assert.NotNull(payload);
        Assert.HasCount(2, payload!.Numbers);
        Assert.Equal(1, payload.Numbers[0]);
        Assert.Equal(2, payload.Numbers[1]);
        Assert.NotNull(payload.Names);
        Assert.HasCount(2, payload.Names!);
        Assert.NotNull(payload.Ids);
        Assert.HasCount(2, payload.Ids!);
        Assert.NotNull(payload.Tags);
        Assert.HasCount(2, payload.Tags!);
        Assert.NotNull(payload.Values);
        Assert.HasCount(2, payload.Values!);

        var roundtripToml = TomlSerializer.Serialize(payload, context.GeneratedCollectionsPayload);
        var roundtrip = TomlSerializer.Deserialize(roundtripToml, context.GeneratedCollectionsPayload);

        Assert.NotNull(roundtrip);
        Assert.Equal(payload.Numbers, roundtrip!.Numbers);
        Assert.Equal(payload.Names, roundtrip.Names);
        Assert.Equal(payload.Ids, roundtrip.Ids);
        Assert.NotNull(roundtrip.Tags);
        Assert.True(roundtrip.Tags!.SetEquals(payload.Tags!));
        Assert.NotNull(roundtrip.Values);
        Assert.True(roundtrip.Values!.SetEquals(payload.Values!));
    }

    [Fact]
    public void GeneratedContext_CanHandleNullableValueTypes()
    {
        var context = TestTomlSerializerContextNullables.Default;
        var toml = """
            count = 1
            when = 1979-05-27T07:32:00Z
            """;

        var payload = TomlSerializer.Deserialize(toml, context.GeneratedNullablePayload);

        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Count);
        Assert.NotNull(payload.When);

        var roundtripToml = TomlSerializer.Serialize(payload, context.GeneratedNullablePayload);
        var roundtrip = TomlSerializer.Deserialize(roundtripToml, context.GeneratedNullablePayload);

        Assert.NotNull(roundtrip);
        Assert.Equal(payload.Count, roundtrip!.Count);
        Assert.Equal(payload.When, roundtrip.When);
    }

    [Fact]
    public void GeneratedContext_CanHandleNullableReferenceTypes()
    {
        var context = TestTomlSerializerContextNullableReferences.Default;

        var emptyPayload = TomlSerializer.Deserialize("", context.GeneratedNullableReferencePayload);

        Assert.NotNull(emptyPayload);
        Assert.Null(emptyPayload!.NullableMock);
        Assert.Equal(string.Empty, emptyPayload.NonNullableMock);

        var toml = """
            nullableMock = "hello"
            nonNullableMock = "world"
            """;

        var payload = TomlSerializer.Deserialize(toml, context.GeneratedNullableReferencePayload);

        Assert.NotNull(payload);
        Assert.Equal("hello", payload!.NullableMock);
        Assert.Equal("world", payload.NonNullableMock);
    }

    [Fact]
    public void GeneratedContext_IncludesInternalProperty_WithTomlInclude()
    {
        var context = TestTomlSerializerContextIncludedInternalProperty.Default;

        var toml = TomlSerializer.Serialize(new GeneratedIncludedInternalPropertyPayload(), context.GeneratedIncludedInternalPropertyPayload);
        var roundtrip = TomlSerializer.Deserialize("myProperty = false\n", context.GeneratedIncludedInternalPropertyPayload);

        Assert.Contains("myProperty = true", toml);
        Assert.NotNull(roundtrip);
        Assert.False(roundtrip!.MyProperty);
    }

    [Fact]
    public void GeneratedContext_KeywordMemberNames_Roundtrip()
    {
        var typeInfo = TestTomlSerializerContextNonPublicSetters.Default.GeneratedKeywordRecord;
        var value = new GeneratedKeywordRecord("a", 1) { @event = "e" };

        var toml = TomlSerializer.Serialize(value, typeInfo);

        Assert.Equal(value, TomlSerializer.Deserialize(toml, typeInfo));
        Assert.Contains("class = \"a\"", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullability_OfGenericAndAttributedMembers_IsTheSameInBothPaths()
    {
        var context = TestTomlSerializerContextGenericNullability.Default;

        // An unconstrained type parameter is nullable
        Assert.Equal(TomlSerializer.Serialize(new GeneratedNullableGeneric<string>()), TomlSerializer.Serialize(new GeneratedNullableGeneric<string>(), context.GeneratedNullableGenericString));

        // A class constraint and [NotNull] make the member non-nullable
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new GeneratedClassConstrainedGeneric<string>()));
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new GeneratedClassConstrainedGeneric<string>(), context.GeneratedClassConstrainedGenericString));
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new GeneratedNullabilityAttributes { NotNullValue = null }));
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new GeneratedNullabilityAttributes { NotNullValue = null }, context.GeneratedNullabilityAttributes));
    }

    [Fact]
    public void InitOnlyMember_WithPopulate_IsPopulatedInBothPaths()
    {
        const string Toml = "Items = [2]\n\n[Child]\nB = 2\n";

        AssertPopulated(TomlSerializer.Deserialize<GeneratedInitOnlyPopulateModel>(Toml));
        AssertPopulated(TomlSerializer.Deserialize(Toml, TestTomlSerializerContextInitOnlyPopulate.Default.GeneratedInitOnlyPopulateModel));

        static void AssertPopulated(GeneratedInitOnlyPopulateModel? value)
        {
            Assert.NotNull(value);
            Assert.Equal([1, 2], value.Items);
            Assert.Equal(1, value.Child.A);
            Assert.Equal(2, value.Child.B);
        }
    }

    [Fact]
    public void InternalSetterAndConstructor_AreHandledTheSameInBothPaths()
    {
        var context = TestTomlSerializerContextVisibility.Default;

        // An internal setter is not used without [TomlInclude]
        Assert.Equal(0, TomlSerializer.Deserialize<GeneratedInternalSetterModel>("Value = 5\n")!.Value);
        Assert.Equal(0, TomlSerializer.Deserialize("Value = 5\n", context.GeneratedInternalSetterModel)!.Value);

        // An annotated constructor can be internal
        Assert.Equal("a!", TomlSerializer.Deserialize<GeneratedInternalConstructorModel>("Name = \"a\"\n")!.Name);
        Assert.Equal("a!", TomlSerializer.Deserialize("Name = \"a\"\n", context.GeneratedInternalConstructorModel)!.Name);
    }

    [Fact]
    public void DeclarationOrder_IsTheSameInBothPaths()
    {
        const string Expected = "BaseField = 2\nBaseProperty = 1\nDerivedField = 4\nDerivedProperty = 3\n";

        Assert.Equal(Expected, TomlSerializer.Serialize(new GeneratedOrderDerived(), new TomlSerializerOptions { IncludeFields = true }));
        Assert.Equal(Expected, TomlSerializer.Serialize(new GeneratedOrderDerived(), TestTomlSerializerContextMemberOrder.Default.GeneratedOrderDerived));
    }

    [Fact]
    public void GeneratedContext_MissingKeyWithBraces_ReportsTheName()
    {
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("x = 1\n", TestTomlSerializerContextNonPublicSetters.Default.GeneratedBraceNameModel));

        Assert.Contains("Missing required TOML key '{name}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredModifier_MissingKey_ThrowsInBothPaths()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedRequiredModifierModel>("Age = 1\n"));
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("Age = 1\n", TestTomlSerializerContextNonPublicSetters.Default.GeneratedRequiredModifierModel));
    }

    [Fact]
    public void RequiredModifier_WithSetsRequiredMembers_IsOptionalInBothPaths()
    {
        Assert.Equal("default", TomlSerializer.Deserialize<GeneratedSetsRequiredMembersModel>("Age = 1\n")!.Name);
        Assert.Equal("default", TomlSerializer.Deserialize("Age = 1\n", TestTomlSerializerContextNonPublicSetters.Default.GeneratedSetsRequiredMembersModel)!.Name);
    }

    [Fact]
    public void GeneratedContext_SetsIncludedMembersWithNonPublicSetters()
    {
        const string Toml = "PrivateSet = 1\n_privateField = 2\nPrivateProperty = 3\n";
        var context = TestTomlSerializerContextNonPublicSetters.Default;

        var generated = TomlSerializer.Deserialize(Toml, context.GeneratedIncludedNonPublicSetterPayload)!;
        var reflection = TomlSerializer.Deserialize<GeneratedIncludedNonPublicSetterPayload>(Toml)!;

        foreach (var value in new[] { generated, reflection })
        {
            Assert.Equal(1, value.PrivateSet);
            Assert.Equal(2, value.GetPrivateField());
            Assert.Equal(3, value.GetPrivateProperty());
        }

        var reflectionLines = TomlSerializer.Serialize(reflection).Split('\n').Order(StringComparer.Ordinal);
        var generatedLines = TomlSerializer.Serialize(generated, context.GeneratedIncludedNonPublicSetterPayload).Split('\n').Order(StringComparer.Ordinal);
        Assert.Equal(reflectionLines, generatedLines);
    }

    [Fact]
    public void GeneratedContext_SetsIncludedStructMembersWithNonPublicSetters()
    {
        var value = TomlSerializer.Deserialize("PrivateSet = 1\n_privateField = 2\n", TestTomlSerializerContextNonPublicSetters.Default.GeneratedIncludedNonPublicSetterStruct);

        Assert.Equal(1, value.PrivateSet);
        Assert.Equal(2, value.GetPrivateField());
    }

    [Fact]
    public void GeneratedContext_SetsIncludedMembersWithNonPublicSetters_AfterConstructor()
    {
        var value = TomlSerializer.Deserialize("Name = \"a\"\nCount = 2\n", TestTomlSerializerContextNonPublicSetters.Default.GeneratedIncludedConstructorPayload)!;

        Assert.Equal("a", value.Name);
        Assert.Equal(2, value.Count);
    }

    [Fact]
    public void GeneratedContext_IncludesPrivateProperty_WithTomlInclude_WhenWriting()
    {
        var context = TestTomlSerializerContextIncludedPrivateProperty.Default;

        var toml = TomlSerializer.Serialize(new GeneratedIncludedPrivatePropertyPayload(), context.GeneratedIncludedPrivatePropertyPayload);

        Assert.Contains("myProperty = true", toml);
    }

    [Fact]
    public void GeneratedContext_CanHandleEnumsAndUntypedObjects()
    {
        var context = TestTomlSerializerContextEnumsAndObjects.Default;
        var toml = """
            kind = "a"
            value = 1
            """;

        var payload = TomlSerializer.Deserialize(toml, context.GeneratedEnumAndObjectPayload);

        Assert.NotNull(payload);
        Assert.Equal(GeneratedEnumKind.A, payload!.Kind);
        Assert.IsAssignableTo<long>(payload.Value);
        Assert.Equal(1, (long)payload.Value!);

        var roundtripToml = TomlSerializer.Serialize(payload, context.GeneratedEnumAndObjectPayload);
        var roundtrip = TomlSerializer.Deserialize(roundtripToml, context.GeneratedEnumAndObjectPayload);

        Assert.NotNull(roundtrip);
        Assert.Equal(payload.Kind, roundtrip!.Kind);
        Assert.Equal(payload.Value, roundtrip.Value);
    }

    [Fact]
    public void GeneratedContext_CanHandleTomlObjectProperties()
    {
        var context = TestTomlSerializerContextTomlObject.Default;
        var toml = """
            [node]
            answer = 42
            """;

        var payload = TomlSerializer.Deserialize(toml, context.GeneratedTomlObjectPayload);

        Assert.NotNull(payload);
        Assert.IsAssignableTo<TomlTable>(payload!.Node);
        Assert.Equal(42L, ((TomlTable)payload.Node!)["answer"]);

        payload.Node = new TomlArray { "x", 2L };
        var roundtripToml = TomlSerializer.Serialize(payload, context.GeneratedTomlObjectPayload);
        var roundtrip = TomlSerializer.Deserialize(roundtripToml, context.GeneratedTomlObjectPayload);

        Assert.Contains("node = [\"x\", 2]", roundtripToml);
        Assert.NotNull(roundtrip);
        Assert.IsAssignableTo<TomlArray>(roundtrip!.Node);
        Assert.Equal(new TomlArray { "x", 2L }, (TomlArray)roundtrip.Node!);
    }

    [Fact]
    public void GeneratedContext_EnumWithJsonStringEnumConverter_UsesStrings()
    {
        var context = TestTomlSerializerContextStringEnums.Default;

        var toml = TomlSerializer.Serialize(new GeneratedStringEnumPayload(), context.GeneratedStringEnumPayload);
        var roundtrip = TomlSerializer.Deserialize(toml, context.GeneratedStringEnumPayload);

        Assert.Contains("order = [\"LogLevel\", \"Time\", \"Component\"]", toml);
        Assert.NotNull(roundtrip);
        Assert.Equal(new[]
        {
            GeneratedStringEnumKind.LogLevel,
            GeneratedStringEnumKind.Time,
            GeneratedStringEnumKind.Component,
        }, roundtrip!.Order);
    }

    // --- Feature 1: Default Derived Type (source-gen) ---

    [Fact]
    public void GeneratedContext_DefaultDerivedType_Serialize_OmitsDiscriminator()
    {
        var context = TestTomlSerializerContextDefaultDerivedType.Default;
        GeneratedDefaultShape model = new GeneratedDefaultCircle { Color = "red", Radius = 5.0 };
        var toml = TomlSerializer.Serialize(model, context.GeneratedDefaultShape);

        Assert.DoesNotContain("type", toml);
        Assert.Contains("color = \"red\"", toml);
        Assert.Contains("radius = 5", toml);
    }

    [Fact]
    public void GeneratedContext_DefaultDerivedType_Serialize_WritesDiscriminator_ForNonDefault()
    {
        var context = TestTomlSerializerContextDefaultDerivedType.Default;
        GeneratedDefaultShape model = new GeneratedDefaultSquare { Color = "blue", Side = 3.0 };
        var toml = TomlSerializer.Serialize(model, context.GeneratedDefaultShape);

        Assert.Contains("type = \"square\"", toml);
        Assert.Contains("color = \"blue\"", toml);
        Assert.Contains("side = 3", toml);
    }

    [Fact]
    public void GeneratedContext_DefaultDerivedType_Deserialize_MissingDiscriminator()
    {
        var context = TestTomlSerializerContextDefaultDerivedType.Default;
        var toml = """
            color = "red"
            radius = 5.0
            """;

        var result = TomlSerializer.Deserialize(toml, context.GeneratedDefaultShape);

        Assert.IsType<GeneratedDefaultCircle>(result);
        var circle = (GeneratedDefaultCircle)result!;
        Assert.Equal("red", circle.Color);
        Assert.Equal(5.0, circle.Radius);
    }

    [Fact]
    public void GeneratedContext_DefaultDerivedType_Deserialize_UnknownDiscriminator()
    {
        var context = TestTomlSerializerContextDefaultDerivedType.Default;
        var toml = """
            type = "triangle"
            color = "green"
            """;

        var result = TomlSerializer.Deserialize(toml, context.GeneratedDefaultShape);

        Assert.IsType<GeneratedDefaultCircle>(result);
        Assert.Equal("green", ((GeneratedDefaultCircle)result!).Color);
    }

    [Fact]
    public void GeneratedContext_DefaultDerivedType_Roundtrip()
    {
        var context = TestTomlSerializerContextDefaultDerivedType.Default;
        GeneratedDefaultShape original = new GeneratedDefaultCircle { Color = "red", Radius = 5.0 };
        var toml = TomlSerializer.Serialize(original, context.GeneratedDefaultShape);
        var result = TomlSerializer.Deserialize(toml, context.GeneratedDefaultShape);

        Assert.IsType<GeneratedDefaultCircle>(result);
        var circle = (GeneratedDefaultCircle)result!;
        Assert.Equal("red", circle.Color);
        Assert.Equal(5.0, circle.Radius);
    }

    // --- Feature 2: UnknownDerivedTypeHandling on attribute (source-gen) ---

    [Fact]
    public void GeneratedContext_UnknownDiscriminator_FallbackViaTomlAttribute()
    {
        var context = TestTomlSerializerContextAttrFallback.Default;
        var toml = """
            kind = "unknown"
            name = "test"
            """;

        var result = TomlSerializer.Deserialize(toml, context.GeneratedAttrFallbackBase);

        Assert.IsType<GeneratedAttrFallbackBase>(result);
        Assert.Equal("test", result!.Name);
    }

    [Fact]
    public void GeneratedContext_UnknownDiscriminator_FallbackViaJsonAttribute()
    {
        var context = TestTomlSerializerContextJsonAttrFallback.Default;
        var toml = """
            kind = "unknown"
            name = "test"
            """;

        var result = TomlSerializer.Deserialize(toml, context.GeneratedJsonAttrFallbackBase);

        Assert.IsType<GeneratedJsonAttrFallbackBase>(result);
        Assert.Equal("test", result!.Name);
    }

    // --- Feature 3: Integer Discriminators (source-gen) ---

    [Fact]
    public void GeneratedContext_IntDiscriminator_Serialize()
    {
        var context = TestTomlSerializerContextIntDiscriminator.Default;
        GeneratedIntDiscrimShape model = new GeneratedIntDiscrimCircle { Color = "red", Radius = 5.0 };
        var toml = TomlSerializer.Serialize(model, context.GeneratedIntDiscrimShape);

        Assert.Contains("type = \"1\"", toml);
        Assert.Contains("color = \"red\"", toml);
        Assert.Contains("radius = 5", toml);
    }

    [Fact]
    public void GeneratedContext_IntDiscriminator_Deserialize()
    {
        var context = TestTomlSerializerContextIntDiscriminator.Default;
        var toml = """
            type = "2"
            color = "blue"
            side = 3.0
            """;

        var result = TomlSerializer.Deserialize(toml, context.GeneratedIntDiscrimShape);

        Assert.IsType<GeneratedIntDiscrimSquare>(result);
        var square = (GeneratedIntDiscrimSquare)result!;
        Assert.Equal("blue", square.Color);
        Assert.Equal(3.0, square.Side);
    }

    [Fact]
    public void GeneratedContext_IntDiscriminator_Roundtrip()
    {
        var context = TestTomlSerializerContextIntDiscriminator.Default;
        GeneratedIntDiscrimShape original = new GeneratedIntDiscrimCircle { Color = "red", Radius = 5.0 };
        var toml = TomlSerializer.Serialize(original, context.GeneratedIntDiscrimShape);
        var result = TomlSerializer.Deserialize(toml, context.GeneratedIntDiscrimShape);

        Assert.IsType<GeneratedIntDiscrimCircle>(result);
        var circle = (GeneratedIntDiscrimCircle)result!;
        Assert.Equal("red", circle.Color);
        Assert.Equal(5.0, circle.Radius);
    }
}
