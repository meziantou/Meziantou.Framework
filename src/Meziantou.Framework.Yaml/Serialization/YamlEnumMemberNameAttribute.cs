namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Specifies the YAML scalar used to represent an enum member.</summary>
/// <remarks>
/// This is the YAML equivalent of <c>System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute</c>.
/// Apply it to an enum field to override the name emitted and accepted for that value.
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class YamlEnumMemberNameAttribute : YamlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlEnumMemberNameAttribute"/> class.
    /// </summary>
    /// <param name="name">The YAML scalar used to represent the enum member.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public YamlEnumMemberNameAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>Gets the YAML scalar used to represent the enum member.</summary>
    public string Name { get; }
}
