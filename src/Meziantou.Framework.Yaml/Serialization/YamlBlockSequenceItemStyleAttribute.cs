namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Overrides how nested block collections are emitted for items in block sequences written by the attributed member.</summary>
/// <remarks>
/// Apply this attribute to a property or field whose value may contain block sequences. The override is scoped to the
/// serialization of that member value and affects nested mappings and sequences encountered below it.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class YamlBlockSequenceItemStyleAttribute : YamlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlBlockSequenceItemStyleAttribute"/> class.
    /// </summary>
    public YamlBlockSequenceItemStyleAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlBlockSequenceItemStyleAttribute"/> class with a mapping style override.
    /// </summary>
    /// <param name="mappingStyle">The style to use for mappings that are items in block sequences.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mappingStyle"/> is not a defined <see cref="YamlSequenceItemStyle"/>.</exception>
    public YamlBlockSequenceItemStyleAttribute(YamlSequenceItemStyle mappingStyle)
    {
        MappingStyle = mappingStyle;
    }

    /// <summary>Gets or sets the style to use for mappings that are items in block sequences.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is not a defined <see cref="YamlSequenceItemStyle"/>.</exception>
    public YamlSequenceItemStyle MappingStyle
    {
        get;
#pragma warning disable CA1019 // Define accessors for attribute arguments
        set
#pragma warning restore CA1019
        {
            YamlSerializerOptions.ValidateSequenceItemStyle(value, nameof(value));
            field = value;
        }
    }

    /// <summary>Gets or sets the style to use for sequences that are items in block sequences.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is not a defined <see cref="YamlSequenceItemStyle"/>.</exception>
    public YamlSequenceItemStyle SequenceStyle
    {
        get;
        set
        {
            YamlSerializerOptions.ValidateSequenceItemStyle(value, nameof(value));
            field = value;
        }
    }
}
