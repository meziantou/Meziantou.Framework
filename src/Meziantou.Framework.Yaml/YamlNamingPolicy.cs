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

    /// <summary>Converts the specified name according to the policy.</summary>
    /// <param name="name">The CLR member name.</param>
    /// <returns>The converted name.</returns>
    public abstract string ConvertName(string name);
}
