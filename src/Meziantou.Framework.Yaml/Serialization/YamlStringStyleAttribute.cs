namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Overrides the style used to emit the string values written by the attributed member.</summary>
/// <remarks>
/// Apply this attribute to a property or field whose value is a string or contains strings. The override is scoped
/// to the serialization of that member value and affects the strings encountered below it, which includes the items
/// of a collection and the values of a nested object. It does not affect mapping keys.
/// See <see cref="YamlScalarStylePreferences.StringStyle"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class YamlStringStyleAttribute : YamlAttribute
{
    /// <summary>Initializes a new instance of the <see cref="YamlStringStyleAttribute"/> class.</summary>
    /// <param name="style">The style to use for string values.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> is not a defined <see cref="ScalarStyle"/>.</exception>
    public YamlStringStyleAttribute(ScalarStyle style)
    {
        YamlSerializerOptions.ValidateScalarStyle(style, nameof(style));
        Style = style;
    }

    /// <summary>Gets the style to use for string values.</summary>
    public ScalarStyle Style { get; }
}
