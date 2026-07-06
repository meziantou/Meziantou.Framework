namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Specifies how numbers are handled when serializing or deserializing the annotated member or type.</summary>
/// <remarks>
/// This is the YAML equivalent of <see cref="System.Text.Json.Serialization.JsonNumberHandlingAttribute"/>.
/// When applied to a type, it sets the default handling for all numeric members declared on that type.
/// When applied to a member, it overrides the type-level and serializer-level handling for that member.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class YamlNumberHandlingAttribute : YamlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlNumberHandlingAttribute"/> class.
    /// </summary>
    /// <param name="handling">The number handling to apply.</param>
    public YamlNumberHandlingAttribute(YamlNumberHandling handling)
    {
        Handling = handling;
    }

    /// <summary>Gets the number handling to apply.</summary>
    public YamlNumberHandling Handling { get; }
}
