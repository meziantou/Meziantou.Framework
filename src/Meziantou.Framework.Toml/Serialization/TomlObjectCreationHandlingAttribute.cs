namespace Meziantou.Framework.Toml.Serialization;

/// <summary>Determines how deserialization handles an existing instance for the annotated member or type.</summary>
/// <remarks>
/// When applied to a type, it sets the default handling for all members declared on that type. When applied to a
/// member, it overrides the type-level and serializer-level handling for that member.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class TomlObjectCreationHandlingAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlObjectCreationHandlingAttribute"/> class.
    /// </summary>
    /// <param name="handling">The object creation handling to apply.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="handling"/> is not a defined <see cref="TomlObjectCreationHandling"/>.</exception>
    public TomlObjectCreationHandlingAttribute(TomlObjectCreationHandling handling)
    {
        if (!Enum.IsDefined(handling))
        {
            throw new ArgumentOutOfRangeException(nameof(handling), handling, $"The value is not a defined {nameof(TomlObjectCreationHandling)}.");
        }

        Handling = handling;
    }

    /// <summary>Gets the object creation handling to apply.</summary>
    public TomlObjectCreationHandling Handling { get; }
}
