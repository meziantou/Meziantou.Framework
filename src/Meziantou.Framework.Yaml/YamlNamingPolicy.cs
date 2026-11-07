namespace Meziantou.Framework.Yaml;

/// <summary>Determines the naming policy used to convert a CLR member name to a YAML key.</summary>
#if MEZIANTOU_FRAMEWORK_YAML_SOURCE_GENERATOR
internal
#else
public
#endif
abstract class YamlNamingPolicy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlNamingPolicy"/> class.
    /// </summary>
    protected YamlNamingPolicy()
    {
    }

    /// <summary>Gets the naming policy for camelCase.</summary>
    public static YamlNamingPolicy CamelCase { get; } = new YamlCamelCaseNamingPolicy();

    /// <summary>Gets the naming policy for snake_case (lowercase).</summary>
    public static YamlNamingPolicy SnakeCaseLower { get; } = new YamlSnakeCaseLowerNamingPolicy();

    /// <summary>Gets the naming policy for SNAKE_CASE (uppercase).</summary>
    public static YamlNamingPolicy SnakeCaseUpper { get; } = new YamlSnakeCaseUpperNamingPolicy();

    /// <summary>Gets the naming policy for kebab-case (lowercase).</summary>
    public static YamlNamingPolicy KebabCaseLower { get; } = new YamlKebabCaseLowerNamingPolicy();

    /// <summary>Gets the naming policy for KEBAB-CASE (uppercase).</summary>
    public static YamlNamingPolicy KebabCaseUpper { get; } = new YamlKebabCaseUpperNamingPolicy();

    /// <summary>Gets the naming policy for PascalCase.</summary>
    public static YamlNamingPolicy PascalCase { get; } = new YamlPascalCaseNamingPolicy();

    /// <summary>Gets the policy matching the specified <see cref="YamlKnownNamingPolicy"/> value.</summary>
    /// <param name="namingPolicy">The naming policy to resolve.</param>
    /// <returns>The matching policy, or <see langword="null"/> when no policy must be applied.</returns>
    internal static YamlNamingPolicy? GetPolicy(YamlKnownNamingPolicy namingPolicy)
    {
        return namingPolicy switch
        {
            YamlKnownNamingPolicy.CamelCase => CamelCase,
            YamlKnownNamingPolicy.SnakeCaseLower => SnakeCaseLower,
            YamlKnownNamingPolicy.SnakeCaseUpper => SnakeCaseUpper,
            YamlKnownNamingPolicy.KebabCaseLower => KebabCaseLower,
            YamlKnownNamingPolicy.KebabCaseUpper => KebabCaseUpper,
            YamlKnownNamingPolicy.PascalCase => PascalCase,
            _ => null,
        };
    }

    /// <summary>Converts the specified name according to the policy.</summary>
    /// <param name="name">The CLR member name.</param>
    /// <returns>The converted name.</returns>
    public abstract string ConvertName(string name);
}
