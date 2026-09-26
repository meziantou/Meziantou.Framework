using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
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
    [TomlPropertyName("baseValue")]
    public string? Base { get; init; }
}

public sealed class GeneratedDerivedOptions : GeneratedBaseOptions
{
    [TomlPropertyName("derivedValue")]
    public string? Derived { get; init; }
}

public abstract class GeneratedOverriddenBaseOptions
{
    [TomlPropertyName("baseValue")]
    public virtual required string? Base { get; init; }
}

public sealed class GeneratedOverriddenDerivedOptions : GeneratedOverriddenBaseOptions
{
    [TomlPropertyName("derivedValue")]
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
    [TomlPropertyOrder(-10)]
    public int Age { get; set; }

    [TomlPropertyOrder(10)]
    public string Name { get; set; } = "";
}

public sealed class GeneratedReorderedPerson
{
    public string Name { get; set; } = "";

    [TomlPropertyOrder(-1)]
    public int Age { get; set; }

    [TomlPropertyOrder(-2)]
    public int Id { get; set; }
}

public sealed class GeneratedRequiredPerson
{
    [TomlRequired]
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
    [TomlConstructor]
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

    [TomlExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }
}

public sealed class GeneratedCtorPerson
{
    [TomlConstructor]
    public GeneratedCtorPerson(string firstName, int age = 42)
    {
        FirstName = firstName;
        Age = age;
    }

    [TomlPropertyName("first_name")]
    public string FirstName { get; }

    public int Age { get; }
}

public sealed class GeneratedCtorSelectionPerson
{
    public GeneratedCtorSelectionPerson()
    {
        Name = "default";
    }

    [TomlConstructor]
    public GeneratedCtorSelectionPerson(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

[TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[TomlDerivedType(typeof(GeneratedCat), "cat")]
[TomlDerivedType(typeof(GeneratedDog), "dog")]
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

public class GeneratedIncludedPrivateBaseMembers
{
    [TomlInclude]
    private int BaseProperty { get; set; } = 1;

    [TomlInclude]
#pragma warning disable IDE0044 // Set by the serializer
    private int _baseField = 2;
#pragma warning restore IDE0044

    public int GetBaseProperty() => BaseProperty;

    public int GetBaseField() => _baseField;
}

public sealed class GeneratedIncludedPrivateBaseMembersDerived : GeneratedIncludedPrivateBaseMembers
{
    public int Derived { get; set; } = 3;
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

[TomlConverter(typeof(TomlStringEnumConverter))]
public enum GeneratedStringEnumKind
{
    LogLevel,
    Time,
    Component,
}

[TomlConverter(typeof(TomlStringEnumConverter))]
[Flags]
public enum GeneratedTomlStringEnum
{
    None = 0,
    [TomlStringEnumMemberName("first-value")]
    First = 1,
    Second = 2,
}

public sealed class GeneratedTomlStringEnumPayload
{
    public GeneratedTomlStringEnum One { get; set; } = GeneratedTomlStringEnum.First;

    public GeneratedTomlStringEnum Both { get; set; } = GeneratedTomlStringEnum.First | GeneratedTomlStringEnum.Second;

    [TomlConverter(typeof(TomlStringEnumConverter))]
    public GeneratedEnumKind Member { get; set; } = GeneratedEnumKind.B;

    [TomlConverter(typeof(TomlStringEnumConverter))]
    public GeneratedEnumKind? NullableMember { get; set; } = GeneratedEnumKind.B;

    public GeneratedEnumKind Plain { get; set; } = GeneratedEnumKind.B;
}

public enum GeneratedCaseEnum
{
    A,
    a,
}

public enum GeneratedCaseCustomEnum
{
    [TomlStringEnumMemberName("b")]
    A,
    B,
}

public enum GeneratedConflictEnum
{
    [TomlStringEnumMemberName("B")]
    A,
    B,
}

[Flags]
public enum GeneratedCommaEnum
{
    None = 0,
    [TomlStringEnumMemberName("x,y")]
    A = 1,
    B = 2,
}

public sealed class GeneratedEnumNamesHolder<T>
    where T : struct, Enum
{
    [TomlConverter(typeof(TomlStringEnumConverter))]
    public T V { get; set; }
}

public sealed class GeneratedEnumOptionsPayload
{
    public GeneratedEnumKind Kind { get; set; } = GeneratedEnumKind.B;
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
[TomlSerializable(typeof(GeneratedIncludedPrivateBaseMembersDerived))]
internal sealed partial class TestTomlSerializerContextNonPublicSetters : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(IncludeFields = true)]
[TomlSerializable(typeof(GeneratedMemberSelectionModel))]
internal sealed partial class TestTomlSerializerContextIncludeFields : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(IncludeFields = true, IgnoreReadOnlyFields = true, IgnoreReadOnlyProperties = true)]
[TomlSerializable(typeof(GeneratedMemberSelectionModel))]
internal sealed partial class TestTomlSerializerContextIgnoreReadOnlyMembers : TomlSerializerContext
{
}

public sealed class GeneratedDomHolder
{
    public TomlTable? Table { get; set; }

    public TomlArray? Array { get; set; }

    public TomlTableArray? Items { get; set; }

    public GeneratedDomAnimal? Animal { get; set; }
}

[TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[TomlDerivedType(typeof(GeneratedDomCat), "cat")]
public abstract class GeneratedDomAnimal
{
    public string Name { get; set; } = "";
}

public sealed class GeneratedDomCat : GeneratedDomAnimal
{
    public int Lives { get; set; }
}

[TomlSerializable(typeof(GeneratedDomHolder))]
internal sealed partial class TestTomlSerializerContextDom : TomlSerializerContext
{
}

public sealed class GeneratedSetsRequiredMembers
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public GeneratedSetsRequiredMembers()
    {
        A = "from constructor";
        C = "from constructor";
    }

    public required string A { get; init; }

    public int B { get; set; }

    public required string C { get; set; }
}

public sealed class GeneratedSetsRequiredMembersWithParameters
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public GeneratedSetsRequiredMembersWithParameters(int b)
    {
        A = "from constructor";
        B = b;
    }

    public required string A { get; init; }

    public int B { get; }
}

[TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[TomlDerivedType(typeof(GeneratedConcreteCircle), "circle")]
public class GeneratedConcreteShape
{
    public string Name { get; set; } = "";
}

public sealed class GeneratedConcreteCircle : GeneratedConcreteShape
{
    public double Radius { get; set; }
}

public sealed class GeneratedConcreteSquare : GeneratedConcreteShape
{
}

[TomlSerializable(typeof(GeneratedConcreteShape))]
internal sealed partial class TestTomlSerializerContextConcretePolymorphicBase : TomlSerializerContext
{
}

public sealed class GeneratedReadOnlySetHolder
{
    public IReadOnlySet<int> Values { get; set; } = new HashSet<int>();
}

[TomlSerializable(typeof(GeneratedReadOnlySetHolder))]
internal sealed partial class TestTomlSerializerContextReadOnlySet : TomlSerializerContext
{
}

[TomlSerializable(typeof(GeneratedSetsRequiredMembers))]
[TomlSerializable(typeof(GeneratedSetsRequiredMembersWithParameters))]
internal sealed partial class TestTomlSerializerContextSetsRequiredMembers : TomlSerializerContext
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

[TomlSourceGenerationOptions(Converters = [typeof(TomlStringEnumConverter)])]
[TomlSerializable(typeof(GeneratedEnumOptionsPayload))]
internal sealed partial class TestTomlSerializerContextStringEnumOptions : TomlSerializerContext
{
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedStringEnumPayload))]
[TomlSerializable(typeof(GeneratedTomlStringEnumPayload))]
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

public sealed class GeneratedConstructionCounter
{
    [ThreadStatic]
    private static int s_constructions;

    public GeneratedConstructionCounter() => s_constructions++;

    public static int Constructions { get => s_constructions; set => s_constructions = value; }

    public required int Id { get; set; }

    public string Name { get; init; } = "default";

    public int Plain { get; set; } = 5;
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly struct GeneratedInitStruct
{
    public int A { get; init; }

    public int B { get; init; }
}

public sealed class GeneratedGenericInit<T>
{
    public T? Value { get; init; }

