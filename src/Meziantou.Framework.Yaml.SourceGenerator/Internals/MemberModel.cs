using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

internal sealed class MemberModel
{
    public MemberModel(
        ISymbol symbol,
        ITypeSymbol type,
        string serializedNameExpressionForRead,
        string serializedNameExpressionForWrite,
        Func<string, string> accessExpression,
        Func<string, string> assignExpression,
        int? ignoreCondition,
        string? attributeConverterTypeName,
        string? objectCreationHandling,
        string? blockSequenceMappingStyle,
        string? blockSequenceSequenceStyle,
        string? stringStyle,
        bool isRequired,
        bool isIgnoredOnRead,
        bool isInitOnly,
        bool isRequiredKeyword,
        bool requiresIncludeFields,
        bool disallowNullOnSerialize,
        bool disallowNullOnDeserialize,
        bool isReadOnlyProperty,
        bool isReadOnlyField,
        bool skipObjectInitializer,
        int? numberHandling,
        List<(string Member, string Scalar)>? enumCustomNames)
    {
        Symbol = symbol;
        Type = type;
        SerializedNameExpressionForRead = serializedNameExpressionForRead;
        SerializedNameExpressionForWrite = serializedNameExpressionForWrite;
        AccessExpression = accessExpression;
        AssignExpression = assignExpression;
        IgnoreCondition = ignoreCondition;
        AttributeConverterTypeName = attributeConverterTypeName;
        ObjectCreationHandling = objectCreationHandling;
        BlockSequenceMappingStyle = blockSequenceMappingStyle;
        BlockSequenceSequenceStyle = blockSequenceSequenceStyle;
        StringStyle = stringStyle;
        IsRequired = isRequired;
        IsIgnoredOnRead = isIgnoredOnRead;
        IsInitOnly = isInitOnly;
        IsRequiredKeyword = isRequiredKeyword;
        RequiresIncludeFields = requiresIncludeFields;
        DisallowNullOnSerialize = disallowNullOnSerialize;
        DisallowNullOnDeserialize = disallowNullOnDeserialize;
        IsReadOnlyProperty = isReadOnlyProperty;
        IsReadOnlyField = isReadOnlyField;
        SkipObjectInitializer = skipObjectInitializer;
        NumberHandling = numberHandling;
        EnumCustomNames = enumCustomNames;
    }

    public ISymbol Symbol { get; }
    public ITypeSymbol Type { get; }
    public string SerializedNameExpressionForRead { get; }
    public string SerializedNameExpressionForWrite { get; }

    /// <summary>Gets the serialized name of the member, used to sort members when the mapping order is <c>Sorted</c>.</summary>
    public string SerializedName { get; set; } = string.Empty;

    /// <summary>Gets the value of the <c>[YamlPropertyOrder]</c> attribute applied to the member, or 0.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Builds the expression reading the member from the given receiver expression.
    /// </summary>
    public Func<string, string> AccessExpression { get; }
    public Func<string, string> AssignExpression { get; }
    public int? IgnoreCondition { get; }
    public string? AttributeConverterTypeName { get; }
    public string? ObjectCreationHandling { get; }
    public string? BlockSequenceMappingStyle { get; }
    public string? BlockSequenceSequenceStyle { get; }
    public string? StringStyle { get; }
    public bool IsRequired { get; }
    public bool IsIgnoredOnRead { get; }
    public bool IsInitOnly { get; }
    public bool IsRequiredKeyword { get; }
    public bool RequiresIncludeFields { get; }
    public bool DisallowNullOnSerialize { get; }
    public bool DisallowNullOnDeserialize { get; }
    public bool IsReadOnlyProperty { get; }
    public bool IsReadOnlyField { get; }

    /// <summary>
    /// Indicates the member must be assigned after the instance is created instead of through an object initializer,
    /// either because it is written through an <c>[UnsafeAccessor]</c> stub or because the instance itself is created by one.
    /// </summary>
    public bool SkipObjectInitializer { get; }
    public int? NumberHandling { get; }
    public List<(string Member, string Scalar)>? EnumCustomNames { get; }
    public bool NeedsObjectInitializer => (IsInitOnly || IsRequiredKeyword) && !SkipObjectInitializer;
}
