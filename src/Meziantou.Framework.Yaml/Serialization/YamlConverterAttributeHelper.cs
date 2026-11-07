namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Helpers shared by the type-level and member-level <see cref="YamlConverterAttribute"/> resolution paths.
/// </summary>
internal static class YamlConverterAttributeHelper
{
    /// <summary>
    /// Resolves the converter type declared by a <see cref="YamlConverterAttribute"/>. An open generic converter type
    /// is closed over the generic arguments of <paramref name="typeToConvert"/> when both have the same arity.
    /// </summary>
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2055",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2068",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public static Type ResolveConverterType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type converterType,
        Type typeToConvert)
    {
        if (!converterType.IsGenericTypeDefinition)
        {
            return converterType;
        }

        // An open generic converter is supported when the target type is a closed generic type with the same
        // total number of generic parameters (nested types include the parameters of their containing types).
        if (!typeToConvert.IsGenericType || typeToConvert.ContainsGenericParameters ||
            converterType.GetGenericArguments().Length != typeToConvert.GetGenericArguments().Length)
        {
            throw new NotSupportedException($"The open generic converter type '{converterType}' is not compatible with type '{typeToConvert}'. Ensure that the total number of generic type parameters on the converter matches the number on the target type.");
        }

        try
        {
            return converterType.MakeGenericType(typeToConvert.GetGenericArguments());
        }
        catch (ArgumentException exception)
        {
            throw new NotSupportedException($"The open generic converter type '{converterType}' cannot be constructed for type '{typeToConvert}' because the generic constraints are not satisfied.", exception);
        }
    }
}
