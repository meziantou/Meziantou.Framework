namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Determines how unmapped YAML members are handled when deserializing the annotated type.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class YamlUnmappedMemberHandlingAttribute : YamlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlUnmappedMemberHandlingAttribute"/> class.
    /// </summary>
    /// <param name="handling">The unmapped member handling to apply.</param>
    public YamlUnmappedMemberHandlingAttribute(YamlUnmappedMemberHandling handling)
    {
        Handling = handling;
    }

    /// <summary>Gets the unmapped member handling to apply.</summary>
    public YamlUnmappedMemberHandling Handling { get; }
}