    public string Name { get; init; } = "default";
}

public sealed class GeneratedThrowingSetter
{
    public int Q { get => GetType().Name.Length; set => throw new ArgumentOutOfRangeException(nameof(value), GetType().Name); }
}

public sealed class GeneratedThrowingGetter
{
    public int Q => throw new InvalidOperationException(GetType().Name);
}

public sealed class GeneratedThrowingConstructorWithoutParameters
{
    public GeneratedThrowingConstructorWithoutParameters() => throw new InvalidOperationException("constructor");

    public int Q { get; set; }
}

public sealed class GeneratedThrowingMembersHolder
{
    public GeneratedThrowingSetter? Setter { get; set; }

    public GeneratedThrowingGetter? Getter { get; set; }

    public GeneratedThrowingConstructorWithoutParameters? Constructor { get; set; }
}

public sealed class GeneratedPrivateSetterExtensionData
{
    public int A { get; set; }

    [TomlExtensionData]
    [TomlInclude]
    public Dictionary<string, object?>? Ext { get; private set; }
}

public sealed class GeneratedPrivateInitExtensionData
{
    public int A { get; set; }

    [TomlExtensionData]
    [TomlInclude]
    public Dictionary<string, object?>? Ext { get; private init; }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly struct GeneratedStructWithConstructor
{
    [TomlConstructor]
    public GeneratedStructWithConstructor(int x) => X = x * 10;

    public int X { get; }
}

public sealed class GeneratedNormalizingConstructor
{
    public GeneratedNormalizingConstructor(string name) => Name = name.ToUpperInvariant();

    public string Name { get; set; }
}

public sealed class GeneratedConvertingConstructor
{
    public GeneratedConvertingConstructor(string x) => X = int.Parse(x, System.Globalization.CultureInfo.InvariantCulture);

    public int X { get; set; }
}

public sealed class GeneratedRequiredOfAnotherType
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public GeneratedRequiredOfAnotherType(int? age) => Age = (age ?? 0) + 100;

    public required int Age { get; init; }
}

// The generated code used to assign the int? parameter to the int member (CS0266)
public sealed class GeneratedRequiredOfAnotherTypeWithoutSetsRequiredMembers
{
    public GeneratedRequiredOfAnotherTypeWithoutSetsRequiredMembers(int? age) => Age = (age ?? 0) + 100;

    public required int Age { get; init; }
}

public sealed class GeneratedInitOfAnotherType
{
    public GeneratedInitOfAnotherType(int? age) => Age = (age ?? 0) + 100;

    public int Age { get; init; }
}

public enum GeneratedDefaultColor
{
    Negative = -1,
    Red,
    Green,
}

public sealed record GeneratedDefaultValues(string Name, GeneratedDefaultColor? Color = GeneratedDefaultColor.Green, GeneratedDefaultColor Negative = GeneratedDefaultColor.Negative, int? Count = 5);

public sealed class GeneratedAmbiguousConstructors
{
    public GeneratedAmbiguousConstructors(int value) => Value = value;

    public GeneratedAmbiguousConstructors(string value) => Value = value.Length;

    public int Value { get; }
}

public sealed class GeneratedMultipleAnnotatedConstructors
{
    [TomlConstructor]
    public GeneratedMultipleAnnotatedConstructors(int value) => Value = value;

    [TomlConstructor]
    public GeneratedMultipleAnnotatedConstructors(string value) => Value = value.Length;

    public int Value { get; }
}

[TomlSerializable(typeof(GeneratedThrowingMembersHolder))]
[TomlSerializable(typeof(GeneratedPrivateSetterExtensionData))]
[TomlSerializable(typeof(GeneratedPrivateInitExtensionData))]
[TomlSerializable(typeof(GeneratedStructWithConstructor))]
[TomlSerializable(typeof(GeneratedNormalizingConstructor))]
[TomlSerializable(typeof(GeneratedConvertingConstructor))]
[TomlSerializable(typeof(GeneratedRequiredOfAnotherType))]
[TomlSerializable(typeof(GeneratedInitOfAnotherType))]
[TomlSerializable(typeof(GeneratedRequiredOfAnotherTypeWithoutSetsRequiredMembers))]
[TomlSerializable(typeof(GeneratedDefaultValues))]
[TomlSerializable(typeof(GeneratedReorderedPerson))]
[TomlSerializable(typeof(GeneratedAmbiguousConstructors))]
[TomlSerializable(typeof(GeneratedMultipleAnnotatedConstructors))]
[TomlSerializable(typeof(GeneratedConstructionCounter))]
[TomlSerializable(typeof(GeneratedInitStruct))]
[TomlSerializable(typeof(GeneratedGenericInit<int>))]
internal sealed partial class TestTomlSerializerContextSingleConstruction : TomlSerializerContext
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

public sealed class GeneratedThrowingConstructor
{
    public GeneratedThrowingConstructor(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    public int Value { get; }
}

public sealed class GeneratedThrowingConstructorHolder
{
    public GeneratedThrowingConstructor? Inner { get; set; }
}

[TomlSerializable(typeof(GeneratedThrowingConstructorHolder))]
internal sealed partial class TestTomlSerializerContextThrowingConstructor : TomlSerializerContext
{
}

public abstract class AbstractWithoutPolymorphism
{
    public int Value { get; set; }
}

public sealed class AbstractWithoutPolymorphismHolder
{
    public AbstractWithoutPolymorphism? Inner { get; set; }
}

public sealed class GeneratedShapeCollectionHolder
{
    public IList<GeneratedDefaultShape> Shapes { get; set; } = [];
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedShapeCollectionHolder))]
internal sealed partial class TestTomlSerializerContextShapeCollection : TomlSerializerContext
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

public class GeneratedPrivateHidingBase
{
    public string Name { get; set; } = "base";
}

public class GeneratedPrivateHidingMiddle : GeneratedPrivateHidingBase
{
    private new string Name { get; set; } = "private";

    public string GetPrivateName() => Name;
}

public sealed class GeneratedPrivateHidingDerived : GeneratedPrivateHidingMiddle
{
    public int Extra { get; set; } = 1;
}

[TomlSerializable(typeof(GeneratedPrivateHidingDerived))]
internal sealed partial class TestTomlSerializerContextPrivateHiding : TomlSerializerContext
{
}

public sealed class GeneratedRequiredLocationRoot
{
    public GeneratedRequiredLocationChild A { get; set; } = new() { Z = 0 };

    public GeneratedRequiredLocationRecord B { get; set; } = new(0, 0);
}

public sealed class GeneratedRequiredLocationChild
{
    public required int Z { get; set; }

    public int X { get; set; }
}

public sealed record GeneratedRequiredLocationRecord(int Y, int W);

public sealed class GeneratedSeveralRequired
{
    public int B { get; set; }

    [TomlRequired]
    public int Req { get; set; }

    [TomlRequired]
    public int Req2 { get; set; }
}

public sealed record GeneratedSeveralRequiredRecord(int B, int Req, int Req2);

[TomlSourceGenerationOptions(RespectRequiredConstructorParameters = true)]
[TomlSerializable(typeof(GeneratedRequiredLocationRoot))]
[TomlSerializable(typeof(GeneratedRequiredLocationChild))]
[TomlSerializable(typeof(GeneratedSeveralRequired))]
[TomlSerializable(typeof(GeneratedSeveralRequiredRecord))]
internal sealed partial class TestTomlSerializerContextRequiredLocation : TomlSerializerContext
{
}

public sealed class GeneratedAggregationRoot
{
    public IList<int> Numbers { get; set; } = [];

    public GeneratedAggregationRecord First { get; set; } = new(0);

    public GeneratedAggregationRecord Second { get; set; } = new(0);

    public GeneratedAggregationChild C1 { get; set; } = new();

    public GeneratedAggregationChild C2 { get; set; } = new();

    public GeneratedRequiredLocationChild R1 { get; set; } = new() { Z = 0 };

