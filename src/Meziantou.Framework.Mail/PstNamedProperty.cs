namespace Meziantou.Framework.Mail;

/// <summary>The name of a named property: a property set and either a numeric identifier (LID) or a string.</summary>
public sealed class PstNamedProperty : IEquatable<PstNamedProperty>
{
    /// <summary>Initializes a new instance of the <see cref="PstNamedProperty"/> class with a numeric identifier.</summary>
    /// <param name="propertySet">The GUID of the property set, such as <see cref="PstPropertySets.Appointment"/>.</param>
    /// <param name="id">The numeric identifier (LID) of the property in the set.</param>
    public PstNamedProperty(Guid propertySet, int id)
    {
        PropertySet = propertySet;
        Id = id;
    }

    /// <summary>Initializes a new instance of the <see cref="PstNamedProperty"/> class with a name.</summary>
    /// <param name="propertySet">The GUID of the property set, such as <see cref="PstPropertySets.PublicStrings"/>.</param>
    /// <param name="name">The name of the property in the set.</param>
    public PstNamedProperty(Guid propertySet, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        PropertySet = propertySet;
        Name = name;
    }

    /// <summary>Gets the GUID of the property set.</summary>
    public Guid PropertySet { get; }

    /// <summary>Gets the numeric identifier (LID), or <see langword="null"/> when the property has a name.</summary>
    public int? Id { get; }

    /// <summary>Gets the name, or <see langword="null"/> when the property has a numeric identifier.</summary>
    public string? Name { get; }

    /// <inheritdoc />
    public bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] PstNamedProperty? other) => other is not null && PropertySet == other.PropertySet && Id == other.Id && string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? obj) => Equals(obj as PstNamedProperty);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(PropertySet, Id, Name is null ? 0 : StringComparer.Ordinal.GetHashCode(Name));

    /// <summary>Returns the property set and the identifier or the name.</summary>
    /// <returns>A string such as <c>{00062002-0000-0000-c000-000000000046}:0x820D</c>.</returns>
    public override string ToString() => Name is null ? $"{{{PropertySet}}}:0x{Id!.Value:X4}" : $"{{{PropertySet}}}:{Name}";
}
