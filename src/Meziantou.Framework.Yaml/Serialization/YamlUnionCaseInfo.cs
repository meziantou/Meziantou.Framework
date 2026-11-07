using System.Collections.ObjectModel;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Describes a single case of a C# union type to a <see cref="YamlTypeClassifierFactory"/>.</summary>
public sealed class YamlUnionCaseInfo
{
    private static readonly ReadOnlyCollection<YamlUnionCaseProperty> EmptyProperties = Array.AsReadOnly(Array.Empty<YamlUnionCaseProperty>());

    /// <summary>Initializes a new instance of the <see cref="YamlUnionCaseInfo"/> class.</summary>
    /// <param name="caseType">The declared case type.</param>
    /// <param name="shape">The YAML shape the case is represented by.</param>
    /// <param name="properties">The mapping keys declared by the case, or <see langword="null"/> when the case does not serialize as an object mapping.</param>
    /// <param name="disallowUnmappedProperties">Whether the case rejects mapping keys it does not declare.</param>
    /// <exception cref="ArgumentNullException"><paramref name="caseType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A property entry is <see langword="null"/>.</exception>
    public YamlUnionCaseInfo(
        Type caseType,
        YamlUnionCaseShape shape,
        IReadOnlyList<YamlUnionCaseProperty>? properties = null,
        bool disallowUnmappedProperties = false)
    {
        ArgumentNullException.ThrowIfNull(caseType);

        CaseType = caseType;
        Shape = shape;
        DisallowUnmappedProperties = disallowUnmappedProperties;

        if (properties is null)
        {
            Properties = EmptyProperties;
            return;
        }

        HasObjectProperties = true;
        var copy = new YamlUnionCaseProperty[properties.Count];
        for (var i = 0; i < properties.Count; i++)
        {
            copy[i] = properties[i] ?? throw new ArgumentException("Properties cannot contain null entries.", nameof(properties));
        }

        Properties = Array.AsReadOnly(copy);
    }

    /// <summary>Gets the declared case type.</summary>
    public Type CaseType { get; }

    /// <summary>Gets the YAML shape the case is represented by.</summary>
    public YamlUnionCaseShape Shape { get; }

    /// <summary>Gets the mapping keys declared by the case.</summary>
    public IReadOnlyList<YamlUnionCaseProperty> Properties { get; }

    /// <summary>
    /// Gets a value indicating whether the case serializes as an object mapping whose keys are known.
    /// A mapping case backed by a dictionary has no declared keys and therefore reports <see langword="false"/>.
    /// </summary>
    public bool HasObjectProperties { get; }

    /// <summary>Gets a value indicating whether the case rejects mapping keys it does not declare.</summary>
    public bool DisallowUnmappedProperties { get; }
}
