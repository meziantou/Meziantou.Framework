namespace Meziantou.Framework.Csv.Serialization;

internal sealed class CsvKebabCaseLowerNamingPolicy : CsvSeparatorNamingPolicy
{
    internal CsvKebabCaseLowerNamingPolicy()
        : base('-', upperCase: false)
    {
    }
}
