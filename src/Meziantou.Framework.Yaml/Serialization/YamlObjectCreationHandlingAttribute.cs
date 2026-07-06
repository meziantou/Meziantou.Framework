namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Determines how deserialization handles an existing instance for the annotated member or type.</summary>
/// <remarks>
/// When applied to a type, it sets the default handling for all members declared on that type. When applied to a
/// member, it overrides the type-level and serializer-level handling for that member.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class YamlObjectCreationHandlingAttribute : YamlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlObjectCreationHandlingAttribute"/> class.
    /// </summary>
    /// <param name="handling">The object creation handling to apply.</param>
    public YamlObjectCreationHandlingAttribute(YamlObjectCreationHandling handling)
    {
        Handling = handling;
    }

    /// <summary>Gets the object creation handling to apply.</summary>
    public YamlObjectCreationHandling Handling { get; }
}
