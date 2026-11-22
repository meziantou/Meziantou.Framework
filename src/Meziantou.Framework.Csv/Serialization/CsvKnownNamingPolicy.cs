namespace Meziantou.Framework.Csv.Serialization;

/// <summary>
/// Specifies a built-in <see cref="CsvNamingPolicy"/> for use with the source generator.
/// </summary>
public enum CsvKnownNamingPolicy
{
    /// <summary>No naming policy is applied; CLR member names are used as-is.</summary>
    Unspecified = 0,

    /// <summary>
    /// camelCase. See <see cref="CsvNamingPolicy.CamelCase"/>.
    /// </summary>
    CamelCase = 1,

    /// <summary>
    /// snake_case (lowercase). See <see cref="CsvNamingPolicy.SnakeCaseLower"/>.
    /// </summary>
    SnakeCaseLower = 2,

    /// <summary>
    /// SNAKE_CASE (uppercase). See <see cref="CsvNamingPolicy.SnakeCaseUpper"/>.
    /// </summary>
    SnakeCaseUpper = 3,

    /// <summary>
    /// kebab-case (lowercase). See <see cref="CsvNamingPolicy.KebabCaseLower"/>.
    /// </summary>
    KebabCaseLower = 4,

    /// <summary>
    /// KEBAB-CASE (uppercase). See <see cref="CsvNamingPolicy.KebabCaseUpper"/>.
    /// </summary>
    KebabCaseUpper = 5,

    /// <summary>
    /// PascalCase. See <see cref="CsvNamingPolicy.PascalCase"/>.
    /// </summary>
    PascalCase = 6,
}
