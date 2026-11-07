namespace Meziantou.Framework.Toml.Serialization;

/// <summary>Determines how unmapped TOML members are handled when deserializing the annotated type.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class TomlUnmappedMemberHandlingAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlUnmappedMemberHandlingAttribute"/> class.
    /// </summary>
    /// <param name="handling">The unmapped member handling to apply.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="handling"/> is not a defined <see cref="TomlUnmappedMemberHandling"/>.</exception>
    public TomlUnmappedMemberHandlingAttribute(TomlUnmappedMemberHandling handling)
    {
        if (!Enum.IsDefined(handling))
        {
            throw new ArgumentOutOfRangeException(nameof(handling), handling, $"The value is not a defined {nameof(TomlUnmappedMemberHandling)}.");
        }

        Handling = handling;
    }

    /// <summary>Gets the unmapped member handling to apply.</summary>
    public TomlUnmappedMemberHandling Handling { get; }
}
