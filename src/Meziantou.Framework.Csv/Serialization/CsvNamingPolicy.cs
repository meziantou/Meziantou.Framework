namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Determines the naming policy used to convert a CLR member name to a CSV column name.</summary>
public abstract class CsvNamingPolicy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CsvNamingPolicy"/> class.
    /// </summary>
    protected CsvNamingPolicy()
    {
    }

    /// <summary>Gets the naming policy for camelCase.</summary>
    public static CsvNamingPolicy CamelCase { get; } = new CsvCamelCaseNamingPolicy();

    /// <summary>Gets the naming policy for snake_case (lowercase).</summary>
    public static CsvNamingPolicy SnakeCaseLower { get; } = new CsvSnakeCaseLowerNamingPolicy();

    /// <summary>Gets the naming policy for SNAKE_CASE (uppercase).</summary>
    public static CsvNamingPolicy SnakeCaseUpper { get; } = new CsvSnakeCaseUpperNamingPolicy();

    /// <summary>Gets the naming policy for kebab-case (lowercase).</summary>
    public static CsvNamingPolicy KebabCaseLower { get; } = new CsvKebabCaseLowerNamingPolicy();

    /// <summary>Gets the naming policy for KEBAB-CASE (uppercase).</summary>
    public static CsvNamingPolicy KebabCaseUpper { get; } = new CsvKebabCaseUpperNamingPolicy();

    /// <summary>Gets the naming policy for PascalCase.</summary>
    public static CsvNamingPolicy PascalCase { get; } = new CsvPascalCaseNamingPolicy();

    /// <summary>Gets the policy matching the specified <see cref="CsvKnownNamingPolicy"/> value.</summary>
    /// <param name="namingPolicy">The naming policy to resolve.</param>
    /// <returns>The matching policy, or <see langword="null"/> when no policy must be applied.</returns>
    internal static CsvNamingPolicy? GetPolicy(CsvKnownNamingPolicy namingPolicy)
    {
        return namingPolicy switch
        {
            CsvKnownNamingPolicy.CamelCase => CamelCase,
            CsvKnownNamingPolicy.SnakeCaseLower => SnakeCaseLower,
            CsvKnownNamingPolicy.SnakeCaseUpper => SnakeCaseUpper,
            CsvKnownNamingPolicy.KebabCaseLower => KebabCaseLower,
            CsvKnownNamingPolicy.KebabCaseUpper => KebabCaseUpper,
            CsvKnownNamingPolicy.PascalCase => PascalCase,
            _ => null,
        };
    }

    /// <summary>Converts the specified name according to the policy.</summary>
    /// <param name="name">The CLR member name.</param>
    /// <returns>The converted name.</returns>
    public abstract string ConvertName(string name);
}
