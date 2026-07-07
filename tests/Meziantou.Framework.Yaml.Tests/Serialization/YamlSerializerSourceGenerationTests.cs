#pragma warning disable MA0048 // File name must match type name
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Meziantou.Framework.Yaml.Model;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;

internal sealed class GeneratedPerson
{
    [YamlPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    public int Age { get; set; }
}

internal sealed class GeneratedContainer
{
    public GeneratedPerson? Person { get; set; }
}

internal sealed class GeneratedWithDefaultOptions
{
    public string DisplayName { get; set; } = string.Empty;

    public string? Optional { get; set; }
}

internal sealed class GeneratedOptionsPayload
{
    public int Value;
    public string? Optional;
    public readonly int ReadOnlyValue = 5;

    public int Property { get; set; }
}

internal sealed class GeneratedReadOnlyPropertyPayload
{
    public int Mutable { get; set; } = 1;

    public int ReadOnly { get; } = 2;
}

internal sealed class GeneratedConstructorParametersPayload
{
    public GeneratedConstructorParametersPayload(string name, int age)
    {
        Name = name;
        Age = age;
    }

    public string? Name { get; }

    public int Age { get; }
}

internal sealed class GeneratedNullableAnnotationsPayload
{
    public string Name { get; set; } = string.Empty;

