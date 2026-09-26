namespace Meziantou.Framework.Toml;

/// <summary>Determines the naming policy used to convert a CLR member name to a TOML key.</summary>
#if MEZIANTOU_FRAMEWORK_TOML_SOURCE_GENERATOR
internal
#else
public
#endif
abstract class TomlNamingPolicy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlNamingPolicy"/> class.
    /// </summary>
    protected TomlNamingPolicy()
    {
    }

    /// <summary>Gets the naming policy for camelCase.</summary>
    public static TomlNamingPolicy CamelCase { get; } = new TomlCamelCaseNamingPolicy();

    /// <summary>Gets the naming policy for snake_case (lowercase).</summary>
    public static TomlNamingPolicy SnakeCaseLower { get; } = new TomlSnakeCaseLowerNamingPolicy();

    /// <summary>Gets the naming policy for SNAKE_CASE (uppercase).</summary>
    public static TomlNamingPolicy SnakeCaseUpper { get; } = new TomlSnakeCaseUpperNamingPolicy();

    /// <summary>Gets the naming policy for kebab-case (lowercase).</summary>
    public static TomlNamingPolicy KebabCaseLower { get; } = new TomlKebabCaseLowerNamingPolicy();

    /// <summary>Gets the naming policy for KEBAB-CASE (uppercase).</summary>
    public static TomlNamingPolicy KebabCaseUpper { get; } = new TomlKebabCaseUpperNamingPolicy();

    /// <summary>Gets the naming policy for PascalCase.</summary>
    public static TomlNamingPolicy PascalCase { get; } = new TomlPascalCaseNamingPolicy();

    /// <summary>Gets the policy matching the specified <see cref="TomlKnownNamingPolicy"/> value.</summary>
    /// <param name="namingPolicy">The naming policy to resolve.</param>
    /// <returns>The matching policy, or <see langword="null"/> when no policy must be applied.</returns>
    internal static TomlNamingPolicy? GetPolicy(TomlKnownNamingPolicy namingPolicy)
    {
        return namingPolicy switch
        {
            TomlKnownNamingPolicy.CamelCase => CamelCase,
            TomlKnownNamingPolicy.SnakeCaseLower => SnakeCaseLower,
            TomlKnownNamingPolicy.SnakeCaseUpper => SnakeCaseUpper,
            TomlKnownNamingPolicy.KebabCaseLower => KebabCaseLower,
            TomlKnownNamingPolicy.KebabCaseUpper => KebabCaseUpper,
            TomlKnownNamingPolicy.PascalCase => PascalCase,
            _ => null,
        };
    }

    /// <summary>Converts the specified name according to the policy.</summary>
    /// <param name="name">The CLR member name.</param>
    /// <returns>The converted name.</returns>
    public abstract string ConvertName(string name);
}