    public GeneratedRequiredLocationChild R2 { get; set; } = new() { Z = 0 };
}

public sealed record GeneratedAggregationRecord(int Value);

public sealed class GeneratedAggregationChild
{
    public int Value { get; set; }
}

[TomlSerializable(typeof(GeneratedAggregationRoot))]
internal sealed partial class TestTomlSerializerContextAggregation : TomlSerializerContext
{
}

public sealed class GeneratedValidatedServer : ITomlOnDeserialized
{
    public int Port { get; set; } = 80;

    public void OnTomlDeserialized()
    {
        if (Port <= 0)
        {
            throw new InvalidOperationException("Port must be positive.");
        }
    }
}

public sealed class GeneratedValidatedHolder : ITomlOnDeserialized
{
    public int Port { get; set; } = 80;

    public GeneratedValidatedServer? Server { get; set; }

    public List<GeneratedValidatedServer>? Servers { get; set; }

    public void OnTomlDeserialized()
    {
        if (Port <= 0)
        {
            throw new InvalidOperationException("Port must be positive.");
        }
    }
}

[TomlSerializable(typeof(GeneratedValidatedHolder))]
internal sealed partial class TestTomlSerializerContextValidated : TomlSerializerContext
{
}

public sealed class GeneratedPrivateGetterChild
{
    public int X { get; set; }
}

public sealed class GeneratedPrivateGetterModel
{
    [TomlInclude]
    [TomlObjectCreationHandling(Meziantou.Framework.Toml.TomlObjectCreationHandling.Populate)]
    private GeneratedPrivateGetterChild Child { get; } = new();

    [TomlInclude]
    private List<int> Items { get; } = [1];

    [TomlInclude]
    public GeneratedPrivateGetterChild? Settable { private get; set; }

    [TomlInclude]
    [TomlObjectCreationHandling(Meziantou.Framework.Toml.TomlObjectCreationHandling.Populate)]
    public List<int>? Populated { private get; set; } = [1];

    [TomlInclude]
    [TomlSingleOrArray]
    private List<int> Single { get; } = [1];

    public GeneratedPrivateGetterChild GetChild() => Child;

    public List<int> GetItems() => Items;

    public GeneratedPrivateGetterChild? GetSettable() => Settable;

    public List<int>? GetPopulated() => Populated;

    public List<int> GetSingle() => Single;
}

[TomlSerializable(typeof(GeneratedPrivateGetterModel))]
internal sealed partial class TestTomlSerializerContextPrivateGetter : TomlSerializerContext
{
}

public enum GeneratedManyErrorsKind
{
    A,
    B,
}

public sealed class GeneratedManyErrorsItem
{
    public int X { get; set; }

    public GeneratedManyErrorsKind E { get; set; }
}

public sealed record GeneratedManyErrorsRecord(int X, GeneratedManyErrorsKind E);

public sealed class GeneratedManyErrorsRoot
{
    public IList<GeneratedManyErrorsItem>? L { get; set; }

    public IList<GeneratedManyErrorsRecord>? R { get; set; }

    public IList<GeneratedSeveralRequired>? Q { get; set; }
}

[TomlSerializable(typeof(GeneratedManyErrorsRoot))]
internal sealed partial class TestTomlSerializerContextManyErrors : TomlSerializerContext
{
}

public sealed record GeneratedEscapeRecord(int X);

public sealed class GeneratedEscapeChild
{
    public string? A { get; set; }

    [TomlExtensionData]
    public Dictionary<string, GeneratedEscapeRecord>? Ext { get; set; }
}

public sealed class GeneratedEscapeHolder
{
    public GeneratedEscapeRecord? Rec { get; set; }

    public string? N { get; set; }
}

// Reads a nested value with the metadata of the options, like a user converter composing other types
public sealed class GeneratedEscapeHolderConverter : TomlConverter<GeneratedEscapeHolder>
{
    public override GeneratedEscapeHolder Read(TomlReader reader)
    {
        var value = new GeneratedEscapeHolder();
        reader.Read();
        while (reader.TokenType == TomlTokenType.PropertyName)
        {
            var name = reader.PropertyName;
            reader.Read();
            if (name == "Rec")
            {
                value.Rec = reader.Options.GetTypeInfo<GeneratedEscapeRecord>().Read(reader);
            }
            else
            {
                value.N = reader.GetString();
                reader.Read();
            }
        }

        reader.Read();
        return value;
    }

    public override void Write(TomlWriter writer, GeneratedEscapeHolder value) => throw new NotSupportedException();
}

public sealed class GeneratedEscapeRoot
{
    public GeneratedEscapeChild? Child { get; set; }

    [TomlConverter(typeof(GeneratedEscapeHolderConverter))]
    public GeneratedEscapeHolder? W { get; set; }

    public int A { get; set; }

    public int N { get; set; }
}

[TomlSerializable(typeof(GeneratedEscapeRoot))]
internal sealed partial class TestTomlSerializerContextEscape : TomlSerializerContext
{
}

public class GeneratedAccessorBase
{
    public string X { get; init; } = "x-init";
}

public class GeneratedAccessorDerived : GeneratedAccessorBase
{
    [TomlInclude]
    public string Y { get; private set; } = "y-init";

    public required string R { get; init; }

    public string GetY() => Y;
}

public class GeneratedAccessorSameType
{
    public string P { get; init; } = "p";

    public required string Q { get; init; }

    // The generated code sets the field through an accessor
#pragma warning disable IDE0044 // Make field readonly
    [TomlInclude]
    private string _f = "f";
#pragma warning restore IDE0044

    public string GetF() => _f;
}

[TomlSerializable(typeof(GeneratedAccessorDerived))]
[TomlSerializable(typeof(GeneratedAccessorSameType))]
internal sealed partial class TestTomlSerializerContextAccessorNames : TomlSerializerContext
{
}

public sealed class GeneratedRecursiveNode
{
    public string? Name { get; set; }

    public GeneratedRecursiveNode? Next { get; set; }
}

public sealed class GeneratedMutualA
{
    public GeneratedMutualB? B { get; set; }
}

public sealed class GeneratedMutualB
{
    public GeneratedMutualA? A { get; set; }

    public int Value { get; set; }
}

public sealed class GeneratedObsoleteModel
{
    [Obsolete("Use B")]
    public int Old { get; set; }

    [Obsolete("Use B", error: true)]
    public int OldError { get; set; }

    public int B { get; set; }
}

public sealed class GeneratedObsoleteConstructor
{
    [Obsolete("Use the factory", error: true)]
    public GeneratedObsoleteConstructor()
    {
    }

    public int B { get; set; }
}

public class GeneratedHidingAccessorBase
{
    public int X { get; set; }
}

public sealed class GeneratedHidingAccessorDerived : GeneratedHidingAccessorBase
{
    [TomlInclude]
    public new string X { get; private set; } = "d";
}

public sealed class GeneratedPrivateHidingAccessorDerived : GeneratedHidingAccessorBase
{
    [TomlInclude]
    private new string X { get; set; } = "d2";

    public string GetX() => X;
}

[TomlSerializable(typeof(GeneratedHidingAccessorDerived))]
[TomlSerializable(typeof(GeneratedPrivateHidingAccessorDerived))]
internal sealed partial class TestTomlSerializerContextHidingAccessors : TomlSerializerContext
{
}

public sealed class GeneratedCtorInit
{
    public GeneratedCtorInit(int a) => A = a;

    public int A { get; }

    public string Q { get; init; } = "q-init";

    public int Z { get; set; }
}

public sealed class GeneratedGenericInitPopulate<T>
{
    public T? V { get; init; }