    public string? Optional { get; set; }
}

internal sealed class GeneratedNamingPolicyPayload
{
    public string URLValue { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

[YamlUnmappedMemberHandling(YamlUnmappedMemberHandling.Disallow)]
internal sealed class GeneratedAttributedUnmappedPayload
{
    public string DisplayName { get; set; } = string.Empty;
}

[YamlUnmappedMemberHandling(YamlUnmappedMemberHandling.Disallow)]
internal sealed class GeneratedAttributedExtensionDataPayload
{
    public string DisplayName { get; set; } = string.Empty;

    [YamlExtensionData]
    public Dictionary<string, object?> Extra { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class GeneratedSchemaAwareScalars
{
    public string? NullableText { get; set; }

    public string? QuotedText { get; set; }

    public bool PlainFlag { get; set; }

    public string? QuotedFlag { get; set; }
}

internal enum GeneratedColor
{
    Red = 1,
    Green = 2,
    Blue = 3,
}

internal sealed class GeneratedPrimitives
{
    public bool BoolValue { get; set; }
    public byte ByteValue { get; set; }
    public sbyte SByteValue { get; set; }
    public short Int16Value { get; set; }
    public ushort UInt16Value { get; set; }
    public int Int32Value { get; set; }
    public uint UInt32Value { get; set; }
    public long Int64Value { get; set; }
    public ulong UInt64Value { get; set; }
    public nint NIntValue { get; set; }
    public nuint NUIntValue { get; set; }
    public float SingleValue { get; set; }
    public double DoubleValue { get; set; }
    public decimal DecimalValue { get; set; }
    public char CharValue { get; set; }
    public GeneratedColor Color { get; set; }
    public int? NullableInt { get; set; }
    public GeneratedColor? NullableColor { get; set; }
}

internal sealed class GeneratedWellKnownScalars
{
    public DateTime WhenUtc { get; set; }
    public DateTimeOffset WhenOffset { get; set; }
    public Guid Id { get; set; }
    public TimeSpan Duration { get; set; }
}

internal sealed class GeneratedNullableScalars
{
    public DateTimeOffset? PublishDate { get; set; }
    public bool? AllowPostingOnSocialMedia { get; set; }
}

internal sealed class GeneratedModernScalars
{
    public DateOnly Date { get; set; }
    public TimeOnly Time { get; set; }
    public Half Ratio { get; set; }
    public Int128 Big { get; set; }
    public UInt128 UBig { get; set; }
}

internal sealed class GeneratedCollections
{
    public int[] Numbers { get; set; } = Array.Empty<int>();

    public List<string> Names { get; set; } = new();

    public Dictionary<string, int> Map { get; set; } = new(StringComparer.Ordinal);

    public List<GeneratedPerson> People { get; set; } = new();

    public Dictionary<string, GeneratedPerson> PeopleByName { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class GeneratedMoreCollections
{
    public IReadOnlyList<int>? ReadOnlyNumbers { get; set; }

    public ISet<string>? Tags { get; set; }

    public Dictionary<int, string> IntKeyMap { get; set; } = new();

    public IReadOnlyDictionary<GeneratedColor, int>? EnumKeyMap { get; set; }

    public ImmutableArray<int> ImmutableNumbers { get; set; }

    public ImmutableList<string>? ImmutableNames { get; set; }
}

internal sealed class GeneratedReferenceNode
{
    public string Name { get; set; } = string.Empty;

    public GeneratedReferenceNode? Next { get; set; }
}

internal sealed class GeneratedReferenceContainer
{
    public GeneratedReferenceNode? First { get; set; }

    public GeneratedReferenceNode? Second { get; set; }
}

[YamlPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[YamlDerivedType(typeof(GeneratedDog), "dog")]
[YamlDerivedType(typeof(GeneratedCat), "cat")]
internal abstract class GeneratedAnimal
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class GeneratedDog : GeneratedAnimal
{
    public int BarkVolume { get; set; }
}

internal sealed class GeneratedCat : GeneratedAnimal
{
    public bool LikesCream { get; set; }
}

internal sealed class GeneratedZoo
{
    public GeneratedAnimal? Animal { get; set; }
}

[YamlPolymorphic(DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag)]
[YamlDerivedType(typeof(GeneratedTaggedDog), "dog", Tag = "!dog")]
internal abstract class GeneratedTaggedAnimal
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class GeneratedTaggedDog : GeneratedTaggedAnimal
{
    public int BarkVolume { get; set; }
}

internal sealed class GeneratedTaggedZoo
{
    public GeneratedTaggedAnimal? Animal { get; set; }
}

[YamlPolymorphic(TypeDiscriminatorPropertyName = "type")]
[YamlDerivedType(typeof(GeneratedDefaultCat), "cat")]
[YamlDerivedType(typeof(GeneratedDefaultOther))]
internal abstract class GeneratedDefaultAnimal
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class GeneratedDefaultCat : GeneratedDefaultAnimal
{
    public int Lives { get; set; }
}

internal sealed class GeneratedDefaultOther : GeneratedDefaultAnimal
{
}

internal sealed class GeneratedDefaultZoo
{
    public GeneratedDefaultAnimal? Animal { get; set; }
}

[YamlPolymorphic]
[YamlDerivedType(typeof(GeneratedYamlDefaultCat), "cat")]
[YamlDerivedType(typeof(GeneratedYamlDefaultOther))]
internal abstract class GeneratedYamlDefaultAnimal
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class GeneratedYamlDefaultCat : GeneratedYamlDefaultAnimal
{
    public int Lives { get; set; }
}

internal sealed class GeneratedYamlDefaultOther : GeneratedYamlDefaultAnimal
{
}

internal sealed class GeneratedYamlDefaultZoo
{
    public GeneratedYamlDefaultAnimal? Animal { get; set; }
}

[YamlPolymorphic(UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase)]
[YamlDerivedType(typeof(GeneratedFallbackCircle), "circle")]
internal class GeneratedFallbackShape
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class GeneratedFallbackCircle : GeneratedFallbackShape
{
    public double Radius { get; set; }
}

internal sealed class GeneratedFallbackZoo
{
    public GeneratedFallbackShape? Shape { get; set; }
}

[YamlPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[YamlDerivedType(typeof(GeneratedJsonIntDog), 1)]
[YamlDerivedType(typeof(GeneratedJsonIntCat), 2)]
internal abstract class GeneratedJsonIntAnimal
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class GeneratedJsonIntDog : GeneratedJsonIntAnimal
{
    public int BarkVolume { get; set; }
}

internal sealed class GeneratedJsonIntCat : GeneratedJsonIntAnimal
{
    public int Lives { get; set; }
}

internal sealed class GeneratedJsonIntZoo
{
    public GeneratedJsonIntAnimal? Animal { get; set; }
}

[YamlPolymorphic]
[YamlDerivedType(typeof(GeneratedYamlIntDog), 1)]
[YamlDerivedType(typeof(GeneratedYamlIntCat), 2)]
internal abstract class GeneratedYamlIntAnimal
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class GeneratedYamlIntDog : GeneratedYamlIntAnimal
{
    public int BarkVolume { get; set; }
}

internal sealed class GeneratedYamlIntCat : GeneratedYamlIntAnimal
{
    public int Lives { get; set; }
}

internal sealed class GeneratedYamlIntZoo
{
    public GeneratedYamlIntAnimal? Animal { get; set; }
}

[YamlPolymorphic(TypeDiscriminatorPropertyName = "$type", UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase)]
[YamlDerivedType(typeof(GeneratedJsonFallbackCircle), "circle")]
internal class GeneratedJsonFallbackShape
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class GeneratedJsonFallbackCircle : GeneratedJsonFallbackShape
{
    public double Radius { get; set; }
}

internal sealed class GeneratedJsonFallbackZoo
{
    public GeneratedJsonFallbackShape? Shape { get; set; }
}

internal sealed class ConstantIntConverter : YamlConverter<int>
{
    public override int Read(YamlReader reader)
    {
        reader.Skip();
        return 123;
    }

    public override void Write(YamlWriter writer, int value)
        => writer.WriteScalar("123");
}

internal sealed class GeneratedLifecycleCallbacks : IYamlOnDeserializing, IYamlOnDeserialized, IYamlOnSerializing, IYamlOnSerialized
{
    public int Value { get; set; }

    [YamlIgnore]
    public int OnDeserializingCount { get; private set; }

    [YamlIgnore]
    public int OnDeserializedCount { get; private set; }

    [YamlIgnore]
    public int OnSerializingCount { get; private set; }

    [YamlIgnore]
    public int OnSerializedCount { get; private set; }

    public void OnDeserializing() => OnDeserializingCount++;

    public void OnDeserialized() => OnDeserializedCount++;

    public void OnSerializing() => OnSerializingCount++;

    public void OnSerialized() => OnSerializedCount++;
}

internal sealed class GeneratedRequiredPayload
{
    [YamlRequired]
    public int RequiredValue { get; set; }
}

internal sealed class GeneratedYamlRequiredInitOnlyPayload
{
    [YamlRequired]
    public string Name { get; init; } = string.Empty;

    public int Age { get; init; }
}

internal sealed class GeneratedJsonRequiredInitOnlyPayload
{
    [YamlRequired]
    public string Name { get; init; } = string.Empty;

    public int Age { get; init; }
}

internal sealed class GeneratedOptionalInitOnlyPayload
{
    public string Name { get; init; } = "fallback";

    public int Age { get; init; } = 7;
}

internal sealed class GeneratedNullableInitOnlyPayload
{
    public string? NullableName { get; init; }

    public string NonNullableName { get; init; } = "fallback";
}

internal sealed class GeneratedExtensionDataDictionaryPayload
{
    public int Known { get; set; }

    [YamlExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }
}

internal sealed class GeneratedExtensionDataMappingPayload
{
    [YamlExtensionData]
    public YamlMapping? Extra { get; set; }
}

internal sealed class GeneratedInitOnlyExtensionDataDictionaryPayload
{
    public int Known { get; set; }

    [YamlExtensionData]
    public Dictionary<string, object?> Extra { get; init; } = new(StringComparer.Ordinal);
}

internal sealed class GeneratedInitOnlyExtensionDataMappingPayload
{
    public int Known { get; set; }

    [YamlExtensionData]
    public YamlMapping Extra { get; init; } = new();
}

internal sealed class GeneratedMemberConverterPayload
{
    [YamlConverter(typeof(ConstantIntConverter))]
    public int Value { get; set; }
}

[YamlConverter(typeof(GeneratedTypeConverter))]
internal sealed class GeneratedTypeWithConverter
{
    public int Value { get; set; }
}

internal sealed class GeneratedTypeConverter : YamlConverter<GeneratedTypeWithConverter>
{
    public override GeneratedTypeWithConverter? Read(YamlReader reader)
    {
        if (reader.TokenType is YamlTokenType.Scalar && YamlScalar.IsNull(reader.ScalarValue.AsSpan()))
        {
            reader.Read();
            return null;
        }

        if (reader.TokenType != YamlTokenType.Scalar)
        {
            throw YamlThrowHelper.ThrowExpectedScalar(reader);
        }

        if (!YamlScalar.TryParseInt64(reader.ScalarValue.AsSpan(), out var parsed))
        {
            throw YamlThrowHelper.ThrowInvalidIntegerScalar(reader);
        }

        reader.Read();
        return new GeneratedTypeWithConverter { Value = (int)parsed };
    }

    public override void Write(YamlWriter writer, GeneratedTypeWithConverter value)
        => writer.WriteScalar(value.Value);
}

internal sealed class GeneratedYamlCtorModel
{
#pragma warning disable IDE0060 // Remove unused parameter
    [YamlConstructor]
    public GeneratedYamlCtorModel(string name, int age, bool ignored = false)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        Name = name;
        Age = age;
    }

    public string Name { get; }

    public int Age { get; }
}

internal sealed class GeneratedJsonCtorModel
{
    [YamlConstructor]
    public GeneratedJsonCtorModel(string name, int age)
    {
        Name = name;
        Age = age;
    }

    public string Name { get; }

    public int Age { get; }
}

internal sealed class GeneratedInternalYamlCtorModel
{
    [YamlConstructor]
    internal GeneratedInternalYamlCtorModel(string name, int age)
    {
        Name = name;
        Age = age;
    }

    public string Name { get; }

    public int Age { get; }
}

internal sealed class GeneratedInternalJsonCtorModel
{
    [YamlConstructor]
    internal GeneratedInternalJsonCtorModel(string name, int age)
    {
        Name = name;
        Age = age;
    }

    public string Name { get; }

    public int Age { get; }
}

internal sealed record GeneratedFunctionDto(string Name, string? Description, string Abbreviation);

internal sealed class GeneratedPopulateChild
{
    public int Existing { get; set; }

    public int Added { get; set; }
}

internal sealed class GeneratedPopulateContainer
{
    public GeneratedPopulateChild Child { get; } = new() { Existing = 1 };

    public List<int> Numbers { get; } = [1, 2];
}

internal sealed class GeneratedYamlNodePayload
{
    public string Name { get; set; } = string.Empty;

    public YamlNode? Content { get; set; }
}

[YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
internal sealed class GeneratedPopulateViaTypeAttributeContainer
{
    public GeneratedPopulateChild Child { get; } = new() { Existing = 1 };

    [YamlObjectCreationHandling(YamlObjectCreationHandling.Replace)]
    public List<int> Numbers { get; } = [1, 2];
}

[StructLayout(LayoutKind.Auto)]
internal struct GeneratedPopulateStructChild
{
    public int Existing { get; set; }

    public int Added { get; set; }
}

internal sealed class GeneratedPopulateStructContainer
{
    [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
    public GeneratedPopulateStructChild Child { get; set; } = new() { Existing = 1 };
}

internal sealed class GeneratedReadOnlyPopulateStructContainer
{
    [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
    public GeneratedPopulateStructChild Child { get; } = new() { Existing = 1 };
}

[YamlSerializable(typeof(GeneratedPerson))]
[YamlSerializable(typeof(GeneratedContainer))]
[YamlSerializable(typeof(GeneratedPrimitives))]
[YamlSerializable(typeof(GeneratedWellKnownScalars))]
[YamlSerializable(typeof(GeneratedNullableScalars))]
[YamlSerializable(typeof(GeneratedModernScalars))]
[YamlSerializable(typeof(GeneratedColor))]
[YamlSerializable(typeof(bool))]
[YamlSerializable(typeof(int))]
[YamlSerializable(typeof(int?))]
[YamlSerializable(typeof(GeneratedCollections))]
[YamlSerializable(typeof(GeneratedMoreCollections))]
[YamlSerializable(typeof(List<int>))]
[YamlSerializable(typeof(Dictionary<string, int>))]
[YamlSerializable(typeof(Dictionary<int, int>))]
[YamlSerializable(typeof(Dictionary<int, string>))]
[YamlSerializable(typeof(IReadOnlyList<int>))]
[YamlSerializable(typeof(ISet<string>))]
[YamlSerializable(typeof(HashSet<int>))]
[YamlSerializable(typeof(IReadOnlyDictionary<GeneratedColor, int>))]
[YamlSerializable(typeof(ImmutableArray<int>))]
[YamlSerializable(typeof(ImmutableList<string>))]
[YamlSerializable(typeof(int[]))]
[YamlSerializable(typeof(GeneratedReferenceNode))]
[YamlSerializable(typeof(GeneratedReferenceContainer))]
[YamlSerializable(typeof(GeneratedAnimal))]
[YamlSerializable(typeof(GeneratedZoo))]
[YamlSerializable(typeof(GeneratedTaggedAnimal))]
[YamlSerializable(typeof(GeneratedTaggedZoo))]
[YamlSerializable(typeof(GeneratedDefaultAnimal))]
[YamlSerializable(typeof(GeneratedDefaultZoo))]
[YamlSerializable(typeof(GeneratedYamlDefaultAnimal))]
[YamlSerializable(typeof(GeneratedYamlDefaultZoo))]
[YamlSerializable(typeof(GeneratedFallbackShape))]
[YamlSerializable(typeof(GeneratedFallbackZoo))]
[YamlSerializable(typeof(GeneratedJsonIntAnimal))]
[YamlSerializable(typeof(GeneratedJsonIntZoo))]
[YamlSerializable(typeof(GeneratedYamlIntAnimal))]
[YamlSerializable(typeof(GeneratedYamlIntZoo))]
[YamlSerializable(typeof(GeneratedJsonFallbackShape))]
[YamlSerializable(typeof(GeneratedJsonFallbackZoo))]
[YamlSerializable(typeof(GeneratedLifecycleCallbacks))]
[YamlSerializable(typeof(GeneratedRequiredPayload))]
[YamlSerializable(typeof(GeneratedOptionsPayload))]
[YamlSerializable(typeof(GeneratedReadOnlyPropertyPayload))]
[YamlSerializable(typeof(GeneratedConstructorParametersPayload))]
[YamlSerializable(typeof(GeneratedNullableAnnotationsPayload))]
[YamlSerializable(typeof(GeneratedYamlRequiredInitOnlyPayload))]
[YamlSerializable(typeof(GeneratedJsonRequiredInitOnlyPayload))]
[YamlSerializable(typeof(GeneratedOptionalInitOnlyPayload))]
[YamlSerializable(typeof(GeneratedNullableInitOnlyPayload))]
[YamlSerializable(typeof(GeneratedExtensionDataDictionaryPayload))]
[YamlSerializable(typeof(GeneratedExtensionDataMappingPayload))]
[YamlSerializable(typeof(GeneratedInitOnlyExtensionDataDictionaryPayload))]
[YamlSerializable(typeof(GeneratedInitOnlyExtensionDataMappingPayload))]
[YamlSerializable(typeof(GeneratedAttributedUnmappedPayload))]
[YamlSerializable(typeof(GeneratedAttributedExtensionDataPayload))]
[YamlSerializable(typeof(GeneratedMemberConverterPayload))]
[YamlSerializable(typeof(GeneratedTypeWithConverter))]
[YamlSerializable(typeof(GeneratedYamlCtorModel))]
[YamlSerializable(typeof(GeneratedJsonCtorModel))]
[YamlSerializable(typeof(GeneratedInternalYamlCtorModel))]
[YamlSerializable(typeof(GeneratedInternalJsonCtorModel))]
[YamlSerializable(typeof(GeneratedPopulateChild))]
[YamlSerializable(typeof(GeneratedPopulateContainer))]
[YamlSerializable(typeof(GeneratedPopulateViaTypeAttributeContainer))]
[YamlSerializable(typeof(GeneratedPopulateStructChild))]
[YamlSerializable(typeof(GeneratedPopulateStructContainer))]
[YamlSerializable(typeof(GeneratedReadOnlyPopulateStructContainer))]
[YamlSerializable(typeof(YamlValue))]
[YamlSerializable(typeof(YamlNode))]
[YamlSerializable(typeof(GeneratedYamlNodePayload))]
internal sealed partial class TestYamlSerializerContext : YamlSerializerContext
{
    public TestYamlSerializerContext()
    {
    }

    public TestYamlSerializerContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}

[YamlSourceGenerationOptions(
    WriteIndented = false,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = YamlIgnoreCondition.WhenWritingNull,
    BlockSequenceMappingStyle = YamlSequenceItemStyle.Expanded,
    BlockSequenceSequenceStyle = YamlSequenceItemStyle.Compact,
    PropertyNamingPolicy = YamlKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = YamlKnownNamingPolicy.CamelCase)]
[YamlSerializable(typeof(GeneratedWithDefaultOptions))]
[YamlSerializable(typeof(GeneratedFunctionDto))]
internal sealed partial class TestYamlSerializerContextWithOptions : YamlSerializerContext
{
    public TestYamlSerializerContextWithOptions()
    {
    }

    public TestYamlSerializerContextWithOptions(YamlSerializerOptions options)
        : base(options)
    {
    }
}

[YamlSourceGenerationOptions(PropertyNamingPolicy = YamlKnownNamingPolicy.SnakeCaseLower)]
[YamlSerializable(typeof(GeneratedNamingPolicyPayload))]
internal sealed partial class TestYamlSerializerContextWithSnakeCaseNamingPolicy : YamlSerializerContext
{
}

[YamlSourceGenerationOptions(
    Converters = new[] { typeof(ConstantIntConverter) })]
[YamlSerializable(typeof(int))]
internal sealed partial class TestYamlSerializerContextWithConverters : YamlSerializerContext
{
}

[YamlSourceGenerationOptions(
    Schema = YamlSchemaKind.Extended,
    UseSchema = true)]
[YamlSerializable(typeof(GeneratedSchemaAwareScalars))]
internal sealed partial class TestYamlSerializerContextWithSchema : YamlSerializerContext
{
}

[YamlSourceGenerationOptions(
    UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow)]
[YamlSerializable(typeof(GeneratedWithDefaultOptions))]
internal sealed partial class TestYamlSerializerContextWithStrictUnmappedMembers : YamlSerializerContext
{
}

[YamlSourceGenerationOptions(
    PreferredObjectCreationHandling = YamlObjectCreationHandling.Populate)]
[YamlSerializable(typeof(GeneratedPopulateChild))]
[YamlSerializable(typeof(GeneratedPopulateContainer))]
internal sealed partial class TestYamlSerializerContextWithPopulate : YamlSerializerContext
{
}

[YamlSerializable(typeof(GeneratedPerson), TypeInfoPropertyName = "GeneratedPersonTypeInfo")]
[YamlSerializable(typeof(Dictionary<string, int>), TypeInfoPropertyName = "IntMapTypeInfo")]
internal sealed partial class TestYamlSerializerContextWithCustomPropertyNames : YamlSerializerContext
{
}
public class YamlSerializerSourceGenerationTests
{
    [Fact]
    public void GeneratedContext_YamlNodeRoot_UsesModelConverter()
    {
        var node = new YamlValue("abc");

        var generated = YamlSerializer.Serialize(node, TestYamlSerializerContext.Default.YamlValue);
        var reflection = YamlSerializer.Serialize(node);

        Assert.Equal(reflection, generated);
    }

    [Fact]
    public void GeneratedContext_YamlNodeRoot_DeserializesDynamicContent()
    {
        var yaml = "items:\n- one\n- two\n";

        var node = YamlSerializer.Deserialize(yaml, TestYamlSerializerContext.Default.YamlNode);

        Assert.IsType(typeof(YamlMapping), node);
        var mapping = (YamlMapping)node!;
        Assert.IsType(typeof(YamlSequence), mapping["items"]);
    }

    [Fact]
    public void GeneratedContext_YamlNodeMember_RoundTripsDynamicContent()
    {
        var yaml = """
            Name: dynamic
            Content:
              script: |
                echo hello
              values:
              - 1
              - true
            """;

        var payload = YamlSerializer.Deserialize(yaml, TestYamlSerializerContext.Default.GeneratedYamlNodePayload);

        Assert.NotNull(payload);
        Assert.Equal("dynamic", payload.Name);
        Assert.IsType(typeof(YamlMapping), payload.Content);

        var serialized = YamlSerializer.Serialize(payload, TestYamlSerializerContext.Default.GeneratedYamlNodePayload);

        Assert.Contains("Content:", serialized);
        Assert.Contains("script:", serialized);
        Assert.Contains("values:", serialized);
    }

    [Fact]
    public void GeneratedContext_MergeKey_AppliesToDictionaryStringKey()
    {
        var context = TestYamlSerializerContext.Default;
        var yaml = """
            <<:
              a: 1
              b: 2
            b: 3
            """;

        var dictionary = YamlSerializer.Deserialize(yaml, context.DictionaryStringInt32);

        Assert.NotNull(dictionary);
        Assert.Equal(1, dictionary["a"]);
        Assert.Equal(3, dictionary["b"]);
    }

    [Fact]
    public void GeneratedContext_MergeKey_AppliesToObject()
    {
        var context = TestYamlSerializerContext.Default;
        var yaml = """
            <<:
              first_name: Ada
              Age: 37
            Age: 38
            """;

        var person = YamlSerializer.Deserialize(yaml, context.GeneratedPerson);

        Assert.NotNull(person);
        Assert.Equal("Ada", person.FirstName);
        Assert.Equal(38, person.Age);
    }

    [Fact]
    public void GeneratedContext_MergeKey_AppliesToParameterizedConstructor()
    {
        var context = TestYamlSerializerContext.Default;
        var yaml = """
            <<:
              Name: Ada
              Age: 37
            Age: 38
            """;

        var model = YamlSerializer.Deserialize(yaml, context.GeneratedYamlCtorModel);

        Assert.NotNull(model);
        Assert.Equal("Ada", model.Name);
        Assert.Equal(38, model.Age);
    }

    [Fact]
    public void GeneratedContext_MergeKey_ExplicitKeyBeforeMergeWins()
    {
        var context = TestYamlSerializerContext.Default;
        var yaml = """
            Age: 38
            <<:
              Name: Ada
              Age: 37
            """;

        var model = YamlSerializer.Deserialize(yaml, context.GeneratedYamlCtorModel);

        Assert.NotNull(model);
        Assert.Equal("Ada", model.Name);
        Assert.Equal(38, model.Age);
    }

    [Fact]
    public void GeneratedContext_ParameterizedConstructorStringParameterReadsYamlNullAsNull()
    {
        var context = TestYamlSerializerContextWithOptions.Default;
        var yaml = """
            name: My Function
            description: null
            abbreviation: MF
            """;

        var model = YamlSerializer.Deserialize(yaml, context.GeneratedFunctionDto);

        Assert.NotNull(model);
        Assert.Equal("My Function", model.Name);
        Assert.Null(model.Description);
        Assert.Equal("MF", model.Abbreviation);
    }

    [Fact]
    public void GeneratedContextProvidesTypedMetadata()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedPerson;
        var yaml = YamlSerializer.Serialize(
            new GeneratedPerson
            {
                FirstName = "Ada",
                Age = 37,
            },
            typeInfo);
        var person = YamlSerializer.Deserialize(yaml, typeInfo);

        Assert.Contains("first_name", yaml);
        Assert.NotNull(person);
        Assert.Equal("Ada", person.FirstName);
        Assert.Equal(37, person.Age);
    }

    [Fact]
    public void GeneratedContext_ReplaceIsDefault_ForReadOnlyMembers()
    {
        var context = TestYamlSerializerContext.Default;
        var yaml = """
            Child:
              Added: 2
            Numbers:
              - 3
              - 4
            """;

        var value = YamlSerializer.Deserialize(yaml, context.GeneratedPopulateContainer);

        Assert.NotNull(value);
        Assert.Equal(1, value.Child.Existing);
        Assert.Equal(0, value.Child.Added);
        Assert.Equal(new[] { 1, 2 }, value.Numbers);
    }

    [Fact]
    public void GeneratedContext_PopulatesReadOnlyMembers_WhenPreferredObjectCreationHandlingIsPopulate()
    {
        var context = TestYamlSerializerContextWithPopulate.Default;
        var yaml = """
            Child:
              Added: 2
            Numbers:
              - 3
              - 4
            """;

        var value = YamlSerializer.Deserialize(yaml, context.GeneratedPopulateContainer);

        Assert.NotNull(value);
        Assert.Equal(1, value.Child.Existing);
        Assert.Equal(2, value.Child.Added);
        Assert.Equal(new[] { 1, 2, 3, 4 }, value.Numbers);
    }

    [Fact]
    public void GeneratedContext_HonorsJsonObjectCreationHandlingAttribute_OnTypeAndProperty()
    {
        var context = TestYamlSerializerContext.Default;
        var yaml = """
            Child:
              Added: 2
            Numbers:
              - 3
              - 4
            """;

        var value = YamlSerializer.Deserialize(yaml, context.GeneratedPopulateViaTypeAttributeContainer);

        Assert.NotNull(value);
        Assert.Equal(1, value.Child.Existing);
        Assert.Equal(2, value.Child.Added);
        Assert.Equal(new[] { 1, 2 }, value.Numbers);
    }

    [Fact]
    public void GeneratedContext_PopulateOnStructPropertyWithSetter_AssignsBackModifiedCopy()
    {
        var context = TestYamlSerializerContext.Default;
        var yaml = """
            Child:
              Added: 2
            """;

        var value = YamlSerializer.Deserialize(yaml, context.GeneratedPopulateStructContainer);

        Assert.NotNull(value);
        Assert.Equal(1, value.Child.Existing);
        Assert.Equal(2, value.Child.Added);
    }

    [Fact]
    public void GeneratedContext_PopulateOnReadOnlyStructProperty_Throws()
    {
        var context = TestYamlSerializerContext.Default;
        var yaml = """
            Child:
              Added: 2
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => YamlSerializer.Deserialize(yaml, context.GeneratedReadOnlyPopulateStructContainer));

        Assert.Contains("value type", exception.Message);
        Assert.Contains("doesn't have a setter", exception.Message);
    }

    [Fact]
    public void GeneratedContext_WellKnownScalarTypes_RoundTrip()
    {
        var payload = new GeneratedWellKnownScalars
        {
            WhenUtc = new DateTime(2026, 03, 01, 12, 34, 56, DateTimeKind.Utc),
            WhenOffset = new DateTimeOffset(2026, 03, 01, 12, 34, 56, TimeSpan.FromHours(2)),
            Id = Guid.Parse("6d0c86e2-1e37-4c33-9c2f-5304a33f2c5e"),
            Duration = TimeSpan.FromMilliseconds(1234),
        };

        var context = TestYamlSerializerContext.Default;
        var typeInfo = context.GeneratedWellKnownScalars;

        var yaml = YamlSerializer.Serialize(payload, typeInfo);
        var roundTrip = YamlSerializer.Deserialize(yaml, typeInfo);

        Assert.NotNull(roundTrip);
        Assert.Equal(payload.WhenUtc, roundTrip.WhenUtc);
        Assert.Equal(payload.WhenOffset, roundTrip.WhenOffset);
        Assert.Equal(payload.Id, roundTrip.Id);
        Assert.Equal(payload.Duration, roundTrip.Duration);
    }

    [Fact]
    public void GeneratedContext_NullableDateTimeOffsetAndBoolean_AreEmittedPlain()
    {
        var context = TestYamlSerializerContext.Default;
        var typeInfo = context.GeneratedNullableScalars;
        var payload = new GeneratedNullableScalars
        {
            PublishDate = new DateTimeOffset(2019, 06, 17, 0, 0, 0, TimeSpan.Zero),
            AllowPostingOnSocialMedia = false,
        };

        var yaml = YamlSerializer.Serialize(payload, typeInfo);

        Assert.Equal("""
            PublishDate: 2019-06-17T00:00:00.0000000Z
            AllowPostingOnSocialMedia: false

            """, yaml, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void GeneratedContext_DateTimeAndDateTimeOffset_UseZSuffixForUtc()
    {
        var payload = new GeneratedWellKnownScalars
        {
            WhenUtc = new DateTime(2027, 04, 19, 12, 00, 00, DateTimeKind.Utc),
            WhenOffset = new DateTimeOffset(2027, 04, 19, 12, 00, 00, TimeSpan.Zero),
            Id = Guid.Empty,
            Duration = TimeSpan.Zero,
        };

        var context = TestYamlSerializerContext.Default;
        var yaml = YamlSerializer.Serialize(payload, context.GeneratedWellKnownScalars);

        Assert.Contains("WhenUtc: 2027-04-19T12:00:00.0000000Z", yaml);
        Assert.Contains("WhenOffset: 2027-04-19T12:00:00.0000000Z", yaml);
    }

    [Fact]
    public void GeneratedContext_ModernScalarTypes_RoundTrip()
    {
        var payload = new GeneratedModernScalars
        {
            Date = new DateOnly(2026, 03, 01),
            Time = new TimeOnly(12, 34, 56),
            Ratio = (Half)1.5f,
            Big = Int128.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture),
            UBig = UInt128.Parse("123456789012345678901234567891", CultureInfo.InvariantCulture),
        };

        var context = TestYamlSerializerContext.Default;
        var typeInfo = context.GeneratedModernScalars;

        var yaml = YamlSerializer.Serialize(payload, typeInfo);
        var roundTrip = YamlSerializer.Deserialize(yaml, typeInfo);

        Assert.NotNull(roundTrip);
        Assert.Equal(payload.Date, roundTrip.Date);
        Assert.Equal(payload.Time, roundTrip.Time);
        Assert.Equal(payload.Ratio, roundTrip.Ratio);
        Assert.Equal(payload.Big, roundTrip.Big);
        Assert.Equal(payload.UBig, roundTrip.UBig);
    }

    [Fact]
    public void GeneratedContextExposesDefaultTypeInfoPropertyNames()
    {
        var context = TestYamlSerializerContext.Default;

        Assert.NotNull(context.GeneratedPerson);
        Assert.NotNull(context.GeneratedModernScalars);
        Assert.NotNull(context.Boolean);
        Assert.NotNull(context.Int32);
        Assert.NotNull(context.NullableInt32);
        Assert.NotNull(context.ListInt32);
        Assert.NotNull(context.DictionaryStringInt32);
        Assert.NotNull(context.Int32Array);

        var yaml = YamlSerializer.Serialize(
            new GeneratedPerson
            {
                FirstName = "Ada",
                Age = 37,
            },
            context.GeneratedPerson);
        var person = YamlSerializer.Deserialize(yaml, context.GeneratedPerson);

        Assert.Contains("first_name: Ada", yaml);
        Assert.NotNull(person);
        Assert.Equal("Ada", person.FirstName);
        Assert.Equal(37, person.Age);
    }

    [Fact]
    public void GeneratedContextSupportsCustomTypeInfoPropertyNames()
    {
        var context = TestYamlSerializerContextWithCustomPropertyNames.Default;

        Assert.NotNull(context.GeneratedPersonTypeInfo);
        Assert.NotNull(context.IntMapTypeInfo);

        var yaml = YamlSerializer.Serialize(
            new GeneratedPerson
            {
                FirstName = "Ada",
                Age = 37,
            },
            context.GeneratedPersonTypeInfo);
        var person = YamlSerializer.Deserialize(yaml, context.GeneratedPersonTypeInfo);
        var resolved = context.GetTypeInfo(typeof(GeneratedPerson), context.GeneratedPersonTypeInfo.Options);

        Assert.Contains("first_name: Ada", yaml);
        Assert.NotNull(person);
        Assert.Equal("Ada", person.FirstName);
        Assert.Equal(37, person.Age);
        Assert.Same(context.GeneratedPersonTypeInfo, resolved);
    }

    [Fact]
    public void GeneratedContextWorksAsResolver()
    {
        var context = new TestYamlSerializerContext();
        var yaml = YamlSerializer.Serialize(
            new GeneratedContainer
            {
                Person = new GeneratedPerson
                {
                    FirstName = "Ada",
                    Age = 37,
                },
            },
            typeof(GeneratedContainer),
            context);
        var container = (GeneratedContainer?)YamlSerializer.Deserialize(yaml, typeof(GeneratedContainer), context);

        Assert.NotNull(container);
        Assert.NotNull(container.Person);
        Assert.Equal("Ada", container.Person.FirstName);
        Assert.Equal(37, container.Person.Age);
    }

    [Fact]
    public void GeneratedContextCanBeUsedDirectlyWithSerializerOverloads()
    {
        var context = TestYamlSerializerContext.Default;
        var value = new GeneratedPerson
        {
            FirstName = "Ada",
            Age = 37,
        };

        var yaml = YamlSerializer.Serialize(value, typeof(GeneratedPerson), context);
        var person = (GeneratedPerson?)YamlSerializer.Deserialize(yaml, typeof(GeneratedPerson), context);

        Assert.Contains("first_name: Ada", yaml);
        Assert.NotNull(person);
        Assert.Equal("Ada", person.FirstName);
        Assert.Equal(37, person.Age);
    }

    [Fact]
    public void GenericSerializerUsesTypeInfoResolverFromContextOptions()
    {
        var context = TestYamlSerializerContext.Default;
        var options = context.GeneratedPerson.Options;

        var value = new GeneratedPerson
        {
            FirstName = "Ada",
            Age = 37,
        };

        var yaml = YamlSerializer.Serialize(value, options);
        var person = YamlSerializer.Deserialize<GeneratedPerson>(yaml, options);

        Assert.Contains("first_name: Ada", yaml);
        Assert.NotNull(person);
        Assert.Equal("Ada", person.FirstName);
        Assert.Equal(37, person.Age);
    }

    [Fact]
    public void GeneratedContextDefaultAppliesYamlSourceGenerationOptions()
    {
        var context = TestYamlSerializerContextWithOptions.Default;
        var options = context.GeneratedWithDefaultOptions.Options;

        Assert.False(options.WriteIndented);
        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Equal(YamlIgnoreCondition.WhenWritingNull, options.DefaultIgnoreCondition);
        Assert.Equal(YamlSequenceItemStyle.Expanded, options.BlockSequenceMappingStyle);
        Assert.Equal(YamlSequenceItemStyle.Compact, options.BlockSequenceSequenceStyle);
        Assert.Same(YamlNamingPolicy.CamelCase, options.PropertyNamingPolicy);
        Assert.Same(YamlNamingPolicy.CamelCase, options.DictionaryKeyPolicy);
        Assert.Equal(YamlUnmappedMemberHandling.Skip, options.UnmappedMemberHandling);
        Assert.False(options.IncludeFields);
        Assert.False(options.IgnoreReadOnlyFields);
        Assert.False(options.IgnoreReadOnlyProperties);
        Assert.False(options.RejectUnmatchedProperties);
        Assert.True(options.RespectRequiredConstructorParameters);
        Assert.True(options.RespectNullableAnnotations);
        Assert.Same(context, options.TypeInfoResolver);

        var yaml = YamlSerializer.Serialize(
            new GeneratedWithDefaultOptions
            {
                DisplayName = "Ada",
                Optional = null,
            },
            typeof(GeneratedWithDefaultOptions),
            options);

        Assert.Contains("displayName: Ada", yaml);
        Assert.DoesNotContain("optional:", yaml);
    }

    [Fact]
    public void GeneratedContext_IncludeFields_IncludesPublicFieldsForReadAndWrite()
    {
        var context = new TestYamlSerializerContext(new YamlSerializerOptions { IncludeFields = true });

        var yaml = YamlSerializer.Serialize(
            new GeneratedOptionsPayload { Property = 1, Value = 2, Optional = "field" },
            context.GeneratedOptionsPayload);

        Assert.Contains("Property: 1", yaml);
        Assert.Contains("Value: 2", yaml);
        Assert.Contains("Optional: field", yaml);
        Assert.Contains("ReadOnlyValue: 5", yaml);

        var result = YamlSerializer.Deserialize("Property: 3\nValue: 4\nOptional: test\n", context.GeneratedOptionsPayload);

        Assert.NotNull(result);
        Assert.Equal(3, result.Property);
        Assert.Equal(4, result.Value);
        Assert.Equal("test", result.Optional);
    }

    [Fact]
    public void GeneratedContext_IncludeFields_DoesNotIncludePublicFieldsByDefault()
    {
        var context = TestYamlSerializerContext.Default;

        var yaml = YamlSerializer.Serialize(
            new GeneratedOptionsPayload { Property = 1, Value = 2, Optional = "field" },
            context.GeneratedOptionsPayload);

        Assert.Contains("Property: 1", yaml);
        Assert.DoesNotContain("Value:", yaml);
        Assert.DoesNotContain("Optional:", yaml);

        var result = YamlSerializer.Deserialize("Property: 3\nValue: 4\nOptional: test\n", context.GeneratedOptionsPayload);

        Assert.NotNull(result);
        Assert.Equal(3, result.Property);
        Assert.Equal(0, result.Value);
        Assert.Null(result.Optional);
    }

    [Fact]
    public void GeneratedContext_IgnoreReadOnlyMembers_SkipsReadOnlyMembersDuringSerialization()
    {
        var context = new TestYamlSerializerContext(
            new YamlSerializerOptions
            {
                IncludeFields = true,
                IgnoreReadOnlyFields = true,
                IgnoreReadOnlyProperties = true,
            });

        var yaml = YamlSerializer.Serialize(
            new GeneratedOptionsPayload { Property = 1, Value = 2 },
            context.GeneratedOptionsPayload);

        Assert.Contains("Property: 1", yaml);
        Assert.Contains("Value: 2", yaml);
        Assert.DoesNotContain("ReadOnlyValue:", yaml);

        var propertyYaml = YamlSerializer.Serialize(
            new GeneratedReadOnlyPropertyPayload { Mutable = 3 },
            context.GeneratedReadOnlyPropertyPayload);

        Assert.Contains("Mutable: 3", propertyYaml);
        Assert.DoesNotContain("ReadOnly:", propertyYaml);
    }

    [Fact]
    public void GeneratedContextAppliesYamlKnownNamingPolicyWithoutJsonNamingPolicy()
    {
        var context = TestYamlSerializerContextWithSnakeCaseNamingPolicy.Default;
        var typeInfo = context.GeneratedNamingPolicyPayload;

        var yaml = YamlSerializer.Serialize(
            new GeneratedNamingPolicyPayload
            {
                URLValue = "https://example.com",
                DisplayName = "Ada",
            },
            typeInfo);

        Assert.Contains("url_value: \"https://example.com\"", yaml);
        Assert.Contains("display_name: Ada", yaml);

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped);
        Assert.Equal("https://example.com", roundtripped.URLValue);
        Assert.Equal("Ada", roundtripped.DisplayName);
    }

    [Fact]
    public void GeneratedContextAllowsOptionsWithSameResolverButDifferentInstance()
    {
        var context = TestYamlSerializerContext.Default;
        var options = new YamlSerializerOptions
        {
            TypeInfoResolver = context,
            SourceName = "override.yaml",
        };

        // Options that reference a context as TypeInfoResolver but are a separate instance
        // now work correctly — the context resolves type info and the caller's options
        // are used for runtime behavior (e.g. SourceName).
        var yaml = YamlSerializer.Serialize(new GeneratedPerson { FirstName = "Alice", Age = 30 }, options);
        Assert.Contains("Alice", yaml);
    }

    [Fact]
    public void GeneratedContext_CanUseSchemaAwareScalarResolution()
    {
        var context = TestYamlSerializerContextWithSchema.Default;
        var options = context.GeneratedSchemaAwareScalars.Options;

        Assert.True(options.UseSchema);
        Assert.Equal(YamlSchemaKind.Extended, options.Schema);

        var yaml = """
            NullableText: null
            QuotedText: "null"
            PlainFlag: yes
            QuotedFlag: "yes"
            """;
        var value = YamlSerializer.Deserialize(yaml, context.GeneratedSchemaAwareScalars);

        Assert.NotNull(value);
        Assert.Null(value.NullableText);
        Assert.Equal("null", value.QuotedText);
        Assert.Equal(true, value.PlainFlag);
        Assert.Equal("yes", value.QuotedFlag);
    }

    [Fact]
    public void GeneratedContext_SkipsUnmappedMembersByDefault()
    {
        var context = TestYamlSerializerContext.Default;
        var value = YamlSerializer.Deserialize(
            "first_name: Ada\nAge: 37\nUnknown: test\n",
            context.GeneratedPerson);

        Assert.NotNull(value);
        Assert.Equal("Ada", value.FirstName);
        Assert.Equal(37, value.Age);
    }

    [Fact]
    public void GeneratedContext_CanDisallowUnmappedMembersViaOptions()
    {
        var context = TestYamlSerializerContextWithStrictUnmappedMembers.Default;
        var options = context.GeneratedWithDefaultOptions.Options;

        Assert.Equal(YamlUnmappedMemberHandling.Disallow, options.UnmappedMemberHandling);

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize(
            "DisplayName: Ada\nUnknown: test\n",
            context.GeneratedWithDefaultOptions));

        Assert.Contains("Unknown", exception.Message);
        Assert.Contains(typeof(GeneratedWithDefaultOptions).ToString(), exception.Message);
    }

    [Fact]
    public void GeneratedContext_HonorsJsonUnmappedMemberHandlingAttribute()
    {
        var context = TestYamlSerializerContext.Default;

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize(
            "DisplayName: Ada\nUnknown: test\n",
            context.GeneratedAttributedUnmappedPayload));

        Assert.Contains("Unknown", exception.Message);
        Assert.Contains(typeof(GeneratedAttributedUnmappedPayload).ToString(), exception.Message);
    }

    [Fact]
    public void GeneratedContext_UnmappedMemberHandlingDoesNotConflictWithExtensionData()
    {
        var context = TestYamlSerializerContext.Default;
        var value = YamlSerializer.Deserialize(
            "DisplayName: Ada\nUnknown: test\n",
            context.GeneratedAttributedExtensionDataPayload);

        Assert.NotNull(value);
        Assert.Equal("Ada", value.DisplayName);
        Assert.Equal("test", value.Extra["Unknown"]);
    }

    [Fact]
    public void GeneratedContext_RejectUnmatchedProperties_DisallowsUnknownMembers()
    {
        var context = new TestYamlSerializerContextWithOptions(new YamlSerializerOptions { RejectUnmatchedProperties = true });

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize(
            "displayName: Ada\nUnknown: test\n",
            context.GeneratedWithDefaultOptions));

        Assert.Contains("Unknown", exception.Message);
    }

    [Fact]
    public void GeneratedContext_RejectUnmatchedProperties_DoesNotConflictWithExtensionData()
    {
        var context = new TestYamlSerializerContext(new YamlSerializerOptions { RejectUnmatchedProperties = true });

        var value = YamlSerializer.Deserialize(
            "DisplayName: Ada\nUnknown: test\n",
            context.GeneratedAttributedExtensionDataPayload);

        Assert.NotNull(value);
        Assert.Equal("Ada", value.DisplayName);
        Assert.Equal("test", value.Extra["Unknown"]);
    }

    [Fact]
    public void GeneratedContext_RespectRequiredConstructorParameters_RequiresNonOptionalParametersByDefault()
    {
        var context = TestYamlSerializerContext.Default;

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize(
            "Age: 42\n",
            context.GeneratedConstructorParametersPayload));

        Assert.Contains("name", exception.Message);
    }

    [Fact]
    public void GeneratedContext_RespectRequiredConstructorParameters_CanBeDisabled()
    {
        var context = new TestYamlSerializerContext(new YamlSerializerOptions { RespectRequiredConstructorParameters = false });

        var result = YamlSerializer.Deserialize(
            "Age: 42\n",
            context.GeneratedConstructorParametersPayload);

        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.Equal(42, result.Age);

        var missingValueType = YamlSerializer.Deserialize(
            "Name: Ada\n",
            context.GeneratedConstructorParametersPayload);

        Assert.NotNull(missingValueType);
        Assert.Equal("Ada", missingValueType.Name);
        Assert.Equal(0, missingValueType.Age);
    }

    [Fact]
    public void GeneratedContext_RespectNullableAnnotations_RejectsNullDuringDeserializationByDefault()
    {
        var context = TestYamlSerializerContext.Default;

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize(
            "Name: null\nOptional: null\n",
            context.GeneratedNullableAnnotationsPayload));

        Assert.Contains("Name", exception.Message);
    }

    [Fact]
    public void GeneratedContext_RespectNullableAnnotations_CanBeDisabledForDeserialization()
    {
        var context = new TestYamlSerializerContext(new YamlSerializerOptions { RespectNullableAnnotations = false });

        var result = YamlSerializer.Deserialize(
            "Name: null\nOptional: null\n",
            context.GeneratedNullableAnnotationsPayload);

        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.Null(result.Optional);
    }

    [Fact]
    public void GeneratedContext_RespectNullableAnnotations_RejectsNullDuringSerializationByDefault()
    {
        var context = TestYamlSerializerContext.Default;

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Serialize(
            new GeneratedNullableAnnotationsPayload { Name = null! },
            context.GeneratedNullableAnnotationsPayload));

        Assert.Contains("Name", exception.Message);
    }

    [Fact]
    public void GeneratedContext_RespectNullableAnnotations_CanBeDisabledForSerialization()
    {
        var context = new TestYamlSerializerContext(new YamlSerializerOptions { RespectNullableAnnotations = false });

        var yaml = YamlSerializer.Serialize(
            new GeneratedNullableAnnotationsPayload { Name = null! },
            context.GeneratedNullableAnnotationsPayload);

        Assert.Contains("Name: null", yaml);
    }

    [Fact]
    public void GeneratedContextOptionsCanRegisterConvertersAtBuildTime()
    {
        var context = TestYamlSerializerContextWithConverters.Default;
        var options = context.Int32.Options;
        Assert.Single(options.Converters);
        Assert.IsType(typeof(ConstantIntConverter), options.Converters[0]);

        var yaml = YamlSerializer.Serialize(42, typeof(int), context);
        Assert.Equal("123\n", yaml);

        var roundTrip = YamlSerializer.Deserialize(yaml, typeof(int), context);
        Assert.Equal(123, roundTrip);
    }

    [Fact]
    public void GeneratedContextRoundTripsPrimitiveMembers()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedPrimitives;

        var value = new GeneratedPrimitives
        {
            BoolValue = true,
            ByteValue = 255,
            SByteValue = -12,
            Int16Value = short.MinValue,
            UInt16Value = ushort.MaxValue,
            Int32Value = int.MinValue,
            UInt32Value = uint.MaxValue,
            Int64Value = long.MinValue,
            UInt64Value = ulong.MaxValue,
            NIntValue = (nint)1234,
            NUIntValue = (nuint)5678,
            SingleValue = float.PositiveInfinity,
            DoubleValue = double.NaN,
            DecimalValue = 79228162514264337593543950335m,
            CharValue = 'Z',
            Color = GeneratedColor.Green,
            NullableInt = 42,
            NullableColor = GeneratedColor.Blue,
        };

        var yaml = YamlSerializer.Serialize(value, typeInfo);
        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);

        Assert.NotNull(roundtripped);
        Assert.Equal(value.BoolValue, roundtripped.BoolValue);
        Assert.Equal(value.ByteValue, roundtripped.ByteValue);
        Assert.Equal(value.SByteValue, roundtripped.SByteValue);
        Assert.Equal(value.Int16Value, roundtripped.Int16Value);
        Assert.Equal(value.UInt16Value, roundtripped.UInt16Value);
        Assert.Equal(value.Int32Value, roundtripped.Int32Value);
        Assert.Equal(value.UInt32Value, roundtripped.UInt32Value);
        Assert.Equal(value.Int64Value, roundtripped.Int64Value);
        Assert.Equal(value.UInt64Value, roundtripped.UInt64Value);
        Assert.Equal(value.NIntValue, roundtripped.NIntValue);
        Assert.Equal(value.NUIntValue, roundtripped.NUIntValue);
        Assert.True(float.IsPositiveInfinity(roundtripped.SingleValue));
        Assert.True(double.IsNaN(roundtripped.DoubleValue));
        Assert.Equal(value.DecimalValue, roundtripped.DecimalValue);
        Assert.Equal(value.CharValue, roundtripped.CharValue);
        Assert.Equal(value.Color, roundtripped.Color);
        Assert.Equal(value.NullableInt, roundtripped.NullableInt);
        Assert.Equal(value.NullableColor, roundtripped.NullableColor);

        Assert.Contains("ByteValue: 255", yaml);
        Assert.Contains("SingleValue: .inf", yaml);
        Assert.Contains("Color: Green", yaml);
    }

    [Fact]
    public void GeneratedContextSupportsRootEnumAndNullable()
    {
        var context = new TestYamlSerializerContext();

        var enumTypeInfo = context.GeneratedColor;
        var yamlEnum = YamlSerializer.Serialize(GeneratedColor.Red, enumTypeInfo);
        Assert.Equal("Red\n", yamlEnum);
        Assert.Equal(GeneratedColor.Red, YamlSerializer.Deserialize(yamlEnum, enumTypeInfo));

        var nullableTypeInfo = context.NullableInt32;
        var yamlValue = YamlSerializer.Serialize((int?)123, nullableTypeInfo);
        Assert.Equal("123\n", yamlValue);
        Assert.Equal(123, YamlSerializer.Deserialize(yamlValue, nullableTypeInfo));

        var yamlNull = YamlSerializer.Serialize((int?)null, nullableTypeInfo);
        Assert.Equal("null\n", yamlNull);
        Assert.Null(YamlSerializer.Deserialize(yamlNull, nullableTypeInfo));
    }

    [Fact]
    public void GeneratedContextRoundTripsCollectionMembers()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedCollections;

        var value = new GeneratedCollections
        {
            Numbers = new[] { 1, 2, 3 },
            Names = new List<string> { "Ada", "Bob" },
            Map = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["one"] = 1,
                ["two"] = 2,
            },
            People = new List<GeneratedPerson>
            {
                new GeneratedPerson { FirstName = "Ada", Age = 37 },
                new GeneratedPerson { FirstName = "Bob", Age = 28 },
            },
            PeopleByName = new Dictionary<string, GeneratedPerson>(StringComparer.Ordinal)
            {
                ["ada"] = new GeneratedPerson { FirstName = "Ada", Age = 37 },
            },
        };

        var yaml = YamlSerializer.Serialize(value, typeInfo);
        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);

        Assert.NotNull(roundtripped);
        Assert.Equal(value.Numbers, roundtripped.Numbers);
        Assert.Equal(value.Names, roundtripped.Names);
        Assert.HasCount(2, roundtripped.Map);
        Assert.Equal(1, roundtripped.Map["one"]);
        Assert.Equal(2, roundtripped.Map["two"]);
        Assert.HasCount(2, roundtripped.People);
        Assert.Equal("Ada", roundtripped.People[0].FirstName);
        Assert.Equal(37, roundtripped.People[0].Age);
        Assert.Equal("Bob", roundtripped.People[1].FirstName);
        Assert.Equal(28, roundtripped.People[1].Age);
        Assert.Equal("Ada", roundtripped.PeopleByName["ada"].FirstName);

        Assert.Contains("Numbers:", yaml);
        Assert.Contains("- 1", yaml);
        Assert.Contains("Map:", yaml);
        Assert.Contains("People:", yaml);
    }

    [Fact]
    public void GeneratedContextSupportsRootCollections()
    {
        var context = new TestYamlSerializerContext();

        var listTypeInfo = context.ListInt32;
        var yamlList = YamlSerializer.Serialize(new List<int> { 1, 2, 3 }, listTypeInfo);
        var list = YamlSerializer.Deserialize(yamlList, listTypeInfo);
        Assert.NotNull(list);
        Assert.Equal(new[] { 1, 2, 3 }, list);

        var arrayTypeInfo = context.Int32Array;
        var yamlArray = YamlSerializer.Serialize(new[] { 4, 5 }, arrayTypeInfo);
        Assert.Equal(new[] { 4, 5 }, YamlSerializer.Deserialize(yamlArray, arrayTypeInfo));

        var dictTypeInfo = context.DictionaryStringInt32;
        var yamlDict = YamlSerializer.Serialize(new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = 2 }, dictTypeInfo);
        var dict = YamlSerializer.Deserialize(yamlDict, dictTypeInfo);
        Assert.NotNull(dict);
        Assert.Equal(1, dict["a"]);
        Assert.Equal(2, dict["b"]);
    }

    [Fact]
    public void GeneratedContextSupportsAdditionalRootCollections()
    {
        var context = new TestYamlSerializerContext();

        var readOnlyListTypeInfo = context.IReadOnlyListInt32;
        var yamlReadOnly = YamlSerializer.Serialize((IReadOnlyList<int>)new List<int> { 1, 2 }, readOnlyListTypeInfo);
        var roundReadOnly = YamlSerializer.Deserialize(yamlReadOnly, readOnlyListTypeInfo);
        Assert.NotNull(roundReadOnly);
        Assert.HasCount(2, roundReadOnly);
        Assert.Equal(1, roundReadOnly[0]);

        var setTypeInfo = context.HashSetInt32;
        var yamlSet = YamlSerializer.Serialize(new HashSet<int> { 1, 2, 1 }, setTypeInfo);
        var roundSet = YamlSerializer.Deserialize(yamlSet, setTypeInfo);
        Assert.NotNull(roundSet);
        Assert.HasCount(2, roundSet);

        var dictTypeInfo = context.DictionaryInt32Int32;
        var yamlDict = YamlSerializer.Serialize(new Dictionary<int, int> { [1] = 2 }, dictTypeInfo);
        Assert.Contains("1:", yamlDict);
        var roundDict = YamlSerializer.Deserialize(yamlDict, dictTypeInfo);
        Assert.NotNull(roundDict);
        Assert.Equal(2, roundDict[1]);

        var enumDictTypeInfo = context.IReadOnlyDictionaryGeneratedColorInt32;
        var yamlEnumDict = YamlSerializer.Serialize((IReadOnlyDictionary<GeneratedColor, int>)new Dictionary<GeneratedColor, int> { [GeneratedColor.Red] = 1 }, enumDictTypeInfo);
        Assert.Contains("Red:", yamlEnumDict);
        var roundEnumDict = YamlSerializer.Deserialize(yamlEnumDict, enumDictTypeInfo);
        Assert.NotNull(roundEnumDict);
        Assert.Equal(1, roundEnumDict[GeneratedColor.Red]);

        var immutableArrayTypeInfo = context.ImmutableArrayInt32;
        var yamlImmutable = YamlSerializer.Serialize(ImmutableArray.Create(3, 4), immutableArrayTypeInfo);
        var roundImmutable = YamlSerializer.Deserialize(yamlImmutable, immutableArrayTypeInfo);
        Assert.HasCount(2, roundImmutable);
        Assert.Equal(3, roundImmutable[0]);
        Assert.Equal(4, roundImmutable[1]);
    }

    [Fact]
    public void GeneratedContextRoundTripsAdditionalCollectionMembers()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedMoreCollections;

        var value = new GeneratedMoreCollections
        {
            ReadOnlyNumbers = new List<int> { 1, 2 },
            Tags = new HashSet<string>(StringComparer.Ordinal) { "a", "b", "a" },
            IntKeyMap = new Dictionary<int, string> { [1] = "one", [2] = "two" },
            EnumKeyMap = new Dictionary<GeneratedColor, int> { [GeneratedColor.Green] = 2 },
            ImmutableNumbers = ImmutableArray.Create(10, 20),
            ImmutableNames = ImmutableList.Create("Ada", "Bob"),
        };

        var yaml = YamlSerializer.Serialize(value, typeInfo);
        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);

        Assert.NotNull(roundtripped);
        Assert.NotNull(roundtripped.ReadOnlyNumbers);
        Assert.HasCount(2, roundtripped.ReadOnlyNumbers);
        Assert.Equal(2, roundtripped.ReadOnlyNumbers[1]);

        Assert.NotNull(roundtripped.Tags);
        Assert.HasCount(2, roundtripped.Tags);
        Assert.Contains("a", roundtripped.Tags);

        Assert.Equal("one", roundtripped.IntKeyMap[1]);
        Assert.NotNull(roundtripped.EnumKeyMap);
        Assert.Equal(2, roundtripped.EnumKeyMap[GeneratedColor.Green]);

        Assert.HasCount(2, roundtripped.ImmutableNumbers);
        Assert.Equal(10, roundtripped.ImmutableNumbers[0]);

        Assert.NotNull(roundtripped.ImmutableNames);
        Assert.HasCount(2, roundtripped.ImmutableNames);
        Assert.Equal("Ada", roundtripped.ImmutableNames[0]);

        Assert.Contains("IntKeyMap:", yaml);
        Assert.Contains("1:", yaml);
        Assert.Contains("EnumKeyMap:", yaml);
        Assert.Contains("Green:", yaml);
    }

    [Fact]
    public void GeneratedContextHonorsCustomConverters()
    {
        var context = new TestYamlSerializerContext(
            new YamlSerializerOptions
            {
                Converters =
                [
                    new ConstantIntConverter(),
                ],
            });

        var primitivesTypeInfo = context.GeneratedPrimitives;
        var yaml = YamlSerializer.Serialize(new GeneratedPrimitives { Int32Value = 5 }, primitivesTypeInfo);
        Assert.Contains("Int32Value: 123", yaml);

        var roundtripped = YamlSerializer.Deserialize(yaml, primitivesTypeInfo);
        Assert.NotNull(roundtripped);
        Assert.Equal(123, roundtripped.Int32Value);

        var listTypeInfo = context.ListInt32;
        var yamlList = YamlSerializer.Serialize(new List<int> { 1, 2 }, listTypeInfo);
        Assert.Contains("- 123", yamlList);
        var list = YamlSerializer.Deserialize(yamlList, listTypeInfo);
        Assert.NotNull(list);
        Assert.Equal(new[] { 123, 123 }, list);
    }

    [Fact]
    public void GeneratedContextPreservesSharedReferences()
    {
        var context = new TestYamlSerializerContext(
            new YamlSerializerOptions
            {
                ReferenceHandling = YamlReferenceHandling.Preserve,
            });

        var shared = new GeneratedReferenceNode { Name = "shared" };
        var container = new GeneratedReferenceContainer { First = shared, Second = shared };

        var typeInfo = context.GeneratedReferenceContainer;
        var yaml = YamlSerializer.Serialize(container, typeInfo);
        Assert.Contains("&id002", yaml);
        Assert.Contains("*id002", yaml);

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped);
        Assert.NotNull(roundtripped.First);
        Assert.NotNull(roundtripped.Second);
        Assert.True(ReferenceEquals(roundtripped.First, roundtripped.Second));
        Assert.Equal("shared", roundtripped.First.Name);
    }

    [Fact]
    public void GeneratedContextPreservesCycles()
    {
        var context = new TestYamlSerializerContext(
            new YamlSerializerOptions
            {
                ReferenceHandling = YamlReferenceHandling.Preserve,
            });

        var node = new GeneratedReferenceNode { Name = "self" };
        node.Next = node;

        var typeInfo = context.GeneratedReferenceNode;
        var yaml = YamlSerializer.Serialize(node, typeInfo);
        Assert.Contains("&id001", yaml);
        Assert.Contains("*id001", yaml);

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped);
        Assert.NotNull(roundtripped.Next);
        Assert.True(ReferenceEquals(roundtripped, roundtripped.Next));
    }

    [Fact]
    public void GeneratedContextSupportsPolymorphism_PropertyDiscriminator()
    {
        var context = new TestYamlSerializerContext();

        var typeInfo = context.GeneratedZoo;
        var yaml = YamlSerializer.Serialize(
            new GeneratedZoo
            {
                Animal = new GeneratedDog { Name = "Rex", BarkVolume = 7 },
            },
            typeInfo);

        Assert.Contains("$type: dog", yaml);
        Assert.Contains("BarkVolume: 7", yaml);

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped);
        Assert.IsType(typeof(GeneratedDog), roundtripped.Animal);
        var dog = (GeneratedDog)roundtripped.Animal!;
        Assert.Equal("Rex", dog.Name);
        Assert.Equal(7, dog.BarkVolume);
    }

    [Fact]
    public void GeneratedContextSupportsPolymorphism_TagDiscriminator()
    {
        var context = new TestYamlSerializerContext();

        var typeInfo = context.GeneratedTaggedZoo;
        var yaml = YamlSerializer.Serialize(
            new GeneratedTaggedZoo
            {
                Animal = new GeneratedTaggedDog { Name = "Rex", BarkVolume = 7 },
            },
            typeInfo);

        Assert.Contains("!dog", yaml);
        Assert.DoesNotContain("$type", yaml);

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped);
        Assert.IsType(typeof(GeneratedTaggedDog), roundtripped.Animal);
        var dog = (GeneratedTaggedDog)roundtripped.Animal!;
        Assert.Equal("Rex", dog.Name);
        Assert.Equal(7, dog.BarkVolume);
    }

    [Fact]
    public void GeneratedContextSupportsPolymorphism_DefaultDerivedType_MissingDiscriminator()
    {
        var context = new TestYamlSerializerContext();

        var typeInfo = context.GeneratedDefaultZoo;
        var yaml = "Animal:\n  Name: Cupcake\n";

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped);
        Assert.IsType(typeof(GeneratedDefaultOther), roundtripped.Animal);
        Assert.Equal("Cupcake", roundtripped.Animal!.Name);
    }

    [Fact]
    public void GeneratedContextSupportsPolymorphism_DefaultDerivedType_MatchedDiscriminator()
    {
        var context = new TestYamlSerializerContext();

        var typeInfo = context.GeneratedDefaultZoo;
        var yaml = "Animal:\n  type: cat\n  Name: Biscuit\n  Lives: 7\n";

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped);
        Assert.IsType(typeof(GeneratedDefaultCat), roundtripped.Animal);
        var cat = (GeneratedDefaultCat)roundtripped.Animal!;
        Assert.Equal("Biscuit", cat.Name);
        Assert.Equal(7, cat.Lives);
    }

    [Fact]
    public void GeneratedContextSupportsPolymorphism_DefaultDerivedType_UnknownDiscriminator()
    {
        var context = new TestYamlSerializerContext();

        var typeInfo = context.GeneratedDefaultZoo;
        var yaml = "Animal:\n  type: lizard\n  Name: Gex\n";

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped);
        Assert.IsType(typeof(GeneratedDefaultOther), roundtripped.Animal);
        Assert.Equal("Gex", roundtripped.Animal!.Name);
    }

    [Fact]
    public void GeneratedContextSupportsPolymorphism_DefaultDerivedType_Serialization()
    {
        var context = new TestYamlSerializerContext();

        var typeInfo = context.GeneratedDefaultZoo;
        var yaml = YamlSerializer.Serialize(
            new GeneratedDefaultZoo
            {
                Animal = new GeneratedDefaultOther { Name = "Cupcake" },
            },
            typeInfo);

        Assert.DoesNotContain("type:", yaml);
        Assert.Contains("Name: Cupcake", yaml);

        // Cat should still get a discriminator
        var yamlCat = YamlSerializer.Serialize(
            new GeneratedDefaultZoo
            {
                Animal = new GeneratedDefaultCat { Name = "Biscuit", Lives = 7 },
            },
            typeInfo);

        Assert.Contains("type: cat", yamlCat);
        Assert.Contains("Name: Biscuit", yamlCat);
    }

    [Fact]
    public void GeneratedContextSupportsPolymorphism_YamlDefaultDerivedType_MissingDiscriminator()
    {
        var context = new TestYamlSerializerContext();

        var typeInfo = context.GeneratedYamlDefaultZoo;
        var yaml = "Animal:\n  Name: Cupcake\n";

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped?.Animal);
        Assert.IsType(typeof(GeneratedYamlDefaultOther), roundtripped.Animal);
        Assert.Equal("Cupcake", roundtripped.Animal!.Name);
    }

    [Fact]
    public void GeneratedContextSupportsPolymorphism_YamlDefaultDerivedType_MatchedDiscriminator()
    {
        var context = new TestYamlSerializerContext();

        var typeInfo = context.GeneratedYamlDefaultZoo;
        var yaml = "Animal:\n  $type: cat\n  Name: Biscuit\n  Lives: 7\n";

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped?.Animal);
        Assert.IsType(typeof(GeneratedYamlDefaultCat), roundtripped.Animal);
        var cat = (GeneratedYamlDefaultCat)roundtripped.Animal!;
        Assert.Equal("Biscuit", cat.Name);
        Assert.Equal(7, cat.Lives);
    }

    [Fact]
    public void GeneratedContextSupportsPolymorphism_YamlDefaultDerivedType_Serialization()
    {
        var context = new TestYamlSerializerContext();

        var typeInfo = context.GeneratedYamlDefaultZoo;
        var yaml = YamlSerializer.Serialize(
            new GeneratedYamlDefaultZoo
            {
                Animal = new GeneratedYamlDefaultOther { Name = "Cupcake" },
            },
            typeInfo);

        Assert.DoesNotContain("$type:", yaml);
        Assert.Contains("Name: Cupcake", yaml);

        // Cat should still get a discriminator
        var yamlCat = YamlSerializer.Serialize(
            new GeneratedYamlDefaultZoo
            {
                Animal = new GeneratedYamlDefaultCat { Name = "Biscuit", Lives = 7 },
            },
            typeInfo);

        Assert.Contains("$type: cat", yamlCat);
        Assert.Contains("Name: Biscuit", yamlCat);
    }

    [Fact]
    public void GeneratedContextSupportsPolymorphism_YamlUnknownHandlingFallBackToBase()
    {
        var context = new TestYamlSerializerContext();

        var typeInfo = context.GeneratedFallbackZoo;
        var yaml = "Shape:\n  $type: unknown\n  Name: Base\n";

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped);
        Assert.IsType(typeof(GeneratedFallbackShape), roundtripped.Shape);
        Assert.Equal("Base", roundtripped.Shape!.Name);
    }

    [Fact]
    public void GeneratedContextSupportsPolymorphism_JsonIntDiscriminator()
    {
        var context = new TestYamlSerializerContext();

        var zoo = new GeneratedJsonIntZoo { Animal = new GeneratedJsonIntDog { Name = "Rex", BarkVolume = 3 } };
        var yaml = YamlSerializer.Serialize(zoo, context.GeneratedJsonIntZoo);
        Assert.Contains("$type: 1", yaml);

        var roundtripped = YamlSerializer.Deserialize(yaml, context.GeneratedJsonIntZoo);
        Assert.NotNull(roundtripped?.Animal);
        Assert.IsType(typeof(GeneratedJsonIntDog), roundtripped.Animal);
        Assert.Equal("Rex", roundtripped.Animal!.Name);
        Assert.Equal(3, ((GeneratedJsonIntDog)roundtripped.Animal).BarkVolume);
    }

    [Fact]
    public void GeneratedContextSupportsPolymorphism_YamlIntDiscriminator()
    {
        var context = new TestYamlSerializerContext();

        var zoo = new GeneratedYamlIntZoo { Animal = new GeneratedYamlIntCat { Name = "Mittens", Lives = 9 } };
        var yaml = YamlSerializer.Serialize(zoo, context.GeneratedYamlIntZoo);
        Assert.Contains("$type: 2", yaml);

        var roundtripped = YamlSerializer.Deserialize(yaml, context.GeneratedYamlIntZoo);
        Assert.NotNull(roundtripped?.Animal);
        Assert.IsType(typeof(GeneratedYamlIntCat), roundtripped.Animal);
        Assert.Equal("Mittens", roundtripped.Animal!.Name);
        Assert.Equal(9, ((GeneratedYamlIntCat)roundtripped.Animal).Lives);
    }

    [Fact]
    public void GeneratedContextSupportsPolymorphism_JsonUnknownHandlingFallBackToBase()
    {
        var context = new TestYamlSerializerContext();

        var typeInfo = context.GeneratedJsonFallbackZoo;
        var yaml = "Shape:\n  $type: unknown\n  Name: Base\n";

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped);
        Assert.IsType(typeof(GeneratedJsonFallbackShape), roundtripped.Shape);
        Assert.Equal("Base", roundtripped.Shape!.Name);
    }

    [Fact]
    public void GeneratedContextErrorsIncludeSourceNameAndLocation()
    {
        var context = new TestYamlSerializerContext(
            new YamlSerializerOptions
            {
                SourceName = "generated.yaml",
            });

        var typeInfo = context.GeneratedPerson;
        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("123", typeInfo));
        Assert.Equal("generated.yaml", exception.SourceName);
        Assert.Contains("Lin:", exception.Message);
        Assert.Contains("Col:", exception.Message);
    }

    [Fact]
    public void GeneratedContextInvokesLifecycleCallbacks()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedLifecycleCallbacks;

        var value = new GeneratedLifecycleCallbacks { Value = 7 };
        var yaml = YamlSerializer.Serialize(value, typeInfo);

        Assert.Equal(1, value.OnSerializingCount);
        Assert.Equal(1, value.OnSerializedCount);
        Assert.Contains("Value: 7", yaml);

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped);
        Assert.Equal(1, roundtripped.OnDeserializingCount);
        Assert.Equal(1, roundtripped.OnDeserializedCount);
        Assert.Equal(7, roundtripped.Value);
    }

    [Fact]
    public void GeneratedContextHonorsYamlRequiredAttribute()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedRequiredPayload;

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("Other: 1", typeInfo));
        Assert.Contains("RequiredValue", exception.Message);
    }

    [Fact]
    public void GeneratedContextSupportsInitOnlyMembers()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedOptionalInitOnlyPayload;

        var value = YamlSerializer.Deserialize("Name: Ada\nAge: 37\n", typeInfo);

        Assert.NotNull(value);
        Assert.Equal("Ada", value.Name);
        Assert.Equal(37, value.Age);
    }

    [Fact]
    public void GeneratedContextPreservesDefaultsForMissingInitOnlyMembers()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedOptionalInitOnlyPayload;

        var value = YamlSerializer.Deserialize("Name: Ada\n", typeInfo);

        Assert.NotNull(value);
        Assert.Equal("Ada", value.Name);
        Assert.Equal(7, value.Age);
    }

    [Fact]
    public void GeneratedContextPreservesNullableDefaultsForMissingInitOnlyMembers()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedNullableInitOnlyPayload;

        var value = YamlSerializer.Deserialize("NonNullableName: Ada\n", typeInfo);

        Assert.NotNull(value);
        Assert.Null(value.NullableName);
        Assert.Equal("Ada", value.NonNullableName);
    }

    [Fact]
    public void GeneratedContextCanDeserializeNullableInitOnlyMembersViaResolverOverload()
    {
        var context = new TestYamlSerializerContext();

        var value = (GeneratedNullableInitOnlyPayload?)YamlSerializer.Deserialize(
            "{}\n",
            typeof(GeneratedNullableInitOnlyPayload),
            context);

        Assert.NotNull(value);
        Assert.Null(value.NullableName);
        Assert.Equal("fallback", value.NonNullableName);
    }

    [Fact]
    public void GeneratedContextHonorsYamlRequiredAttributeOnInitOnlyMembers()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedYamlRequiredInitOnlyPayload;

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("Age: 37\n", typeInfo));
        Assert.Contains("Name", exception.Message);

        var value = YamlSerializer.Deserialize("Name: Ada\nAge: 37\n", typeInfo);
        Assert.NotNull(value);
        Assert.Equal("Ada", value.Name);
        Assert.Equal(37, value.Age);
    }

    [Fact]
    public void GeneratedContextHonorsJsonRequiredAttributeOnInitOnlyMembers()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedJsonRequiredInitOnlyPayload;

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("Age: 37\n", typeInfo));
        Assert.Contains("Name", exception.Message);

        var value = YamlSerializer.Deserialize("Name: Ada\nAge: 37\n", typeInfo);
        Assert.NotNull(value);
        Assert.Equal("Ada", value.Name);
        Assert.Equal(37, value.Age);
    }

    [Fact]
    public void GeneratedContextSupportsYamlExtensionDataDictionary()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedExtensionDataDictionaryPayload;

        var yaml = """
Known: 2
extra_int: 1
extra_list:
  - a
  - b
""";

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped);
        Assert.Equal(2, roundtripped.Known);
        Assert.NotNull(roundtripped.Extra);
        Assert.Contains("extra_int", roundtripped.Extra);
        Assert.Contains("extra_list", roundtripped.Extra);

        var serialized = YamlSerializer.Serialize(
            new GeneratedExtensionDataDictionaryPayload
            {
                Known = 3,
                Extra = new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = 5 },
            },
            typeInfo);
        Assert.Contains("Known: 3", serialized);
        Assert.Contains("x: 5", serialized);
    }

    [Fact]
    public void GeneratedContextSupportsYamlExtensionDataMapping()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedExtensionDataMappingPayload;

        var roundtripped = YamlSerializer.Deserialize("a: 1", typeInfo);
        Assert.NotNull(roundtripped);
        Assert.NotNull(roundtripped.Extra);
        Assert.Single(roundtripped.Extra);

        var serialized = YamlSerializer.Serialize(
            new GeneratedExtensionDataMappingPayload
            {
                Extra = new YamlMapping
                {
                    { new YamlValue("x"), new YamlValue("y") },
                },
            },
            typeInfo);
        Assert.Contains("x:", serialized);
        Assert.Contains("y", serialized);
    }

    [Fact]
    public void GeneratedContextSupportsInitOnlyYamlExtensionDataDictionary()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedInitOnlyExtensionDataDictionaryPayload;

        var yaml = """
Known: 2
extra_int: 1
extra_list:
  - a
  - b
""";

        var roundtripped = YamlSerializer.Deserialize(yaml, typeInfo);
        Assert.NotNull(roundtripped);
        Assert.Equal(2, roundtripped.Known);
        Assert.NotNull(roundtripped.Extra);
        Assert.Contains("extra_int", roundtripped.Extra);
        Assert.Contains("extra_list", roundtripped.Extra);

        var withoutExtras = YamlSerializer.Deserialize("Known: 3\n", typeInfo);
        Assert.NotNull(withoutExtras);
        Assert.NotNull(withoutExtras.Extra);
        Assert.Empty(withoutExtras.Extra);

        var serialized = YamlSerializer.Serialize(
            new GeneratedInitOnlyExtensionDataDictionaryPayload
            {
                Known = 4,
                Extra = new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = 5 },
            },
            typeInfo);
        Assert.Contains("Known: 4", serialized);
        Assert.Contains("x: 5", serialized);
    }

    [Fact]
    public void GeneratedContextSupportsInitOnlyYamlExtensionDataMapping()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedInitOnlyExtensionDataMappingPayload;

        var roundtripped = YamlSerializer.Deserialize("Known: 2\na: 1\n", typeInfo);
        Assert.NotNull(roundtripped);
        Assert.Equal(2, roundtripped.Known);
        Assert.NotNull(roundtripped.Extra);
        Assert.Single(roundtripped.Extra);

        var withoutExtras = YamlSerializer.Deserialize("Known: 3\n", typeInfo);
        Assert.NotNull(withoutExtras);
        Assert.NotNull(withoutExtras.Extra);
        Assert.Empty(withoutExtras.Extra);

        var serialized = YamlSerializer.Serialize(
            new GeneratedInitOnlyExtensionDataMappingPayload
            {
                Known = 4,
                Extra = new YamlMapping
                {
                    { new YamlValue("x"), new YamlValue("y") },
                },
            },
            typeInfo);
        Assert.Contains("Known: 4", serialized);
        Assert.Contains("x:", serialized);
        Assert.Contains("y", serialized);
    }

    [Fact]
    public void GeneratedContextHonorsYamlConverterAttributeOnMember()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedMemberConverterPayload;

        var yaml = YamlSerializer.Serialize(new GeneratedMemberConverterPayload { Value = 5 }, typeInfo);
        Assert.Contains("Value: 123", yaml);

        var roundtripped = YamlSerializer.Deserialize("Value: 999", typeInfo);
        Assert.NotNull(roundtripped);
        Assert.Equal(123, roundtripped.Value);
    }

    [Fact]
    public void GeneratedContextHonorsYamlConverterAttributeOnType()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedTypeWithConverter;

        var yaml = YamlSerializer.Serialize(new GeneratedTypeWithConverter { Value = 5 }, typeInfo);
        Assert.DoesNotContain("Value:", yaml);
        Assert.Contains("5", yaml);

        var roundtripped = YamlSerializer.Deserialize("42", typeInfo);
        Assert.NotNull(roundtripped);
        Assert.Equal(42, roundtripped.Value);
    }

    [Fact]
    public void GeneratedContextUsesYamlConstructor()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedYamlCtorModel;

        var value = YamlSerializer.Deserialize("Name: Bob\nAge: 42\n", typeInfo);

        Assert.NotNull(value);
        Assert.Equal("Bob", value.Name);
        Assert.Equal(42, value.Age);
    }

    [Fact]
    public void GeneratedContextUsesJsonConstructor()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedJsonCtorModel;

        var value = YamlSerializer.Deserialize("Name: Bob\nAge: 42\n", typeInfo);

        Assert.NotNull(value);
        Assert.Equal("Bob", value.Name);
        Assert.Equal(42, value.Age);
    }

    [Fact]
    public void GeneratedContextUsesInternalYamlConstructor()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedInternalYamlCtorModel;

        var value = YamlSerializer.Deserialize("Name: Bob\nAge: 42\n", typeInfo);

        Assert.NotNull(value);
        Assert.Equal("Bob", value.Name);
        Assert.Equal(42, value.Age);
    }

    [Fact]
    public void GeneratedContextUsesInternalJsonConstructor()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedInternalJsonCtorModel;

        var value = YamlSerializer.Deserialize("Name: Bob\nAge: 42\n", typeInfo);

        Assert.NotNull(value);
        Assert.Equal("Bob", value.Name);
        Assert.Equal(42, value.Age);
    }

    [Fact]
    public void GeneratedContextThrowsWhenConstructorParameterMissing()
    {
        var context = new TestYamlSerializerContext();
        var typeInfo = context.GeneratedYamlCtorModel;

        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("Name: Bob\n", typeInfo));
        Assert.Contains("age", ex.Message);
    }
}
