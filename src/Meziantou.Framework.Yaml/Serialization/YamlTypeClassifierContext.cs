using System.Collections.ObjectModel;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Describes the type a <see cref="YamlTypeClassifierFactory"/> is asked to classify.</summary>
public sealed class YamlTypeClassifierContext
{
    private static readonly ReadOnlyCollection<YamlUnionCaseInfo> EmptyUnionCases = Array.AsReadOnly(Array.Empty<YamlUnionCaseInfo>());
    private static readonly ReadOnlyCollection<YamlDerivedType> EmptyDerivedTypes = Array.AsReadOnly(Array.Empty<YamlDerivedType>());

    private YamlTypeClassifierContext(
        Type declaringType,
        YamlTypeClassifierKind kind,
        ReadOnlyCollection<YamlUnionCaseInfo> unionCases,
        ReadOnlyCollection<YamlDerivedType> derivedTypes,
        string? typeDiscriminatorPropertyName)
    {
        DeclaringType = declaringType;
        Kind = kind;
        UnionCases = unionCases;
        DerivedTypes = derivedTypes;
        TypeDiscriminatorPropertyName = typeDiscriminatorPropertyName;
    }

    /// <summary>Creates a context describing a C# union type.</summary>
    /// <param name="declaringType">The union type.</param>
    /// <param name="unionCases">The cases declared by <paramref name="declaringType"/>.</param>
    /// <returns>The context to create a classifier from.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="declaringType"/> or <paramref name="unionCases"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A case entry is <see langword="null"/>.</exception>
    public static YamlTypeClassifierContext CreateForUnion(Type declaringType, IReadOnlyList<YamlUnionCaseInfo> unionCases)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(unionCases);

        return new(declaringType, YamlTypeClassifierKind.Union, Copy(unionCases, nameof(unionCases)), EmptyDerivedTypes, typeDiscriminatorPropertyName: null);
    }

    /// <summary>Creates a context describing a polymorphic type.</summary>
    /// <param name="declaringType">The base type.</param>
    /// <param name="derivedTypes">The types derived from <paramref name="declaringType"/>.</param>
    /// <param name="typeDiscriminatorPropertyName">The mapping key carrying the type discriminator, when the type uses one.</param>
    /// <returns>The context to create a classifier from.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="declaringType"/> or <paramref name="derivedTypes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A derived type entry is <see langword="null"/>.</exception>
    public static YamlTypeClassifierContext CreateForPolymorphicType(Type declaringType, IReadOnlyList<YamlDerivedType> derivedTypes, string? typeDiscriminatorPropertyName)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(derivedTypes);

        return new(declaringType, YamlTypeClassifierKind.PolymorphicType, EmptyUnionCases, Copy(derivedTypes, nameof(derivedTypes)), typeDiscriminatorPropertyName);
    }

    private static ReadOnlyCollection<T> Copy<T>(IReadOnlyList<T> values, string paramName)
        where T : class
    {
        if (values.Count == 0)
        {
            return Array.AsReadOnly(Array.Empty<T>());
        }

        var copy = new T[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            copy[i] = values[i] ?? throw new ArgumentException("The collection cannot contain null entries.", paramName);
        }

        return Array.AsReadOnly(copy);
    }

    /// <summary>Gets the type being classified.</summary>
    public Type DeclaringType { get; }

    /// <summary>Gets the kind of the type being classified.</summary>
    public YamlTypeClassifierKind Kind { get; }

    /// <summary>Gets the cases declared by <see cref="DeclaringType"/>, when it is a union type.</summary>
    public ReadOnlyCollection<YamlUnionCaseInfo> UnionCases { get; }

    /// <summary>Gets the types derived from <see cref="DeclaringType"/>, when it is a polymorphic type.</summary>
    public ReadOnlyCollection<YamlDerivedType> DerivedTypes { get; }

    /// <summary>Gets the mapping key carrying the type discriminator, when <see cref="DeclaringType"/> uses one.</summary>
    public string? TypeDiscriminatorPropertyName { get; }
}
