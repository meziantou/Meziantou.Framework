namespace Meziantou.Framework.Yaml;

/// <summary>
/// Specifies a built-in <see cref="YamlNamingPolicy"/> for use with the source generator.
/// </summary>
public enum YamlKnownNamingPolicy
{
    /// <summary>No naming policy is applied; CLR member names are used as-is.</summary>
    Unspecified = 0,

    /// <summary>
    /// camelCase. See <see cref="YamlNamingPolicy.CamelCase"/>.
    /// </summary>
    CamelCase = 1,

    /// <summary>
    /// snake_case (lowercase). See <see cref="YamlNamingPolicy.SnakeCaseLower"/>.
    /// </summary>
    SnakeCaseLower = 2,

    /// <summary>
    /// SNAKE_CASE (uppercase). See <see cref="YamlNamingPolicy.SnakeCaseUpper"/>.
    /// </summary>
    SnakeCaseUpper = 3,

    /// <summary>
    /// kebab-case (lowercase). See <see cref="YamlNamingPolicy.KebabCaseLower"/>.
    /// </summary>
    KebabCaseLower = 4,

    /// <summary>
    /// KEBAB-CASE (uppercase). See <see cref="YamlNamingPolicy.KebabCaseUpper"/>.
    /// </summary>
    KebabCaseUpper = 5,
}
