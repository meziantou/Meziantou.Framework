using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

internal sealed class MemberModel
{
    public MemberModel(
        ISymbol symbol,
        ITypeSymbol type,
        string serializedNameExpressionForRead,
        string serializedNameExpressionForWrite,
        string accessExpression,
        Func<string, string> assignExpression,
        string ignoreConditionExpression,
        string? attributeConverterTypeName,
        string? objectCreationHandling,
        string? blockSequenceMappingStyle,
        string? blockSequenceSequenceStyle,
        bool isRequired,
        bool isInitOnly,
        bool isRequiredKeyword,
        bool requiresIncludeFields,
        bool disallowNullOnSerialize,
        bool disallowNullOnDeserialize,
        bool isReadOnlyProperty,
        bool isReadOnlyField,
        int? numberHandling,
        List<(string Member, string Scalar)>? enumCustomNames)
    {
        Symbol = symbol;
        Type = type;
        SerializedNameExpressionForRead = serializedNameExpressionForRead;
        SerializedNameExpressionForWrite = serializedNameExpressionForWrite;
        AccessExpression = accessExpression;
        AssignExpression = assignExpression;
        IgnoreConditionExpression = ignoreConditionExpression;
        AttributeConverterTypeName = attributeConverterTypeName;
        ObjectCreationHandling = objectCreationHandling;
        BlockSequenceMappingStyle = blockSequenceMappingStyle;
        BlockSequenceSequenceStyle = blockSequenceSequenceStyle;
        IsRequired = isRequired;
        IsInitOnly = isInitOnly;
        IsRequiredKeyword = isRequiredKeyword;
        RequiresIncludeFields = requiresIncludeFields;
        DisallowNullOnSerialize = disallowNullOnSerialize;
        DisallowNullOnDeserialize = disallowNullOnDeserialize;
        IsReadOnlyProperty = isReadOnlyProperty;
        IsReadOnlyField = isReadOnlyField;
        NumberHandling = numberHandling;
        EnumCustomNames = enumCustomNames;
    }

    public ISymbol Symbol { get; }
    public ITypeSymbol Type { get; }
    public string SerializedNameExpressionForRead { get; }
    public string SerializedNameExpressionForWrite { get; }
    public string AccessExpression { get; }
    public Func<string, string> AssignExpression { get; }
    public string IgnoreConditionExpression { get; }
    public string? AttributeConverterTypeName { get; }
    public string? ObjectCreationHandling { get; }
    public string? BlockSequenceMappingStyle { get; }
    public string? BlockSequenceSequenceStyle { get; }
    public bool IsRequired { get; }
    public bool IsInitOnly { get; }
    public bool IsRequiredKeyword { get; }
    public bool RequiresIncludeFields { get; }
    public bool DisallowNullOnSerialize { get; }
    public bool DisallowNullOnDeserialize { get; }
    public bool IsReadOnlyProperty { get; }
    public bool IsReadOnlyField { get; }
    public int? NumberHandling { get; }
    public List<(string Member, string Scalar)>? EnumCustomNames { get; }
    public bool NeedsObjectInitializer => IsInitOnly || IsRequiredKeyword;
}
