namespace Meziantou.Framework.Toml.Serialization;

/// <summary>Determines how unmapped TOML members are handled when deserializing the annotated type.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class TomlUnmappedMemberHandlingAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlUnmappedMemberHandlingAttribute"/> class.
    /// </summary>
    /// <param name="handling">The unmapped member handling to apply.</param>
    public TomlUnmappedMemberHandlingAttribute(TomlUnmappedMemberHandling handling)
    {
        Handling = handling;
    }

    /// <summary>Gets the unmapped member handling to apply.</summary>
    public TomlUnmappedMemberHandling Handling { get; }
}
