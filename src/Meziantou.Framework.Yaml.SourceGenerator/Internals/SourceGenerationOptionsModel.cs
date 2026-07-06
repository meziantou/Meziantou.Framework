using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

internal sealed class SourceGenerationOptionsModel
{
    public bool? WriteIndented { get; set; }
    public int? IndentSize { get; set; }
    public bool? PropertyNameCaseInsensitive { get; set; }
    public bool? IncludeFields { get; set; }
    public bool? IgnoreReadOnlyFields { get; set; }
    public bool? IgnoreReadOnlyProperties { get; set; }
    public bool? RejectUnmatchedProperties { get; set; }
    public bool? RespectRequiredConstructorParameters { get; set; }
    public bool? RespectNullableAnnotations { get; set; }
    public string? DefaultIgnoreCondition { get; set; }
    public string? PropertyNamingPolicy { get; set; }
    public string? DictionaryKeyPolicy { get; set; }
    public string? MappingOrder { get; set; }
    public string? BlockSequenceMappingStyle { get; set; }
    public string? BlockSequenceSequenceStyle { get; set; }
    public string? Schema { get; set; }
    public bool? UseSchema { get; set; }
    public string? UnmappedMemberHandling { get; set; }
    public string? PreferredObjectCreationHandling { get; set; }
    public string? DuplicateKeyHandling { get; set; }
    public bool? UnsafeAllowDeserializeFromTagTypeName { get; set; }
    public string? ReferenceHandling { get; set; }
    public string? SourceName { get; set; }
    public bool? PreferPlainStyle { get; set; }
    public bool? PreferQuotedForAmbiguousScalars { get; set; }
    public string? DiscriminatorStyle { get; set; }
    public string? TypeDiscriminatorPropertyName { get; set; }
    public string? UnknownDerivedTypeHandling { get; set; }
    public ImmutableArray<ITypeSymbol> ConverterTypes { get; set; } = ImmutableArray<ITypeSymbol>.Empty;

    public void ApplyFrom(SourceGenerationOptionsModel other)
    {
        if (other.WriteIndented.HasValue) WriteIndented = other.WriteIndented;
        if (other.IndentSize.HasValue) IndentSize = other.IndentSize;
        if (other.PropertyNameCaseInsensitive.HasValue) PropertyNameCaseInsensitive = other.PropertyNameCaseInsensitive;
        if (other.IncludeFields.HasValue) IncludeFields = other.IncludeFields;
        if (other.IgnoreReadOnlyFields.HasValue) IgnoreReadOnlyFields = other.IgnoreReadOnlyFields;
        if (other.IgnoreReadOnlyProperties.HasValue) IgnoreReadOnlyProperties = other.IgnoreReadOnlyProperties;
        if (other.RejectUnmatchedProperties.HasValue) RejectUnmatchedProperties = other.RejectUnmatchedProperties;
        if (other.RespectRequiredConstructorParameters.HasValue) RespectRequiredConstructorParameters = other.RespectRequiredConstructorParameters;
        if (other.RespectNullableAnnotations.HasValue) RespectNullableAnnotations = other.RespectNullableAnnotations;
        if (!string.IsNullOrEmpty(other.DefaultIgnoreCondition)) DefaultIgnoreCondition = other.DefaultIgnoreCondition;
        if (!string.IsNullOrEmpty(other.PropertyNamingPolicy)) PropertyNamingPolicy = other.PropertyNamingPolicy;
        if (!string.IsNullOrEmpty(other.DictionaryKeyPolicy)) DictionaryKeyPolicy = other.DictionaryKeyPolicy;
        if (!string.IsNullOrEmpty(other.MappingOrder)) MappingOrder = other.MappingOrder;
        if (!string.IsNullOrEmpty(other.BlockSequenceMappingStyle)) BlockSequenceMappingStyle = other.BlockSequenceMappingStyle;
        if (!string.IsNullOrEmpty(other.BlockSequenceSequenceStyle)) BlockSequenceSequenceStyle = other.BlockSequenceSequenceStyle;
        if (!string.IsNullOrEmpty(other.Schema)) Schema = other.Schema;
        if (other.UseSchema.HasValue) UseSchema = other.UseSchema;
        if (!string.IsNullOrEmpty(other.UnmappedMemberHandling)) UnmappedMemberHandling = other.UnmappedMemberHandling;
        if (!string.IsNullOrEmpty(other.PreferredObjectCreationHandling)) PreferredObjectCreationHandling = other.PreferredObjectCreationHandling;
        if (!string.IsNullOrEmpty(other.DuplicateKeyHandling)) DuplicateKeyHandling = other.DuplicateKeyHandling;
        if (other.UnsafeAllowDeserializeFromTagTypeName.HasValue) UnsafeAllowDeserializeFromTagTypeName = other.UnsafeAllowDeserializeFromTagTypeName;
        if (!string.IsNullOrEmpty(other.ReferenceHandling)) ReferenceHandling = other.ReferenceHandling;
        if (other.SourceName is not null) SourceName = other.SourceName;
        if (other.PreferPlainStyle.HasValue) PreferPlainStyle = other.PreferPlainStyle;
        if (other.PreferQuotedForAmbiguousScalars.HasValue) PreferQuotedForAmbiguousScalars = other.PreferQuotedForAmbiguousScalars;
        if (!string.IsNullOrEmpty(other.DiscriminatorStyle)) DiscriminatorStyle = other.DiscriminatorStyle;
        if (other.TypeDiscriminatorPropertyName is not null) TypeDiscriminatorPropertyName = other.TypeDiscriminatorPropertyName;
        if (!string.IsNullOrEmpty(other.UnknownDerivedTypeHandling)) UnknownDerivedTypeHandling = other.UnknownDerivedTypeHandling;
        if (!other.ConverterTypes.IsDefaultOrEmpty) ConverterTypes = other.ConverterTypes;
    }
}