    public int Z { get; set; }
}

public sealed class GeneratedInitPopulateHolder
{
    [TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
    public GeneratedCtorInit C { get; } = new(1) { Q = "q0" };

    [TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
    public GeneratedCtorInit C2 { get; set; } = new(3) { Q = "q3" };

    [TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
    public GeneratedGenericInitPopulate<string> G { get; } = new() { V = "v0" };
}

[TomlSerializable(typeof(GeneratedInitPopulateHolder))]
internal sealed partial class TestTomlSerializerContextInitPopulate : TomlSerializerContext
{
}

public sealed class GeneratedExtensionKeyPolicyModel
{
    public string Name { get; set; } = "";

    [TomlExtensionData]
    public Dictionary<string, object> Extra { get; set; } = [];
}

[TomlSourceGenerationOptions(DictionaryKeyPolicy = TomlKnownNamingPolicy.CamelCase)]
[TomlSerializable(typeof(GeneratedExtensionKeyPolicyModel))]
internal sealed partial class TestTomlSerializerContextExtensionKeyPolicy : TomlSerializerContext
{
}

[TomlSerializable(typeof(GeneratedObsoleteModel))]
[TomlSerializable(typeof(GeneratedObsoleteConstructor))]
internal sealed partial class TestTomlSerializerContextObsolete : TomlSerializerContext
{
}

[TomlSerializable(typeof(GeneratedRecursiveNode))]
[TomlSerializable(typeof(GeneratedMutualA))]
internal sealed partial class TestTomlSerializerContextRecursive : TomlSerializerContext
{
}

[TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
public sealed class GeneratedRequiredPopulateModel
{
    public required IList<string> Required { get; set; } = ["pre"];

    public IList<string> Other { get; set; } = ["pre"];

    [TomlIgnore(Condition = TomlIgnoreCondition.WhenReading)]
    public required string Unread { get; set; } = "keep";

    public required string Name { get; init; }

    public GeneratedPopulatePoint Point { get; init; } = new() { X = 1, Y = 2 };
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public struct GeneratedPopulatePoint
{
    public int X { get; set; }

    public int Y { get; set; }
}

public sealed class GeneratedInitExtensionDataModel : ITomlOnDeserializing
{
    [TomlIgnore]
    public required string R { get; init; } = "r-init";

    public required string A { get; init; }

    [TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
    public IList<int> P { get; init; } = [1];

    [TomlExtensionData]
    public Dictionary<string, object> Ext { get; init; } = [];

    public string? AWhenDeserializing { get; private set; }

    public void OnTomlDeserializing() => AWhenDeserializing = A;
}

[TomlSerializable(typeof(GeneratedInitExtensionDataModel))]
internal sealed partial class TestTomlSerializerContextInitExtensionData : TomlSerializerContext
{
}

[TomlSerializable(typeof(GeneratedRequiredPopulateModel))]
internal sealed partial class TestTomlSerializerContextRequiredPopulate : TomlSerializerContext
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
    public void ExceptionsFromTheModel_AreTheSameOnBothPaths()
    {
        var typeInfo = TestTomlSerializerContextSingleConstruction.Default.GeneratedThrowingMembersHolder;

        Assert.Throws<ArgumentOutOfRangeException>(() => TomlSerializer.Deserialize<GeneratedThrowingMembersHolder>("[Setter]\nQ = 1\n"));
        Assert.Throws<ArgumentOutOfRangeException>(() => TomlSerializer.Deserialize("[Setter]\nQ = 1\n", typeInfo));
        Assert.Throws<InvalidOperationException>(() => TomlSerializer.Serialize(new GeneratedThrowingMembersHolder { Getter = new() }));
        Assert.Throws<InvalidOperationException>(() => TomlSerializer.Serialize(new GeneratedThrowingMembersHolder { Getter = new() }, typeInfo));

        var reflection = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedThrowingMembersHolder>("[Constructor]\nQ = 1\n"));
        var generated = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("[Constructor]\nQ = 1\n", typeInfo));
        Assert.Equal(reflection.Message, generated.Message);
        Assert.Contains("Failed to create an instance", generated.Message);
        Assert.False(TomlSerializer.TryDeserialize("[Constructor]\nQ = 1\n", typeInfo, out _));
    }

    [Fact]
    public void ExtensionDataWithANonPublicSetter_IsInitializedByBothPaths()
    {
        var context = TestTomlSerializerContextSingleConstruction.Default;
        const string Toml = "A = 1\nz = 2\n";

        Assert.Equal(2L, TomlSerializer.Deserialize<GeneratedPrivateSetterExtensionData>(Toml)!.Ext!["z"]);
        Assert.Equal(2L, TomlSerializer.Deserialize(Toml, context.GeneratedPrivateSetterExtensionData)!.Ext!["z"]);
        Assert.Equal(2L, TomlSerializer.Deserialize<GeneratedPrivateInitExtensionData>(Toml)!.Ext!["z"]);
        Assert.Equal(2L, TomlSerializer.Deserialize(Toml, context.GeneratedPrivateInitExtensionData)!.Ext!["z"]);
    }

    [Fact]
    public void StructWithAnnotatedConstructor_IsCreatedWithIt()
    {
        Assert.Equal(50, TomlSerializer.Deserialize<GeneratedStructWithConstructor>("X = 5\n").X);
        Assert.Equal(50, TomlSerializer.Deserialize("X = 5\n", TestTomlSerializerContextSingleConstruction.Default.GeneratedStructWithConstructor).X);
    }

    [Fact]
    public void MembersBoundToConstructorParameters_GetTheirValueFromTheConstructorOnly()
    {
        var context = TestTomlSerializerContextSingleConstruction.Default;

        Assert.Equal("ABC", TomlSerializer.Deserialize<GeneratedNormalizingConstructor>("Name = \"abc\"\n")!.Name);
        Assert.Equal("ABC", TomlSerializer.Deserialize("Name = \"abc\"\n", context.GeneratedNormalizingConstructor)!.Name);
        Assert.True(TomlSerializer.TryDeserialize<GeneratedConvertingConstructor>("X = \"5\"\n", out var reflection));
        Assert.Equal(5, reflection.X);
        Assert.Equal(5, TomlSerializer.Deserialize("X = \"5\"\n", context.GeneratedConvertingConstructor)!.X);
        Assert.Equal(103, TomlSerializer.Deserialize<GeneratedInitOfAnotherType>("Age = 3\n")!.Age);
        Assert.Equal(103, TomlSerializer.Deserialize("Age = 3\n", context.GeneratedInitOfAnotherType)!.Age);
        Assert.Equal(103, TomlSerializer.Deserialize<GeneratedRequiredOfAnotherType>("Age = 3\n")!.Age);
        Assert.Equal(103, TomlSerializer.Deserialize("Age = 3\n", context.GeneratedRequiredOfAnotherType)!.Age);
    }

    [Fact]
    public void ConstructorDefaultValues_OfNullableAndEnumParameters_AreUsed()
    {
        var expected = new GeneratedDefaultValues("a");

        Assert.Equal(expected, TomlSerializer.Deserialize<GeneratedDefaultValues>("Name = \"a\"\n"));
        Assert.Equal(expected, TomlSerializer.Deserialize("Name = \"a\"\n", TestTomlSerializerContextSingleConstruction.Default.GeneratedDefaultValues));
    }

    [Fact]
    public void DefaultMappingOrder_HonorsPropertyOrderAttributes()
    {
        var value = new GeneratedReorderedPerson { Name = "Ada", Age = 37, Id = 1 };

        var reflection = TomlSerializer.Serialize(value);
        var generated = TomlSerializer.Serialize(value, TestTomlSerializerContextSingleConstruction.Default.GeneratedReorderedPerson);

        Assert.Equal("Id = 1\nAge = 37\nName = \"Ada\"\n", reflection);
        Assert.Equal(reflection, generated);
        Assert.Equal(TomlMappingOrderPolicy.OrderThenDeclaration, TomlSerializerOptions.Default.MappingOrder);
    }

    [Fact]
    public void GeneratedContext_RespectsTomlPropertyOrder_WhenWriting()
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
    public void GeneratedContext_CapturesTomlExtensionData_WhenReading()
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
    public void GeneratedContext_CanDeserializeUsingTomlConstructor_AndDefaultValues()
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
    public void GeneratedContext_PrefersAnnotatedTomlConstructor_WhenMultipleAreAvailable()
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
    public void GeneratedContext_PolymorphicBaseInstance_IsWrittenLikeReflection()
    {
        var typeInfo = TestTomlSerializerContextConcretePolymorphicBase.Default.GeneratedConcreteShape;

        Assert.Equal("Name = \"s\"\n", TomlSerializer.Serialize(new GeneratedConcreteShape { Name = "s" }, typeInfo));
        Assert.Equal(TomlSerializer.Serialize<GeneratedConcreteShape>(new GeneratedConcreteShape { Name = "s" }), TomlSerializer.Serialize(new GeneratedConcreteShape { Name = "s" }, typeInfo));

        var generatedError = Assert.Throws<TomlException>(() => TomlSerializer.Serialize<GeneratedConcreteShape>(new GeneratedConcreteSquare(), typeInfo));
        var reflectionError = Assert.Throws<TomlException>(() => TomlSerializer.Serialize<GeneratedConcreteShape>(new GeneratedConcreteSquare()));
        Assert.Equal(reflectionError.Message, generatedError.Message);
    }

    [Fact]
    public void ReadOnlySet_RoundTripsWithReflectionAndGeneratedCode()
    {
        var typeInfo = TestTomlSerializerContextReadOnlySet.Default.GeneratedReadOnlySetHolder;
        var value = new GeneratedReadOnlySetHolder { Values = new HashSet<int> { 1, 2 } };

        var generated = TomlSerializer.Serialize(value, typeInfo);
        var reflection = TomlSerializer.Serialize(value);

        Assert.Equal("Values = [1, 2]\n", generated);
        Assert.Equal(generated, reflection);
        Assert.True(TomlSerializer.Deserialize(generated, typeInfo)!.Values.SetEquals([1, 2]));
        Assert.True(TomlSerializer.Deserialize<GeneratedReadOnlySetHolder>(reflection)!.Values.SetEquals([1, 2]));
        Assert.True(TomlSerializer.Deserialize<IReadOnlySet<string>>("value = ['a']", new TomlSerializerOptions { RootValueHandling = TomlRootValueHandling.WrapInRootKey })!.Contains("a"));
    }

    [Fact]
    public void GeneratedContext_SetsRequiredMembersConstructor_KeepsTheValuesItSets()
    {
        var context = TestTomlSerializerContextSetsRequiredMembers.Default;

        var value = TomlSerializer.Deserialize("B = 1", context.GeneratedSetsRequiredMembers)!;
        var withParameters = TomlSerializer.Deserialize("B = 2", context.GeneratedSetsRequiredMembersWithParameters)!;

        Assert.Equal("from constructor", value.A);
        Assert.Equal("from constructor", value.C);
        Assert.Equal(1, value.B);
        Assert.Equal("from constructor", withParameters.A);
        Assert.Equal(2, withParameters.B);
        Assert.Equal("from constructor", TomlSerializer.Deserialize<GeneratedSetsRequiredMembers>("B = 1")!.A);
        Assert.Equal("x", TomlSerializer.Deserialize("A = 'x'", context.GeneratedSetsRequiredMembers)!.A);
    }

    [Fact]
    public void GeneratedContext_DomMembers_AreWritten()
    {
        var value = new GeneratedDomHolder
        {
            Table = new TomlTable { ["a"] = 1L },
            Array = new TomlArray { 1L, "x" },
            Items = new TomlTableArray { new TomlTable { ["b"] = 2L } },
            Animal = new GeneratedDomCat { Name = "Tom", Lives = 9 },
        };

        var toml = TomlSerializer.Serialize(value, TestTomlSerializerContextDom.Default.GeneratedDomHolder);

        Assert.Equal(TomlSerializer.Serialize(value), toml);
        var roundtrip = TomlSerializer.Deserialize(toml, TestTomlSerializerContextDom.Default.GeneratedDomHolder)!;
        Assert.Equal(1L, roundtrip.Table!["a"]);
        Assert.Equal("x", roundtrip.Array![1]);
        Assert.Equal(2L, roundtrip.Items![0]["b"]);
        Assert.Equal(9, Assert.IsType<GeneratedDomCat>(roundtrip.Animal).Lives);
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
    public void GeneratedContext_UnmappedMemberHandlingAttribute_OverridesOptions()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("Unknown = 1", TestTomlSerializerContextValidationDefault.Default.TomlDisallowUnmappedModel));
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
    public void GeneratedContext_CanDeserializeOverriddenProperties_WithInheritedTomlPropertyName()
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
    public void GeneratedContext_CanSerializeOverriddenProperties_WithInheritedTomlPropertyName()
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
    public void GeneratedContext_ResolvesRuntimeConverters_OnceWhenFirstUsed()
    {
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var factory = new CountingRuntimeGuidConverterFactory("ref:id", id);
        var context = TestTomlSerializerContext.Default;
        var options = context.Options with { Converters = [factory] };

        // The member types are resolved when first used, which lets a recursive type reference itself
        var typeInfo = (TomlTypeInfo<GeneratedRuntimeConverterHolder>)context.GetTypeInfo(typeof(GeneratedRuntimeConverterHolder), options)!;
        TomlSerializer.Serialize(new GeneratedRuntimeConverterHolder { Id = id }, typeInfo);
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
    public void GeneratedContext_EnumWithTomlStringEnumConverter_UsesStrings()
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

    [Fact]
    public void TomlStringEnumConverter_WritesAndReadsNames()
    {
        const string Expected = "one = \"first-value\"\nboth = \"first-value, Second\"\nmember = \"B\"\nnullableMember = \"B\"\nplain = 1\n";
        var typeInfo = TestTomlSerializerContextStringEnums.Default.GeneratedTomlStringEnumPayload;
        var camelCase = new TomlSerializerOptions { PropertyNamingPolicy = TomlNamingPolicy.CamelCase };

        Assert.Equal(Expected, TomlSerializer.Serialize(new GeneratedTomlStringEnumPayload(), camelCase).ReplaceLineEndings("\n"));
        Assert.Equal(Expected, TomlSerializer.Serialize(new GeneratedTomlStringEnumPayload(), typeInfo).ReplaceLineEndings("\n"));

        const string Toml = "one = 'FIRST-VALUE'\nboth = 'Second, first-value'\nmember = 'a'\nnullableMember = 'a'\nplain = 'A'\n";
        foreach (var value in new[] { TomlSerializer.Deserialize<GeneratedTomlStringEnumPayload>(Toml, camelCase)!, TomlSerializer.Deserialize(Toml, typeInfo)! })
        {
            Assert.Equal(GeneratedTomlStringEnum.First, value.One);
            Assert.Equal(GeneratedTomlStringEnum.First | GeneratedTomlStringEnum.Second, value.Both);
            Assert.Equal(GeneratedEnumKind.A, value.Member);
            Assert.Equal(GeneratedEnumKind.A, value.NullableMember);
            Assert.Equal(GeneratedEnumKind.A, value.Plain);
        }
    }

    [Fact]
    public void TomlStringEnumConverter_PrefersTheExactName()
    {
        Assert.Equal(GeneratedCaseEnum.a, Roundtrip(GeneratedCaseEnum.a));
        Assert.Equal(GeneratedCaseEnum.A, Roundtrip(GeneratedCaseEnum.A));
        Assert.Equal(GeneratedCaseCustomEnum.A, Roundtrip(GeneratedCaseCustomEnum.A));
        Assert.Equal(GeneratedCaseCustomEnum.B, Roundtrip(GeneratedCaseCustomEnum.B));
        Assert.Equal(GeneratedCaseEnum.a, TomlSerializer.Deserialize<Dictionary<string, GeneratedCaseEnum>>("V = 'a'")!["V"]);

        static T Roundtrip<T>(T value)
            where T : struct, Enum
            => TomlSerializer.Deserialize<GeneratedEnumNamesHolder<T>>(TomlSerializer.Serialize(new GeneratedEnumNamesHolder<T> { V = value }))!.V;
    }

    [Fact]
    public void TomlStringEnumConverter_InvalidMemberNames_AreConfigurationErrors()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new GeneratedEnumNamesHolder<GeneratedConflictEnum> { V = GeneratedConflictEnum.B }));
        Assert.Throws<TomlException>(() => TomlSerializer.TryDeserialize<GeneratedEnumNamesHolder<GeneratedConflictEnum>>("V = 'B'", out _));
        Assert.Throws<TomlException>(() => TomlSerializer.TryDeserialize<GeneratedEnumNamesHolder<GeneratedCommaEnum>>("V = 'B'", out _));
    }

    // The names of each enum are cached; the cache must not keep the enums of a collectible assembly alive
    [Fact(DisableParallelization = true)]
    public void TomlStringEnumConverter_DoesNotKeepCollectibleEnumsAlive()
    {
        var enumType = SerializeCollectibleEnum();
        for (var i = 0; i < 10 && enumType.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(enumType.IsAlive);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static WeakReference SerializeCollectibleEnum()
        {
            var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("CollectibleTomlEnum"), System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect);
            var enumBuilder = assembly.DefineDynamicModule("CollectibleTomlEnum").DefineEnum("CollectibleEnum", System.Reflection.TypeAttributes.Public, typeof(int));
            enumBuilder.DefineLiteral("First", 1);
            var enumType = enumBuilder.CreateType();
            var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), enumType);
            var dictionary = (System.Collections.IDictionary)Activator.CreateInstance(dictionaryType)!;
            dictionary["V"] = Enum.ToObject(enumType, 1);

            var toml = TomlSerializer.Serialize(dictionary, dictionaryType, new TomlSerializerOptions { Converters = [new TomlStringEnumConverter()] });

            Assert.Equal("V = \"First\"", toml.Trim());
            return new WeakReference(enumType);
        }
    }

