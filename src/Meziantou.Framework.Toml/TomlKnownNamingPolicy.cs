namespace Meziantou.Framework.Toml;

/// <summary>
/// Specifies a built-in <see cref="TomlNamingPolicy"/> for use with the source generator.
/// </summary>
#if MEZIANTOU_FRAMEWORK_TOML_SOURCE_GENERATOR
internal
#else
public
#endif
enum TomlKnownNamingPolicy
{
    /// <summary>No naming policy is applied; CLR member names are used as-is.</summary>
    Unspecified = 0,

    /// <summary>
    /// camelCase. See <see cref="TomlNamingPolicy.CamelCase"/>.
    /// </summary>
    CamelCase = 1,

    /// <summary>
    /// snake_case (lowercase). See <see cref="TomlNamingPolicy.SnakeCaseLower"/>.
    /// </summary>
    SnakeCaseLower = 2,

    /// <summary>
    /// SNAKE_CASE (uppercase). See <see cref="TomlNamingPolicy.SnakeCaseUpper"/>.
    /// </summary>
    SnakeCaseUpper = 3,

    /// <summary>
    /// kebab-case (lowercase). See <see cref="TomlNamingPolicy.KebabCaseLower"/>.
    /// </summary>
    KebabCaseLower = 4,

    /// <summary>
    /// KEBAB-CASE (uppercase). See <see cref="TomlNamingPolicy.KebabCaseUpper"/>.
    /// </summary>
    KebabCaseUpper = 5,

    /// <summary>
    /// PascalCase. See <see cref="TomlNamingPolicy.PascalCase"/>.
    /// </summary>
    PascalCase = 6,
}
