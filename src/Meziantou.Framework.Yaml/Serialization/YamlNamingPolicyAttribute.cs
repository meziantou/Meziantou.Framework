namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Specifies the naming policy used to convert the names of the annotated member or of all members of the annotated type.</summary>
/// <remarks>
/// When applied to a type, it sets the naming policy for all members declared on that type.
/// When applied to a member, it overrides the type-level and serializer-level naming policy for that member.
/// <see cref="YamlPropertyNameAttribute"/> takes precedence over this attribute.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
    AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false)]
public sealed class YamlNamingPolicyAttribute : YamlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlNamingPolicyAttribute"/> class.
    /// </summary>
    /// <param name="namingPolicy">The naming policy to apply. Use <see cref="YamlKnownNamingPolicy.Unspecified"/> to use the CLR names as-is.</param>
    public YamlNamingPolicyAttribute(YamlKnownNamingPolicy namingPolicy)
    {
        NamingPolicy = namingPolicy;
    }

    /// <summary>Gets the naming policy to apply.</summary>
    public YamlKnownNamingPolicy NamingPolicy { get; }
}