    [Fact]
    public void TomlStringEnumConverter_InOptions_AppliesToEveryEnum()
    {
        var options = new TomlSerializerOptions { Converters = [new TomlStringEnumConverter()] };

        Assert.Equal("Kind = \"B\"\n", TomlSerializer.Serialize(new GeneratedEnumOptionsPayload(), options).ReplaceLineEndings("\n"));
        Assert.Equal("Kind = \"B\"\n", TomlSerializer.Serialize(new GeneratedEnumOptionsPayload(), TestTomlSerializerContextStringEnumOptions.Default.GeneratedEnumOptionsPayload).ReplaceLineEndings("\n"));
        Assert.Equal(GeneratedEnumKind.A, TomlSerializer.Deserialize("Kind = 'A'", TestTomlSerializerContextStringEnumOptions.Default.GeneratedEnumOptionsPayload)!.Kind);
    }

    [Fact]
    public void TomlStringEnumMemberNameAttribute_RejectsAnEmptyName()
    {
        Assert.Throws<ArgumentException>(() => new TomlStringEnumMemberNameAttribute(""));
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
    public void TypeWithoutASelectableConstructor_IsSerializedAndReportsTheSameReadErrorOnBothPaths()
    {
        var context = TestTomlSerializerContextSingleConstruction.Default;

        Assert.Equal("Value = 1\n", TomlSerializer.Serialize(new GeneratedAmbiguousConstructors(1)));
        Assert.Equal("Value = 1\n", TomlSerializer.Serialize(new GeneratedAmbiguousConstructors(1), context.GeneratedAmbiguousConstructors));
        Assert.Equal("Value = 1\n", TomlSerializer.Serialize(new GeneratedMultipleAnnotatedConstructors(1)));
        Assert.Equal("Value = 1\n", TomlSerializer.Serialize(new GeneratedMultipleAnnotatedConstructors(1), context.GeneratedMultipleAnnotatedConstructors));

        var ambiguous = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedAmbiguousConstructors>("Value = 1\n"));
        var multiple = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedMultipleAnnotatedConstructors>("Value = 1\n"));

