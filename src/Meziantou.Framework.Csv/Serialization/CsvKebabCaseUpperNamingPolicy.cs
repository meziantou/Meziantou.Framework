namespace Meziantou.Framework.Csv.Serialization;

internal sealed class CsvKebabCaseUpperNamingPolicy : CsvSeparatorNamingPolicy
{
    internal CsvKebabCaseUpperNamingPolicy()
        : base('-', upperCase: true)
    {
    }
}
