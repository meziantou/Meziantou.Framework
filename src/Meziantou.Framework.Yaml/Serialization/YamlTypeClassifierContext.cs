using System.Collections.ObjectModel;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Describes the type a <see cref="YamlTypeClassifierFactory"/> is asked to classify.</summary>
public sealed class YamlTypeClassifierContext
{
    /// <summary>Initializes a new instance of the <see cref="YamlTypeClassifierContext"/> class.</summary>
    /// <param name="declaringType">The type being classified.</param>
    /// <param name="kind">The kind of the type being classified.</param>
    /// <param name="unionCases">The cases declared by <paramref name="declaringType"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="declaringType"/> or <paramref name="unionCases"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A case entry is <see langword="null"/>.</exception>
    public YamlTypeClassifierContext(Type declaringType, YamlTypeClassifierKind kind, IReadOnlyList<YamlUnionCaseInfo> unionCases)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(unionCases);

        DeclaringType = declaringType;
        Kind = kind;

        var copy = new YamlUnionCaseInfo[unionCases.Count];
        for (var i = 0; i < unionCases.Count; i++)
        {
            copy[i] = unionCases[i] ?? throw new ArgumentException("Union cases cannot contain null entries.", nameof(unionCases));
        }

        UnionCases = Array.AsReadOnly(copy);
    }

    /// <summary>Gets the type being classified.</summary>
    public Type DeclaringType { get; }

    /// <summary>Gets the kind of the type being classified.</summary>
    public YamlTypeClassifierKind Kind { get; }

    /// <summary>Gets the cases declared by <see cref="DeclaringType"/>.</summary>
    public ReadOnlyCollection<YamlUnionCaseInfo> UnionCases { get; }
}