        Assert.Contains($"No suitable constructor could be selected for type '{typeof(GeneratedAmbiguousConstructors).FullName}'.", ambiguous.Message);
        Assert.Contains($"Multiple constructors on type '{typeof(GeneratedMultipleAnnotatedConstructors).FullName}' are annotated", multiple.Message);
        Assert.Equal(ambiguous.Message, Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("Value = 1\n", context.GeneratedAmbiguousConstructors)).Message);
        Assert.Equal(multiple.Message, Assert.Throws<TomlException>(() => TomlSerializer.Deserialize("Value = 1\n", context.GeneratedMultipleAnnotatedConstructors)).Message);
    }

    [Theory]
    [InlineData("Id = 1\n", "default", 5)]
    [InlineData("Id = 1\nName = \"x\"\nPlain = 7\n", "x", 7)]
    public void InitOnlyAndRequiredMembers_RunTheConstructorOnce(string toml, string expectedName, int expectedPlain)
    {
        GeneratedConstructionCounter.Constructions = 0;

        var value = TomlSerializer.Deserialize(toml, TestTomlSerializerContextSingleConstruction.Default.GeneratedConstructionCounter)!;

        Assert.Equal(1, GeneratedConstructionCounter.Constructions);
        Assert.Equal(1, value.Id);
        Assert.Equal(expectedName, value.Name);
        Assert.Equal(expectedPlain, value.Plain);
    }

    [Fact]
    public void InitOnlyMembers_OfAStructAndOfAGenericType_AreSet()
    {
        var context = TestTomlSerializerContextSingleConstruction.Default;

        var structValue = TomlSerializer.Deserialize("B = 2\n", context.GeneratedInitStruct);
        var genericValue = TomlSerializer.Deserialize("Value = 3\n", context.GeneratedGenericInitInt32)!;

        Assert.Equal(0, structValue.A);
        Assert.Equal(2, structValue.B);
        Assert.Equal(3, genericValue.Value);
        Assert.Equal("default", genericValue.Name);
    }

    [Fact]
    public void IncludedPrivateMembersOfTheBaseType_AreSerializedByBothPaths()
    {
        var typeInfo = TestTomlSerializerContextNonPublicSetters.Default.GeneratedIncludedPrivateBaseMembersDerived;

        var reflection = TomlSerializer.Serialize(new GeneratedIncludedPrivateBaseMembersDerived());
        var generated = TomlSerializer.Serialize(new GeneratedIncludedPrivateBaseMembersDerived(), typeInfo);

        Assert.Equal("_baseField = 2\nBaseProperty = 1\nDerived = 3\n", reflection);
        Assert.Equal(reflection, generated);
        const string Toml = "_baseField = 20\nBaseProperty = 10\nDerived = 30\n";
        foreach (var value in new[] { TomlSerializer.Deserialize<GeneratedIncludedPrivateBaseMembersDerived>(Toml)!, TomlSerializer.Deserialize(Toml, typeInfo)! })
        {
            Assert.Equal(10, value.GetBaseProperty());
            Assert.Equal(20, value.GetBaseField());
            Assert.Equal(30, value.Derived);
        }
    }

    [Fact]
    public void InstanceCreationError_HasALocation()
    {
        const string Toml = "\n[Inner]\nValue = -1\n";

        var reflection = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedThrowingConstructorHolder>(Toml));
        var generated = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(Toml, TestTomlSerializerContextThrowingConstructor.Default.GeneratedThrowingConstructorHolder));
        var abstractType = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<AbstractWithoutPolymorphismHolder>(Toml));

        foreach (var exception in new[] { reflection, generated, abstractType })
        {
            Assert.Contains("Failed to create an instance", exception.Message);
            Assert.Equal(1, Assert.Single(exception.Diagnostics).Span.Start.Line);
        }
    }

    [Fact]
    public void PolymorphicCollection_IsWrittenAsAnArrayOfTables()
    {
        var model = new GeneratedShapeCollectionHolder
        {
            Shapes = [new GeneratedDefaultCircle { Color = "red", Radius = 5.0 }, new GeneratedDefaultSquare { Color = "blue", Side = 2.0 }],
        };
        var options = new TomlSerializerOptions { PropertyNamingPolicy = TomlNamingPolicy.CamelCase };

        var generated = TomlSerializer.Serialize(model, TestTomlSerializerContextShapeCollection.Default.GeneratedShapeCollectionHolder);
        var reflection = TomlSerializer.Serialize(model, options);

        Assert.Equal(
            """
            [[shapes]]
            color = "red"
            radius = 5.0

            [[shapes]]
            type = "square"
            color = "blue"
            side = 2.0

            """.ReplaceLineEndings("\n"),
            generated.ReplaceLineEndings("\n"));
        Assert.Equal(generated, reflection);
        var roundtrip = TomlSerializer.Deserialize(generated, TestTomlSerializerContextShapeCollection.Default.GeneratedShapeCollectionHolder)!;
        Assert.IsType<GeneratedDefaultCircle>(roundtrip.Shapes[0]);
        Assert.Equal(2.0, Assert.IsType<GeneratedDefaultSquare>(roundtrip.Shapes[1]).Side);
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
    public void ClassWithInitExtensionData_IsCreatedWithoutAnObjectInitializer()
    {
        const string Toml = "A = 'a'\nP = [2]\nZ = 3\n";

        var reflection = TomlSerializer.Deserialize<GeneratedInitExtensionDataModel>(Toml)!;
        var generated = TomlSerializer.Deserialize(Toml, TestTomlSerializerContextInitExtensionData.Default.GeneratedInitExtensionDataModel)!;

        foreach (var value in new[] { reflection, generated })
        {
            Assert.Equal("r-init", value.R);
            Assert.Equal([1, 2], value.P);
            Assert.Equal(3L, value.Ext["Z"]);
            Assert.Null(value.AWhenDeserializing);
        }
    }

    [Fact]
    public void RequiredMembers_ArePopulatedAndKeepTheirInitializerWhenUnread()
    {
        const string Toml = "Required = ['x']\nOther = ['x']\nName = 'n'\nPoint = { Y = 5 }\n";

        var reflection = TomlSerializer.Deserialize<GeneratedRequiredPopulateModel>(Toml)!;
        var generated = TomlSerializer.Deserialize(Toml, TestTomlSerializerContextRequiredPopulate.Default.GeneratedRequiredPopulateModel)!;

        foreach (var value in new[] { reflection, generated })
        {
            Assert.Equal(["pre", "x"], value.Required);
            Assert.Equal(["pre", "x"], value.Other);
            Assert.Equal("keep", value.Unread);
            Assert.Equal("n", value.Name);
            Assert.Equal(1, value.Point.X);
            Assert.Equal(5, value.Point.Y);
        }
    }

    [Fact]
    public void NonPublicAccessors_OfAMemberHidingABaseMember_UseTheDeclaringTypeMember()
    {
        var context = TestTomlSerializerContextHidingAccessors.Default;

        Assert.Equal("q", TomlSerializer.Deserialize("X = 'q'", context.GeneratedHidingAccessorDerived)!.X);
        Assert.Equal("q", TomlSerializer.Deserialize("X = 'q'", context.GeneratedPrivateHidingAccessorDerived)!.GetX());
        Assert.Equal("X = \"d2\"\n", TomlSerializer.Serialize(new GeneratedPrivateHidingAccessorDerived(), context.GeneratedPrivateHidingAccessorDerived).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Populate_TypesWithInitMembersBuiltWithoutTheStreamingPath_KeepTheExistingInstance()
    {
        const string Toml = "[C]\nZ = 5\n[C2]\nA = 7\nZ = 6\n[G]\nZ = 8\n";

        var reflection = TomlSerializer.Deserialize<GeneratedInitPopulateHolder>(Toml)!;
        var generated = TomlSerializer.Deserialize(Toml, TestTomlSerializerContextInitPopulate.Default.GeneratedInitPopulateHolder)!;

        foreach (var value in new[] { reflection, generated })
        {
            Assert.Equal((1, "q0", 5), (value.C.A, value.C.Q, value.C.Z));
            Assert.Equal((3, "q3", 6), (value.C2.A, value.C2.Q, value.C2.Z));
            Assert.Equal(("v0", 8), (value.G.V, value.G.Z));
        }
    }

    [Fact]
    public void ExtensionDataKeys_AreWrittenAsRead_WhateverTheDictionaryKeyPolicy()
    {
        const string Toml = "Name = \"n\"\nFooBar = 1\nfooBar = 2\n";
        var options = new TomlSerializerOptions { DictionaryKeyPolicy = TomlNamingPolicy.CamelCase };
        var typeInfo = TestTomlSerializerContextExtensionKeyPolicy.Default.GeneratedExtensionKeyPolicyModel;

        Assert.Equal(Toml, TomlSerializer.Serialize(TomlSerializer.Deserialize<GeneratedExtensionKeyPolicyModel>(Toml, options), options).ReplaceLineEndings("\n"));
        Assert.Equal(Toml, TomlSerializer.Serialize(TomlSerializer.Deserialize(Toml, typeInfo)!, typeInfo).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ObsoleteMembersAndConstructors_AreSerialized()
    {
        const string Toml = "Old = 1\nOldError = 2\nB = 3\n";

        var value = TomlSerializer.Deserialize(Toml, TestTomlSerializerContextObsolete.Default.GeneratedObsoleteModel)!;
        var created = TomlSerializer.Deserialize("B = 4", TestTomlSerializerContextObsolete.Default.GeneratedObsoleteConstructor)!;

        Assert.Equal(Toml, TomlSerializer.Serialize(value, TestTomlSerializerContextObsolete.Default.GeneratedObsoleteModel).ReplaceLineEndings("\n"));
        Assert.Equal(Toml, TomlSerializer.Serialize(TomlSerializer.Deserialize<GeneratedObsoleteModel>(Toml)!).ReplaceLineEndings("\n"));
        Assert.Equal(4, created.B);
    }

    [Theory]
    [InlineData("Port = 'http'\n")]
    [InlineData("[Server]\nPort = 'http'\n")]
    [InlineData("[[Servers]]\nPort = 'http'\n")]
    public void DeserializedCallback_DoesNotRunOnATableWithErrors(string toml)
    {
        var lastWins = new TomlSerializerOptions { DuplicateKeyHandling = TomlDuplicateKeyHandling.LastWins };
        var context = TestTomlSerializerContextValidated.Default;

        Assert.False(TomlSerializer.TryDeserialize<GeneratedValidatedHolder>(toml, out _));
        Assert.False(TomlSerializer.TryDeserialize<GeneratedValidatedHolder>(toml, out _, lastWins));
        Assert.False(TomlSerializer.TryDeserialize(toml, context.GeneratedValidatedHolder, out _));
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedValidatedHolder>(toml));
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(toml, context.GeneratedValidatedHolder));
    }

    [Fact]
    public void MembersWithANonPublicGetter_AreReadThroughTheirAccessor()
    {
        const string Toml = "Items = [2]\nPopulated = [2]\nSingle = 3\n[Child]\nX = 5\n[Settable]\nX = 6\n";

        var generated = TomlSerializer.Deserialize(Toml, TestTomlSerializerContextPrivateGetter.Default.GeneratedPrivateGetterModel)!;
        var reflection = TomlSerializer.Deserialize<GeneratedPrivateGetterModel>(Toml)!;

        foreach (var value in new[] { generated, reflection })
        {
            Assert.Equal(5, value.GetChild().X);
            Assert.Equal([1], value.GetItems());
            Assert.Equal(6, value.GetSettable()!.X);
            Assert.Equal([1, 2], value.GetPopulated()!);
            Assert.Equal([1, 3], value.GetSingle());
        }

        Assert.Equal(TomlSerializer.Serialize(reflection), TomlSerializer.Serialize(generated, TestTomlSerializerContextPrivateGetter.Default.GeneratedPrivateGetterModel));
    }

    [Fact]
    public void RecursiveTypes_WithOptionsOtherThanTheContextOptions_AreResolvedLazily()
    {
        var options = TestTomlSerializerContextRecursive.Default.Options with { MaxDepth = 32 };

        var node = TomlSerializer.Deserialize<GeneratedRecursiveNode>("Name = 'a'\n[Next]\nName = 'b'\n", options)!;
        var mutual = TomlSerializer.Deserialize<GeneratedMutualA>("[B]\nValue = 1\n[B.A.B]\nValue = 2\n", options)!;

        Assert.Equal("b", node.Next!.Name);
        Assert.Equal(2, mutual.B!.A!.B!.Value);
        Assert.Equal("Name = \"a\"\n[Next]\nName = \"b\"\n", TomlSerializer.Serialize(node, options).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void InitAccessors_DoNotCollideWithTheOtherSetterAccessors()
    {
        var derived = TomlSerializer.Deserialize("X = 'x-toml'\nY = 'y-toml'\nR = 'r'\n", TestTomlSerializerContextAccessorNames.Default.GeneratedAccessorDerived)!;
        var sameType = TomlSerializer.Deserialize("P = 'p-toml'\nQ = 'q'\n_f = 'f-toml'\n", TestTomlSerializerContextAccessorNames.Default.GeneratedAccessorSameType)!;

        Assert.Equal("x-toml", derived.X);
        Assert.Equal("y-toml", derived.GetY());
        Assert.Equal("p-toml", sameType.P);
        Assert.Equal("f-toml", sameType.GetF());
    }

    [Theory]
    [InlineData("[Child]\nExt = { X = 'bad' }\nA = 'text'\n", 1)]
    [InlineData("W = { Rec = { X = 'bad' }, N = 'text' }\n", 0)]
    public void RecordedError_OfANestedValueItsReaderHasNotFinished_StopsTheReading(string toml, int line)
    {
        // The parent must not continue inside the table: the keys that follow belong to the child
        var reflection = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedEscapeRoot>(toml));
        var generated = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(toml, TestTomlSerializerContextEscape.Default.GeneratedEscapeRoot));

        foreach (var exception in new[] { reflection, generated })
        {
            var diagnostic = Assert.Single(exception.Diagnostics);
            Assert.Equal(line, diagnostic.Span.Start.Line);
        }
    }

    [Theory]
    [InlineData("L", false)]
    [InlineData("L", true)]
    [InlineData("R", false)]
    public void Deserialize_ManyTablesWithErrors_IsLinear(string key, bool generated)
    {
        // Each table with an error signals its parent; the first diagnostics, which embed long input values, must not be
        // formatted again for each one
        var big = new string('z', 10_000);
        var builder = new StringBuilder();
        for (var i = 0; i < 100; i++)
        {
            builder.Append("[[").Append(key).Append("]]\nX = 1\nE = '").Append(big).Append("'\n");
        }

        for (var i = 0; i < 2000; i++)
        {
            builder.Append("[[").Append(key).Append("]]\nX = 'a'\nE = 0\n");
        }

        var toml = builder.ToString();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var exception = generated
            ? Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(toml, TestTomlSerializerContextManyErrors.Default.GeneratedManyErrorsRoot))
            : Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedManyErrorsRoot>(toml));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(Meziantou.Framework.Toml.Serialization.Internal.TomlSerializationOperationState.MaxRecordedDiagnostics + 1, exception.Diagnostics.Count);
        Assert.True(allocated < 500_000_000, $"Allocated {allocated} bytes");
    }

    // Every element reports each of its missing required keys: the recorded errors must stay bounded
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_ManyMissingRequiredKeys_StopsAfterTheMaximumCount(bool generated)
    {
        var toml = "Q = [" + string.Concat(Enumerable.Repeat("{},", 50_000)) + "]\n";
        var typeInfo = generated ? (TomlTypeInfo)TestTomlSerializerContextManyErrors.Default.GeneratedManyErrorsRoot : TomlSerializerOptions.Default.GetTypeInfo<GeneratedManyErrorsRoot>();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var exception = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(toml, typeInfo));
        var deserializeAllocated = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        Assert.False(TomlSerializer.TryDeserialize(toml, typeInfo, out _));
        var tryDeserializeAllocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(Meziantou.Framework.Toml.Serialization.Internal.TomlSerializationOperationState.MaxRecordedDiagnostics + 1, exception.Diagnostics.Count);
        Assert.True(deserializeAllocated < 100_000_000, $"Deserialize allocated {deserializeAllocated} bytes");
        Assert.True(tryDeserializeAllocated < deserializeAllocated / 2, $"TryDeserialize allocated {tryDeserializeAllocated} bytes");
    }

    [Fact]
    public void Deserialize_ReportsTheErrorsOfEveryTableAndElement()
    {
        const string Toml = """
            Numbers = [1, 'x', 3, 'y']
            [First]
            Value = 'a'
            [Second]
            Value = 'b'
            [C1]
            Value = 'c'
            [C2]
            Value = 'd'
            [R1]
            X = 1
            [R2]
            X = 2
            """;

        var reflection = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedAggregationRoot>(Toml));
        var generated = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(Toml, TestTomlSerializerContextAggregation.Default.GeneratedAggregationRoot));

        foreach (var exception in new[] { reflection, generated })
        {
            Assert.Equal([0, 0, 2, 4, 6, 8, 9, 11], exception.Diagnostics.Select(diagnostic => diagnostic.Span.Start.Line).Order().ToArray());
        }
    }

    [Fact]
    public void MissingRequiredKeys_AreAllReportedWithTheValueErrors()
    {
        const string Toml = "B = 'z'\n";
        var context = TestTomlSerializerContextRequiredLocation.Default;
        var options = new TomlSerializerOptions { RespectRequiredConstructorParameters = true };

        var exceptions = new[]
        {
            Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedSeveralRequired>(Toml)),
            Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(Toml, context.GeneratedSeveralRequired)),
            Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedSeveralRequiredRecord>(Toml, options)),
            Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(Toml, context.GeneratedSeveralRequiredRecord)),
        };

        foreach (var exception in exceptions)
        {
            Assert.HasCount(3, exception.Diagnostics);
        }
    }

    [Fact]
    public void RootTableErrors_AreReportedAtTheStartOfTheDocument()
    {
        const string Toml = "X = 1\n\n[Sub]\nY = 2\n";

        var reflection = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedRequiredLocationChild>(Toml));
        var generated = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(Toml, TestTomlSerializerContextRequiredLocation.Default.GeneratedRequiredLocationChild));
        var mismatch = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<List<int>>("value = 1"));

        Assert.Equal(1, reflection.Line);
        Assert.Equal(1, generated.Line);
        Assert.Equal(1, mismatch.Line);
        Assert.Single(mismatch.Diagnostics);
    }

    [Theory]
    [InlineData("[A]\nX = 1\n\n[B]\nY = 2\nW = 3\n", "Missing required TOML key 'Z'", 0)]
    [InlineData("[A]\nZ = 1\n\n[B]\nY = 2\n\n[C]\n", "Missing required constructor parameter 'W'", 3)]
    public void MissingRequiredKey_IsReportedAtTheStartOfItsTable(string toml, string message, int expectedLine)
    {
        var options = new TomlSerializerOptions { RespectRequiredConstructorParameters = true };

        var reflection = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedRequiredLocationRoot>(toml, options));
        var generated = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize(toml, TestTomlSerializerContextRequiredLocation.Default.GeneratedRequiredLocationRoot));

        foreach (var exception in new[] { reflection, generated })
        {
            var diagnostic = Assert.Single(exception.Diagnostics);
            Assert.Contains(message, diagnostic.Message, StringComparison.Ordinal);
            Assert.Equal(expectedLine, diagnostic.Span.Start.Line);
        }
    }

    [Fact]
    public void PrivateNewMember_DoesNotHideThePublicBaseMember()
    {
        const string Expected = "Name = \"base\"\nExtra = 1\n";

        Assert.Equal(Expected, TomlSerializer.Serialize(new GeneratedPrivateHidingDerived()).ReplaceLineEndings("\n"));
        Assert.Equal(Expected, TomlSerializer.Serialize(new GeneratedPrivateHidingDerived(), TestTomlSerializerContextPrivateHiding.Default.GeneratedPrivateHidingDerived).ReplaceLineEndings("\n"));

        var reflection = TomlSerializer.Deserialize<GeneratedPrivateHidingDerived>("Name = 'x'")!;
        var generated = TomlSerializer.Deserialize("Name = 'x'", TestTomlSerializerContextPrivateHiding.Default.GeneratedPrivateHidingDerived)!;
        Assert.Equal("x", ((GeneratedPrivateHidingBase)reflection).Name);
        Assert.Equal("private", reflection.GetPrivateName());
        Assert.Equal("x", ((GeneratedPrivateHidingBase)generated).Name);
        Assert.Equal("private", generated.GetPrivateName());
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
